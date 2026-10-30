using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DropSort.Domain.Library.Movies;
using DropSort.Infrastructure.Persistence.Migrations;
using DropSort.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DropSort.Tests.Infrastructure;

/// <summary>
/// Phase 3 marked movies and episodes watched through a <see cref="PersonalLibraryRepository" />
/// connection that is independent of the catalog unit of work, and both open IMMEDIATE (write-lock)
/// transactions. SQLite allows one writer at a time, so while any catalog work is in flight - a Home
/// projection, a background metadata match, an import - the watch write could not take the lock and,
/// because SQLite's busy handling is unfair, was starved until it failed with "database is locked".
/// The user saw that as the generic "operation could not be completed" on Mark Watched. These tests
/// pin down that writes across the two connections now serialise cleanly instead of colliding.
/// </summary>
public sealed class WatchHistoryConcurrencyTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"dropsort_conc_{Guid.NewGuid():N}");
    private readonly string _connectionString;

    public WatchHistoryConcurrencyTests()
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
    public async Task MarkWatched_SucceedsWhileCatalogWritesRunConcurrently()
    {
        var factory = new CatalogUnitOfWorkFactory(_connectionString);
        var personal = new PersonalLibraryRepository(_connectionString);
        var movieId = CreateMovie(factory, "watched-target", "Reservoir Dogs");

        const int catalogWriters = 4;
        const int catalogWritesEach = 120;
        const int watchWrites = 120;
        var failures = new ConcurrentQueue<Exception>();
        using var start = new Barrier(catalogWriters + 1);

        var catalogTasks = Enumerable.Range(0, catalogWriters).Select(worker => Task.Run(() =>
        {
            start.SignalAndWait();
            for (var i = 0; i < catalogWritesEach; i++)
            {
                try { CreateMovie(factory, $"noise-{worker}-{i}", $"Noise {worker}-{i}"); }
                catch (Exception ex) { failures.Enqueue(ex); return; }
            }
        })).ToArray();

        var watchTask = Task.Run(() =>
        {
            start.SignalAndWait();
            for (var i = 0; i < watchWrites; i++)
            {
                try { personal.AddWatchEvent(movieId, DateTimeOffset.Now, DateTimeOffset.Now); }
                catch (Exception ex) { failures.Enqueue(ex); return; }
            }
        });

        await Task.WhenAll(catalogTasks.Append(watchTask));

        Assert.True(failures.IsEmpty,
            "Concurrent writes must not collide, but at least one failed: " +
            string.Join(" | ", failures.Select(f => f.Message)));
        Assert.Equal(watchWrites, personal.GetWatchEvents(movieId).Count);
    }

    [Fact]
    public async Task MarkEpisodeWatched_SucceedsWhileCatalogWritesRunConcurrently()
    {
        var factory = new CatalogUnitOfWorkFactory(_connectionString);
        var personal = new PersonalLibraryRepository(_connectionString);
        var episodeId = CreateEpisode();

        const int catalogWriters = 4;
        const int catalogWritesEach = 120;
        const int watchWrites = 120;
        var failures = new ConcurrentQueue<Exception>();
        using var start = new Barrier(catalogWriters + 1);

        var catalogTasks = Enumerable.Range(0, catalogWriters).Select(worker => Task.Run(() =>
        {
            start.SignalAndWait();
            for (var i = 0; i < catalogWritesEach; i++)
            {
                try { CreateMovie(factory, $"tv-noise-{worker}-{i}", $"Noise {worker}-{i}"); }
                catch (Exception ex) { failures.Enqueue(ex); return; }
            }
        })).ToArray();

        var watchTask = Task.Run(() =>
        {
            start.SignalAndWait();
            for (var i = 0; i < watchWrites; i++)
            {
                try { personal.AddEpisodeWatchEvent(episodeId, DateTimeOffset.Now, DateTimeOffset.Now); }
                catch (Exception ex) { failures.Enqueue(ex); return; }
            }
        });

        await Task.WhenAll(catalogTasks.Append(watchTask));

        Assert.True(failures.IsEmpty,
            "Concurrent episode writes must not collide, but at least one failed: " +
            string.Join(" | ", failures.Select(f => f.Message)));
    }

    private int CreateMovie(CatalogUnitOfWorkFactory factory, string externalId, string title)
    {
        using var work = factory.Begin();
        var movie = work.Movies.Create(new MovieCatalogData(
            "tmdb", externalId, title, null, 1994, null,
            ImmutableArray<string>.Empty, null, null, null,
            MetadataStatus.NeedsMatch, null, null), DateTimeOffset.UtcNow);
        work.Commit();
        return movie.Id;
    }

    private int CreateEpisode()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO tv_shows(id, title, sort_title, year, date_added, created_at, updated_at, genres, metadata_status)
            VALUES (1, 'Show', 'show', 2020, '2026-01-01', '2026-01-01', '2026-01-01', '[]', 'PENDING');
            INSERT INTO tv_seasons(id, show_id, season_number, created_at, updated_at) VALUES (10, 1, 1, '2026-01-01', '2026-01-01');
            INSERT INTO tv_episodes(id, season_id, episode_number, title, created_at, updated_at) VALUES (100, 10, 1, 'Pilot', '2026-01-01', '2026-01-01');
            """;
        command.ExecuteNonQuery();
        return 100;
    }
}
