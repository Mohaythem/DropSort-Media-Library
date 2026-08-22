from __future__ import annotations

from dataclasses import dataclass
import math


def _require_text(value: str, field_name: str) -> None:
    if not isinstance(value, str) or not value.strip():
        raise ValueError(f"{field_name} must be a non-empty string")


def _validate_optional_text(value: str | None, field_name: str) -> None:
    if value is not None:
        _require_text(value, field_name)


def _validate_year(value: int | None) -> None:
    if value is not None and (
        isinstance(value, bool) or not isinstance(value, int) or not 1 <= value <= 9999
    ):
        raise ValueError("year must be an integer from 1 through 9999")


def _validate_rating(value: float | None) -> None:
    if value is not None and (
        isinstance(value, bool)
        or not isinstance(value, (int, float))
        or not math.isfinite(value)
        or not 0 <= value <= 10
    ):
        raise ValueError("rating must be a finite number from 0 through 10")


def _validate_string_tuple(value: tuple[str, ...], field_name: str) -> None:
    if not isinstance(value, tuple) or any(
        not isinstance(item, str) or not item.strip() for item in value
    ):
        raise ValueError(f"{field_name} must contain non-empty strings")


@dataclass(frozen=True, slots=True)
class MovieSearchQuery:
    title: str
    year: int | None = None

    def __post_init__(self) -> None:
        _require_text(self.title, "title")
        object.__setattr__(self, "title", " ".join(self.title.split()))
        _validate_year(self.year)


@dataclass(frozen=True, slots=True)
class MovieCandidate:
    provider: str
    external_id: str
    title: str
    original_title: str | None
    year: int | None
    overview: str | None
    rating: float | None
    poster_reference: str | None

    def __post_init__(self) -> None:
        _require_text(self.provider, "provider")
        _require_text(self.external_id, "external_id")
        _require_text(self.title, "title")
        _validate_optional_text(self.original_title, "original_title")
        _validate_year(self.year)
        _validate_optional_text(self.overview, "overview")
        _validate_rating(self.rating)
        _validate_optional_text(self.poster_reference, "poster_reference")


@dataclass(frozen=True, slots=True)
class MovieMetadata:
    provider: str
    external_id: str
    title: str
    original_title: str | None
    year: int | None
    overview: str | None
    genres: tuple[str, ...]
    runtime_minutes: int | None
    rating: float | None
    director: str | None
    cast: tuple[str, ...]
    poster_reference: str | None

    def __post_init__(self) -> None:
        _require_text(self.provider, "provider")
        _require_text(self.external_id, "external_id")
        _require_text(self.title, "title")
        _validate_optional_text(self.original_title, "original_title")
        _validate_year(self.year)
        _validate_optional_text(self.overview, "overview")
        _validate_string_tuple(self.genres, "genres")
        if self.runtime_minutes is not None and (
            isinstance(self.runtime_minutes, bool)
            or not isinstance(self.runtime_minutes, int)
            or self.runtime_minutes <= 0
        ):
            raise ValueError("runtime_minutes must be a positive integer")
        _validate_rating(self.rating)
        _validate_optional_text(self.director, "director")
        _validate_string_tuple(self.cast, "cast")
        _validate_optional_text(self.poster_reference, "poster_reference")
