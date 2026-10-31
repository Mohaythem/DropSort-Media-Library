using System;
using TBDMovies.Domain.Library.Movies;

namespace TBDMovies.Domain.Library.Personal;

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

/// <summary>A durable historical watch entry. Snapshot fields keep history useful after catalog removal.</summary>
public sealed record WatchHistoryEntry
{
    public int Id { get; }
    public WatchEventMediaKind MediaKind { get; }
    public int? MovieId { get; }
    public int? EpisodeId { get; }
    public int? ShowId { get; }
    public string TitleSnapshot { get; }
    public int? YearSnapshot { get; }
    public string? ShowTitleSnapshot { get; }
    public int? SeasonNumberSnapshot { get; }
    public int? EpisodeNumberSnapshot { get; }
    public string? ArtworkReference { get; }
    public string? BackdropReference { get; }
    public string? PosterReference { get; }
    public DateTimeOffset WatchedAt { get; }
    public DateTimeOffset CreatedAt { get; }
    public bool IsAvailable { get; }

    public WatchHistoryEntry(
        int id,
        WatchEventMediaKind mediaKind,
        int? movieId,
        int? episodeId,
        int? showId,
        string titleSnapshot,
        int? yearSnapshot,
        string? showTitleSnapshot,
        int? seasonNumberSnapshot,
        int? episodeNumberSnapshot,
        string? artworkReference,
        string? backdropReference,
        string? posterReference,
        DateTimeOffset watchedAt,
        DateTimeOffset createdAt,
        bool isAvailable)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
        if (!Enum.IsDefined(mediaKind)) throw new ArgumentOutOfRangeException(nameof(mediaKind));
        if (movieId is not null && episodeId is not null)
            throw new ArgumentException("A watch entry cannot reference both a movie and an episode.");
        if (mediaKind == WatchEventMediaKind.Movie && episodeId is not null)
            throw new ArgumentException("A movie watch entry cannot reference an episode.", nameof(episodeId));
        if (mediaKind == WatchEventMediaKind.Episode && movieId is not null)
            throw new ArgumentException("An episode watch entry cannot reference a movie.", nameof(movieId));
        if (string.IsNullOrWhiteSpace(titleSnapshot)) throw new ArgumentException("A title snapshot is required.", nameof(titleSnapshot));
        TitleSnapshot = titleSnapshot.Trim();
        Id = id;
        MediaKind = mediaKind;
        MovieId = movieId;
        EpisodeId = episodeId;
        ShowId = showId;
        YearSnapshot = yearSnapshot;
        ShowTitleSnapshot = showTitleSnapshot?.Trim();
        SeasonNumberSnapshot = seasonNumberSnapshot;
        EpisodeNumberSnapshot = episodeNumberSnapshot;
        ArtworkReference = artworkReference?.Trim();
        BackdropReference = backdropReference?.Trim();
        PosterReference = posterReference?.Trim();
        WatchedAt = watchedAt;
        CreatedAt = createdAt;
        IsAvailable = isAvailable;
    }
}

public enum WatchEventMediaKind
{
    Movie,
    Episode
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

/// <summary>
/// A user-created custom list. Identity is the stable <paramref name="Id" />; <paramref name="Name" /> is
/// display-only, so a rename never changes identity or loses membership. <paramref name="ItemCount" /> is
/// the number of members in the list, counting movies and TV shows together.
/// </summary>
public record CustomListRecord(int Id, string Name, int ItemCount);

/// <summary>The kind of media a custom-list member is.</summary>
public enum CustomListMediaKind
{
    Movie,
    Show
}

/// <summary>
/// One member of a custom list, movie or TV show, with just what a list card needs to render and to
/// navigate. <paramref name="IsMissing" /> means no present local file (a movie with no present file, or a
/// show with no present episode file).
/// </summary>
public record CustomListMember(
    CustomListMediaKind Kind,
    int Id,
    string Title,
    int? Year,
    string? PosterReference,
    bool IsMissing,
    string? CustomDisplayTitle = null,
    string? CanonicalTitle = null);
