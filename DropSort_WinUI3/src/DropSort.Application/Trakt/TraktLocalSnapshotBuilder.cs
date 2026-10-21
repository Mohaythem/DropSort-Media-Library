using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DropSort.Application.External;
using DropSort.Application.Repositories;
using DropSort.Domain.Library.Availability;
using DropSort.Domain.Library.Movies;
using DropSort.Domain.Library.Shows;

namespace DropSort.Application.Trakt;

/// <summary>
/// Thread-safe builder that creates immutable local candidate snapshots off the UI thread.
/// Probes disk availability independently without holding database write units of work.
/// </summary>
public sealed class TraktLocalSnapshotBuilder : ITraktLocalSnapshotBuilder
{
    private readonly IMovieRepository _movieRepository;
    private readonly IMediaFileRepository _mediaFileRepository;
    private readonly ITvShowRepository _tvShowRepository;
    private readonly ITvSeasonRepository _tvSeasonRepository;
    private readonly ITvEpisodeRepository _tvEpisodeRepository;
    private readonly IAvailabilityInspector _availabilityInspector;

    public TraktLocalSnapshotBuilder(
        IMovieRepository movieRepository,
        IMediaFileRepository mediaFileRepository,
        ITvShowRepository tvShowRepository,
        ITvSeasonRepository tvSeasonRepository,
        ITvEpisodeRepository tvEpisodeRepository,
        IAvailabilityInspector availabilityInspector)
    {
        _movieRepository = movieRepository ?? throw new ArgumentNullException(nameof(movieRepository));
        _mediaFileRepository = mediaFileRepository ?? throw new ArgumentNullException(nameof(mediaFileRepository));
        _tvShowRepository = tvShowRepository ?? throw new ArgumentNullException(nameof(tvShowRepository));
        _tvSeasonRepository = tvSeasonRepository ?? throw new ArgumentNullException(nameof(tvSeasonRepository));
        _tvEpisodeRepository = tvEpisodeRepository ?? throw new ArgumentNullException(nameof(tvEpisodeRepository));
        _availabilityInspector = availabilityInspector ?? throw new ArgumentNullException(nameof(availabilityInspector));
    }

    /// <inheritdoc />
    public Task<TraktLocalSnapshot> BuildSnapshotAsync(CancellationToken cancellationToken = default)
    {
        // Execute strictly off the UI thread without holding write locks
        return Task.Run(() => BuildSnapshotCore(cancellationToken), cancellationToken);
    }

