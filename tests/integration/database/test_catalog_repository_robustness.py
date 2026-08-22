from __future__ import annotations

from datetime import datetime, timezone
from pathlib import Path

import pytest

from dropsort.database.repositories import (
    MediaFileRepository,
    SqliteCatalogUnitOfWork,
    SqliteMovieRepository,
)
from dropsort.library.movies import (
    CatalogDataError,
    CatalogRecordNotFoundError,
    MediaFilePathConflictError,
    MovieCatalogData,
    VerifiedMediaFileFacts,
)


NOW = datetime(2026, 8, 11, 12, 0, tzinfo=timezone.utc)


def _movie_data() -> MovieCatalogData:
    return MovieCatalogData("tmdb", "1", "Movie", None, None, None, (), None, None, None)


def _facts(path: Path) -> VerifiedMediaFileFacts:
    return VerifiedMediaFileFacts(path.absolute(), 1, ".mkv", None, None, None, NOW)


def test_movie_repository_rejects_naive_clock_and_invalid_lookup_identity(harness) -> None:
    repository = SqliteMovieRepository(harness.database)

    with pytest.raises(ValueError, match="timezone-aware"):
        repository.create(_movie_data(), now=datetime(2026, 8, 11))
    with pytest.raises(ValueError, match="provider"):
        repository.get_by_external_id("", "1")
    with pytest.raises(ValueError, match="external_id"):
        repository.get_by_external_id("tmdb", "")


@pytest.mark.parametrize(
    ("genres", "date_added"),
    [("{}", NOW.isoformat()), ("[]", "not-a-timestamp")],
)
def test_movie_repository_rejects_structurally_corrupt_rows(
    harness,
    genres: str,
    date_added: str,
) -> None:
    with harness.database.transaction() as conn:
        cursor = conn.execute(
            """
            INSERT INTO movies(
                provider, external_id, title, genres, date_added, created_at, updated_at
            ) VALUES ('tmdb', ?, 'Movie', ?, ?, ?, ?)
            """,
            (genres, genres, date_added, NOW.isoformat(), NOW.isoformat()),
        )
        movie_id = int(cursor.lastrowid)

    with pytest.raises(CatalogDataError):
        SqliteMovieRepository(harness.database).get_by_id(movie_id)


def test_media_repository_rejects_corrupt_status_and_timestamp(harness, tmp_path: Path) -> None:
    path = (tmp_path / "Movie.mkv").absolute()
    with harness.database.transaction() as conn:
        cursor = conn.execute(
            """
            INSERT INTO media_files(
                current_path, path_key, file_size, status, discovered_at, last_seen_at
            ) VALUES (?, ?, 1, 'PRESENT', 'not-a-time', ?)
            """,
            (str(path), str(path).casefold(), NOW.isoformat()),
        )
        media_file_id = int(cursor.lastrowid)

    with pytest.raises(CatalogDataError):
        MediaFileRepository(harness.database).get_by_id(media_file_id)


def test_media_repository_missing_link_and_naive_present_are_controlled(
    harness,
) -> None:
    repository = MediaFileRepository(harness.database)

    with pytest.raises(CatalogRecordNotFoundError):
        repository.link_to_movie(999, 1)
    with pytest.raises(ValueError, match="timezone-aware"):
        repository.mark_present(999, observed_at=datetime(2026, 8, 11))


def test_refresh_detects_path_change_between_read_and_guarded_update(
    harness,
    tmp_path: Path,
    monkeypatch,
) -> None:
    movie = SqliteMovieRepository(harness.database).create(_movie_data(), now=NOW)
    repository = MediaFileRepository(harness.database)
    facts = _facts(tmp_path / "Movie.mkv")
    media_file = repository.add(facts, movie.id)
    original_execute = repository._execute_rowcount

    def simulate_concurrent_change(sql: str, values: tuple[object, ...]) -> int:
        if "path_key = ?" in sql:
            return 0
        return original_execute(sql, values)

    monkeypatch.setattr(repository, "_execute_rowcount", simulate_concurrent_change)

    with pytest.raises(MediaFilePathConflictError, match="changed"):
        repository.refresh_verified_facts(media_file.id, facts)


def test_legacy_connection_overrides_and_missing_paths_remain_supported(
    harness,
    tmp_path: Path,
) -> None:
    repository = MediaFileRepository(harness.database)
    path = (tmp_path / "Legacy.mkv").absolute()
    with harness.database.transaction() as conn:
        media_file_id = repository.create(path, 1, conn=conn)
        assert repository.get_path(media_file_id, conn=conn) == path

    with pytest.raises(KeyError):
        repository.get_path(999)
    with harness.database.transaction() as conn:
        with pytest.raises(KeyError):
            repository.update_path(999, path, conn=conn)


def test_catalog_unit_of_work_rejects_invalid_lifecycle(harness) -> None:
    unit_of_work = SqliteCatalogUnitOfWork(harness.database)

    with pytest.raises(RuntimeError, match="not active"):
        unit_of_work.__exit__(None, None, None)
    with unit_of_work:
        with pytest.raises(RuntimeError, match="already active"):
            unit_of_work.__enter__()
