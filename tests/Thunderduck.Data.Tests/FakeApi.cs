using System.Net;
using System.Text;

/// <summary>Scripted HTTP API: routes match method + path prefix; each route
/// replays its responses in order and repeats the last one. Status 599
/// simulates a network failure (HttpRequestException).</summary>
public sealed class FakeApi : HttpMessageHandler
{
    private readonly List<(string Method, string Prefix, Queue<(int, string)> Responses, (int, string) Last)> _routes = new();

    public List<(string Method, string PathAndQuery, string? Body, string? Auth)> Requests { get; } = new();

    public FakeApi On(string method, string pathPrefix, params (int Status, string Json)[] responses)
    {
        _routes.Add((method, pathPrefix, new Queue<(int, string)>(responses), responses[^1]));
        return this;
    }

    public Func<Task>? BeforeEachResponse { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        var path = request.RequestUri!.PathAndQuery;
        lock (Requests) Requests.Add((request.Method.Method, path, body, request.Headers.Authorization?.ToString()));
        if (BeforeEachResponse is not null) await BeforeEachResponse();
        foreach (var r in _routes)
        {
            if (r.Method == request.Method.Method && path.StartsWith(r.Prefix, StringComparison.Ordinal))
            {
                (int status, string json) = r.Last;
                lock (r.Responses) if (r.Responses.Count > 0) (status, json) = r.Responses.Dequeue();
                if (status == 599) throw new HttpRequestException("simulated network failure");
                return new HttpResponseMessage((HttpStatusCode)status)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                };
            }
        }
        return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{\"detail\":\"no fake route\"}") };
    }
}
