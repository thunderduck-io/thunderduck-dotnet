using System.Data;
using System.Text.Json;
using Thunderduck.Data;
using Xunit;

public class DataReaderTests
{
    static ThunderduckDataReader Run(FakeApi api) => TestApi.Reader(api);

    [Fact]
    public void Reads_typed_values_across_pages()
    {
        var api = TestApi.Query(["id", "price", "day", "tags"], ["INTEGER", "DECIMAL(18,2)", "DATE", "VARCHAR[]"],
            [1, "10.25", "2024-01-31", new[] { "a" }], [2, "3.10", "2024-02-01", new[] { "b" }], [3, null, null, null]);
        using var r = Run(api);
        Assert.Equal(4, r.FieldCount);
        Assert.Equal(typeof(decimal), r.GetFieldType(1));
        Assert.Equal("DECIMAL(18,2)", r.GetDataTypeName(1));
        var ids = new List<int>();
        while (r.Read())
        {
            ids.Add(r.GetInt32(0));
            if (r.GetInt32(0) == 1)
            {
                Assert.Equal(10.25m, r.GetDecimal(1));
                Assert.Equal(new DateTime(2024, 1, 31), r.GetDateTime(2));
                Assert.Equal(new DateOnly(2024, 1, 31), r.GetFieldValue<DateOnly>(2));
                Assert.Equal("a", r.GetFieldValue<JsonElement>(3)[0].GetString());
            }
            if (r.GetInt32(0) == 3) Assert.True(r.IsDBNull(1));
        }
        Assert.Equal(new[] { 1, 2, 3 }, ids);
        Assert.Contains(api.Requests, q => q.PathAndQuery == "/executions/e1/result?limit=2&offset=2&exact_decimals=true");
    }

    [Fact]
    public void Ordinal_lookup_is_case_insensitive_and_schema_table_works()
    {
        using var r = Run(TestApi.Query(["Name"], ["VARCHAR"], ["x"]));
        Assert.Equal(0, r.GetOrdinal("name"));
        var schema = r.GetSchemaTable();
        Assert.Equal("Name", schema.Rows[0]["ColumnName"]);
        Assert.Equal(typeof(string), schema.Rows[0]["DataType"]);
    }

    [Fact]
    public void DataTable_Load_works()
    {
        using var r = Run(TestApi.Query(["n"], ["BIGINT"], [1L], [2L], [3L]));
        var t = new DataTable();
        t.Load(r);
        Assert.Equal(3, t.Rows.Count);
        Assert.Equal(typeof(long), t.Columns[0].DataType);
    }

    [Fact]
    public void Empty_reader_for_non_select()
    {
        using var r = Run(new FakeApi().On("GET", "/executions/e1/result", (404, "{}")));
        Assert.Equal(0, r.FieldCount);
        Assert.False(r.HasRows);
        Assert.False(r.Read());
        Assert.Equal(-1, r.RecordsAffected);
        Assert.False(r.NextResult());
    }
}
