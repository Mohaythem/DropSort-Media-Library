using System.Globalization;
using System.Text.Json;
using DropSort.Infrastructure.Persistence.Migrations;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DropSort.Tests.Infrastructure;

/// <summary>Complete common-column snapshots across realistic catalog-era schemas.</summary>
public sealed class DatabaseHistoricalRowsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"dropsort_historical_rows_{Guid.NewGuid():N}");
    private readonly string _cs;

    public DatabaseHistoricalRowsTests()
    {
        Directory.CreateDirectory(_directory);
        _cs = new SqliteConnectionStringBuilder { DataSource = Path.Combine(_directory, "library.db"), Pooling = false }.ToString();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    public void HistoricalUpgrade_PreservesEverySeededValueAndRelationship(int version)
    {
        var migrator = new DatabaseMigrator(_cs);
        migrator.Migrate(version);
        var snapshots = new Dictionary<string, (string Columns, string[] Rows)>();
        using (var db = Open())
        {
            Seed(db, version);
            foreach (var table in new[] { "movies", "media_files", "metadata_cache", "file_operations", "watched_folders", "settings", "movie_personal_state", "tv_shows", "tv_seasons", "tv_episodes", "episode_media_files" })
            {
                var columns = Columns(db, table);
                snapshots[table] = (columns, Rows(db, $"SELECT {columns} FROM {table} ORDER BY 1"));
            }
            if (version >= 8)
                snapshots["watch_events"] = (Columns(db, "watch_events"), Rows(db, "SELECT * FROM watch_events ORDER BY id"));
            if (version >= 10)
                snapshots["tv_show_aliases"] = (Columns(db, "tv_show_aliases"), Rows(db, "SELECT * FROM tv_show_aliases ORDER BY id"));
            if (version >= 12)
            {
                snapshots["custom_lists"] = (Columns(db, "custom_lists"), Rows(db, "SELECT * FROM custom_lists ORDER BY id"));
                var columns = version == 12 ? "id,list_id,movie_id,added_at" : Columns(db, "custom_list_members");
                snapshots["custom_list_members"] = (columns, Rows(db, $"SELECT {columns} FROM custom_list_members ORDER BY id"));
            }
        }

        migrator.Migrate();
        using var after = Open();
        foreach (var (table, snapshot) in snapshots)
            Assert.Equal(snapshot.Rows, Rows(after, $"SELECT {snapshot.Columns} FROM {table} ORDER BY 1"));
        Assert.Equal(14L, Scalar(after, "PRAGMA user_version"));
        Assert.Equal(version == 14 ? 1L : 0L, Scalar(after, "SELECT review_match FROM movies WHERE id=10"));
        Assert.Equal("ok", Scalar(after, "PRAGMA integrity_check"));
        Assert.Empty(Rows(after, "PRAGMA foreign_key_check"));
        Assert.Equal("1", Convert.ToString(Scalar(after, "PRAGMA foreign_keys"), CultureInfo.InvariantCulture));
        if (version == 7)
        {
            Assert.Equal(new[] { "[60,\"MOVIE\",10,null,\"2026-01-03\",\"2026-01-03\",\"Matched Movie\",2025,\"/movie-back.jpg\",\"/movie-poster.jpg\"]" },
                Rows(after, "SELECT id,media_kind,movie_id,episode_id,watched_at,created_at,title_snapshot,year_snapshot,artwork_reference,poster_reference FROM watch_events ORDER BY id"));
        }
        if (version == 11)
        {
            Assert.Equal("Keep", Scalar(after, "SELECT display_name FROM custom_lists ORDER BY id LIMIT 1"));
            Assert.Equal(new[] { "[\"MOVIE\",10,\"2026-01-04\"]" }, Rows(after, "SELECT media_kind,movie_id,added_at FROM custom_list_members"));
        }
        Assert.Equal(30L, Scalar(after, "SELECT show_id FROM tv_show_aliases WHERE alias_sort_title='fixture show'"));
        // Running migration again must preserve all rows and the SQL schema, and take no extra backup.
        var current = FullSnapshot(after);
        var backupDirectory = Path.Combine(_directory, "backups");
        var backupCount = Directory.Exists(backupDirectory) ? Directory.GetFiles(backupDirectory, "*.db").Length : 0;
        migrator.Migrate();
        Assert.Equal(current, FullSnapshot(after));
        Assert.Equal(backupCount, Directory.Exists(backupDirectory) ? Directory.GetFiles(backupDirectory, "*.db").Length : 0);
    }

    [Fact]
    public void RebuildFailure_RollsBackDroppedTableRowsAndVersion_ThenRetriesAfterRepair()
    {
        var migrator = new DatabaseMigrator(_cs);
        migrator.Migrate(12);
        string before;
        using (var db = Open())
        {
            Seed(db, 12);
            // v13 fails late, after it copied and dropped the membership table.
            Exec(db, "CREATE UNIQUE INDEX ux_custom_list_members_show ON settings(key);");
            before = FullSnapshot(db);
        }
        Assert.Throws<SqliteException>(() => migrator.Migrate());
        using (var db = Open())
        {
            Assert.Equal(12L, Scalar(db, "PRAGMA user_version"));
            Assert.Equal(before, FullSnapshot(db));
            Exec(db, "DROP INDEX ux_custom_list_members_show;");
        }
        migrator.Migrate();
        using var after = Open();
        Assert.Equal(new[] { "[80,70,\"MOVIE\",10,null,\"2026-01-04\"]" }, Rows(after, "SELECT id,list_id,media_kind,movie_id,show_id,added_at FROM custom_list_members"));
        Assert.Empty(Rows(after, "PRAGMA foreign_key_check"));
        Assert.Equal("ok", Scalar(after, "PRAGMA integrity_check"));
    }

    [Fact]
    public void CurrentStampWithoutReviewMatchColumn_IsRejected()
    {
        new DatabaseMigrator(_cs).Migrate(13);
        using (var db = Open()) Exec(db, "PRAGMA user_version=14;");
        Assert.Throws<InvalidOperationException>(() => new DatabaseMigrator(_cs).Migrate());
        using var after = Open();
        Assert.Equal(14L, Scalar(after, "PRAGMA user_version"));
        Assert.Equal(0L, Scalar(after, "SELECT COUNT(*) FROM pragma_table_info('movies') WHERE name='review_match'"));
    }

    [Fact]
    public void InvalidNegativeSourceVersion_IsRejectedBeforeJournalChanges()
    {
        using (var db = Open()) Exec(db, "PRAGMA journal_mode=DELETE; PRAGMA user_version=-1;");
        Assert.Throws<InvalidOperationException>(() => new DatabaseMigrator(_cs).Migrate());
        using var after = Open();
        Assert.Equal(-1L, Scalar(after, "PRAGMA user_version"));
        Assert.Equal("delete", Scalar(after, "PRAGMA journal_mode"));
    }

    [Fact]
    public void UnknownVersionZeroSchema_WithSqliteLikeUserName_IsRejected()
    {
        using (var db = Open()) Exec(db, "CREATE TABLE sqlitex_user(value TEXT); INSERT INTO sqlitex_user VALUES ('keep');");
        Assert.Throws<InvalidOperationException>(() => new DatabaseMigrator(_cs).Migrate());
        using var after = Open();
        Assert.Equal("keep", Scalar(after, "SELECT value FROM sqlitex_user"));
        Assert.Equal(0L, Scalar(after, "PRAGMA user_version"));
    }

    [Fact]
    public void ConstraintsAndDeleteCascades_PreserveDurableHistoryAndCatalogFileRecords()
    {
        new DatabaseMigrator(_cs).Migrate();
        using var db = Open();
        Seed(db, 14);
        foreach (var sql in new[]
        {
            "UPDATE movies SET external_id='m10' WHERE id=11", // offline row needs matching provider too below
            "UPDATE movies SET provider='tmdb',external_id='m10',metadata_status='READY' WHERE id=11",
            "UPDATE movies SET provider='tmdb' WHERE id=11",
            "UPDATE tv_seasons SET season_number=1000 WHERE id=31",
            "UPDATE media_files SET movie_id=999 WHERE id=20",
            "INSERT INTO episode_media_files VALUES (22,32,'duplicate')",
            "INSERT INTO custom_list_members VALUES (99,70,'MOVIE',10,NULL,'duplicate')",
            "INSERT INTO custom_list_members VALUES (99,70,'SHOW',10,30,'invalid')"
        }) Assert.Throws<SqliteException>(() => Exec(db, sql));

        Exec(db, "DELETE FROM movies WHERE id=10; DELETE FROM tv_shows WHERE id=30;");
        Assert.Empty(Rows(db, "SELECT * FROM movie_personal_state"));
        Assert.Empty(Rows(db, "SELECT * FROM tv_seasons"));
        Assert.Empty(Rows(db, "SELECT * FROM tv_episodes"));
        Assert.Empty(Rows(db, "SELECT * FROM episode_media_files"));
        Assert.Empty(Rows(db, "SELECT * FROM custom_list_members"));
        Assert.Equal(new[] { "[20,null]", "[21,null]", "[22,null]", "[23,null]" }, Rows(db, "SELECT id,movie_id FROM media_files ORDER BY id"));
        Assert.Equal(new[] { "[60,\"MOVIE\",null,null,\"Matched Movie\"]", "[61,\"EPISODE\",null,null,\"Episode Two\"]" }, Rows(db, "SELECT id,media_kind,movie_id,episode_id,title_snapshot FROM watch_events ORDER BY id"));
        Assert.Equal(20L, Scalar(db, "SELECT media_file_id FROM file_operations WHERE id='operation-1'"));
        Exec(db, "DELETE FROM media_files WHERE id=20;");
        Assert.Null(Scalar(db, "SELECT media_file_id FROM file_operations WHERE id='operation-1'"));
        Assert.Empty(Rows(db, "PRAGMA foreign_key_check"));
    }

    private static void Seed(SqliteConnection db, int version)
    {
        Exec(db, """
            INSERT INTO movies(id,provider,external_id,title,original_title,year,overview,runtime_minutes,rating,poster_path,date_added,created_at,updated_at,genres,metadata_status,backdrop_path,tagline)
            VALUES (10,'tmdb','m10','Matched Movie','Original',2025,'Overview',105,8.25,'/movie-poster.jpg','2026-01-01','2026-01-01','2026-01-02','["Drama"]','READY','/movie-back.jpg','Tagline'),
                   (11,NULL,NULL,'Offline Movie',NULL,NULL,NULL,NULL,NULL,NULL,'2026-01-01','2026-01-01','2026-01-01','[]','PENDING',NULL,NULL);
            INSERT INTO tv_shows(id,provider,external_id,title,sort_title,original_title,year,overview,genres,poster_path,metadata_status,date_added,created_at,updated_at,backdrop_path,rating,tagline)
            VALUES (30,'tmdb','s30','Fixture Show','fixture show','Original Show',2024,'Show Overview','["Comedy"]','/show-poster.jpg','READY','2026-01-01','2026-01-01','2026-01-02','/show-back.jpg',7.5,'Show Tagline');
            INSERT INTO tv_seasons(id,show_id,season_number,title,overview,created_at,updated_at,poster_path,air_date,external_id)
            VALUES (31,30,1,'Season One','Season Overview','2026-01-01','2026-01-02','/season.jpg','2024-01-01','season31');
            INSERT INTO tv_episodes(id,season_id,episode_number,title,overview,runtime_minutes,air_date,created_at,updated_at,still_path,rating,external_id)
            VALUES (32,31,2,'Episode Two','Episode Overview',42,'2024-01-02','2026-01-01','2026-01-02','/still.jpg',9.0,'episode32');
            INSERT INTO media_files(id,movie_id,current_path,path_key,file_size,extension,resolution,codec,source,status,discovered_at,last_seen_at)
            VALUES (20,10,'C:\fixture\movie.mkv','movie',123,'.mkv','1080p','h264','bluray','PRESENT','2026-01-01','2026-01-02'),
                   (21,10,'C:\fixture\movie-4k.mkv','movie4k',456,'.mkv','2160p','h265','web','MISSING','2026-01-01','2026-01-02'),
                   (22,NULL,'C:\fixture\episode.mkv','episode',789,'.mkv','1080p','h264','web','PRESENT','2026-01-01','2026-01-02'),
                   (23,NULL,'C:\fixture\episode-4k.mkv','episode4k',987,'.mkv','2160p','h265','web','PRESENT','2026-01-01','2026-01-02');
            INSERT INTO episode_media_files VALUES (22,32,'2026-01-02'),(23,32,'2026-01-03');
            INSERT INTO movie_personal_state VALUES (10,'LIKED','2026-01-04','2026-01-01','2026-01-04');
            INSERT INTO metadata_cache VALUES (40,'tmdb','movie:m10','{"id":"m10","title":"Matched Movie"}','2026-01-01','2026-12-31');
            INSERT INTO watched_folders VALUES (50,'C:\fixture','SCAN',1,'2026-01-01');
            INSERT INTO settings VALUES ('ui_media_folders','["C:\\fixture"]','2026-01-01'),('ui_custom_lists','["Keep"]','2026-01-01');
            INSERT INTO file_operations(id,operation_type,source_path,destination_path,state,media_file_id,source_dev,source_ino,created_at,updated_at)
            VALUES ('operation-1','MOVE','C:\fixture\old.mkv','C:\fixture\movie.mkv','COMMITTED',20,'18446744073709551615','18446744073709551614','2026-01-01','2026-01-02');
            """);
        if (version == 7) Exec(db, "INSERT INTO watch_events VALUES (60,10,'2026-01-03','2026-01-03');");
        else Exec(db, """
            INSERT INTO watch_events(id,media_kind,movie_id,episode_id,watched_at,created_at,title_snapshot,year_snapshot,show_title_snapshot,season_number_snapshot,episode_number_snapshot,artwork_reference,backdrop_reference,poster_reference)
            VALUES (60,'MOVIE',10,NULL,'2026-01-03','2026-01-03','Matched Movie',2025,NULL,NULL,NULL,'/movie-back.jpg','/movie-back.jpg','/movie-poster.jpg'),
                   (61,'EPISODE',NULL,32,'2026-01-04','2026-01-04','Episode Two',2024,'Fixture Show',1,2,'/still.jpg','/show-back.jpg','/show-poster.jpg');
            """);
        if (version >= 10) Exec(db, "INSERT INTO tv_show_aliases VALUES (90,30,'fixture show','2026-01-01');");
        if (version == 11) Exec(db, "INSERT INTO custom_list_movies VALUES (80,'thrillers',10,'2026-01-04');");
        if (version >= 12)
        {
            Exec(db, "INSERT INTO custom_lists VALUES (70,'Keep','2026-01-01');");
            if (version == 12) Exec(db, "INSERT INTO custom_list_members VALUES (80,70,10,'2026-01-04');");
            else Exec(db, "INSERT INTO custom_list_members VALUES (80,70,'MOVIE',10,NULL,'2026-01-04'),(81,70,'SHOW',NULL,30,'2026-01-05');");
        }
        if (version == 14) Exec(db, "UPDATE movies SET review_match=1 WHERE id=10;");
    }

    private SqliteConnection Open()
    {
        var db = new SqliteConnection(_cs);
        db.Open();
        Exec(db, "PRAGMA foreign_keys=ON;");
        return db;
    }

    private static string Columns(SqliteConnection db, string table)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table})";
        using var reader = cmd.ExecuteReader();
        var columns = new List<string>();
        while (reader.Read()) columns.Add(reader.GetString(1));
        return string.Join(',', columns);
    }

    private static string[] Rows(SqliteConnection db, string sql)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        using var reader = cmd.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read())
        {
            var values = new object?[reader.FieldCount];
            for (var i = 0; i < values.Length; i++) values[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(JsonSerializer.Serialize(values));
        }
        return rows.ToArray();
    }

    private static string FullSnapshot(SqliteConnection db)
    {
        var schema = Rows(db, "SELECT type,name,tbl_name,sql FROM sqlite_master WHERE name NOT GLOB 'sqlite_*' ORDER BY type,name");
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT GLOB 'sqlite_*' ORDER BY name";
        var tables = new List<string>();
        using (var reader = cmd.ExecuteReader()) while (reader.Read()) tables.Add(reader.GetString(0));
        return string.Join('\n', schema.Concat(tables.SelectMany(table => Rows(db, $"SELECT * FROM {table} ORDER BY 1"))));
    }

    private static void Exec(SqliteConnection db, string sql) { using var cmd = db.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery(); }
    private static object? Scalar(SqliteConnection db, string sql) { using var cmd = db.CreateCommand(); cmd.CommandText = sql; var value = cmd.ExecuteScalar(); return value is DBNull ? null : value; }
}
