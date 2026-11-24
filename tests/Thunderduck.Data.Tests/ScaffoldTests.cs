using Thunderduck.Client;
using Xunit;

public class ScaffoldTests
{
    [Fact]
    public void Exception_carries_kind_status_and_execution_id_and_is_a_DbException()
    {
        var e = new ThunderduckException(ThunderduckErrorKind.QueryFailed, "boom", 400, "exec-1");
        Assert.IsAssignableFrom<System.Data.Common.DbException>(e);
        Assert.Equal(ThunderduckErrorKind.QueryFailed, e.Kind);
        Assert.Equal(400, e.HttpStatus);
        Assert.Equal("exec-1", e.ExecutionId);
        Assert.Equal("boom", e.Message);
    }

    [Fact]
    public void Options_defaults()
    {
        var o = new ThunderduckClientOptions { BaseUri = new Uri("https://h"), Token = "tdk_x" };
        Assert.Equal(TimeSpan.FromSeconds(1), o.PollInterval);
        Assert.Equal(TimeSpan.FromMinutes(5), o.Timeout);
        Assert.DoesNotContain("tdk_x", o.ToString());
    }
}
