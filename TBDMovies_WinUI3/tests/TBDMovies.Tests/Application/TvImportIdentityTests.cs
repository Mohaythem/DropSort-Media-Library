using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TBDMovies.Application.Dto;
using TBDMovies.Application.External;
using TBDMovies.Application.Services;
using TBDMovies.Domain.Library.Shows;
using TBDMovies.Domain.Metadata.Contracts;
using TBDMovies.FileSystem.Discovery;
using TBDMovies.Infrastructure.Persistence.Migrations;
using TBDMovies.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Xunit;

namespace TBDMovies.Tests.Application;

/// <summary>
/// Stable local TV import identity across metadata matching. Episode import resolves a show by its
/// filename-derived alias, which is immutable local identity. Metadata matching later rewrites the
/// show's display title and sort_title (and sets provider/external_id) with the provider's canonical
/// values. A later episode arriving under the original filename must still resolve to the same logical
/// show, so seasons/episodes stay together and no duplicate show or provider-uniqueness conflict is
/// created.
/// </summary>
public sealed class TvImportIdentityTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _connectionString;
    private readonly string _workspace;
    private readonly CatalogUnitOfWorkFactory _factory;

    public TvImportIdentityTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"tbdmovies_tvid_{Guid.NewGuid():N}.db");
        _connectionString = $"Data Source={_dbPath}";
        new DatabaseMigrator(_connectionString).Migrate();
        _factory = new CatalogUnitOfWorkFactory(_connectionString);
        _workspace = Path.Combine(Path.GetTempPath(), $"tbdmovies_tvid_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workspace);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
        try { if (Directory.Exists(_workspace)) Directory.Delete(_workspace, true); } catch { }
    }

    [Fact]
    public void EpisodeUnderOriginalAlias_ResolvesToSameShow_AfterMetadataRewritesTitle()
    {
        var import = new ImportService(_factory, new NoProvider());

        // Import episode 1 under the filename alias "Alias Show".
        var first = import.RegisterEpisodeImport(Command(WriteMedia("Alias.Show.S01E01.1080p.mkv")));
        var showId = first.Show.Id;
        Assert.Equal("aliasshow", first.Show.SortTitle);

        // Metadata matching rewrites title + sort_title and stamps the provider identity, exactly as
        // MetadataMatchingService.ApplyTvShowMetadataAsync does through TvShows.UpdateMetadata.
        using (var uow = _factory.Begin())
        {
            uow.TvShows.UpdateMetadata(
                showId,
                new TvShowCatalogData(provider: "tmdb", externalId: "555", title: "Canonical Name"),
                DateTimeOffset.UtcNow);
            uow.Commit();
        }

        // The filename sort_title no longer resolves the show (this is the exact condition under which
        // the old code created a duplicate); the immutable import alias still does.
        using (var uow = _factory.Begin())
        {
            Assert.Null(uow.TvShows.GetBySortTitle("alias show"));
            Assert.Equal(showId, uow.TvShows.GetByAlias("alias show")!.Id);
        }

        // Import episode 2 under the ORIGINAL filename alias: it must attach to the same logical show.
        var second = import.RegisterEpisodeImport(Command(WriteMedia("Alias.Show.S01E02.1080p.mkv")));
        Assert.Equal(showId, second.Show.Id);

        using (var uow = _factory.Begin())
        {
            Assert.Equal(1, uow.TvShows.CountAll());                    // no duplicate show
            var show = uow.TvShows.GetById(showId)!;
            Assert.Equal("tmdb", show.Data.Provider);                   // provider identity intact
            Assert.Equal("555", show.Data.ExternalId);                  // no uniqueness conflict
            var seasons = uow.TvSeasons.ListByShow(showId);
            var season = Assert.Single(seasons);
            Assert.Equal(2, uow.TvEpisodes.ListBySeason(season.Id).Count); // both episodes, one show
        }
    }

    [Fact]
    public void SameEpisodeFile_ReimportedAfterMetadataRewrite_IsRecognizedAsAlreadyRegistered()
    {
        var import = new ImportService(_factory, new NoProvider());
        var path = WriteMedia("Alias.Show.S01E01.1080p.mkv");
        var first = import.RegisterEpisodeImport(Command(path));
        var showId = first.Show.Id;

        using (var uow = _factory.Begin())
        {
            uow.TvShows.UpdateMetadata(
                showId,
                new TvShowCatalogData(provider: "tmdb", externalId: "777", title: "Canonical Name"),
                DateTimeOffset.UtcNow);
            uow.Commit();
        }

        // Re-importing the exact same file after the metadata rename must be recognized as already
        // registered, not rejected as owned by another episode.
        var again = import.RegisterEpisodeImport(Command(path));
        Assert.True(again.AlreadyRegistered);
        Assert.Equal(showId, again.Show.Id);
    }

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

    private sealed class NoProvider : IMetadataProvider
    {
        public string ProviderName => "none";
        public IReadOnlyList<MovieCandidate> Search(MovieSearchQuery query) => [];
        public MovieMetadata? GetMovie(string externalId) => null;
    }
}
