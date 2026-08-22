from __future__ import annotations

from hashlib import sha256
from pathlib import Path
import sqlite3
import threading

import pytest

from dropsort.application.errors import CatalogClearBlockedError, CatalogClearError
from dropsort.application.use_cases.clear_library_data import ClearLibraryData
from dropsort.database.repositories.library_maintenance import (
    SqliteLibraryMaintenanceRepository,
)


class FakePosterCache:
    def __init__(self, *, removed: int = 2, error: Exception | None = None) -> None:
        self.removed = removed
        self.error = error
        self.calls = 0

    def clear(self) -> int:
        self.calls += 1
        if self.error is not None:
            raise self.error
        return self.removed


def _seed_catalog(harness, media_path: Path, *, operation_state: str = "COMMITTED") -> None:
    timestamp = "2026-08-15T12:00:00+00:00"
    with harness.database.transaction() as connection:
        movie_id = connection.execute(
            """
            INSERT INTO movies(provider, external_id, title, date_added, created_at, updated_at)
            VALUES ('tmdb', '1', 'Movie', ?, ?, ?)
            """,
            (timestamp, timestamp, timestamp),
        ).lastrowid
        media_id = connection.execute(
            """
            INSERT INTO media_files(
                movie_id, current_path, path_key, file_size, status, discovered_at, last_seen_at
            ) VALUES (?, ?, ?, ?, 'PRESENT', ?, ?)
            """,
            (movie_id, str(media_path), str(media_path).casefold(), media_path.stat().st_size, timestamp, timestamp),
        ).lastrowid
        connection.execute(
            """
            INSERT INTO metadata_cache(provider, cache_key, payload, fetched_at, expires_at)
            VALUES ('tmdb', 'search:movie', '{}', ?, ?)
            """,
            (timestamp, timestamp),
        )
        connection.execute(
            """
            INSERT INTO file_operations(
                id, operation_type, source_path, destination_path, state,
                media_file_id, created_at, updated_at
            ) VALUES ('history-1', 'MOVE', ?, ?, ?, ?, ?, ?)
            """,
            (
                str(media_path),
                str(media_path.with_name("destination.mkv")),
                operation_state,
                media_id,
                timestamp,
                timestamp,
            ),
        )


def _counts(harness) -> tuple[int, int, int, int]:
    with harness.database.connection() as connection:
        return tuple(
            int(connection.execute(f"SELECT COUNT(*) FROM {table}").fetchone()[0])
            for table in ("movies", "media_files", "metadata_cache", "file_operations")
        )  # type: ignore[return-value]


def test_clear_is_transactional_preserves_history_and_never_mutates_media(
    harness,
    tmp_path: Path,
) -> None:
    media = tmp_path / "Movie.mkv"
    media.write_bytes(b"immutable movie bytes")
    digest = sha256(media.read_bytes()).hexdigest()
    _seed_catalog(harness, media)
    cache = FakePosterCache()

    result = ClearLibraryData(
        SqliteLibraryMaintenanceRepository(harness.database),
        cache,
        execution_lock=threading.Lock(),
    ).execute()

    assert (result.movies_removed, result.media_files_removed, result.metadata_entries_removed) == (1, 1, 1)
    assert result.poster_files_removed == 2
    assert result.warning is None
    assert _counts(harness) == (0, 0, 0, 1)
    assert media.read_bytes() == b"immutable movie bytes"
    assert sha256(media.read_bytes()).hexdigest() == digest
    with harness.database.connection() as connection:
        history = connection.execute(
            "SELECT id, state, media_file_id, source_path FROM file_operations"
        ).fetchone()
    assert tuple(history) == ("history-1", "COMMITTED", None, str(media))


def test_clear_blocks_nonterminal_operation_without_deleting_anything(
    harness,
    tmp_path: Path,
) -> None:
    media = tmp_path / "Movie.mkv"
    media.write_bytes(b"movie")
    _seed_catalog(harness, media, operation_state="RECOVERY_REQUIRED")
    cache = FakePosterCache()

    with pytest.raises(CatalogClearBlockedError, match="operation"):
        ClearLibraryData(
            SqliteLibraryMaintenanceRepository(harness.database),
            cache,
            execution_lock=threading.Lock(),
        ).execute()

    assert _counts(harness) == (1, 1, 1, 1)
    assert cache.calls == 0
    assert media.read_bytes() == b"movie"


def test_clear_rolls_back_on_database_failure(harness, tmp_path: Path) -> None:
    media = tmp_path / "Movie.mkv"
    media.write_bytes(b"movie")
    _seed_catalog(harness, media)
    with harness.database.transaction() as connection:
        connection.execute(
            """
            CREATE TRIGGER block_movie_delete BEFORE DELETE ON movies
            BEGIN SELECT RAISE(ABORT, 'blocked'); END
            """
        )
    cache = FakePosterCache()

    with pytest.raises(CatalogClearError):
        ClearLibraryData(
            SqliteLibraryMaintenanceRepository(harness.database),
            cache,
            execution_lock=threading.Lock(),
        ).execute()

    assert _counts(harness) == (1, 1, 1, 1)
    assert cache.calls == 0


def test_clear_is_idempotent_and_cache_failure_is_a_post_commit_warning(
    harness,
) -> None:
    cache = FakePosterCache(error=OSError("cache unavailable"))
    use_case = ClearLibraryData(
        SqliteLibraryMaintenanceRepository(harness.database),
        cache,
        execution_lock=threading.Lock(),
    )

    first = use_case.execute()
    second = use_case.execute()

    assert first.movies_removed == second.movies_removed == 0
    assert first.warning == second.warning == "POSTER_CACHE_CLEANUP_FAILED"
    assert cache.calls == 2


def test_clear_refuses_while_shared_catalog_operation_lock_is_busy(harness) -> None:
    lock = threading.Lock()
    lock.acquire()
    try:
        with pytest.raises(CatalogClearBlockedError, match="busy"):
            ClearLibraryData(
                SqliteLibraryMaintenanceRepository(harness.database),
                FakePosterCache(),
                execution_lock=lock,
            ).execute()
    finally:
        lock.release()

