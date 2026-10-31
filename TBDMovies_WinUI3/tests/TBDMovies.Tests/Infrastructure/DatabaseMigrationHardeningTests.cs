using System;
using System.IO;
using System.Linq;
using TBDMovies.Infrastructure.Persistence;
using TBDMovies.Infrastructure.Persistence.Migrations;
using Microsoft.Data.Sqlite;
using Xunit;

namespace TBDMovies.Tests.Infrastructure;

/// <summary>Disposable, row-bearing migration fixtures for release hardening.</summary>
public sealed class DatabaseMigrationHardeningTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"tbdmovies_db_hardening_{Guid.NewGuid():N}");
    private readonly string _path;
    private readonly string _cs;

    public DatabaseMigrationHardeningTests()
    {
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, "library.db");
        _cs = $"Data Source={_path}";
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); } catch { }
    }

    [Fact]
    public void V8WatchHistory_ReconciliationPreservesBothArtworkReferences()
    {
        new DatabaseMigrator(_cs).Migrate(8);
        using (var db = Open())
        {
            SeedMovie(db, 1, "Watched Movie");
            Exec(db, "INSERT INTO watch_events(id,media_kind,movie_id,watched_at,created_at,title_snapshot,year_snapshot,artwork_reference,backdrop_reference,poster_reference) VALUES (7,'MOVIE',1,'2026-01-01','2026-01-01','Watched Movie',2024,'/art.jpg','/backdrop.jpg','/poster.jpg');");
        }
        new DatabaseMigrator(_cs).Migrate();

        using var upgraded = Open();
        Assert.Equal("/backdrop.jpg", Scalar(upgraded, "SELECT backdrop_reference FROM watch_events WHERE id=7"));
        Assert.Equal("/poster.jpg", Scalar(upgraded, "SELECT poster_reference FROM watch_events WHERE id=7"));
        Assert.Equal(1L, Scalar(upgraded, "SELECT movie_id FROM watch_events WHERE id=7"));
    }

    [Fact]
    public void V8LegacyWatchHistory_MissingArtworkColumnsIsReconciledWithoutLosingSnapshot()
    {
        new DatabaseMigrator(_cs).Migrate(8);
        using (var db = Open())
        {
            SeedMovie(db, 1, "Legacy Watched");
            Exec(db, "ALTER TABLE watch_events RENAME TO watch_events_legacy; CREATE TABLE watch_events (id INTEGER PRIMARY KEY, media_kind TEXT NOT NULL CHECK(media_kind IN ('MOVIE','EPISODE')), movie_id INTEGER REFERENCES movies(id) ON DELETE SET NULL, episode_id INTEGER REFERENCES tv_episodes(id) ON DELETE SET NULL, watched_at TEXT NOT NULL, created_at TEXT NOT NULL, title_snapshot TEXT NOT NULL, year_snapshot INTEGER, show_title_snapshot TEXT, season_number_snapshot INTEGER, episode_number_snapshot INTEGER, artwork_reference TEXT, CHECK ((media_kind='MOVIE' AND episode_id IS NULL) OR (media_kind='EPISODE' AND movie_id IS NULL))); INSERT INTO watch_events SELECT id,media_kind,movie_id,episode_id,watched_at,created_at,title_snapshot,year_snapshot,show_title_snapshot,season_number_snapshot,episode_number_snapshot,artwork_reference FROM watch_events_legacy; DROP TABLE watch_events_legacy;");
            Exec(db, "INSERT INTO watch_events(id,media_kind,movie_id,watched_at,created_at,title_snapshot,year_snapshot,artwork_reference) VALUES (8,'MOVIE',1,'2026-01-02','2026-01-02','Legacy Watched',2020,'/legacy-art.jpg');");
        }
        new DatabaseMigrator(_cs).Migrate();
        using var upgraded = Open();
        Assert.Equal("Legacy Watched", Scalar(upgraded, "SELECT title_snapshot FROM watch_events WHERE id=8"));
        Assert.Equal("/legacy-art.jpg", Scalar(upgraded, "SELECT artwork_reference FROM watch_events WHERE id=8"));
        Assert.True(IsDbNull(Scalar(upgraded, "SELECT backdrop_reference FROM watch_events WHERE id=8")));
        Assert.True(IsDbNull(Scalar(upgraded, "SELECT poster_reference FROM watch_events WHERE id=8")));
    }

    [Fact]
    public void V7CatalogFixture_PreservesRowsRelationshipsAndBackfillsAliases()
    {
        new DatabaseMigrator(_cs).Migrate(7);
        using (var db = Open())
        {
            SeedMovie(db, 1, "Movie One");
            SeedMovie(db, 2, "Offline Two", "NULL", "PENDING");
            Exec(db, "INSERT INTO media_files(id,movie_id,current_path,path_key,file_size,extension,discovered_at,last_seen_at) VALUES (11,1,'C:\\fixture\\one.mkv','c:\\fixture\\one.mkv',101,'.mkv','2026-01-01','2026-01-02'),(12,1,'C:\\fixture\\one-4k.mkv','c:\\fixture\\one-4k.mkv',202,'.mkv','2026-01-01','2026-01-02');");
            Exec(db, "INSERT INTO metadata_cache(id,provider,cache_key,payload,fetched_at) VALUES (21,'tmdb','movie:1','{\"id\":1}','2026-01-01');");
            Exec(db, "INSERT INTO watched_folders(id,path,folder_role,enabled,created_at) VALUES (31,'\\\\NAS\\Media','MOVIES',1,'2026-01-01');");
            Exec(db, "INSERT INTO settings(key,value,updated_at) VALUES ('ui_media_folders','[\"\\\\NAS\\Media\"]','2026-01-01'),('ui_theme','dark','2026-01-01');");
            Exec(db, "INSERT INTO movie_personal_state(movie_id,preference,watchlist_added_at,created_at,updated_at) VALUES (1,'LIKED','2026-01-04','2026-01-01','2026-01-04');");
            Exec(db, "INSERT INTO watch_events(id,movie_id,watched_at,created_at) VALUES (41,1,'2026-01-03','2026-01-03');");
            SeedShow(db, 50, "Fixture Show");
            Exec(db, "INSERT INTO tv_seasons(id,show_id,season_number,title,created_at,updated_at) VALUES (51,50,1,'Season 1','2026-01-01','2026-01-01');");
            Exec(db, "INSERT INTO tv_episodes(id,season_id,episode_number,title,created_at,updated_at) VALUES (52,51,2,'Episode 2','2026-01-01','2026-01-01');");
            Exec(db, "INSERT INTO media_files(id,movie_id,current_path,path_key,file_size,discovered_at,last_seen_at) VALUES (53,NULL,'C:\\fixture\\episode.mkv','c:\\fixture\\episode.mkv',303,'2026-01-01','2026-01-02');");
            Exec(db, "INSERT INTO episode_media_files(media_file_id,episode_id,linked_at) VALUES (53,52,'2026-01-02');");
        }
        new DatabaseMigrator(_cs).Migrate();
        using var db2 = Open();
        Assert.Equal("Movie One", Scalar(db2, "SELECT title FROM movies WHERE id=1"));
        Assert.Equal("PENDING", Scalar(db2, "SELECT metadata_status FROM movies WHERE id=2"));
        Assert.Equal("C:\\fixture\\one-4k.mkv", Scalar(db2, "SELECT current_path FROM media_files WHERE id=12"));
        Assert.Equal("{\"id\":1}", Scalar(db2, "SELECT payload FROM metadata_cache WHERE id=21"));
        Assert.Equal("\\\\NAS\\Media", Scalar(db2, "SELECT path FROM watched_folders WHERE id=31"));
        Assert.Equal("dark", Scalar(db2, "SELECT value FROM settings WHERE key='ui_theme'"));
        Assert.Equal("LIKED", Scalar(db2, "SELECT preference FROM movie_personal_state WHERE movie_id=1"));
        Assert.Equal("Movie One", Scalar(db2, "SELECT title_snapshot FROM watch_events WHERE id=41"));
        Assert.Equal("Fixture Show", Scalar(db2, "SELECT title FROM tv_shows WHERE id=50"));
        Assert.Equal(2L, Scalar(db2, "SELECT episode_number FROM tv_episodes WHERE id=52"));
        Assert.Equal(52L, Scalar(db2, "SELECT episode_id FROM episode_media_files WHERE media_file_id=53"));
        Assert.Equal(50L, Scalar(db2, "SELECT show_id FROM tv_show_aliases WHERE alias_sort_title='fixture show'"));
    }

    [Fact]
    public void V7OrphanWatchHistory_IsRejectedOrPreservedFailClosed()
    {
        new DatabaseMigrator(_cs).Migrate(7);
        using (var db = Open())
        {
            Exec(db, "PRAGMA foreign_keys=OFF; INSERT INTO watch_events(id,movie_id,watched_at,created_at) VALUES (700,999,'2026-01-01','2026-01-01'); PRAGMA foreign_keys=ON;");
        }
        Assert.ThrowsAny<Exception>(() => new DatabaseMigrator(_cs).Migrate());
        using var after = Open();
        Assert.Equal(7L, Scalar(after, "PRAGMA user_version"));
        Assert.Equal(1L, Scalar(after, "SELECT COUNT(*) FROM watch_events WHERE id=700"));
    }

    [Fact]
    public void V9Fixture_PreservesProviderIdentityAndAliasRows()
    {
        new DatabaseMigrator(_cs).Migrate(9);
        using (var db = Open())
        {
            SeedMovie(db, 3, "Provider Movie", "imdb", "READY");
            SeedShow(db, 4, "Provider Show");
        }
        new DatabaseMigrator(_cs).Migrate(10);
        new DatabaseMigrator(_cs).Migrate();
        using var db2 = Open();
        Assert.Equal("imdb", Scalar(db2, "SELECT provider FROM movies WHERE id=3"));
        Assert.Equal("m3", Scalar(db2, "SELECT external_id FROM movies WHERE id=3"));
        Assert.Equal(4L, Scalar(db2, "SELECT show_id FROM tv_show_aliases WHERE alias_sort_title='provider show'"));
        Assert.Equal(0L, Scalar(db2, "SELECT review_match FROM movies WHERE id=3"));
    }

    [Fact]
    public void CurrentV14ReviewMatch_OneSurvivesReopen()
    {
        new DatabaseMigrator(_cs).Migrate();
        using (var db = Open()) { SeedMovie(db, 5, "Reviewed"); Exec(db, "UPDATE movies SET review_match=1 WHERE id=5;"); }
        new DatabaseMigrator(_cs).Migrate();
        using var db2 = Open();
        Assert.Equal(1L, Scalar(db2, "SELECT review_match FROM movies WHERE id=5"));
    }

    [Fact]
    public async Task ConcurrentMigrators_SerializeAndLeaveOneValidLatestSchema()
    {
        var first = Task.Run(() => new DatabaseMigrator(_cs).Migrate());
        var second = Task.Run(() => new DatabaseMigrator(_cs).Migrate());
        await Task.WhenAll(first, second);
        using var db = Open();
        Assert.Equal(DatabaseMigrator.LatestVersion, Convert.ToInt32(Scalar(db, "PRAGMA user_version")));
        Assert.Equal("ok", Scalar(db, "PRAGMA integrity_check"));
        Assert.Equal(0L, Scalar(db, "SELECT COUNT(*) FROM pragma_foreign_key_check"));
    }

    [Fact]
    public void V11Fixture_PreservesMovieFileWatchlistAndCustomMembership()
    {
        new DatabaseMigrator(_cs).Migrate(11);
        using (var db = Open())
        {
            SeedMovie(db, 10, "Fixture Movie");
            Exec(db, "INSERT INTO media_files(id,movie_id,current_path,path_key,file_size,discovered_at,last_seen_at) VALUES (20,10,'C:\\fixture\\movie.mkv','c:\\fixture\\movie.mkv',123,'2026-01-01','2026-01-01');");
            Exec(db, "INSERT INTO movie_personal_state(movie_id,preference,watchlist_added_at,created_at,updated_at) VALUES (10,'LIKED','2026-01-02','2026-01-01','2026-01-01');");
            Exec(db, "INSERT INTO watch_events(id,media_kind,movie_id,watched_at,created_at,title_snapshot,year_snapshot,artwork_reference,backdrop_reference,poster_reference) VALUES (30,'MOVIE',10,'2026-01-03','2026-01-03','Fixture Movie',2025,'/art.jpg','/backdrop.jpg','/poster.jpg');");
            Exec(db, "INSERT INTO settings(key,value,updated_at) VALUES ('ui_custom_lists','[\"Keep\"]','2026-01-01');");
            Exec(db, "INSERT INTO custom_list_movies(list_key,movie_id,added_at) VALUES ('thrillers',10,'2026-01-04');");
        }
        new DatabaseMigrator(_cs).Migrate();
        using var db2 = Open();
        Assert.Equal("Fixture Movie", Scalar(db2, "SELECT title_snapshot FROM watch_events WHERE id=30"));
        Assert.Equal("LIKED", Scalar(db2, "SELECT preference FROM movie_personal_state WHERE movie_id=10"));
        Assert.Equal(1L, Scalar(db2, "SELECT COUNT(*) FROM media_files WHERE movie_id=10"));
        Assert.Equal(1L, Scalar(db2, "SELECT COUNT(*) FROM custom_list_members WHERE movie_id=10"));
        Assert.Equal(0L, Scalar(db2, "SELECT review_match FROM movies WHERE id=10"));
    }

    [Fact]
    public void V13Fixture_PreservesMixedMovieAndShowMembership()
    {
        new DatabaseMigrator(_cs).Migrate(13);
        using (var db = Open())
        {
            SeedMovie(db, 1, "Movie");
            SeedShow(db, 2, "Show");
            Exec(db, "INSERT INTO custom_lists(id,display_name,created_at) VALUES (9,'Mixed','2026-01-01');");
            Exec(db, "INSERT INTO custom_list_members(id,list_id,media_kind,movie_id,show_id,added_at) VALUES (90,9,'MOVIE',1,NULL,'2026-01-02'),(91,9,'SHOW',NULL,2,'2026-01-03');");
        }
        new DatabaseMigrator(_cs).Migrate();
        using var db2 = Open();
        Assert.Equal(2L, Scalar(db2, "SELECT COUNT(*) FROM custom_list_members WHERE list_id=9"));
        Assert.Equal(1L, Scalar(db2, "SELECT COUNT(*) FROM custom_list_members WHERE media_kind='MOVIE' AND movie_id=1"));
        Assert.Equal(1L, Scalar(db2, "SELECT COUNT(*) FROM custom_list_members WHERE media_kind='SHOW' AND show_id=2"));
    }

    [Fact]
    public void V13MissingRequiredIndex_FinalValidationFailureIsAtomic()
    {
        new DatabaseMigrator(_cs).Migrate(13);
        using (var db = Open()) Exec(db, "DROP INDEX idx_custom_list_members_show;");

        Assert.Throws<InvalidOperationException>(() => new DatabaseMigrator(_cs).Migrate());
        using var after = Open();
        Assert.Equal(13L, Scalar(after, "PRAGMA user_version"));
        Assert.Equal(0L, Scalar(after, "SELECT COUNT(*) FROM pragma_table_info('movies') WHERE name='review_match'"));
        Assert.Throws<InvalidOperationException>(() => new DatabaseMigrator(_cs).Migrate());
        Assert.Equal(13L, Scalar(after, "PRAGMA user_version"));
    }

    [Fact]
    public void FailedMigrationRetries_RetainVerifiedPreMigrationBackup()
    {
        new DatabaseMigrator(_cs).Migrate(13);
        using (var db = Open())
            SeedMovie(db, 77, "Retry Fixture");

        string original;
        using (var source = Open())
            original = DatabaseSafetyBackup.CreateVerifiedBackup(source, _cs, 13, "valid-prebreak", pruneOldBackups: false);
        var originalBytes = File.ReadAllBytes(original);
        using (var db = Open()) Exec(db, "ALTER TABLE movies ADD COLUMN review_match INTEGER NOT NULL DEFAULT 0;");

        var migrator = new DatabaseMigrator(_cs);
        Assert.True(DatabaseSafetyBackup.VerifyBackup(original, out var version));
        Assert.Equal(13, version);

        for (var attempt = 0; attempt < DatabaseSafetyBackup.RetentionCount + 2; attempt++)
            Assert.Throws<SqliteException>(() => migrator.Migrate());
        Assert.Equal(originalBytes, File.ReadAllBytes(original));
        using (var after = Open())
            Assert.Equal(13L, Scalar(after, "PRAGMA user_version"));

        var restorePath = Path.Combine(_directory, "restored-from-valid-v13.db");
        File.Copy(original, restorePath, overwrite: true);
        var restoredConnection = $"Data Source={restorePath}";
        new DatabaseMigrator(restoredConnection).Migrate();
        using var restored = new SqliteConnection(restoredConnection);
        restored.Open();
        Assert.Equal("Retry Fixture", Scalar(restored, "SELECT title FROM movies WHERE id=77"));
        Assert.Equal((long)DatabaseMigrator.LatestVersion, Scalar(restored, "PRAGMA user_version"));
    }

    [Fact]
    public void UnknownNonEmptyVersionZeroDatabase_IsRejectedWithoutStamping()
    {
        using (var db = Open()) Exec(db, "CREATE TABLE foreign_app(id INTEGER PRIMARY KEY, value TEXT); INSERT INTO foreign_app VALUES (1,'keep');");
        Assert.Throws<InvalidOperationException>(() => new DatabaseMigrator(_cs).Migrate());
        using var after = Open();
        Assert.Equal(0L, Scalar(after, "PRAGMA user_version"));
        Assert.Equal(1L, Scalar(after, "SELECT COUNT(*) FROM foreign_app"));
    }

    [Fact]
    public void UnsupportedFutureVersion_PreservesRowsAndDeleteJournalMode()
    {
        using (var db = Open())
        {
            Exec(db, "PRAGMA journal_mode=DELETE; CREATE TABLE future_rows(id INTEGER PRIMARY KEY,value TEXT); INSERT INTO future_rows VALUES (1,'keep'); PRAGMA user_version=99;");
        }
        Assert.Throws<InvalidOperationException>(() => new DatabaseMigrator(_cs).Migrate());
        using var after = Open();
        Assert.Equal(99L, Scalar(after, "PRAGMA user_version"));
        Assert.Equal("keep", Scalar(after, "SELECT value FROM future_rows WHERE id=1"));
        Assert.Equal("delete", Convert.ToString(Scalar(after, "PRAGMA journal_mode"))!.ToLowerInvariant());
        Assert.Throws<ArgumentOutOfRangeException>(() => new DatabaseMigrator(_cs).Migrate(-1));
    }

    [Fact]
    public void CurrentDatabase_ReopenIsRowAndSchemaStable()
    {
        new DatabaseMigrator(_cs).Migrate();
        using (var db = Open()) { SeedMovie(db, 4, "Stable"); Exec(db, "PRAGMA wal_checkpoint(FULL);"); }
        var before = Snapshot();
        new DatabaseMigrator(_cs).Migrate();
        Assert.Equal(before, Snapshot());
    }

    private SqliteConnection Open()
    {
        var db = new SqliteConnection(_cs);
        db.Open();
        Exec(db, "PRAGMA foreign_keys=ON;");
        return db;
    }

    private string Snapshot()
    {
        using var db = Open();
        var queries = new[]
        {
            "SELECT id,provider,external_id,title,metadata_status,review_match FROM movies ORDER BY id",
            "SELECT id,movie_id,current_path,path_key,file_size,status FROM media_files ORDER BY id",
            "SELECT id,media_kind,movie_id,episode_id,title_snapshot,backdrop_reference,poster_reference FROM watch_events ORDER BY id",
            "SELECT id,display_name,created_at FROM custom_lists ORDER BY id",
            "SELECT id,list_id,media_kind,movie_id,show_id,added_at FROM custom_list_members ORDER BY id",
            "SELECT key,value,updated_at FROM settings ORDER BY key"
        };
        var values = queries.Select(query =>
        {
            using var command = db.CreateCommand();
            command.CommandText = query;
            using var reader = command.ExecuteReader();
            var rows = new System.Collections.Generic.List<string>();
            while (reader.Read())
                rows.Add(string.Join(",", Enumerable.Range(0, reader.FieldCount).Select(reader.IsDBNull).Select((isNull, i) => isNull ? "<NULL>" : reader.GetValue(i).ToString())));
            return string.Join(";", rows);
        });
        return string.Join("|", values) + ":v" + Scalar(db, "PRAGMA user_version") + ":schema" + Scalar(db, "SELECT group_concat(name,',') FROM sqlite_master WHERE type='table' ORDER BY name");
    }

    private static void SeedMovie(SqliteConnection db, int id, string title)
        => SeedMovie(db, id, title, "tmdb", "READY");

    private static void SeedMovie(SqliteConnection db, int id, string title, string provider, string status)
        => Exec(db, $"INSERT INTO movies(id,provider,external_id,title,date_added,created_at,updated_at,genres,metadata_status) VALUES ({id},{(provider == "NULL" ? "NULL" : $"'{provider}'")},{(provider == "NULL" ? "NULL" : $"'m{id}'")},'{title}','2026-01-01','2026-01-01','2026-01-01','[]','{status}');");

    private static void SeedShow(SqliteConnection db, int id, string title)
        => Exec(db, $"INSERT INTO tv_shows(id,provider,external_id,title,sort_title,date_added,created_at,updated_at,genres,metadata_status) VALUES ({id},'tmdb','s{id}','{title}','{title.ToLowerInvariant()}','2026-01-01','2026-01-01','2026-01-01','[]','READY');");

    private static void Exec(SqliteConnection db, string sql)
    {
        using var cmd = db.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery();
    }

    private static object? Scalar(SqliteConnection db, string sql)
    {
        using var cmd = db.CreateCommand(); cmd.CommandText = sql; return cmd.ExecuteScalar();
    }

    private static bool IsDbNull(object? value) => value is null || value is DBNull;
}
