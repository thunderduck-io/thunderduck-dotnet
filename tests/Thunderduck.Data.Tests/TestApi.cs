using System.Text.Json;
using Thunderduck.Client;
using Thunderduck.Data;

static partial class TestApi
{
    /// <summary>auth ok, submit -> e1, success, and the given rows served 2 per page.
    /// FakeApi matches path prefixes in registration order, so the result routes
    /// are registered before the bare /executions/e1 route that would shadow them.</summary>
    public static FakeApi Query(string[] columns, string[] types, params object?[][] rows)
    {
        var api = new FakeApi();
        for (var off = 0; off < Math.Max(rows.Length, 1); off += 2)
        {
            var page = rows.Skip(off).Take(2).ToArray();
            var json = JsonSerializer.Serialize(new { columns, column_types = types, rows = page, total = rows.Length });
            api.On("GET", $"/executions/e1/result?limit=2&offset={off}&", (200, json));
        }
        return api
            .On("GET", "/auth/me", (200, "{}"))
            .On("POST", "/queries", (201, "{\"id\":\"e1\"}"))
            .On("GET", "/executions/e1", (200, "{\"uuid\":\"e1\",\"status\":\"success\"}"));
    }

    public static ThunderduckClient Client(FakeApi api) => new(new ThunderduckClientOptions
    {
        BaseUri = new Uri("https://api.example"), Token = "tdk_secret",
        PollInterval = TimeSpan.FromMilliseconds(5), Timeout = TimeSpan.FromSeconds(5),
        HttpClient = new HttpClient(api),
    });

    public static ThunderduckDataReader Reader(FakeApi api) =>
        ThunderduckDataReader.CreateAsync(Client(api), "e1", 2, CancellationToken.None).GetAwaiter().GetResult();
}
