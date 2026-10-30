from __future__ import annotations

from collections.abc import Callable
from datetime import datetime, timezone
from threading import Event
from typing import Protocol

from dropsort.application.dto.library import MediaFileAvailability
from dropsort.application.dto.reconciliation import (
    LibraryReconciliationProgress,
    MediaFileStatusChange,
)
from dropsort.application.errors import (
    LibraryReconciliationCancelled,
    LibraryReconciliationError,
)
from dropsort.library.availability import AvailabilityInspectionStatus
from dropsort.library.movies import MediaFileRepository, MediaFileStatus, MediaFileStatusUpdate


class AvailabilityInspector(Protocol):
    def inspect(self, path): ...


ProgressCallback = Callable[[LibraryReconciliationProgress], None]


class ReconciliationCancellation:
    def __init__(self) -> None:
        self._event = Event()

    def cancel(self) -> None:
        self._event.set()

    def is_cancelled(self) -> bool:
        return self._event.is_set()


class ReconcileLibraryFiles:
    """Explicit bounded catalog reconciliation; never scans outside catalog paths."""

    def __init__(
        self,
        repository: MediaFileRepository,
        inspector: AvailabilityInspector,
        *,
        now: Callable[[], datetime] | None = None,
        batch_size: int = 100,
        progress_interval: int = 16,
    ) -> None:
        if any(
            isinstance(value, bool) or not isinstance(value, int) or value <= 0
            for value in (batch_size, progress_interval)
        ):
            raise ValueError("batch_size and progress_interval must be positive integers")
        self._repository = repository
        self._inspector = inspector
        self._now = now or (lambda: datetime.now(timezone.utc))
        self._batch_size = batch_size
        self._progress_interval = progress_interval

    def execute(
        self,
        *,
        progress: ProgressCallback | None = None,
        cancellation: ReconciliationCancellation | None = None,
    ) -> LibraryReconciliationProgress:
        total = self._repository.count_cataloged()
        counts = [0, 0, 0, 0, 0]  # checked, present, missing, errors, changes
        latest = _progress(total, counts)
        last_emitted: LibraryReconciliationProgress | None = None
        if total == 0:
            _emit(progress, latest)
            return latest
        after_id = 0
        pending: list[MediaFileStatusUpdate] = []
        pending_events: dict[int, MediaFileStatusChange] = {}
        while True:
            self._raise_if_cancelled(cancellation, latest)
            page = self._repository.list_cataloged(after_id=after_id, limit=self._batch_size)
            if not page:
                break
            for media_file in page:
                self._raise_if_cancelled(cancellation, latest)
                inspection = self._inspector.inspect(media_file.current_path)
                counts[0] += 1
                if inspection.status is AvailabilityInspectionStatus.PRESENT:
                    counts[1] += 1
                    desired = MediaFileStatus.PRESENT
                elif inspection.status is AvailabilityInspectionStatus.MISSING:
                    counts[2] += 1
                    desired = MediaFileStatus.MISSING
                else:
                    counts[3] += 1
                    desired = media_file.status
                if desired is not media_file.status:
                    pending.append(
                        MediaFileStatusUpdate(
                            media_file.id,
                            media_file.current_path,
                            desired,
                            self._require_aware_now(),
                        )
                    )
                    if media_file.movie_id is not None:
                        pending_events[media_file.id] = MediaFileStatusChange(
                            media_file_id=media_file.id,
                            movie_id=media_file.movie_id,
                            status=MediaFileAvailability(desired.value),
                        )
                after_id = media_file.id
                if counts[0] % self._progress_interval == 0:
                    latest = _progress(total, counts)
                    _emit(progress, latest)
                    last_emitted = latest
            batch_changes: tuple[MediaFileStatusChange, ...] = ()
            if pending:
                try:
                    applied_changes = self._repository.apply_status_updates(tuple(pending))
                except Exception as error:
                    raise LibraryReconciliationError(
                        "could not commit library status updates"
                    ) from error
                counts[4] += applied_changes
                if applied_changes == len(pending):
                    batch_changes = tuple(pending_events.values())
                else:
                    batch_changes = _confirmed_changes(
                        self._repository,
                        tuple(pending),
                        pending_events,
                    )
                pending.clear()
                pending_events.clear()
            latest = _progress(total, counts, changes=batch_changes)
            _emit(progress, latest)
            last_emitted = latest
        latest = _progress(total, counts)
        if latest != last_emitted:
            _emit(progress, latest)
        return latest

    def _require_aware_now(self) -> datetime:
        value = self._now()
        if not isinstance(value, datetime) or value.tzinfo is None:
            raise ValueError("now must return a timezone-aware datetime")
        return value

    @staticmethod
    def _raise_if_cancelled(cancellation, latest) -> None:
        if cancellation is not None and cancellation.is_cancelled():
            raise LibraryReconciliationCancelled(latest)


def _progress(
    total: int,
    counts: list[int],
    *,
    changes: tuple[MediaFileStatusChange, ...] = (),
) -> LibraryReconciliationProgress:
    return LibraryReconciliationProgress(total, *counts, changes=changes)


def _confirmed_changes(
    repository: MediaFileRepository,
    updates: tuple[MediaFileStatusUpdate, ...],
    events: dict[int, MediaFileStatusChange],
) -> tuple[MediaFileStatusChange, ...]:
    confirmed: list[MediaFileStatusChange] = []
    for update in updates:
        event = events.get(update.media_file_id)
        if event is None:
            continue
        current = repository.get_by_id(update.media_file_id)
        if (
            current is not None
            and current.movie_id == event.movie_id
            and current.current_path == update.expected_path
            and current.status is update.status
        ):
            confirmed.append(event)
    return tuple(confirmed)


def _emit(callback: ProgressCallback | None, value: LibraryReconciliationProgress) -> None:
    if callback is not None:
        callback(value)
