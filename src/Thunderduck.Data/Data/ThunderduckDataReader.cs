using System.Collections;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Thunderduck.Client;

namespace Thunderduck.Data;

/// <summary>Forward-only reader over a query's stored result, page by page
/// (the next page is fetched while the current one is read).</summary>
public sealed class ThunderduckDataReader : DbDataReader
{
    private readonly ThunderduckClient _client;
    private readonly string _id;
    private readonly int _pageSize;
    private readonly CancellationToken _ct;
    private readonly string[] _columns;
    private readonly string[] _types;
    private ResultPage? _page;
    private int _row = -1;
    private long _nextOffset;
    private Task<ResultPage?>? _prefetch;
    private bool _closed;

    private ThunderduckDataReader(ThunderduckClient client, string id, int pageSize, ResultPage? first, CancellationToken ct)
    {
        _client = client;
        _id = id;
        _pageSize = pageSize;
        _ct = ct;
        _page = first;
        _columns = first?.Columns.ToArray() ?? Array.Empty<string>();
        _types = first?.ColumnTypes.ToArray() ?? Array.Empty<string>();
        if (first is not null) Advance(first);
    }

    internal static async Task<ThunderduckDataReader> CreateAsync(ThunderduckClient client, string id, int pageSize, CancellationToken ct)
    {
        var first = await client.GetPageAsync(id, pageSize, 0, ct).ConfigureAwait(false);
        return new ThunderduckDataReader(client, id, pageSize, first, ct);
    }

    private void Advance(ResultPage page)
    {
        _nextOffset = page.Offset + page.Rows.Count;
        _prefetch = page.Rows.Count == 0 || _nextOffset >= page.Total
            ? null
            : _client.GetPageAsync(_id, _pageSize, _nextOffset, _ct);
    }

    /// <inheritdoc/>
    public override bool Read() => ReadAsync(CancellationToken.None).GetAwaiter().GetResult();

    /// <inheritdoc/>
    public override async Task<bool> ReadAsync(CancellationToken cancellationToken)
    {
        if (_closed || _page is null) return false;
        if (++_row < _page.Rows.Count) return true;
        if (_prefetch is null) { _row = _page.Rows.Count; return false; }
        var next = await _prefetch.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (next is null || next.Rows.Count == 0) { _prefetch = null; _row = _page.Rows.Count; return false; }
        _page = next;
        _row = 0;
        Advance(next);
        return true;
    }

    private JsonElement Cell(int i)
    {
        if (_page is null || _row < 0 || _row >= _page.Rows.Count) throw new InvalidOperationException("No current row; call Read() first.");
        return _page.Rows[_row][i];
    }

    /// <inheritdoc/>
    public override object GetValue(int ordinal) => TypeMap.ToProvider(Native(ordinal));

    private object Native(int ordinal) => TypeMap.Convert(Cell(ordinal), _types[ordinal]);
    /// <inheritdoc/>
    public override int GetValues(object[] values)
    {
        var n = Math.Min(values.Length, FieldCount);
        for (var i = 0; i < n; i++) values[i] = GetValue(i);
        return n;
    }
    /// <inheritdoc/>
    public override bool IsDBNull(int ordinal) => Cell(ordinal).ValueKind == JsonValueKind.Null;

