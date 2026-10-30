using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DropSort.Application.Dto;
using DropSort.Application.External;
using DropSort.Application.Repositories;
using DropSort.Application.Services;
using DropSort.Domain.Core.Operations;
using DropSort.Domain.Library.Movies;
using DropSort.Domain.Library.Shows;
using DropSort.Domain.Media.Discovery;
using DropSort.Domain.Media.Parser;
using DropSort.Domain.Metadata.Contracts;
using DropSort.FileSystem.Discovery;
using DropSort.FileSystem.Inspection;
using DropSort.FileSystem.Operations;
using DropSort.Infrastructure.Metadata.Tmdb;
using DropSort.Infrastructure.Persistence.Migrations;
using DropSort.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DropSort.Tests.Application;

public sealed class TmdbOfflineAndIdempotencyTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _connectionString;
    private readonly CatalogUnitOfWorkFactory _uowFactory;

    public TmdbOfflineAndIdempotencyTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "DropSort_TmdbOfflineTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        var dbPath = Path.Combine(_tempDir, "dropsort_test.db");
        _connectionString = $"Data Source={dbPath}";
        var migrator = new DatabaseMigrator(_connectionString);
        migrator.Migrate();
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
    public void Local_workflows_add_check_and_relink_function_normally_without_tmdb()
    {
        // 1. Arrange: Unconfigured metadata provider (TMDB offline)
        var unconfigured = new OfflineFailingMetadataProvider();
        var discovery = new MediaDiscoveryService();
        var import = new ImportService(_uowFactory, unconfigured, discovery);

        var movieRepo = new PerCallMovieRepository(_uowFactory);
        var mediaFileRepo = new PerCallMediaFileRepository(_uowFactory);

        // 2. Add local movie file
        var mediaDir = Path.Combine(_tempDir, "Media");
        Directory.CreateDirectory(mediaDir);
        var originalFile = Path.Combine(mediaDir, "The.Matrix.1999.1080p.mkv");
        File.WriteAllBytes(originalFile, new byte[1024]);

        var parsed = new ParsedMedia(
            "The.Matrix.1999.1080p.mkv",
            MediaType.Movie,
            "The Matrix",
            1999,
            "1080p",
            null,
            "x264",
            ".mkv");

        var result = import.RegisterMovieImport(new ConfirmMovieImportCommand(
            originalFile,
            1024,
            parsed,
            DateTimeOffset.UtcNow));

        Assert.NotNull(result.Movie);
        Assert.Equal("The Matrix", result.Movie.Title);
        Assert.Equal(1999, result.Movie.Year);
        Assert.Equal(MetadataStatus.Pending, result.Movie.MetadataStatus);
        Assert.Null(result.Movie.ExternalId);

        // 3. Health check: file is present
        var inspector = new AvailabilityInspector();
        var reconciliation = new ReconciliationService(mediaFileRepo, inspector, movieRepo);
        var health = reconciliation.CheckLibrary();
        Assert.Equal(0, health.FileProgress.MissingFiles);

        // 4. Move file outside app: reports missing
        var missingTarget = Path.Combine(_tempDir, "Relocated", "The.Matrix.1999.1080p.mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(missingTarget)!);
        File.Move(originalFile, missingTarget);

        var healthMissing = reconciliation.CheckLibrary();
        Assert.Equal(1, healthMissing.FileProgress.MissingFiles);

        // 5. Relink file: repairs path
        var prep = reconciliation.PrepareMediaRelink(result.MediaFile.Id, missingTarget);
        Assert.NotNull(prep);

        var relinked = reconciliation.ConfirmMediaRelink(prep.PreviewId);
        Assert.Equal(MediaFileStatus.Present, relinked.MediaFile.Status);
        Assert.Equal(missingTarget, relinked.MediaFile.CurrentPath);

        // 6. Final health check: 0 missing
        var healthRestored = reconciliation.CheckLibrary();
        Assert.Equal(0, healthRestored.FileProgress.MissingFiles);
    }

    [Fact]
    public async Task Repeated_matching_is_idempotent_and_preserves_local_movie_file()
    {
        var mockProvider = new MockTestMetadataProvider();
        var matching = new MetadataMatchingService(_uowFactory, mockProvider);

        var mediaDir = Path.Combine(_tempDir, "Media2");
        Directory.CreateDirectory(mediaDir);
        var originalFile = Path.Combine(mediaDir, "Inception.2010.mkv");
        File.WriteAllBytes(originalFile, new byte[2048]);

        // Create movie in DB
        int movieId;
        int mediaFileId;
        using (var uow = _uowFactory.Begin())
        {
            var movie = uow.Movies.Create(
                new MovieCatalogData(null, null, "Inception", null, 2010, null, [], null, null, null, MetadataStatus.Pending),
                DateTimeOffset.UtcNow);
            movieId = movie.Id;
            var file = uow.MediaFiles.Add(
                new VerifiedMediaFileFacts(originalFile, 2048, ".mkv", "1080p", "h264", "disc", DateTimeOffset.UtcNow),
                movieId);
            mediaFileId = file.Id;
            uow.Commit();
        }

        // First match
        var candidate = new MovieCandidate("TMDB", "27205", "Inception", "Inception", 2010, "A thief who steals corporate secrets...", 8.4, "/poster.jpg");
        var matched1 = await matching.MatchMovieAsync(movieId, candidate);

        Assert.Equal("27205", matched1.ExternalId);
        Assert.Equal("Inception", matched1.Title);
        Assert.Equal(MetadataStatus.Ready, matched1.MetadataStatus);

        // Second match (idempotent repeated call)
        var matched2 = await matching.MatchMovieAsync(movieId, candidate);
        Assert.Equal("27205", matched2.ExternalId);

        // Third match: Refresh metadata using external ID
        var refreshed = await matching.RefreshMovieMetadataAsync(movieId);
        Assert.NotNull(refreshed);
        Assert.Equal("27205", refreshed.ExternalId);
        Assert.Equal("Inception", refreshed.Title);

        // Verify media file is still present and intact
        using (var uow = _uowFactory.Begin())
        {
            var file = uow.MediaFiles.GetById(mediaFileId);
            Assert.NotNull(file);
            Assert.Equal(movieId, file.MovieId);
            Assert.Equal(originalFile, file.CurrentPath);
            Assert.True(File.Exists(originalFile));
        }
    }

    [Fact]
    public async Task Tv_matching_preserves_existing_episode_media_files()
    {
        var mockProvider = new MockTestMetadataProvider();
        var matching = new MetadataMatchingService(_uowFactory, mockProvider);

        var mediaDir = Path.Combine(_tempDir, "TvMedia");
        Directory.CreateDirectory(mediaDir);
        var epFile = Path.Combine(mediaDir, "Breaking.Bad.S01E01.mkv");
        File.WriteAllBytes(epFile, new byte[3000]);

        int showId;
        int episodeId;
        int mediaFileId;

        using (var uow = _uowFactory.Begin())
        {
            var show = uow.TvShows.Create(new TvShowCatalogData(null, null, "Breaking Bad"), DateTimeOffset.UtcNow);
            showId = show.Id;

            var season = uow.TvSeasons.Create(showId, 1, "Season 1", null, DateTimeOffset.UtcNow);
            var episode = uow.TvEpisodes.Create(season.Id, 1, "Episode 1", null, null, null, DateTimeOffset.UtcNow);
            episodeId = episode.Id;

            var file = uow.MediaFiles.Add(new VerifiedMediaFileFacts(epFile, 3000, ".mkv", "1080p", "h264", "disc", DateTimeOffset.UtcNow));
            mediaFileId = file.Id;

            uow.TvEpisodes.LinkMediaFile(episodeId, mediaFileId, DateTimeOffset.UtcNow);
            uow.Commit();
        }

        // Match TV show
        var tvCandidate = new TvCandidate("TMDB", "1396", "Breaking Bad", "Breaking Bad", 2008, "A high school chemistry teacher diagnosed with lung cancer...", 8.9, "/bb_poster.jpg", "/bb_back.jpg");
        var matchedShow = await matching.MatchTvShowAsync(showId, tvCandidate, fetchEpisodes: true);

        Assert.Equal("1396", matchedShow.ExternalId);
        Assert.Equal(MetadataStatus.Ready, matchedShow.MetadataStatus);

        // Verify that the episode was enriched AND its media file is still linked and exists
        using (var uow = _uowFactory.Begin())
        {
            var ep = uow.TvEpisodes.GetById(episodeId);
            Assert.NotNull(ep);
            Assert.Equal("Pilot", ep.Title); // Updated from TMDB
            Assert.Equal(58, ep.RuntimeMinutes);

            var files = uow.TvEpisodes.ListMediaFiles(episodeId);
            Assert.Single(files);
            Assert.Equal(mediaFileId, files[0].Id);
            Assert.Equal(epFile, files[0].CurrentPath);
            Assert.True(File.Exists(epFile));
        }
    }

    [Fact]
    public async Task TmdbClient_handles_request_timeout_gracefully()
    {
        var handler = new ThrowingTimeoutHttpMessageHandler();
        var client = new HttpClient(handler);
        using var tmdb = new TmdbClient(() => "mock_token", client);

        var result = await tmdb.TestConnectionAsync();
        Assert.False(result.Success);
        Assert.Contains("timed out", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class OfflineFailingMetadataProvider : IMetadataProvider
    {
        public string ProviderName => "OFFLINE_TEST";
        public bool IsConfigured => false;

        public Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConnectionTestResult(false, "Offline mode"));

        public IReadOnlyList<MovieCandidate> Search(MovieSearchQuery query) => [];
        public Task<IReadOnlyList<MovieCandidate>> SearchMoviesAsync(MovieSearchQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MovieCandidate>>([]);

        public MovieMetadata? GetMovie(string externalId) => null;
        public Task<MovieMetadata?> GetMovieAsync(string externalId, CancellationToken cancellationToken = default) =>
            Task.FromResult<MovieMetadata?>(null);

        public Task<IReadOnlyList<TvCandidate>> SearchTvAsync(TvSearchQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TvCandidate>>([]);

        public Task<TvShowMetadata?> GetTvShowAsync(string externalId, CancellationToken cancellationToken = default) =>
            Task.FromResult<TvShowMetadata?>(null);

        public Task<TvSeasonMetadata?> GetTvSeasonAsync(string showExternalId, int seasonNumber, CancellationToken cancellationToken = default) =>
            Task.FromResult<TvSeasonMetadata?>(null);
    }

    private sealed class MockTestMetadataProvider : IMetadataProvider
    {
        public string ProviderName => "TMDB";
        public bool IsConfigured => true;

        public Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConnectionTestResult(true, "Connected", 200));

        public IReadOnlyList<MovieCandidate> Search(MovieSearchQuery query) => [];
        public Task<IReadOnlyList<MovieCandidate>> SearchMoviesAsync(MovieSearchQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MovieCandidate>>([]);

        public MovieMetadata? GetMovie(string externalId) =>
            GetMovieAsync(externalId).GetAwaiter().GetResult();

        public Task<MovieMetadata?> GetMovieAsync(string externalId, CancellationToken cancellationToken = default) =>
            Task.FromResult<MovieMetadata?>(new MovieMetadata(
                "TMDB",
                externalId,
                "Inception",
                "Inception",
                2010,
                "A thief who steals corporate secrets...",
                ["Action", "Science Fiction"],
                148,
                8.4,
                "/poster.jpg"));

        public Task<IReadOnlyList<TvCandidate>> SearchTvAsync(TvSearchQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TvCandidate>>([]);

        public Task<TvShowMetadata?> GetTvShowAsync(string externalId, CancellationToken cancellationToken = default) =>
            Task.FromResult<TvShowMetadata?>(new TvShowMetadata(
                "TMDB",
                externalId,
                "Breaking Bad",
                "Breaking Bad",
                2008,
                "A high school chemistry teacher...",
                ["Drama", "Crime"],
                8.9,
                "/bb_poster.jpg",
                "/bb_back.jpg",
                [
                    new TvSeasonMetadata(1, "Season 1", "Overview 1", null, "2008-01-20", [
                        new TvEpisodeMetadata(1, "Pilot", "Walter White turns to crime", 58, "2008-01-20", 8.5, "/still.jpg")
                    ])
                ]));

        public Task<TvSeasonMetadata?> GetTvSeasonAsync(string showExternalId, int seasonNumber, CancellationToken cancellationToken = default) =>
            Task.FromResult<TvSeasonMetadata?>(new TvSeasonMetadata(
                seasonNumber,
                $"Season {seasonNumber}",
                $"Overview {seasonNumber}",
                null,
                "2008-01-20",
                [new TvEpisodeMetadata(1, "Pilot", "Walter White turns to crime", 58, "2008-01-20", 8.5, "/still.jpg")]));
    }

    private sealed class ThrowingTimeoutHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new OperationCanceledException("The request was canceled due to timeout.");
        }
    }
}
