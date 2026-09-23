using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DropSort.Domain.Core.Operations;
using DropSort.Domain.Library.Movies;
using DropSort.Infrastructure.Persistence.Migrations;
using DropSort.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DropSort.Tests.Infrastructure;

/// <summary>
/// Persistence Write Coordination: every writer to library.db - catalog unit of work, personal
/// library, maintenance, settings, and the file-operation journal - must serialise through the single
/// per-database <c>DatabaseWriteGate</c> so IMMEDIATE write transactions queue instead of colliding
/// ("database is locked"). These tests pin down that FileOperationStore and SettingsRepository now
/// participate in that contract, that the gate is released on exceptions, and that the non-reentrant
/// gate never self-deadlocks a single logical write (CommitVerified calls a nested transition helper).
/// </summary>
public sealed class WriteCoordinationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"dropsort_writecoord_{Guid.NewGuid():N}");
    private readonly string _connectionString;

    public WriteCoordinationTests()
    {
        Directory.CreateDirectory(_directory);
        _connectionString = $"Data Source={Path.Combine(_directory, "library.db")}";
        new DatabaseMigrator(_connectionString).Migrate();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); } catch { }
    }

    [Fact]
    public async Task AllWriterKinds_RunningConcurrently_SerializeWithoutDatabaseLocked()
    {
        var factory = new CatalogUnitOfWorkFactory(_connectionString);
        var personal = new PersonalLibraryRepository(_connectionString);
        var settings = new SettingsRepository(_connectionString);
        using var store = new FileOperationStore(_connectionString);
        var movieId = CreateMovie(factory, "coord-target", "Heat");

        const int writersEach = 100;
        var failures = new ConcurrentQueue<Exception>();
        using var start = new Barrier(4);

        var catalog = Task.Run(() =>
        {
            start.SignalAndWait();
            for (var i = 0; i < writersEach; i++)
                try { CreateMovie(factory, $"noise-{i}", $"Noise {i}"); }
                catch (Exception ex) { failures.Enqueue(ex); return; }
        });

        var watch = Task.Run(() =>
        {
            start.SignalAndWait();
            for (var i = 0; i < writersEach; i++)
                try { personal.AddWatchEvent(movieId, DateTimeOffset.Now, DateTimeOffset.Now); }
                catch (Exception ex) { failures.Enqueue(ex); return; }
        });

        var journal = Task.Run(() =>
        {
            start.SignalAndWait();
            for (var i = 0; i < writersEach; i++)
                try
                {
                    var plan = new FileOperationPlan(
                        Guid.NewGuid().ToString("D"), OperationType.Move,
                        Path.Combine(_directory, $"s{i}.mkv"), Path.Combine(_directory, $"d{i}.mkv"), null, null);
                    store.Create(plan, DateTimeOffset.UtcNow);
                    store.Transition(plan.OperationId, OperationState.Validated);
                }
                catch (Exception ex) { failures.Enqueue(ex); return; }
        });

        var settingsTask = Task.Run(() =>
        {
            start.SignalAndWait();
            for (var i = 0; i < writersEach; i++)
                try { settings.Set("ui_probe", i.ToString(), DateTimeOffset.UtcNow); }
                catch (Exception ex) { failures.Enqueue(ex); return; }
        });

        await Task.WhenAll(catalog, watch, journal, settingsTask);

        Assert.True(failures.IsEmpty,
            "Concurrent writes across all writer kinds must serialise, but at least one failed: " +
            string.Join(" | ", failures.Select(f => f.Message)));
        Assert.Equal(writersEach, personal.GetWatchEvents(movieId).Count);
        Assert.Equal(writersEach, store.ListHistory(int.MaxValue).Count);
    }

    [Fact]
    public void HeldCatalogUnitOfWork_BlocksFileOperationWrite_UntilReleased()
    {
        var factory = new CatalogUnitOfWorkFactory(_connectionString);
        using var store = new FileOperationStore(_connectionString);
        var plan = new FileOperationPlan(
            Guid.NewGuid().ToString("D"), OperationType.Move,
            Path.Combine(_directory, "s.mkv"), Path.Combine(_directory, "d.mkv"), null, null);

        var uow = factory.Begin(); // acquires the write gate and an IMMEDIATE transaction
        var write = Task.Run(() => store.Create(plan, DateTimeOffset.UtcNow));

        // While the gate is held the journal write cannot proceed.
        Assert.False(write.Wait(TimeSpan.FromMilliseconds(400)), "FileOperationStore bypassed the write gate");

        uow.Commit();
        uow.Dispose(); // releases the gate

        Assert.True(write.Wait(TimeSpan.FromSeconds(10)), "gate was not released to the waiting journal write");
        Assert.NotNull(store.GetById(plan.OperationId));
    }

    [Fact]
    public void FileOperationWrite_ThatThrows_ReleasesGate()
    {
        using var store = new FileOperationStore(_connectionString);
        Assert.Throws<KeyNotFoundException>(() => store.CommitVerified("does-not-exist"));

        // If the gate leaked on the exception path this same-path write would block forever.
        var plan = new FileOperationPlan(
            Guid.NewGuid().ToString("D"), OperationType.Move,
            Path.Combine(_directory, "s.mkv"), Path.Combine(_directory, "d.mkv"), null, null);
        var write = Task.Run(() => store.Create(plan, DateTimeOffset.UtcNow));
        Assert.True(write.Wait(TimeSpan.FromSeconds(10)), "gate was not released after an exception");
    }

    [Fact]
    public void CommitVerified_UpdatesMediaFileUnderGate_WithoutSelfDeadlock()
    {
        SeedMovieWithMediaFile(id: 1, path: Path.Combine(_directory, "old.mkv"));
        using var store = new FileOperationStore(_connectionString);
        var destination = Path.Combine(_directory, "new.mkv");
        var plan = new FileOperationPlan(
            Guid.NewGuid().ToString("D"), OperationType.Move,
            Path.Combine(_directory, "old.mkv"), destination, 1, null);

        var run = Task.Run(() =>
        {
            store.Create(plan, DateTimeOffset.UtcNow);
            store.Transition(plan.OperationId, OperationState.Validated);
            store.Transition(plan.OperationId, OperationState.Executing);
            store.Transition(plan.OperationId, OperationState.FsVerified);
            return store.CommitVerified(plan.OperationId);
        });

        // CommitVerified opens the gate then calls the nested (ungated) transition helper; a reentrant
        // acquisition would deadlock here.
        Assert.True(run.Wait(TimeSpan.FromSeconds(10)), "CommitVerified self-deadlocked on the write gate");
        Assert.Equal(OperationState.Committed, run.Result.State);
        Assert.Equal(destination, MediaFilePath(1));
    }

    private int CreateMovie(CatalogUnitOfWorkFactory factory, string externalId, string title)
    {
        using var work = factory.Begin();
        var movie = work.Movies.Create(new MovieCatalogData(
            "tmdb", externalId, title, null, 1995, null,
            ImmutableArray<string>.Empty, null, null, null,
            MetadataStatus.NeedsMatch, null, null), DateTimeOffset.UtcNow);
        work.Commit();
        return movie.Id;
    }

    private void SeedMovieWithMediaFile(int id, string path)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO movies(id, provider, external_id, title, date_added, created_at, updated_at, genres, metadata_status)
            VALUES ($id, 'tmdb', $ext, 'Seed', '2026-01-01', '2026-01-01', '2026-01-01', '[]', 'READY');
            INSERT INTO media_files(id, movie_id, current_path, path_key, file_size, status, discovered_at, last_seen_at)
            VALUES ($id, $id, $path, $path, 10, 'PRESENT', '2026-01-01', '2026-01-01');
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$ext", id.ToString());
        command.Parameters.AddWithValue("$path", path);
        command.ExecuteNonQuery();
    }

    private string MediaFilePath(int id)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT current_path FROM media_files WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        return (string)command.ExecuteScalar()!;
    }
}
