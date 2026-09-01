using System;
using DropSort.Domain.Library.Movies;

namespace DropSort.Domain.Library.Personal;

public enum PersonalPreference
{
    NoOpinion,
    Liked,
    Blacklisted
}

public enum PersonalLibrarySection
{
    Watchlist,
    ReadyToWatch,
    Liked,
    Blacklisted
}

public record PersonalMovieState
{
    public int MovieId { get; }
    public PersonalPreference Preference { get; }
    public DateTimeOffset? WatchlistAddedAt { get; }
    public int WatchCount { get; }
    public DateTimeOffset? LastWatched { get; }
    public DateTimeOffset? CreatedAt { get; }
    public DateTimeOffset? UpdatedAt { get; }

    public PersonalMovieState(
        int movieId,
        PersonalPreference preference,
        DateTimeOffset? watchlistAddedAt,
        int watchCount,
        DateTimeOffset? lastWatched,
        DateTimeOffset? createdAt,
        DateTimeOffset? updatedAt)
    {
        if (movieId <= 0) throw new ArgumentOutOfRangeException(nameof(movieId));
        if (!Enum.IsDefined(preference)) throw new ArgumentOutOfRangeException(nameof(preference));
        if (watchCount < 0) throw new ArgumentOutOfRangeException(nameof(watchCount));
        if (watchCount == 0 && lastWatched is not null)
            throw new ArgumentException("lastWatched requires at least one watch event.", nameof(lastWatched));
        if (watchCount > 0 && lastWatched is null)
            throw new ArgumentException("A positive watchCount requires lastWatched.", nameof(lastWatched));
        MovieId = movieId;
        Preference = preference;
        WatchlistAddedAt = watchlistAddedAt;
        WatchCount = watchCount;
        LastWatched = lastWatched;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public bool IsWatchlisted => WatchlistAddedAt.HasValue;
    public bool Watched => WatchCount > 0;
}

public sealed record WatchEvent
{
    public int Id { get; }
    public int MovieId { get; }
    public DateTimeOffset WatchedAt { get; }
    public bool Rewatch { get; }
    public DateTimeOffset? CreatedAt { get; }

    public WatchEvent(int id, int movieId, DateTimeOffset watchedAt, bool rewatch, DateTimeOffset? createdAt = null)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
        if (movieId <= 0) throw new ArgumentOutOfRangeException(nameof(movieId));
        Id = id;
        MovieId = movieId;
        WatchedAt = watchedAt;
        Rewatch = rewatch;
        CreatedAt = createdAt;
    }
}

public record ReadyToWatchMovie(
    int MovieId,
    Movie Movie,
    int PresentMediaFileCount,
    DateTimeOffset WatchlistAddedAt);

public record PersonalMovieSummary(
    Movie Movie,
    int MediaFileCount,
    int MissingFileCount,
    PersonalPreference Preference,
    bool Watchlisted,
    int WatchCount,
    DateTimeOffset? LastWatched);
