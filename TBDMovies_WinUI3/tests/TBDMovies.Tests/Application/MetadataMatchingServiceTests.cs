using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TBDMovies.Application.External;
using TBDMovies.Application.Repositories;
using TBDMovies.Application.Services;
using TBDMovies.Domain.Library.Movies;
using TBDMovies.Domain.Library.Shows;
using TBDMovies.Domain.Media.Matcher;
using TBDMovies.Domain.Metadata.Contracts;
using Xunit;

namespace TBDMovies.Tests.Application;

public class MetadataMatchingServiceTests
{
    private sealed class FakeUnitOfWork : ICatalogUnitOfWork, ICatalogUnitOfWorkFactory
    {
        public FakeMovieRepo MoviesRepo { get; } = new();
        public FakeTvShowRepo TvShowsRepo { get; } = new();
        public FakeTvSeasonRepo TvSeasonsRepo { get; } = new();
        public FakeTvEpisodeRepo TvEpisodesRepo { get; } = new();

        public IMovieRepository Movies => MoviesRepo;
        public IMediaFileRepository MediaFiles => throw new NotImplementedException();
        public ITvShowRepository TvShows => TvShowsRepo;
        public ITvSeasonRepository TvSeasons => TvSeasonsRepo;
        public ITvEpisodeRepository TvEpisodes => TvEpisodesRepo;

        public bool Committed { get; private set; }

        public ICatalogUnitOfWork Begin() => this;
        public void Commit() => Committed = true;
        public void Dispose() { }
    }

    private sealed class FakeMovieRepo : IMovieRepository
    {
        private int _nextId = 1;
        public readonly Dictionary<int, Movie> Storage = new();

        public Movie Create(MovieCatalogData data, DateTimeOffset now)
        {
            var m = new Movie(_nextId++, data, now, now, now);
            Storage[m.Id] = m;
            return m;
        }

        public Movie UpdateMetadata(int id, MovieCatalogData data, DateTimeOffset now)
        {
            var m = Storage[id];
            var updated = new Movie(m.Id, data, m.DateAdded, m.CreatedAt, now);
            Storage[id] = updated;
            return updated;
        }

        public Movie? GetById(int id) => Storage.GetValueOrDefault(id);
        public Movie? GetByExternalId(string provider, string externalId) =>
            Storage.Values.FirstOrDefault(movie =>
                string.Equals(movie.Provider, provider, StringComparison.OrdinalIgnoreCase)
                && string.Equals(movie.ExternalId, externalId, StringComparison.OrdinalIgnoreCase));
        public IReadOnlyList<Movie> ListAll() => new List<Movie>(Storage.Values);
        public IReadOnlyList<Movie> ListPage(int afterId, int limit) => ListAll();
        public int CountAll() => Storage.Count;
        public void RecordOpened(int id, DateTimeOffset openedAt)
        {
            if (Storage.TryGetValue(id, out var movie))
                Storage[id] = new Movie(movie.Id, movie.Data, movie.DateAdded, movie.CreatedAt, movie.UpdatedAt, movie.ReviewMatch, openedAt);
        }
        public void Delete(int id) => Storage.Remove(id);
        public void SetReviewMatch(int id, bool value)
        {
            if (Storage.TryGetValue(id, out var m))
                Storage[id] = new Movie(m.Id, m.Data, m.DateAdded, m.CreatedAt, m.UpdatedAt, value);
        }
        public void SetCustomDisplayTitle(int id, string? customDisplayTitle)
        {
            if (Storage.TryGetValue(id, out var m))
                Storage[id] = new Movie(m.Id, m.Data, m.DateAdded, m.CreatedAt, m.UpdatedAt, m.ReviewMatch, m.LastOpenedAt, customDisplayTitle);
        }
    }

    private sealed class MockMetadataProvider : IMetadataProvider
    {
        public string ProviderName => "TMDB";
        public bool IsConfigured => true;

