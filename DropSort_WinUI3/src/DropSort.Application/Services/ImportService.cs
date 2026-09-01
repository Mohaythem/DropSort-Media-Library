using System;
using System.Collections.Generic;
using DropSort.Application.Contracts;
using DropSort.Application.Dto;
using DropSort.Application.Repositories;
using DropSort.Application.External;
using DropSort.Domain.Library.Movies;
using DropSort.Domain.Metadata.Contracts;

namespace DropSort.Application.Services;

public class ImportService : IImportUiActions
{
    private readonly ICatalogUnitOfWorkFactory _uowFactory;
    private readonly IMetadataProvider _metadataProvider;
    private readonly IMediaDiscoveryService? _discovery;
    private readonly Func<DateTimeOffset> _clock;

    public ImportService(
        ICatalogUnitOfWorkFactory uowFactory,
        IMetadataProvider metadataProvider,
        IMediaDiscoveryService? discovery = null,
        Func<DateTimeOffset>? clock = null)
    {
        _uowFactory = uowFactory;
        _metadataProvider = metadataProvider;
        _discovery = discovery;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public ImportReviewSession PrepareImportReview(string rootPath, bool recursive, Action<ImportReviewProgress>? progress = null, Func<bool>? isCancelled = null)
    {
        if (_discovery is null) throw new InvalidOperationException("Media discovery is not configured.");
        var items = _discovery.Discover(
            rootPath,
            recursive,
            (discovered, processed, current) => progress?.Invoke(new ImportReviewProgress(discovered, processed, current)),
            isCancelled);
        return new ImportReviewSession(items);
    }

    public MovieFileIngestionResult ConfirmMovieImport(ConfirmMovieImportCommand command)
    {
        // V1: Confirms import by registering locally, then enriching if a candidate was selected.
        var registration = RegisterMovieImport(command);
        if (command.SelectedCandidate != null)
        {
            return EnrichMovieImport(command, registration);
        }
        return registration;
    }

    public MovieFileIngestionResult RegisterMovieImport(ConfirmMovieImportCommand command)
    {
        var movieData = new MovieCatalogData(
            provider: null,
            externalId: null,
            title: command.ParsedMedia.Title ?? System.IO.Path.GetFileNameWithoutExtension(command.FilePath),
            originalTitle: null,
            year: command.ParsedMedia.Year,
            overview: null,
            genres: System.Collections.Immutable.ImmutableArray<string>.Empty,
            runtimeMinutes: null,
            rating: null,
            posterReference: null,
            metadataStatus: MetadataStatus.Pending);

        var fileFacts = new VerifiedMediaFileFacts(
            command.FilePath,
            command.FileSize,
            command.ParsedMedia.Extension,
            command.ParsedMedia.Resolution,
            command.ParsedMedia.Codec,
            command.ParsedMedia.Source,
            command.ObservedAt);

        using var uow = _uowFactory.Begin();
        
        var existingFile = uow.MediaFiles.GetByPath(command.FilePath);
        if (existingFile != null && existingFile.MovieId.HasValue)
        {
            var existingMovie = uow.Movies.GetById(existingFile.MovieId.Value)!;
            return new MovieFileIngestionResult(existingMovie, existingFile);
        }

        var movie = uow.Movies.Create(movieData, _clock());
        var mediaFile = existingFile == null 
            ? uow.MediaFiles.Add(fileFacts, movie.Id)
            : uow.MediaFiles.LinkToMovie(existingFile.Id, movie.Id);
            
        uow.Commit();
        return new MovieFileIngestionResult(movie, mediaFile);
    }

    public MovieFileIngestionResult EnrichMovieImport(ConfirmMovieImportCommand command, MovieFileIngestionResult registration)
    {
        if (command.SelectedCandidate == null) throw new ArgumentException("Enrichment requires a selected candidate.");
        
        // Simulates TMDB optional enrichment contract
        var remote = _metadataProvider.GetMovie(command.SelectedCandidate.ExternalId);
        if (remote == null) return registration; // Fails gracefully or throws depending on specific error.
        
        var data = new MovieCatalogData(
            provider: remote.Provider,
            externalId: remote.ExternalId,
            title: remote.Title,
            originalTitle: remote.OriginalTitle,
            year: remote.Year,
            overview: remote.Overview,
            genres: remote.Genres,
            runtimeMinutes: remote.RuntimeMinutes,
            rating: remote.Rating,
            posterReference: remote.PosterReference,
            metadataStatus: MetadataStatus.Ready);

        using var uow = _uowFactory.Begin();
        var updatedMovie = uow.Movies.UpdateMetadata(registration.Movie.Id, data, _clock());
        uow.Commit();
        
        return new MovieFileIngestionResult(updatedMovie, registration.MediaFile);
    }

    public ManualMovieSearchResult ManualMovieSearch(string title, string? year = null)
    {
        int? parsedYear = int.TryParse(year, out var y) ? y : null;
        var results = _metadataProvider.Search(new MovieSearchQuery(title, parsedYear));
        return new ManualMovieSearchResult(results);
    }
}
