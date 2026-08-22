from __future__ import annotations

from datetime import datetime, timezone
from pathlib import Path

import pytest

from dropsort.database.repositories import MediaFileRepository, SqliteMovieRepository
from dropsort.library.movies import (
    MediaFilePathConflictError,
    MediaFileStatus,
    MediaFileStatusUpdate,
    MovieCatalogData,
    VerifiedMediaFileFacts,
)


NOW = datetime(2026, 8, 12, 10, 0, tzinfo=timezone.utc)


def _movie(harness, external_id: str = "1") -> int:
    return SqliteMovieRepository(harness.database).create(
        MovieCatalogData("tmdb", external_id, "Movie", None, 2024, None, (), None, None, None),
        now=NOW,
    ).id


def _add(repository: MediaFileRepository, path: Path, movie_id: int, size: int = 5):
    return repository.add(
        VerifiedMediaFileFacts(path.absolute(), size, ".mkv", "1080p", "x264", "BluRay", NOW),
        movie_id,
    )


def test_catalog_enumeration_is_counted_paged_and_deterministic(harness, tmp_path: Path) -> None:
    repository = MediaFileRepository(harness.database)
    movie_id = _movie(harness)
    rows = tuple(_add(repository, tmp_path / f"{index}.mkv", movie_id) for index in range(5))

    assert repository.count_cataloged() == 5
    assert repository.list_cataloged(after_id=0, limit=2) == rows[:2]
    assert repository.list_cataloged(after_id=rows[1].id, limit=10) == rows[2:]


def test_status_batch_uses_path_compare_and_swap_and_skips_stale_rows(harness, tmp_path: Path) -> None:
    repository = MediaFileRepository(harness.database)
    movie_id = _movie(harness)
    first = _add(repository, tmp_path / "One.mkv", movie_id)
    second = _add(repository, tmp_path / "Two.mkv", movie_id)

    applied = repository.apply_status_updates(
        (
            MediaFileStatusUpdate(first.id, first.current_path, MediaFileStatus.MISSING, NOW),
            MediaFileStatusUpdate(second.id, tmp_path / "stale.mkv", MediaFileStatus.MISSING, NOW),
        )
    )

    assert applied == 1
    assert repository.get_by_id(first.id).status is MediaFileStatus.MISSING  # type: ignore[union-attr]
    assert repository.get_by_id(second.id).status is MediaFileStatus.PRESENT  # type: ignore[union-attr]


def test_stale_reconciliation_update_after_relink_is_ignored(
    harness,
    tmp_path: Path,
) -> None:
    repository = MediaFileRepository(harness.database)
    movie_id = _movie(harness)
    old_path = (tmp_path / "old.mkv").absolute()
    new_path = (tmp_path / "new.mkv").absolute()
    row = _add(repository, old_path, movie_id)
    repository.mark_missing(row.id)
    stale_update = MediaFileStatusUpdate(
        row.id,
        old_path,
        MediaFileStatus.MISSING,
        NOW,
    )

    repository.relink(
        row.id,
        expected_path=old_path,
        new_path=new_path,
        observed_at=NOW,
    )
    applied = repository.apply_status_updates((stale_update,))

    current = repository.get_by_id(row.id)
    assert applied == 0
    assert current is not None
    assert current.current_path == new_path
    assert current.status is MediaFileStatus.PRESENT


def test_transactional_relink_preserves_row_association_and_technical_facts(
    harness,
    tmp_path: Path,
) -> None:
    repository = MediaFileRepository(harness.database)
    movie_id = _movie(harness)
    old_path = (tmp_path / "old.mkv").absolute()
    new_path = (tmp_path / "new.mkv").absolute()
    row = _add(repository, old_path, movie_id)
    repository.mark_missing(row.id)

    relinked = repository.relink(
        row.id,
        expected_path=old_path,
        new_path=new_path,
        observed_at=NOW,
    )

    assert relinked.id == row.id
    assert relinked.movie_id == movie_id
    assert relinked.current_path == new_path
    assert relinked.status is MediaFileStatus.PRESENT
    assert (relinked.file_size, relinked.resolution, relinked.codec, relinked.source) == (
        row.file_size,
        row.resolution,
        row.codec,
        row.source,
    )


def test_relink_blocks_casefold_catalog_owner_and_stale_old_path(harness, tmp_path: Path) -> None:
    repository = MediaFileRepository(harness.database)
    movie_id = _movie(harness)
    missing = _add(repository, tmp_path / "missing.mkv", movie_id)
    owned = _add(repository, tmp_path / "Owned.MKV", movie_id)
    repository.mark_missing(missing.id)

    with pytest.raises(MediaFilePathConflictError):
        repository.relink(
            missing.id,
            expected_path=missing.current_path,
            new_path=owned.current_path.with_name("owned.mkv"),
            observed_at=NOW,
        )
    with pytest.raises(MediaFilePathConflictError):
        repository.relink(
            missing.id,
            expected_path=tmp_path / "stale.mkv",
            new_path=tmp_path / "new.mkv",
            observed_at=NOW,
        )
