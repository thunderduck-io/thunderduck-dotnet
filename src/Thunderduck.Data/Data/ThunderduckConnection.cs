using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Thunderduck.Client;

namespace Thunderduck.Data;

/// <summary>A connection to thunderduck's query API.</summary>
public sealed class ThunderduckConnection : DbConnection
{
    private readonly HttpMessageHandler? _handler; // test seam
    private string _connectionString = "";
    private ConnectionState _state = ConnectionState.Closed;
    private ThunderduckClient? _client;
    private ThunderduckConnectionStringBuilder _settings = new();

    /// <summary>Empty connection.</summary>
    public ThunderduckConnection() { }
    /// <summary>Connection with a connection string.</summary>
    public ThunderduckConnection(string connectionString) => ConnectionString = connectionString;
    internal ThunderduckConnection(string connectionString, HttpMessageHandler handler) : this(connectionString) => _handler = handler;

    /// <inheritdoc/>
    [AllowNull]
    public override string ConnectionString
    {
        get
        {
            if (_state != ConnectionState.Open) return _connectionString;
            var redacted = new ThunderduckConnectionStringBuilder(_connectionString);
            redacted.Remove("Token");
            return redacted.ConnectionString;
        }
        set
        {
            if (_state == ConnectionState.Open) throw new InvalidOperationException("Close the connection before changing ConnectionString.");
            _connectionString = value ?? "";
            _settings = new ThunderduckConnectionStringBuilder(_connectionString);
        }
    }

    /// <inheritdoc/>
    public override string Database => "";
    /// <inheritdoc/>
    public override string DataSource => _settings.Host;
    /// <inheritdoc/>
    public override string ServerVersion => "thunderduck";
    /// <inheritdoc/>
    public override ConnectionState State => _state;

    internal ThunderduckClient Client => _client ?? throw new InvalidOperationException("The connection is not open.");
    internal ThunderduckConnectionStringBuilder Settings => _settings;

    /// <inheritdoc/>
    public override void Open() => OpenAsync(CancellationToken.None).GetAwaiter().GetResult();

    /// <inheritdoc/>
    public override async Task OpenAsync(CancellationToken cancellationToken)
    {
        if (_state == ConnectionState.Open) return;
        if (string.IsNullOrWhiteSpace(_settings.Host)) throw new InvalidOperationException("Host is required.");
        if (string.IsNullOrWhiteSpace(_settings.Token)) throw new InvalidOperationException("Token is required.");
        var client = new ThunderduckClient(new ThunderduckClientOptions
        {
            BaseUri = _settings.BaseUri,
            Token = _settings.Token,
            PollInterval = TimeSpan.FromMilliseconds(_settings.PollIntervalMs),
            Timeout = _settings.Timeout,
            HttpClient = _handler is null ? null : new HttpClient(_handler, disposeHandler: false),
        });
        await client.ValidateAsync(cancellationToken).ConfigureAwait(false);
        _client = client;
        _state = ConnectionState.Open;
    }

    /// <inheritdoc/>
    public override void Close()
    {
        _client = null;
        _state = ConnectionState.Closed;
    }

    /// <inheritdoc/>
    public override void ChangeDatabase(string databaseName) => throw new NotSupportedException("thunderduck has no current database; use catalog.schema.table names.");

    /// <inheritdoc/>
    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException("Transactions are not supported.");

    /// <summary>A command on this connection (typed, so <c>Parameters.AddWithValue</c> is available).</summary>
    public new ThunderduckCommand CreateCommand() => new() { Connection = this };

    /// <inheritdoc/>
    protected override DbCommand CreateDbCommand() => CreateCommand();

    /// <inheritdoc/>
    protected override DbProviderFactory DbProviderFactory => ThunderduckFactory.Instance;

    /// <inheritdoc/>
    public override DataTable GetSchema() => GetSchema("MetaDataCollections");
    /// <inheritdoc/>
    public override DataTable GetSchema(string collectionName) => GetSchema(collectionName, Array.Empty<string?>());
    /// <inheritdoc/>
    public override DataTable GetSchema(string collectionName, string?[] restrictionValues) =>
        SchemaCollections.Get(this, collectionName, restrictionValues);
}
