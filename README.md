# Thunderduck.Data

ADO.NET provider and async client for [thunderduck](https://thunderduck.ai).
It lets .NET 8+ applications query thunderduck catalogs with plain ADO.NET,
Dapper or `DataTable`. It has no third-party dependencies.

```
dotnet add package Thunderduck.Data
```

[Source on GitHub](https://github.com/thunderduck-io/thunderduck-dotnet) ·
[Documentation](https://docs.thunderduck.ai/clients/dotnet) ·
[NuGet](https://www.nuget.org/packages/Thunderduck.Data)

You need an API token (`tdk_…`). Create one in the console under
**Settings → API tokens**. Load it from configuration or a secret store, and
never hard-code it.

## Connection string

| Key | Default | Meaning |
|---|---|---|
| `Host` | — | API host, e.g. `api.thunderduck.ai` |
| `Token` | — | API token (`tdk_…`). Removed from `ConnectionString` after `Open()` |
| `Ssl` | `true` | HTTPS |
| `Port` | — | Optional port |
| `PollInterval` | `1000` | How often a running query is polled, in ms |
| `Timeout` | `300` | Default query timeout in seconds (`CommandTimeout` overrides it) |
| `PageSize` | `1000` | Rows fetched per page while reading |

## ADO.NET

```csharp
using Thunderduck.Data;

await using var conn = new ThunderduckConnection(
    $"Host=api.thunderduck.ai;Token={token}");
await conn.OpenAsync();            // validates the token

await using var cmd = conn.CreateCommand();
cmd.CommandText = "SELECT order_id, total_amount FROM pg.public.orders WHERE total_amount > @min";
cmd.Parameters.AddWithValue("min", 100m);

await using var reader = await cmd.ExecuteReaderAsync();
while (await reader.ReadAsync())
    Console.WriteLine($"{reader.GetInt64(0)}: {reader.GetDecimal(1)}");
```

Address tables as `catalog.schema.table`, or `catalog.table` for two-level
catalogs. There is no current database.

## Dapper

```csharp
using Dapper;

var orders = await conn.QueryAsync<Order>(
    "SELECT order_id AS Id, total_amount AS Total FROM pg.public.orders WHERE total_amount > @min",
    new { min = 100m });

public sealed record Order(long Id, decimal Total);
```

## Typed client

Use the client when you want to submit, wait and page yourself:

```csharp
using Thunderduck.Client;

var client = new ThunderduckClient(new ThunderduckClientOptions {
    BaseUri = new Uri("https://api.thunderduck.ai"), Token = token });

string id = await client.SubmitAsync("SELECT * FROM lake.events", ct);
await client.WaitAsync(id, TimeSpan.FromMinutes(10), ct);
await foreach (var page in client.ReadPagesAsync(id, pageSize: 5000, ct))
    Console.WriteLine($"{page.Offset}..{page.Offset + page.Rows.Count} of {page.Total}");
```

Pass your own `HttpClient`, for example one from `IHttpClientFactory`, in
`ThunderduckClientOptions.HttpClient`.

## Cancellation

Each of these stops waiting and cancels the query on the server:

- cancelling the `CancellationToken`;
- calling `DbCommand.Cancel()`, which works from another thread;
- the timeout running out.

A cancelled caller doesn't leave a job running.

## Browsing the catalog

`GetSchema()` reads the catalog API and doesn't run SQL. Collections:

| Collection | Columns | Restrictions |
|---|---|---|
| `MetaDataCollections` | `CollectionName`, `NumberOfRestrictions` | — |
| `Catalogs` | `CATALOG_NAME`, `CATALOG_TYPE`, `ERROR` | catalog |
| `Schemas` | `CATALOG_NAME`, `SCHEMA_NAME` | catalog, schema |
| `Tables` | `CATALOG_NAME`, `SCHEMA_NAME`, `TABLE_NAME`, `TABLE_TYPE` | catalog, schema, table |
| `Columns` | `CATALOG_NAME`, `SCHEMA_NAME`, `TABLE_NAME`, `COLUMN_NAME`, `ORDINAL_POSITION`, `DATA_TYPE` | catalog, schema, table, column |

```csharp
var columns = conn.GetSchema("Columns", new[] { "pg", "public", "orders" });
```

## Types

| DuckDB | .NET (`GetFieldType`) |
|---|---|
| BOOLEAN | `bool` |
| TINYINT / SMALLINT / INTEGER / BIGINT | `sbyte` / `short` / `int` / `long` |
| UTINYINT / USMALLINT / UINTEGER / UBIGINT | `byte` / `ushort` / `uint` / `ulong` |
| HUGEINT / UHUGEINT | `System.Numerics.BigInteger` |
| FLOAT / DOUBLE | `float` / `double` |
| DECIMAL(p,s) | `decimal` (exact) |
| DATE | `DateTime` (midnight); `GetFieldValue<DateOnly>` too |
| TIME | `TimeSpan`; `GetFieldValue<TimeOnly>` too |
| TIMESTAMP | `DateTime` (`Kind` Unspecified) |
| TIMESTAMP WITH TIME ZONE | `DateTime` (`Kind` Utc); `GetFieldValue<DateTimeOffset>` keeps the offset |
| UUID | `Guid` |
| BLOB | `byte[]` |
| VARCHAR / JSON / ENUM / INTERVAL | `string` |
| LIST / STRUCT / MAP | `string` (raw JSON), or `GetFieldValue<JsonElement>` |

As in Npgsql, DATE, TIME and TIMESTAMPTZ come back as `DateTime`/`TimeSpan`, so
Dapper and `DataTable` map them onto ordinary properties.

Parameters accept these .NET types plus `DateOnly`, `TimeOnly`, `DateTimeOffset` and `char`. The driver
inlines them into the SQL as typed DuckDB literals, because the API has no
server-side parameter binding.

## Limits

- Each query runs as a job, so expect seconds before the first row. Don't use it
  for tight OLTP loops.
- There are no transactions or batches, and `RecordsAffected` is always -1.
- Parameters are inlined into the SQL by the client as literals; the server
  doesn't bind them.
- Results are read from the stored result. Stored results expire after one day,
  and reading an expired one throws `ThunderduckException` with
  `Kind = ResultExpired`.

## Errors

Every failure is a `ThunderduckException` (a `DbException`) with:

- `Kind`: `Authentication`, `QueryFailed`, `Cancelled`, `Timeout`,
  `ResultExpired` or `Http`;
- `HttpStatus`;
- `ExecutionId`.

The message is the server's error. It never contains the token.

## License

Apache-2.0. See [LICENSE](LICENSE).

This repository is a mirror of Thunderduck's internal source; see
[CONTRIBUTING.md](CONTRIBUTING.md) for how issues and pull requests are handled,
and [SECURITY.md](SECURITY.md) to report a vulnerability.