        public List<MovieCandidate> MovieSearchResults { get; set; } = [];
        public List<string> MovieQueries { get; } = [];
        public Func<MovieSearchQuery, IReadOnlyList<MovieCandidate>>? MovieSearchFactory { get; set; }
        public MovieMetadata? MovieToReturn { get; set; }
        public List<TvCandidate> TvSearchResults { get; set; } = [];
        public List<string> TvQueries { get; } = [];
        public Func<TvSearchQuery, IReadOnlyList<TvCandidate>>? TvSearchFactory { get; set; }
        public TvShowMetadata? TvShowToReturn { get; set; }
        public TvSeasonMetadata? TvSeasonToReturn { get; set; }

        public Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConnectionTestResult(true, "Successfully connected to TMDB.", 200));

        public IReadOnlyList<MovieCandidate> Search(MovieSearchQuery query) => MovieSearchResults;

        public Task<IReadOnlyList<MovieCandidate>> SearchMoviesAsync(MovieSearchQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MovieCandidate>>(RecordMovieSearch(query));

        private IReadOnlyList<MovieCandidate> RecordMovieSearch(MovieSearchQuery query)
        {
            MovieQueries.Add(query.Title);
            return MovieSearchFactory?.Invoke(query) ?? MovieSearchResults;
        }

        public MovieMetadata? GetMovie(string externalId) => MovieToReturn;

        public Task<MovieMetadata?> GetMovieAsync(string externalId, CancellationToken cancellationToken = default) =>
            Task.FromResult(MovieToReturn);

        public Task<IReadOnlyList<TvCandidate>> SearchTvAsync(TvSearchQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TvCandidate>>(RecordTvSearch(query));

        private IReadOnlyList<TvCandidate> RecordTvSearch(TvSearchQuery query)
        {
            TvQueries.Add(query.Title);
            return TvSearchFactory?.Invoke(query) ?? TvSearchResults;
        }

        public Task<TvShowMetadata?> GetTvShowAsync(string externalId, CancellationToken cancellationToken = default) =>
            Task.FromResult(TvShowToReturn);

        public Task<TvSeasonMetadata?> GetTvSeasonAsync(string showExternalId, int seasonNumber, CancellationToken cancellationToken = default) =>
            Task.FromResult(TvSeasonToReturn);
    }

    private sealed class MockPosterService : IPosterService
    {
        public List<(string Provider, string Reference)> CachedRequests { get; } = [];

        public Task<string?> EnsurePosterCachedAsync(string provider, string reference, CancellationToken cancellationToken = default)
        {
            CachedRequests.Add((provider, reference));
            return Task.FromResult<string?>($"/cache/{reference}");
        }

        public string? GetCachedPosterPath(string provider, string reference) => null;
        public PosterAsset? LoadPoster(PosterRequest request) => null;
    }

    private sealed class MockSettingsRepo : ISettingsRepository
    {
        public Dictionary<string, string> Storage { get; } = new();

        public string? Get(string key) => Storage.GetValueOrDefault(key);
        public void Set(string key, string value, DateTimeOffset updatedAt) => Storage[key] = value;
        public void Delete(string key) => Storage.Remove(key);
    }

    private sealed class FakeMaintenanceRepo : ILibraryMaintenanceRepository
    {
        public (int Movies, int MediaFiles, int MetadataEntries, int Shows, int Seasons, int Episodes) ClearCatalog() =>
            (0, 0, 0, 0, 0, 0);
    }

    [Fact]
    public void SettingsService_RemovesLegacyToken_WithoutRestoringIt()
    {
        var settingsRepo = new MockSettingsRepo();
        settingsRepo.Set("tmdb_read_access_token", "saved_token_123", DateTimeOffset.UtcNow);

        var service = new SettingsService(
            new FakeMaintenanceRepo(),
            posterCache: null,
            settings: settingsRepo);

        Assert.False(service.IsTmdbConfigured());
        Assert.Null(service.GetTmdbToken());
        Assert.Equal("saved_token_123", settingsRepo.Get("tmdb_read_access_token"));
    }

    [Fact]
    public async Task SettingsService_TestTmdbConnectionAsync_DelegatesToProvider()
    {
        var provider = new MockMetadataProvider();
        var service = new SettingsService(new FakeMaintenanceRepo());

        var result = await service.TestTmdbConnectionAsync(provider);
        Assert.True(result.Success);
        Assert.Equal(200, result.StatusCode);
    }

    [Fact]
    public async Task MetadataMatchingService_SearchMovieCandidatesAsync_ReturnsScoredDecision()
    {
        var uow = new FakeUnitOfWork();
        var provider = new MockMetadataProvider
        {
            MovieSearchResults =
            [
                new MovieCandidate("TMDB", "550", "Fight Club", "Fight Club", 1999, "An insomniac office worker...", 8.4, "/poster.jpg")
            ]
        };

        var service = new MetadataMatchingService(uow, provider);
        var decision = await service.SearchMovieCandidatesAsync("Fight Club", 1999);

        Assert.Equal(MatchStatus.Matched, decision.Status);
        Assert.NotNull(decision.Candidate);
        Assert.Equal("550", decision.Candidate!.ExternalId);
    }

    [Fact]
    public async Task SearchMovieCandidatesAsync_UsesOneBoundedNormalizedFallback()
    {
        var uow = new FakeUnitOfWork();
        var provider = new MockMetadataProvider
        {
            MovieSearchFactory = query => query.Title == "Kaze Tachinu"
                ? [new MovieCandidate("TMDB", "335984", "The Wind Rises", "Kaze Tachinu", 2013, null, null, null)]
                : []
        };

        var decision = await new MetadataMatchingService(uow, provider)
            .SearchMovieCandidatesAsync("Kaze.Tachinu", 2013);

        Assert.Equal(MatchStatus.Matched, decision.Status);
        Assert.Equal("335984", decision.Candidate!.ExternalId);
        Assert.Equal(2, provider.MovieQueries.Count);
        Assert.Equal("Kaze.Tachinu", provider.MovieQueries[0]);
        Assert.Equal("Kaze Tachinu", provider.MovieQueries[1]);
    }

    [Fact]
    public async Task SearchMovieCandidatesAsync_PropagatesProviderFailure_without_converting_to_no_match()
    {
        var provider = new MockMetadataProvider
        {
            MovieSearchFactory = _ => throw new MetadataServiceException(MetadataFailureKind.Authentication)
        };

        var error = await Assert.ThrowsAsync<MetadataServiceException>(() =>
            new MetadataMatchingService(new FakeUnitOfWork(), provider)
                .SearchMovieCandidatesAsync("Kaze Tachinu", 2013));

        Assert.Equal(MetadataFailureKind.Authentication, error.Kind);
        Assert.Single(provider.MovieQueries);
    }

    [Fact]
    public async Task SearchMovieCandidatesAsync_RetainsEqualScoreCandidates_from_fallback_queries()
    {
        var provider = new MockMetadataProvider
        {
            MovieSearchFactory = query => query.Title switch
            {
                "Equal.Title" => [new MovieCandidate("TMDB", "first", "Equal Title", null, null, null, null, null)],
                "Equal Title" => [new MovieCandidate("TMDB", "second", "Equal Title", null, null, null, null, null)],
                _ => []
            }
        };

        var decision = await new MetadataMatchingService(new FakeUnitOfWork(), provider)
            .SearchMovieCandidatesAsync("Equal.Title", null);

        Assert.Equal(MatchStatus.ReviewRequired, decision.Status);
        Assert.Equal(2, decision.RankedCandidates.Length);
        Assert.Equal(2, provider.MovieQueries.Count);
    }

    [Fact]
    public async Task SearchTvCandidatesAsync_RetainsLowScoreFallbackCandidates_for_manual_review()
    {
        var provider = new MockMetadataProvider
        {
            TvSearchFactory = query => query.Title == "Noisy Show"
                ? [new TvCandidate("TMDB", "1", "Unrelated Show", null, null, null, null, null)]
                : []
        };

        var decision = await new MetadataMatchingService(new FakeUnitOfWork(), provider)
            .SearchTvCandidatesAsync("Noisy.Show", null);

        Assert.Equal(MatchStatus.NoMatch, decision.Status);
        Assert.Single(decision.RankedCandidates);
        Assert.Equal(2, provider.TvQueries.Count);
    }

    [Fact]
    public async Task MetadataMatchingService_MatchMovieAsync_UpdatesCatalogAndCachesPoster()
    {
        var uow = new FakeUnitOfWork();
        var now = DateTimeOffset.UtcNow;
        var existingMovie = uow.MoviesRepo.Create(
            new MovieCatalogData(null, null, "Inception", null, 2010, null, ImmutableArray<string>.Empty, null, null, null, MetadataStatus.Pending),
            now);

        var provider = new MockMetadataProvider
        {
            MovieToReturn = new MovieMetadata(
                "TMDB",
                "27205",
                "Inception",
                "Inception",
                2010,
                "A thief steals corporate secrets...",
                ImmutableArray.Create("Action", "Sci-Fi"),
                148,
                8.8,
                "/inception_poster.jpg",
                "/inception_backdrop.jpg",
                "Your mind is the scene of the crime.")
        };

        var posterService = new MockPosterService();
        var service = new MetadataMatchingService(uow, provider, posterService, () => now);

        var candidate = new MovieCandidate("TMDB", "27205", "Inception", null, 2010, null, null, "/inception_poster.jpg");
        var updated = await service.MatchMovieAsync(existingMovie.Id, candidate);

        Assert.Equal(MetadataStatus.Ready, updated.MetadataStatus);
        Assert.Equal("TMDB", updated.Provider);
        Assert.Equal("27205", updated.ExternalId);
        Assert.Equal("Inception", updated.Title);
        Assert.Equal(148, updated.Data.RuntimeMinutes);
        Assert.Equal(8.8, updated.Data.Rating);
        Assert.True(uow.Committed);

        Assert.Single(posterService.CachedRequests);
        Assert.Equal("TMDB", posterService.CachedRequests[0].Provider);
        Assert.Equal("/inception_poster.jpg", posterService.CachedRequests[0].Reference);
    }

    [Fact]
    public async Task MetadataMatchingService_MatchMovieAsync_StaleCandidateWithNoProviderRecord_FailsSafelyWithoutWriting()
    {
        // A stale/invalid candidate whose TMDB record no longer resolves must not attach wrong metadata or
        // leave a half-written movie: the provider fetch returns null, the match throws NotFound, and the
        // movie stays in its clean Pending state with no provider identity and no commit.
        var uow = new FakeUnitOfWork();
        var now = DateTimeOffset.UtcNow;
        var existingMovie = uow.MoviesRepo.Create(
            new MovieCatalogData(null, null, "Ghost", null, 2010, null, ImmutableArray<string>.Empty, null, null, null, MetadataStatus.Pending),
            now);

        var provider = new MockMetadataProvider { MovieToReturn = null }; // stale id resolves to nothing
        var service = new MetadataMatchingService(uow, provider, new MockPosterService(), () => now);
        var candidate = new MovieCandidate("TMDB", "does-not-exist", "Ghost", null, 2010, null, null, null);

        var error = await Assert.ThrowsAsync<MetadataServiceException>(
            () => service.MatchMovieAsync(existingMovie.Id, candidate));
        Assert.Equal(MetadataFailureKind.NotFound, error.Kind);

        var after = uow.MoviesRepo.GetById(existingMovie.Id)!;
        Assert.Equal(MetadataStatus.Pending, after.MetadataStatus); // unchanged; no wrong metadata attached
        Assert.Null(after.Provider);
        Assert.Null(after.ExternalId);
        Assert.False(uow.Committed);
    }

    [Fact]
    public async Task MetadataMatchingService_MatchMovieAsync_IdentityConflict_PreservesOfflineMovieState()
    {
        var uow = new FakeUnitOfWork();
        var now = DateTimeOffset.UtcNow;
        var owner = uow.MoviesRepo.Create(
            new MovieCatalogData("TMDB", "27205", "Inception", "Inception", 2010,
                "Owner overview", ImmutableArray.Create("Sci-Fi"), 148, 8.8, "/owner.jpg"),
            now);
        var offlineMovie = uow.MoviesRepo.Create(
            new MovieCatalogData(null, null, "Inception local copy", null, 2010, null,
                ImmutableArray<string>.Empty, null, null, null, MetadataStatus.Pending),
            now);

        var provider = new MockMetadataProvider
        {
            MovieToReturn = new MovieMetadata(
                "TMDB", "27205", "Inception", "Inception", 2010, "Provider overview",
                ImmutableArray.Create("Sci-Fi"), 148, 8.8, "/provider.jpg")
        };
        var service = new MetadataMatchingService(uow, provider, clock: () => now);

        var error = await Assert.ThrowsAsync<MetadataServiceException>(() =>
            service.MatchMovieAsync(
                offlineMovie.Id,
                new MovieCandidate("TMDB", "27205", "Inception", null, 2010, null, null, "/provider.jpg")));

        Assert.Equal(MetadataFailureKind.IdentityConflict, error.Kind);
        Assert.Equal(2, uow.MoviesRepo.CountAll());
        var preservedOfflineMovie = uow.MoviesRepo.GetById(offlineMovie.Id);
        Assert.NotNull(preservedOfflineMovie);
        Assert.Null(preservedOfflineMovie!.Provider);
        Assert.Null(preservedOfflineMovie.ExternalId);
        Assert.Equal("Inception local copy", preservedOfflineMovie.Title);
        Assert.Equal(MetadataStatus.Pending, preservedOfflineMovie.MetadataStatus);
        var preservedOwner = uow.MoviesRepo.GetById(owner.Id);
        Assert.NotNull(preservedOwner);
        Assert.Equal("27205", preservedOwner!.ExternalId);
        Assert.Equal("Owner overview", preservedOwner.Overview);
        Assert.False(uow.Committed);
    }

    [Fact]
    public async Task MetadataMatchingService_RefreshMovieMetadataAsync_UpdatesExistingMovie()
    {
        var uow = new FakeUnitOfWork();
        var now = DateTimeOffset.UtcNow;
        var existingMovie = uow.MoviesRepo.Create(
            new MovieCatalogData("TMDB", "27205", "Inception Old", null, 2010, null, ImmutableArray<string>.Empty, null, null, null, MetadataStatus.Ready),
            now);

        var provider = new MockMetadataProvider
        {
            MovieToReturn = new MovieMetadata(
                "TMDB",
                "27205",
                "Inception Updated",
                "Inception",
                2010,
                "Updated overview",
                ImmutableArray.Create("Action"),
                148,
                9.0,
                "/new_poster.jpg")
        };

        var service = new MetadataMatchingService(uow, provider, clock: () => now);
        var refreshed = await service.RefreshMovieMetadataAsync(existingMovie.Id);

        Assert.NotNull(refreshed);
        Assert.Equal("Inception Updated", refreshed!.Title);
        Assert.Equal(9.0, refreshed.Data.Rating);
        Assert.True(uow.Committed);
    }

    [Fact]
    public async Task MetadataMatchingService_MatchTvShowAsync_CreatesHierarchyAndUpdatesMetadata()
    {
        var uow = new FakeUnitOfWork();
        var now = DateTimeOffset.UtcNow;
        var existingShow = uow.TvShowsRepo.Create(
            new TvShowCatalogData(null, null, "Breaking Bad"),
            now);

        // Pre-create season 1 episode 1 in DB
        var season1 = uow.TvSeasonsRepo.Create(existingShow.Id, 1, null, null, now);
        var ep1 = uow.TvEpisodesRepo.Create(season1.Id, 1, null, null, null, null, now);

        var provider = new MockMetadataProvider
        {
            TvShowToReturn = new TvShowMetadata(
                "TMDB",
                "1396",
                "Breaking Bad",
                "Breaking Bad",
                2008,
                "A chemistry teacher diagnosed with lung cancer...",
                ImmutableArray.Create("Drama", "Crime"),
                9.5,
                "/bb_poster.jpg",
                "/bb_backdrop.jpg",
                ImmutableArray.Create(
                    new TvSeasonMetadata(
                        1,
                        "Season 1",
                        "Overview Season 1",
                        "/s1_poster.jpg",
                        "2008-01-20",
                        ImmutableArray<TvEpisodeMetadata>.Empty,
                        "3572")
                ),
                "Change the equation"),
            TvSeasonToReturn = new TvSeasonMetadata(
                1,
                "Season 1",
                "Overview Season 1",
                "/s1_poster.jpg",
                "2008-01-20",
                ImmutableArray.Create(
                    new TvEpisodeMetadata(
                        1,
                        "Pilot",
                        "Diagnosed with terminal lung cancer...",
                        58,
                        "2008-01-20",
                        8.5,
                        "/pilot_still.jpg",
                        "62085")
                ),
                "3572")
        };

        var posterService = new MockPosterService();
        var service = new MetadataMatchingService(uow, provider, posterService, () => now);

        var candidate = new TvCandidate("TMDB", "1396", "Breaking Bad", null, 2008, null, null, "/bb_poster.jpg");
        var updated = await service.MatchTvShowAsync(existingShow.Id, candidate, fetchEpisodes: true);

        Assert.Equal(MetadataStatus.Ready, updated.MetadataStatus);
        Assert.Equal("TMDB", updated.Data.Provider);
        Assert.Equal("1396", updated.Data.ExternalId);
        Assert.Equal(9.5, updated.Data.Rating);
        Assert.True(uow.Committed);

        // Check season was updated
        var updatedSeason = uow.TvSeasonsRepo.GetByNumber(existingShow.Id, 1);
        Assert.NotNull(updatedSeason);
        Assert.Equal("Season 1", updatedSeason!.Title);
        Assert.Equal("3572", updatedSeason.ExternalId);

        // Check episode 1 was updated
        var updatedEp = uow.TvEpisodesRepo.GetByNumber(season1.Id, 1);
        Assert.NotNull(updatedEp);
        Assert.Equal("Pilot", updatedEp!.Title);
        Assert.Equal(58, updatedEp.RuntimeMinutes);
        Assert.Equal(8.5, updatedEp.Rating);
        Assert.Equal("62085", updatedEp.ExternalId);

        // Poster cached
        Assert.Single(posterService.CachedRequests);
        Assert.Equal("/bb_poster.jpg", posterService.CachedRequests[0].Reference);
    }

    [Fact]
    public async Task MetadataMatchingService_RematchTvShow_AbortsWhenSeasonDetailsAreIncomplete()
    {
        var uow = new FakeUnitOfWork();
        var now = DateTimeOffset.UtcNow;
        var show = uow.TvShowsRepo.Create(new TvShowCatalogData("TMDB", "old", "Old Show"), now);
        uow.TvSeasonsRepo.Create(show.Id, 1, "Old season", "Old overview", now);

        var provider = new MockMetadataProvider
        {
            TvShowToReturn = new TvShowMetadata(
                "TMDB", "new", "New Show", null, 2024, null, ImmutableArray<string>.Empty,
                null, null, null,
                ImmutableArray.Create(new TvSeasonMetadata(1, "New season", null, null, null,
                    ImmutableArray<TvEpisodeMetadata>.Empty)))
        };

        var service = new MetadataMatchingService(uow, provider);
        var error = await Assert.ThrowsAsync<MetadataServiceException>(() =>
            service.MatchTvShowAsync(show.Id, new TvCandidate("TMDB", "new", "New Show", null, 2024, null, null, null)));

        Assert.Equal(MetadataFailureKind.IncompleteMetadata, error.Kind);
        Assert.False(uow.Committed);
        Assert.Equal("old", uow.TvShowsRepo.GetById(show.Id)!.Data.ExternalId);
        Assert.Equal("Old season", uow.TvSeasonsRepo.GetByNumber(show.Id, 1)!.Title);
    }

    [Fact]
    public async Task MetadataMatchingService_MatchTvShow_CancellationBeforeWriteDoesNotCommit()
    {
        var uow = new FakeUnitOfWork();
        var now = DateTimeOffset.UtcNow;
        var show = uow.TvShowsRepo.Create(new TvShowCatalogData(null, null, "Show"), now);
        var provider = new MockMetadataProvider
        {
            TvShowToReturn = new TvShowMetadata("TMDB", "1", "Show", null, 2024, null,
                ImmutableArray<string>.Empty, null, null, null, ImmutableArray<TvSeasonMetadata>.Empty)
        };
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var service = new MetadataMatchingService(uow, provider);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.MatchTvShowAsync(show.Id, new TvCandidate("TMDB", "1", "Show", null, 2024, null, null, null), ct: cts.Token));

        Assert.False(uow.Committed);
        Assert.Null(uow.TvShowsRepo.GetById(show.Id)!.Data.ExternalId);
    }
}
