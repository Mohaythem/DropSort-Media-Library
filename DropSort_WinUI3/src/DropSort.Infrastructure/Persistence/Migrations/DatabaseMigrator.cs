using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace DropSort.Infrastructure.Persistence.Migrations;

public class DatabaseMigrator
{
    public const int LatestVersion = 9;
    private readonly string _connectionString;

    public DatabaseMigrator(string connectionString)
    {
        _connectionString = connectionString;
    }

    private static readonly IReadOnlyList<string> MigrationScripts = new[]
    {
        // 0001_initial
        @"
            CREATE TABLE movies (
                id INTEGER PRIMARY KEY,
                provider TEXT NOT NULL,
                external_id TEXT NOT NULL,
                title TEXT NOT NULL,
                original_title TEXT,
                year INTEGER,
                overview TEXT,
                runtime_minutes INTEGER,
                rating REAL,
                poster_path TEXT,
                date_added TEXT NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                UNIQUE(provider, external_id)
            );

            CREATE TABLE media_files (
                id INTEGER PRIMARY KEY,
                movie_id INTEGER REFERENCES movies(id) ON DELETE SET NULL,
                current_path TEXT NOT NULL,
                path_key TEXT NOT NULL UNIQUE,
                file_size INTEGER NOT NULL CHECK(file_size >= 0),
                extension TEXT,
                resolution TEXT,
                codec TEXT,
                source TEXT,
                status TEXT NOT NULL DEFAULT 'PRESENT' CHECK(status IN ('PRESENT', 'MISSING')),
                discovered_at TEXT NOT NULL,
                last_seen_at TEXT NOT NULL
            );

            CREATE TABLE metadata_cache (
                id INTEGER PRIMARY KEY,
                provider TEXT NOT NULL,
                cache_key TEXT NOT NULL,
                payload TEXT NOT NULL,
                fetched_at TEXT NOT NULL,
                expires_at TEXT,
                UNIQUE(provider, cache_key)
            );

            CREATE TABLE file_operations (
                id TEXT PRIMARY KEY,
                operation_type TEXT NOT NULL CHECK(operation_type IN ('MOVE', 'RENAME')),
                source_path TEXT NOT NULL,
                destination_path TEXT NOT NULL,
                state TEXT NOT NULL CHECK(state IN (
                    'PLANNED', 'VALIDATED', 'EXECUTING', 'FS_VERIFIED',
                    'COMMITTED', 'FAILED', 'RECOVERY_REQUIRED'
                )),
                media_file_id INTEGER REFERENCES media_files(id) ON DELETE SET NULL,
                reverses_operation_id TEXT REFERENCES file_operations(id) ON DELETE SET NULL,
                source_size INTEGER,
                source_mtime_ns INTEGER,
                source_dev TEXT,
                source_ino TEXT,
                destination_size INTEGER,
                destination_mtime_ns INTEGER,
                destination_dev TEXT,
                destination_ino TEXT,
                destination_sha256 TEXT,
                strategy TEXT,
                error_code TEXT,
                error_message TEXT,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );

            CREATE INDEX idx_file_operations_state ON file_operations(state);
            CREATE INDEX idx_file_operations_media_file_id ON file_operations(media_file_id);

            CREATE TABLE watched_folders (
                id INTEGER PRIMARY KEY,
                path TEXT NOT NULL COLLATE NOCASE UNIQUE,
                folder_role TEXT NOT NULL CHECK(folder_role IN ('MOVIES', 'SCAN')),
                enabled INTEGER NOT NULL DEFAULT 1 CHECK(enabled IN (0, 1)),
                created_at TEXT NOT NULL
            );

            CREATE TABLE settings (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
        ",
        // 0002_portable_filesystem_identity
        @"
            PRAGMA defer_foreign_keys = ON;

            CREATE TABLE file_operations_portable (
                id TEXT PRIMARY KEY,
                operation_type TEXT NOT NULL CHECK(operation_type IN ('MOVE', 'RENAME')),
                source_path TEXT NOT NULL,
                destination_path TEXT NOT NULL,
                state TEXT NOT NULL CHECK(state IN (
                    'PLANNED', 'VALIDATED', 'EXECUTING', 'FS_VERIFIED',
                    'COMMITTED', 'FAILED', 'RECOVERY_REQUIRED'
                )),
                media_file_id INTEGER REFERENCES media_files(id) ON DELETE SET NULL,
                reverses_operation_id TEXT REFERENCES file_operations_portable(id) ON DELETE SET NULL,
                source_size INTEGER,
                source_mtime_ns INTEGER,
                source_dev TEXT,
                source_ino TEXT,
                destination_size INTEGER,
                destination_mtime_ns INTEGER,
                destination_dev TEXT,
                destination_ino TEXT,
                destination_sha256 TEXT,
                strategy TEXT,
                error_code TEXT,
                error_message TEXT,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );

            INSERT INTO file_operations_portable (
                id, operation_type, source_path, destination_path, state,
                media_file_id, reverses_operation_id,
                source_size, source_mtime_ns, source_dev, source_ino,
                destination_size, destination_mtime_ns, destination_dev, destination_ino,
                destination_sha256, strategy, error_code, error_message, created_at, updated_at
            )
            SELECT
                id, operation_type, source_path, destination_path, state,
                media_file_id, reverses_operation_id,
                source_size, source_mtime_ns, CAST(source_dev AS TEXT), CAST(source_ino AS TEXT),
                destination_size, destination_mtime_ns,
                CAST(destination_dev AS TEXT), CAST(destination_ino AS TEXT),
                destination_sha256, strategy, error_code, error_message, created_at, updated_at
            FROM file_operations;

            DROP TABLE file_operations;
            ALTER TABLE file_operations_portable RENAME TO file_operations;

            CREATE INDEX idx_file_operations_state ON file_operations(state);
            CREATE INDEX idx_file_operations_media_file_id ON file_operations(media_file_id);
        ",
        // 0003_movie_catalog
        @"
            ALTER TABLE movies ADD COLUMN genres TEXT NOT NULL DEFAULT '[]';
            CREATE INDEX idx_media_files_movie_id ON media_files(movie_id);
        ",
        // 0004_personal_library_foundation
        @"
            CREATE TABLE movie_personal_state (
                movie_id INTEGER PRIMARY KEY REFERENCES movies(id) ON DELETE CASCADE,
                preference TEXT NOT NULL DEFAULT 'NO_OPINION'
                    CHECK(preference IN ('NO_OPINION', 'LIKED', 'BLACKLISTED')),
                watchlist_added_at TEXT,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );

            CREATE INDEX idx_movie_personal_state_preference ON movie_personal_state(preference);
            CREATE INDEX idx_movie_personal_state_watchlist ON movie_personal_state(watchlist_added_at);

            CREATE TABLE watch_events (
                id INTEGER PRIMARY KEY,
                movie_id INTEGER NOT NULL REFERENCES movies(id) ON DELETE CASCADE,
                watched_at TEXT NOT NULL,
                created_at TEXT NOT NULL
            );

            CREATE INDEX idx_watch_events_movie_watched ON watch_events(movie_id, watched_at, id);
        ",
        // 0005_offline_movie_registration
        @"
            PRAGMA defer_foreign_keys = ON;

            CREATE TABLE movies_offline_registration (
                id INTEGER PRIMARY KEY,
                provider TEXT,
                external_id TEXT,
                title TEXT NOT NULL,
                original_title TEXT,
                year INTEGER,
                overview TEXT,
                runtime_minutes INTEGER,
                rating REAL,
                poster_path TEXT,
                date_added TEXT NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                genres TEXT NOT NULL DEFAULT '[]',
                metadata_status TEXT NOT NULL DEFAULT 'PENDING'
                    CHECK(metadata_status IN ('PENDING', 'READY', 'FAILED', 'NEEDS_MATCH')),
                CHECK (
                    (provider IS NULL AND external_id IS NULL)
                    OR (
                        provider IS NOT NULL
                        AND external_id IS NOT NULL
                        AND length(trim(provider)) > 0
                        AND length(trim(external_id)) > 0
                    )
                ),
                CHECK (
                    metadata_status != 'READY'
                    OR (provider IS NOT NULL AND external_id IS NOT NULL)
                ),
                UNIQUE(provider, external_id)
            );

            INSERT INTO movies_offline_registration (
                id, provider, external_id, title, original_title, year, overview,
                runtime_minutes, rating, poster_path, date_added, created_at, updated_at,
                genres, metadata_status
            )
            SELECT
                id, provider, external_id, title, original_title, year, overview,
                runtime_minutes, rating, poster_path, date_added, created_at, updated_at,
                genres, 'READY'
            FROM movies;

            DROP TABLE movies;
            ALTER TABLE movies_offline_registration RENAME TO movies;
        ",
        // 0006_tv_hierarchy
        //
        // The TV catalog: show -> season -> episode, plus the link from an episode to the media files
        // already registered in media_files. Additive only - no existing table is rewritten, so an
        // existing movie library migrates by gaining empty tables.
        //
        // Identity is the row id everywhere. sort_title is the normalized title registration matches on
        // (see TvShowCatalogData.NormalizeTitle) and is unique, so scanning the same show twice reuses
        // one show row instead of creating a second. The pair uniqueness on (show_id, season_number) and
        // (season_id, episode_number) is what keeps a repeated scan idempotent.
        //
        // episode_media_files has the media file as its primary key: one file belongs to at most one
        // episode, while an episode can carry several files (a 1080p and a 4K copy of the same episode).
        // Deleting a media file removes the link and leaves the episode registered; that is the same
        // rule the movie side follows for a missing file.
        @"
            CREATE TABLE tv_shows (
                id INTEGER PRIMARY KEY,
                provider TEXT,
                external_id TEXT,
                title TEXT NOT NULL,
                sort_title TEXT NOT NULL,
                original_title TEXT,
                year INTEGER,
                overview TEXT,
                genres TEXT NOT NULL DEFAULT '[]',
                poster_path TEXT,
                metadata_status TEXT NOT NULL DEFAULT 'PENDING'
                    CHECK(metadata_status IN ('PENDING', 'READY', 'FAILED', 'NEEDS_MATCH')),
                date_added TEXT NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                CHECK (length(trim(title)) > 0),
                CHECK (length(trim(sort_title)) > 0),
                CHECK (year IS NULL OR (year >= 1 AND year <= 9999)),
                CHECK (
                    (provider IS NULL AND external_id IS NULL)
                    OR (
                        provider IS NOT NULL
                        AND external_id IS NOT NULL
                        AND length(trim(provider)) > 0
                        AND length(trim(external_id)) > 0
                    )
                ),
                CHECK (
                    metadata_status != 'READY'
                    OR (provider IS NOT NULL AND external_id IS NOT NULL)
                ),
                UNIQUE(provider, external_id)
            );

            CREATE UNIQUE INDEX idx_tv_shows_sort_title ON tv_shows(sort_title);

            CREATE TABLE tv_seasons (
                id INTEGER PRIMARY KEY,
                show_id INTEGER NOT NULL REFERENCES tv_shows(id) ON DELETE CASCADE,
                season_number INTEGER NOT NULL CHECK(season_number >= 0 AND season_number <= 999),
                title TEXT,
                overview TEXT,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                UNIQUE(show_id, season_number)
            );

            CREATE INDEX idx_tv_seasons_show_id ON tv_seasons(show_id);

            CREATE TABLE tv_episodes (
                id INTEGER PRIMARY KEY,
                season_id INTEGER NOT NULL REFERENCES tv_seasons(id) ON DELETE CASCADE,
                episode_number INTEGER NOT NULL CHECK(episode_number >= 0 AND episode_number <= 999),
                title TEXT,
                overview TEXT,
                runtime_minutes INTEGER CHECK(runtime_minutes IS NULL OR runtime_minutes > 0),
                air_date TEXT,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                UNIQUE(season_id, episode_number)
            );

            CREATE INDEX idx_tv_episodes_season_id ON tv_episodes(season_id);

            CREATE TABLE episode_media_files (
                media_file_id INTEGER PRIMARY KEY REFERENCES media_files(id) ON DELETE CASCADE,
                episode_id INTEGER NOT NULL REFERENCES tv_episodes(id) ON DELETE CASCADE,
                linked_at TEXT NOT NULL
            );

            CREATE INDEX idx_episode_media_files_episode_id ON episode_media_files(episode_id);
        ",
        // 0007_media_details_enrichment
        @"
            ALTER TABLE movies ADD COLUMN backdrop_path TEXT;
            ALTER TABLE movies ADD COLUMN tagline TEXT;

            ALTER TABLE tv_shows ADD COLUMN backdrop_path TEXT;
            ALTER TABLE tv_shows ADD COLUMN rating REAL;
            ALTER TABLE tv_shows ADD COLUMN tagline TEXT;

            ALTER TABLE tv_seasons ADD COLUMN poster_path TEXT;
            ALTER TABLE tv_seasons ADD COLUMN air_date TEXT;
            ALTER TABLE tv_seasons ADD COLUMN external_id TEXT;

            ALTER TABLE tv_episodes ADD COLUMN still_path TEXT;
            ALTER TABLE tv_episodes ADD COLUMN rating REAL;
            ALTER TABLE tv_episodes ADD COLUMN external_id TEXT;
        ",
        // 0008_durable_watch_history
        @"
            PRAGMA defer_foreign_keys = ON;
            CREATE TABLE watch_events_new (
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
                backdrop_reference TEXT,
                poster_reference TEXT,
                CHECK (
                    (media_kind = 'MOVIE' AND episode_id IS NULL) OR
                    (media_kind = 'EPISODE' AND movie_id IS NULL)
                )
            );
            INSERT INTO watch_events_new
                (id, media_kind, movie_id, watched_at, created_at, title_snapshot, year_snapshot,
                 artwork_reference, poster_reference)
            SELECT w.id, 'MOVIE', w.movie_id, w.watched_at, w.created_at, m.title, m.year,
                   COALESCE(m.backdrop_path, m.poster_path), m.poster_path
              FROM watch_events w JOIN movies m ON m.id = w.movie_id;
            DROP TABLE watch_events;
            ALTER TABLE watch_events_new RENAME TO watch_events;
            CREATE INDEX idx_watch_events_movie_watched ON watch_events(movie_id, julianday(watched_at), id);
            CREATE INDEX idx_watch_events_episode_watched ON watch_events(episode_id, julianday(watched_at), id);
            CREATE INDEX idx_watch_events_recent ON watch_events(julianday(watched_at) DESC, id DESC);
        ",
        // 0009_reconcile_watch_events_artwork_columns
        // Corrective migration for schema drift: some databases reached user_version = 8 under an
        // earlier revision of the 0008 script that did NOT create the backdrop_reference and
        // poster_reference columns. Because the migrator never re-runs a version it has already
        // stamped, those installs are permanently stuck on the old watch_events shape, and every
        // write (Mark Watched) fails with 'table watch_events has no column named poster_reference'.
        // Rebuild the table from the columns that exist in BOTH shapes so the schema is normalised
        // regardless of which 0008 the database was migrated under. Pre-existing rows keep their data;
        // the two artwork columns are left NULL for legacy rows (read paths already tolerate NULL) and
        // are populated normally for events written after this migration. Fresh v8 databases have no
        // watch_events rows yet when this runs, so no data is lost by copying only the shared columns.
        @"
            PRAGMA defer_foreign_keys = ON;
            CREATE TABLE watch_events_reconciled (
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
                backdrop_reference TEXT,
                poster_reference TEXT,
                CHECK (
                    (media_kind = 'MOVIE' AND episode_id IS NULL) OR
                    (media_kind = 'EPISODE' AND movie_id IS NULL)
                )
            );
            INSERT INTO watch_events_reconciled
                (id, media_kind, movie_id, episode_id, watched_at, created_at, title_snapshot,
                 year_snapshot, show_title_snapshot, season_number_snapshot, episode_number_snapshot,
                 artwork_reference)
            SELECT id, media_kind, movie_id, episode_id, watched_at, created_at, title_snapshot,
                   year_snapshot, show_title_snapshot, season_number_snapshot, episode_number_snapshot,
                   artwork_reference
              FROM watch_events;
            DROP TABLE watch_events;
            ALTER TABLE watch_events_reconciled RENAME TO watch_events;
            CREATE INDEX idx_watch_events_movie_watched ON watch_events(movie_id, julianday(watched_at), id);
            CREATE INDEX idx_watch_events_episode_watched ON watch_events(episode_id, julianday(watched_at), id);
            CREATE INDEX idx_watch_events_recent ON watch_events(julianday(watched_at) DESC, id DESC);
        "
    };

    public void Migrate(int targetVersion = LatestVersion)
    {
        if (targetVersion < 0 || targetVersion > MigrationScripts.Count)
            throw new ArgumentOutOfRangeException(nameof(targetVersion));
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        ExecutePragma(connection, "foreign_keys", "ON");
        ExecutePragma(connection, "journal_mode", "WAL");
        ExecutePragma(connection, "synchronous", "FULL");

        var currentVersion = ReadIntPragma(connection, "user_version");
        if (currentVersion > MigrationScripts.Count)
            throw new InvalidOperationException($"Database schema version {currentVersion} is newer than this build.");

        for (var i = currentVersion; i < targetVersion; i++)
        {
            var requiresForeignKeysOff = i is 4 or 7 or 8;
            if (requiresForeignKeysOff) ExecutePragma(connection, "foreign_keys", "OFF");
            try
            {
                using var tx = connection.BeginTransaction(deferred: false);
                using var cmd = connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = MigrationScripts[i];
                cmd.ExecuteNonQuery();
                cmd.CommandText = $"PRAGMA user_version = {i + 1};";
                cmd.ExecuteNonQuery();
                cmd.CommandText = "PRAGMA foreign_key_check;";
                using (var violations = cmd.ExecuteReader())
                {
                    if (violations.Read())
                        throw new SqliteException($"Migration {i + 1} introduced a foreign-key violation.", 19);
                }
                tx.Commit();
            }
            finally
            {
                if (requiresForeignKeysOff)
                {
                    ExecutePragma(connection, "foreign_keys", "ON");
                    if (ReadIntPragma(connection, "foreign_keys") != 1)
                        throw new InvalidOperationException("Foreign-key enforcement was not restored after migration.");
                }
            }
        }

        if (targetVersion == LatestVersion) ValidateSchema(connection);
    }

    private static void ValidateSchema(SqliteConnection connection)
    {
        var requiredTables = new[]
        {
            "movies", "media_files", "metadata_cache", "file_operations", "watched_folders",
            "settings", "movie_personal_state", "watch_events",
            "tv_shows", "tv_seasons", "tv_episodes", "episode_media_files"
        };
        var requiredIndexes = new[]
        {
            "idx_file_operations_state", "idx_file_operations_media_file_id", "idx_media_files_movie_id",
            "idx_movie_personal_state_preference", "idx_movie_personal_state_watchlist",
            "idx_watch_events_movie_watched", "idx_watch_events_episode_watched", "idx_watch_events_recent",
            "idx_tv_shows_sort_title", "idx_tv_seasons_show_id", "idx_tv_episodes_season_id",
            "idx_episode_media_files_episode_id"
        };
        foreach (var table in requiredTables) RequireSchemaObject(connection, "table", table);
        foreach (var index in requiredIndexes) RequireSchemaObject(connection, "index", index);
        // Column-level guard: user_version alone cannot detect a table left on an older shape by a
        // migration script that was edited after it had already been stamped (the drift that made
        // Mark Watched fail with 'no column named poster_reference'). Assert the columns the write
        // paths depend on actually exist so any future drift fails startup loudly instead of being
        // masked behind a generic runtime error dialog.
        RequireTableColumns(connection, "watch_events",
            "artwork_reference", "backdrop_reference", "poster_reference");
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_key_check;";
        using var reader = command.ExecuteReader();
        if (reader.Read()) throw new InvalidOperationException("Database schema contains foreign-key violations.");
    }

    private static void RequireSchemaObject(SqliteConnection connection, string type, string name)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = $type AND name = $name;";
        command.Parameters.AddWithValue("$type", type);
        command.Parameters.AddWithValue("$name", name);
        if (Convert.ToInt32(command.ExecuteScalar()) != 1)
            throw new InvalidOperationException($"Required SQLite {type} '{name}' is missing.");
    }

    private static void RequireTableColumns(SqliteConnection connection, string table, params string[] columns)
    {
        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = $"PRAGMA table_info({table});";
            using var reader = command.ExecuteReader();
            while (reader.Read()) present.Add(reader.GetString(1));
        }
        foreach (var column in columns)
            if (!present.Contains(column))
                throw new InvalidOperationException(
                    $"Required SQLite column '{table}.{column}' is missing (schema drift).");
    }

    private static int ReadIntPragma(SqliteConnection connection, string name)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA {name};";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static void ExecutePragma(SqliteConnection connection, string name, string value)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA {name} = {value};";
        command.ExecuteNonQuery();
    }
}
