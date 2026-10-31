using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TBDMovies.Infrastructure.Persistence.Migrations;
using TBDMovies.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Xunit;

namespace TBDMovies.Tests.Infrastructure;

/// <summary>
/// Reproduces the production defect where Mark Watched failed with
/// "table watch_events has no column named poster_reference".
///
/// The real library.db reached user_version = 8 under an EARLIER revision of the 0008 script that
/// created watch_events WITHOUT the backdrop_reference / poster_reference columns. The 0008 script was
/// later edited to add them, but the migrator never re-runs a version it has already stamped, so the
/// on-disk table stayed on the old shape while user_version still read 8. Every fresh-schema test
/// missed this because those tests build watch_events from the CURRENT 0008 script (which has the
/// columns); none reproduced a v8 database whose watch_events predates the column addition.
///
/// These tests build exactly that drifted v8 state and assert the corrective 0009 migration reconciles
/// it so the real failing action (AddWatchEvent) succeeds and legacy rows are preserved.
/// </summary>
public sealed class Migration9WatchEventsDriftTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"tbdmovies_watch9_{Guid.NewGuid():N}");
    private readonly string _connectionString;

    public Migration9WatchEventsDriftTests()
    {
        Directory.CreateDirectory(_directory);
        _connectionString = $"Data Source={Path.Combine(_directory, "drift.db")}";
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    [Fact]
    public void CorrectiveMigration_AddsMissingArtworkColumns_AndMarkWatchedSucceeds()
    {
        SeedDriftedVersion8Database();

        // Sanity: the drifted database is exactly the shape that broke production.
        Assert.Equal(8, ReadUserVersion());
        Assert.False(WatchEventsHasColumn("poster_reference"));
        Assert.False(WatchEventsHasColumn("backdrop_reference"));

        // The corrective migration runs and its column-level schema validation passes.
        new DatabaseMigrator(_connectionString).Migrate();

        Assert.Equal(DatabaseMigrator.LatestVersion, ReadUserVersion());
        Assert.True(WatchEventsHasColumn("poster_reference"));
        Assert.True(WatchEventsHasColumn("backdrop_reference"));

        // The exact runtime action that failed before the fix now succeeds against the reconciled table.
        var repository = new PersonalLibraryRepository(_connectionString);
        var recorded = repository.AddWatchEvent(1, new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero));
        Assert.True(recorded.Id > 0);
        // Preserved legacy event (id 42) plus the freshly written one.
        Assert.Equal(2, repository.GetState(1).WatchCount);
    }

    [Fact]
    public void CorrectiveMigration_PreservesLegacyEvents_WithNullArtworkForOldRows()
    {
        SeedDriftedVersion8Database();

        new DatabaseMigrator(_connectionString).Migrate();

        var repository = new PersonalLibraryRepository(_connectionString);
        var legacy = Assert.Single(repository.ListRecent());
        Assert.Equal(42, legacy.Id);
        Assert.Equal("Legacy Movie", legacy.TitleSnapshot);
        Assert.Equal(1999, legacy.YearSnapshot);

        // The two columns added by the corrective migration are stored NULL for legacy rows: assert the
        // raw table values directly, since the read path deliberately falls back to the live movie's
        // artwork so a preserved event still renders (DTO PosterReference therefore shows the movie's
        // poster_path, not the raw NULL).
        Assert.Null(RawWatchEventValue(42, "backdrop_reference"));
        Assert.Null(RawWatchEventValue(42, "poster_reference"));
        // artwork_reference existed pre-drift and is preserved verbatim.
        Assert.Equal("/legacy-backdrop.jpg", RawWatchEventValue(42, "artwork_reference"));
    }

    [Fact]
    public void CorrectiveMigration_IsIdempotentAcrossRepeatedRuns()
    {
        SeedDriftedVersion8Database();

        new DatabaseMigrator(_connectionString).Migrate();
        new DatabaseMigrator(_connectionString).Migrate();

        Assert.Equal(DatabaseMigrator.LatestVersion, ReadUserVersion());
        var repository = new PersonalLibraryRepository(_connectionString);
        Assert.Single(repository.ListRecent());
        Assert.True(repository.AddWatchEvent(1, new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero)).Id > 0);
    }

    /// <summary>
    /// Builds a database stamped at user_version = 8 whose watch_events table matches the pre-fix 0008
    /// shape: every snapshot column through artwork_reference, but MISSING backdrop_reference and
    /// poster_reference. A legacy movie event is seeded so column preservation can be asserted.
    /// </summary>
    private void SeedDriftedVersion8Database()
    {
        new DatabaseMigrator(_connectionString).Migrate(7);
        using var connection = Open();
        Execute(connection, """
            DROP TABLE watch_events;
            CREATE TABLE watch_events (
                id INTEGER PRIMARY KEY,
                media_kind TEXT NOT NULL CHECK(media_kind IN ('MOVIE', 'EPISODE')),
                movie_id INTEGER REFERENCES movies(id) ON DELETE SET NULL,
                episode_id INTEGER REFERENCES tv_episodes(id) ON DELETE SET NULL,
                watched_at TEXT NOT NULL,
                created_at TEXT NOT NULL,
                title_snapshot TEXT NOT NULL,
                year_snapshot INTEGER,
                show_title_snapshot TEXT,
                season_number_snapshot INTEGER,
                episode_number_snapshot INTEGER,
                artwork_reference TEXT,
                CHECK (
                    (media_kind = 'MOVIE' AND episode_id IS NULL) OR
                    (media_kind = 'EPISODE' AND movie_id IS NULL)
                )
            );
            CREATE INDEX idx_watch_events_movie_watched ON watch_events(movie_id, julianday(watched_at), id);
            CREATE INDEX idx_watch_events_episode_watched ON watch_events(episode_id, julianday(watched_at), id);
            CREATE INDEX idx_watch_events_recent ON watch_events(julianday(watched_at) DESC, id DESC);

            INSERT INTO movies(id, provider, external_id, title, year, poster_path, backdrop_path, date_added, created_at, updated_at, genres, metadata_status)
            VALUES (1, 'tmdb', '1', 'Legacy Movie', 1999, '/legacy-poster.jpg', '/legacy-backdrop.jpg', '2026-01-01', '2026-01-01', '2026-01-01', '[]', 'READY');

            INSERT INTO watch_events(id, media_kind, movie_id, watched_at, created_at, title_snapshot, year_snapshot, artwork_reference)
            VALUES (42, 'MOVIE', 1, '2026-02-03T04:05:06.0000000+00:00', '2026-02-03T04:06:06.0000000+00:00', 'Legacy Movie', 1999, '/legacy-backdrop.jpg');

            PRAGMA user_version = 8;
            """);
    }

    private int ReadUserVersion()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private bool WatchEventsHasColumn(string column)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(watch_events);";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private string? RawWatchEventValue(int id, string column)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {column} FROM watch_events WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        var value = command.ExecuteScalar();
        return value is null or DBNull ? null : Convert.ToString(value);
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var foreignKeys = connection.CreateCommand();
        foreignKeys.CommandText = "PRAGMA foreign_keys = ON;";
        foreignKeys.ExecuteNonQuery();
        return connection;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
