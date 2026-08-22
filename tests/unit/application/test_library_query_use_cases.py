from __future__ import annotations

from datetime import datetime, timezone
from pathlib import Path

import pytest

from dropsort.application.dto.library import (
    MediaFileAvailability,
    MovieListQuery,
)
from dropsort.application.errors import LibraryQueryError, MovieNotFoundError
from dropsort.application.use_cases import (
    GetMovieDetails,
    ListMovies,
)
from dropsort.library.movies import (
    CatalogQueryError,
    MediaFile,
    MediaFileStatus,
    Movie,
    MovieCatalogData,
    MovieDetailsSnapshot,
    MovieSummary,
)


NOW = datetime(2026, 8, 11, 12, 0, tzinfo=timezone.utc)


def _movie(movie_id: int = 1) -> Movie:
    return Movie(
        id=movie_id,
        data=MovieCatalogData(
            provider="tmdb",
            external_id=str(movie_id),
            title="The Dark Knight",
            original_title="The Dark Knight",
            year=2008,
            overview="Overview",
            genres=("Drama", "Action"),
            runtime_minutes=152,
            rating=8.5,
            poster_reference="/poster.jpg",
        ),
        date_added=NOW,
        created_at=NOW,
        updated_at=NOW,
    )


def _media_file(movie_id: int = 1) -> MediaFile:
    return MediaFile(
        id=5,
        movie_id=movie_id,
        current_path=Path(r"D:\Movies\The Dark Knight.mkv"),
        file_size=1_234,
        extension=".mkv",
        resolution="1080p",
        codec="x264",
        source="BluRay",
        status=MediaFileStatus.PRESENT,
        discovered_at=NOW,
        last_seen_at=NOW,
    )


class FakeReadRepository:
    def __init__(self) -> None:
        self.summary = MovieSummary(movie=_movie(), media_file_count=2)
        self.details = MovieDetailsSnapshot(movie=_movie(), media_files=(_media_file(),))
        self.calls: list[tuple[object, ...]] = []
        self.error: Exception | None = None

    def list_movies(self, *, limit: int, offset: int) -> tuple[MovieSummary, ...]:
        self.calls.append(("list", limit, offset))
        if self.error is not None:
            raise self.error
        return (self.summary,)

    def get_movie_details(self, movie_id: int) -> MovieDetailsSnapshot | None:
        self.calls.append(("details", movie_id))
        if self.error is not None:
            raise self.error
        return self.details if movie_id == 1 else None


def test_list_movies_maps_domain_summary_to_presentation_item() -> None:
    repository = FakeReadRepository()

    result = ListMovies(repository).execute(MovieListQuery(limit=25, offset=10))

    assert repository.calls == [("list", 25, 10)]
    assert len(result) == 1
    assert result[0].movie_id == 1
    assert result[0].media_file_count == 2
    assert result[0].poster_reference == "/poster.jpg"


def test_get_movie_details_maps_movie_files_without_touching_paths() -> None:
    repository = FakeReadRepository()

    result = GetMovieDetails(repository).execute(1)

    assert result.genres == ("Drama", "Action")
    assert result.media_files[0].current_path == r"D:\Movies\The Dark Knight.mkv"
    assert result.media_files[0].status is MediaFileAvailability.PRESENT


def test_get_movie_details_rejects_invalid_or_unknown_id() -> None:
    use_case = GetMovieDetails(FakeReadRepository())

    for invalid_id in (0, -1, True):
        with pytest.raises(ValueError, match="movie_id"):
            use_case.execute(invalid_id)  # type: ignore[arg-type]
    with pytest.raises(MovieNotFoundError, match="999"):
        use_case.execute(999)


@pytest.mark.parametrize("use_case", ("list", "details"))
def test_controlled_catalog_query_failures_are_translated_at_application_boundary(
    use_case: str,
) -> None:
    repository = FakeReadRepository()
    repository.error = CatalogQueryError("database unavailable")

    with pytest.raises(LibraryQueryError, match="local movie library"):
        if use_case == "list":
            ListMovies(repository).execute()
        else:
            GetMovieDetails(repository).execute(1)
