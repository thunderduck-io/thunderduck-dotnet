using System.Data;
using Thunderduck.Data;
using Xunit;
public class LiveTests
{
    static ThunderduckConnection Open()
    {
        var c = new ThunderduckConnection($"Host={Environment.GetEnvironmentVariable("TD_TEST_HOST")};Token={Environment.GetEnvironmentVariable("TD_TEST_TOKEN")}");
        c.Open();
        return c;
    }

    [LiveFact]
    public void Range_round_trip_pages()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT range AS n FROM range(2500)";
        using var r = cmd.ExecuteReader();
        long sum = 0, count = 0;
        while (r.Read()) { sum += r.GetInt64(0); count++; }
        Assert.Equal(2500, count);
        Assert.Equal(2500L * 2499 / 2, sum);
    }

    [LiveFact]
    public void Typed_columns_and_exact_decimal()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT 12345678901234567.890123::DECIMAL(38,6) AS d, DATE '2024-01-31' AS day, uuid() AS u";
        using var r = cmd.ExecuteReader();
        Assert.True(r.Read());
        Assert.Equal(12345678901234567.890123m, r.GetDecimal(0));
        Assert.Equal(new DateOnly(2024, 1, 31), r.GetFieldValue<DateOnly>(1));
        Assert.NotEqual(Guid.Empty, r.GetGuid(2));
    }

    [LiveFact]
    public async Task Cancel_a_long_query()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM range(10000000000) a";
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cmd.ExecuteReaderAsync(cts.Token));
    }

    [LiveFact]
    public void GetSchema_tables()
    {
        using var c = Open();
        Assert.True(c.GetSchema("Tables").Rows.Count >= 0);
    }
}
