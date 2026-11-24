using Thunderduck.Client;
using Xunit;

public class ClientTests
{
    static (ThunderduckClient, FakeApi) Make(FakeApi? api = null)
    {
        api ??= new FakeApi();
        var c = new ThunderduckClient(new ThunderduckClientOptions
        {
            BaseUri = new Uri("https://api.example"), Token = "tdk_secret",
            PollInterval = TimeSpan.FromMilliseconds(5), Timeout = TimeSpan.FromSeconds(5),
            HttpClient = new HttpClient(api),
        });
        return (c, api);
    }

    [Fact]
    public async Task Submit_posts_sql_with_bearer_token_and_returns_id()
    {
        var (c, api) = Make(new FakeApi().On("POST", "/queries", (201, "{\"id\":\"e1\"}")));
        Assert.Equal("e1", await c.SubmitAsync("select 1"));
        var r = Assert.Single(api.Requests);
        Assert.Equal("POST", r.Method);
        Assert.Equal("{\"sql\":\"select 1\"}", r.Body);
        Assert.Equal("Bearer tdk_secret", r.Auth);
    }

    [Fact]
    public async Task Wait_polls_through_404_and_running_until_success()
    {
        var (c, api) = Make(new FakeApi().On("GET", "/executions/e1",
            (404, "{}"), (200, "{\"uuid\":\"e1\",\"status\":\"running\"}"),
            (200, "{\"uuid\":\"e1\",\"status\":\"success\",\"result_row_count\":3}")));
        var e = await c.WaitAsync("e1");
        Assert.Equal("success", e.Status);
        Assert.Equal(3, e.ResultRowCount);
        Assert.Equal(3, api.Requests.Count);
    }

    [Fact]
    public async Task Wait_retries_transient_5xx_and_network_errors_while_polling()
    {
        var api = new FakeApi().On("GET", "/executions/e1",
            (502, "{\"detail\":\"bad gateway\"}"), (503, "{}"), (599, "network"),
            (200, "{\"uuid\":\"e1\",\"status\":\"success\"}"));
        var (c, _) = Make(api);
        Assert.Equal("success", (await c.WaitAsync("e1")).Status);
    }

    [Fact]
    public async Task Persistent_5xx_ends_at_the_deadline_with_a_server_cancel()
    {
        var api = new FakeApi()
            .On("GET", "/executions/e1", (503, "{}"))
            .On("POST", "/executions/e1/cancel", (200, "{}"));
        var (c, _) = Make(api);
        var ex = await Assert.ThrowsAsync<ThunderduckException>(() => c.WaitAsync("e1", TimeSpan.FromMilliseconds(60)));
        Assert.Equal(ThunderduckErrorKind.Timeout, ex.Kind);
        Assert.Contains(api.Requests, r => r.PathAndQuery == "/executions/e1/cancel");
    }

    [Fact]
    public async Task Client_errors_while_polling_are_not_retried()
    {
        var (c, _) = Make(new FakeApi().On("GET", "/executions/e1", (403, "{\"detail\":\"nope\"}")));
        var ex = await Assert.ThrowsAsync<ThunderduckException>(() => c.WaitAsync("e1"));
        Assert.Equal(ThunderduckErrorKind.Authentication, ex.Kind);
    }

    [Fact]
    public async Task Wait_error_throws_QueryFailed_with_server_message()
    {
        var (c, _) = Make(new FakeApi().On("GET", "/executions/e1",
            (200, "{\"uuid\":\"e1\",\"status\":\"error\",\"error_message\":\"Binder Error: x\"}")));
        var ex = await Assert.ThrowsAsync<ThunderduckException>(() => c.WaitAsync("e1"));
        Assert.Equal(ThunderduckErrorKind.QueryFailed, ex.Kind);
        Assert.Equal("Binder Error: x", ex.Message);
        Assert.Equal("e1", ex.ExecutionId);
    }

    [Fact]
    public async Task Wait_cancelled_status_throws_Cancelled()
    {
        var (c, _) = Make(new FakeApi().On("GET", "/executions/e1", (200, "{\"uuid\":\"e1\",\"status\":\"cancelled\"}")));
        var ex = await Assert.ThrowsAsync<ThunderduckException>(() => c.WaitAsync("e1"));
        Assert.Equal(ThunderduckErrorKind.Cancelled, ex.Kind);
    }

    [Fact]
    public async Task Cancelling_the_token_mid_wait_posts_one_server_cancel()
    {
        using var cts = new CancellationTokenSource();
        var api = new FakeApi()
            .On("GET", "/executions/e1", (200, "{\"uuid\":\"e1\",\"status\":\"running\"}"))
            .On("POST", "/executions/e1/cancel", (200, "{}"));
        var (c, _) = Make(api);
        var task = c.WaitAsync("e1", ct: cts.Token);
        await Task.Delay(30);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Single(api.Requests, r => r.Method == "POST" && r.PathAndQuery == "/executions/e1/cancel");
    }

