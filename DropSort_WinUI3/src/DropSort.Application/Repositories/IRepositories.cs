using System;
using System.Collections.Generic;
using DropSort.Domain.Library.Movies;
using DropSort.Domain.Library.Personal;
using DropSort.Domain.Core.Operations;

namespace DropSort.Application.Repositories;

public interface IMovieRepository
{
    Movie? GetById(int id);
    Movie? GetByExternalId(string provider, string externalId);
    IReadOnlyList<Movie> ListAll();
    IReadOnlyList<Movie> ListPage(int afterId, int limit);
    int CountAll();
    Movie Create(MovieCatalogData data, DateTimeOffset now);
    Movie UpdateMetadata(int id, MovieCatalogData data, DateTimeOffset now);
    void Delete(int id);
}

public interface IMediaFileRepository
{
    MediaFile? GetById(int id);
    MediaFile? GetByPath(string path);
    IReadOnlyList<MediaFile> GetByMovieId(int movieId);
    IReadOnlyList<MediaFile> ListAll();
    IReadOnlyList<MediaFile> ListMissing();
    MediaFile Add(VerifiedMediaFileFacts facts, int? movieId = null);
    MediaFile LinkToMovie(int id, int movieId);
    MediaFile RefreshVerifiedFacts(int id, VerifiedMediaFileFacts facts);
    MediaFile MarkMissing(int id, DateTimeOffset observedAt);
    MediaFile Relink(int id, string expectedPath, VerifiedMediaFileFacts facts);
    void Delete(int id);
}

public interface IFileOperationStore
{
    FileOperationRecord? GetById(string id);
    IReadOnlyList<FileOperationRecord> ListHistory(int limit = 50, int offset = 0);
    IReadOnlyList<FileOperationRecord> ListNonterminal();
    FileOperationRecord Create(FileOperationPlan plan, DateTimeOffset now);
    FileOperationRecord Transition(string id, OperationState state, OperationUpdate? update = null);
    FileOperationRecord CommitVerified(string id);
}

public interface IPersonalLibraryRepository
{
    PersonalMovieState GetState(int movieId);
    PersonalMovieState SetPreference(int movieId, PersonalPreference preference, DateTimeOffset now);
    PersonalMovieState ClearPreference(int movieId, DateTimeOffset now);
    PersonalMovieState AddToWatchlist(int movieId, DateTimeOffset now);
    PersonalMovieState RemoveFromWatchlist(int movieId, DateTimeOffset now);
    WatchEvent AddWatchEvent(int movieId, DateTimeOffset watchedAt, DateTimeOffset createdAt);
    WatchEvent DeleteWatchEvent(int eventId);
    IReadOnlyList<WatchEvent> GetWatchEvents(int movieId);
    IReadOnlyList<PersonalMovieSummary> ListMovies(PersonalLibrarySection section, int limit = 100, int offset = 0);
}

public interface IWatchHistoryRepository
{
    WatchHistoryEntry AddEpisodeWatchEvent(int episodeId, DateTimeOffset watchedAt, DateTimeOffset createdAt);
    IReadOnlyList<WatchHistoryEntry> ListRecent(int limit = 50, int offset = 0);

    /// <summary>
    /// The most recently watched distinct media, newest first: each logical movie (by movie_id) and each
    /// logical TV show (by its parent show_id, so every episode of a show shares one entry) appears once,
    /// represented by its latest watch event, and the list is ordered by that latest event. The raw
    /// watch_events history is untouched; this only collapses the read for "Recently watched", which shows
    /// media, not individual replays. Episodes orphaned from a deleted show cannot be grouped and each
    /// stays distinct.
    /// </summary>
    IReadOnlyList<WatchHistoryEntry> ListRecentlyWatchedMedia(int limit = 50, int offset = 0);
    IReadOnlySet<int> GetWatchedEpisodeIds(int showId);

    /// <summary>
    /// Removes every watch event - movie, episode and events orphaned by deleted media - and reports
    /// how many rows were deleted. Personal preferences and the watchlist are untouched.
    /// </summary>
    int ClearWatchHistory();
}

public interface ILibraryMaintenanceRepository
{
    /// <summary>
    /// Empties the local index - movies, media files, metadata, personal state and the whole TV
    /// hierarchy - in one transaction, and reports what was removed. Catalog rows only: not one media
    /// file on disk is touched.
    /// </summary>
    (int Movies, int MediaFiles, int MetadataEntries, int Shows, int Seasons, int Episodes) ClearCatalog();
}

public interface ISettingsRepository
{
    string? Get(string key);
    void Set(string key, string value, DateTimeOffset updatedAt);
    void Delete(string key);
}

public interface ICatalogUnitOfWork : IDisposable
{
    IMovieRepository Movies { get; }
    IMediaFileRepository MediaFiles { get; }

    /// <summary>The TV half of the catalog, in the same transaction as the movie half.</summary>
    ITvShowRepository TvShows { get; }

    ITvSeasonRepository TvSeasons { get; }

    ITvEpisodeRepository TvEpisodes { get; }

    void Commit();
}

public interface ICatalogUnitOfWorkFactory
{
    ICatalogUnitOfWork Begin();
}
