-- dropsort: foreign_keys_off

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
