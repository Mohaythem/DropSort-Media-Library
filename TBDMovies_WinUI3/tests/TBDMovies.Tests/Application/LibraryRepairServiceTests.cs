using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using TBDMovies.Application.Services;
using TBDMovies.Domain.Library.Movies;
using TBDMovies.Domain.Library.Shows;
using TBDMovies.Infrastructure.Persistence.Migrations;
using TBDMovies.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Xunit;

namespace TBDMovies.Tests.Application;

/// <summary>
/// Manual repair: move a movie's file into an existing series as an episode, and remove a catalog item
/// without deleting its file. Runs against the real SQLite catalog, so it exercises the actual FK
/// behaviour (media_files.movie_id ON DELETE SET NULL, episode links) the operation depends on.
/// </summary>
public sealed class LibraryRepairServiceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"tbdmovies_repair_{Guid.NewGuid():N}.db");
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), $"tbdmovies_repair_{Guid.NewGuid():N}");
    private readonly string _connectionString;
    private readonly CatalogUnitOfWorkFactory _factory;
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    public LibraryRepairServiceTests()
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
    public void MoveMovieToSeries_ReclassifiesFileAsEpisode_RemovesMovie_AndKeepsFileOnDisk()
    {
        var showId = CreateShow("الرجل العناب");
        var (movieId, filePath, fileId) = CreateMovieWithFile("الرجل العناب 29.mkv");

        var repair = new LibraryRepairService(_factory, () => Now);
        var result = repair.MoveMovieToSeries(movieId, showId, seasonNumber: 1, episodeNumber: 29);

        Assert.Equal(showId, result.ShowId);
        using var uow = _factory.Begin();
        Assert.Null(uow.Movies.GetById(movieId));                       // movie removed
        var season = uow.TvSeasons.GetByNumber(showId, 1)!;
        var episode = uow.TvEpisodes.GetByNumber(season.Id, 29)!;
        Assert.Equal(episode.Id, uow.TvEpisodes.GetEpisodeIdForMediaFile(fileId)); // file attached to episode
        Assert.NotNull(uow.MediaFiles.GetById(fileId));                 // file row preserved
        Assert.True(File.Exists(filePath));                             // the physical file is untouched
    }

    [Fact]
    public void MoveMovieToSeries_ReusesExistingSeasonAndEpisode_WithoutDuplicates()
    {
        var showId = CreateShow("Show");
        int seasonId, episodeId;
        using (var uow = _factory.Begin())
        {
            var season = uow.TvSeasons.Create(showId, 1, "Season 1", null, Now);
            var episode = uow.TvEpisodes.Create(season.Id, 5, "Existing", null, null, null, Now);
            seasonId = season.Id;
            episodeId = episode.Id;
            uow.Commit();
        }

        var (movieId, _, fileId) = CreateMovieWithFile("Show 5.mkv");
        new LibraryRepairService(_factory, () => Now).MoveMovieToSeries(movieId, showId, 1, 5);

        using var check = _factory.Begin();
        Assert.Single(check.TvSeasons.ListByShow(showId));              // no duplicate season
        Assert.Single(check.TvEpisodes.ListBySeason(seasonId));         // no duplicate episode
        Assert.Equal(episodeId, check.TvEpisodes.GetEpisodeIdForMediaFile(fileId));
        Assert.Equal("Existing", check.TvEpisodes.GetById(episodeId)!.Title); // existing metadata kept
    }

    [Fact]
    public void MoveMovieToSeries_DoesNotDisturbUnrelatedShowsOrEpisodes()
    {
        var targetShow = CreateShow("Target");
        var otherShow = CreateShow("Other");
        int otherEpisodeId;
        using (var uow = _factory.Begin())
        {
            var season = uow.TvSeasons.Create(otherShow, 3, null, null, Now);
            otherEpisodeId = uow.TvEpisodes.Create(season.Id, 7, "Untouched", null, null, null, Now).Id;
            uow.Commit();
        }

        var (movieId, _, _) = CreateMovieWithFile("Target 1.mkv");
        new LibraryRepairService(_factory, () => Now).MoveMovieToSeries(movieId, targetShow, 1, 1);

        using var check = _factory.Begin();
        Assert.Equal("Untouched", check.TvEpisodes.GetById(otherEpisodeId)!.Title);
        Assert.DoesNotContain(check.TvSeasons.ListByShow(otherShow), s => s.Number != 3);
    }

    [Fact]
    public void RemoveMovieFromLibrary_RemovesCatalogRows_ButPreservesTheFileOnDisk()
    {
        var (movieId, filePath, fileId) = CreateMovieWithFile("Some Movie (2020).mkv");
        var otherShow = CreateShow("Kept Show");

        new LibraryRepairService(_factory, () => Now).RemoveMovieFromLibrary(movieId);

        using var uow = _factory.Begin();
        Assert.Null(uow.Movies.GetById(movieId));            // movie row gone
        Assert.Null(uow.MediaFiles.GetById(fileId));         // media-file row gone
        Assert.True(File.Exists(filePath));                  // the physical file is preserved
        Assert.NotNull(uow.TvShows.GetById(otherShow));      // unrelated item untouched
    }

    private int CreateShow(string title)
    {
        using var uow = _factory.Begin();
        var show = uow.TvShows.Create(new TvShowCatalogData(null, null, title), Now);
        uow.Commit();
        return show.Id;
    }

    private (int MovieId, string FilePath, int FileId) CreateMovieWithFile(string fileName)
    {
        var path = Path.Combine(_workspace, fileName);
        File.WriteAllText(path, "payload");
        using var uow = _factory.Begin();
        var movie = uow.Movies.Create(
            new MovieCatalogData(null, null, Path.GetFileNameWithoutExtension(fileName), null, null, null,
                ImmutableArray<string>.Empty, null, null, null, MetadataStatus.Pending),
            Now);
        var file = uow.MediaFiles.Add(
            new VerifiedMediaFileFacts(path, new FileInfo(path).Length, ".mkv", null, null, null, Now),
            movie.Id);
        uow.Commit();
        return (movie.Id, path, file.Id);
    }
}
