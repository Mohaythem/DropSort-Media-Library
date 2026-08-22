from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path

from dropsort.core.operations.models import FileOperationRecord


@dataclass(frozen=True, slots=True)
class OperationJournalSnapshot:
    """One journal record plus local-catalog context for application reads."""

    record: FileOperationRecord
    movie_title: str | None
    current_catalog_path: Path | None
    reversed_by_operation_id: str | None

    def __post_init__(self) -> None:
        if not isinstance(self.record, FileOperationRecord):
            raise ValueError("record must be FileOperationRecord")
        if self.movie_title is not None and (
            not isinstance(self.movie_title, str) or not self.movie_title.strip()
        ):
            raise ValueError("movie_title must be non-empty text when present")
        if self.current_catalog_path is not None and not isinstance(
            self.current_catalog_path, Path
        ):
            raise ValueError("current_catalog_path must be Path when present")
        if self.reversed_by_operation_id is not None and (
            not isinstance(self.reversed_by_operation_id, str)
            or not self.reversed_by_operation_id.strip()
        ):
            raise ValueError("reversed_by_operation_id must be non-empty text when present")
