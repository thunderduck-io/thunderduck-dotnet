using Dapper;
using Xunit;

public class DapperTests
{
    public sealed record Order(int Id, decimal Total, string Customer, DateTime PlacedAt);

    [Fact]
    public void Query_maps_rows_to_records_and_passes_parameters()
    {
        var api = TestApi.Query(["id", "total", "customer", "PlacedAt"],
            ["INTEGER", "DECIMAL(12,2)", "VARCHAR", "TIMESTAMP"],
            [1, "19.99", "ada", "2024-01-31T10:00:00"], [2, "5.00", "bob", "2024-02-01T11:30:00"]);
        using var c = TestApi.Open(api);
        var rows = c.Query<Order>("SELECT id, total, customer, placed_at AS PlacedAt FROM orders WHERE total > @min", new { min = 1.5m }).ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal(new Order(1, 19.99m, "ada", new DateTime(2024, 1, 31, 10, 0, 0)), rows[0]);
        // The body is JSON (System.Text.Json escapes '>'), so decode it before checking the SQL.
        Assert.Contains(api.Requests, r => r.Method == "POST" &&
            System.Text.Json.JsonDocument.Parse(r.Body!).RootElement.GetProperty("sql").GetString()!
                .Contains("total > 1.5", StringComparison.Ordinal));
    }

    [Fact]
    public void ExecuteScalar_with_dapper()
    {
        using var c = TestApi.Open(TestApi.Query(["n"], ["BIGINT"], [42L]));
        Assert.Equal(42L, c.ExecuteScalar<long>("SELECT count(*) FROM t"));
    }
}
