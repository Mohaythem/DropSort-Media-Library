from __future__ import annotations

from datetime import datetime, timezone
from pathlib import Path

import pytest

from dropsort.application.errors import LibraryReconciliationCancelled, LibraryReconciliationError
from dropsort.application.use_cases.reconcile_library_files import (
    ReconciliationCancellation,
    ReconcileLibraryFiles,
)
from dropsort.database.repositories import MediaFileRepository
from dropsort.library.availability import (
    AvailabilityInspection,
    AvailabilityInspectionStatus,
    NoFollowMediaFileInspector,
)
from dropsort.library.movies import MediaFileStatus


NOW = datetime(2026, 8, 12, 12, 0, tzinfo=timezone.utc)


def _add(repository: MediaFileRepository, path: Path, size: int = 5) -> int:
    return repository.create(path.absolute(), size)


def _journal_count(harness) -> int:
    with harness.database.connection() as connection:
        return int(connection.execute("SELECT COUNT(*) FROM file_operations").fetchone()[0])


def test_reconciliation_marks_confirmed_missing_and_restores_present(
    harness,
    tmp_path: Path,
) -> None:
    repository = MediaFileRepository(harness.database)
    present = tmp_path / "present.mkv"
    returned = tmp_path / "returned.mkv"
    present.write_bytes(b"movie")
    returned.write_bytes(b"movie")
    present_id = _add(repository, present)
    missing_id = _add(repository, tmp_path / "missing.mkv")
    returned_id = _add(repository, returned)
    repository.mark_missing(returned_id)
    before = {path: path.read_bytes() for path in (present, returned)}
    progress = []

    result = ReconcileLibraryFiles(repository, NoFollowMediaFileInspector(), now=lambda: NOW).execute(
        progress=progress.append
    )

    assert result == progress[-1]
    assert (result.total, result.checked, result.present, result.missing, result.errors) == (3, 3, 2, 1, 0)
    assert repository.get_by_id(present_id).status is MediaFileStatus.PRESENT  # type: ignore[union-attr]
    assert repository.get_by_id(missing_id).status is MediaFileStatus.MISSING  # type: ignore[union-attr]
    assert repository.get_by_id(returned_id).status is MediaFileStatus.PRESENT  # type: ignore[union-attr]
    assert {path: path.read_bytes() for path in before} == before
    assert _journal_count(harness) == 0


def test_inspection_error_preserves_last_known_status(harness, tmp_path: Path) -> None:
    repository = MediaFileRepository(harness.database)
    media_id = _add(repository, tmp_path / "unavailable.mkv")

    class ErrorInspector:
        def inspect(self, path: Path) -> AvailabilityInspection:
            return AvailabilityInspection(path, AvailabilityInspectionStatus.ERROR, error_code="DENIED")

    result = ReconcileLibraryFiles(repository, ErrorInspector(), now=lambda: NOW).execute()

    assert result.errors == 1
    assert result.status_changes == 0
    assert repository.get_by_id(media_id).status is MediaFileStatus.PRESENT  # type: ignore[union-attr]


def test_cancellation_commits_only_completed_batches_and_restart_finishes(
    harness,
    tmp_path: Path,
) -> None:
    repository = MediaFileRepository(harness.database)
    for index in range(6):
        _add(repository, tmp_path / f"missing-{index}.mkv")
    cancellation = ReconciliationCancellation()
    values = []

    def on_progress(value) -> None:
        values.append(value)
        if value.checked == 2:
            cancellation.cancel()

    with pytest.raises(LibraryReconciliationCancelled) as caught:
        ReconcileLibraryFiles(
            repository,
            NoFollowMediaFileInspector(),
            now=lambda: NOW,
            batch_size=2,
            progress_interval=1,
        ).execute(progress=on_progress, cancellation=cancellation)

    assert caught.value.progress.checked == 2
    statuses = repository.list_cataloged(after_id=0, limit=10)
    assert [item.status for item in statuses[:2]] == [MediaFileStatus.MISSING] * 2
    assert [item.status for item in statuses[2:]] == [MediaFileStatus.PRESENT] * 4

    restarted = ReconcileLibraryFiles(repository, NoFollowMediaFileInspector(), now=lambda: NOW).execute()
    assert restarted.checked == 6
    assert all(item.status is MediaFileStatus.MISSING for item in repository.list_cataloged(after_id=0, limit=10))


def test_database_batch_failure_is_controlled_and_not_reported_as_success(
    harness,
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    repository = MediaFileRepository(harness.database)
    _add(repository, tmp_path / "missing.mkv")
    monkeypatch.setattr(repository, "apply_status_updates", lambda _updates: (_ for _ in ()).throw(RuntimeError("db failed")))

    with pytest.raises(LibraryReconciliationError, match="status updates"):
        ReconcileLibraryFiles(repository, NoFollowMediaFileInspector(), now=lambda: NOW).execute()

    assert repository.list_cataloged(after_id=0, limit=10)[0].status is MediaFileStatus.PRESENT


def test_empty_catalog_has_exact_zero_progress(harness) -> None:
    values = []
    result = ReconcileLibraryFiles(
        MediaFileRepository(harness.database),
        NoFollowMediaFileInspector(),
        now=lambda: NOW,
    ).execute(progress=values.append)

    assert result.total == result.checked == 0
    assert values == [result]


def test_reconciliation_validates_configuration_clock_and_pre_start_cancellation(
    harness,
) -> None:
    repository = MediaFileRepository(harness.database)
    for kwargs in ({"batch_size": 0}, {"progress_interval": True}):
        with pytest.raises(ValueError):
            ReconcileLibraryFiles(repository, NoFollowMediaFileInspector(), **kwargs)

    repository.create(Path.cwd() / "missing.mkv", 1)
    cancellation = ReconciliationCancellation()
    cancellation.cancel()
    with pytest.raises(LibraryReconciliationCancelled) as cancelled:
        ReconcileLibraryFiles(repository, NoFollowMediaFileInspector()).execute(
            cancellation=cancellation
        )
    assert cancelled.value.progress.checked == 0

    with pytest.raises(ValueError, match="timezone-aware"):
        ReconcileLibraryFiles(
            repository,
            NoFollowMediaFileInspector(),
            now=lambda: datetime(2026, 8, 12),
        ).execute()