    private TraktLocalSnapshot BuildSnapshotCore(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var candidates = new List<TraktLocalCandidate>();
        var pathInspectionCache = new Dictionary<string, AvailabilityInspectionStatus>(StringComparer.OrdinalIgnoreCase);

        // --- 1. Movie Candidates ---
        // Load all media files once to avoid N+1 queries per movie
        var movies = _movieRepository.ListAll();
        var allMediaFiles = _mediaFileRepository.ListAll();
        var filesByMovieId = allMediaFiles
            .Where(f => f.MovieId.HasValue)
            .GroupBy(f => f.MovieId!.Value)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<MediaFile>)g.ToList());

        foreach (var movie in movies)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var identity = ParseMovieIdentity(movie);
            filesByMovieId.TryGetValue(movie.Id, out var files);
            var availability = ResolveAvailability(files, pathInspectionCache, cancellationToken);

            candidates.Add(new TraktLocalCandidate(movie.Id, TraktMediaKind.Movie, identity, availability));
        }

        // --- 2. TV Show Hierarchy Candidates ---
        var shows = _tvShowRepository.ListAll();
        foreach (var show in shows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var showInfo = GetShowProviderInfo(show);
            var showIdentity = ParseShowIdentity(show);

            // Batched file association read: exactly one query per show for all episodes
            var mediaFilesByEpisode = _tvEpisodeRepository.ListMediaFilesByShow(show.Id);
            var seasons = _tvSeasonRepository.ListByShow(show.Id);
            var episodes = _tvEpisodeRepository.ListByShow(show.Id);

            var episodeAvailabilities = new Dictionary<int, TraktAvailability>();
            var episodesBySeason = episodes.GroupBy(e => e.SeasonId).ToDictionary(g => g.Key, g => g.ToList());
            var seasonsById = seasons.ToDictionary(s => s.Id);

            // 2a. Episodes
            foreach (var episode in episodes)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // If parent season is missing, omit identity so it never falsely defaults to season 0 (specials)
                TraktIdentity episodeIdentity;
                if (seasonsById.TryGetValue(episode.SeasonId, out var parentSeason))
                {
                    episodeIdentity = BuildEpisodeIdentity(showInfo, parentSeason.Number, episode);
                }
                else
                {
                    // Missing season: omit identity so it cannot falsely match specials or any coordinates
                    episodeIdentity = new TraktIdentity(TraktMediaKind.Episode);
                }

                mediaFilesByEpisode.TryGetValue(episode.Id, out var epFiles);
                var epAvailability = ResolveAvailability(epFiles, pathInspectionCache, cancellationToken);
                episodeAvailabilities[episode.Id] = epAvailability;

                candidates.Add(new TraktLocalCandidate(episode.Id, TraktMediaKind.Episode, episodeIdentity, epAvailability));
            }

            // 2b. Seasons
            foreach (var season in seasons)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var seasonIdentity = BuildSeasonIdentity(showInfo, season);
                episodesBySeason.TryGetValue(season.Id, out var seasonEps);

                var seasonAvailability = DetermineHierarchyAvailability(
                    seasonEps?.Select(e => episodeAvailabilities.TryGetValue(e.Id, out var av) ? av : TraktAvailability.Missing));

                candidates.Add(new TraktLocalCandidate(season.Id, TraktMediaKind.Season, seasonIdentity, seasonAvailability));
            }

            // 2c. Show
            var showAvailability = DetermineHierarchyAvailability(
                episodes.Select(e => episodeAvailabilities.TryGetValue(e.Id, out var av) ? av : TraktAvailability.Missing));

            candidates.Add(new TraktLocalCandidate(show.Id, TraktMediaKind.Show, showIdentity, showAvailability));
        }

        return new TraktLocalSnapshot(candidates, DateTimeOffset.UtcNow);
    }

    private TraktAvailability ResolveAvailability(
        IReadOnlyList<MediaFile>? files,
        Dictionary<string, AvailabilityInspectionStatus> pathCache,
        CancellationToken cancellationToken)
    {
        if (files == null || files.Count == 0)
            return TraktAvailability.Missing;

        bool hasPresent = false;
        bool hasOfflineOrError = false;

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var path = file.CurrentPath;
            if (string.IsNullOrWhiteSpace(path))
                continue;

            if (!pathCache.TryGetValue(path, out var status))
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var inspection = _availabilityInspector.Inspect(path);
                    status = inspection.Status;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    status = AvailabilityInspectionStatus.Error;
                }

                cancellationToken.ThrowIfCancellationRequested();

                pathCache[path] = status;
            }

            if (status == AvailabilityInspectionStatus.Present)
            {
                hasPresent = true;
            }
            else if (status is AvailabilityInspectionStatus.StorageUnavailable or AvailabilityInspectionStatus.Error)
            {
                hasOfflineOrError = true;
            }
        }

        if (hasPresent)
            return TraktAvailability.Available;

        if (hasOfflineOrError)
            return TraktAvailability.Unknown;

        return TraktAvailability.Missing;
    }

    /// <summary>
    /// Conservatively evaluates hierarchy availability from registered episodes.
    /// Never claims whole show or season Available from a locally registered subset.
    /// </summary>
    private static TraktAvailability DetermineHierarchyAvailability(IEnumerable<TraktAvailability>? episodeAvailabilities)
    {
        if (episodeAvailabilities == null)
            return TraktAvailability.Missing;

        bool anyAvailable = false;
        bool anyUnknown = false;
        bool hasEpisodes = false;

        foreach (var av in episodeAvailabilities)
        {
            hasEpisodes = true;
            if (av == TraktAvailability.Available)
                anyAvailable = true;
            else if (av == TraktAvailability.Unknown)
                anyUnknown = true;
        }

        if (!hasEpisodes)
            return TraktAvailability.Missing;

        // Shows/seasons with available episodes conservatively Partial unless full remote set known complete
        if (anyAvailable)
            return TraktAvailability.Partial;

        if (anyUnknown)
            return TraktAvailability.Unknown;

        return TraktAvailability.Missing;
    }

    private static TraktIdentity ParseMovieIdentity(Movie movie)
    {
        if (string.IsNullOrWhiteSpace(movie.Provider) || string.IsNullOrWhiteSpace(movie.ExternalId))
            return new TraktIdentity(TraktMediaKind.Movie);

        var provider = movie.Provider.Trim().ToLowerInvariant();
        var externalId = movie.ExternalId.Trim();

        return provider switch
        {
            "tmdb" => int.TryParse(externalId, out var id) && id > 0
                ? new TraktIdentity(TraktMediaKind.Movie, TmdbId: id)
                : new TraktIdentity(TraktMediaKind.Movie),
            "imdb" => new TraktIdentity(TraktMediaKind.Movie, ImdbId: externalId),
            "trakt" => long.TryParse(externalId, out var id) && id > 0
                ? new TraktIdentity(TraktMediaKind.Movie, TraktId: id)
                : new TraktIdentity(TraktMediaKind.Movie),
            "tvdb" => int.TryParse(externalId, out var id) && id > 0
                ? new TraktIdentity(TraktMediaKind.Movie, TvdbId: id)
                : new TraktIdentity(TraktMediaKind.Movie),
            _ => new TraktIdentity(TraktMediaKind.Movie)
        };
    }

    private static TraktIdentity ParseShowIdentity(TvShow show)
    {
        if (string.IsNullOrWhiteSpace(show.Provider) || string.IsNullOrWhiteSpace(show.ExternalId))
            return new TraktIdentity(TraktMediaKind.Show);

        var provider = show.Provider.Trim().ToLowerInvariant();
        var externalId = show.ExternalId.Trim();

        return provider switch
        {
            "tmdb" => int.TryParse(externalId, out var id) && id > 0
                ? new TraktIdentity(TraktMediaKind.Show, TmdbId: id)
                : new TraktIdentity(TraktMediaKind.Show),
            "imdb" => new TraktIdentity(TraktMediaKind.Show, ImdbId: externalId),
            "trakt" => long.TryParse(externalId, out var id) && id > 0
                ? new TraktIdentity(TraktMediaKind.Show, TraktId: id)
                : new TraktIdentity(TraktMediaKind.Show),
            "tvdb" => int.TryParse(externalId, out var id) && id > 0
                ? new TraktIdentity(TraktMediaKind.Show, TvdbId: id)
                : new TraktIdentity(TraktMediaKind.Show),
            _ => new TraktIdentity(TraktMediaKind.Show)
        };
    }

    private static (string? Provider, int? TmdbId, string? ImdbId, long? TraktId, int? TvdbId) GetShowProviderInfo(TvShow show)
    {
        if (string.IsNullOrWhiteSpace(show.Provider) || string.IsNullOrWhiteSpace(show.ExternalId))
            return (null, null, null, null, null);

        var provider = show.Provider.Trim().ToLowerInvariant();
        var externalId = show.ExternalId.Trim();

        return provider switch
        {
            "tmdb" => int.TryParse(externalId, out var id) && id > 0 ? ("tmdb", id, null, null, null) : (null, null, null, null, null),
            "imdb" => ("imdb", null, externalId, null, null),
            "trakt" => long.TryParse(externalId, out var id) && id > 0 ? ("trakt", null, null, id, null) : (null, null, null, null, null),
            "tvdb" => int.TryParse(externalId, out var id) && id > 0 ? ("tvdb", null, null, null, id) : (null, null, null, null, null),
            _ => (null, null, null, null, null)
        };
    }

    private static TraktIdentity BuildSeasonIdentity(
        (string? Provider, int? TmdbId, string? ImdbId, long? TraktId, int? TvdbId) showInfo,
        TvSeason season)
    {
        int? directTmdbId = null;

        // Lineage verification: in DropSort, only the TMDB enrich path stores external_id on seasons.
        // Direct season external ID is never inferred from TVDB, IMDb, or Trakt show providers.
        if (string.Equals(showInfo.Provider, "tmdb", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(season.ExternalId))
        {
            if (int.TryParse(season.ExternalId.Trim(), out var id) && id > 0)
            {
                directTmdbId = id;
            }
        }

        return new TraktIdentity(
            TraktMediaKind.Season,
            TmdbId: directTmdbId,
            ShowTraktId: showInfo.TraktId,
            ShowTmdbId: showInfo.TmdbId,
            ShowImdbId: showInfo.ImdbId,
            ShowTvdbId: showInfo.TvdbId,
            SeasonNumber: season.Number);
    }

    private static TraktIdentity BuildEpisodeIdentity(
        (string? Provider, int? TmdbId, string? ImdbId, long? TraktId, int? TvdbId) showInfo,
        int seasonNumber,
        TvEpisode episode)
    {
        int? directTmdbId = null;

        // Lineage verification: in DropSort, only the TMDB enrich path stores external_id on episodes.
        // Direct episode external ID is never inferred from TVDB, IMDb, or Trakt show providers.
        if (string.Equals(showInfo.Provider, "tmdb", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(episode.ExternalId))
        {
            if (int.TryParse(episode.ExternalId.Trim(), out var id) && id > 0)
            {
                directTmdbId = id;
            }
        }

        return new TraktIdentity(
            TraktMediaKind.Episode,
            TmdbId: directTmdbId,
            ShowTraktId: showInfo.TraktId,
            ShowTmdbId: showInfo.TmdbId,
            ShowImdbId: showInfo.ImdbId,
            ShowTvdbId: showInfo.TvdbId,
            SeasonNumber: seasonNumber,
            EpisodeNumber: episode.Number);
    }
}
