using System.Data;
using Thunderduck.Client;

namespace Thunderduck.Data;

/// <summary>GetSchema() from the catalog API — no SQL is run.</summary>
internal static class SchemaCollections
{
    private static readonly string[] Names = ["MetaDataCollections", "Catalogs", "Schemas", "Tables", "Columns"];

    public static DataTable Get(ThunderduckConnection conn, string collection, string?[] r)
    {
        if (!Names.Contains(collection, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"Unknown schema collection '{collection}'. Available: {string.Join(", ", Names)}.");
        if (string.Equals(collection, "MetaDataCollections", StringComparison.OrdinalIgnoreCase))
        {
            var meta = Table("MetaDataCollections", "CollectionName", "NumberOfRestrictions");
            meta.Rows.Add("MetaDataCollections", 0);
            meta.Rows.Add("Catalogs", 1);
            meta.Rows.Add("Schemas", 2);
            meta.Rows.Add("Tables", 3);
            meta.Rows.Add("Columns", 4);
            return meta;
        }

        var tree = conn.Client.GetCatalogAsync().GetAwaiter().GetResult();
        string? R(int i) => i < r.Length ? r[i] : null;
        bool M(string? restriction, string? value) => restriction is null || string.Equals(restriction, value, StringComparison.Ordinal);
        object N(string? s) => (object?)s ?? DBNull.Value;

        switch (collection.ToLowerInvariant())
        {
            case "catalogs":
            {
                var t = Table("Catalogs", "CATALOG_NAME", "CATALOG_TYPE", "ERROR");
                foreach (var c in tree.Catalogs.Where(c => M(R(0), c.Name)))
                    t.Rows.Add(c.Name, N(c.Type), N(c.Error));
                return t;
            }
            case "schemas":
            {
                var t = Table("Schemas", "CATALOG_NAME", "SCHEMA_NAME");
                foreach (var c in tree.Catalogs.Where(c => M(R(0), c.Name)))
                    foreach (var s in c.Schemas.Where(s => M(R(1), s.Name)))
                        t.Rows.Add(c.Name, N(s.Name));
                return t;
            }
            case "tables":
            {
                var t = Table("Tables", "CATALOG_NAME", "SCHEMA_NAME", "TABLE_NAME", "TABLE_TYPE");
                foreach (var (c, s, tb) in Each(tree, R(0), R(1), R(2), M))
                    t.Rows.Add(c.Name, N(s.Name), tb.Name, tb.Kind.ToUpperInvariant() == "VIEW" ? "VIEW" : "BASE TABLE");
                return t;
            }
            default: // columns
            {
                var t = Table("Columns", "CATALOG_NAME", "SCHEMA_NAME", "TABLE_NAME", "COLUMN_NAME", "ORDINAL_POSITION", "DATA_TYPE");
                t.Columns["ORDINAL_POSITION"]!.DataType = typeof(int);
                foreach (var (c, s, tb) in Each(tree, R(0), R(1), R(2), M))
                    for (var i = 0; i < tb.Columns.Count; i++)
                        if (M(R(3), tb.Columns[i].Name))
                            t.Rows.Add(c.Name, N(s.Name), tb.Name, tb.Columns[i].Name, i + 1, tb.Columns[i].Type);
                return t;
            }
        }
    }

    private static IEnumerable<(CatalogInfo, SchemaInfo, TableInfo)> Each(CatalogTree tree, string? cat, string? sch, string? tab,
        Func<string?, string?, bool> m) =>
        from c in tree.Catalogs where m(cat, c.Name)
        from s in c.Schemas where m(sch, s.Name)
        from t in s.Tables where m(tab, t.Name)
        select (c, s, t);

    private static DataTable Table(string name, params string[] columns)
    {
        var t = new DataTable(name);
        foreach (var c in columns) t.Columns.Add(c, typeof(object));
        return t;
    }
}
