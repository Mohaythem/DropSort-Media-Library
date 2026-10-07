using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.IO;
using System.Text.RegularExpressions;
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
    private const int MaxSearchQueries = 3;
    private static readonly Regex LeadingReleaseSite = new(
        @"^\s*(?:\[\s*)?(?:www\.)?[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?\.(?:com|net|org)\b(?:\s*\])?[\s._-]*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex BracketedReleaseNoise = new(
        @"\[[^\]]*(?:2160p|1440p|1080p|720p|576p|480p|4k|uhd|bluray|blu-ray|bdrip|brrip|web-dl|webrip|hdtv|dvdrip|hdrip|remux|x264|x265|h\.?264|h\.?265|hevc|av1|xvid|divx)[^\]]*\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex LanguageBracketNoise = new(
        @"\[(?:ar|en|eng|ja|jpn|fr|de|ger)\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex TechnicalToken = new(
        @"(?i)\b(?:2160p|1440p|1080p|720p|576p|480p|4k|uhd|bluray|blu-ray|bdrip|brrip|web-dl|webrip|hdtv|dvdrip|hdrip|remux|x264|x265|h\.?264|h\.?265|hevc|av1|xvid|divx)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex StandaloneWebRelease = new(
        @"(?i)\bWEB(?=\s*(?:[- ]?(?:DL|Rip)\b|(?:2160p|1440p|1080p|720p|576p|480p|4k|uhd|bluray|blu-ray|bdrip|brrip|hdtv|dvdrip|hdrip|remux|x264|x265|h\.?264|h\.?265|hevc|av1|xvid|divx)\b))",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Separators = new(@"[._-]+", RegexOptions.Compiled);
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
        var scoringTitle = NormalizeSearchTitle(title);
        var aggregate = new List<MovieCandidate>();
        MatchDecision? latest = null;

        foreach (var query in BuildSearchQueries(title))
        {
            var candidates = await _metadataProvider.SearchMoviesAsync(new MovieSearchQuery(query, year), ct);
            aggregate.AddRange(candidates);
            var decision = MetadataScorer.Score(scoringTitle, year, DistinctMovies(aggregate));
            latest = decision;

            // A clear winner is sufficient evidence; do not spend additional provider calls after
            // the conservative automatic-match gate has passed.
            if (decision.Status == MatchStatus.Matched)
            {
                break;
            }
        }

        return latest ?? MetadataScorer.Score(scoringTitle, year, Array.Empty<MovieCandidate>());
    }

    public async Task<Movie> MatchMovieAsync(
        int movieId,
        MovieCandidate candidate,
        CancellationToken ct = default,
        MetadataStatus? expectedMetadataStatus = null,
        bool reviewMatch = false)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var snapshot = ReadMovieSnapshot(movieId);
        EnsureExpectedMetadataStatus(snapshot.MetadataStatus, expectedMetadataStatus);
        var metadata = await _metadataProvider.GetMovieAsync(candidate.ExternalId, ct)
            ?? throw new MetadataServiceException(MetadataFailureKind.NotFound);

        using var uow = _uowFactory.Begin();
        var movie = uow.Movies.GetById(movieId)
            ?? throw new InvalidOperationException($"Movie {movieId} not found.");
        if (movie.UpdatedAt != snapshot.UpdatedAt
            || !IsSameIdentity(snapshot, movie.Provider, movie.ExternalId)
            || (expectedMetadataStatus.HasValue && movie.MetadataStatus != expectedMetadataStatus.Value))
        {
            throw new MetadataServiceException(MetadataFailureKind.StaleRequest);
        }

        // Identity collision: another movie already owns this provider identity.
        if (!string.IsNullOrWhiteSpace(candidate.Provider) && !string.IsNullOrWhiteSpace(candidate.ExternalId))
        {
            var owner = uow.Movies.GetByExternalId(candidate.Provider, candidate.ExternalId);
            if (owner is not null && owner.Id != movieId)
            {
                // Never delete or merge the source movie here. Its local files and catalog state can
                // carry preferences, watchlist/custom-list membership, or history that must survive a
                // conflicting manual match. Let the caller resolve the duplicate explicitly.
                throw new MetadataServiceException(MetadataFailureKind.IdentityConflict);
            }
        }
        ct.ThrowIfCancellationRequested();

        var now = _clock();
        var updatedCatalogData = new MovieCatalogData(
            provider: candidate.Provider,
            externalId: candidate.ExternalId,
            title: metadata.Title,
            originalTitle: NullIfBlank(metadata.OriginalTitle),
            year: metadata.Year,
            overview: NullIfBlank(metadata.Overview),
            genres: metadata.Genres,
            runtimeMinutes: metadata.RuntimeMinutes,
            rating: metadata.Rating,
            posterReference: NullIfBlank(metadata.PosterReference),
            metadataStatus: MetadataStatus.Ready,
            backdropReference: NullIfBlank(metadata.BackdropReference),
            tagline: NullIfBlank(metadata.Tagline));

        uow.Movies.UpdateMetadata(movieId, updatedCatalogData, now);
        // A definitive match clears the review flag; an auto-picked close/ambiguous match sets it so the
        // library can recommend a glance while still showing the metadata.
        uow.Movies.SetReviewMatch(movieId, reviewMatch);
        var updatedMovie = uow.Movies.GetById(movieId)!;

        ct.ThrowIfCancellationRequested();
        uow.Commit();
        if (!string.IsNullOrWhiteSpace(metadata.PosterReference) && _posterService != null)
        {
            _ = _posterService.EnsurePosterCachedAsync(candidate.Provider, metadata.PosterReference, ct);
        }
        return updatedMovie;
    }

    public Movie SetMovieMetadataStatus(int movieId, MetadataStatus status, MetadataStatus expectedStatus)
    {
        using var uow = _uowFactory.Begin();
        var movie = uow.Movies.GetById(movieId) ?? throw new InvalidOperationException($"Movie {movieId} not found.");
        EnsureExpectedMetadataStatus(movie.MetadataStatus, expectedStatus);
        var data = new MovieCatalogData(movie.Provider, movie.ExternalId, movie.Title, movie.OriginalTitle,
            movie.Year, movie.Overview, movie.Genres, movie.RuntimeMinutes, movie.Rating,
            movie.PosterReference, status, movie.BackdropReference, movie.Tagline);
        var updated = uow.Movies.UpdateMetadata(movieId, data, _clock());
        uow.Commit();
        return updated;
    }

    public async Task<Movie?> RefreshMovieMetadataAsync(int movieId, CancellationToken ct = default)
    {
        string? externalId;
        string? provider;
        MovieSnapshot snapshot;

        using (var uowCheck = _uowFactory.Begin())
        {
            var existingMovie = uowCheck.Movies.GetById(movieId);
            if (existingMovie == null || string.IsNullOrWhiteSpace(existingMovie.ExternalId) || string.IsNullOrWhiteSpace(existingMovie.Provider))
            {
                return null;
            }
            externalId = existingMovie.ExternalId;
            provider = existingMovie.Provider;
            snapshot = new MovieSnapshot(
                existingMovie.Provider,
                existingMovie.ExternalId,
                existingMovie.UpdatedAt,
                existingMovie.MetadataStatus);
        }

        var metadata = await _metadataProvider.GetMovieAsync(externalId, ct);
        if (metadata == null)
        {
            return null;
        }

        using var uow = _uowFactory.Begin();
        var movie = uow.Movies.GetById(movieId);
        if (movie == null || movie.UpdatedAt != snapshot.UpdatedAt || !IsSameIdentity(snapshot, movie.Provider, movie.ExternalId))
        {
            throw new MetadataServiceException(MetadataFailureKind.StaleRequest);
        }
        ct.ThrowIfCancellationRequested();
        var now = _clock();
        var updatedCatalogData = new MovieCatalogData(
            provider: provider,
            externalId: externalId,
            title: metadata.Title,
            originalTitle: NullIfBlank(metadata.OriginalTitle),
            year: metadata.Year,
            overview: NullIfBlank(metadata.Overview),
            genres: metadata.Genres,
            runtimeMinutes: metadata.RuntimeMinutes,
            rating: metadata.Rating,
            posterReference: NullIfBlank(metadata.PosterReference),
            metadataStatus: MetadataStatus.Ready,
            backdropReference: NullIfBlank(metadata.BackdropReference),
            tagline: NullIfBlank(metadata.Tagline));

        var updatedMovie = uow.Movies.UpdateMetadata(movieId, updatedCatalogData, now);

        ct.ThrowIfCancellationRequested();
        uow.Commit();
        if (!string.IsNullOrWhiteSpace(metadata.PosterReference) && _posterService != null)
        {
            _ = _posterService.EnsurePosterCachedAsync(provider, metadata.PosterReference, ct);
        }
        return updatedMovie;
    }

    public async Task<TvMatchDecision> SearchTvCandidatesAsync(string title, int? year, CancellationToken ct = default)
    {
        var scoringTitle = NormalizeSearchTitle(title);
        var aggregate = new List<TvCandidate>();
        TvMatchDecision? latest = null;

        foreach (var query in BuildSearchQueries(title))
        {
            var candidates = await _metadataProvider.SearchTvAsync(new TvSearchQuery(query, year), ct);
            aggregate.AddRange(candidates);
            var decision = MetadataScorer.ScoreTv(scoringTitle, year, DistinctTv(aggregate));
            latest = decision;
            if (decision.Status == MatchStatus.Matched)
            {
                break;
            }
        }

        return latest ?? MetadataScorer.ScoreTv(scoringTitle, year, Array.Empty<TvCandidate>());
    }

    /// <summary>
    /// Produces at most three deterministic queries: the user's text, a release-noise-free title,
    /// and the scorer's punctuation/case-normalized form. Provider failures are deliberately not
    /// caught here, so authentication/network errors remain service errors rather than no-match.
    /// </summary>
    private static IReadOnlyList<string> BuildSearchQueries(string? title)
    {
        var raw = title?.Trim() ?? string.Empty;
        var cleaned = NormalizeSearchTitle(raw);
        var normalized = MetadataScorer.NormalizeTitle(cleaned);
        var queries = new List<string>(MaxSearchQueries);
        AddQuery(queries, raw);
        AddQuery(queries, cleaned);
        AddQuery(queries, normalized);
        return queries;
    }

    private static void AddQuery(ICollection<string> queries, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (!queries.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            queries.Add(value);
        }
    }

    private static string NormalizeSearchTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        var value = title.Trim();
        var extension = Path.GetExtension(value);
        if (extension is ".mkv" or ".mp4" or ".avi" or ".mov" or ".m4v" or ".wmv")
        {
            value = Path.GetFileNameWithoutExtension(value);
        }

        value = LeadingReleaseSite.Replace(value, string.Empty);
        value = BracketedReleaseNoise.Replace(value, " ");
        value = LanguageBracketNoise.Replace(value, " ");
        value = StandaloneWebRelease.Replace(value, " ");
        value = value.Replace('[', ' ').Replace(']', ' ');
        var technical = TechnicalToken.Match(value);
        if (technical.Success)
        {
            value = value[..technical.Index];
        }

        value = Separators.Replace(value, " ");
        return string.Join(" ", value.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim();
    }

    private static IReadOnlyList<MovieCandidate> DistinctMovies(IEnumerable<MovieCandidate> candidates) =>
        candidates
            .GroupBy(candidate => (candidate.Provider, candidate.ExternalId), StringTupleComparer.Instance)
            .Select(group => group.First())
            .ToArray();

    private static IReadOnlyList<TvCandidate> DistinctTv(IEnumerable<TvCandidate> candidates) =>
        candidates
            .GroupBy(candidate => (candidate.Provider, candidate.ExternalId), StringTupleComparer.Instance)
            .Select(group => group.First())
            .ToArray();

    private sealed class StringTupleComparer : IEqualityComparer<(string Provider, string ExternalId)>
    {
        public static StringTupleComparer Instance { get; } = new();

        public bool Equals((string Provider, string ExternalId) x, (string Provider, string ExternalId) y) =>
            string.Equals(x.Provider, y.Provider, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.ExternalId, y.ExternalId, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Provider, string ExternalId) value) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(value.Provider ?? string.Empty),
                StringComparer.OrdinalIgnoreCase.GetHashCode(value.ExternalId ?? string.Empty));
    }

    public async Task<TvShow> MatchTvShowAsync(
        int showId,
        TvCandidate candidate,
        bool fetchEpisodes = true,
        CancellationToken ct = default,
        MetadataStatus? expectedMetadataStatus = null)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var snapshot = ReadShowSnapshot(showId);
        EnsureExpectedMetadataStatus(snapshot.MetadataStatus, expectedMetadataStatus);
        var showMetadata = await _metadataProvider.GetTvShowAsync(candidate.ExternalId, ct)
            ?? throw new MetadataServiceException(MetadataFailureKind.NotFound);

        return await ApplyTvShowMetadataAsync(
            showId,
            candidate.Provider,
            candidate.ExternalId,
            showMetadata,
            fetchEpisodes,
            ct,
            snapshot,
            expectedMetadataStatus);
    }

    public TvShow SetTvShowMetadataStatus(int showId, MetadataStatus status, MetadataStatus expectedStatus)
    {
        using var uow = _uowFactory.Begin();
        var show = uow.TvShows.GetById(showId) ?? throw new InvalidOperationException($"Show {showId} not found.");
        EnsureExpectedMetadataStatus(show.MetadataStatus, expectedStatus);
        var data = new TvShowCatalogData(show.Data.Provider, show.Data.ExternalId, show.Title,
            show.Data.OriginalTitle, show.Data.Year, show.Data.Overview, show.Data.Genres,
            show.Data.PosterReference, status, show.Data.Rating, show.Data.BackdropReference, show.Data.Tagline);
        var updated = uow.TvShows.UpdateMetadata(showId, data, _clock());
        uow.Commit();
        return updated;
    }

    public async Task<TvShow?> RefreshTvShowMetadataAsync(int showId, bool fetchEpisodes = true, CancellationToken ct = default)
    {
        string? externalId;
        string? provider;
        TvShowSnapshot snapshot;

        using (var uowCheck = _uowFactory.Begin())
        {
            var existingShow = uowCheck.TvShows.GetById(showId);
            if (existingShow == null || string.IsNullOrWhiteSpace(existingShow.Data.ExternalId) || string.IsNullOrWhiteSpace(existingShow.Data.Provider))
            {
                return null;
            }
            externalId = existingShow.Data.ExternalId;
            provider = existingShow.Data.Provider;
            snapshot = new TvShowSnapshot(
                existingShow.Data.Provider,
                existingShow.Data.ExternalId,
                existingShow.UpdatedAt,
                existingShow.MetadataStatus);
        }

        var showMetadata = await _metadataProvider.GetTvShowAsync(externalId, ct);
        if (showMetadata == null)
        {
            return null;
        }

        return await ApplyTvShowMetadataAsync(
            showId,
            provider,
            externalId,
            showMetadata,
            fetchEpisodes,
            ct,
            snapshot,
            expectedMetadataStatus: null);
    }

    private async Task<TvShow> ApplyTvShowMetadataAsync(
        int showId,
        string provider,
        string externalId,
        TvShowMetadata showMetadata,
        bool fetchEpisodes,
        CancellationToken ct,
        TvShowSnapshot snapshot,
        MetadataStatus? expectedMetadataStatus)
    {
        var detailedSeasons = new List<TvSeasonMetadata>();
        // A null per-season fetch is only fatal on a TRUE rematch: an existing show that already carries a
        // real provider identity being re-pointed at a DIFFERENT one, where a half-fetched response could
        // strand the show between two identities. A FIRST manual match (the local show has no identity yet)
        // and a same-identity refresh must tolerate a season whose episodes cannot be fetched — real TMDB
        // returns seasons without inline episodes, so each season is fetched individually and any single
        // 404 (Specials/Season 0 especially) would otherwise abort the whole match. On tolerance the
        // show-level season stub is stored, so the show and every fetchable season still match.
        var isRematchOfExistingIdentity =
            !string.IsNullOrWhiteSpace(snapshot.Provider) && !IsSameIdentity(snapshot, provider, externalId);
        if (fetchEpisodes && !showMetadata.Seasons.IsDefaultOrEmpty)
        {
            foreach (var season in showMetadata.Seasons)
            {
                if (season.Episodes.IsDefaultOrEmpty)
                {
                    var fetchedSeason = await _metadataProvider.GetTvSeasonAsync(externalId, season.SeasonNumber, ct);
                    if (fetchedSeason is null && isRematchOfExistingIdentity)
                    {
                        throw new MetadataServiceException(MetadataFailureKind.IncompleteMetadata);
                    }
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

        if (show.UpdatedAt != snapshot.UpdatedAt
            || !IsSameIdentity(snapshot, show.Data.Provider, show.Data.ExternalId)
            || (expectedMetadataStatus.HasValue && show.MetadataStatus != expectedMetadataStatus.Value))
        {
            throw new MetadataServiceException(MetadataFailureKind.StaleRequest);
        }

        // Identity-collision guard: if a DIFFERENT show already owns this provider identity, refuse the
        // match before any write. Otherwise the UNIQUE(provider, external_id) constraint would throw
        // mid-transaction; catching it here keeps both shows untouched (no partial write) and lets the UI
        // surface a clear conflict/review state instead of a raw database error.
        if (!string.IsNullOrWhiteSpace(provider) && !string.IsNullOrWhiteSpace(externalId))
        {
            var owner = uow.TvShows.GetByExternalId(provider, externalId);
            if (owner is not null && owner.Id != showId)
            {
                throw new MetadataServiceException(MetadataFailureKind.IdentityConflict);
            }
        }

        ct.ThrowIfCancellationRequested();

        var now = _clock();
        var updatedCatalogData = new TvShowCatalogData(
            provider: provider,
            externalId: externalId,
            title: showMetadata.Title,
            originalTitle: NullIfBlank(showMetadata.OriginalTitle),
            year: showMetadata.FirstAirYear,
            overview: NullIfBlank(showMetadata.Overview),
            genres: showMetadata.Genres,
            posterReference: NullIfBlank(showMetadata.PosterReference),
            metadataStatus: MetadataStatus.Ready,
            rating: showMetadata.Rating,
            backdropReference: NullIfBlank(showMetadata.BackdropReference),
            tagline: NullIfBlank(showMetadata.Tagline));

        var updatedShow = uow.TvShows.UpdateMetadata(showId, updatedCatalogData, now);

        if (fetchEpisodes && detailedSeasons.Count > 0)
        {
            var returnedSeasonNumbers = new HashSet<int>();
            foreach (var season in detailedSeasons)
            {
                returnedSeasonNumbers.Add(season.SeasonNumber);
                var dbSeason = uow.TvSeasons.GetByNumber(showId, season.SeasonNumber)
                    ?? uow.TvSeasons.Create(showId, season.SeasonNumber, NullIfBlank(season.Title), NullIfBlank(season.Overview), now);

                dbSeason = uow.TvSeasons.UpdateMetadata(
                    dbSeason.Id,
                    NullIfBlank(season.Title),
                    NullIfBlank(season.Overview),
                    NullIfBlank(season.PosterReference),
                    NullIfBlank(season.AirDate),
                    NullIfBlank(season.ExternalId),
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
                                NullIfBlank(episode.Title),
                                NullIfBlank(episode.Overview),
                                episode.RuntimeMinutes,
                                NullIfBlank(episode.AirDate),
                                episode.Rating,
                                NullIfBlank(episode.StillReference),
                                NullIfBlank(episode.ExternalId),
                                now);
                        }
                    }
                }
            }

            // A rematch must not leave provider fields from the old show attached to
            // seasons/episodes which the new provider response does not contain.
            if (!IsSameIdentity(snapshot, provider, externalId))
            {
                foreach (var oldSeason in uow.TvSeasons.ListByShow(showId))
                {
                    if (returnedSeasonNumbers.Contains(oldSeason.Number))
                    {
                        continue;
                    }

                    uow.TvSeasons.UpdateMetadata(oldSeason.Id, null, null, null, null, null, now);
                    foreach (var oldEpisode in uow.TvEpisodes.ListBySeason(oldSeason.Id))
                    {
                        ClearEpisodeMetadata(uow, oldEpisode, now);
                    }
                }

                foreach (var oldEpisode in uow.TvEpisodes.ListByShow(showId))
                {
                    var seasonNumber = uow.TvSeasons.GetById(oldEpisode.SeasonId)?.Number;
                    if (seasonNumber.HasValue && returnedSeasonNumbers.Contains(seasonNumber.Value))
                    {
                        var returned = detailedSeasons.Find(s => s.SeasonNumber == seasonNumber.Value);
                        if (returned is not null && !returned.Episodes.Any(e => e.EpisodeNumber == oldEpisode.Number))
                        {
                            ClearEpisodeMetadata(uow, oldEpisode, now);
                        }
                    }
                }
            }
        }

        if (!IsSameIdentity(snapshot, provider, externalId) && (!fetchEpisodes || detailedSeasons.Count == 0))
        {
            foreach (var oldSeason in uow.TvSeasons.ListByShow(showId))
            {
                uow.TvSeasons.UpdateMetadata(oldSeason.Id, null, null, null, null, null, now);
            }
            foreach (var oldEpisode in uow.TvEpisodes.ListByShow(showId))
            {
                ClearEpisodeMetadata(uow, oldEpisode, now);
            }
        }

        ct.ThrowIfCancellationRequested();
        uow.Commit();

        if (!string.IsNullOrWhiteSpace(showMetadata.PosterReference) && _posterService != null)
        {
            _ = _posterService.EnsurePosterCachedAsync(provider, showMetadata.PosterReference, ct);
        }
        return updatedShow;
    }

    private TvShowSnapshot ReadShowSnapshot(int showId)
    {
        using var uow = _uowFactory.Begin();
        var show = uow.TvShows.GetById(showId)
            ?? throw new InvalidOperationException($"Show {showId} not found.");
        return new TvShowSnapshot(
            show.Data.Provider,
            show.Data.ExternalId,
            show.UpdatedAt,
            show.MetadataStatus);
    }

    private static bool IsSameIdentity(TvShowSnapshot snapshot, string? provider, string? externalId) =>
        string.Equals(snapshot.Provider, provider, StringComparison.Ordinal) &&
        string.Equals(snapshot.ExternalId, externalId, StringComparison.Ordinal);

    private static bool IsSameIdentity(MovieSnapshot snapshot, string? provider, string? externalId) =>
        string.Equals(snapshot.Provider, provider, StringComparison.Ordinal) &&
        string.Equals(snapshot.ExternalId, externalId, StringComparison.Ordinal);

    private MovieSnapshot ReadMovieSnapshot(int movieId)
    {
        using var uow = _uowFactory.Begin();
        var movie = uow.Movies.GetById(movieId)
            ?? throw new InvalidOperationException($"Movie {movieId} not found.");
        return new MovieSnapshot(movie.Provider, movie.ExternalId, movie.UpdatedAt, movie.MetadataStatus);
    }

    private static void EnsureExpectedMetadataStatus(
        MetadataStatus actual,
        MetadataStatus? expected)
    {
        if (expected.HasValue && actual != expected.Value)
        {
            throw new MetadataServiceException(MetadataFailureKind.StaleRequest);
        }
    }

    private static void ClearEpisodeMetadata(ICatalogUnitOfWork uow, TvEpisode episode, DateTimeOffset now) =>
        uow.TvEpisodes.UpdateMetadata(episode.Id, null, null, null, null, null, null, null, now);

    /// <summary>
    /// Normalizes an optional provider string to null when it is blank. TMDB returns "" (not null) for an
    /// absent overview, original title, tagline, poster/backdrop path, season/episode name, air date, etc.,
    /// and the catalog's domain records reject an empty-but-non-null string — so writing "" straight through
    /// threw and failed the whole match. Folding "" to null keeps "absent" meaning absent.
    /// </summary>
    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private sealed record TvShowSnapshot(
        string? Provider,
        string? ExternalId,
        DateTimeOffset UpdatedAt,
        MetadataStatus MetadataStatus);

    private sealed record MovieSnapshot(
        string? Provider,
        string? ExternalId,
        DateTimeOffset UpdatedAt,
        MetadataStatus MetadataStatus);
}
