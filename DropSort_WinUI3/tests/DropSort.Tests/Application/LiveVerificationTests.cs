using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DropSort.Application.Contracts;
using DropSort.Application.Dto;
using DropSort.Application.External;
using DropSort.Application.Repositories;
using DropSort.Application.Services;
using DropSort.Domain.Configuration;
using DropSort.Domain.Library.Movies;
using DropSort.Domain.Library.Shows;
using DropSort.Domain.Media.Discovery;
using DropSort.Domain.Media.Parser;
using DropSort.Domain.Metadata.Contracts;
using DropSort.FileSystem.Discovery;
using DropSort.FileSystem.Inspection;
using DropSort.FileSystem.Operations;
using DropSort.Infrastructure.Metadata.Cache;
using DropSort.Infrastructure.Metadata.Tmdb;
using DropSort.Infrastructure.Persistence.Migrations;
using DropSort.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DropSort.Tests.Application;

public sealed class LiveVerificationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _connectionString;
    private readonly CatalogUnitOfWorkFactory _uowFactory;

    public LiveVerificationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "DropSort_LiveVerif_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        var dbPath = Path.Combine(_tempDir, "dropsort_verif.db");
        _connectionString = $"Data Source={dbPath}";
        new DatabaseMigrator(_connectionString).Migrate();
        _uowFactory = new CatalogUnitOfWorkFactory(_connectionString);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch { }
    }

    [Fact]
    public async Task Live_scenario_valid_invalid_and_offline_connection_tests()
    {
        // 1. Valid TMDB connection (200 OK)
        var validHandler = new SimpleMockHandler(HttpStatusCode.OK, "{\"success\":true,\"status_code\":1,\"status_message\":\"Success.\"}");
        using var validClient = new TmdbClient(() => "valid_token", new HttpClient(validHandler));
        var validResult = await validClient.TestConnectionAsync();
        Assert.True(validResult.Success);
        Assert.Equal(200, validResult.StatusCode);

        // 2. Invalid TMDB credentials (401 Unauthorized)
        var invalidHandler = new SimpleMockHandler(HttpStatusCode.Unauthorized, "{\"status_code\":7,\"status_message\":\"Invalid API key.\",\"success\":false}");
        using var invalidClient = new TmdbClient(() => "invalid_token", new HttpClient(invalidHandler));
        var invalidResult = await invalidClient.TestConnectionAsync();
        Assert.False(invalidResult.Success);
        Assert.Equal(401, invalidResult.StatusCode);

        // 3. Offline failure case (HttpRequestException)
        var offlineHandler = new OfflineThrowingHandler();
        using var offlineClient = new TmdbClient(() => "some_token", new HttpClient(offlineHandler));
        var offlineResult = await offlineClient.TestConnectionAsync();
        Assert.False(offlineResult.Success);
        Assert.Contains("Cannot reach TMDB", offlineResult.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Live_scenario_movie_match_and_tv_show_match_with_seasons_and_episodes()
    {
        var posterDir = Path.Combine(_tempDir, "posters");
        Directory.CreateDirectory(posterDir);

        var imageBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01 };
        var extendedImageBytes = new byte[256];
        Array.Copy(imageBytes, extendedImageBytes, imageBytes.Length);

        var fakeHttp = new SimpleMockHandler(HttpStatusCode.OK, extendedImageBytes);
        var posterCache = new DiskPosterCache(posterDir, new HttpClient(fakeHttp));

        var provider = new ComprehensiveMockMetadataProvider();
        var matching = new MetadataMatchingService(_uowFactory, provider, posterCache);

        // A. Match one Movie
        int movieId;
        using (var uow = _uowFactory.Begin())
        {
            var movie = uow.Movies.Create(
                new MovieCatalogData(null, null, "Interstellar", null, 2014, null, [], null, null, null, MetadataStatus.Pending),
                DateTimeOffset.UtcNow);
            movieId = movie.Id;
            uow.Commit();
        }

        var movieCandidates = await matching.SearchMovieCandidatesAsync("Interstellar", 2014);
        Assert.NotEmpty(movieCandidates.RankedCandidates);
        var chosenMovie = movieCandidates.RankedCandidates[0].Candidate;

        var matchedMovie = await matching.MatchMovieAsync(movieId, chosenMovie);
        Assert.Equal("157336", matchedMovie.ExternalId);
        Assert.Equal("Interstellar", matchedMovie.Title);
        Assert.Equal(169, matchedMovie.RuntimeMinutes);
        Assert.Equal(8.6, matchedMovie.Rating);
        Assert.Contains("Science Fiction", matchedMovie.Genres);

        // B. Match one TV Show
        int showId;
        using (var uow = _uowFactory.Begin())
        {
            var show = uow.TvShows.Create(new TvShowCatalogData(null, null, "Stranger Things"), DateTimeOffset.UtcNow);
            showId = show.Id;
            var s1 = uow.TvSeasons.Create(showId, 1, "Season 1", null, DateTimeOffset.UtcNow);
            uow.TvEpisodes.Create(s1.Id, 1, "Chapter 1", null, null, null, DateTimeOffset.UtcNow);
            uow.Commit();
        }

        var tvCandidates = await matching.SearchTvCandidatesAsync("Stranger Things", 2016);
        Assert.NotEmpty(tvCandidates.RankedCandidates);
        var chosenTv = tvCandidates.RankedCandidates[0].Candidate;

        var matchedShow = await matching.MatchTvShowAsync(showId, chosenTv, fetchEpisodes: true);
        Assert.Equal("66732", matchedShow.ExternalId);
        Assert.Equal("Stranger Things", matchedShow.Title);

        using (var read = _uowFactory.Begin())
        {
            var seasons = read.TvSeasons.ListByShow(showId);
            Assert.NotEmpty(seasons);
            var ep = read.TvEpisodes.GetByNumber(seasons[0].Id, 1);
            Assert.NotNull(ep);
            Assert.Equal("Chapter One: The Vanishing of Will Byers", ep.Title);
            Assert.Equal(48, ep.RuntimeMinutes);
        }
    }

    [Fact]
    public async Task Live_scenario_posters_survive_restart_from_local_cache()
    {
        var posterDir = Path.Combine(_tempDir, "persisted_posters");
        Directory.CreateDirectory(posterDir);

        var validJpeg = new byte[300];
        validJpeg[0] = 0xFF; validJpeg[1] = 0xD8; validJpeg[2] = 0xFF; validJpeg[3] = 0xE0;

        // Session 1: Download and cache
        var handler1 = new SimpleMockHandler(HttpStatusCode.OK, validJpeg);
        var cache1 = new DiskPosterCache(posterDir, new HttpClient(handler1));

        var cachedPath1 = await cache1.EnsurePosterCachedAsync("TMDB", "/matrix_poster.jpg");
        Assert.NotNull(cachedPath1);
        Assert.True(File.Exists(cachedPath1));

        // Session 2: App restart simulation - brand new cache instance with failing network
        var failingHandler = new OfflineThrowingHandler();
        var cache2 = new DiskPosterCache(posterDir, new HttpClient(failingHandler));

        // Must read local file immediately without making network request
        var fastLocalPath = cache2.GetCachedPosterPath("TMDB", "/matrix_poster.jpg");
        Assert.NotNull(fastLocalPath);
        Assert.Equal(cachedPath1, fastLocalPath);

        // EnsurePosterCachedAsync must also return local path without throwing
        var path2 = await cache2.EnsurePosterCachedAsync("TMDB", "/matrix_poster.jpg");
        Assert.Equal(cachedPath1, path2);
    }

    [Fact]
    public void Live_scenario_theme_switching_and_persistence()
    {
        var settingsRepo = new SettingsRepository(_connectionString);
        var maintenanceRepo = new LibraryMaintenanceRepository(_connectionString);
        var settingsService = new SettingsService(maintenanceRepo, settings: settingsRepo);

        Assert.Equal(UiTheme.Slate, settingsService.CurrentUiTheme());
        settingsService.SetUiTheme(UiTheme.Dark);
        Assert.Equal(UiTheme.Dark, settingsService.CurrentUiTheme());
        settingsService.SetUiTheme(UiTheme.Light);
        Assert.Equal(UiTheme.Light, settingsService.CurrentUiTheme());
        settingsService.SetUiTheme(UiTheme.Slate);
        Assert.Equal(UiTheme.Slate, settingsService.CurrentUiTheme());

        // Language settings persistence
        Assert.Equal(UiLanguage.English, settingsService.CurrentUiLanguage());
        settingsService.SetUiLanguage(UiLanguage.Arabic);
        Assert.Equal(UiLanguage.Arabic, settingsService.CurrentUiLanguage());
        settingsService.SetUiLanguage(UiLanguage.English);
        Assert.Equal(UiLanguage.English, settingsService.CurrentUiLanguage());
    }

    private sealed class SimpleMockHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _code;
        private readonly byte[] _bytes;
        private readonly string? _text;

        public SimpleMockHandler(HttpStatusCode code, string text)
        {
            _code = code;
            _text = text;
            _bytes = System.Text.Encoding.UTF8.GetBytes(text);
        }

        public SimpleMockHandler(HttpStatusCode code, byte[] bytes)
        {
            _code = code;
            _bytes = bytes;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var resp = new HttpResponseMessage(_code)
            {
                Content = new ByteArrayContent(_bytes)
            };
            return Task.FromResult(resp);
        }
    }

    private sealed class OfflineThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new HttpRequestException("Host unreachable - offline test");
        }
    }

    private sealed class ComprehensiveMockMetadataProvider : IMetadataProvider
    {
        public string ProviderName => "TMDB";
        public bool IsConfigured => true;

        public Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConnectionTestResult(true, "Connected", 200));

        public IReadOnlyList<MovieCandidate> Search(MovieSearchQuery query) => [];
        public Task<IReadOnlyList<MovieCandidate>> SearchMoviesAsync(MovieSearchQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MovieCandidate>>([
                new MovieCandidate("TMDB", "157336", "Interstellar", "Interstellar", 2014, "Mankind was born on Earth. It was never meant to die here.", 8.6, "/interstellar.jpg")
            ]);

        public MovieMetadata? GetMovie(string externalId) =>
            GetMovieAsync(externalId).GetAwaiter().GetResult();

        public Task<MovieMetadata?> GetMovieAsync(string externalId, CancellationToken cancellationToken = default) =>
            Task.FromResult<MovieMetadata?>(new MovieMetadata(
                "TMDB",
                externalId,
                "Interstellar",
                "Interstellar",
                2014,
                "A team of explorers travel through a wormhole in space...",
                ["Adventure", "Drama", "Science Fiction"],
                169,
                8.6,
                "/interstellar.jpg"));

        public Task<IReadOnlyList<TvCandidate>> SearchTvAsync(TvSearchQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TvCandidate>>([
                new TvCandidate("TMDB", "66732", "Stranger Things", "Stranger Things", 2016, "When a young boy vanishes...", 8.6, "/st_poster.jpg", "/st_back.jpg")
            ]);

        public Task<TvShowMetadata?> GetTvShowAsync(string externalId, CancellationToken cancellationToken = default) =>
            Task.FromResult<TvShowMetadata?>(new TvShowMetadata(
                "TMDB",
                externalId,
                "Stranger Things",
                "Stranger Things",
                2016,
                "When a young boy vanishes, a small town uncovers a mystery...",
                ["Sci-Fi & Fantasy", "Drama", "Mystery"],
                8.6,
                "/st_poster.jpg",
                "/st_back.jpg",
                [
                    new TvSeasonMetadata(1, "Season 1", "Vanishing of Will Byers", null, "2016-07-15", [
                        new TvEpisodeMetadata(1, "Chapter One: The Vanishing of Will Byers", "On his way home from a friend's house...", 48, "2016-07-15", 8.4, "/ep1.jpg")
                    ])
                ]));

        public Task<TvSeasonMetadata?> GetTvSeasonAsync(string showExternalId, int seasonNumber, CancellationToken cancellationToken = default) =>
            Task.FromResult<TvSeasonMetadata?>(new TvSeasonMetadata(
                seasonNumber,
                $"Season {seasonNumber}",
                "Season overview",
                null,
                "2016-07-15",
                [new TvEpisodeMetadata(1, "Chapter One: The Vanishing of Will Byers", "On his way home from a friend's house...", 48, "2016-07-15", 8.4, "/ep1.jpg")]));
    }
}
