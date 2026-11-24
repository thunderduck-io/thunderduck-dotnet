using System.Collections;
using System.Data.Common;

namespace Thunderduck.Data;

/// <summary>Parameters of a <see cref="ThunderduckCommand"/>.</summary>
public sealed class ThunderduckParameterCollection : DbParameterCollection
{
    private readonly List<ThunderduckParameter> _items = new();

    /// <summary>Add a parameter with a value.</summary>
    public ThunderduckParameter AddWithValue(string name, object? value)
    {
        var p = new ThunderduckParameter(name, value);
        _items.Add(p);
        return p;
    }

    internal IReadOnlyDictionary<string, object?> ToDictionary()
    {
        var d = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in _items) d[p.NormalizedName] = p.Value;
        return d;
    }

    /// <inheritdoc/>
    public override int Count => _items.Count;
    /// <inheritdoc/>
    public override object SyncRoot => ((ICollection)_items).SyncRoot;
    /// <inheritdoc/>
    public override int Add(object value) { _items.Add(Cast(value)); return _items.Count - 1; }
    /// <inheritdoc/>
    public override void AddRange(Array values) { foreach (var v in values) Add(v!); }
    /// <inheritdoc/>
    public override void Clear() => _items.Clear();
    /// <inheritdoc/>
    public override bool Contains(object value) => value is ThunderduckParameter p && _items.Contains(p);
    /// <inheritdoc/>
    public override bool Contains(string value) => IndexOf(value) >= 0;
    /// <inheritdoc/>
    public override void CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);
    /// <inheritdoc/>
    public override IEnumerator GetEnumerator() => _items.GetEnumerator();
    /// <inheritdoc/>
    public override int IndexOf(object value) => value is ThunderduckParameter p ? _items.IndexOf(p) : -1;
    /// <inheritdoc/>
    public override int IndexOf(string parameterName)
    {
        var n = parameterName.TrimStart('@', ':', '$');
        return _items.FindIndex(p => string.Equals(p.NormalizedName, n, StringComparison.OrdinalIgnoreCase));
    }
    /// <inheritdoc/>
    public override void Insert(int index, object value) => _items.Insert(index, Cast(value));
    /// <inheritdoc/>
    public override void Remove(object value) => _items.Remove(Cast(value));
    /// <inheritdoc/>
    public override void RemoveAt(int index) => _items.RemoveAt(index);
    /// <inheritdoc/>
    public override void RemoveAt(string parameterName) => _items.RemoveAt(IndexOfOrThrow(parameterName));
    /// <inheritdoc/>
    protected override DbParameter GetParameter(int index) => _items[index];
    /// <inheritdoc/>
    protected override DbParameter GetParameter(string parameterName) => _items[IndexOfOrThrow(parameterName)];
    /// <inheritdoc/>
    protected override void SetParameter(int index, DbParameter value) => _items[index] = Cast(value);
    /// <inheritdoc/>
    protected override void SetParameter(string parameterName, DbParameter value) => _items[IndexOfOrThrow(parameterName)] = Cast(value);

    private int IndexOfOrThrow(string name) =>
        IndexOf(name) is var i and >= 0 ? i : throw new IndexOutOfRangeException($"No parameter named {name}.");
    private static ThunderduckParameter Cast(object value) =>
        value as ThunderduckParameter ?? throw new InvalidCastException("Expected a ThunderduckParameter.");
}
