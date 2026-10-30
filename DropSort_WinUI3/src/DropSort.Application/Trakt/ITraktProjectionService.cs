using System.Collections.Generic;
using System.Threading;

namespace DropSort.Application.Trakt;

/// <summary>
/// Projects raw remote Trakt models into presentation-ready rows with local library matching,
/// independent file availability, and exact watched badges.
/// </summary>
public interface ITraktProjectionService
{
    /// <summary>
    /// Projects custom list items into ordered presentation rows.
    /// Retains all raw payload fields, notes, list rank, and unknown/person/missing-title items.
    /// </summary>
    IReadOnlyList<TraktProjectedListItem> ProjectListItems(
        IEnumerable<TraktListItem> items,
        TraktLocalSnapshot snapshot,
        TraktWatchedContext? watchedContext = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Projects watchlist items into ordered presentation rows.
    /// Retains list rank, raw payload fields, and matches local catalog items for navigation.
    /// </summary>
    IReadOnlyList<TraktProjectedWatchlistItem> ProjectWatchlist(
        IEnumerable<TraktWatchlistItem> items,
        TraktLocalSnapshot snapshot,
        TraktWatchedContext? watchedContext = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Projects playback history events into chronological rows.
    /// Preserves repeated history event identifiers and maps matched local catalog items.
    /// </summary>
    IReadOnlyList<TraktProjectedHistoryItem> ProjectHistory(
        IEnumerable<TraktHistoryItem> items,
        TraktLocalSnapshot snapshot,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Projects watched movie records with play counts and local availability.
    /// </summary>
    IReadOnlyList<TraktProjectedWatchedMovie> ProjectWatchedMovies(
        IEnumerable<TraktWatchedMovie> movies,
        TraktLocalSnapshot snapshot,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Projects watched TV shows with watched episode counts, season/episode hierarchy, and availability.
    /// </summary>
    IReadOnlyList<TraktProjectedWatchedShow> ProjectWatchedShows(
        IEnumerable<TraktWatchedShow> shows,
        TraktLocalSnapshot snapshot,
        CancellationToken cancellationToken = default);
}
