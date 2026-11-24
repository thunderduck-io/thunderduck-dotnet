using Dapper;
using Thunderduck.Data;

var token = Environment.GetEnvironmentVariable("THUNDERDUCK_TOKEN")
    ?? throw new InvalidOperationException("Set THUNDERDUCK_TOKEN to a tdk_… API token.");
await using var conn = new ThunderduckConnection($"Host=api.thunderduck.ai;Token={token}");
await conn.OpenAsync();

var orders = await conn.QueryAsync<Order>(
    "SELECT order_id AS Id, total_amount AS Total FROM tndr_postgres_demo.ecommerce.orders WHERE total_amount > @min LIMIT 10",
    new { min = 100m });
foreach (var o in orders) Console.WriteLine($"{o.Id}: {o.Total}");

public sealed record Order(long Id, decimal Total);
