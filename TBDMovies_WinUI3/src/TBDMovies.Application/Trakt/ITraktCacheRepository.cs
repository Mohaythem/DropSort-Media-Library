using System;
using System.Collections.Generic;
using System.Threading;

namespace TBDMovies.Application.Trakt;

/// <summary>
/// Safe error enumeration for Trakt cache failures.
/// Raw error messages, tokens, and query strings must never be stored.
/// </summary>
public enum TraktCacheErrorCode
{
    None = 0,
    HttpError,
    NetworkError,
    RateLimited,
    Unauthorized,
    InvalidResponse,
    Timeout,
    Conflict,
    Cancelled,
    Unknown
}

/// <summary>
/// Immutable snapshot record for a cached Trakt resource.
/// Stores raw complete JSON snapshots without local catalog foreign keys.
/// </summary>
public sealed record TraktCacheEntry(
    string AccountId,
    string ResourceKey,
    string? PayloadJson,
    DateTimeOffset? SucceededAt,
    DateTimeOffset AttemptedAt,
    string? ErrorCode)
{
    public TraktCacheErrorCode? ParsedErrorCode =>
        Enum.TryParse<TraktCacheErrorCode>(ErrorCode, ignoreCase: true, out var parsed) ? parsed : null;
}

/// <summary>
/// Well-known resource keys for Trakt resource snapshots.
/// Separate keys are used for list catalogs, custom list items, watched records, and history.
/// </summary>
public static class TraktCacheResourceKeys
{
    public const string Lists = "lists";
    public const string WatchlistMovies = "watchlist:movies";
    public const string WatchlistShows = "watchlist:shows";
    public const string WatchedMovies = "watched:movies";
    public const string WatchedShows = "watched:shows";
    public const string HistoryMovies = "history:movies";
    public const string HistoryEpisodes = "history:episodes";

    public static string ListItems(string listId)
    {
        if (string.IsNullOrWhiteSpace(listId))
            throw new ArgumentException("List ID must be non-empty.", nameof(listId));
        return $"list:{listId.Trim()}:items";
    }
}

/// <summary>
/// Contract for isolated Trakt offline resource snapshot persistence.
/// </summary>
public sealed record TraktCacheSummary(string AccountId, string ResourceKey,
    DateTimeOffset? SucceededAt, DateTimeOffset AttemptedAt, string? ErrorCode);

public interface ITraktCacheRepository
{
    TraktCacheEntry? Get(string accountId, string resourceKey);
    IReadOnlyList<TraktCacheSummary> List(string accountId);
    void SaveComplete(string accountId, string resourceKey, string payloadJson, DateTimeOffset now, CancellationToken cancellationToken = default);
    void RecordFailure(string accountId, string resourceKey, TraktCacheErrorCode errorCode, DateTimeOffset now, CancellationToken cancellationToken = default);
    void DeleteAccount(string accountId, CancellationToken cancellationToken = default);
}
