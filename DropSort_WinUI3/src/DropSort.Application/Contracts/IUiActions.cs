using System;
using System.Collections.Generic;
using DropSort.Application.Dto;
using DropSort.Application.External;
using DropSort.Domain.Configuration;
using DropSort.Domain.Library.Personal;

namespace DropSort.Application.Contracts;

public interface ILibraryUiActions
{
    IReadOnlyList<MovieListItem> ListMovies();
    MovieListItem GetMovieItem(int movieId);
    MovieDetails GetMovieDetails(int movieId);
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
}

public interface IImportUiActions
{
    ImportReviewSession PrepareImportReview(
        string rootPath,
        bool recursive,
        Action<ImportReviewProgress>? progress = null,
        Func<bool>? isCancelled = null);

    MovieFileIngestionResult ConfirmMovieImport(ConfirmMovieImportCommand command);
    MovieFileIngestionResult RegisterMovieImport(ConfirmMovieImportCommand command);
    MovieFileIngestionResult EnrichMovieImport(ConfirmMovieImportCommand command, MovieFileIngestionResult registration);
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

    TvShowDetails GetShowDetails(int showId);

    /// <summary>The registered files of one episode, for Play and Open Folder.</summary>
    IReadOnlyList<DropSort.Domain.Library.Movies.MediaFile> ListEpisodeFiles(int episodeId);

    /// <summary>
    /// The episode a registered media file belongs to, or null when the file is not an episode file.
    /// Check Library uses it to name a missing file as an episode rather than as a movie.
    /// </summary>
    int? FindEpisodeForMediaFile(int mediaFileId);
}

public interface ISettingsUiActions
{
    // Simplified credential status for parity
    bool IsTmdbConfigured();
    bool ApplyTmdbSessionToken(string token);
    bool ClearTmdbSessionToken();
    string? GetTmdbToken();
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
    void DiscardMediaRelinkPreview(string previewId);
    
    LibraryHealthProgress CheckLibrary(
        Action<LibraryHealthProgress>? progress = null, 
        Func<bool>? isCancelled = null);
}