    [Fact]
    public async Task Timeout_cancels_on_server_and_throws_Timeout()
    {
        var api = new FakeApi()
            .On("GET", "/executions/e1", (200, "{\"uuid\":\"e1\",\"status\":\"running\"}"))
            .On("POST", "/executions/e1/cancel", (200, "{}"));
        var (c, _) = Make(api);
        var ex = await Assert.ThrowsAsync<ThunderduckException>(() => c.WaitAsync("e1", TimeSpan.FromMilliseconds(40)));
        Assert.Equal(ThunderduckErrorKind.Timeout, ex.Kind);
        Assert.Contains(api.Requests, r => r.PathAndQuery == "/executions/e1/cancel");
    }

    [Fact]
    public async Task Auth_failure_maps_to_Authentication_without_leaking_token()
    {
        var (c, _) = Make(new FakeApi().On("POST", "/queries", (401, "{\"detail\":\"invalid token\"}")));
        var ex = await Assert.ThrowsAsync<ThunderduckException>(() => c.SubmitAsync("select 1"));
        Assert.Equal(ThunderduckErrorKind.Authentication, ex.Kind);
        Assert.DoesNotContain("tdk_secret", ex.ToString());
    }

    [Fact]
    public async Task Cancel_swallows_404_and_409_but_surfaces_auth_errors()
    {
        var (c1, _) = Make(new FakeApi().On("POST", "/executions/a/cancel", (409, "{\"detail\":\"finished\"}")));
        await c1.CancelAsync("a");
        var (c2, _) = Make(new FakeApi().On("POST", "/executions/b/cancel", (403, "{\"detail\":\"nope\"}")));
        var ex = await Assert.ThrowsAsync<ThunderduckException>(() => c2.CancelAsync("b"));
        Assert.Equal(ThunderduckErrorKind.Authentication, ex.Kind);
    }

    [Fact]
    public async Task Page_requests_exact_decimals_and_maps_404_202_409()
    {
        var api = new FakeApi()
            .On("GET", "/executions/a/result", (202, "{}"), (200, "{\"columns\":[\"x\"],\"column_types\":[\"INTEGER\"],\"rows\":[[1]],\"total\":1}"))
            .On("GET", "/executions/b/result", (404, "{}"))
            .On("GET", "/executions/c/result", (409, "{\"detail\":\"result expired\"}"));
        var (c, _) = Make(api);
        var p = await c.GetPageAsync("a", 10, 0);
        Assert.NotNull(p);
        Assert.Equal(1, p!.Rows[0][0].GetInt32());
        Assert.Contains(api.Requests, r => r.PathAndQuery == "/executions/a/result?limit=10&offset=0&exact_decimals=true");
        Assert.Null(await c.GetPageAsync("b", 10, 0));
        var ex = await Assert.ThrowsAsync<ThunderduckException>(() => c.GetPageAsync("c", 10, 0));
        Assert.Equal(ThunderduckErrorKind.ResultExpired, ex.Kind);
    }

    [Fact]
    public async Task ReadPages_follows_total_even_when_the_server_caps_page_size()
    {
        // Asked for 5 per page; the server caps at 2 (result_max_rows). total = 5.
        var api = new FakeApi()
            .On("GET", "/executions/e/result?limit=5&offset=0", (200, "{\"columns\":[\"n\"],\"column_types\":[\"BIGINT\"],\"rows\":[[1],[2]],\"total\":5}"))
            .On("GET", "/executions/e/result?limit=5&offset=2", (200, "{\"columns\":[\"n\"],\"column_types\":[\"BIGINT\"],\"rows\":[[3],[4]],\"total\":5}"))
            .On("GET", "/executions/e/result?limit=5&offset=4", (200, "{\"columns\":[\"n\"],\"column_types\":[\"BIGINT\"],\"rows\":[[5]],\"total\":5}"));
        var (c, _) = Make(api);
        var all = new List<long>();
        await foreach (var page in c.ReadPagesAsync("e", 5))
            all.AddRange(page.Rows.Select(r => r[0].GetInt64()));
        Assert.Equal(new long[] { 1, 2, 3, 4, 5 }, all);
    }

    [Fact]
    public async Task Catalog_tree_maps_two_and_three_level_catalogs()
    {
        var json = """
        {"catalogs":[
          {"name":"lake","type":"nessie_iceberg","tables":[{"name":"t","kind":"table","columns":[{"name":"a","type":"INTEGER"}]}]},
          {"name":"pg","type":"postgresql","namespaces":[{"name":"public","tables":[{"name":"orders","kind":"view","columns":[]}]}]},
          {"name":"bad","error":"unreachable"}]}
        """;
        var (c, _) = Make(new FakeApi().On("GET", "/catalog/schema", (200, json)));
        var tree = await c.GetCatalogAsync();
        Assert.Equal(3, tree.Catalogs.Count);
        Assert.Null(tree.Catalogs[0].Schemas[0].Name);
        Assert.Equal("a", tree.Catalogs[0].Schemas[0].Tables[0].Columns[0].Name);
        Assert.Equal("public", tree.Catalogs[1].Schemas[0].Name);
        Assert.Equal("view", tree.Catalogs[1].Schemas[0].Tables[0].Kind);
        Assert.Equal("unreachable", tree.Catalogs[2].Error);
    }
}
