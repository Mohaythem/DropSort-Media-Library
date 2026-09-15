using System;
using System.Collections.Generic;
using System.Linq;
using DropSort.Application.External;
using DropSort.Domain.Library.Movies;
using DropSort.Domain.Media.Discovery;
using DropSort.Domain.Media.Parser;
using DropSort.Domain.Metadata.Contracts;
using DropSort.Domain.Media.Matcher;

namespace DropSort.Application.Dto;

public record MovieListItem(int Id, string Title, int? Year, string? PosterReference, bool IsMissing, bool HasPendingMetadata);

public record MovieDetails(
    int Id, 
    string Title, 
    string? OriginalTitle, 
    int? Year, 
    string? Overview, 
    IReadOnlyList<string> Genres, 
    int? RuntimeMinutes, 
    double? Rating, 
    string? PosterReference, 
    IReadOnlyList<MediaFile> MediaFiles);

public record MovieFileIngestionResult(Movie Movie, MediaFile MediaFile, bool AlreadyRegistered = false);

public record ConfirmMovieImportCommand(
    string FilePath,
    long FileSize,
    ParsedMedia ParsedMedia,
    DateTimeOffset ObservedAt,
    MovieCandidate? SelectedCandidate = null);

public record ImportReviewProgress(int FilesDiscovered, int FilesProcessed, string? CurrentDirectory);

public record ImportReviewSession(IReadOnlyList<DiscoveredMedia> Items);

public enum ImportItemStatus
{
    ReadyToAdd,
    Matching,
    Matched,
    NeedsMatch,
    MetadataUnavailable,
    MetadataError,
    MetadataCancelled,
    RegistrationFailed
}

/// <summary>A discovered item selected by the user for local registration.</summary>
public record ImportSelection(DiscoveredMedia Item);

/// <summary>The outcome for one selected item. Registration and enrichment are reported separately.</summary>
public record ImportItemResult(
    string Path,
    DiscoveryClassification Classification,
    bool Registered,
    bool AutoMatched,
    MatchStatus? MatchStatus = null,
    string? Failure = null,
    ImportItemStatus Status = ImportItemStatus.ReadyToAdd,
    MetadataFailureKind? MetadataFailureKind = null,
    bool AlreadyRegistered = false);

/// <summary>Batch import results; metadata failures never undo successful local registration.</summary>
public record ImportBatchResult(IReadOnlyList<ImportItemResult> Items)
{
    public int RegisteredCount => Items.Count(item => item.Registered);
    public int AutoMatchedCount => Items.Count(item => item.AutoMatched);
    public int AddedCount => Items.Count(item => item.Registered && !item.AlreadyRegistered);
    public int ExistingCount => Items.Count(item => item.Registered && item.AlreadyRegistered);
    public int MatchedCount => Items.Count(item => item.Status == ImportItemStatus.Matched);
    public int NeedsMatchCount => Items.Count(item => item.Status == ImportItemStatus.NeedsMatch);
    public int MetadataUnavailableCount => Items.Count(item => item.Status == ImportItemStatus.MetadataUnavailable);
    public int MetadataErrorsCount => Items.Count(item => item.Status == ImportItemStatus.MetadataError);
    public int MetadataCancelledCount => Items.Count(item => item.Status == ImportItemStatus.MetadataCancelled);
    public int RegistrationFailuresCount => Items.Count(item => item.Status == ImportItemStatus.RegistrationFailed);
}

public record ManualMovieSearchResult(IReadOnlyList<MovieCandidate> Candidates);

public record ClearLibraryDataResult(
    int MoviesRemoved,
    int MediaFilesRemoved,
    int MetadataEntriesRemoved,
    int PosterFilesRemoved,
    string? Warning,
    int ShowsRemoved = 0,
    int SeasonsRemoved = 0,
    int EpisodesRemoved = 0);

public record PersonalMovieSnapshot(
    int MovieId,
    DropSort.Domain.Library.Personal.PersonalPreference Preference,
    bool Watchlisted,
    int WatchCount,
    DateTimeOffset? LastWatched);
