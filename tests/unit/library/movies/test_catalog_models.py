from __future__ import annotations

from dataclasses import FrozenInstanceError
from datetime import datetime, timezone
from pathlib import Path

import pytest

from dropsort.library.movies import (
    MediaFile,
    MediaFileStatus,
    Movie,
    MovieCatalogData,
    VerifiedMediaFileFacts,
)


NOW = datetime(2026, 8, 11, 12, 0, tzinfo=timezone.utc)


def _movie_data(**overrides: object) -> MovieCatalogData:
    values: dict[str, object] = {
        "provider": "tmdb",
        "external_id": "155",
        "title": "The Dark Knight",
        "original_title": "The Dark Knight",
        "year": 2008,
        "overview": "Batman faces the Joker.",
        "genres": ("Drama", "Action"),
        "runtime_minutes": 152,
        "rating": 8.5,
        "poster_reference": "/poster.jpg",
    }
    values.update(overrides)
    return MovieCatalogData(**values)  # type: ignore[arg-type]


def test_movie_catalog_data_preserves_optional_metadata_and_genres() -> None:
    data = _movie_data(
        original_title=None,
        year=None,
        overview=None,
        genres=(),
        runtime_minutes=None,
        rating=None,
        poster_reference=None,
    )

    assert data.year is None
    assert data.overview is None
    assert data.genres == ()
    assert data.runtime_minutes is None
    assert data.rating is None


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("provider", ""),
        ("external_id", " "),
        ("title", ""),
        ("year", 0),
        ("runtime_minutes", 0),
        ("rating", 11.0),
        ("genres", ("Drama", "")),
    ],
)
def test_movie_catalog_data_rejects_invalid_values(field: str, value: object) -> None:
    with pytest.raises(ValueError):
        _movie_data(**{field: value})


def test_movie_model_is_immutable_and_requires_aware_timestamps() -> None:
    movie = Movie(
        id=1,
        data=_movie_data(),
        date_added=NOW,
        created_at=NOW,
        updated_at=NOW,
    )

    with pytest.raises(FrozenInstanceError):
        movie.id = 2  # type: ignore[misc]
    with pytest.raises(ValueError, match="timezone-aware"):
        Movie(1, _movie_data(), NOW.replace(tzinfo=None), NOW, NOW)


def test_movie_exposes_flat_catalog_properties() -> None:
    movie = Movie(1, _movie_data(), NOW, NOW, NOW)

    assert movie.original_title == "The Dark Knight"
    assert movie.year == 2008
    assert movie.overview == "Batman faces the Joker."
    assert movie.genres == ("Drama", "Action")
    assert movie.runtime_minutes == 152
    assert movie.rating == 8.5
    assert movie.poster_reference == "/poster.jpg"


def test_movie_rejects_invalid_data_and_id() -> None:
    with pytest.raises(ValueError, match="positive"):
        Movie(0, _movie_data(), NOW, NOW, NOW)
    with pytest.raises(ValueError, match="MovieCatalogData"):
        Movie(1, None, NOW, NOW, NOW)  # type: ignore[arg-type]


def test_verified_file_facts_require_absolute_path_and_preserve_technical_data(
    tmp_path: Path,
) -> None:
    path = (tmp_path / "Movie.mkv").absolute()
    facts = VerifiedMediaFileFacts(
        current_path=path,
        file_size=123,
        extension=".mkv",
        resolution="1080p",
        codec="x264",
        source="BluRay",
        observed_at=NOW,
    )

    assert facts.current_path == path
    assert facts.file_size == 123
    assert facts.extension == ".mkv"


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("current_path", Path("relative.mkv")),
        ("file_size", -1),
        ("extension", "mkv"),
        ("observed_at", NOW.replace(tzinfo=None)),
    ],
)
def test_verified_file_facts_reject_invalid_values(
    tmp_path: Path,
    field: str,
    value: object,
) -> None:
    values: dict[str, object] = {
        "current_path": (tmp_path / "Movie.mkv").absolute(),
        "file_size": 123,
        "extension": ".mkv",
        "resolution": None,
        "codec": None,
        "source": None,
        "observed_at": NOW,
    }
    values[field] = value

    with pytest.raises(ValueError):
        VerifiedMediaFileFacts(**values)  # type: ignore[arg-type]


def test_media_file_keeps_logical_association_separate_from_physical_facts(
    tmp_path: Path,
) -> None:
    facts = VerifiedMediaFileFacts(
        (tmp_path / "Movie.mkv").absolute(),
        123,
        ".mkv",
        "1080p",
        "x264",
        "BluRay",
        NOW,
    )
    media_file = MediaFile(
        id=2,
        movie_id=1,
        current_path=facts.current_path,
        file_size=facts.file_size,
        extension=facts.extension,
        resolution=facts.resolution,
        codec=facts.codec,
        source=facts.source,
        status=MediaFileStatus.PRESENT,
        discovered_at=NOW,
        last_seen_at=NOW,
    )

    assert media_file.movie_id == 1
    assert media_file.status is MediaFileStatus.PRESENT


def test_media_file_accepts_legacy_optional_extension(tmp_path: Path) -> None:
    media_file = MediaFile(
        id=1,
        movie_id=None,
        current_path=(tmp_path / "legacy").absolute(),
        file_size=0,
        extension=None,
        resolution=None,
        codec=None,
        source=None,
        status=MediaFileStatus.MISSING,
        discovered_at=NOW,
        last_seen_at=NOW,
    )

    assert media_file.extension is None
    assert media_file.movie_id is None


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("id", 0),
        ("movie_id", 0),
        ("current_path", Path("relative.mkv")),
        ("file_size", -1),
        ("extension", "mkv"),
        ("status", "PRESENT"),
        ("discovered_at", NOW.replace(tzinfo=None)),
    ],
)
def test_media_file_rejects_invalid_catalog_values(
    tmp_path: Path,
    field: str,
    value: object,
) -> None:
    values: dict[str, object] = {
        "id": 1,
        "movie_id": 1,
        "current_path": (tmp_path / "Movie.mkv").absolute(),
        "file_size": 1,
        "extension": ".mkv",
        "resolution": None,
        "codec": None,
        "source": None,
        "status": MediaFileStatus.PRESENT,
        "discovered_at": NOW,
        "last_seen_at": NOW,
    }
    values[field] = value

    with pytest.raises(ValueError):
        MediaFile(**values)  # type: ignore[arg-type]
