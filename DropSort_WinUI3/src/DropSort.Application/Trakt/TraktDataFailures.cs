using System;
using System.Net;

namespace DropSort.Application.Trakt;

/// <summary>
/// Categorized failure kinds for Trakt HTTP data operations.
/// </summary>
public enum TraktFailureKind
{
    /// <summary>
    /// HTTP 401 Unauthorized status. Caller must acquire new authentication token.
    /// </summary>
    Unauthorized,

    /// <summary>
    /// HTTP 429 Too Many Requests status after retry limits or retry budget expired.
    /// </summary>
    RateLimited,

    /// <summary>
    /// Network connection failure, DNS resolution error, or transport timeout.
    /// </summary>
    Network,

    /// <summary>
    /// Payload violation, missing pagination headers, data count mismatch, or payload size limit reached.
    /// </summary>
    InvalidResponse,

    /// <summary>
    /// Unexpected HTTP status code such as 403, 404, 420, 500, or HTTP redirect.
    /// </summary>
    HttpError
}

/// <summary>
/// Sanitized exception for Trakt data client failures.
/// Never exposes OAuth tokens or sensitive response payloads.
/// </summary>
public sealed class TraktClientException : Exception
{
    public TraktFailureKind FailureKind { get; }

    public HttpStatusCode? StatusCode { get; }

    public string Endpoint { get; }

    public TraktClientException(
        TraktFailureKind failureKind,
        string message,
        string endpoint,
        HttpStatusCode? statusCode = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        FailureKind = failureKind;
        Endpoint = endpoint;
        StatusCode = statusCode;
    }

    public override string ToString() =>
        $"[TraktClientException Kind={FailureKind}, Endpoint={Endpoint}, StatusCode={StatusCode}]: {Message}";
}
