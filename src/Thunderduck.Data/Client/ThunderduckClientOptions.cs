namespace Thunderduck.Client;

/// <summary>Settings for <see cref="ThunderduckClient"/>.</summary>
public sealed class ThunderduckClientOptions
{
    /// <summary>API base, e.g. <c>https://api.thunderduck.ai</c>.</summary>
    public required Uri BaseUri { get; init; }
    /// <summary>API token (<c>tdk_…</c>) created in the console.</summary>
    public required string Token { get; init; }
    /// <summary>How often a running query is polled.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1);
    /// <summary>Default upper bound for waiting on a query.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(5);
    /// <summary>Optional caller-managed client (e.g. from IHttpClientFactory).</summary>
    public HttpClient? HttpClient { get; init; }

    /// <inheritdoc/>
    public override string ToString() => $"ThunderduckClientOptions {{ BaseUri = {BaseUri} }}";
}
