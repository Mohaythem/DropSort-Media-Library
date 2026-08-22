from __future__ import annotations

from datetime import datetime, timezone
from pathlib import Path

from dropsort.application.use_cases.reconcile_library_files import ReconcileLibraryFiles
from dropsort.database.repositories import MediaFileRepository
from dropsort.library.availability import (
    AvailabilityInspection,
    AvailabilityInspectionStatus,
    MediaFileIdentity,
)
from dropsort.library.movies import MediaFileStatus


NOW = datetime(2026, 8, 12, tzinfo=timezone.utc)


class SyntheticInspector:
    def inspect(self, path: Path) -> AvailabilityInspection:
        index = int(path.stem.split("-")[-1])
        if index < 800:
            return AvailabilityInspection(
                path,
                AvailabilityInspectionStatus.PRESENT,
                MediaFileIdentity(1, index, index, 1, index + 1),
            )
        if index < 950:
            return AvailabilityInspection(path, AvailabilityInspectionStatus.MISSING)
        return AvailabilityInspection(
            path,
            AvailabilityInspectionStatus.ERROR,
            error_code="SYNTHETIC_UNAVAILABLE",
        )


def test_one_thousand_row_reconciliation_is_bounded_truthful_and_zero_mutation(
    harness,
    tmp_path: Path,
) -> None:
    repository = MediaFileRepository(harness.database)
    with harness.database.transaction() as connection:
        rows = [
            (
                str((tmp_path / f"movie-{index}.mkv").absolute()),
                str((tmp_path / f"movie-{index}.mkv").absolute()).casefold(),
                1,
                MediaFileStatus.PRESENT.value,
                NOW.isoformat(),
                NOW.isoformat(),
            )
            for index in range(1_000)
        ]
        connection.executemany(
            """
            INSERT INTO media_files(
                current_path, path_key, file_size, status, discovered_at, last_seen_at
            ) VALUES (?, ?, ?, ?, ?, ?)
            """,
            rows,
        )
    progress = []

    result = ReconcileLibraryFiles(
        repository,
        SyntheticInspector(),
        now=lambda: NOW,
        batch_size=100,
        progress_interval=25,
    ).execute(progress=progress.append)

    assert (result.total, result.checked, result.present, result.missing, result.errors) == (
        1_000,
        1_000,
        800,
        150,
        50,
    )
    assert result.status_changes == 150
    assert all(left.checked <= right.checked for left, right in zip(progress, progress[1:]))
    stored = repository.list_cataloged(after_id=0, limit=1_000)
    assert sum(item.status is MediaFileStatus.MISSING for item in stored) == 150
    assert sum(item.status is MediaFileStatus.PRESENT for item in stored) == 850
    with harness.database.connection() as connection:
        assert connection.execute("SELECT COUNT(*) FROM file_operations").fetchone()[0] == 0
