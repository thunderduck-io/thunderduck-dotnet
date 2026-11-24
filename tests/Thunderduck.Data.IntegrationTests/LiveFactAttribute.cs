using Xunit;
public sealed class LiveFactAttribute : FactAttribute
{
    public LiveFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("TD_TEST_HOST") is null || Environment.GetEnvironmentVariable("TD_TEST_TOKEN") is null)
            Skip = "Set TD_TEST_HOST and TD_TEST_TOKEN to run live tests.";
    }
}

