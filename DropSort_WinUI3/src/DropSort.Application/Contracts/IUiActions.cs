using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DropSort.Application.Dto;
using DropSort.Application.External;
using DropSort.Domain.Configuration;
using DropSort.Domain.Library.Personal;
using DropSort.Domain.Media.Discovery;

namespace DropSort.Application.Contracts;

public interface ILibraryUiActions
{
    IReadOnlyList<MovieListItem> ListMovies();
    MovieListItem GetMovieItem(int movieId);
    MovieDetails GetMovieDetails(int movieId);
    DateTimeOffset RecordMovieOpened(int movieId);
    void SetCustomDisplayTitle(int movieId, string? customDisplayTitle);
}

/// <summary>
/// Manual catalog repair, independent of the automatic classifier: move a movie's file(s) into an existing
/// TV show as an episode, or remove a catalog item without touching the file on disk.
/// </summary>
public interface ILibraryRepairUiActions
{
    /// <summary>
    /// Moves a movie's local file(s) into an existing TV show as one episode. The season and episode are
    /// created only if missing; the movie is then removed. Atomic: the media files are preserved and
    /// re-linked, no duplicate show/season/episode is created, and unrelated shows/episodes are untouched.
    /// Returns the target show id for navigation.
    /// </summary>
    MoveToSeriesResult MoveMovieToSeries(int movieId, int showId, int seasonNumber, int episodeNumber);

    /// <summary>
    /// Removes a movie and its media-file registrations from the catalog only. No file on disk is deleted,
    /// and unrelated items are untouched.
    /// </summary>
    void RemoveMovieFromLibrary(int movieId);

    /// <summary>Removes a show and its episode registrations. Files on disk are kept.</summary>
    void RemoveShowFromLibrary(int showId);
}

public interface IPersonalLibraryUiActions
{
    PersonalMovieSnapshot GetPersonalSnapshot(int movieId);
    PersonalMovieSnapshot SetPersonalPreference(int movieId, PersonalPreference preference);
    PersonalMovieSnapshot ClearPersonalPreference(int movieId);
    PersonalMovieSnapshot AddToWatchlist(int movieId);
    PersonalMovieSnapshot RemoveFromWatchlist(int movieId);
    PersonalMovieSnapshot RecordWatch(int movieId, DateTimeOffset? watchedAt = null);

    /// <summary>
    /// The stored watch events, newest first. RemoveWatchEvent takes an event id, so the history has
    /// to be readable through the same contract for that call to be usable at all.
    /// </summary>
    IReadOnlyList<WatchEvent> ListWatchEvents(int movieId);

    PersonalMovieSnapshot RemoveWatchEvent(int eventId);
    IReadOnlyList<MovieListItem> ListPersonalMovies(PersonalLibrarySection section);

    /// <summary>Every custom list, in creation order, each with its member count (movies + shows).</summary>
    IReadOnlyList<CustomList> ListCustomLists();

    /// <summary>
    /// Creates a custom list and returns it (including its new stable id). Throws
    /// <see cref="DuplicateCustomListNameException" /> when the trimmed name already exists (case-insensitive).
    /// </summary>
    CustomList CreateCustomList(string name);

    /// <summary>
    /// Renames a custom list. Identity and membership are unchanged. Throws
    /// <see cref="DuplicateCustomListNameException" /> when another list already uses the name.
    /// </summary>
    void RenameCustomList(int listId, string name);

    /// <summary>Deletes a custom list and its membership rows. Media items and other lists are untouched.</summary>
    void DeleteCustomList(int listId);

    /// <summary>Adds a movie to a custom list; idempotent. Returns the movie's list ids after.</summary>
    IReadOnlySet<int> AddMovieToCustomList(int listId, int movieId);

    /// <summary>Removes a movie from a custom list. Returns the movie's list ids after.</summary>
    IReadOnlySet<int> RemoveMovieFromCustomList(int listId, int movieId);

