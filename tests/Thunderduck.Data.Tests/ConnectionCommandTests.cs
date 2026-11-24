using System.Data;
using Thunderduck.Client;
using Thunderduck.Data;
using Xunit;

public class ConnectionCommandTests
{
    [Fact]
    public void Builder_parses_and_builds_base_uri()
    {
        var b = new ThunderduckConnectionStringBuilder("Host=api.example;Token=tdk_x;Ssl=false;Port=8000;PageSize=50");
        Assert.Equal(new Uri("http://api.example:8000/"), b.BaseUri);
        Assert.Equal(50, b.PageSize);
        Assert.Equal(TimeSpan.FromSeconds(300), b.Timeout);
    }

    [Fact]
    public void Open_validates_token_and_redacts_connection_string()
    {
        var api = new FakeApi().On("GET", "/auth/me", (200, "{}"));
        using var c = new ThunderduckConnection(TestApi.Cs, api);
        Assert.Contains("tdk_secret", c.ConnectionString);
        c.Open();
        Assert.Equal(ConnectionState.Open, c.State);
        Assert.DoesNotContain("tdk_secret", c.ConnectionString);
        Assert.Equal("api.example", c.DataSource);
    }

    [Fact]
    public void Open_with_bad_token_throws_Authentication()
    {
        var api = new FakeApi().On("GET", "/auth/me", (401, "{\"detail\":\"bad\"}"));
        using var c = new ThunderduckConnection(TestApi.Cs, api);
        var ex = Assert.Throws<ThunderduckException>(() => c.Open());
        Assert.Equal(ThunderduckErrorKind.Authentication, ex.Kind);
        Assert.Equal(ConnectionState.Closed, c.State);
    }

    [Fact]
    public void Transactions_are_not_supported()
    {
        using var c = TestApi.Open(new FakeApi().On("GET", "/auth/me", (200, "{}")));
        Assert.Throws<NotSupportedException>(() => c.BeginTransaction());
    }

    [Fact]
    public void ExecuteScalar_renders_parameters_and_returns_first_value()
    {
        var api = TestApi.Query(["n"], ["BIGINT"], [7L]);
        using var c = TestApi.Open(api);
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM t WHERE name = @name";
        ((ThunderduckParameterCollection)cmd.Parameters).AddWithValue("name", "O'Brien");
        Assert.Equal(7L, cmd.ExecuteScalar());
        Assert.Contains(api.Requests, r => r.Body == "{\"sql\":\"SELECT count(*) FROM t WHERE name = \\u0027O\\u0027\\u0027Brien\\u0027\"}");
    }

    [Fact]
    public void ExecuteNonQuery_returns_minus_one_for_a_statement_without_result()
    {
        var api = new FakeApi()
            .On("GET", "/auth/me", (200, "{}"))
            .On("POST", "/queries", (201, "{\"id\":\"e1\"}"))
            .On("GET", "/executions/e1/result", (404, "{}"))
            .On("GET", "/executions/e1", (200, "{\"uuid\":\"e1\",\"status\":\"success\"}"));
        using var c = TestApi.Open(api);
        using var cmd = c.CreateCommand();
        cmd.CommandText = "CREATE SCHEMA cat.s";
        Assert.Equal(-1, cmd.ExecuteNonQuery());
    }

    [Fact]
    public async Task Cancel_from_another_thread_stops_and_cancels_on_server()
    {
        var api = new FakeApi()
            .On("GET", "/auth/me", (200, "{}"))
            .On("POST", "/queries", (201, "{\"id\":\"e1\"}"))
            .On("POST", "/executions/e1/cancel", (200, "{}"))
            .On("GET", "/executions/e1", (200, "{\"uuid\":\"e1\",\"status\":\"running\"}"));
        using var c = TestApi.Open(api);
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT 1";
        var run = Task.Run(() => cmd.ExecuteReader());
        await Task.Delay(50);
        cmd.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Single(api.Requests, r => r.PathAndQuery == "/executions/e1/cancel");
    }

    [Fact]
    public void CreateCommand_is_typed_so_AddWithValue_is_reachable()
    {
        using var c = new ThunderduckConnection(TestApi.Cs);
        ThunderduckCommand cmd = c.CreateCommand();
        cmd.Parameters.AddWithValue("x", 1);
        Assert.Same(c, cmd.Connection);
        Assert.Single(cmd.Parameters);
    }

    [Fact]
    public void Factory_creates_provider_objects()
    {
        Assert.IsType<ThunderduckConnection>(ThunderduckFactory.Instance.CreateConnection());
        Assert.IsType<ThunderduckCommand>(ThunderduckFactory.Instance.CreateCommand());
        Assert.IsType<ThunderduckParameter>(ThunderduckFactory.Instance.CreateParameter());
    }
}
