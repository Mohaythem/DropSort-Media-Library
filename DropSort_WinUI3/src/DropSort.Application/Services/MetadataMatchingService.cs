using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using DropSort.Application.External;
using DropSort.Application.Repositories;
using DropSort.Domain.Library.Movies;
using DropSort.Domain.Library.Shows;
using DropSort.Domain.Media.Matcher;
using DropSort.Domain.Metadata.Contracts;

namespace DropSort.Application.Services;

public sealed class MetadataMatchingService
{
    private readonly ICatalogUnitOfWorkFactory _uowFactory;
    private readonly IMetadataProvider _metadataProvider;
    private readonly IPosterService? _posterService;
    private readonly Func<DateTimeOffset> _clock;

    public MetadataMatchingService(
        ICatalogUnitOfWorkFactory uowFactory,
        IMetadataProvider metadataProvider,
        IPosterService? posterService = null,
        Func<DateTimeOffset>? clock = null)
    {
        _uowFactory = uowFactory ?? throw new ArgumentNullException(nameof(uowFactory));
        _metadataProvider = metadataProvider ?? throw new ArgumentNullException(nameof(metadataProvider));
        _posterService = posterService;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<MatchDecision> SearchMovieCandidatesAsync(string title, int? year, CancellationToken ct = default)
    {
        var candidates = await _metadataProvider.SearchMoviesAsync(new MovieSearchQuery(title, year), ct);
        return MetadataScorer.Score(title, year, candidates);
    }

    public async Task<Movie> MatchMovieAsync(int movieId, MovieCandidate candidate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var metadata = await _metadataProvider.GetMovieAsync(candidate.ExternalId, ct)
            ?? throw new InvalidOperationException($"Could not retrieve metadata for movie ID '{candidate.ExternalId}'.");

        using var uow = _uowFactory.Begin();
        var movie = uow.Movies.GetById(movieId)
            ?? throw new InvalidOperationException($"Movie {movieId} not found.");

        var now = _clock();
        var updatedCatalogData = new MovieCatalogData(
            provider: candidate.Provider,
            externalId: candidate.ExternalId,
            title: metadata.Title,
            originalTitle: metadata.OriginalTitle,
            year: metadata.Year,
            overview: metadata.Overview,
            genres: metadata.Genres,
            runtimeMinutes: metadata.RuntimeMinutes,
            rating: metadata.Rating,
            posterReference: metadata.PosterReference,
            metadataStatus: MetadataStatus.Ready,
            backdropReference: metadata.BackdropReference,
            tagline: metadata.Tagline);

        var updatedMovie = uow.Movies.UpdateMetadata(movieId, updatedCatalogData, now);

        if (!string.IsNullOrWhiteSpace(metadata.PosterReference) && _posterService != null)
        {
            _ = _posterService.EnsurePosterCachedAsync(candidate.Provider, metadata.PosterReference, ct);
        }

        uow.Commit();
        return updatedMovie;
    }

    public async Task<Movie?> RefreshMovieMetadataAsync(int movieId, CancellationToken ct = default)
    {
        string? externalId;
        string? provider;

        using (var uowCheck = _uowFactory.Begin())
        {
            var existingMovie = uowCheck.Movies.GetById(movieId);
            if (existingMovie == null || string.IsNullOrWhiteSpace(existingMovie.ExternalId) || string.IsNullOrWhiteSpace(existingMovie.Provider))
            {
                return null;
            }
            externalId = existingMovie.ExternalId;
            provider = existingMovie.Provider;
        }

        var metadata = await _metadataProvider.GetMovieAsync(externalId, ct);
        if (metadata == null)
        {
            return null;
        }

        using var uow = _uowFactory.Begin();
        var now = _clock();
        var updatedCatalogData = new MovieCatalogData(
            provider: provider,
            externalId: externalId,
            title: metadata.Title,
            originalTitle: metadata.OriginalTitle,
            year: metadata.Year,
            overview: metadata.Overview,
            genres: metadata.Genres,
            runtimeMinutes: metadata.RuntimeMinutes,
            rating: metadata.Rating,
            posterReference: metadata.PosterReference,
            metadataStatus: MetadataStatus.Ready,
            backdropReference: metadata.BackdropReference,
            tagline: metadata.Tagline);

        var updatedMovie = uow.Movies.UpdateMetadata(movieId, updatedCatalogData, now);

        if (!string.IsNullOrWhiteSpace(metadata.PosterReference) && _posterService != null)
        {
            _ = _posterService.EnsurePosterCachedAsync(provider, metadata.PosterReference, ct);
        }

        uow.Commit();
        return updatedMovie;
    }

    public async Task<TvMatchDecision> SearchTvCandidatesAsync(string title, int? year, CancellationToken ct = default)
    {
        var candidates = await _metadataProvider.SearchTvAsync(new TvSearchQuery(title, year), ct);
        return MetadataScorer.ScoreTv(title, year, candidates);
    }

    public async Task<TvShow> MatchTvShowAsync(int showId, TvCandidate candidate, bool fetchEpisodes = true, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var showMetadata = await _metadataProvider.GetTvShowAsync(candidate.ExternalId, ct)
            ?? throw new InvalidOperationException($"Could not retrieve metadata for TV show ID '{candidate.ExternalId}'.");

        return await ApplyTvShowMetadataAsync(showId, candidate.Provider, candidate.ExternalId, showMetadata, fetchEpisodes, ct);
    }

    public async Task<TvShow?> RefreshTvShowMetadataAsync(int showId, bool fetchEpisodes = true, CancellationToken ct = default)
    {
        string? externalId;
        string? provider;

        using (var uowCheck = _uowFactory.Begin())
        {
            var existingShow = uowCheck.TvShows.GetById(showId);
            if (existingShow == null || string.IsNullOrWhiteSpace(existingShow.Data.ExternalId) || string.IsNullOrWhiteSpace(existingShow.Data.Provider))
            {
                return null;
            }
            externalId = existingShow.Data.ExternalId;
            provider = existingShow.Data.Provider;
        }

        var showMetadata = await _metadataProvider.GetTvShowAsync(externalId, ct);
        if (showMetadata == null)
        {
            return null;
        }

        return await ApplyTvShowMetadataAsync(showId, provider, externalId, showMetadata, fetchEpisodes, ct);
    }

    private async Task<TvShow> ApplyTvShowMetadataAsync(
        int showId,
        string provider,
        string externalId,
        TvShowMetadata showMetadata,
        bool fetchEpisodes,
        CancellationToken ct)
    {
        var detailedSeasons = new List<TvSeasonMetadata>();
        if (fetchEpisodes && !showMetadata.Seasons.IsDefaultOrEmpty)
        {
            foreach (var season in showMetadata.Seasons)
            {
                if (season.Episodes.IsDefaultOrEmpty)
                {
                    var fetchedSeason = await _metadataProvider.GetTvSeasonAsync(externalId, season.SeasonNumber, ct);
                    detailedSeasons.Add(fetchedSeason ?? season);
                }
                else
                {
                    detailedSeasons.Add(season);
                }
            }
        }

        using var uow = _uowFactory.Begin();
        var show = uow.TvShows.GetById(showId)
            ?? throw new InvalidOperationException($"Show {showId} not found.");

        var now = _clock();
        var updatedCatalogData = new TvShowCatalogData(
            provider: provider,
            externalId: externalId,
            title: showMetadata.Title,
            originalTitle: showMetadata.OriginalTitle,
            year: showMetadata.FirstAirYear,
            overview: showMetadata.Overview,
            genres: showMetadata.Genres,
            posterReference: showMetadata.PosterReference,
            metadataStatus: MetadataStatus.Ready,
            rating: showMetadata.Rating,
            backdropReference: showMetadata.BackdropReference,
            tagline: showMetadata.Tagline);

        var updatedShow = uow.TvShows.UpdateMetadata(showId, updatedCatalogData, now);

        if (fetchEpisodes && detailedSeasons.Count > 0)
        {
            foreach (var season in detailedSeasons)
            {
                var dbSeason = uow.TvSeasons.GetByNumber(showId, season.SeasonNumber)
                    ?? uow.TvSeasons.Create(showId, season.SeasonNumber, season.Title, season.Overview, now);

                dbSeason = uow.TvSeasons.UpdateMetadata(
                    dbSeason.Id,
                    season.Title,
                    season.Overview,
                    season.PosterReference,
                    season.AirDate,
                    season.ExternalId,
                    now);

                if (!season.Episodes.IsDefaultOrEmpty)
                {
                    foreach (var episode in season.Episodes)
                    {
                        var dbEpisode = uow.TvEpisodes.GetByNumber(dbSeason.Id, episode.EpisodeNumber);
                        if (dbEpisode != null)
                        {
                            uow.TvEpisodes.UpdateMetadata(
                                dbEpisode.Id,
                                episode.Title,
                                episode.Overview,
                                episode.RuntimeMinutes,
                                episode.AirDate,
                                episode.Rating,
                                episode.StillReference,
                                episode.ExternalId,
                                now);
                        }
                    }
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(showMetadata.PosterReference) && _posterService != null)
        {
            _ = _posterService.EnsurePosterCachedAsync(provider, showMetadata.PosterReference, ct);
        }

        uow.Commit();
        return updatedShow;
    }
}
