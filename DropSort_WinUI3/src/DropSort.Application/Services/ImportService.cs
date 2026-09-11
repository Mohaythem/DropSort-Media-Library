using System;
using System.Collections.Generic;
using DropSort.Application.Contracts;
using DropSort.Application.Dto;
using DropSort.Application.Repositories;
using DropSort.Application.External;
using DropSort.Domain.Library.Movies;
using DropSort.Domain.Library.Shows;
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

    /// <summary>
    /// Registers one episode file into the hierarchy.
    /// <para>
    /// Everything happens in one transaction: the show is looked up by its normalized title and created
    /// only if it is new, then the season, then the episode, then the media file is stored and linked.
    /// Re-registering the same path returns the rows it already belongs to, so scanning a folder twice
    /// does not duplicate anything and does not move a file between episodes.
    /// </para>
    /// <para>
    /// A file the parser could not resolve is refused with the reason instead of being registered
    /// against a guessed show or season - that is what keeps an ambiguous name from corrupting the
    /// hierarchy.
    /// </para>
    /// </summary>
    public EpisodeFileIngestionResult RegisterEpisodeImport(ConfirmEpisodeImportCommand command)
    {
        var parsed = command.ParsedMedia;

        if (parsed.SeasonNumber is not int seasonNumber || parsed.EpisodeNumber is not int episodeNumber)
        {
            throw new EpisodeRegistrationException(
                EpisodeRegistrationRefusal.NoEpisodeMarker,
                $"'{System.IO.Path.GetFileName(command.FilePath)}' carries no season and episode marker.");
        }

        if (string.IsNullOrWhiteSpace(parsed.Title))
        {
            throw new EpisodeRegistrationException(
                EpisodeRegistrationRefusal.NoShowTitle,
                $"'{System.IO.Path.GetFileName(command.FilePath)}' has no show name before the episode marker.");
        }

        if (seasonNumber is < 0 or > 999 || episodeNumber is < 0 or > 999)
        {
            throw new EpisodeRegistrationException(
                EpisodeRegistrationRefusal.NumbersOutOfRange,
                $"Season {seasonNumber} episode {episodeNumber} is outside the range the catalog accepts.");
        }

        var showData = new TvShowCatalogData(provider: null, externalId: null, title: parsed.Title!);
        var now = _clock();

        using var uow = _uowFactory.Begin();

        var existingFile = uow.MediaFiles.GetByPath(command.FilePath);

        if (existingFile is not null)
        {
            if (existingFile.MovieId.HasValue)
            {
                throw new EpisodeRegistrationException(
                    EpisodeRegistrationRefusal.AlreadyAMovieFile,
                    $"'{System.IO.Path.GetFileName(command.FilePath)}' is already registered as a movie file.");
            }

            var owner = uow.TvEpisodes.GetEpisodeIdForMediaFile(existingFile.Id);

            if (owner is int ownerEpisodeId)
            {
                var ownerEpisode = uow.TvEpisodes.GetById(ownerEpisodeId)!;
                var ownerSeason = uow.TvSeasons.GetById(ownerEpisode.SeasonId)!;
                var ownerShow = uow.TvShows.GetById(ownerSeason.ShowId)!;

                var isSameEpisode = ownerSeason.Number == seasonNumber
                    && ownerEpisode.Number == episodeNumber
                    && string.Equals(ownerShow.SortTitle, showData.SortTitle, StringComparison.Ordinal);

                if (!isSameEpisode)
                {
                    throw new EpisodeRegistrationException(
                        EpisodeRegistrationRefusal.OwnedByAnotherEpisode,
                        $"'{System.IO.Path.GetFileName(command.FilePath)}' is already registered as "
                            + $"{ownerShow.Title} S{ownerSeason.Number:00}E{ownerEpisode.Number:00}.");
                }

                return new EpisodeFileIngestionResult(
                    ownerShow,
                    ownerSeason,
                    ownerEpisode,
                    existingFile,
                    AlreadyRegistered: true);
            }
        }

        var show = uow.TvShows.GetBySortTitle(showData.SortTitle)
            ?? uow.TvShows.Create(showData, now);

        var season = uow.TvSeasons.GetByNumber(show.Id, seasonNumber)
            ?? uow.TvSeasons.Create(show.Id, seasonNumber, title: null, overview: null, now);

        var episode = uow.TvEpisodes.GetByNumber(season.Id, episodeNumber)
            ?? uow.TvEpisodes.Create(
                season.Id,
                episodeNumber,
                title: null,
                overview: null,
                runtimeMinutes: null,
                airDate: null,
                now);

        var facts = new VerifiedMediaFileFacts(
            command.FilePath,
            command.FileSize,
            parsed.Extension,
            parsed.Resolution,
            parsed.Codec,
            parsed.Source,
            command.ObservedAt);

        var mediaFile = existingFile ?? uow.MediaFiles.Add(facts);
        uow.TvEpisodes.LinkMediaFile(episode.Id, mediaFile.Id, now);

        uow.Commit();

        return new EpisodeFileIngestionResult(show, season, episode, mediaFile, AlreadyRegistered: false);
    }
}
