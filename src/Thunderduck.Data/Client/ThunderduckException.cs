using System.Data.Common;

namespace Thunderduck.Client;

/// <summary>What went wrong.</summary>
public enum ThunderduckErrorKind
{
    /// <summary>The token was rejected (HTTP 401/403).</summary>
    Authentication,
    /// <summary>The query ran and failed; the message is the server's error.</summary>
    QueryFailed,
    /// <summary>The query was cancelled (by this client or elsewhere).</summary>
    Cancelled,
    /// <summary>The query did not finish within the timeout (it was cancelled).</summary>
    Timeout,
    /// <summary>The stored result expired; re-run the query.</summary>
    ResultExpired,
    /// <summary>Any other HTTP failure.</summary>
    Http,
}

/// <summary>An error from thunderduck. Never contains the API token.</summary>
public sealed class ThunderduckException : DbException
{
    /// <summary>Create an exception.</summary>
    public ThunderduckException(ThunderduckErrorKind kind, string message, int? httpStatus = null,
        string? executionId = null, Exception? inner = null) : base(message, inner)
    {
        Kind = kind;
        HttpStatus = httpStatus;
        ExecutionId = executionId;
    }

    /// <summary>Error category.</summary>
    public ThunderduckErrorKind Kind { get; }
    /// <summary>HTTP status, when the error came from a response.</summary>
    public int? HttpStatus { get; }
    /// <summary>The execution the error belongs to, when known.</summary>
    public string? ExecutionId { get; }
}
