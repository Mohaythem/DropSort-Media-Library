from __future__ import annotations

import threading
from typing import Protocol

from dropsort.application.dto.catalog_maintenance import ClearLibraryDataResult
from dropsort.application.errors import CatalogClearBlockedError, CatalogClearError
from dropsort.library.movies import (
    CatalogMaintenanceBlockedError,
    CatalogMaintenanceError,
    LibraryMaintenanceRepository,
)


class PosterCacheMaintenance(Protocol):
    def clear(self) -> int: ...


class ClearLibraryData:
    """Explicitly forget catalog/cache state without touching user media files."""

    def __init__(
        self,
        repository: LibraryMaintenanceRepository,
        poster_cache: PosterCacheMaintenance,
        *,
        execution_lock: threading.Lock,
    ) -> None:
        self._repository = repository
        self._poster_cache = poster_cache
        self._execution_lock = execution_lock

    def execute(self) -> ClearLibraryDataResult:
        if not self._execution_lock.acquire(blocking=False):
            raise CatalogClearBlockedError(
                "DropSort is busy with another catalog or file operation"
            )
        try:
            try:
                counts = self._repository.clear_catalog()
            except CatalogMaintenanceBlockedError as error:
                raise CatalogClearBlockedError(str(error)) from error
            except CatalogMaintenanceError as error:
                raise CatalogClearError("the local library could not be cleared") from error
        finally:
            self._execution_lock.release()

        warning = None
        try:
            poster_files_removed = self._poster_cache.clear()
        except (OSError, ValueError):
            poster_files_removed = 0
            warning = "POSTER_CACHE_CLEANUP_FAILED"
        return ClearLibraryDataResult(
            movies_removed=counts.movies,
            media_files_removed=counts.media_files,
            metadata_entries_removed=counts.metadata_entries,
            poster_files_removed=poster_files_removed,
            warning=warning,
        )
