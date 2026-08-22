from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime
from enum import StrEnum


MAX_LIBRARY_PAGE_SIZE = 1_000


class MediaFileAvailability(StrEnum):
    PRESENT = "PRESENT"
    MISSING = "MISSING"


@dataclass(frozen=True, slots=True)
class MovieListQuery:
    limit: int = 100
    offset: int = 0

    def __post_init__(self) -> None:
        _validate_limit(self.limit, maximum=MAX_LIBRARY_PAGE_SIZE)
        if (
            isinstance(self.offset, bool)
            or not isinstance(self.offset, int)
            or self.offset < 0
        ):
            raise ValueError("offset must be a non-negative integer")


@dataclass(frozen=True, slots=True)
class MovieListItem:
    movie_id: int
    provider: str
    title: str
    original_title: str | None
    year: int | None
    rating: float | None
    poster_reference: str | None
    media_file_count: int
    date_added: datetime
    missing_file_count: int = 0

    def __post_init__(self) -> None:
        _validate_positive_id(self.movie_id, "movie_id")
        if not isinstance(self.provider, str) or not self.provider.strip():
            raise ValueError("provider must be non-empty text")
        if (
            isinstance(self.media_file_count, bool)
            or not isinstance(self.media_file_count, int)
            or self.media_file_count < 0
        ):
            raise ValueError("media_file_count must be a non-negative integer")
        if (
            isinstance(self.missing_file_count, bool)
            or not isinstance(self.missing_file_count, int)
            or not 0 <= self.missing_file_count <= self.media_file_count
        ):
            raise ValueError("missing_file_count must be within media_file_count")

    @property
    def all_files_missing(self) -> bool:
        return self.media_file_count > 0 and self.missing_file_count == self.media_file_count


@dataclass(frozen=True, slots=True)
class MediaFileDetails:
    media_file_id: int
    current_path: str
    file_size: int
    extension: str | None
    resolution: str | None
    codec: str | None
    source: str | None
    status: MediaFileAvailability

    def __post_init__(self) -> None:
        _validate_positive_id(self.media_file_id, "media_file_id")
        if not isinstance(self.current_path, str) or not self.current_path:
            raise ValueError("current_path must be a non-empty string")
        if (
            isinstance(self.file_size, bool)
            or not isinstance(self.file_size, int)
            or self.file_size < 0
        ):
            raise ValueError("file_size must be a non-negative integer")
        if not isinstance(self.status, MediaFileAvailability):
            raise ValueError("status must be MediaFileAvailability")


@dataclass(frozen=True, slots=True)
class MovieDetails:
    movie_id: int
    provider: str
    external_id: str
    title: str
    original_title: str | None
    year: int | None
    overview: str | None
    genres: tuple[str, ...]
    runtime_minutes: int | None
    rating: float | None
    poster_reference: str | None
    date_added: datetime
    media_files: tuple[MediaFileDetails, ...]

    def __post_init__(self) -> None:
        _validate_positive_id(self.movie_id, "movie_id")
        if not isinstance(self.genres, tuple):
            raise ValueError("genres must be a tuple")
        if not isinstance(self.media_files, tuple):
            raise ValueError("media_files must be a tuple")


def _validate_limit(value: int, *, maximum: int) -> None:
    if (
        isinstance(value, bool)
        or not isinstance(value, int)
        or not 1 <= value <= maximum
    ):
        raise ValueError(f"limit must be an integer from 1 through {maximum}")


def _validate_positive_id(value: int, field_name: str) -> None:
    if isinstance(value, bool) or not isinstance(value, int) or value <= 0:
        raise ValueError(f"{field_name} must be a positive integer")
