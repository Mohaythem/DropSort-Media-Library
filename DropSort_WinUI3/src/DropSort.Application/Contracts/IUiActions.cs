using System;
using System.Collections.Generic;
using DropSort.Application.Dto;
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
}

public interface ISettingsUiActions
{
    // Simplified credential status for parity
    bool IsTmdbConfigured();
    bool ApplyTmdbSessionToken(string token);
    bool ClearTmdbSessionToken();
    
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
