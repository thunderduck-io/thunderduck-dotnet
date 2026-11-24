using Xunit;

public class GetSchemaTests
{
    const string Tree = """
    {"catalogs":[
      {"name":"lake","type":"nessie_iceberg","tables":[{"name":"events","kind":"table","columns":[{"name":"id","type":"BIGINT"},{"name":"ts","type":"TIMESTAMP"}]}]},
      {"name":"pg","type":"postgresql","namespaces":[{"name":"public","tables":[{"name":"orders","kind":"view","columns":[{"name":"total","type":"DECIMAL(12,2)"}]}]}]}]}
    """;

    static Thunderduck.Data.ThunderduckConnection Conn() => TestApi.Open(new FakeApi()
        .On("GET", "/auth/me", (200, "{}"))
        .On("GET", "/catalog/schema", (200, Tree)));

    [Fact]
    public void Collections()
    {
        using var c = Conn();
        Assert.Equal(5, c.GetSchema().Rows.Count);
        Assert.Equal(2, c.GetSchema("Catalogs").Rows.Count);
        var schemas = c.GetSchema("Schemas");
        Assert.Equal(2, schemas.Rows.Count);
        Assert.Equal(DBNull.Value, schemas.Rows[0]["SCHEMA_NAME"]);
        var tables = c.GetSchema("Tables");
        Assert.Equal("VIEW", tables.Rows[1]["TABLE_TYPE"]);
        Assert.Equal(3, c.GetSchema("Columns").Rows.Count);
    }

    [Fact]
    public void Restrictions_filter()
    {
        using var c = Conn();
        var cols = c.GetSchema("Columns", new[] { "lake", null, "events" });
        Assert.Equal(2, cols.Rows.Count);
        Assert.Equal(2, cols.Rows[1]["ORDINAL_POSITION"]);
        Assert.Single(c.GetSchema("Tables", new[] { "pg", "public" }).Rows);
        Assert.Throws<ArgumentException>(() => c.GetSchema("Nope"));
    }
}
