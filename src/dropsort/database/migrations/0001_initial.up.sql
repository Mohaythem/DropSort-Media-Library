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
