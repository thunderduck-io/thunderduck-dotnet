using Thunderduck.Data;

static partial class TestApi
{
    public const string Cs = "Host=api.example;Token=tdk_secret;PollInterval=5;Timeout=5;PageSize=2";

    public static ThunderduckConnection Open(FakeApi api)
    {
        var c = new ThunderduckConnection(Cs, api);
        c.Open();
        return c;
    }
}
