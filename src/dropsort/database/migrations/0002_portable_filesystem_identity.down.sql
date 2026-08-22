PRAGMA defer_foreign_keys = ON;

CREATE TEMP TABLE filesystem_identity_downgrade_guard (
    values_fit_signed_sqlite_integer INTEGER NOT NULL
        CHECK(values_fit_signed_sqlite_integer = 1)
);

INSERT INTO filesystem_identity_downgrade_guard(values_fit_signed_sqlite_integer)
SELECT CASE WHEN EXISTS (
    SELECT 1
      FROM file_operations
     WHERE (source_dev IS NOT NULL AND CAST(CAST(source_dev AS INTEGER) AS TEXT) != source_dev)
        OR (source_ino IS NOT NULL AND CAST(CAST(source_ino AS INTEGER) AS TEXT) != source_ino)
        OR (destination_dev IS NOT NULL AND CAST(CAST(destination_dev AS INTEGER) AS TEXT) != destination_dev)
        OR (destination_ino IS NOT NULL AND CAST(CAST(destination_ino AS INTEGER) AS TEXT) != destination_ino)
) THEN 0 ELSE 1 END;

CREATE TABLE file_operations_integer (
    id TEXT PRIMARY KEY,
    operation_type TEXT NOT NULL CHECK(operation_type IN ('MOVE', 'RENAME')),
    source_path TEXT NOT NULL,
    destination_path TEXT NOT NULL,
    state TEXT NOT NULL CHECK(state IN (
        'PLANNED', 'VALIDATED', 'EXECUTING', 'FS_VERIFIED',
        'COMMITTED', 'FAILED', 'RECOVERY_REQUIRED'
    )),
    media_file_id INTEGER REFERENCES media_files(id) ON DELETE SET NULL,
    reverses_operation_id TEXT REFERENCES file_operations_integer(id) ON DELETE SET NULL,
    source_size INTEGER,
    source_mtime_ns INTEGER,
    source_dev INTEGER,
    source_ino INTEGER,
    destination_size INTEGER,
    destination_mtime_ns INTEGER,
    destination_dev INTEGER,
    destination_ino INTEGER,
    destination_sha256 TEXT,
    strategy TEXT,
    error_code TEXT,
    error_message TEXT,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);

INSERT INTO file_operations_integer (
    id, operation_type, source_path, destination_path, state,
    media_file_id, reverses_operation_id,
    source_size, source_mtime_ns, source_dev, source_ino,
    destination_size, destination_mtime_ns, destination_dev, destination_ino,
    destination_sha256, strategy, error_code, error_message, created_at, updated_at
)
SELECT
    id, operation_type, source_path, destination_path, state,
    media_file_id, reverses_operation_id,
    source_size, source_mtime_ns, CAST(source_dev AS INTEGER), CAST(source_ino AS INTEGER),
    destination_size, destination_mtime_ns,
    CAST(destination_dev AS INTEGER), CAST(destination_ino AS INTEGER),
    destination_sha256, strategy, error_code, error_message, created_at, updated_at
FROM file_operations;

DROP TABLE file_operations;
ALTER TABLE file_operations_integer RENAME TO file_operations;

CREATE INDEX idx_file_operations_state ON file_operations(state);
CREATE INDEX idx_file_operations_media_file_id ON file_operations(media_file_id);

DROP TABLE filesystem_identity_downgrade_guard;
