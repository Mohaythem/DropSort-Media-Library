using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TBDMovies.Application.Trakt;

/// <summary>
/// Unified UI coordinator contract for Trakt authentication, background synchronization,
/// and offline cache access. Enforces a single coordinator gate, generation isolation,
/// and active-session boundary enforcement.
/// </summary>
public interface ITraktUiActions
{
    /// <summary>
    /// Raised whenever Trakt synchronization completes and the local cache has been updated.
    /// </summary>
    event System.EventHandler? SyncCompleted;

    /// <summary>
    /// Gets the current immutable synchronization and authentication state snapshot.
    /// Thread-safe and lock-free.
    /// </summary>
    TraktSyncState GetSyncState();

    /// <summary>
    /// Initiates Trakt device code authorization. Cancels any active synchronization.
    /// </summary>
    Task<TraktDeviceCode> BeginAuthAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Completes Trakt device authorization by polling for user confirmation.
    /// </summary>
    Task<TraktAuthState> CompleteAuthAsync(TraktDeviceCode deviceCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnects Trakt account, immediately cancels all active network operations,
    /// clears secret credentials, and publishes disconnected state.
    /// Retains explicit last account offline cache.
    /// </summary>
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes full Trakt read-only sync: verifies account UUID, fetches user lists,
    /// positive numeric list items, unfiltered watchlist, watched movies, watched shows,
    /// and raw history into cache.
    /// </summary>
    Task<TraktSyncResult> SyncAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves lightweight cache summaries for the specified account or active/last account.
    /// Filters out orphan deleted-list item rows.
    /// </summary>
    IReadOnlyList<TraktCacheSummary> GetCacheSummaries(string? accountId = null);

    /// <summary>
    /// Retrieves full custom list records for the specified account or active/last account.
    /// Preserves raw element order and custom fields.
    /// </summary>
    IReadOnlyList<TraktCustomList> GetLists(string? accountId = null);

    /// <summary>
    /// Retrieves all items for a custom list. Preserves raw element order, 64-bit IDs,
    /// and unknown/person entries. Returns empty if list was deleted or orphaned.
    /// </summary>
    IReadOnlyList<TraktListItem> GetListItems(long listId, string? accountId = null);

    /// <summary>
    /// Retrieves all unfiltered watchlist items.
    /// </summary>
    IReadOnlyList<TraktWatchlistItem> GetWatchlist(string? accountId = null);

    /// <summary>
    /// Retrieves all watched movies.
    /// </summary>
    IReadOnlyList<TraktWatchedMovie> GetWatchedMovies(string? accountId = null);

    /// <summary>
    /// Retrieves all watched shows with nested seasons and episode counts.
    /// </summary>
    IReadOnlyList<TraktWatchedShow> GetWatchedShows(string? accountId = null);

    /// <summary>
    /// Retrieves raw watch history events.
    /// </summary>
    IReadOnlyList<TraktHistoryItem> GetHistory(string? accountId = null);

    /// <summary>
    /// Retrieves the raw cache entry for a specific resource key without modifying state.
    /// </summary>
    TraktCacheEntry? GetRawResource(string resourceKey, string? accountId = null);
}
