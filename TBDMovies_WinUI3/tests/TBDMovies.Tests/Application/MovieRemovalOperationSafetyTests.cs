using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using TBDMovies.Application.Services;
using TBDMovies.Domain.Core.Operations;
using TBDMovies.Domain.Library.Movies;
using TBDMovies.Domain.Library.Shows;
using TBDMovies.Infrastructure.Persistence.Migrations;
using TBDMovies.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Xunit;

namespace TBDMovies.Tests.Application;

public sealed class MovieRemovalOperationSafetyTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"tbdmovies_repair_guard_{Guid.NewGuid():N}.db");
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), $"tbdmovies_repair_guard_ws_{Guid.NewGuid():N}");
    private readonly string _connectionString;
    private readonly CatalogUnitOfWorkFactory _factory;
    private readonly FileOperationStore _operations;
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    public MovieRemovalOperationSafetyTests()
    {
        _connectionString = $"Data Source={_dbPath}";
        new DatabaseMigrator(_connectionString).Migrate();
        _factory = new CatalogUnitOfWorkFactory(_connectionString);
        _operations = new FileOperationStore(_connectionString, () => Now);
        Directory.CreateDirectory(_workspace);
    }

    public void Dispose()
    {
        _operations.Dispose();
        SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
        try { if (Directory.Exists(_workspace)) Directory.Delete(_workspace, true); } catch { }
    }

    [Theory]
    [InlineData(OperationState.Planned)]
    [InlineData(OperationState.Executing)]
    public void RemoveMovieFromLibrary_BlocksWhenFileHasNonterminalOperation(OperationState nonterminalState)
    {
        var (movieId, filePath, fileId) = CreateMovieWithFile($"Movie_{nonterminalState}.mkv");
        var opId = $"op-{nonterminalState}-{Guid.NewGuid():N}";
        CreateOperationInState(opId, fileId, filePath, nonterminalState);

        // Seed watch event to verify history snapshot is not disturbed on rejection
        SeedWatchEvent(movieId);

        var repair = new LibraryRepairService(_factory, () => Now, _operations);

        var ex = Assert.Throws<InvalidOperationException>(() => repair.RemoveMovieFromLibrary(movieId));
        Assert.Contains("pending operations", ex.Message);

        // Verify no rows or linkages were mutated
        using var check = _factory.Begin();
        Assert.NotNull(check.Movies.GetById(movieId));
        Assert.NotNull(check.MediaFiles.GetById(fileId));
        Assert.True(File.Exists(filePath), "Media file on disk must not be deleted.");

        var op = _operations.GetById(opId);
        Assert.NotNull(op);
        Assert.Equal(fileId, op.MediaFileId); // Linkage to media file is intact, NOT set to null!
        Assert.Equal(nonterminalState, op.State);

        // Verify watch event still linked to movie
        Assert.True(HasWatchEventForMovie(movieId));
    }

    [Theory]
    [InlineData(OperationState.Committed)]
    [InlineData(OperationState.Failed)]
    public void RemoveMovieFromLibrary_PermitsRemovalWhenOperationsAreTerminal(OperationState terminalState)
    {
        var (movieId, filePath, fileId) = CreateMovieWithFile($"Movie_Terminal_{terminalState}.mkv");
        var opId = $"op-term-{terminalState}-{Guid.NewGuid():N}";
        CreateOperationInState(opId, fileId, filePath, terminalState);

        var repair = new LibraryRepairService(_factory, () => Now, _operations);

        // Does not throw
        repair.RemoveMovieFromLibrary(movieId);

        // Catalog rows removed, but physical file on disk preserved
        using var check = _factory.Begin();
        Assert.Null(check.Movies.GetById(movieId));
        Assert.Null(check.MediaFiles.GetById(fileId));
        Assert.True(File.Exists(filePath), "Media file on disk must never be deleted.");

        // Operation record preserved in history
        var op = _operations.GetById(opId);
        Assert.NotNull(op);
        Assert.Equal(terminalState, op.State);
    }

    [Fact]
    public void RemoveMovieFromLibrary_MultiFileMovie_BlocksIfAnyFileHasNonterminalOperation()
    {
        var filePathA = Path.Combine(_workspace, "Part1.mkv");
        var filePathB = Path.Combine(_workspace, "Part2.mkv");
        File.WriteAllText(filePathA, "part1");
        File.WriteAllText(filePathB, "part2");

        int movieId, fileIdA, fileIdB;
        using (var uow = _factory.Begin())
        {
            var movie = uow.Movies.Create(
                new MovieCatalogData(null, null, "MultiPart Movie", null, null, null,
                    ImmutableArray<string>.Empty, null, null, null, MetadataStatus.Pending),
                Now);
            var fileA = uow.MediaFiles.Add(
                new VerifiedMediaFileFacts(filePathA, 5, ".mkv", null, null, null, Now),
                movie.Id);
            var fileB = uow.MediaFiles.Add(
                new VerifiedMediaFileFacts(filePathB, 5, ".mkv", null, null, null, Now),
                movie.Id);
            uow.Commit();
            movieId = movie.Id;
            fileIdA = fileA.Id;
            fileIdB = fileB.Id;
        }

        // File A has a committed operation
        CreateOperationInState($"op-multi-a-{Guid.NewGuid():N}", fileIdA, filePathA, OperationState.Committed);
        // File B has a pending executing operation
        var opBId = $"op-multi-b-{Guid.NewGuid():N}";
        CreateOperationInState(opBId, fileIdB, filePathB, OperationState.Executing);

        var repair = new LibraryRepairService(_factory, () => Now, _operations);

        // Attempting to remove must fail because File B has pending operation
        var ex = Assert.Throws<InvalidOperationException>(() => repair.RemoveMovieFromLibrary(movieId));
        Assert.Contains("pending operations", ex.Message);

        using (var check = _factory.Begin())
        {
            Assert.NotNull(check.Movies.GetById(movieId));
            Assert.NotNull(check.MediaFiles.GetById(fileIdA));
            Assert.NotNull(check.MediaFiles.GetById(fileIdB));
        }

        // Now transition File B's operation to FsVerified then Committed
        _operations.Transition(opBId, OperationState.FsVerified);
        _operations.CommitVerified(opBId);

        // Now removal must succeed
        repair.RemoveMovieFromLibrary(movieId);

        using (var check = _factory.Begin())
        {
            Assert.Null(check.Movies.GetById(movieId));
            Assert.Null(check.MediaFiles.GetById(fileIdA));
            Assert.Null(check.MediaFiles.GetById(fileIdB));
        }
        Assert.True(File.Exists(filePathA));
        Assert.True(File.Exists(filePathB));
    }

    [Fact]
    public void RemoveMovieFromLibrary_PreservesSharedRegistration_WhenFileAlsoLinkedToEpisode()
    {
        var (movieId, filePath, fileId) = CreateMovieWithFile("Shared_Feature.mkv");

        // Also create a show with an episode and link this file to it
        int showId, seasonId, episodeId;
        using (var uow = _factory.Begin())
        {
            var show = uow.TvShows.Create(new TvShowCatalogData(null, null, "Anthology Show"), Now);
            var season = uow.TvSeasons.Create(show.Id, 1, null, null, Now);
            var episode = uow.TvEpisodes.Create(season.Id, 1, "Special", null, null, null, Now);
            uow.TvEpisodes.LinkMediaFile(episode.Id, fileId, Now);
            uow.Commit();
            showId = show.Id;
            seasonId = season.Id;
            episodeId = episode.Id;
        }

        var repair = new LibraryRepairService(_factory, () => Now, _operations);
        repair.RemoveMovieFromLibrary(movieId);

        using (var check = _factory.Begin())
        {
            Assert.Null(check.Movies.GetById(movieId)); // Movie is deleted
            Assert.NotNull(check.MediaFiles.GetById(fileId)); // Media file is PRESERVED for the episode!
            Assert.Equal(episodeId, check.TvEpisodes.GetEpisodeIdForMediaFile(fileId)); // Linkage intact!
        }
        Assert.True(File.Exists(filePath));
    }

    [Fact]
    public void RemoveShowFromLibrary_StillBlocksWhenEpisodeFileHasPendingOperation()
    {
        var filePath = Path.Combine(_workspace, "Show_Ep1.mkv");
        File.WriteAllText(filePath, "ep1");

        int showId, fileId;
        using (var uow = _factory.Begin())
        {
            var show = uow.TvShows.Create(new TvShowCatalogData(null, null, "Show With Pending Op"), Now);
            var season = uow.TvSeasons.Create(show.Id, 1, null, null, Now);
            var episode = uow.TvEpisodes.Create(season.Id, 1, "Ep 1", null, null, null, Now);
            var file = uow.MediaFiles.Add(
                new VerifiedMediaFileFacts(filePath, 3, ".mkv", null, null, null, Now),
                movieId: null);
            uow.TvEpisodes.LinkMediaFile(episode.Id, file.Id, Now);
            uow.Commit();
            showId = show.Id;
            fileId = file.Id;
        }

        var opId = $"op-show-{Guid.NewGuid():N}";
        CreateOperationInState(opId, fileId, filePath, OperationState.Executing);

        var repair = new LibraryRepairService(_factory, () => Now, _operations);

        var ex = Assert.Throws<InvalidOperationException>(() => repair.RemoveShowFromLibrary(showId));
        Assert.Contains("pending operations", ex.Message);

        using (var check = _factory.Begin())
        {
            Assert.NotNull(check.TvShows.GetById(showId));
            Assert.NotNull(check.MediaFiles.GetById(fileId));
        }
    }

    private (int MovieId, string FilePath, int FileId) CreateMovieWithFile(string fileName)
    {
        var path = Path.Combine(_workspace, fileName);
        File.WriteAllText(path, "movie_content");
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

    private void CreateOperationInState(string opId, int mediaFileId, string path, OperationState targetState)
    {
        var dest = Path.Combine(_workspace, "dest_" + Path.GetFileName(path));
        var plan = new FileOperationPlan(opId, OperationType.Rename, path, dest, mediaFileId);
        _operations.Create(plan, Now);

        if (targetState == OperationState.Planned) return;

        if (targetState == OperationState.Failed)
        {
            _operations.Transition(opId, OperationState.Failed);
            return;
        }

        _operations.Transition(opId, OperationState.Validated);
        if (targetState == OperationState.Validated) return;

        _operations.Transition(opId, OperationState.Executing);
        if (targetState == OperationState.Executing) return;

        _operations.Transition(opId, OperationState.FsVerified);
        if (targetState == OperationState.FsVerified) return;

        if (targetState == OperationState.RecoveryRequired)
        {
            _operations.Transition(opId, OperationState.RecoveryRequired);
            return;
        }

        if (targetState == OperationState.Committed)
        {
            // For CommitVerified, destination must exist if media_file_id is present
            File.WriteAllText(dest, "dest_content");
            _operations.CommitVerified(opId);
        }
    }

    private void SeedWatchEvent(int movieId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO watch_events (media_kind, movie_id, watched_at, created_at, title_snapshot)
            VALUES ('MOVIE', $movie_id, $watched_at, $created_at, 'Test Movie');
            """;
        cmd.Parameters.AddWithValue("$movie_id", movieId);
        cmd.Parameters.AddWithValue("$watched_at", Now.ToString("O"));
        cmd.Parameters.AddWithValue("$created_at", Now.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    private bool HasWatchEventForMovie(int movieId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM watch_events WHERE movie_id = $movie_id;";
        cmd.Parameters.AddWithValue("$movie_id", movieId);
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }
}
