from __future__ import annotations

from pathlib import Path

import pytest

from dropsort.core.operations import OperationState, OperationType
from dropsort.core.operations.models import FileOperationRecord
from dropsort.library.operations import OperationJournalSnapshot


def _record() -> FileOperationRecord:
    return FileOperationRecord(
        id="operation-1",
        operation_type=OperationType.MOVE,
        source=Path("source.mkv"),
        destination=Path("destination.mkv"),
        state=OperationState.COMMITTED,
        media_file_id=1,
        reverses_operation_id=None,
        source_size=1,
        source_mtime_ns=1,
        source_dev=1,
        source_ino=1,
        destination_size=1,
        destination_mtime_ns=1,
        destination_dev=1,
        destination_ino=1,
        destination_sha256=None,
        strategy="hardlink-unlink",
        error_code=None,
        error_message=None,
        created_at="2026-01-01T00:00:00+00:00",
        updated_at="2026-01-01T00:00:00+00:00",
    )


def test_snapshot_validates_catalog_context() -> None:
    snapshot = OperationJournalSnapshot(_record(), "Movie", Path("current.mkv"), "reverse-1")
    assert snapshot.movie_title == "Movie"
    for values in (
        (object(), None, None, None),
        (_record(), "", None, None),
        (_record(), None, "not-path", None),
        (_record(), None, None, ""),
    ):
        with pytest.raises(ValueError):
            OperationJournalSnapshot(*values)  # type: ignore[arg-type]
