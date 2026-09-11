using System;
using System.Collections.Generic;
using DropSort.Domain.Library.Movies;
using DropSort.Domain.Media.Discovery;
using DropSort.Domain.Media.Parser;
using DropSort.Domain.Metadata.Contracts;

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

public record MovieFileIngestionResult(Movie Movie, MediaFile MediaFile);

public record ConfirmMovieImportCommand(
    string FilePath,
    long FileSize,
    ParsedMedia ParsedMedia,
    DateTimeOffset ObservedAt,
    MovieCandidate? SelectedCandidate = null);

public record ImportReviewProgress(int FilesDiscovered, int FilesProcessed, string? CurrentDirectory);

public record ImportReviewSession(IReadOnlyList<DiscoveredMedia> Items);

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
