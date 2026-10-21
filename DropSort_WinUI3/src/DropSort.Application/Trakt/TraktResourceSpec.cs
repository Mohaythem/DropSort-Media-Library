using System;

namespace DropSort.Application.Trakt;

/// <summary>
/// Supported read-only resource types for Trakt sync.
/// </summary>
public enum TraktResourceType
{
    UserLists,
    ListItems,
    Watchlist,
    WatchedMovies,
    WatchedShows,
    History
}

/// <summary>
/// Immutable specification for whitelisted Trakt GET resources.
/// Prevents arbitrary URL construction and validates resource identifiers.
/// </summary>
public readonly record struct TraktResourceSpec
{
    public TraktResourceType ResourceType { get; }

    public long? CustomListId { get; }

    private TraktResourceSpec(TraktResourceType resourceType, long? customListId = null)
    {
        ResourceType = resourceType;
        CustomListId = customListId;
    }

    /// <summary>
    /// Creates a specification for user custom list headers.
    /// </summary>
    public static TraktResourceSpec UserLists() =>
        new(TraktResourceType.UserLists);

    /// <summary>
    /// Creates a specification for items within a specific custom list.
    /// Validates that list identifier is a positive integer.
    /// </summary>
    /// <param name="listId">Numeric positive Trakt list identifier.</param>
    public static TraktResourceSpec ListItems(long listId)
    {
        if (listId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(listId),
                listId,
                "Custom list identifier must be a positive integer.");
        }

        return new(TraktResourceType.ListItems, listId);
    }

    /// <summary>
    /// Creates a specification for user watchlist items.
    /// </summary>
    public static TraktResourceSpec Watchlist() =>
        new(TraktResourceType.Watchlist);

    /// <summary>
    /// Creates a specification for watched movie records.
    /// </summary>
    public static TraktResourceSpec WatchedMovies() =>
        new(TraktResourceType.WatchedMovies);

    /// <summary>
    /// Creates a specification for watched show records with seasons.
    /// </summary>
    public static TraktResourceSpec WatchedShows() =>
        new(TraktResourceType.WatchedShows);

    /// <summary>
    /// Creates a specification for chronological watch history events.
    /// </summary>
    public static TraktResourceSpec History() =>
        new(TraktResourceType.History);

    /// <summary>
    /// Returns the whitelisted relative path for the resource.
    /// </summary>
    public string GetRelativePath() => ResourceType switch
    {
        TraktResourceType.UserLists => "users/me/lists",
        TraktResourceType.ListItems => $"users/me/lists/{CustomListId!.Value}/items",
        TraktResourceType.Watchlist => "sync/watchlist",
        TraktResourceType.WatchedMovies => "sync/watched/movies",
        TraktResourceType.WatchedShows => "sync/watched/shows",
        TraktResourceType.History => "sync/history",
        _ => throw new InvalidOperationException($"Unsupported resource type {ResourceType}.")
    };

    /// <summary>
    /// Indicates if this endpoint requires pagination verification headers.
    /// </summary>
    public bool RequiresPaginationHeaders() => ResourceType switch
    {
        TraktResourceType.UserLists => false, // Catalog may return unpaginated; verifies headers if present.
        TraktResourceType.ListItems => true,
        TraktResourceType.Watchlist => true,
        TraktResourceType.WatchedMovies => true,
        TraktResourceType.WatchedShows => true,
        TraktResourceType.History => true,
        _ => true
    };

    /// <summary>
    /// Computes the persistent cache resource key.
    /// </summary>
    public string ToCacheResourceKey() => ResourceType switch
    {
        TraktResourceType.UserLists => "lists",
        TraktResourceType.ListItems => $"list:{CustomListId!.Value}:items",
        TraktResourceType.Watchlist => "watchlist",
        TraktResourceType.WatchedMovies => "watched:movies",
        TraktResourceType.WatchedShows => "watched:shows",
        TraktResourceType.History => "history",
        _ => throw new InvalidOperationException($"Unsupported resource type {ResourceType}.")
    };
}
