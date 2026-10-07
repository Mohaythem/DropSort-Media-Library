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
using DropSort.Domain.Library.Movies;
using DropSort.Domain.Library.Shows;
using DropSort.Domain.Media.Matcher;
using DropSort.Domain.Metadata.Contracts;
using DropSort.FileSystem.Discovery;
using DropSort.Infrastructure.Persistence.Migrations;
using DropSort.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DropSort.Tests.Application;

/// <summary>
/// Reproduction harness for the manual TV "Apply Match" failure, exercised against the REAL SQLite
/// repositories (existing MetadataMatchingService TV tests use in-memory fakes that skip domain-model
/// validation and DB constraints). Registers a local show with episodes, then applies a realistic
/// multi-season TMDB shape (seasons returned WITHOUT inline episodes, fetched one-by-one, including a
/// Specials/Season 0) and observes what happens.
/// </summary>
public sealed class TvManualMatchReproTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _connectionString;
    private readonly string _workspace;
    private readonly CatalogUnitOfWorkFactory _factory;

    public TvManualMatchReproTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"dropsort_tvmm_{Guid.NewGuid():N}.db");
        _connectionString = $"Data Source={_dbPath}";
        new DatabaseMigrator(_connectionString).Migrate();
        _factory = new CatalogUnitOfWorkFactory(_connectionString);
        _workspace = Path.Combine(Path.GetTempPath(), $"dropsort_tvmm_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workspace);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
        try { if (Directory.Exists(_workspace)) Directory.Delete(_workspace, true); } catch { }
    }

    [Fact]
    public async Task FirstManualMatch_WithSpecialsSeasonThatFailsToFetch_StillCompletesAndKeepsEpisodes()
    {
        var showId = RegisterLocalShow();

        // Realistic TMDB shape: GetTvShowAsync returns seasons WITHOUT inline episodes; a Specials (0) and
        // a real season 1 are listed. The per-season fetch for Specials returns null (a 404), which is
        // common; season 1 fetches fine. A first-time manual match must NOT abort over the un-fetchable
        // Specials — it stores the Specials stub and enriches season 1.
        var provider = new PerSeasonProvider
        {
            Show = new TvShowMetadata("tmdb", "prison", "Prison Break", "Prison Break", 2005,
                "Overview", ImmutableArray.Create("Drama"), 8.3, "/p.jpg", "/b.jpg",
                ImmutableArray.Create(
                    Season(0, "Specials"),
                    Season(1, "Season 1")),
                "Tagline"),
            SeasonsByNumber =
            {
                [0] = null, // Specials fetch 404s
                [1] = new TvSeasonMetadata(1, "Season 1", "S1", "/s1.jpg", "2005-08-29",
                    ImmutableArray.Create(
                        Episode(1, "Pilot"),
                        Episode(2, "Allen")),
                    "s1id"),
            },
        };

        var service = new MetadataMatchingService(_factory, provider);
        var candidate = new TvCandidate("tmdb", "prison", "Prison Break", null, 2005, null, null, null, null);

        var updated = await service.MatchTvShowAsync(showId, candidate, fetchEpisodes: true);

        Assert.Equal(MetadataStatus.Ready, updated.MetadataStatus);
        Assert.Equal("prison", updated.Data.ExternalId);
        using var uow = _factory.Begin();
        var seasons = uow.TvSeasons.ListByShow(showId);
        // Both the Specials stub (0) and season 1 exist; the local episodes are still attached and enriched.
        Assert.Contains(seasons, s => s.Number == 0);
        var season1 = seasons.Single(s => s.Number == 1);
        var episodes = uow.TvEpisodes.ListBySeason(season1.Id);
        Assert.Equal(2, episodes.Count);                          // the two local episodes remain
        Assert.Equal("Pilot", episodes.Single(e => e.Number == 1).Title);   // enriched from TMDB
    }

    [Fact]
    public async Task FirstManualMatch_CleanMultiSeasonIncludingSpecials_Succeeds()
    {
        var showId = RegisterLocalShow();
        var provider = new PerSeasonProvider
        {
            Show = new TvShowMetadata("tmdb", "prison", "Prison Break", "Prison Break", 2005,
                "Overview", ImmutableArray.Create("Drama"), 8.3, "/p.jpg", "/b.jpg",
                ImmutableArray.Create(Season(0, "Specials"), Season(1, "Season 1")),
                "Tagline"),
            SeasonsByNumber =
            {
                [0] = new TvSeasonMetadata(0, "Specials", null, null, null,
                    ImmutableArray.Create(Episode(1, "Special 1")), "s0id"),
                [1] = new TvSeasonMetadata(1, "Season 1", "S1", "/s1.jpg", "2005-08-29",
                    ImmutableArray.Create(Episode(1, "Pilot"), Episode(2, "Allen"), Episode(3, "Cell Test")), "s1id"),
            },
        };
        var service = new MetadataMatchingService(_factory, provider);
        var candidate = new TvCandidate("tmdb", "prison", "Prison Break", null, 2005, null, null, null, null);

        var updated = await service.MatchTvShowAsync(showId, candidate, fetchEpisodes: true);
        Assert.Equal(MetadataStatus.Ready, updated.MetadataStatus);
    }

    private int RegisterLocalShow()
    {
        var import = new ImportService(_factory, new NoProvider());
        var first = import.RegisterEpisodeImport(Command(WriteMedia("Prison.Break.S01E01.1080p.mkv")));
        import.RegisterEpisodeImport(Command(WriteMedia("Prison.Break.S01E02.1080p.mkv")));
        return first.Show.Id;
    }

    private static TvSeasonMetadata Season(int number, string name) =>
        new(number, name, null, null, null, ImmutableArray<TvEpisodeMetadata>.Empty, number.ToString());

    private static TvEpisodeMetadata Episode(int number, string title) =>
        new(number, title, "Overview", 42, "2005-08-29", 8.0, "/still.jpg", $"e{number}");

    private string WriteMedia(string fileName)
    {
        var path = Path.Combine(_workspace, fileName);
        File.WriteAllBytes(path, new byte[2048]);
        return path;
    }

    private ConfirmEpisodeImportCommand Command(string path)
    {
        var discovered = new MediaDiscoveryService()
            .Discover(Path.GetDirectoryName(path)!, recursive: false)
            .Single(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase));
        return new ConfirmEpisodeImportCommand(path, discovered.FileSize ?? 0, discovered.ParsedMedia!, DateTimeOffset.UtcNow);
    }

    private sealed class PerSeasonProvider : IMetadataProvider
    {
        public string ProviderName => "tmdb";
        public bool IsConfigured => true;
        public TvShowMetadata? Show { get; init; }
        public Dictionary<int, TvSeasonMetadata?> SeasonsByNumber { get; init; } = new();
        public IReadOnlyList<MovieCandidate> Search(MovieSearchQuery query) => [];
        public MovieMetadata? GetMovie(string externalId) => null;
        public Task<TvShowMetadata?> GetTvShowAsync(string externalId, CancellationToken ct = default) =>
            Task.FromResult(Show);
        public Task<TvSeasonMetadata?> GetTvSeasonAsync(string showExternalId, int seasonNumber, CancellationToken ct = default) =>
            Task.FromResult(SeasonsByNumber.TryGetValue(seasonNumber, out var s) ? s : null);
    }

    private sealed class NoProvider : IMetadataProvider
    {
        public string ProviderName => "none";
        public IReadOnlyList<MovieCandidate> Search(MovieSearchQuery query) => [];
        public MovieMetadata? GetMovie(string externalId) => null;
    }
}
