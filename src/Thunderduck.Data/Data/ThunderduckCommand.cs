using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

namespace Thunderduck.Data;

/// <summary>A SQL statement run as a thunderduck query.</summary>
public sealed class ThunderduckCommand : DbCommand
{
    private readonly ThunderduckParameterCollection _parameters = new();
    private CancellationTokenSource? _running;

    /// <inheritdoc/>
    [AllowNull] public override string CommandText { get; set; } = "";
    /// <summary>Seconds to wait for the query; 0 = the connection's Timeout.</summary>
    public override int CommandTimeout { get; set; }
    /// <inheritdoc/>
    public override CommandType CommandType
    {
        get => CommandType.Text;
        set { if (value != CommandType.Text) throw new NotSupportedException("Only CommandType.Text is supported."); }
    }
    /// <inheritdoc/>
    public override bool DesignTimeVisible { get; set; }
    /// <inheritdoc/>
    public override UpdateRowSource UpdatedRowSource { get; set; } = UpdateRowSource.None;
    /// <inheritdoc/>
    protected override DbConnection? DbConnection { get; set; }
    /// <inheritdoc/>
    protected override DbParameterCollection DbParameterCollection => _parameters;
    /// <inheritdoc/>
    protected override DbTransaction? DbTransaction { get => null; set { if (value is not null) throw new NotSupportedException("Transactions are not supported."); } }
    /// <summary>The parameters (supports <c>AddWithValue</c>).</summary>
    public new ThunderduckParameterCollection Parameters => _parameters;

    /// <summary>Stop the running query and cancel it on the server. Safe from another thread.</summary>
    public override void Cancel() => _running?.Cancel();

    /// <inheritdoc/>
    public override void Prepare() { }

    /// <inheritdoc/>
    protected override DbParameter CreateDbParameter() => new ThunderduckParameter();

    /// <inheritdoc/>
    public override int ExecuteNonQuery() => ExecuteNonQueryAsync(CancellationToken.None).GetAwaiter().GetResult();

    /// <inheritdoc/>
    public override async Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
    {
        await using var r = await ExecuteDbDataReaderAsync(CommandBehavior.Default, cancellationToken).ConfigureAwait(false);
        return -1;
    }

    /// <inheritdoc/>
    public override object? ExecuteScalar() => ExecuteScalarAsync(CancellationToken.None).GetAwaiter().GetResult();

    /// <inheritdoc/>
    public override async Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken)
    {
        await using var r = await ExecuteDbDataReaderAsync(CommandBehavior.SingleRow, cancellationToken).ConfigureAwait(false);
        return r.FieldCount > 0 && await r.ReadAsync(cancellationToken).ConfigureAwait(false) ? r.GetValue(0) : null;
    }

    /// <inheritdoc/>
    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) =>
        ExecuteDbDataReaderAsync(behavior, CancellationToken.None).GetAwaiter().GetResult();

    /// <inheritdoc/>
    protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
    {
        var conn = DbConnection as ThunderduckConnection ?? throw new InvalidOperationException("Connection is not set.");
        var client = conn.Client;
        var sql = SqlParameterRenderer.Render(CommandText, _parameters.ToDictionary());
        using var running = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _running = running;
        try
        {
            var id = await client.SubmitAsync(sql, running.Token).ConfigureAwait(false);
            var timeout = CommandTimeout > 0 ? TimeSpan.FromSeconds(CommandTimeout) : conn.Settings.Timeout;
            await client.WaitAsync(id, timeout, running.Token).ConfigureAwait(false);
            return await ThunderduckDataReader.CreateAsync(client, id, conn.Settings.PageSize, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _running = null;
        }
    }
}
