from __future__ import annotations

from dataclasses import FrozenInstanceError
from datetime import datetime, timezone

import pytest

from dropsort.application.dto.library import (
    MediaFileAvailability,
    MediaFileDetails,
    MovieDetails,
    MovieListItem,
    MovieListQuery,
)


NOW = datetime(2026, 8, 11, 12, 0, tzinfo=timezone.utc)


def test_movie_list_query_defaults_and_validates_bounds() -> None:
    assert MovieListQuery() == MovieListQuery(limit=100, offset=0)

    for invalid_limit in (0, -1, True, 1001):
        with pytest.raises(ValueError, match="limit"):
            MovieListQuery(limit=invalid_limit)  # type: ignore[arg-type]
    for invalid_offset in (-1, True):
        with pytest.raises(ValueError, match="offset"):
            MovieListQuery(offset=invalid_offset)  # type: ignore[arg-type]


def test_presentation_dtos_are_immutable_and_keep_optional_values() -> None:
    item = MovieListItem(
        movie_id=1,
        provider="tmdb",
        title="Movie",
        original_title=None,
        year=None,
        rating=None,
        poster_reference=None,
        media_file_count=0,
        date_added=NOW,
    )
    details = MovieDetails(
        movie_id=1,
        provider="tmdb",
        external_id="1",
        title="Movie",
        original_title=None,
        year=None,
        overview=None,
        genres=(),
        runtime_minutes=None,
        rating=None,
        poster_reference=None,
        date_added=NOW,
        media_files=(),
    )

    assert item.year is None
    assert details.overview is None
    with pytest.raises(FrozenInstanceError):
        item.title = "Changed"  # type: ignore[misc]


def test_media_file_details_exposes_raw_catalog_values_and_controlled_status() -> None:
    details = MediaFileDetails(
        media_file_id=2,
        current_path=r"D:\Movies\Movie.mkv",
        file_size=1_234,
        extension=".mkv",
        resolution="2160p",
        codec="x265",
        source="BluRay",
        status=MediaFileAvailability.MISSING,
    )

    assert details.current_path == r"D:\Movies\Movie.mkv"
    assert details.file_size == 1_234
    assert details.status.value == "MISSING"


@pytest.mark.parametrize(
    ("field", "value", "message"),
    (
        ("current_path", "", "current_path"),
        ("file_size", -1, "file_size"),
        ("status", "PRESENT", "status"),
    ),
)
def test_media_file_details_rejects_invalid_boundary_values(
    field: str,
    value: object,
    message: str,
) -> None:
    values: dict[str, object] = {
        "media_file_id": 2,
        "current_path": r"D:\Movies\Movie.mkv",
        "file_size": 1_234,
        "extension": ".mkv",
        "resolution": None,
        "codec": None,
        "source": None,
        "status": MediaFileAvailability.PRESENT,
    }
    values[field] = value

    with pytest.raises(ValueError, match=message):
        MediaFileDetails(**values)  # type: ignore[arg-type]


@pytest.mark.parametrize(
    ("field", "value"),
    (("genres", ["Drama"]), ("media_files", [])),
)
def test_movie_details_requires_immutable_collection_fields(
    field: str,
    value: object,
) -> None:
    values: dict[str, object] = {
        "movie_id": 1,
        "provider": "tmdb",
        "provider": "tmdb",
        "external_id": "1",
        "title": "Movie",
        "original_title": None,
        "year": None,
        "overview": None,
        "genres": (),
        "runtime_minutes": None,
        "rating": None,
        "poster_reference": None,
        "date_added": NOW,
        "media_files": (),
    }
    values[field] = value

    with pytest.raises(ValueError, match=field):
        MovieDetails(**values)  # type: ignore[arg-type]


@pytest.mark.parametrize(
    ("field", "value"),
    (("movie_id", 0), ("media_file_count", -1)),
)
def test_movie_list_item_rejects_invalid_identifiers_and_counts(
    field: str,
    value: int,
) -> None:
    values = {
        "movie_id": 1,
        "provider": "tmdb",
        "title": "Movie",
        "original_title": None,
        "year": 2024,
        "rating": 7.0,
        "poster_reference": None,
        "media_file_count": 1,
        "date_added": NOW,
    }
    values[field] = value

    with pytest.raises(ValueError, match=field):
        MovieListItem(**values)  # type: ignore[arg-type]


def test_movie_list_item_requires_provider_identity_for_poster_namespace() -> None:
    with pytest.raises(ValueError, match="provider"):
        MovieListItem(
            movie_id=1,
            provider="",
            title="Movie",
            original_title=None,
            year=2024,
            rating=None,
            poster_reference="/poster.jpg",
            media_file_count=1,
            date_added=NOW,
        )
