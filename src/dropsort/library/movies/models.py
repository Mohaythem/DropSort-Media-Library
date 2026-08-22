from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime
from enum import StrEnum
import math
from pathlib import Path


class MediaFileStatus(StrEnum):
    PRESENT = "PRESENT"
    MISSING = "MISSING"


@dataclass(frozen=True, slots=True)
class MediaFileStatusUpdate:
    media_file_id: int
    expected_path: Path
    status: MediaFileStatus
    observed_at: datetime

    def __post_init__(self) -> None:
        _validate_positive_id(self.media_file_id, "media_file_id")
        if not isinstance(self.expected_path, Path) or not self.expected_path.is_absolute():
            raise ValueError("expected_path must be an absolute Path")
        if not isinstance(self.status, MediaFileStatus):
            raise ValueError("status must be MediaFileStatus")
        _validate_aware_datetime(self.observed_at, "observed_at")


@dataclass(frozen=True, slots=True)
class MovieCatalogData:
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

    def __post_init__(self) -> None:
        object.__setattr__(self, "provider", _clean_required_text(self.provider, "provider"))
        object.__setattr__(
            self,
            "external_id",
            _clean_required_text(self.external_id, "external_id"),
        )
        object.__setattr__(self, "title", _clean_required_text(self.title, "title"))
        _validate_optional_text(self.original_title, "original_title")
        _validate_year(self.year)
        _validate_optional_text(self.overview, "overview")
        if not isinstance(self.genres, tuple) or any(
            not isinstance(genre, str) or not genre.strip() for genre in self.genres
        ):
            raise ValueError("genres must contain non-empty strings")
        object.__setattr__(self, "genres", tuple(genre.strip() for genre in self.genres))
        if self.runtime_minutes is not None and (
            isinstance(self.runtime_minutes, bool)
            or not isinstance(self.runtime_minutes, int)
            or self.runtime_minutes <= 0
        ):
            raise ValueError("runtime_minutes must be a positive integer")
        if self.rating is not None and (
            isinstance(self.rating, bool)
            or not isinstance(self.rating, (int, float))
            or not math.isfinite(self.rating)
            or not 0.0 <= self.rating <= 10.0
        ):
            raise ValueError("rating must be a finite number from 0 through 10")
        _validate_optional_text(self.poster_reference, "poster_reference")


@dataclass(frozen=True, slots=True)
class Movie:
    id: int
    data: MovieCatalogData
    date_added: datetime
    created_at: datetime
    updated_at: datetime

    def __post_init__(self) -> None:
        _validate_positive_id(self.id, "id")
        if not isinstance(self.data, MovieCatalogData):
            raise ValueError("data must be MovieCatalogData")
        _validate_aware_datetime(self.date_added, "date_added")
        _validate_aware_datetime(self.created_at, "created_at")
        _validate_aware_datetime(self.updated_at, "updated_at")

    @property
    def provider(self) -> str:
        return self.data.provider

    @property
    def external_id(self) -> str:
        return self.data.external_id

    @property
    def title(self) -> str:
        return self.data.title

    @property
    def original_title(self) -> str | None:
        return self.data.original_title

    @property
    def year(self) -> int | None:
        return self.data.year

    @property
    def overview(self) -> str | None:
        return self.data.overview

    @property
    def genres(self) -> tuple[str, ...]:
        return self.data.genres

    @property
    def runtime_minutes(self) -> int | None:
        return self.data.runtime_minutes

    @property
    def rating(self) -> float | None:
        return self.data.rating

    @property
    def poster_reference(self) -> str | None:
        return self.data.poster_reference


@dataclass(frozen=True, slots=True)
class VerifiedMediaFileFacts:
    current_path: Path
    file_size: int
    extension: str
    resolution: str | None
    codec: str | None
    source: str | None
    observed_at: datetime

    def __post_init__(self) -> None:
        if not isinstance(self.current_path, Path) or not self.current_path.is_absolute():
            raise ValueError("current_path must be an absolute Path")
        if (
            isinstance(self.file_size, bool)
            or not isinstance(self.file_size, int)
            or self.file_size < 0
        ):
            raise ValueError("file_size must be a non-negative integer")
        if (
            not isinstance(self.extension, str)
            or not self.extension.startswith(".")
            or len(self.extension) < 2
        ):
            raise ValueError("extension must include a leading dot")
        for field_name in ("resolution", "codec", "source"):
            _validate_optional_text(getattr(self, field_name), field_name)
        _validate_aware_datetime(self.observed_at, "observed_at")


@dataclass(frozen=True, slots=True)
class MediaFile:
    id: int
    movie_id: int | None
    current_path: Path
    file_size: int
    extension: str | None
    resolution: str | None
    codec: str | None
    source: str | None
    status: MediaFileStatus
    discovered_at: datetime
    last_seen_at: datetime

    def __post_init__(self) -> None:
        _validate_positive_id(self.id, "id")
        if self.movie_id is not None:
            _validate_positive_id(self.movie_id, "movie_id")
        if not isinstance(self.current_path, Path) or not self.current_path.is_absolute():
            raise ValueError("current_path must be an absolute Path")
        if (
            isinstance(self.file_size, bool)
            or not isinstance(self.file_size, int)
            or self.file_size < 0
        ):
            raise ValueError("file_size must be a non-negative integer")
        if self.extension is not None and (
            not isinstance(self.extension, str) or not self.extension.startswith(".")
        ):
            raise ValueError("extension must include a leading dot")
        for field_name in ("resolution", "codec", "source"):
            _validate_optional_text(getattr(self, field_name), field_name)
        if not isinstance(self.status, MediaFileStatus):
            raise ValueError("status must be MediaFileStatus")
        _validate_aware_datetime(self.discovered_at, "discovered_at")
        _validate_aware_datetime(self.last_seen_at, "last_seen_at")


def _clean_required_text(value: str, field_name: str) -> str:
    if not isinstance(value, str) or not value.strip():
        raise ValueError(f"{field_name} must be a non-empty string")
    return value.strip()


def _validate_optional_text(value: str | None, field_name: str) -> None:
    if value is not None and (not isinstance(value, str) or not value.strip()):
        raise ValueError(f"{field_name} must be None or a non-empty string")


def _validate_year(value: int | None) -> None:
    if value is not None and (
        isinstance(value, bool) or not isinstance(value, int) or not 1 <= value <= 9999
    ):
        raise ValueError("year must be an integer from 1 through 9999")


def _validate_positive_id(value: int, field_name: str) -> None:
    if isinstance(value, bool) or not isinstance(value, int) or value <= 0:
        raise ValueError(f"{field_name} must be a positive integer")


def _validate_aware_datetime(value: datetime, field_name: str) -> None:
    if not isinstance(value, datetime) or value.tzinfo is None:
        raise ValueError(f"{field_name} must be a timezone-aware datetime")
