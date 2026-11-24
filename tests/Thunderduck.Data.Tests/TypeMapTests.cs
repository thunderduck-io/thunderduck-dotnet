using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Thunderduck.Client;
using Xunit;

public class TypeMapTests
{
    static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Theory]
    [InlineData("BOOLEAN", typeof(bool))]
    [InlineData("TINYINT", typeof(sbyte))]
    [InlineData("SMALLINT", typeof(short))]
    [InlineData("INTEGER", typeof(int))]
    [InlineData("BIGINT", typeof(long))]
    [InlineData("UTINYINT", typeof(byte))]
    [InlineData("USMALLINT", typeof(ushort))]
    [InlineData("UINTEGER", typeof(uint))]
    [InlineData("UBIGINT", typeof(ulong))]
    [InlineData("HUGEINT", typeof(BigInteger))]
    [InlineData("FLOAT", typeof(float))]
    [InlineData("DOUBLE", typeof(double))]
    [InlineData("DECIMAL(18,4)", typeof(decimal))]
    [InlineData("DATE", typeof(DateOnly))]
    [InlineData("TIME", typeof(TimeOnly))]
    [InlineData("TIMESTAMP", typeof(DateTime))]
    [InlineData("TIMESTAMP_NS", typeof(DateTime))]
    [InlineData("TIMESTAMP WITH TIME ZONE", typeof(DateTimeOffset))]
    [InlineData("UUID", typeof(Guid))]
    [InlineData("BLOB", typeof(byte[]))]
    [InlineData("VARCHAR", typeof(string))]
    [InlineData("INTEGER[]", typeof(string))]
    [InlineData("STRUCT(a INTEGER)", typeof(string))]
    [InlineData("MAP(VARCHAR, INTEGER)", typeof(string))]
    [InlineData("", typeof(string))]
    public void Clr_type(string duck, Type expected) => Assert.Equal(expected, TypeMap.ClrType(duck));

    [Fact]
    public void Converts_values()
    {
        Assert.Equal(DBNull.Value, TypeMap.Convert(J("null"), "INTEGER"));
        Assert.Equal(true, TypeMap.Convert(J("true"), "BOOLEAN"));
        Assert.Equal((sbyte)-3, TypeMap.Convert(J("-3"), "TINYINT"));
        Assert.Equal(42, TypeMap.Convert(J("42"), "INTEGER"));
        Assert.Equal(9007199254740993L, TypeMap.Convert(J("9007199254740993"), "BIGINT"));
        Assert.Equal(ulong.MaxValue, TypeMap.Convert(J("18446744073709551615"), "UBIGINT"));
        Assert.Equal(BigInteger.Parse("170141183460469231731687303715884105727"),
            TypeMap.Convert(J("170141183460469231731687303715884105727"), "HUGEINT"));
        Assert.Equal(1.5f, TypeMap.Convert(J("1.5"), "FLOAT"));
        Assert.Equal(12345678901234567.890123m, TypeMap.Convert(J("\"12345678901234567.890123\""), "DECIMAL(38,6)"));
        Assert.Equal(2.5m, TypeMap.Convert(J("2.5"), "DECIMAL(9,2)")); // server without exact_decimals
        Assert.Equal(new DateOnly(2024, 1, 31), TypeMap.Convert(J("\"2024-01-31\""), "DATE"));
        Assert.Equal(new TimeOnly(13, 45, 0, 123), TypeMap.Convert(J("\"13:45:00.123000\""), "TIME"));
        var ts = (DateTime)TypeMap.Convert(J("\"2024-01-31T13:45:00.123456\""), "TIMESTAMP");
        Assert.Equal(DateTimeKind.Unspecified, ts.Kind);
        Assert.Equal(new DateTime(2024, 1, 31, 13, 45, 0).AddTicks(1234560), ts);
        Assert.Equal(new DateTimeOffset(2024, 1, 31, 13, 45, 0, TimeSpan.FromHours(2)),
            TypeMap.Convert(J("\"2024-01-31T13:45:00+02:00\""), "TIMESTAMP WITH TIME ZONE"));
        Assert.Equal(Guid.Parse("2a6a3006-b4a9-4596-9ac7-1861dc1e9dd8"),
            TypeMap.Convert(J("\"2a6a3006-b4a9-4596-9ac7-1861dc1e9dd8\""), "UUID"));
        Assert.Equal(new byte[] { 0xde, 0xad }, TypeMap.Convert(J("\"dead\""), "BLOB"));
        Assert.Equal("[1,2]", TypeMap.Convert(J("[1,2]"), "INTEGER[]"));
        Assert.Equal("{\"a\":1}", TypeMap.Convert(J("{\"a\":1}"), "STRUCT(a INTEGER)"));
        Assert.Equal("héllo", TypeMap.Convert(J("\"héllo\""), "VARCHAR"));
    }

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("de-DE")]
    public void Culture_does_not_change_parsing(string culture)
    {
        var old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo(culture);
            Assert.Equal(1234.5m, TypeMap.Convert(J("\"1234.5\""), "DECIMAL(10,1)"));
            Assert.Equal(typeof(int), TypeMap.ClrType("integer")); // 'i' upper-casing in tr-TR
            Assert.Equal(new DateOnly(2024, 1, 31), TypeMap.Convert(J("\"2024-01-31\""), "DATE"));
        }
        finally { CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = old; }
    }

    [Fact]
    public void Decimal_precision()
    {
        Assert.Equal((18, 4), TypeMap.DecimalPrecision("DECIMAL(18,4)"));
        Assert.Null(TypeMap.DecimalPrecision("INTEGER"));
    }
}
