from __future__ import annotations

from dataclasses import FrozenInstanceError

import pytest

from dropsort.application.dto.reconciliation import (
    LibraryReconciliationProgress,
    RelinkPreview,
    RelinkResult,
)
from dropsort.library.movies import MediaFile, MediaFileStatus
from datetime import datetime, timezone
from pathlib import Path


def test_reconciliation_progress_is_immutable_and_truthful() -> None:
    value = LibraryReconciliationProgress(
        total=10,
        checked=7,
        present=5,
        missing=1,
        errors=1,
        status_changes=2,
    )

    assert value.remaining == 3
    with pytest.raises(FrozenInstanceError):
        value.checked = 8  # type: ignore[misc]


@pytest.mark.parametrize(
    "values",
    (
        {"total": -1},
        {"total": 1, "checked": 2},
        {"total": 2, "checked": 1, "present": 1, "missing": 1},
        {"total": 2, "checked": 1, "status_changes": 2},
    ),
)
def test_reconciliation_progress_rejects_impossible_counts(values: dict[str, int]) -> None:
    defaults = dict(total=0, checked=0, present=0, missing=0, errors=0, status_changes=0)
    defaults.update(values)
    with pytest.raises(ValueError):
        LibraryReconciliationProgress(**defaults)


def test_relink_preview_requires_exact_absolute_paths_and_positive_identity() -> None:
    with pytest.raises(ValueError, match="absolute"):
        RelinkPreview(
            preview_id="token",
            media_file_id=1,
            movie_id=2,
            old_path="relative.mkv",
            new_path=r"D:\Movies\Movie.mkv",
            file_size=5,
            validation_reasons=("SIZE_EXACT",),
        )


def test_relink_preview_and_result_validate_remaining_boundary_fields() -> None:
    valid = dict(
        preview_id="token",
        media_file_id=1,
        movie_id=2,
        old_path=r"D:\Old.mkv",
        new_path=r"D:\New.mkv",
        file_size=5,
        validation_reasons=("SIZE_EXACT",),
    )
    for field, value in (
        ("preview_id", ""),
        ("media_file_id", 0),
        ("movie_id", True),
        ("new_path", "relative.mkv"),
        ("file_size", -1),
        ("validation_reasons", ["SIZE_EXACT"]),
    ):
        values = dict(valid)
        values[field] = value
        with pytest.raises(ValueError):
            RelinkPreview(**values)

    now = datetime(2026, 8, 12, tzinfo=timezone.utc)
    media = MediaFile(1, 2, Path(r"D:\New.mkv"), 5, ".mkv", None, None, None, MediaFileStatus.PRESENT, now, now)
    assert RelinkResult(media).current_path == Path(r"D:\New.mkv")
