using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Thunderduck.Client;

/// <summary>Async client for thunderduck's REST query API.</summary>
public sealed class ThunderduckClient : IDisposable
{
    private static readonly HttpClient SharedHttp = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    })
    {
        // Deadlines come from the per-call tokens (query Timeout, caller ct);
        // the 100 s default would surface as a bare TaskCanceledException.
        Timeout = Timeout.InfiniteTimeSpan,
    };

    private readonly HttpClient _http;

    /// <summary>Create a client.</summary>
    public ThunderduckClient(ThunderduckClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.Token))
            throw new ArgumentException("Token is required.", nameof(options));
        Options = options;
        _http = options.HttpClient ?? SharedHttp;
    }

    internal ThunderduckClientOptions Options { get; }

    /// <summary>Check the token (GET /auth/me).</summary>
    public async Task ValidateAsync(CancellationToken ct = default)
    {
        using var r = await SendAsync(HttpMethod.Get, "/auth/me", null, ct).ConfigureAwait(false);
        await EnsureOkAsync(r, null, ct).ConfigureAwait(false);
    }

    /// <summary>Submit SQL; returns the execution id.</summary>
    public async Task<string> SubmitAsync(string sql, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sql);
        var body = JsonSerializer.Serialize(new Dictionary<string, string> { ["sql"] = sql });
        using var r = await SendAsync(HttpMethod.Post, "/queries", body, ct).ConfigureAwait(false);
        await EnsureOkAsync(r, null, ct).ConfigureAwait(false);
        using var doc = await ReadJsonAsync(r, ct).ConfigureAwait(false);
        return doc.RootElement.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
            ? id.GetString()!
            : throw new ThunderduckException(ThunderduckErrorKind.Http, "Submit response had no execution id.", (int)r.StatusCode);
    }

    /// <summary>The execution's state, or null while it is not visible yet (404).</summary>
    public async Task<Execution?> GetExecutionAsync(string id, CancellationToken ct = default)
    {
        using var r = await SendAsync(HttpMethod.Get, $"/executions/{Uri.EscapeDataString(id)}", null, ct).ConfigureAwait(false);
        if (r.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureOkAsync(r, id, ct).ConfigureAwait(false);
        using var doc = await ReadJsonAsync(r, ct).ConfigureAwait(false);
        var root = doc.RootElement;
        return new Execution(
            Str(root, "uuid") ?? id,
            Str(root, "status") ?? "running",
            Str(root, "error_message"),
            root.TryGetProperty("result_row_count", out var rc) && rc.ValueKind == JsonValueKind.Number ? rc.GetInt64() : null);
    }

    /// <summary>Poll until the execution succeeds. Cancellation or timeout stops waiting
    /// and cancels the query on the server (best effort).</summary>
    public async Task<Execution> WaitAsync(string id, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        using var deadline = new CancellationTokenSource(timeout ?? Options.Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
        try
        {
            while (true)
            {
                Execution? e;
                try
                {
                    e = await GetExecutionAsync(id, linked.Token).ConfigureAwait(false);
                }
                catch (ThunderduckException ex) when (IsTransient(ex))
                {
                    // A proxy 5xx or a network blip must not abandon a long wait (and
                    // leave the query running): keep polling until the deadline.
                    await Task.Delay(Options.PollInterval, linked.Token).ConfigureAwait(false);
                    continue;
                }
                switch (e?.Status.ToLowerInvariant())
                {
                    case "success":
                        return e;
                    case "error":
                        throw new ThunderduckException(ThunderduckErrorKind.QueryFailed,
                            e.ErrorMessage ?? "The query failed.", null, id);
                    case "cancelled":
                        throw new ThunderduckException(ThunderduckErrorKind.Cancelled, "The query was cancelled.", null, id);
                }
                await Task.Delay(Options.PollInterval, linked.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            await CancelQuietlyAsync(id).ConfigureAwait(false);
            if (ct.IsCancellationRequested) throw;
            throw new ThunderduckException(ThunderduckErrorKind.Timeout,
                $"The query did not finish within {(timeout ?? Options.Timeout).TotalSeconds:0.#} s and was cancelled.", null, id);
        }
    }

    /// <summary>Cancel an execution. A finished or unknown run is not an error;
    /// a rejected token is.</summary>
    public async Task CancelAsync(string id, CancellationToken ct = default)
    {
        using var r = await SendAsync(HttpMethod.Post, $"/executions/{Uri.EscapeDataString(id)}/cancel", null, ct).ConfigureAwait(false);
        if (r.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Conflict) return;
        await EnsureOkAsync(r, id, ct).ConfigureAwait(false);
    }

    /// <summary>One page of the stored result; null when the execution stored none (non-SELECT).</summary>
    public async Task<ResultPage?> GetPageAsync(string id, int limit, long offset, CancellationToken ct = default)
    {
        var path = string.Create(CultureInfo.InvariantCulture,
            $"/executions/{Uri.EscapeDataString(id)}/result?limit={limit}&offset={offset}&exact_decimals=true");
        while (true)
        {
            using var r = await SendAsync(HttpMethod.Get, path, null, ct).ConfigureAwait(false);
            if (r.StatusCode == HttpStatusCode.Accepted)
            {
                await Task.Delay(Options.PollInterval, ct).ConfigureAwait(false);
                continue;
            }
            if (r.StatusCode == HttpStatusCode.NotFound) return null;
            if (r.StatusCode == HttpStatusCode.Conflict)
                throw new ThunderduckException(ThunderduckErrorKind.ResultExpired,
                    await DetailAsync(r, ct).ConfigureAwait(false), 409, id);
            await EnsureOkAsync(r, id, ct).ConfigureAwait(false);
            using var doc = await ReadJsonAsync(r, ct).ConfigureAwait(false);
            var root = doc.RootElement;
            var columns = Strings(root, "columns");
            var types = Strings(root, "column_types");
            var rows = new List<JsonElement[]>();
            if (root.TryGetProperty("rows", out var rs) && rs.ValueKind == JsonValueKind.Array)
                foreach (var row in rs.EnumerateArray())
                    rows.Add(row.EnumerateArray().Select(c => c.Clone()).ToArray());
            var total = root.TryGetProperty("total", out var t) && t.ValueKind == JsonValueKind.Number ? t.GetInt64() : rows.Count;
            return new ResultPage(columns, types.Count == columns.Count ? types : columns.Select(_ => "").ToList(), rows, total, offset);
        }
    }

    /// <summary>All pages of an execution's result, in order (empty for a non-SELECT).
    /// Advances by the rows actually returned, so a server-side page cap is followed.</summary>
    public async IAsyncEnumerable<ResultPage> ReadPagesAsync(string id, int pageSize = 1000,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        long offset = 0;
        while (true)
        {
            var page = await GetPageAsync(id, pageSize, offset, ct).ConfigureAwait(false);
            if (page is null) yield break;
            yield return page;
            offset += page.Rows.Count;
            if (page.Rows.Count == 0 || offset >= page.Total) yield break;
        }
    }

    /// <summary>The company's catalogs, schemas, tables and columns.</summary>
    public async Task<CatalogTree> GetCatalogAsync(CancellationToken ct = default)
    {
        using var r = await SendAsync(HttpMethod.Get, "/catalog/schema", null, ct).ConfigureAwait(false);
        await EnsureOkAsync(r, null, ct).ConfigureAwait(false);
        using var doc = await ReadJsonAsync(r, ct).ConfigureAwait(false);
        var catalogs = new List<CatalogInfo>();
        if (doc.RootElement.TryGetProperty("catalogs", out var cs) && cs.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in cs.EnumerateArray())
            {
                var schemas = new List<SchemaInfo>();
                if (c.TryGetProperty("tables", out var tabs) && tabs.ValueKind == JsonValueKind.Array)
                    schemas.Add(new SchemaInfo(null, Tables(tabs)));
                if (c.TryGetProperty("namespaces", out var nss) && nss.ValueKind == JsonValueKind.Array)
                    foreach (var ns in nss.EnumerateArray())
                        schemas.Add(new SchemaInfo(Str(ns, "name"),
                            ns.TryGetProperty("tables", out var nt) && nt.ValueKind == JsonValueKind.Array ? Tables(nt) : []));
                catalogs.Add(new CatalogInfo(Str(c, "name") ?? "", Str(c, "type"), schemas, Str(c, "error")));
            }
        }
        return new CatalogTree(catalogs);
    }

    /// <inheritdoc/>
    public void Dispose() { /* the HttpClient is shared or caller-owned */ }

    // ---- helpers ----

    private static bool IsTransient(ThunderduckException ex) =>
        ex.Kind == ThunderduckErrorKind.Http && (ex.HttpStatus is null or >= 500);

    private async Task CancelQuietlyAsync(string id)
    {
        try { await CancelAsync(id, CancellationToken.None).ConfigureAwait(false); }
        catch (Exception) { /* best effort: the caller's cancellation/timeout is what surfaces */ }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? json, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, new Uri(Options.BaseUri, path));
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Options.Token);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (json is not null) req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        try
        {
            return await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new ThunderduckException(ThunderduckErrorKind.Http, $"Could not reach {Options.BaseUri.Host}: {ex.Message}", null, null, ex);
        }
    }

    private static async Task EnsureOkAsync(HttpResponseMessage r, string? id, CancellationToken ct)
    {
        if (r.IsSuccessStatusCode) return;
        var detail = await DetailAsync(r, ct).ConfigureAwait(false);
        var status = (int)r.StatusCode;
        var kind = status is 401 or 403 ? ThunderduckErrorKind.Authentication : ThunderduckErrorKind.Http;
        throw new ThunderduckException(kind, $"HTTP {status}: {detail}", status, id);
    }

    private static async Task<string> DetailAsync(HttpResponseMessage r, CancellationToken ct)
    {
        var text = await r.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("detail", out var d))
                return d.ValueKind == JsonValueKind.String ? d.GetString()! : d.GetRawText();
        }
        catch (JsonException) { }
        return text.Length > 300 ? text[..300] : text;
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage r, CancellationToken ct)
    {
        await using var s = await r.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(s, cancellationToken: ct).ConfigureAwait(false);
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static List<string> Strings(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString()! : x.GetRawText()).ToList()
            : new List<string>();

    private static List<TableInfo> Tables(JsonElement arr) => arr.EnumerateArray()
        .Where(t => Str(t, "name") is not null)
        .Select(t => new TableInfo(Str(t, "name")!, (Str(t, "kind") ?? "table").ToLowerInvariant(),
            t.TryGetProperty("columns", out var cols) && cols.ValueKind == JsonValueKind.Array
                ? cols.EnumerateArray().Where(c => Str(c, "name") is not null)
                    .Select(c => new ColumnInfo(Str(c, "name")!, Str(c, "type") ?? "")).ToList()
                : []))
        .ToList();
}
