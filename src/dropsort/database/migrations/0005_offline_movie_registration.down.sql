-- dropsort: foreign_keys_off

CREATE TEMP TABLE offline_registration_downgrade_guard (
    safe_to_remove_offline_registration INTEGER NOT NULL
        CHECK(safe_to_remove_offline_registration = 1)
);

INSERT INTO offline_registration_downgrade_guard(safe_to_remove_offline_registration)
SELECT CASE WHEN EXISTS (
    SELECT 1
      FROM movies
     WHERE provider IS NULL
        OR external_id IS NULL
        OR metadata_status != 'READY'
) THEN 0 ELSE 1 END;

CREATE TABLE movies_before_offline_registration (
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
    genres TEXT NOT NULL DEFAULT '[]',
    UNIQUE(provider, external_id)
);

INSERT INTO movies_before_offline_registration (
    id, provider, external_id, title, original_title, year, overview,
    runtime_minutes, rating, poster_path, date_added, created_at, updated_at, genres
)
SELECT
    id, provider, external_id, title, original_title, year, overview,
    runtime_minutes, rating, poster_path, date_added, created_at, updated_at, genres
FROM movies;

DROP TABLE movies;
ALTER TABLE movies_before_offline_registration RENAME TO movies;
DROP TABLE offline_registration_downgrade_guard;
