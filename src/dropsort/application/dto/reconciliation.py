from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path

from dropsort.application.dto.library import MediaFileAvailability
from dropsort.library.movies import MediaFile


@dataclass(frozen=True, slots=True)
class MediaFileStatusChange:
    media_file_id: int
    movie_id: int
    status: MediaFileAvailability

    def __post_init__(self) -> None:
        for field_name in ("media_file_id", "movie_id"):
            value = getattr(self, field_name)
            if isinstance(value, bool) or not isinstance(value, int) or value <= 0:
                raise ValueError(f"{field_name} must be a positive integer")
        if not isinstance(self.status, MediaFileAvailability):
            raise ValueError("status must be MediaFileAvailability")


@dataclass(frozen=True, slots=True)
class LibraryReconciliationProgress:
    total: int
    checked: int
    present: int
    missing: int
    errors: int
    status_changes: int
    changes: tuple[MediaFileStatusChange, ...] = ()

    def __post_init__(self) -> None:
        values = (
            self.total,
            self.checked,
            self.present,
            self.missing,
            self.errors,
            self.status_changes,
        )
        if any(isinstance(value, bool) or not isinstance(value, int) or value < 0 for value in values):
            raise ValueError("progress counts must be non-negative integers")
        if self.checked > self.total:
            raise ValueError("checked cannot exceed total")
        if self.present + self.missing + self.errors != self.checked:
            raise ValueError("inspection counts must equal checked")
        if self.status_changes > self.checked:
            raise ValueError("status_changes cannot exceed checked")
        if not isinstance(self.changes, tuple) or any(
            not isinstance(change, MediaFileStatusChange) for change in self.changes
        ):
            raise ValueError("changes must be a tuple of MediaFileStatusChange values")
        if len({change.media_file_id for change in self.changes}) != len(self.changes):
            raise ValueError("changes cannot repeat a media_file_id")

    @property
    def remaining(self) -> int:
        return self.total - self.checked


@dataclass(frozen=True, slots=True)
class RelinkPreview:
    preview_id: str
    media_file_id: int
    movie_id: int
    old_path: str
    new_path: str
    file_size: int
    validation_reasons: tuple[str, ...]

    def __post_init__(self) -> None:
        if not isinstance(self.preview_id, str) or not self.preview_id:
            raise ValueError("preview_id must be non-empty text")
        for field_name in ("media_file_id", "movie_id"):
            value = getattr(self, field_name)
            if isinstance(value, bool) or not isinstance(value, int) or value <= 0:
                raise ValueError(f"{field_name} must be a positive integer")
        for field_name in ("old_path", "new_path"):
            value = getattr(self, field_name)
            if not isinstance(value, str) or not Path(value).is_absolute():
                raise ValueError(f"{field_name} must be an absolute path")
        if isinstance(self.file_size, bool) or not isinstance(self.file_size, int) or self.file_size < 0:
            raise ValueError("file_size must be a non-negative integer")
        if not isinstance(self.validation_reasons, tuple):
            raise ValueError("validation_reasons must be a tuple")


@dataclass(frozen=True, slots=True)
class RelinkResult:
    media_file: MediaFile

    def __getattr__(self, name: str):
        return getattr(self.media_file, name)
