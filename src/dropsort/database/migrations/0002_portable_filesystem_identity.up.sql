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
