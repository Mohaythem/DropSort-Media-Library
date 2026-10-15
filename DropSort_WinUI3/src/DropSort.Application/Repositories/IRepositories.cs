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

    /// <summary>Sets the persisted "review match recommended" flag without touching metadata or timestamps.</summary>
    void SetReviewMatch(int id, bool value);
    void RecordOpened(int id, DateTimeOffset openedAt);
    void Delete(int id);
}

public interface IMediaFileRepository
{
    MediaFile? GetById(int id);
    MediaFile? GetByPath(string path);
    IReadOnlyList<MediaFile> GetByMovieId(int movieId);
    IReadOnlyList<MediaFile> ListAll();
    IReadOnlyList<MediaFile> ListMissing();

    /// <summary>
    /// The ids of movies that have at least one PRESENT media file, in a single query. Library uses this
    /// to compute the "no local file" state for the whole grid without a per-movie query.
    /// </summary>
    IReadOnlySet<int> ListMovieIdsWithPresentFile();

    /// <summary>
    /// How many media files each movie owns, in one query, so the Library grid can show a multi-file
    /// indicator without a per-movie lookup (avoids the ListMovies N+1). Movies with no files are absent.
    /// </summary>
    IReadOnlyDictionary<int, int> CountFilesByMovie();
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

    /// <summary>Every custom list, in creation order, each with its current member count (movies + shows).</summary>
    IReadOnlyList<CustomListRecord> ListCustomLists();

    /// <summary>
    /// Creates a custom list and returns its stable id. Throws
    /// <see cref="DropSort.Application.DuplicateCustomListNameException" /> when the trimmed name matches an
    /// existing list case-insensitively.
    /// </summary>
    int CreateCustomList(string name, DateTimeOffset now);

    /// <summary>
    /// Renames a custom list. Identity and membership are unchanged. Throws
    /// <see cref="DropSort.Application.DuplicateCustomListNameException" /> when another list already uses
    /// the name.
    /// </summary>
    void RenameCustomList(int listId, string name, DateTimeOffset now);

    /// <summary>Deletes a custom list and its membership rows. Media items and other lists are untouched.</summary>
    void DeleteCustomList(int listId);

    /// <summary>Adds a movie to a custom list. Idempotent: a movie already in the list is a no-op.</summary>
    void AddMovieToCustomList(int listId, int movieId, DateTimeOffset now);

    /// <summary>Removes a movie from a custom list. A movie not in the list is a no-op.</summary>
    void RemoveMovieFromCustomList(int listId, int movieId);

    /// <summary>The ids of the custom lists a movie currently belongs to.</summary>
    IReadOnlySet<int> GetCustomListIdsForMovie(int movieId);

    /// <summary>Adds a TV show to a custom list. Idempotent: a show already in the list is a no-op.</summary>
    void AddShowToCustomList(int listId, int showId, DateTimeOffset now);

    /// <summary>Removes a TV show from a custom list. A show not in the list is a no-op.</summary>
    void RemoveShowFromCustomList(int listId, int showId);

    /// <summary>The ids of the custom lists a TV show currently belongs to.</summary>
    IReadOnlySet<int> GetCustomListIdsForShow(int showId);

    /// <summary>The members of a custom list (movies and TV shows), newest membership first.</summary>
    IReadOnlyList<CustomListMember> ListCustomListItems(int listId, int limit = 1000, int offset = 0);
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

public interface IViewingActivityRepository
{
    IReadOnlyList<DropSort.Application.Dto.ActiveViewingItem> ListActive(int limit);
    DropSort.Application.Dto.ActiveViewingItem RecordSuccessfulPlay(string path, DateTimeOffset startedAt);
    IReadOnlyList<WatchHistoryEntry> ListRecentCompleted(int limit, int offset = 0);
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
