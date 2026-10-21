using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace DropSort.Infrastructure.Persistence.Migrations;

public class DatabaseMigrator
{
    public const int LatestVersion = 18;
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
        // Rebuild both shapes, preserving artwork columns when present and using NULL only when a
        // historical v8 database lacks those columns. GetMigrationScript resolves the projection.
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
                 artwork_reference, backdrop_reference, poster_reference)
            SELECT id, media_kind, movie_id, episode_id, watched_at, created_at, title_snapshot,
                   year_snapshot, show_title_snapshot, season_number_snapshot, episode_number_snapshot,
                   artwork_reference, __V8_BACKDROP__, __V8_POSTER__
              FROM watch_events;
            DROP TABLE watch_events;
            ALTER TABLE watch_events_reconciled RENAME TO watch_events;
            CREATE INDEX idx_watch_events_movie_watched ON watch_events(movie_id, julianday(watched_at), id);
            CREATE INDEX idx_watch_events_episode_watched ON watch_events(episode_id, julianday(watched_at), id);
            CREATE INDEX idx_watch_events_recent ON watch_events(julianday(watched_at) DESC, id DESC);
        ",
        // 0010_tv_show_import_aliases
        // Stable local TV import identity. Episode import resolves a show by its filename-derived
        // sort_title; metadata matching later overwrites title/sort_title with the provider's canonical
        // values, so a later episode arriving under the original filename alias could no longer find the
        // show and created a duplicate (and could then collide on tv_shows UNIQUE(provider, external_id)
        // when matched). This table keeps the immutable local import aliases independent of display
        // metadata. Existing shows are backfilled with their current sort_title so present resolvability
        // survives the next metadata change; aliases already lost to an earlier overwrite cannot be
        // recovered but no future import will split a show again.
        @"
            CREATE TABLE tv_show_aliases (
                id INTEGER PRIMARY KEY,
                show_id INTEGER NOT NULL REFERENCES tv_shows(id) ON DELETE CASCADE,
                alias_sort_title TEXT NOT NULL,
                created_at TEXT NOT NULL,
                UNIQUE(alias_sort_title)
            );
            CREATE INDEX idx_tv_show_aliases_show_id ON tv_show_aliases(show_id);
            INSERT INTO tv_show_aliases (show_id, alias_sort_title, created_at)
            SELECT id, sort_title, created_at FROM tv_shows;
        ",
        // 0011_custom_list_membership
        // Makes the already-exposed custom list real: a movie can belong to a custom list. The list is
        // identified by its stable slot key (the settings-stored custom list), not a catalog row, so
        // renaming the list keeps its membership. A movie deleted from the catalog cascades out of every
        // list; deleting a custom list removes only its rows here. Watchlist and Favorites are unchanged
        // (they remain movie_personal_state).
        @"
            CREATE TABLE custom_list_movies (
                id INTEGER PRIMARY KEY,
                list_key TEXT NOT NULL,
                movie_id INTEGER NOT NULL REFERENCES movies(id) ON DELETE CASCADE,
                added_at TEXT NOT NULL,
                UNIQUE(list_key, movie_id)
            );
            CREATE INDEX idx_custom_list_movies_list ON custom_list_movies(list_key);
        ",
        // 0012_custom_list_definitions
        // Promote custom lists from a single fixed settings slot to real, independently identified lists.
        // Before this migration a custom list was only a display name in the UI settings row
        // 'ui_custom_lists', and all membership shared one fixed key ('thrillers') in custom_list_movies,
        // so only one list could ever hold movies. This migration makes each list a row in custom_lists
        // with a stable id, and rekeys membership on that id in custom_list_members. The list name becomes
        // display-only (a rename never changes identity or loses membership), and a movie can belong to
        // any number of lists.
        //
        // Data preservation runs inside the same transaction as the backup-protected forward migration:
        //   1. Every name stored in 'ui_custom_lists' (a JSON array in the settings table, which lives in
        //      this same database) becomes one custom_lists row, in array order. Malformed settings JSON is
        //      ignored (json_valid guard) so a bad settings value degrades to "no import" instead of failing
        //      the migration.
        //   2. If the previous single-slot membership had rows but no name was stored, one 'My List' row is
        //      created so those movies are not lost.
        //   3. The old fixed-slot ('thrillers') membership is moved onto the first list, preserving added_at.
        // The obsolete custom_list_movies table is then dropped. The settings row is left untouched; the UI
        // no longer reads it.
        @"
            PRAGMA defer_foreign_keys = ON;

            CREATE TABLE custom_lists (
                id INTEGER PRIMARY KEY,
                display_name TEXT NOT NULL,
                created_at TEXT NOT NULL,
                CHECK (length(trim(display_name)) > 0)
            );
            CREATE INDEX idx_custom_lists_created ON custom_lists(created_at, id);

            CREATE TABLE custom_list_members (
                id INTEGER PRIMARY KEY,
                list_id INTEGER NOT NULL REFERENCES custom_lists(id) ON DELETE CASCADE,
                movie_id INTEGER NOT NULL REFERENCES movies(id) ON DELETE CASCADE,
                added_at TEXT NOT NULL,
                UNIQUE(list_id, movie_id)
            );
            CREATE INDEX idx_custom_list_members_list ON custom_list_members(list_id);
            CREATE INDEX idx_custom_list_members_movie ON custom_list_members(movie_id);

            INSERT INTO custom_lists (display_name, created_at)
            SELECT trim(je.value), strftime('%Y-%m-%dT%H:%M:%fZ','now')
              FROM settings s, json_each(s.value) je
             WHERE s.key = 'ui_custom_lists'
               AND json_valid(s.value)
               AND json_type(s.value) = 'array'
               AND je.type = 'text'
               AND length(trim(je.value)) > 0
             ORDER BY je.id;

            INSERT INTO custom_lists (display_name, created_at)
            SELECT 'My List', strftime('%Y-%m-%dT%H:%M:%fZ','now')
             WHERE EXISTS (SELECT 1 FROM custom_list_movies)
               AND NOT EXISTS (SELECT 1 FROM custom_lists);

            INSERT INTO custom_list_members (list_id, movie_id, added_at)
            SELECT (SELECT id FROM custom_lists ORDER BY id LIMIT 1), clm.movie_id, clm.added_at
              FROM custom_list_movies clm
             WHERE EXISTS (SELECT 1 FROM custom_lists);

            DROP TABLE custom_list_movies;
        ",
        // 0013_custom_list_mixed_media
        // Lets a custom list hold TV shows as well as movies. v12's custom_list_members kept a NOT NULL
        // movie_id; this rebuilds it so a member is a movie OR a show, tagged by media_kind, with the two
        // foreign keys mutually exclusive (a CHECK enforces exactly one). Existing v12 movie memberships
        // are copied forward unchanged as MOVIE rows, preserving their ids and added_at. Per-kind
        // uniqueness is enforced by two partial unique indexes (a plain UNIQUE over nullable columns would
        // treat NULLs as distinct and let a show be added twice). Deleting a movie or a show still cascades
        // its membership rows; deleting a list still cascades all of its rows.
        @"
            PRAGMA defer_foreign_keys = ON;

            CREATE TABLE custom_list_members_mixed (
                id INTEGER PRIMARY KEY,
                list_id INTEGER NOT NULL REFERENCES custom_lists(id) ON DELETE CASCADE,
                media_kind TEXT NOT NULL CHECK(media_kind IN ('MOVIE', 'SHOW')),
                movie_id INTEGER REFERENCES movies(id) ON DELETE CASCADE,
                show_id INTEGER REFERENCES tv_shows(id) ON DELETE CASCADE,
                added_at TEXT NOT NULL,
                CHECK (
                    (media_kind = 'MOVIE' AND movie_id IS NOT NULL AND show_id IS NULL)
                    OR (media_kind = 'SHOW' AND show_id IS NOT NULL AND movie_id IS NULL)
                )
            );

            INSERT INTO custom_list_members_mixed (id, list_id, media_kind, movie_id, show_id, added_at)
            SELECT id, list_id, 'MOVIE', movie_id, NULL, added_at FROM custom_list_members;

            DROP TABLE custom_list_members;
            ALTER TABLE custom_list_members_mixed RENAME TO custom_list_members;

            CREATE INDEX idx_custom_list_members_list ON custom_list_members(list_id);
            CREATE INDEX idx_custom_list_members_movie ON custom_list_members(movie_id);
            CREATE INDEX idx_custom_list_members_show ON custom_list_members(show_id);
            CREATE UNIQUE INDEX ux_custom_list_members_movie
                ON custom_list_members(list_id, movie_id) WHERE movie_id IS NOT NULL;
            CREATE UNIQUE INDEX ux_custom_list_members_show
                ON custom_list_members(list_id, show_id) WHERE show_id IS NOT NULL;
        ",
        // v14: a persisted "review match recommended" flag. A movie can carry confident metadata yet still
        // want a human glance (e.g. it was auto-picked from a close popularity tie-break). Additive column,
        // default 0; no table rebuild, so the metadata_status CHECK is untouched.
        @"
            ALTER TABLE movies ADD COLUMN review_match INTEGER NOT NULL DEFAULT 0;
        ",
        // v15: persist the last time a movie or show was opened in the UI. Additive nullable columns;
        // existing date-added and metadata timestamps are unchanged.
        @"
            ALTER TABLE movies ADD COLUMN last_opened_at TEXT NULL;
            ALTER TABLE tv_shows ADD COLUMN last_opened_at TEXT NULL;
        ",
        // v16: one current viewing row per logical movie or episode. Historical watch_events remain append-only.
        @"
            CREATE TABLE active_viewings (
                id INTEGER PRIMARY KEY,
                media_kind TEXT NOT NULL CHECK(media_kind IN ('MOVIE', 'EPISODE')),
                movie_id INTEGER REFERENCES movies(id) ON DELETE CASCADE,
                episode_id INTEGER REFERENCES tv_episodes(id) ON DELETE CASCADE,
                media_file_id INTEGER REFERENCES media_files(id) ON DELETE SET NULL,
                title_snapshot TEXT NOT NULL,
                year_snapshot INTEGER,
                season_number_snapshot INTEGER,
                episode_number_snapshot INTEGER,
                episode_title_snapshot TEXT,
                path TEXT NOT NULL,
                started_at TEXT NOT NULL,
                artwork_reference TEXT,
                backdrop_reference TEXT,
                poster_reference TEXT,
                CHECK ((media_kind = 'MOVIE' AND movie_id IS NOT NULL AND episode_id IS NULL)
                    OR (media_kind = 'EPISODE' AND episode_id IS NOT NULL AND movie_id IS NULL))
            );
            CREATE UNIQUE INDEX ux_active_viewings_movie ON active_viewings(movie_id) WHERE movie_id IS NOT NULL;
            CREATE UNIQUE INDEX ux_active_viewings_episode ON active_viewings(episode_id) WHERE episode_id IS NOT NULL;
            CREATE INDEX idx_active_viewings_started ON active_viewings(julianday(started_at) DESC, id DESC);
        ",
        // v17: persist independent TV-show Favorites and Watch Later membership.
        @"
            CREATE TABLE tv_show_personal_state (
                show_id INTEGER PRIMARY KEY REFERENCES tv_shows(id) ON DELETE CASCADE,
                is_favorite INTEGER NOT NULL DEFAULT 0 CHECK(is_favorite IN (0, 1)),
                watch_later_added_at TEXT,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            CREATE INDEX idx_tv_show_personal_state_favorite ON tv_show_personal_state(is_favorite);
            CREATE INDEX idx_tv_show_personal_state_watch_later ON tv_show_personal_state(watch_later_added_at);
        ",
        // v18: account-scoped complete Trakt resources. No local catalog or watch-state relations.
        @"
            CREATE TABLE trakt_resource_cache (
                account_id TEXT NOT NULL,
                resource_key TEXT NOT NULL,
                payload_json TEXT,
                succeeded_at TEXT,
                attempted_at TEXT NOT NULL,
                error_code TEXT,
                PRIMARY KEY (account_id, resource_key),
                CHECK (length(trim(account_id)) > 0),
                CHECK (length(trim(resource_key)) > 0),
                CHECK (length(trim(attempted_at)) > 0),
                CHECK (payload_json IS NULL OR json_valid(payload_json) = 1)
            );
            CREATE INDEX idx_trakt_resource_cache_account ON trakt_resource_cache(account_id);
        "
    };

    public void Migrate(int targetVersion = LatestVersion)
    {
        if (targetVersion < 0 || targetVersion > MigrationScripts.Count)
            throw new ArgumentOutOfRangeException(nameof(targetVersion));

        using var writeGate = DatabaseWriteGate.Enter(_connectionString);
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        // Read and reject the version before changing any connection or database pragmas. A version-0
        // database with user tables is an unknown schema, not a new DropSort database; adopting it would
        // make an unsupported database look successfully migrated.
        var currentVersion = ReadIntPragma(connection, "user_version");
        if (currentVersion < 0)
            throw new InvalidOperationException($"Database schema version {currentVersion} is invalid.");
        if (currentVersion > MigrationScripts.Count)
            throw new InvalidOperationException($"Database schema version {currentVersion} is newer than this build.");
        if (currentVersion == 0 && HasUserSchemaObjects(connection))
            throw new InvalidOperationException("A non-empty database with schema version 0 is not a supported DropSort schema.");

        EnsureForeignKeyIntegrity(connection, null, "Existing database");
        ExecutePragma(connection, "foreign_keys", "ON");
        ExecutePragma(connection, "journal_mode", "WAL");
        ExecutePragma(connection, "synchronous", "FULL");

        // Protect an existing database before a forward migration. A brand-new database (version 0) has
        // no data to protect, so no backup is taken. If the required backup cannot be created or
        // verified, this throws and no migration runs: the database is left untouched (fail closed).
        string? verifiedBackupPath = null;
        if (currentVersion > 0 && currentVersion < targetVersion)
        {
            verifiedBackupPath = DatabaseSafetyBackup.CreateVerifiedBackup(
                connection, _connectionString, currentVersion, $"migrate-to-v{targetVersion}",
                pruneOldBackups: false);
        }

        for (var i = currentVersion; i < targetVersion; i++)
        {
            var requiresForeignKeysOff = i is 4 or 7 or 8;
            if (requiresForeignKeysOff) ExecutePragma(connection, "foreign_keys", "OFF");
            try
            {
                using var tx = connection.BeginTransaction(deferred: false);
                using var cmd = connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = GetMigrationScript(i, connection, tx);
                cmd.ExecuteNonQuery();

                // Validate the complete current schema while the final migration is still rollbackable.
                // This prevents a bad final schema from being stamped as successfully migrated.
                // Keep the v14 validation gate before its commit as well as validating v15.
                // Adding a migration must not weaken the prior rollback boundary on a corrupt schema.
                if (i + 1 >= 14 && targetVersion == LatestVersion)
                    ValidateSchema(connection, tx, includeActivity: i + 1 >= 15,
                        includeViewing: i + 1 >= 16, includeShowPersonal: i + 1 >= 17,
                        includeTrakt: i + 1 >= 18);

                cmd.CommandText = $"PRAGMA user_version = {i + 1};";
                cmd.ExecuteNonQuery();
                EnsureForeignKeyIntegrity(connection, tx, $"Migration {i + 1}");
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
        if (verifiedBackupPath is not null)
            DatabaseSafetyBackup.PruneAfterSuccessfulMigration(verifiedBackupPath);
    }

    private static void ValidateSchema(SqliteConnection connection, SqliteTransaction? transaction = null,
        bool includeActivity = true, bool includeViewing = true, bool includeShowPersonal = true,
        bool includeTrakt = true)
    {
        var requiredTables = new[]
        {
            "movies", "media_files", "metadata_cache", "file_operations", "watched_folders",
            "settings", "movie_personal_state", "watch_events",
            "tv_shows", "tv_seasons", "tv_episodes", "episode_media_files", "tv_show_aliases",
            "custom_lists", "custom_list_members"
        };
        var requiredIndexes = new[]
        {
            "idx_file_operations_state", "idx_file_operations_media_file_id", "idx_media_files_movie_id",
            "idx_movie_personal_state_preference", "idx_movie_personal_state_watchlist",
            "idx_watch_events_movie_watched", "idx_watch_events_episode_watched", "idx_watch_events_recent",
            "idx_tv_shows_sort_title", "idx_tv_seasons_show_id", "idx_tv_episodes_season_id",
            "idx_episode_media_files_episode_id", "idx_tv_show_aliases_show_id",
            "idx_custom_lists_created", "idx_custom_list_members_list", "idx_custom_list_members_movie",
            "idx_custom_list_members_show", "ux_custom_list_members_movie", "ux_custom_list_members_show"
        };
        foreach (var table in requiredTables) RequireSchemaObject(connection, "table", table, transaction);
        foreach (var index in requiredIndexes) RequireSchemaObject(connection, "index", index, transaction);
        if (includeViewing)
        {
            RequireSchemaObject(connection, "table", "active_viewings", transaction);
            foreach (var index in new[] { "ux_active_viewings_movie", "ux_active_viewings_episode", "idx_active_viewings_started" })
                RequireSchemaObject(connection, "index", index, transaction);
            RequireTableColumns(connection, "active_viewings", transaction,
                "media_kind", "movie_id", "episode_id", "media_file_id", "path", "started_at");
        }
        if (includeShowPersonal)
        {
            RequireSchemaObject(connection, "table", "tv_show_personal_state", transaction);
            foreach (var index in new[] { "idx_tv_show_personal_state_favorite", "idx_tv_show_personal_state_watch_later" })
                RequireSchemaObject(connection, "index", index, transaction);
            RequireTableColumns(connection, "tv_show_personal_state", transaction,
                "show_id", "is_favorite", "watch_later_added_at", "created_at", "updated_at");
        }
        if (includeTrakt)
        {
            RequireSchemaObject(connection, "table", "trakt_resource_cache", transaction);
            RequireSchemaObject(connection, "index", "idx_trakt_resource_cache_account", transaction);
            RequireTableColumns(connection, "trakt_resource_cache", transaction,
                "account_id", "resource_key", "payload_json", "succeeded_at", "attempted_at", "error_code");
        }
        // Column-level guard: user_version alone cannot detect a table left on an older shape by a
        // migration script that was edited after it had already been stamped (the drift that made
        // Mark Watched fail with 'no column named poster_reference'). Assert the columns the write
        // paths depend on actually exist so any future drift fails startup loudly instead of being
        // masked behind a generic runtime error dialog.
        RequireTableColumns(connection, "watch_events", transaction,
            "artwork_reference", "backdrop_reference", "poster_reference");
        RequireTableColumns(connection, "movies", transaction, "review_match");
        if (includeActivity)
        {
            RequireTableColumns(connection, "movies", transaction, "last_opened_at");
            RequireTableColumns(connection, "tv_shows", transaction, "last_opened_at");
        }
        EnsureForeignKeyIntegrity(connection, transaction, "Database schema");
    }

    private static void RequireSchemaObject(
        SqliteConnection connection, string type, string name, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = $type AND name = $name;";
        command.Parameters.AddWithValue("$type", type);
        command.Parameters.AddWithValue("$name", name);
        if (Convert.ToInt32(command.ExecuteScalar()) != 1)
            throw new InvalidOperationException($"Required SQLite {type} '{name}' is missing.");
    }

    private static void RequireTableColumns(
        SqliteConnection connection, string table, SqliteTransaction? transaction, params string[] columns)
    {
        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
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

    private static bool HasUserSchemaObjects(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS (SELECT 1 FROM sqlite_master WHERE type IN ('table', 'index', 'trigger', 'view') AND name NOT GLOB 'sqlite_*');";
        return Convert.ToInt32(command.ExecuteScalar()) == 1;
    }

    private static void EnsureForeignKeyIntegrity(
        SqliteConnection connection, SqliteTransaction? transaction, string context)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA foreign_key_check;";
        using var reader = command.ExecuteReader();
        if (reader.Read())
            throw new InvalidOperationException($"{context} contains foreign-key violations.");
    }

    private static string GetMigrationScript(int index, SqliteConnection connection, SqliteTransaction transaction)
    {
        if (index != 8) return MigrationScripts[index];

        // Historical v8 databases have two shapes. v9 must carry forward artwork values when those columns
        // are present, while still accepting the legacy shape where they were absent.
        var backdrop = HasTableColumn(connection, "watch_events", "backdrop_reference", transaction)
            ? "backdrop_reference" : "NULL";
        var poster = HasTableColumn(connection, "watch_events", "poster_reference", transaction)
            ? "poster_reference" : "NULL";
        return MigrationScripts[index]
            .Replace("__V8_BACKDROP__", backdrop)
            .Replace("__V8_POSTER__", poster);
    }

    private static bool HasTableColumn(
        SqliteConnection connection, string table, string column, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"PRAGMA table_info({table});";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static void ExecutePragma(SqliteConnection connection, string name, string value)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA {name} = {value};";
        command.ExecuteNonQuery();
    }
}
