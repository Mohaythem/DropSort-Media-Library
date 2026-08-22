from __future__ import annotations

from datetime import datetime, timedelta, timezone
from pathlib import Path

from dropsort.application.dto.library import MovieListQuery
from dropsort.application.use_cases import GetMovieDetails, ListMovies
from dropsort.database.repositories import (
    MediaFileRepository,
    SqliteMovieLibraryReadRepository,
    SqliteMovieRepository,
)
from dropsort.library.movies import MediaFileStatus, MovieCatalogData, VerifiedMediaFileFacts


NOW = datetime(2026, 8, 11, 12, 0, tzinfo=timezone.utc)


def _create_catalog(harness, tmp_path: Path) -> tuple[int, int]:
    movies = SqliteMovieRepository(harness.database)
    files = MediaFileRepository(harness.database)
    first = movies.create(
        MovieCatalogData(
            provider="tmdb",
            external_id="155",
            title="The Dark Knight",
            original_title="The Dark Knight",
            year=2008,
            overview="Overview",
            genres=("Drama", "Action"),
            runtime_minutes=152,
            rating=8.5,
            poster_reference="/poster.jpg",
        ),
        now=NOW,
    )
    second = movies.create(
        MovieCatalogData(
            provider="tmdb",
            external_id="157336",
            title="Interstellar",
            original_title=None,
            year=2014,
            overview=None,
            genres=(),
            runtime_minutes=None,
            rating=None,
            poster_reference=None,
        ),
        now=NOW + timedelta(days=1),
    )
    media = files.add(
        VerifiedMediaFileFacts(
            current_path=(tmp_path / "The Dark Knight.mkv").absolute(),
            file_size=1_234,
            extension=".mkv",
            resolution="1080p",
            codec="x264",
            source="BluRay",
            observed_at=NOW,
        ),
        first.id,
    )
    files.mark_missing(media.id)
    return first.id, second.id


def _database_snapshot(harness) -> tuple[tuple[object, ...], tuple[object, ...]]:
    with harness.database.connection() as connection:
        movies = tuple(
            tuple(row)
            for row in connection.execute("SELECT * FROM movies ORDER BY id").fetchall()
        )
        files = tuple(
            tuple(row)
            for row in connection.execute("SELECT * FROM media_files ORDER BY id").fetchall()
        )
    return movies, files


def test_query_use_cases_return_local_presentation_dtos_without_catalog_mutation(
    harness,
    tmp_path: Path,
) -> None:
    first_id, second_id = _create_catalog(harness, tmp_path)
    before = _database_snapshot(harness)
    repository = SqliteMovieLibraryReadRepository(harness.database)

    listed = ListMovies(repository).execute(MovieListQuery(limit=10))
    details = GetMovieDetails(repository).execute(first_id)

    assert [item.movie_id for item in listed] == [second_id, first_id]
    assert details.media_files[0].status.value == MediaFileStatus.MISSING.value
    assert details.media_files[0].current_path.endswith("The Dark Knight.mkv")
    assert _database_snapshot(harness) == before
