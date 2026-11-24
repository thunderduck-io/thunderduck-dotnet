using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace Thunderduck.Client;

/// <summary>DuckDB type names (as reported in <c>column_types</c>) to .NET types and values.</summary>
internal static class TypeMap
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Upper-case base type: parameters stripped, nested types collapsed to LIST/STRUCT/MAP/UNION.</summary>
    public static string Normalize(string duckType)
    {
        var t = (duckType ?? "").Trim().ToUpperInvariant();
        if (t.EndsWith(']')) return "LIST";
        foreach (var nested in new[] { "STRUCT", "MAP", "UNION", "LIST" })
            if (t.StartsWith(nested, StringComparison.Ordinal)) return nested;
        var paren = t.IndexOf('(');
        return paren >= 0 ? t[..paren].Trim() : t;
    }

    public static Type ClrType(string duckType) => Normalize(duckType) switch
    {
        "BOOLEAN" or "BOOL" => typeof(bool),
        "TINYINT" or "INT1" => typeof(sbyte),
        "SMALLINT" or "INT2" => typeof(short),
        "INTEGER" or "INT" or "INT4" => typeof(int),
        "BIGINT" or "INT8" => typeof(long),
        "UTINYINT" => typeof(byte),
        "USMALLINT" => typeof(ushort),
        "UINTEGER" => typeof(uint),
        "UBIGINT" => typeof(ulong),
        "HUGEINT" or "UHUGEINT" => typeof(BigInteger),
        "FLOAT" or "REAL" or "FLOAT4" => typeof(float),
        "DOUBLE" or "FLOAT8" => typeof(double),
        "DECIMAL" or "NUMERIC" => typeof(decimal),
        "DATE" => typeof(DateOnly),
        "TIME" => typeof(TimeOnly),
        "TIMESTAMP" or "DATETIME" or "TIMESTAMP_S" or "TIMESTAMP_MS" or "TIMESTAMP_NS" => typeof(DateTime),
        "TIMESTAMP WITH TIME ZONE" or "TIMESTAMPTZ" => typeof(DateTimeOffset),
        "UUID" => typeof(Guid),
        "BLOB" or "BYTEA" or "BINARY" or "VARBINARY" => typeof(byte[]),
        _ => typeof(string),
    };

    /// <summary>The type ADO.NET consumers see (GetFieldType/GetValue): DATE, TIME
    /// and TIMESTAMPTZ surface as DateTime, TimeSpan and UTC DateTime, as in
    /// Npgsql, so Dapper/DataTable map them onto ordinary properties. The
    /// native DateOnly/TimeOnly/DateTimeOffset stay readable via GetFieldValue.</summary>
    public static Type ProviderType(string duckType)
    {
        var t = ClrType(duckType);
        return t == typeof(DateOnly) || t == typeof(DateTimeOffset) ? typeof(DateTime)
            : t == typeof(TimeOnly) ? typeof(TimeSpan)
            : t;
    }

    /// <summary>A native value (from <see cref="Convert"/>) as its <see cref="ProviderType"/>.</summary>
    public static object ToProvider(object native) => native switch
    {
        DateOnly d => d.ToDateTime(TimeOnly.MinValue),
        TimeOnly t => t.ToTimeSpan(),
        DateTimeOffset o => o.UtcDateTime,
        _ => native,
    };

    public static object Convert(JsonElement v, string duckType)
    {
        if (v.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return DBNull.Value;
        var t = ClrType(duckType);
        string S() => v.ValueKind == JsonValueKind.String ? v.GetString()! : v.GetRawText();
        if (t == typeof(bool)) return v.ValueKind == JsonValueKind.String ? bool.Parse(v.GetString()!) : v.GetBoolean();
        if (t == typeof(sbyte)) return sbyte.Parse(S(), Inv);
        if (t == typeof(short)) return short.Parse(S(), Inv);
        if (t == typeof(int)) return int.Parse(S(), Inv);
        if (t == typeof(long)) return long.Parse(S(), Inv);
        if (t == typeof(byte)) return byte.Parse(S(), Inv);
        if (t == typeof(ushort)) return ushort.Parse(S(), Inv);
        if (t == typeof(uint)) return uint.Parse(S(), Inv);
        if (t == typeof(ulong)) return ulong.Parse(S(), Inv);
        if (t == typeof(BigInteger)) return BigInteger.Parse(S(), Inv);
        if (t == typeof(float)) return float.Parse(S(), NumberStyles.Float, Inv);
        if (t == typeof(double)) return double.Parse(S(), NumberStyles.Float, Inv);
        if (t == typeof(decimal)) return decimal.Parse(S(), NumberStyles.Float, Inv);
        if (t == typeof(DateOnly)) return DateOnly.Parse(S(), Inv);
        if (t == typeof(TimeOnly)) return TimeOnly.Parse(S(), Inv);
        if (t == typeof(DateTime)) return DateTime.Parse(S(), Inv, DateTimeStyles.None);
        if (t == typeof(DateTimeOffset)) return DateTimeOffset.Parse(S(), Inv);
        if (t == typeof(Guid)) return Guid.Parse(S());
        if (t == typeof(byte[])) return System.Convert.FromHexString(S());
        return S();
    }

    public static (int Precision, int Scale)? DecimalPrecision(string duckType)
    {
        var t = (duckType ?? "").Trim().ToUpperInvariant();
        if (!t.StartsWith("DECIMAL(", StringComparison.Ordinal) && !t.StartsWith("NUMERIC(", StringComparison.Ordinal)) return null;
        var inner = t[(t.IndexOf('(') + 1)..t.IndexOf(')')].Split(',');
        return inner.Length == 2 &&
               int.TryParse(inner[0].Trim(), NumberStyles.Integer, Inv, out var p) &&
               int.TryParse(inner[1].Trim(), NumberStyles.Integer, Inv, out var s)
            ? (p, s) : null;
    }
}
