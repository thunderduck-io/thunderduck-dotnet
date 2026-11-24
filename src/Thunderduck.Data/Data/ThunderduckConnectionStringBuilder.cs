using System.Data.Common;
using System.Globalization;

namespace Thunderduck.Data;

/// <summary>Builds/parses <c>Host=…;Token=…;Ssl=true;Port=…;PollInterval=1000;Timeout=300;PageSize=1000</c>.</summary>
public sealed class ThunderduckConnectionStringBuilder : DbConnectionStringBuilder
{
    /// <summary>Empty builder.</summary>
    public ThunderduckConnectionStringBuilder() { }
    /// <summary>Parse a connection string.</summary>
    public ThunderduckConnectionStringBuilder(string? connectionString) => ConnectionString = connectionString ?? "";

    /// <summary>API host, e.g. api.thunderduck.ai.</summary>
    public string Host { get => Get("Host") ?? ""; set => this["Host"] = value; }
    /// <summary>Optional port.</summary>
    public int? Port { get => Get("Port") is { } p ? int.Parse(p, CultureInfo.InvariantCulture) : null; set => this["Port"] = value; }
    /// <summary>API token (tdk_…).</summary>
    public string Token { get => Get("Token") ?? ""; set => this["Token"] = value; }
    /// <summary>HTTPS (default true).</summary>
    public bool Ssl { get => Get("Ssl") is not { } s || bool.Parse(s); set => this["Ssl"] = value; }
    /// <summary>Poll interval in milliseconds (default 1000).</summary>
    public int PollIntervalMs { get => Int("PollInterval", 1000); set => this["PollInterval"] = value; }
    /// <summary>Default query timeout (seconds in the string; default 300).</summary>
    public TimeSpan Timeout { get => TimeSpan.FromSeconds(Int("Timeout", 300)); set => this["Timeout"] = (int)value.TotalSeconds; }
    /// <summary>Rows fetched per page (default 1000).</summary>
    public int PageSize { get => Int("PageSize", 1000); set => this["PageSize"] = value; }

    /// <summary>The API base URI.</summary>
    public Uri BaseUri => new UriBuilder(Ssl ? "https" : "http", Host, Port ?? -1).Uri;

    private string? Get(string key) => TryGetValue(key, out var v) && v is not null && $"{v}" != "" ? Convert.ToString(v, CultureInfo.InvariantCulture) : null;
    private int Int(string key, int fallback) => Get(key) is { } v ? int.Parse(v, CultureInfo.InvariantCulture) : fallback;
}
