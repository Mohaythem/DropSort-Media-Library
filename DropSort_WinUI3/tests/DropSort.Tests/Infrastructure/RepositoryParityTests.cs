using System.Collections.Immutable;
using DropSort.Domain.Core.Operations;
using DropSort.Domain.Library.Movies;
using DropSort.Domain.Library.Personal;
using DropSort.Infrastructure.Persistence.Migrations;
using DropSort.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DropSort.Tests.Infrastructure;

public sealed class RepositoryParityTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 1, 0, 0, TimeSpan.Zero);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"dropsort_repository_{Guid.NewGuid():N}");
    private readonly string _databasePath;
    private readonly string _connectionString;

    public RepositoryParityTests()
    {
        Directory.CreateDirectory(_directory);
        _databasePath = Path.Combine(_directory, "repository.db");
        _connectionString = $"Data Source={_databasePath}";
        new DatabaseMigrator(_connectionString).Migrate();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    [Fact]
    public void CatalogUnitOfWorkPersistsStableIdsGenresAndOriginalTimestamps()
    {
        int movieId;
        int mediaId;
        using (var unit = new CatalogUnitOfWork(_connectionString))
        {
            var movie = unit.Movies.Create(Data("1", "Original", "Drama"), Now);
            var media = unit.MediaFiles.Add(Facts("Original.mkv"), movie.Id);
            movieId = movie.Id;
            mediaId = media.Id;
            unit.Commit();
        }

        using var connection = Open();
        var movies = new MovieRepository(connection);
        var mediaFiles = new MediaFileRepository(connection);
        var before = movies.GetById(movieId)!;
        var updated = movies.UpdateMetadata(movieId, Data("1", "Updated", "Drama", "Thriller"), Now.AddHours(1));

        Assert.Equal(movieId, updated.Id);
        Assert.Equal(mediaId, mediaFiles.GetById(mediaId)!.Id);
        Assert.Equal(before.DateAdded, updated.DateAdded);
        Assert.Equal(before.CreatedAt, updated.CreatedAt);
        Assert.Equal(new[] { "Drama", "Thriller" }, updated.Genres.ToArray());
        Assert.Equal(movieId, mediaFiles.GetById(mediaId)!.MovieId);
    }

    [Fact]
    public void DisposedUncommittedUnitOfWorkRollsBackAllRows()
    {
        using (var unit = new CatalogUnitOfWork(_connectionString))
        {
            var movie = unit.Movies.Create(Data("rollback", "Rollback"), Now);
            unit.MediaFiles.Add(Facts("Rollback.mkv"), movie.Id);
        }
        using var connection = Open();
        Assert.Equal(0L, Scalar(connection, "SELECT COUNT(*) FROM movies;"));
        Assert.Equal(0L, Scalar(connection, "SELECT COUNT(*) FROM media_files;"));
    }

    [Fact]
    public void FileOperationStoreRoundTripsUnsignedFilesystemIdentity()
    {
        using var store = new FileOperationStore(_connectionString, () => Now);
        var plan = new FileOperationPlan(
            "large-identity",
            OperationType.Move,
            Path.Combine(_directory, "source.mkv"),
            Path.Combine(_directory, "destination.mkv"));
        store.Create(plan, Now);
        store.Transition(
            plan.OperationId,
            OperationState.Validated,
            new OperationUpdate(SourceSize: 1, SourceMTimeNs: 2, SourceDev: ulong.MaxValue, SourceIno: ulong.MaxValue - 1));

        var record = store.GetById(plan.OperationId)!;
        Assert.Equal(ulong.MaxValue, record.SourceDev);
        Assert.Equal(ulong.MaxValue - 1, record.SourceIno);
    }

    [Fact]
    public void PersonalHistoryDerivesRewatchAndClearPreservesWatchlist()
    {
        var movieId = SeedMovie("personal");
        var personal = new PersonalLibraryRepository(_connectionString);
        personal.AddToWatchlist(movieId, Now);
        personal.SetPreference(movieId, PersonalPreference.Liked, Now);
        var later = personal.AddWatchEvent(movieId, Now.AddDays(2), Now);
        var earlier = personal.AddWatchEvent(movieId, Now.AddDays(1), Now);

        var history = personal.GetWatchEvents(movieId);
        Assert.Equal([earlier.Id, later.Id], history.Select(item => item.Id));
        Assert.False(history[0].Rewatch);
        Assert.True(history[1].Rewatch);
        Assert.Equal(2, personal.GetState(movieId).WatchCount);
        Assert.True(personal.ClearPreference(movieId, Now).IsWatchlisted);

        personal.DeleteWatchEvent(later.Id);
        var state = personal.GetState(movieId);
        Assert.Equal(1, state.WatchCount);
        Assert.Equal(Now.AddDays(1), state.LastWatched);
    }

    [Fact]
    public void ClearCatalogBlocksNonterminalAndThenPreservesTerminalJournalAndMediaBytes()
    {
        var movieId = SeedMovie("clear");
        int mediaId;
        var path = Path.Combine(_directory, "Clear.mkv");
        File.WriteAllText(path, "immutable");
        using (var connection = Open())
        using (var transaction = connection.BeginTransaction())
        {
            mediaId = new MediaFileRepository(connection, transaction).Add(
                new VerifiedMediaFileFacts(path, new FileInfo(path).Length, ".mkv", null, null, null, Now),
                movieId).Id;
            transaction.Commit();
        }
        using var store = new FileOperationStore(_connectionString, () => Now);
        var plan = new FileOperationPlan(
            "clear-operation", OperationType.Move, path, Path.Combine(_directory, "After.mkv"), mediaId);
        store.Create(plan, Now);

        var maintenance = new LibraryMaintenanceRepository(_connectionString);
        Assert.Throws<InvalidOperationException>(() => maintenance.ClearCatalog());
        Assert.Equal("immutable", File.ReadAllText(path));

        store.Transition(plan.OperationId, OperationState.Failed);
        var counts = maintenance.ClearCatalog();
        Assert.Equal((1, 1), (counts.Movies, counts.MediaFiles));
        Assert.Equal("immutable", File.ReadAllText(path));
        Assert.Null(store.GetById(plan.OperationId)!.MediaFileId);
    }

    private int SeedMovie(string externalId)
    {
        using var unit = new CatalogUnitOfWork(_connectionString);
        var movie = unit.Movies.Create(Data(externalId, externalId), Now);
        unit.Commit();
        return movie.Id;
    }

    private MovieCatalogData Data(string externalId, string title, params string[] genres) => new(
        "tmdb", externalId, title, null, 2026, "Overview", genres.ToImmutableArray(), 100, 8.0, null, MetadataStatus.Ready);

    private VerifiedMediaFileFacts Facts(string name)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, "media");
        return new VerifiedMediaFileFacts(path, new FileInfo(path).Length, Path.GetExtension(path), null, null, null, Now);
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON;";
        command.ExecuteNonQuery();
        return connection;
    }

    private static long Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }
}
