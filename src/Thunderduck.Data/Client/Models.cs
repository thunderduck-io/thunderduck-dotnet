using System.Text.Json;

namespace Thunderduck.Client;

// Positional record properties need <param> docs, or CS1591 fails the build
// (TreatWarningsAsErrors + GenerateDocumentationFile).

/// <summary>A query execution's state.</summary>
/// <param name="Id">Execution id.</param>
/// <param name="Status">running, queued, success, error or cancelled.</param>
/// <param name="ErrorMessage">The server's error, when Status is error.</param>
/// <param name="ResultRowCount">Rows in the stored result, when known.</param>
public sealed record Execution(string Id, string Status, string? ErrorMessage, long? ResultRowCount);

/// <summary>One page of a stored result.</summary>
/// <param name="Columns">Column names.</param>
/// <param name="ColumnTypes">DuckDB type per column.</param>
/// <param name="Rows">Raw JSON cells, one array per row.</param>
/// <param name="Total">Total rows in the result.</param>
/// <param name="Offset">Offset of this page's first row.</param>
public sealed record ResultPage(
    IReadOnlyList<string> Columns,
    IReadOnlyList<string> ColumnTypes,
    IReadOnlyList<JsonElement[]> Rows,
    long Total,
    long Offset);

/// <summary>The company's catalogs as returned by <c>/catalog/schema</c>.</summary>
/// <param name="Catalogs">Every catalog, including unreachable ones (see <see cref="CatalogInfo.Error"/>).</param>
public sealed record CatalogTree(IReadOnlyList<CatalogInfo> Catalogs);

/// <summary>One catalog. Two-level catalogs (catalog.table) have one schema with a null name.</summary>
/// <param name="Name">Catalog name as used in SQL.</param>
/// <param name="Type">Catalog type (e.g. postgresql, nessie_iceberg).</param>
/// <param name="Schemas">Schemas (namespaces).</param>
/// <param name="Error">Why the catalog could not be read, if it could not.</param>
public sealed record CatalogInfo(string Name, string? Type, IReadOnlyList<SchemaInfo> Schemas, string? Error);

/// <summary>A schema (namespace) inside a catalog.</summary>
/// <param name="Name">Schema name; null for two-level catalogs.</param>
/// <param name="Tables">Tables and views.</param>
public sealed record SchemaInfo(string? Name, IReadOnlyList<TableInfo> Tables);

/// <summary>A table or view.</summary>
/// <param name="Name">Table name.</param>
/// <param name="Kind">table or view.</param>
/// <param name="Columns">Columns in order.</param>
public sealed record TableInfo(string Name, string Kind, IReadOnlyList<ColumnInfo> Columns);

/// <summary>A column and its DuckDB type.</summary>
/// <param name="Name">Column name.</param>
/// <param name="Type">DuckDB type.</param>
public sealed record ColumnInfo(string Name, string Type);
