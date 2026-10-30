using System;

namespace DropSort.Infrastructure.Trakt;

/// <summary>
/// Operational limits and configuration bounds for Trakt HTTP data transport.
/// </summary>
public sealed record TraktDataClientOptions(
    int MaxResponseBytesPerPage = TraktDataConfig.DefaultMaxBytesPerPage,
    long MaxCumulativeResponseBytes = TraktDataConfig.DefaultMaxCumulativeBytes,
    int MaxAllowedPages = TraktDataConfig.DefaultMaxAllowedPages,
    int MaxRateLimitRetries = TraktDataConfig.DefaultMaxRateLimitRetries,
    TimeSpan? MaxRateLimitDelay = null)
{
    public TimeSpan MaxRateLimitDelayOrDefault =>
        MaxRateLimitDelay ?? TimeSpan.FromSeconds(TraktDataConfig.DefaultMaxRetryDelaySeconds);
}

/// <summary>
/// Fixed API configuration constants for Trakt data client.
/// </summary>
public static class TraktDataConfig
{
    /// <summary>
    /// Fixed standard production API host.
    /// </summary>
    public const string ApiBaseUrl = "https://api.trakt.tv";

    /// <summary>
    /// Standard Trakt API version required in request headers.
    /// </summary>
    public const string ApiVersion = "2";

    /// <summary>
    /// Standard maximum page limit for paginated sync endpoints.
    /// </summary>
    public const int DefaultPaginationLimit = 250;

    /// <summary>
    /// Maximum safe bytes allowed per single response page (10 Megabytes).
    /// </summary>
    public const int DefaultMaxBytesPerPage = 10 * 1024 * 1024;

    /// <summary>
    /// Maximum safe cumulative bytes allowed per complete resource query (100 Megabytes).
    /// </summary>
    public const long DefaultMaxCumulativeBytes = 100L * 1024 * 1024;

    /// <summary>
    /// Maximum safe page count permitted before failing closed (500 pages = 125,000 items at limit 250).
    /// </summary>
    public const int DefaultMaxAllowedPages = 500;

    /// <summary>
    /// Maximum retry attempts for HTTP 429 rate limit responses.
    /// </summary>
    public const int DefaultMaxRateLimitRetries = 3;

    /// <summary>
    /// Maximum wait delay permitted for HTTP 429 retry before failing closed.
    /// </summary>
    public const int DefaultMaxRetryDelaySeconds = 60;
}
