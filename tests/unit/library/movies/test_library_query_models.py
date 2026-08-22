from __future__ import annotations

from datetime import datetime, timezone
from pathlib import Path

import pytest

from dropsort.library.movies import (
    MediaFile,
    MediaFileStatus,
    Movie,
    MovieCatalogData,
    MovieDetailsSnapshot,
    MovieSummary,
)


NOW = datetime(2026, 8, 11, 12, 0, tzinfo=timezone.utc)


def _movie() -> Movie:
    return Movie(
        id=1,
        data=MovieCatalogData(
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
        ),
        date_added=NOW,
        created_at=NOW,
        updated_at=NOW,
    )


def _media_file(movie_id: int | None = 1) -> MediaFile:
    return MediaFile(
        id=2,
        movie_id=movie_id,
        current_path=Path(r"D:\Movies\Movie.mkv"),
        file_size=100,
        extension=".mkv",
        resolution=None,
        codec=None,
        source=None,
        status=MediaFileStatus.PRESENT,
        discovered_at=NOW,
        last_seen_at=NOW,
    )


def test_read_projections_accept_coherent_domain_values() -> None:
    movie = _movie()

    assert MovieSummary(movie=movie, media_file_count=0).movie is movie
    assert MovieDetailsSnapshot(movie=movie, media_files=(_media_file(),)).movie is movie


@pytest.mark.parametrize(
    ("movie", "count"),
    ((object(), 0), (_movie(), -1)),
)
def test_movie_summary_rejects_invalid_values(movie: object, count: int) -> None:
    with pytest.raises(ValueError):
        MovieSummary(movie=movie, media_file_count=count)  # type: ignore[arg-type]


def test_details_snapshot_requires_movie_and_immutable_media_file_values() -> None:
    with pytest.raises(ValueError, match="movie"):
        MovieDetailsSnapshot(movie=object(), media_files=())  # type: ignore[arg-type]
    with pytest.raises(ValueError, match="tuple"):
        MovieDetailsSnapshot(movie=_movie(), media_files=[])  # type: ignore[arg-type]


def test_details_snapshot_rejects_media_file_from_another_movie() -> None:
    with pytest.raises(ValueError, match="belong"):
        MovieDetailsSnapshot(movie=_movie(), media_files=(_media_file(movie_id=99),))
