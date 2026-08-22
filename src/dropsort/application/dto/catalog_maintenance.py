from __future__ import annotations

from dataclasses import dataclass


@dataclass(frozen=True, slots=True)
class ClearLibraryDataResult:
    movies_removed: int
    media_files_removed: int
    metadata_entries_removed: int
    poster_files_removed: int
    warning: str | None = None
