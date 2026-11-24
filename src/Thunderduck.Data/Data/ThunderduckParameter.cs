using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

namespace Thunderduck.Data;

/// <summary>A named parameter, referenced as @name in the SQL.</summary>
public sealed class ThunderduckParameter : DbParameter
{
    /// <summary>Empty parameter.</summary>
    public ThunderduckParameter() { }
    /// <summary>Named parameter with a value.</summary>
    public ThunderduckParameter(string name, object? value) { ParameterName = name; Value = value; }

    /// <inheritdoc/>
    public override DbType DbType { get; set; } = DbType.Object;
    /// <inheritdoc/>
    public override ParameterDirection Direction
    {
        get => ParameterDirection.Input;
        set { if (value != ParameterDirection.Input) throw new NotSupportedException("Only input parameters are supported."); }
    }
    /// <inheritdoc/>
    public override bool IsNullable { get; set; } = true;
    /// <inheritdoc/>
    [AllowNull] public override string ParameterName { get; set; } = "";
    /// <inheritdoc/>
    public override int Size { get; set; }
    /// <inheritdoc/>
    [AllowNull] public override string SourceColumn { get; set; } = "";
    /// <inheritdoc/>
    public override bool SourceColumnNullMapping { get; set; }
    /// <inheritdoc/>
    public override object? Value { get; set; }
    /// <inheritdoc/>
    public override void ResetDbType() => DbType = DbType.Object;

    internal string NormalizedName => ParameterName.TrimStart('@', ':', '$');
}
