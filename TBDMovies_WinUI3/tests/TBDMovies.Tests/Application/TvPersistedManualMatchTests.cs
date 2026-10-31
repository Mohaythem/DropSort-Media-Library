using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TBDMovies.Application.Dto;
using TBDMovies.Application.External;
using TBDMovies.Application.Services;
using TBDMovies.Domain.Library.Movies;
using TBDMovies.Domain.Library.Shows;
using TBDMovies.Domain.Media.Matcher;
using TBDMovies.Domain.Metadata.Contracts;
using TBDMovies.FileSystem.Discovery;
using TBDMovies.Infrastructure.Persistence.Migrations;
using TBDMovies.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Xunit;

namespace TBDMovies.Tests.Application;

/// <summary>
/// The real "import a TV show with no metadata, then match it from Show Details" lifecycle, against the
/// real SQLite persistence path and a realistic multi-season TMDB shape (real system time, seasons WITHOUT
/// inline episodes fetched per-season, a Specials season, and more provider episodes than exist locally).
/// </summary>
public sealed class TvPersistedManualMatchTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"tbdmovies_tvpm_{Guid.NewGuid():N}.db");
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), $"tbdmovies_tvpm_{Guid.NewGuid():N}");
    private readonly string _connectionString;
    private readonly CatalogUnitOfWorkFactory _factory;

    public TvPersistedManualMatchTests()
    {
        _connectionString = $"Data Source={_dbPath}";
        new DatabaseMigrator(_connectionString).Migrate();
        _factory = new CatalogUnitOfWorkFactory(_connectionString);
        Directory.CreateDirectory(_workspace);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
        try { if (Directory.Exists(_workspace)) Directory.Delete(_workspace, true); } catch { }
    }

    [Fact]
    public async Task PersistedUnmatchedShow_ManualMatch_EnrichesInPlace_WithRealSystemClock()
    {
        // 1-2. Import a local show with episodes and NO provider identity, then persist it exactly as the
        // real import does (real UtcNow clock, NeedsMatch status), and commit.
        int showId = ImportLocalArabicShow(episodeCount: 3);
        using (var uow = _factory.Begin())
        {
            uow.TvShows.UpdateMetadata(showId,
                new TvShowCatalogData(null, null, uow.TvShows.GetById(showId)!.Title, metadataStatus: MetadataStatus.NeedsMatch),
                DateTimeOffset.UtcNow);
            uow.Commit();
        }

        // 3-5. Reopen as a persisted item and manually match to a TMDB identity with full seasons/episodes.
        var provider = RealisticProvider();
        var service = new MetadataMatchingService(_factory, provider); // real clock (UtcNow)
        var candidate = new TvCandidate("TMDB", "88", "الرجل العناب", null, 2020, null, null, null, null);

        var ex = await Record.ExceptionAsync(() => service.MatchTvShowAsync(showId, candidate, fetchEpisodes: true));
        Assert.Null(ex); // trace: this must NOT throw for a first manual match

        // 6. Existing local show enriched in place: identity + metadata set, no duplicate show.
        using var check = _factory.Begin();
        var show = check.TvShows.GetById(showId)!;
        Assert.Equal("TMDB", show.Data.Provider);
        Assert.Equal("88", show.Data.ExternalId);
        Assert.Equal(MetadataStatus.Ready, show.MetadataStatus);
        Assert.Single(check.TvShows.ListAll());                              // no duplicate show
        var season1 = check.TvSeasons.GetByNumber(showId, 1)!;
        var episodes = check.TvEpisodes.ListBySeason(season1.Id);
        Assert.Equal(3, episodes.Count);                                     // local episodes kept
        Assert.Equal("Pilot", episodes.Single(e => e.Number == 1).Title);   // enriched from TMDB
    }

    [Fact]
    public async Task PersistedUnmatchedShow_ManualMatch_ToIdentityOwnedByAnotherShow_FailsSafely()
    {
        var ownerShowId = ImportLocalArabicShow(episodeCount: 1);
        using (var uow = _factory.Begin())
        {
            uow.TvShows.UpdateMetadata(ownerShowId,
                new TvShowCatalogData("TMDB", "88", "Owner", metadataStatus: MetadataStatus.Ready),
                DateTimeOffset.UtcNow);
            uow.Commit();
        }

        var second = ImportLocalShow("Other Show", "Other.Show.S01E01.mkv");
        var service = new MetadataMatchingService(_factory, RealisticProvider());
        var candidate = new TvCandidate("TMDB", "88", "Owner", null, 2020, null, null, null, null);

        var error = await Assert.ThrowsAsync<MetadataServiceException>(
            () => service.MatchTvShowAsync(second, candidate, fetchEpisodes: true));
        Assert.Equal(MetadataFailureKind.IdentityConflict, error.Kind);

        using var check = _factory.Begin();
        Assert.Null(check.TvShows.GetById(second)!.Data.ExternalId); // second untouched (no partial write)
    }

    [Fact]
    public async Task PersistedUnmatchedShow_ManualMatch_SucceedsWhenTmdbReturnsEmptyStringFields()
    {
        // The real root cause: TMDB returns "" (not null) for an absent overview / original name / tagline /
        // poster / season & episode name — common for Arabic shows with no English overview. The catalog's
        // domain records reject an empty-but-non-null string, so writing "" straight through threw and the
        // whole manual match failed. Empty provider strings must fold to null and the match must succeed.
        int showId = ImportLocalArabicShow(episodeCount: 2);
        var provider = new MockProvider
        {
            Show = new TvShowMetadata("TMDB", "88", "الرجل العناب", "", 2020, "",
                ImmutableArray.Create("Drama"), 8.0, "", "",
                ImmutableArray.Create(Season(1, "Season 1")), ""),
            SeasonsByNumber =
            {
                [1] = new TvSeasonMetadata(1, "", "", "", "",
                    ImmutableArray.Create(new TvEpisodeMetadata(1, "", "", null, "", null, "", "")), ""),
            },
        };
        var service = new MetadataMatchingService(_factory, provider);
        var candidate = new TvCandidate("TMDB", "88", "الرجل العناب", null, 2020, null, null, null, null);

        var ex = await Record.ExceptionAsync(() => service.MatchTvShowAsync(showId, candidate, fetchEpisodes: true));
        Assert.Null(ex); // empty TMDB strings must not fail the match

        using var check = _factory.Begin();
        var show = check.TvShows.GetById(showId)!;
        Assert.Equal(MetadataStatus.Ready, show.MetadataStatus);
        Assert.Equal("88", show.Data.ExternalId);
        Assert.Null(show.Data.Overview);        // "" folded to null, never stored as an empty string
        Assert.Null(show.Data.OriginalTitle);
        Assert.Null(show.Data.Tagline);
    }

    private int ImportLocalArabicShow(int episodeCount)
    {
        var import = new ImportService(_factory, new NoProvider());
        int showId = 0;
        for (var i = 1; i <= episodeCount; i++)
        {
            var path = WriteMedia($"الرجل العناب الحلقة {i}.mkv");
            var result = import.RegisterEpisodeImport(Command(path));
            showId = result.Show.Id;
        }

        return showId;
    }

    private int ImportLocalShow(string _, string fileName)
    {
        var import = new ImportService(_factory, new NoProvider());
        return import.RegisterEpisodeImport(Command(WriteMedia(fileName))).Show.Id;
    }

    private static MockProvider RealisticProvider() => new()
    {
        Show = new TvShowMetadata("TMDB", "88", "الرجل العناب", "Al-Ragol Al-Enab", 2020,
            "Overview", ImmutableArray.Create("Drama"), 8.1, "/p.jpg", "/b.jpg",
            ImmutableArray.Create(
                Season(0, "Specials"),
                Season(1, "Season 1")),
            "Tagline"),
        SeasonsByNumber =
        {
            [0] = new TvSeasonMetadata(0, "Specials", null, null, null,
                ImmutableArray.Create(Episode(1, "Special")), "s0"),
            // TMDB has more episodes than exist locally; only the local ones are enriched.
            [1] = new TvSeasonMetadata(1, "Season 1", "S1", "/s1.jpg", "2020-01-01",
                ImmutableArray.Create(Episode(1, "Pilot"), Episode(2, "Two"), Episode(3, "Three"),
                    Episode(4, "Four"), Episode(5, "Five")), "s1"),
        },
    };

    private static TvSeasonMetadata Season(int number, string name) =>
        new(number, name, null, null, null, ImmutableArray<TvEpisodeMetadata>.Empty, number.ToString());

    private static TvEpisodeMetadata Episode(int number, string title) =>
        new(number, title, "Overview", 42, "2020-01-01", 8.0, "/still.jpg", $"e{number}");

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

    private sealed class MockProvider : IMetadataProvider
    {
        public string ProviderName => "TMDB";
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
