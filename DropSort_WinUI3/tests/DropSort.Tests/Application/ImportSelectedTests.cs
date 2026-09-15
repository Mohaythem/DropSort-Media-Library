using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DropSort.Application.Dto;
using DropSort.Application.External;
using DropSort.Application.Services;
using DropSort.Domain.Media.Discovery;
using DropSort.Domain.Media.Parser;
using DropSort.Domain.Library.Movies;
using DropSort.Domain.Metadata.Contracts;
using DropSort.FileSystem.Discovery;
using DropSort.Infrastructure.Persistence.Migrations;
using DropSort.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DropSort.Tests.Application;

[Collection("ImportSelectedSerial")]
public sealed class ImportSelectedTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dropsort_import_" + Guid.NewGuid().ToString("N"));
    private readonly CatalogUnitOfWorkFactory _factory;

    public ImportSelectedTests()
    {
        Directory.CreateDirectory(_root);
        var connection = "Data Source=" + Path.Combine(_root, "catalog.db");
        new DatabaseMigrator(connection).Migrate();
        _factory = new CatalogUnitOfWorkFactory(connection);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [Fact]
    public async Task MixedSelection_RegistersOnlySelectedItems()
    {
        var provider = new CountingProvider();
        var service = CreateService(provider);
        var movie = Discovered("Movie.mkv", MediaType.Movie, "Movie", 2020);
        var episode = Discovered("Show.S01E01.mkv", MediaType.TvEpisode, "Show", null, 1, 1);

        var result = await service.ImportSelectedAsync([new ImportSelection(movie), new ImportSelection(episode)]);

        Assert.Equal(2, result.RegisteredCount);
        Assert.Equal(2, result.Items.Count);
        using var uow = _factory.Begin();
        Assert.Single(uow.Movies.ListAll());
        Assert.Single(uow.TvShows.ListAll());
    }

    [Fact]
    public void MixedDiscovery_ClassifiesMovieAndTvEpisodeTogether()
    {
        File.WriteAllBytes(Path.Combine(_root, "Film (2020).mkv"), [1]);
        File.WriteAllBytes(Path.Combine(_root, "Show.S01E02.mkv"), [2]);

        var items = new MediaDiscoveryService().Discover(_root, recursive: true);

        Assert.Contains(items, item => item.Classification == DiscoveryClassification.MovieCandidate);
        Assert.Contains(items, item => item.Classification == DiscoveryClassification.TvEpisodeCandidate);
    }

    [Fact]
    public async Task UnselectedItem_IsNotRegistered()
    {
        var provider = new CountingProvider();
        var service = CreateService(provider);
        var selected = Discovered("Selected.mkv", MediaType.Movie, "Selected", 2020);
        var unselected = Discovered("Unselected.S01E01.mkv", MediaType.TvEpisode, "Unselected", null, 1, 1);

        var result = await service.ImportSelectedAsync([new ImportSelection(selected)]);

        Assert.Equal(1, result.RegisteredCount);
        using var uow = _factory.Begin();
        Assert.Single(uow.Movies.ListAll());
        Assert.Empty(uow.TvShows.ListAll());
        Assert.DoesNotContain(result.Items, item => item.Path == unselected.Path);
    }

    [Fact]
    public async Task CancelledSelection_LeavesCatalogUnchanged()
    {
        var provider = new CountingProvider();
        var service = CreateService(provider);
        var selected = Discovered("Cancelled.mkv", MediaType.Movie, "Cancelled", 2020);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await service.ImportSelectedAsync([new ImportSelection(selected)], cancellation.Token);

        Assert.Empty(result.Items);
        using var uow = _factory.Begin();
        Assert.Empty(uow.Movies.ListAll());
        Assert.Empty(uow.TvShows.ListAll());
    }

    [Fact]
    public async Task MatchedMovie_IsEnriched_AndAmbiguousMovieStaysPending()
    {
        var provider = new CountingProvider { MovieCandidates = [Candidate("Exact", "1"), Candidate("Exact", "2")] };
        var service = CreateService(provider);
        var selected = Discovered("Exact.mkv", MediaType.Movie, "Exact", 2020);

        var result = await service.ImportSelectedAsync([new ImportSelection(selected)]);

        Assert.True(result.Items[0].Registered);
        Assert.False(result.Items[0].AutoMatched);
        using var uow = _factory.Begin();
        Assert.Equal(MetadataStatus.Pending, uow.Movies.ListAll().Single().MetadataStatus);
    }

    [Fact]
    public async Task HighConfidenceMovie_IsAutoMatched_AndReady()
    {
        var provider = new CountingProvider { MovieCandidates = [Candidate("Exact", "1")] };
        var service = CreateService(provider);
        var selected = Discovered("Exact.mkv", MediaType.Movie, "Exact", 2020);

        var result = await service.ImportSelectedAsync([new ImportSelection(selected)]);

        Assert.True(result.Items[0].Registered);
        Assert.True(result.Items[0].AutoMatched);
        using var uow = _factory.Begin();
        var movie = uow.Movies.ListAll().Single();
        Assert.Equal(MetadataStatus.Ready, movie.MetadataStatus);
        Assert.Equal("1", movie.ExternalId);
    }

    [Fact]
    public async Task AllSelectedItems_RegisterBeforeSlowMetadataSearch()
    {
        var gate = new TaskCompletionSource<IReadOnlyList<MovieCandidate>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new CountingProvider { MovieSearchGate = gate };
        var service = CreateService(provider);
        var first = Discovered("First.mkv", MediaType.Movie, "First", 2020);
        var second = Discovered("Second.mkv", MediaType.Movie, "Second", 2021);

        var importTask = service.ImportSelectedAsync([
            new ImportSelection(first),
            new ImportSelection(second)]);
        await provider.MovieSearchStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        using (var uow = _factory.Begin())
        {
            Assert.Equal(2, uow.Movies.ListAll().Count);
        }

        gate.SetResult([]);
        var result = await importTask;
        Assert.Equal(2, result.RegisteredCount);
    }

    [Fact]
    public async Task ManualMatchDuringAutomaticSearch_IsNotOverwritten()
    {
        var gate = new TaskCompletionSource<IReadOnlyList<MovieCandidate>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var automaticCandidate = Candidate("Automatic", "automatic-1");
        var provider = new CountingProvider
        {
            MovieCandidates = [automaticCandidate],
            MovieSearchGate = gate,
        };
        var matching = new MetadataMatchingService(_factory, provider);
        var service = new ImportService(_factory, provider, matching: matching);
        var selected = Discovered("Race.mkv", MediaType.Movie, "Race", 2020);

        var importTask = service.ImportSelectedAsync([new ImportSelection(selected)]);
        await provider.MovieSearchStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        int movieId;
        using (var uow = _factory.Begin())
        {
            movieId = uow.Movies.ListAll().Single().Id;
        }

        await matching.MatchMovieAsync(movieId, Candidate("Manual", "manual-1"));
        gate.SetResult([automaticCandidate]);

        var result = await importTask;

        Assert.False(result.Items[0].AutoMatched);
        using var finalUow = _factory.Begin();
        var movie = finalUow.Movies.GetById(movieId)!;
        Assert.Equal("manual-1", movie.ExternalId);
    }

    [Fact]
    public async Task ProviderFailureAfterRegistration_PreservesLocalMovie()
    {
        var provider = new CountingProvider { ThrowOnMovieSearch = true };
        var service = CreateService(provider);
        var selected = Discovered("Offline.mkv", MediaType.Movie, "Offline", null);

        var result = await service.ImportSelectedAsync([new ImportSelection(selected)]);

        Assert.True(result.Items[0].Registered);
        Assert.False(result.Items[0].AutoMatched);
        using var uow = _factory.Begin();
        Assert.Single(uow.Movies.ListAll());
    }

    [Fact]
    public async Task UnconfiguredProvider_SkipsMetadataAndPreservesLocalMovie()
    {
        var provider = new CountingProvider { IsConfigured = false, ThrowOnMovieSearch = true };
        var service = CreateService(provider);
        var selected = Discovered("Unconfigured.mkv", MediaType.Movie, "Unconfigured", null);

        var result = await service.ImportSelectedAsync([new ImportSelection(selected)]);

        Assert.True(result.Items[0].Registered);
        Assert.False(result.Items[0].AutoMatched);
        Assert.Null(result.Items[0].MatchStatus);
        using var uow = _factory.Begin();
        Assert.Single(uow.Movies.ListAll());
    }

    [Fact]
    public async Task PosterAcquisition_DoesNotBlockLocalRegistration()
    {
        var provider = new CountingProvider { MovieCandidates = [Candidate("Poster", "poster-1")] };
        var poster = new BlockingPosterService();
        var service = new ImportService(
            _factory,
            provider,
            matching: new MetadataMatchingService(_factory, provider, poster));
        var selected = Discovered("Poster.mkv", MediaType.Movie, "Poster", 2020);

        var importTask = service.ImportSelectedAsync([new ImportSelection(selected)]);
        var completed = await Task.WhenAny(importTask, Task.Delay(TimeSpan.FromSeconds(2)));

        Assert.Same(importTask, completed);
        var result = await importTask;
        Assert.True(result.Items[0].Registered);
        Assert.True(result.Items[0].AutoMatched);
        using var uow = _factory.Begin();
        Assert.Single(uow.Movies.ListAll());
    }

    [Fact]
    public async Task CanceledProviderSearch_PreservesLocalMovie()
    {
        var provider = new CountingProvider { CancelMovieSearch = true };
        var service = CreateService(provider);
        var selected = Discovered("Canceled.mkv", MediaType.Movie, "Canceled", null);

        var result = await service.ImportSelectedAsync([new ImportSelection(selected)]);

        Assert.True(result.Items[0].Registered);
        using var uow = _factory.Begin();
        Assert.Single(uow.Movies.ListAll());
    }

    [Fact]
    public async Task DuplicateTvEpisodes_SearchAndMatchShowOnlyOnce()
    {
        var provider = new CountingProvider { TvCandidates = [new TvCandidate("tmdb", "show-1", "Show", null, 2020, null, null, null)] };
        var service = CreateService(provider);
        var first = Discovered("Show.S01E01.mkv", MediaType.TvEpisode, "Show", 2020, 1, 1);
        var second = Discovered("Show.S01E02.mkv", MediaType.TvEpisode, "Show", 2020, 1, 2);

        var result = await service.ImportSelectedAsync([new ImportSelection(first), new ImportSelection(second)]);

        Assert.Equal(2, result.RegisteredCount);
        Assert.Equal(1, provider.TvSearchCount);
        Assert.Equal(1, provider.TvMatchCount);
        Assert.All(result.Items, item => Assert.True(item.AutoMatched));
    }

    [Fact]
    public async Task NewEpisodeOnMatchedShow_RefreshesStoredIdentityWithoutRematch()
    {
        var provider = new CountingProvider
        {
            TvCandidates = [new TvCandidate("tmdb", "show-1", "Show", null, 2020, null, null, null)],
            TvMetadata = new TvShowMetadata(
                "tmdb",
                "show-1",
                "Show",
                null,
                2020,
                "Overview",
                ImmutableArray<string>.Empty,
                null,
                "/show.jpg",
                null,
                [new TvSeasonMetadata(
                    1,
                    "Season 1",
                    null,
                    null,
                    null,
                    [new TvEpisodeMetadata(2, "Episode Two", null, 42, null, null, null, "ep-2")])])
        };
        var service = CreateService(provider);

        await service.ImportSelectedAsync([
            new ImportSelection(Discovered("Show.S01E01.mkv", MediaType.TvEpisode, "Show", 2020, 1, 1))]);
        var secondResult = await service.ImportSelectedAsync([
            new ImportSelection(Discovered("Show.S01E02.mkv", MediaType.TvEpisode, "Show", 2020, 1, 2))]);

        Assert.True(secondResult.Items[0].AutoMatched);
        Assert.Equal(1, provider.TvSearchCount);
        Assert.Equal(2, provider.TvMatchCount);
        using var uow = _factory.Begin();
        var show = uow.TvShows.ListAll().Single();
        Assert.Equal("show-1", show.ExternalId);
        var episode = uow.TvEpisodes.ListByShow(show.Id).Single(item => item.Number == 2);
        Assert.Equal("Episode Two", episode.Title);
        Assert.Equal("ep-2", episode.ExternalId);
    }

    private ImportService CreateService(CountingProvider provider) =>
        new(_factory, provider, matching: new MetadataMatchingService(_factory, provider));

    private DiscoveredMedia Discovered(string fileName, MediaType type, string title, int? year,
        int? season = null, int? episode = null)
    {
        var path = Path.Combine(_root, fileName);
        return new DiscoveredMedia(path, 100, new ParsedMedia(fileName, type, title, year,
            "1080p", "web", "h264", ".mkv", season, episode),
            type == MediaType.Movie ? DiscoveryClassification.MovieCandidate : DiscoveryClassification.TvEpisodeCandidate, null);
    }

    private static MovieCandidate Candidate(string title, string id) =>
        new("tmdb", id, title, null, 2020, null, null, null);

    private sealed class CountingProvider : IMetadataProvider
    {
        public string ProviderName => "tmdb";
        public bool IsConfigured { get; init; } = true;
        public IReadOnlyList<MovieCandidate> MovieCandidates { get; init; } = [Candidate("Movie", "movie-1")];
        public TaskCompletionSource<IReadOnlyList<MovieCandidate>>? MovieSearchGate { get; init; }
        public TaskCompletionSource MovieSearchStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyList<TvCandidate> TvCandidates { get; init; } = [];
        public TvShowMetadata? TvMetadata { get; init; }
        public bool ThrowOnMovieSearch { get; init; }
        public bool CancelMovieSearch { get; init; }
        public int TvSearchCount { get; private set; }
        public int TvMatchCount { get; private set; }

        public IReadOnlyList<MovieCandidate> Search(MovieSearchQuery query) => MovieCandidates;
        public MovieMetadata? GetMovie(string externalId) => new("tmdb", externalId, "Movie", null, 2020,
            null, ImmutableArray<string>.Empty, null, null, "/movie.jpg");
        public Task<IReadOnlyList<MovieCandidate>> SearchMoviesAsync(MovieSearchQuery query, CancellationToken ct = default)
        {
            if (ThrowOnMovieSearch) throw new InvalidOperationException("offline");
            if (CancelMovieSearch) throw new OperationCanceledException(ct);
            if (MovieSearchGate is { } gate)
            {
                MovieSearchStarted.TrySetResult();
                return gate.Task;
            }
            return Task.FromResult(MovieCandidates);
        }
        public Task<MovieMetadata?> GetMovieAsync(string externalId, CancellationToken ct = default) =>
            Task.FromResult(GetMovie(externalId));
        public Task<IReadOnlyList<TvCandidate>> SearchTvAsync(TvSearchQuery query, CancellationToken ct = default)
        {
            TvSearchCount++;
            return Task.FromResult(TvCandidates);
        }
        public Task<TvShowMetadata?> GetTvShowAsync(string externalId, CancellationToken ct = default)
        {
            TvMatchCount++;
            return Task.FromResult<TvShowMetadata?>(TvMetadata ?? new TvShowMetadata("tmdb", externalId, "Show", null, 2020,
                null, ImmutableArray<string>.Empty, null, null, null, ImmutableArray<TvSeasonMetadata>.Empty));
        }
    }

    private sealed class BlockingPosterService : IPosterService
    {
        public PosterAsset? LoadPoster(PosterRequest request) => null;

        public Task<string?> EnsurePosterCachedAsync(
            string provider,
            string reference,
            CancellationToken cancellationToken = default) =>
            new TaskCompletionSource<string?>().Task;
    }
}
