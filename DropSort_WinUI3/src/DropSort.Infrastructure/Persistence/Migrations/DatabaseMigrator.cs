using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace DropSort.Infrastructure.Persistence.Migrations;

public class DatabaseMigrator
{
    public const int LatestVersion = 5;
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
            var requiresForeignKeysOff = i == 4;
            if (requiresForeignKeysOff) ExecutePragma(connection, "foreign_keys", "OFF");
            try
            {
                using var tx = connection.BeginTransaction();
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
            "settings", "movie_personal_state", "watch_events"
        };
        var requiredIndexes = new[]
        {
            "idx_file_operations_state", "idx_file_operations_media_file_id", "idx_media_files_movie_id",
            "idx_movie_personal_state_preference", "idx_movie_personal_state_watchlist",
            "idx_watch_events_movie_watched"
        };
        foreach (var table in requiredTables) RequireSchemaObject(connection, "table", table);
        foreach (var index in requiredIndexes) RequireSchemaObject(connection, "index", index);
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
