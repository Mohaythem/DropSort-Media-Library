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
using TBDMovies.Domain.Media.Discovery;
using TBDMovies.Domain.Media.Parser;
using TBDMovies.Domain.Metadata.Contracts;
using TBDMovies.FileSystem.Discovery;
using TBDMovies.Infrastructure.Persistence.Migrations;
using TBDMovies.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Xunit;

namespace TBDMovies.Tests.Application;

public sealed class TvMissingEpisodeGapsTests : IDisposable
{
    private readonly string _workspace;
    private readonly string _dbPath;
    private readonly string _connectionString;
    private readonly CatalogUnitOfWorkFactory _factory;

    public TvMissingEpisodeGapsTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), $"tbdmovies_gaps_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workspace);
        _dbPath = Path.Combine(_workspace, "test.db");
        _connectionString = $"Data Source={_dbPath}";
        new DatabaseMigrator(_connectionString).Migrate();
        _factory = new CatalogUnitOfWorkFactory(_connectionString);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_workspace))
        {
            try { Directory.Delete(_workspace, true); } catch { }
        }
    }

    [Fact]
    public void Episodes_1_2_4_Infer_Episode_3_As_Missing()
    {
        var import = NewImportService();
        var show = "Fargo";
        import.RegisterEpisodeImport(Command(WriteMedia("Fargo.S01E01.mkv", show, 1, 1)));
        import.RegisterEpisodeImport(Command(WriteMedia("Fargo.S01E02.mkv", show, 1, 2)));
        import.RegisterEpisodeImport(Command(WriteMedia("Fargo.S01E04.mkv", show, 1, 4)));

        var tv = NewTvService();
        int showId;
        using (var uow = _factory.Begin())
        {
            showId = uow.TvShows.ListAll().Single().Id;
        }

        var details = tv.GetShowDetails(showId);
        var season = Assert.Single(details.Seasons);
        Assert.Equal(4, season.Episodes.Count);

        var ep1 = season.Episodes[0];
        var ep2 = season.Episodes[1];
        var ep3 = season.Episodes[2];
        var ep4 = season.Episodes[3];

        Assert.Equal(1, ep1.Number);
        Assert.True(ep1.Id > 0);
        Assert.Equal(EpisodeAvailability.Present, ep1.Availability);

        Assert.Equal(2, ep2.Number);
        Assert.True(ep2.Id > 0);
        Assert.Equal(EpisodeAvailability.Present, ep2.Availability);

        Assert.Equal(3, ep3.Number);
        Assert.Equal(0, ep3.Id);
        Assert.Equal(EpisodeAvailability.NoFile, ep3.Availability);
        Assert.Empty(ep3.MediaFiles);

        Assert.Equal(4, ep4.Number);
        Assert.True(ep4.Id > 0);
        Assert.Equal(EpisodeAvailability.Present, ep4.Availability);
    }

    [Fact]
    public void Episodes_1_3_5_Infer_Episodes_2_And_4_As_Missing()
    {
        var import = NewImportService();
        var show = "Severance";
        import.RegisterEpisodeImport(Command(WriteMedia("Severance.S01E01.mkv", show, 1, 1)));
        import.RegisterEpisodeImport(Command(WriteMedia("Severance.S01E03.mkv", show, 1, 3)));
        import.RegisterEpisodeImport(Command(WriteMedia("Severance.S01E05.mkv", show, 1, 5)));

        var tv = NewTvService();
        int showId;
        using (var uow = _factory.Begin())
        {
            showId = uow.TvShows.ListAll().Single().Id;
        }

        var details = tv.GetShowDetails(showId);
        var season = Assert.Single(details.Seasons);
        Assert.Equal(5, season.Episodes.Count);

        Assert.Equal(1, season.Episodes[0].Number);
        Assert.True(season.Episodes[0].Id > 0);

        Assert.Equal(2, season.Episodes[1].Number);
        Assert.Equal(0, season.Episodes[1].Id);
        Assert.Equal(EpisodeAvailability.NoFile, season.Episodes[1].Availability);

        Assert.Equal(3, season.Episodes[2].Number);
        Assert.True(season.Episodes[2].Id > 0);

        Assert.Equal(4, season.Episodes[3].Number);
        Assert.Equal(0, season.Episodes[3].Id);
        Assert.Equal(EpisodeAvailability.NoFile, season.Episodes[3].Availability);

        Assert.Equal(5, season.Episodes[4].Number);
        Assert.True(season.Episodes[4].Id > 0);
    }

    [Fact]
    public void Adding_Episode_3_Removes_The_Placeholder_And_Replaces_With_Real_Episode()
    {
        var import = NewImportService();
        var show = "Succession";
        import.RegisterEpisodeImport(Command(WriteMedia("Succession.S01E01.mkv", show, 1, 1)));
        import.RegisterEpisodeImport(Command(WriteMedia("Succession.S01E02.mkv", show, 1, 2)));
        import.RegisterEpisodeImport(Command(WriteMedia("Succession.S01E04.mkv", show, 1, 4)));

        var tv = NewTvService();
        int showId;
        using (var uow = _factory.Begin())
        {
            showId = uow.TvShows.ListAll().Single().Id;
        }

        // Before adding E03: 4 episodes with E03 being a placeholder (Id == 0)
        var detailsBefore = tv.GetShowDetails(showId);
        Assert.Equal(4, detailsBefore.Seasons[0].Episodes.Count);
        Assert.Equal(0, detailsBefore.Seasons[0].Episodes[2].Id);

        // Add real E03
        var registeredE3 = import.RegisterEpisodeImport(Command(WriteMedia("Succession.S01E03.mkv", show, 1, 3)));

        // After adding E03: E03 is real (Id > 0), no placeholders
        var detailsAfter = tv.GetShowDetails(showId);
        var seasonAfter = Assert.Single(detailsAfter.Seasons);
        Assert.Equal(4, seasonAfter.Episodes.Count);

        var ep3 = seasonAfter.Episodes[2];
        Assert.Equal(3, ep3.Number);
        Assert.Equal(registeredE3.Episode.Id, ep3.Id);
        Assert.Equal(EpisodeAvailability.Present, ep3.Availability);
        Assert.Single(ep3.MediaFiles);
    }

    [Fact]
    public void No_Fake_MediaFile_Or_Database_Row_Is_Created()
    {
        var import = NewImportService();
        var show = "Dark";
        import.RegisterEpisodeImport(Command(WriteMedia("Dark.S01E01.mkv", show, 1, 1)));
        import.RegisterEpisodeImport(Command(WriteMedia("Dark.S01E04.mkv", show, 1, 4)));

        var tv = NewTvService();
        int showId;
        using (var uow = _factory.Begin())
        {
            showId = uow.TvShows.ListAll().Single().Id;
        }

        // Get details (infers E02 and E03 missing dynamically)
        var details = tv.GetShowDetails(showId);
        Assert.Equal(4, details.Seasons[0].Episodes.Count);

        // Assert database state: ONLY 2 media_files and 2 tv_episodes rows exist in SQLite
        using (var uow = _factory.Begin())
        {
            var dbEpisodes = uow.TvEpisodes.ListByShow(showId);
            Assert.Equal(2, dbEpisodes.Count);
            Assert.Equal([1, 4], dbEpisodes.Select(e => e.Number).OrderBy(n => n).ToArray());

            var dbFiles = uow.MediaFiles.ListAll();
            Assert.Equal(2, dbFiles.Count);
        }
    }

    [Fact]
    public void No_Guessing_Beyond_Highest_Known_Local_Episode()
    {
        var import = NewImportService();
        var show = "Sherlock";
        // User owns 4, 5, 7 only
        import.RegisterEpisodeImport(Command(WriteMedia("Sherlock.S01E04.mkv", show, 1, 4)));
        import.RegisterEpisodeImport(Command(WriteMedia("Sherlock.S01E05.mkv", show, 1, 5)));
        import.RegisterEpisodeImport(Command(WriteMedia("Sherlock.S01E07.mkv", show, 1, 7)));

        var tv = NewTvService();
        int showId;
        using (var uow = _factory.Begin())
        {
            showId = uow.TvShows.ListAll().Single().Id;
        }

        var details = tv.GetShowDetails(showId);
        var season = Assert.Single(details.Seasons);

        // Range is [4, 7]: Episodes 4, 5, 6 (Missing), 7
        // Must NOT invent 1, 2, 3 before 4.
        // Must NOT invent 8, 9, 10 after 7.
        Assert.Equal(4, season.Episodes.Count);
        Assert.Equal([4, 5, 6, 7], season.Episodes.Select(e => e.Number).ToArray());

        Assert.True(season.Episodes[0].Id > 0); // E04
        Assert.True(season.Episodes[1].Id > 0); // E05
        Assert.Equal(0, season.Episodes[2].Id); // E06 Missing
        Assert.True(season.Episodes[3].Id > 0); // E07
    }

    [Fact]
    public void Specials_Do_Not_Get_Invented_From_Local_Numbering_Gaps()
    {
        var import = NewImportService();
        var show = "Doctor Who";
        // User owns Season 0 Specials 1 and 4
        import.RegisterEpisodeImport(Command(WriteMedia("Doctor.Who.S00E01.mkv", show, 0, 1)));
        import.RegisterEpisodeImport(Command(WriteMedia("Doctor.Who.S00E04.mkv", show, 0, 4)));

        var tv = NewTvService();
        int showId;
        using (var uow = _factory.Begin())
        {
            showId = uow.TvShows.ListAll().Single().Id;
        }

        var details = tv.GetShowDetails(showId);
        var season0 = Assert.Single(details.Seasons);
        Assert.Equal(0, season0.Number);

        // Specials must NOT infer 2 and 3 as missing without provider metadata
        Assert.Equal(2, season0.Episodes.Count);
        Assert.Equal([1, 4], season0.Episodes.Select(e => e.Number).ToArray());
        Assert.All(season0.Episodes, ep => Assert.True(ep.Id > 0));
    }

    [Fact]
    public async Task TMDB_Known_Episode_List_Exposes_Provider_Confirmed_Gaps()
    {
        var import = NewImportService();
        var showName = "Breaking Bad";
        // User owns S01E01, S01E02, S01E04, S01E07
        import.RegisterEpisodeImport(Command(WriteMedia("Breaking.Bad.S01E01.mkv", showName, 1, 1)));
        import.RegisterEpisodeImport(Command(WriteMedia("Breaking.Bad.S01E02.mkv", showName, 1, 2)));
        import.RegisterEpisodeImport(Command(WriteMedia("Breaking.Bad.S01E04.mkv", showName, 1, 4)));
        import.RegisterEpisodeImport(Command(WriteMedia("Breaking.Bad.S01E07.mkv", showName, 1, 7)));

        int showId;
        using (var uow = _factory.Begin())
        {
            showId = uow.TvShows.ListAll().Single().Id;
            // Mark show matched with TMDB
            var show = uow.TvShows.GetById(showId)!;
            uow.TvShows.UpdateMetadata(showId, new TvShowCatalogData(
                "TMDB", "1396", show.Title, metadataStatus: MetadataStatus.Ready), DateTimeOffset.UtcNow);
            uow.Commit();
        }

        // Provider defines Season 1 with 7 episodes (1..7)
        var providerEpisodes = ImmutableArray.Create(
            new TvEpisodeMetadata(1, "Pilot", "Overview 1", 58, "2008-01-20", 8.5, "/still1.jpg"),
            new TvEpisodeMetadata(2, "Cat's in the Bag...", "Overview 2", 48, "2008-01-27", 8.3, "/still2.jpg"),
            new TvEpisodeMetadata(3, "...And the Bag's in the River", "Overview 3", 48, "2008-02-10", 8.7, "/still3.jpg"),
            new TvEpisodeMetadata(4, "Cancer Man", "Overview 4", 48, "2008-02-17", 8.1, "/still4.jpg"),
            new TvEpisodeMetadata(5, "Gray Matter", "Overview 5", 48, "2008-02-24", 8.2, "/still5.jpg"),
            new TvEpisodeMetadata(6, "Crazy Handful of Nothin'", "Overview 6", 48, "2008-03-02", 9.2, "/still6.jpg"),
            new TvEpisodeMetadata(7, "A No-Rough-Stuff-Type Deal", "Overview 7", 48, "2008-03-09", 8.8, "/still7.jpg")
        );

        var fakeProvider = new MockMetadataProvider
        {
            SeasonToReturn = new TvSeasonMetadata(1, "Season 1", "Overview", "/poster.jpg", "2008-01-20", providerEpisodes)
        };

        var tv = NewTvService(fakeProvider);
        var details = await tv.GetShowDetailsEnrichedAsync(showId);
        Assert.NotNull(details);
        var season = Assert.Single(details!.Seasons);

        // TMDB confirms 7 episodes. User owns 1, 2, 4, 7.
        // Gaps: 3, 5, 6.
        Assert.Equal(7, season.Episodes.Count);

        // Real episodes (1, 2, 4, 7)
        Assert.Equal(1, season.Episodes[0].Number);
        Assert.True(season.Episodes[0].Id > 0);
        Assert.Equal(EpisodeAvailability.Present, season.Episodes[0].Availability);

        Assert.Equal(2, season.Episodes[1].Number);
        Assert.True(season.Episodes[1].Id > 0);
        Assert.Equal(EpisodeAvailability.Present, season.Episodes[1].Availability);

        // Provider-confirmed gaps (3, 5, 6)
        Assert.Equal(3, season.Episodes[2].Number);
        Assert.Equal(0, season.Episodes[2].Id);
        Assert.Equal("...And the Bag's in the River", season.Episodes[2].Title);
        Assert.Equal(48, season.Episodes[2].RuntimeMinutes);
        Assert.Equal(EpisodeAvailability.NoFile, season.Episodes[2].Availability);

        Assert.Equal(4, season.Episodes[3].Number);
        Assert.True(season.Episodes[3].Id > 0);

        Assert.Equal(5, season.Episodes[4].Number);
        Assert.Equal(0, season.Episodes[4].Id);
        Assert.Equal("Gray Matter", season.Episodes[4].Title);
        Assert.Equal(48, season.Episodes[4].RuntimeMinutes);

        Assert.Equal(6, season.Episodes[5].Number);
        Assert.Equal(0, season.Episodes[5].Id);
        Assert.Equal("Crazy Handful of Nothin'", season.Episodes[5].Title);

        Assert.Equal(7, season.Episodes[6].Number);
        Assert.True(season.Episodes[6].Id > 0);
    }

    [Fact]
    public void Real_Episodes_Remain_Unchanged_When_Gaps_Are_Synthesized()
    {
        var import = NewImportService();
        var show = "Fargo";
        var file1 = WriteMedia("Fargo.S01E01.mkv", show, 1, 1);
        var file3 = WriteMedia("Fargo.S01E03.mkv", show, 1, 3);
        var reg1 = import.RegisterEpisodeImport(Command(file1));
        var reg3 = import.RegisterEpisodeImport(Command(file3));

        var tv = NewTvService();
        int showId;
        using (var uow = _factory.Begin())
        {
            showId = uow.TvShows.ListAll().Single().Id;
        }

        var details = tv.GetShowDetails(showId);
        var season = Assert.Single(details.Seasons);

        Assert.Equal(3, season.Episodes.Count);

        var ep1 = season.Episodes[0];
        Assert.Equal(1, ep1.Number);
        Assert.Equal(reg1.Episode.Id, ep1.Id);
        Assert.Equal(EpisodeAvailability.Present, ep1.Availability);
        Assert.Single(ep1.MediaFiles);
        Assert.Equal(file1, ep1.MediaFiles[0].CurrentPath);

        var ep2 = season.Episodes[1];
        Assert.Equal(2, ep2.Number);
        Assert.Equal(0, ep2.Id);
        Assert.Equal(EpisodeAvailability.NoFile, ep2.Availability);
        Assert.Empty(ep2.MediaFiles);

        var ep3 = season.Episodes[2];
        Assert.Equal(3, ep3.Number);
        Assert.Equal(reg3.Episode.Id, ep3.Id);
        Assert.Equal(EpisodeAvailability.Present, ep3.Availability);
        Assert.Single(ep3.MediaFiles);
        Assert.Equal(file3, ep3.MediaFiles[0].CurrentPath);
    }

    [Fact]
    public void GetShowDetails_Returns_Local_Gaps_And_Never_Calls_The_Provider()
    {
        var import = NewImportService();
        var show = "Fargo";
        import.RegisterEpisodeImport(Command(WriteMedia("Fargo.S01E01.mkv", show, 1, 1)));
        import.RegisterEpisodeImport(Command(WriteMedia("Fargo.S01E02.mkv", show, 1, 2)));
        import.RegisterEpisodeImport(Command(WriteMedia("Fargo.S01E04.mkv", show, 1, 4)));

        int showId;
        using (var uow = _factory.Begin())
        {
            showId = uow.TvShows.ListAll().Single().Id;
            var s = uow.TvShows.GetById(showId)!;
            uow.TvShows.UpdateMetadata(showId, new TvShowCatalogData(
                "TMDB", "999", s.Title, metadataStatus: MetadataStatus.Ready), DateTimeOffset.UtcNow);
            uow.Commit();
        }

        var provider = new FailingMetadataProvider();
        var tv = NewTvService(provider);

        // The synchronous load path renders local gaps and must not touch the network, even for a matched show.
        var details = tv.GetShowDetails(showId);
        var season = Assert.Single(details.Seasons);
        Assert.Equal([1, 2, 3, 4], season.Episodes.Select(e => e.Number).ToArray());
        Assert.Equal(0, season.Episodes[2].Id);
        Assert.Equal(EpisodeAvailability.NoFile, season.Episodes[2].Availability);
        Assert.Equal(0, provider.CallCount);
    }

    [Fact]
    public async Task Enriched_Provider_Failure_Returns_Null_And_Leaves_Local_Data_Intact()
    {
        var import = NewImportService();
        var show = "Fargo";
        import.RegisterEpisodeImport(Command(WriteMedia("Fargo.S01E01.mkv", show, 1, 1)));
        import.RegisterEpisodeImport(Command(WriteMedia("Fargo.S01E02.mkv", show, 1, 2)));
        import.RegisterEpisodeImport(Command(WriteMedia("Fargo.S01E04.mkv", show, 1, 4)));

        int showId;
        using (var uow = _factory.Begin())
        {
            showId = uow.TvShows.ListAll().Single().Id;
            var s = uow.TvShows.GetById(showId)!;
            uow.TvShows.UpdateMetadata(showId, new TvShowCatalogData(
                "TMDB", "999", s.Title, metadataStatus: MetadataStatus.Ready), DateTimeOffset.UtcNow);
            uow.Commit();
        }

        var provider = new FailingMetadataProvider();
        var tv = NewTvService(provider);

        var enriched = await tv.GetShowDetailsEnrichedAsync(showId);
        Assert.Null(enriched);
        Assert.True(provider.CallCount >= 1);

        // The failed enrichment left both the DTO result and the SQLite rows untouched.
        var local = tv.GetShowDetails(showId);
        Assert.Equal([1, 2, 3, 4], local.Seasons.Single().Episodes.Select(e => e.Number).ToArray());
        using (var uow = _factory.Begin())
        {
            Assert.Equal(3, uow.TvEpisodes.ListByShow(showId).Count);
            Assert.Equal(3, uow.MediaFiles.ListAll().Count);
        }
    }

    [Fact]
    public async Task Enriched_Retains_Local_Ownership_And_File_Data()
    {
        var import = NewImportService();
        var show = "Breaking Bad";
        var file1 = WriteMedia("Breaking.Bad.S01E01.mkv", show, 1, 1);
        var reg1 = import.RegisterEpisodeImport(Command(file1));
        import.RegisterEpisodeImport(Command(WriteMedia("Breaking.Bad.S01E04.mkv", show, 1, 4)));

        int showId;
        using (var uow = _factory.Begin())
        {
            showId = uow.TvShows.ListAll().Single().Id;
            var s = uow.TvShows.GetById(showId)!;
            uow.TvShows.UpdateMetadata(showId, new TvShowCatalogData(
                "TMDB", "1396", s.Title, metadataStatus: MetadataStatus.Ready), DateTimeOffset.UtcNow);
            uow.Commit();
        }

        var provider = new MockMetadataProvider
        {
            SeasonToReturn = new TvSeasonMetadata(1, "Season 1", "Overview", "/poster.jpg", "2008-01-20",
                ImmutableArray.Create(
                    new TvEpisodeMetadata(1, "Pilot", "o", 58, "2008-01-20", 8.5, "/s1.jpg"),
                    new TvEpisodeMetadata(2, "Cat's in the Bag...", "o", 48, "2008-01-27", 8.3, "/s2.jpg"),
                    new TvEpisodeMetadata(3, "...And the Bag's in the River", "o", 48, "2008-02-10", 8.7, "/s3.jpg"),
                    new TvEpisodeMetadata(4, "Cancer Man", "o", 48, "2008-02-17", 8.1, "/s4.jpg")))
        };
        var tv = NewTvService(provider);

        var enriched = await tv.GetShowDetailsEnrichedAsync(showId);
        Assert.NotNull(enriched);
        var season = Assert.Single(enriched!.Seasons);
        Assert.Equal([1, 2, 3, 4], season.Episodes.Select(e => e.Number).ToArray());

        var ep1 = season.Episodes[0];
        Assert.Equal(reg1.Episode.Id, ep1.Id);
        Assert.Equal(EpisodeAvailability.Present, ep1.Availability);
        Assert.Single(ep1.MediaFiles);
        Assert.Equal(file1, ep1.MediaFiles[0].CurrentPath);

        var ep2 = season.Episodes[1];
        Assert.Equal(0, ep2.Id);
        Assert.Equal("Cat's in the Bag...", ep2.Title);
        Assert.Empty(ep2.MediaFiles);
    }

    [Fact]
    public async Task Enriched_Returns_Null_For_An_Unmatched_Local_Show()
    {
        var import = NewImportService();
        var show = "Dark";
        import.RegisterEpisodeImport(Command(WriteMedia("Dark.S01E01.mkv", show, 1, 1)));
        import.RegisterEpisodeImport(Command(WriteMedia("Dark.S01E04.mkv", show, 1, 4)));

        int showId;
        using (var uow = _factory.Begin())
        {
            showId = uow.TvShows.ListAll().Single().Id;
        }

        var provider = new FailingMetadataProvider();
        var tv = NewTvService(provider);

        Assert.Null(await tv.GetShowDetailsEnrichedAsync(showId));
        Assert.Equal(0, provider.CallCount);
    }

    private ImportService NewImportService() =>
        new(_factory, new NullMetadataProvider(), new MediaDiscoveryService());

    private TvLibraryService NewTvService(IMetadataProvider? provider = null) => new(
        new PerCallTvShowRepository(_factory),
        new PerCallTvSeasonRepository(_factory),
        new PerCallTvEpisodeRepository(_factory),
        metadataProvider: provider);

    private string WriteMedia(string fileName, string showTitle, int season, int episode)
    {
        var path = Path.Combine(_workspace, fileName);
        File.WriteAllBytes(path, new byte[1024]);
        return path;
    }

    private ConfirmEpisodeImportCommand Command(string path)
    {
        var discovered = new MediaDiscoveryService()
            .Discover(Path.GetDirectoryName(path)!, recursive: false)
            .Single(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase));

        return new ConfirmEpisodeImportCommand(
            path,
            discovered.FileSize ?? 0,
            discovered.ParsedMedia!,
            DateTimeOffset.UtcNow);
    }

    private sealed class NullMetadataProvider : IMetadataProvider
    {
        public string ProviderName => "none";
        public IReadOnlyList<MovieCandidate> Search(MovieSearchQuery query) => [];
        public MovieMetadata? GetMovie(string externalId) => null;
    }

    private sealed class MockMetadataProvider : IMetadataProvider
    {
        public string ProviderName => "TMDB";
        public TvSeasonMetadata? SeasonToReturn { get; set; }
        public IReadOnlyList<MovieCandidate> Search(MovieSearchQuery query) => [];
        public MovieMetadata? GetMovie(string externalId) => null;

        public Task<TvSeasonMetadata?> GetTvSeasonAsync(string showExternalId, int seasonNumber, CancellationToken cancellationToken = default) =>
            Task.FromResult(SeasonToReturn);
    }

    private sealed class FailingMetadataProvider : IMetadataProvider
    {
        public int CallCount { get; private set; }
        public string ProviderName => "TMDB";
        public IReadOnlyList<MovieCandidate> Search(MovieSearchQuery query) => [];
        public MovieMetadata? GetMovie(string externalId) => null;

        public Task<TvSeasonMetadata?> GetTvSeasonAsync(string showExternalId, int seasonNumber, CancellationToken cancellationToken = default)
        {
            CallCount++;
            throw new InvalidOperationException("Simulated provider failure (offline / timeout).");
        }
    }
}
