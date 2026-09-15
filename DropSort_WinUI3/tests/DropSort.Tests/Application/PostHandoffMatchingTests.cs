using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DropSort.Application.External;
using DropSort.Application.Services;
using DropSort.Domain.Library.Movies;
using DropSort.Domain.Library.Shows;
using DropSort.Domain.Metadata.Contracts;
using DropSort.Infrastructure.Persistence.Migrations;
using DropSort.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DropSort.Tests.Application;

[Collection("PostHandoffMatchingSerial")]
public sealed class PostHandoffMatchingTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"dropsort_matching_{Guid.NewGuid():N}");
    private readonly string _connectionString;
    private readonly CatalogUnitOfWorkFactory _factory;

    public PostHandoffMatchingTests()
    {
        Directory.CreateDirectory(_directory);
        _connectionString = $"Data Source={Path.Combine(_directory, "catalog.db")}";
        new DatabaseMigrator(_connectionString).Migrate();
        _factory = new CatalogUnitOfWorkFactory(_connectionString);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    [Fact]
    public async Task RematchToEmptyCatalog_ClearsProviderDescendants_PreservingRows()
    {
        var show = SeedShow("old", out var seasonId, out var episodeId);
        var provider = new BlockingMetadataProvider
        {
            Show = EmptyShow("new", "New Show")
        };

        var result = await new MetadataMatchingService(_factory, provider)
            .MatchTvShowAsync(show, new TvCandidate("TMDB", "new", "New Show", null, 2024, null, null, null), fetchEpisodes: false);

        using var uow = _factory.Begin();
        var currentShow = uow.TvShows.GetById(show)!;
        var season = uow.TvSeasons.GetById(seasonId)!;
        var episode = uow.TvEpisodes.GetById(episodeId)!;
        Assert.Equal("new", currentShow.Data.ExternalId);
        Assert.Equal(seasonId, season.Id);
        Assert.Equal(episodeId, episode.Id);
        Assert.Null(season.ExternalId);
        Assert.Null(episode.ExternalId);
        Assert.Null(episode.Title);
        Assert.Equal(result.Id, show);
    }

    [Fact]
    public async Task CanceledSeasonFetch_DoesNotCommitShowMetadata()
    {
        var show = SeedShow("old", out _, out _);
        using var cts = new CancellationTokenSource();
        var provider = new BlockingMetadataProvider { Show = ShowWithSeason("new") };
        provider.SeasonTask = new TaskCompletionSource<TvSeasonMetadata?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = new MetadataMatchingService(_factory, provider).MatchTvShowAsync(
            show, new TvCandidate("TMDB", "new", "New", null, 2024, null, null, null), ct: cts.Token);
        await provider.SeasonRequested.Task;
        cts.Cancel();
        provider.SeasonTask.SetCanceled(cts.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);

        using var uow = _factory.Begin();
        Assert.Equal("old", uow.TvShows.GetById(show)!.Data.ExternalId);
    }

    [Fact]
    public async Task ConcurrentRefreshAfterSnapshot_IsRejectedAsStale()
    {
        var show = SeedShow("old", out _, out _);
        var provider = new BlockingMetadataProvider { Show = EmptyShow("old", "Updated"), HoldShow = true };
        var task = new MetadataMatchingService(_factory, provider).RefreshTvShowMetadataAsync(show);
        await provider.ShowRequested.Task;
        using (var update = _factory.Begin())
        {
            update.TvShows.UpdateMetadata(show, new TvShowCatalogData("TMDB", "old", "Concurrent change"), DateTimeOffset.UtcNow.AddSeconds(1));
            update.Commit();
        }
        provider.ShowTask.SetResult(provider.Show);
        var error = await Assert.ThrowsAsync<MetadataServiceException>(() => task);
        Assert.Equal(MetadataFailureKind.StaleRequest, error.Kind);
    }

    [Fact]
    public async Task CancellationAtCommitBoundary_RollsBackShowAndEpisodeMetadata()
    {
        var show = SeedShow("old", out _, out var episodeId);
        using var cts = new CancellationTokenSource();
        var provider = new BlockingMetadataProvider { Show = EmptyShow("new", "New") };
        var service = new MetadataMatchingService(_factory, provider, clock: () =>
        {
            cts.Cancel();
            return DateTimeOffset.UtcNow;
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.MatchTvShowAsync(
            show, new TvCandidate("TMDB", "new", "New", null, 2024, null, null, null), fetchEpisodes: false, ct: cts.Token));

        using var uow = _factory.Begin();
        Assert.Equal("old", uow.TvShows.GetById(show)!.Data.ExternalId);
        Assert.Equal("old-episode", uow.TvEpisodes.GetById(episodeId)!.ExternalId);
    }

    [Fact]
    public async Task PartialRematch_ClearsAbsentEpisodeMetadata_PreservingMediaLink()
    {
        var show = SeedShow("old", out _, out var episodeId);
        int mediaId;
        using (var uow = _factory.Begin())
        {
            var media = uow.MediaFiles.Add(new VerifiedMediaFileFacts(
                Path.Combine(_directory, "episode.mkv"), 123, ".mkv", "1080p", "h264", "test", DateTimeOffset.UtcNow));
            uow.TvEpisodes.LinkMediaFile(episodeId, media.Id, DateTimeOffset.UtcNow);
            uow.Commit();
            mediaId = media.Id;
        }

        var provider = new BlockingMetadataProvider { Show = ShowWithReplacementEpisode("new") };
        var updated = await new MetadataMatchingService(_factory, provider).MatchTvShowAsync(
            show, new TvCandidate("TMDB", "new", "New", null, 2024, null, null, null));

        using var check = _factory.Begin();
        var episode = check.TvEpisodes.GetById(episodeId)!;
        Assert.Equal("new", updated.Data.ExternalId);
        Assert.Null(episode.ExternalId);
        Assert.Null(episode.Title);
        Assert.Equal(episodeId, check.TvEpisodes.GetEpisodeIdForMediaFile(mediaId));
        Assert.Single(check.TvEpisodes.ListMediaFiles(episodeId));
    }

    private int SeedShow(string externalId, out int seasonId, out int episodeId)
    {
        using var uow = _factory.Begin();
        var show = uow.TvShows.Create(new TvShowCatalogData("TMDB", externalId, "Show", metadataStatus: MetadataStatus.Ready), DateTimeOffset.UtcNow);
        var season = uow.TvSeasons.Create(show.Id, 1, "Old season", "Old overview", DateTimeOffset.UtcNow);
        var episode = uow.TvEpisodes.Create(season.Id, 1, "Old episode", "Old overview", 40, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        uow.TvSeasons.UpdateMetadata(season.Id, "Old season", "Old overview", "/old.jpg", "2020-01-01", "old-season", DateTimeOffset.UtcNow);
        uow.TvEpisodes.UpdateMetadata(episode.Id, "Old episode", "Old overview", 40, "2020-01-01", 8.0, "/old-still.jpg", "old-episode", DateTimeOffset.UtcNow);
        uow.Commit();
        seasonId = season.Id;
        episodeId = episode.Id;
        return show.Id;
    }

    private static TvShowMetadata EmptyShow(string id, string title) =>
        new("TMDB", id, title, null, 2024, null, ImmutableArray<string>.Empty, null, null, null, ImmutableArray<TvSeasonMetadata>.Empty);

    private static TvShowMetadata ShowWithSeason(string id) =>
        new("TMDB", id, "New", null, 2024, null, ImmutableArray<string>.Empty, null, null, null,
            ImmutableArray.Create(new TvSeasonMetadata(1, "Season", null, null, null, ImmutableArray<TvEpisodeMetadata>.Empty)));

    private static TvShowMetadata ShowWithReplacementEpisode(string id) =>
        new("TMDB", id, "New", null, 2024, null, ImmutableArray<string>.Empty, null, null, null,
            ImmutableArray.Create(new TvSeasonMetadata(1, "Season", null, null, null,
                ImmutableArray.Create(new TvEpisodeMetadata(2, "Replacement episode", null, 42, null, null, null, null)))));

    private sealed class BlockingMetadataProvider : IMetadataProvider
    {
        public string ProviderName => "TMDB";
        public bool HoldShow { get; set; }
        public TvShowMetadata? Show { get; set; }
        public TaskCompletionSource<TvShowMetadata?> ShowTask { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<TvSeasonMetadata?> SeasonTask { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> ShowRequested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> SeasonRequested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<MovieCandidate> Search(MovieSearchQuery query) => [];
        public MovieMetadata? GetMovie(string externalId) => null;

        public Task<TvShowMetadata?> GetTvShowAsync(string externalId, CancellationToken cancellationToken = default)
        {
            ShowRequested.TrySetResult(true);
            if (!HoldShow && !ShowTask.Task.IsCompleted) ShowTask.TrySetResult(Show);
            return ShowTask.Task;
        }

        public Task<TvSeasonMetadata?> GetTvSeasonAsync(string showExternalId, int seasonNumber, CancellationToken cancellationToken = default)
        {
            SeasonRequested.TrySetResult(true);
            return SeasonTask.Task;
        }
    }
}