    /// <summary>The ids of the custom lists a movie belongs to (for showing membership in Movie Details).</summary>
    IReadOnlySet<int> GetCustomListIdsForMovie(int movieId);

    /// <summary>Adds a TV show to a custom list; idempotent. Returns the show's list ids after.</summary>
    IReadOnlySet<int> AddShowToCustomList(int listId, int showId);

    /// <summary>Removes a TV show from a custom list. Returns the show's list ids after.</summary>
    IReadOnlySet<int> RemoveShowFromCustomList(int listId, int showId);

    /// <summary>The ids of the custom lists a TV show belongs to (for showing membership in TV Details).</summary>
    IReadOnlySet<int> GetCustomListIdsForShow(int showId);

    /// <summary>The members of a custom list (movies and TV shows), newest membership first.</summary>
    IReadOnlyList<CustomListItem> ListCustomListItems(int listId);
}

public interface IWatchHistoryUiActions
{
    IReadOnlyList<WatchHistoryItem> ListRecentWatchHistory(int limit = 50, int offset = 0);
    WatchHistoryItem RecordEpisodeWatch(int episodeId, DateTimeOffset? watchedAt = null);
    int RemoveEpisodeWatch(int episodeId);

    /// <summary>
    /// Clears the entire watch history - movie, episode and orphaned events - and returns the number
    /// of events removed. Personal preferences and the watchlist are left intact.
    /// </summary>
    int ClearWatchHistory();
}

public interface IImportUiActions
{
    /// <summary>
    /// Checks the catalog's canonical media-path identity without touching metadata services or
    /// changing catalog state. Add Media uses this before metadata preflight so an exact existing
    /// file can be shown as already registered and skipped safely.
    /// </summary>
    bool IsMediaFileRegistered(string filePath);

    ImportReviewSession PrepareImportReview(
        string rootPath,
        bool recursive,
        Action<ImportReviewProgress>? progress = null,
        Func<bool>? isCancelled = null);

    /// <summary>Pure metadata lookup for the import review surface. It never writes catalog state.</summary>
    Task<ImportMetadataPreview> PreviewMetadataAsync(
        DiscoveredMedia item,
        CancellationToken cancellationToken = default);

    /// <summary>Retries metadata lookup for one discovered item without rescanning or registering it.</summary>
    Task<ImportMetadataPreview> RetryMetadataAsync(
        DiscoveredMedia item,
        CancellationToken cancellationToken = default);

    MovieFileIngestionResult ConfirmMovieImport(ConfirmMovieImportCommand command);
    MovieFileIngestionResult RegisterMovieImport(ConfirmMovieImportCommand command);
    MovieFileIngestionResult EnrichMovieImport(ConfirmMovieImportCommand command, MovieFileIngestionResult registration);
    Task<ImportBatchResult> ImportSelectedAsync(
        IReadOnlyList<ImportSelection> selections,
        CancellationToken cancellationToken = default,
        Action<ImportProgress>? progress = null);
    ManualMovieSearchResult ManualMovieSearch(string title, string? year = null);

    /// <summary>
    /// Registers one local episode file into the show / season / episode hierarchy, creating only the
    /// rows it needs and reusing the ones that exist. Registering the same path twice is a no-op that
    /// reports the existing rows. A file the parser could not resolve confidently is refused with an
    /// <see cref="EpisodeRegistrationException" /> instead of being registered somewhere plausible.
    /// </summary>
    EpisodeFileIngestionResult RegisterEpisodeImport(ConfirmEpisodeImportCommand command);
}

/// <summary>The read side of the TV catalog: the shows grid and one show's full hierarchy.</summary>
public interface ITvLibraryUiActions
{
    IReadOnlyList<TvShowListItem> ListShows();

    DateTimeOffset RecordShowOpened(int showId);

    TvShowDetails GetShowDetails(int showId);

    void SetCustomDisplayTitle(int showId, string? customDisplayTitle);

