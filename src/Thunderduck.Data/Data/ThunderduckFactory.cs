using System.Data.Common;

namespace Thunderduck.Data;

/// <summary>Provider factory (invariant name <c>Thunderduck.Data</c>).</summary>
public sealed class ThunderduckFactory : DbProviderFactory
{
    /// <summary>The provider invariant name.</summary>
    public const string InvariantName = "Thunderduck.Data";
    /// <summary>Singleton (required by DbProviderFactories).</summary>
    public static readonly ThunderduckFactory Instance = new();
    private ThunderduckFactory() { }
    /// <inheritdoc/>
    public override DbConnection CreateConnection() => new ThunderduckConnection();
    /// <inheritdoc/>
    public override DbCommand CreateCommand() => new ThunderduckCommand();
    /// <inheritdoc/>
    public override DbParameter CreateParameter() => new ThunderduckParameter();
    /// <inheritdoc/>
    public override DbConnectionStringBuilder CreateConnectionStringBuilder() => new ThunderduckConnectionStringBuilder();
}