    /// <inheritdoc/>
    public override T GetFieldValue<T>(int ordinal)
    {
        if (typeof(T) == typeof(JsonElement))
            return (T)(object)JsonDocument.Parse(Cell(ordinal).GetRawText()).RootElement.Clone();
        // Native first, so DateOnly/TimeOnly/DateTimeOffset (with its offset) are exact.
        var v = Native(ordinal);
        if (v is DBNull)
        {
            if (default(T) is null) return default!;
            throw new InvalidCastException($"Column {ordinal} is NULL.");
        }
        return v switch
        {
            T t => t,
            _ when TypeMap.ToProvider(v) is T p => p,
            DateTimeOffset o when typeof(T) == typeof(DateTime) => (T)(object)o.UtcDateTime,
            DateOnly d when typeof(T) == typeof(DateTime) => (T)(object)d.ToDateTime(TimeOnly.MinValue),
            TimeOnly t when typeof(T) == typeof(TimeSpan) => (T)(object)t.ToTimeSpan(),
            DateTime dt when typeof(T) == typeof(DateOnly) => (T)(object)DateOnly.FromDateTime(dt),
            IConvertible c => (T)c.ToType(Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T), CultureInfo.InvariantCulture),
            _ => throw new InvalidCastException($"Cannot read column {ordinal} ({_types[ordinal]}) as {typeof(T).Name}."),
        };
    }

    /// <inheritdoc/>
    public override bool GetBoolean(int ordinal) => GetFieldValue<bool>(ordinal);
    /// <inheritdoc/>
    public override byte GetByte(int ordinal) => GetFieldValue<byte>(ordinal);
    /// <inheritdoc/>
    public override char GetChar(int ordinal) => GetString(ordinal)[0];
    /// <inheritdoc/>
    public override DateTime GetDateTime(int ordinal) => GetFieldValue<DateTime>(ordinal);
    /// <inheritdoc/>
    public override decimal GetDecimal(int ordinal) => GetFieldValue<decimal>(ordinal);
    /// <inheritdoc/>
    public override double GetDouble(int ordinal) => GetFieldValue<double>(ordinal);
    /// <inheritdoc/>
    public override float GetFloat(int ordinal) => GetFieldValue<float>(ordinal);
    /// <inheritdoc/>
    public override Guid GetGuid(int ordinal) => GetFieldValue<Guid>(ordinal);
    /// <inheritdoc/>
    public override short GetInt16(int ordinal) => GetFieldValue<short>(ordinal);
    /// <inheritdoc/>
    public override int GetInt32(int ordinal) => GetFieldValue<int>(ordinal);
    /// <inheritdoc/>
    public override long GetInt64(int ordinal) => GetFieldValue<long>(ordinal);
    /// <inheritdoc/>
    public override string GetString(int ordinal) => GetValue(ordinal) is var v and not DBNull ? Convert.ToString(v, CultureInfo.InvariantCulture)! : throw new InvalidCastException($"Column {ordinal} is NULL.");

    /// <inheritdoc/>
    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length)
    {
        var data = GetFieldValue<byte[]>(ordinal);
        if (buffer is null) return data.Length;
        var n = (int)Math.Max(0, Math.Min(length, data.Length - dataOffset));
        Array.Copy(data, dataOffset, buffer, bufferOffset, n);
        return n;
    }

    /// <inheritdoc/>
    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)
    {
        var s = GetString(ordinal);
        if (buffer is null) return s.Length;
        var n = (int)Math.Max(0, Math.Min(length, s.Length - dataOffset));
        s.CopyTo((int)dataOffset, buffer, bufferOffset, n);
        return n;
    }

    /// <inheritdoc/>
    public override string GetDataTypeName(int ordinal) => _types[ordinal];
    /// <inheritdoc/>
    public override Type GetFieldType(int ordinal) => TypeMap.ProviderType(_types[ordinal]);
    /// <inheritdoc/>
    public override string GetName(int ordinal) => _columns[ordinal];
    /// <inheritdoc/>
    public override int GetOrdinal(string name)
    {
        var i = Array.IndexOf(_columns, name);
        if (i >= 0) return i;
        i = Array.FindIndex(_columns, c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase));
        return i >= 0 ? i : throw new IndexOutOfRangeException($"No column named {name}.");
    }

    /// <inheritdoc/>
    public override DataTable GetSchemaTable()
    {
        var t = new DataTable("SchemaTable");
        t.Columns.Add(SchemaTableColumn.ColumnName, typeof(string));
        t.Columns.Add(SchemaTableColumn.ColumnOrdinal, typeof(int));
        t.Columns.Add(SchemaTableColumn.ColumnSize, typeof(int));
        t.Columns.Add(SchemaTableColumn.NumericPrecision, typeof(short));
        t.Columns.Add(SchemaTableColumn.NumericScale, typeof(short));
        t.Columns.Add(SchemaTableColumn.DataType, typeof(Type));
        t.Columns.Add("DataTypeName", typeof(string));
        t.Columns.Add(SchemaTableColumn.AllowDBNull, typeof(bool));
        t.Columns.Add(SchemaTableColumn.IsKey, typeof(bool));
        t.Columns.Add(SchemaTableColumn.IsUnique, typeof(bool));
        t.Columns.Add(SchemaTableColumn.IsLong, typeof(bool));
        t.Columns.Add(SchemaTableOptionalColumn.IsReadOnly, typeof(bool));
        for (var i = 0; i < _columns.Length; i++)
        {
            var dp = TypeMap.DecimalPrecision(_types[i]);
            t.Rows.Add(_columns[i], i, -1,
                dp is { } p ? (object)(short)p.Precision : DBNull.Value,
                dp is { } s ? (object)(short)s.Scale : DBNull.Value,
                GetFieldType(i), _types[i], true, false, false, false, true);
        }
        return t;
    }

    /// <inheritdoc/>
    public override int Depth => 0;
    /// <inheritdoc/>
    public override int FieldCount => _columns.Length;
    /// <inheritdoc/>
    public override bool HasRows => _page is { Rows.Count: > 0 };
    /// <inheritdoc/>
    public override bool IsClosed => _closed;
    /// <inheritdoc/>
    public override int RecordsAffected => -1;
    /// <inheritdoc/>
    public override object this[int ordinal] => GetValue(ordinal);
    /// <inheritdoc/>
    public override object this[string name] => GetValue(GetOrdinal(name));
    /// <inheritdoc/>
    public override bool NextResult() => false;
    /// <inheritdoc/>
    public override IEnumerator GetEnumerator() => new DbEnumerator(this, closeReader: false);
    /// <inheritdoc/>
    public override void Close() => _closed = true;
}
