from __future__ import annotations

from dataclasses import dataclass
from typing import Protocol


@dataclass(frozen=True, slots=True)
class CatalogClearCounts:
    movies: int
    media_files: int
    metadata_entries: int


class LibraryMaintenanceRepository(Protocol):
    def clear_catalog(self) -> CatalogClearCounts: ...