    /// <summary>
    /// Provider-confirmed season/episode enrichment, fetched asynchronously so it never blocks opening a
    /// show. Returns the enriched hierarchy when at least one season was confirmed by the metadata provider,
    /// or null when the show is unmatched, no provider is configured, or every provider request failed,
    /// timed out, or returned nothing. The local hierarchy from <see cref="GetShowDetails" /> is always the
    /// baseline; enrichment never writes catalog state, never changes local ownership, and never invents a
    /// media file. The default implementation performs no enrichment.
    /// </summary>
    Task<TvShowDetails?> GetShowDetailsEnrichedAsync(int showId, CancellationToken cancellationToken = default)
        => Task.FromResult<TvShowDetails?>(null);

    /// <summary>The registered files of one episode, for Play and Open Folder.</summary>
    IReadOnlyList<DropSort.Domain.Library.Movies.MediaFile> ListEpisodeFiles(int episodeId);

    /// <summary>
    /// The episode a registered media file belongs to, or null when the file is not an episode file.
    /// Check Library uses it to name a missing file as an episode rather than as a movie.
    /// </summary>
    int? FindEpisodeForMediaFile(int mediaFileId);

    IReadOnlyList<TvContinueWatchingItem> ListContinueWatching(int limit = 20);
}

public interface ISettingsUiActions
{
    bool IsTmdbConfigured();
    TmdbCredentialState GetTmdbCredentialState();
    Task<TmdbCredentialResult> SaveTmdbCredentialAsync(string credential, ITmdbCredentialValidator validator, CancellationToken cancellationToken = default);
    Task<bool> RemoveTmdbCredentialAsync(CancellationToken cancellationToken = default);
    Task<ConnectionTestResult> TestTmdbConnectionAsync(IMetadataProvider provider, CancellationToken cancellationToken = default);
    
    ClearLibraryDataResult ClearLibraryData();
    
    UiLanguage CurrentUiLanguage();
    UiLanguage SetUiLanguage(UiLanguage language);
    
    UiTheme CurrentUiTheme();
    UiTheme SetUiTheme(UiTheme theme);
}

public interface IOrganizationUiActions
{
    OrganizationPreview PrepareOrganization(int mediaFileId, string destinationRoot, string destinationFilename);
    OrganizationResult ConfirmOrganization(string previewId);
    void DiscardOrganizationPreview(string previewId);
}

public interface IOperationHistoryUiActions
{
    IReadOnlyList<OperationHistoryItem> ListOperationHistory(OperationHistoryQuery? query = null);
    void SaveOperationHistory(IReadOnlyList<OperationHistoryItem> items, string path);
    OperationDetails GetOperationDetails(string operationId);
    UndoPreview PrepareUndo(string operationId);
    UndoResult ConfirmUndo(string previewId);
    void DiscardUndoPreview(string previewId);
    RecoveryAssessment InspectRecovery(string operationId);
    RecoveryResult AttemptRecovery(string operationId);
}

public interface IReconciliationUiActions
{
    LibraryReconciliationProgress ReconcileLibraryFiles(
        Action<LibraryReconciliationProgress>? progress = null, 
        Func<bool>? isCancelled = null);
        
    RelinkPreview PrepareMediaRelink(int mediaFileId, string candidatePath);
    RelinkResult ConfirmMediaRelink(string previewId);
    RelinkPreview PrepareMediaRelink(int mediaFileId, string candidatePath, Func<bool> isCancelled)
    {
        if (isCancelled()) throw new OperationCanceledException();
        return PrepareMediaRelink(mediaFileId, candidatePath);
    }
    RelinkResult ConfirmMediaRelink(string previewId, Func<bool> isCancelled)
    {
        if (isCancelled()) throw new OperationCanceledException();
        return ConfirmMediaRelink(previewId);
    }
    void DiscardMediaRelinkPreview(string previewId);
    
    LibraryHealthProgress CheckLibrary(
        Action<LibraryHealthProgress>? progress = null, 
        Func<bool>? isCancelled = null);
}
