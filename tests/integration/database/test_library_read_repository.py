from __future__ import annotations

from datetime import datetime, timedelta, timezone
from pathlib import Path
import sqlite3

import pytest

from dropsort.database.repositories import (
    MediaFileRepository,
    SqliteMovieLibraryReadRepository,
    SqliteMovieRepository,
)
from dropsort.library.movies import (
    CatalogQueryError,
    MediaFileStatus,
    MovieCatalogData,
    VerifiedMediaFileFacts,
)


NOW = datetime(2026, 8, 11, 12, 0, tzinfo=timezone.utc)


def _movie_data(movie_id: int, **overrides: object) -> MovieCatalogData:
    values: dict[str, object] = {
        "provider": "tmdb",
        "external_id": str(movie_id),
        "title": f"Movie {movie_id}",
        "original_title": None,
        "year": None,
        "overview": None,
        "genres": (),
        "runtime_minutes": None,
        "rating": None,
        "poster_reference": None,
    }
    values.update(overrides)
    return MovieCatalogData(**values)  # type: ignore[arg-type]


def _create_movie(harness, movie_id: int, *, added: datetime = NOW, **overrides: object):
    return SqliteMovieRepository(harness.database).create(
        _movie_data(movie_id, **overrides),
        now=added,
    )


def _add_file(
    harness,
    movie_id: int,
    path: Path,
    *,
    resolution: str = "1080p",
    status: MediaFileStatus = MediaFileStatus.PRESENT,
) -> None:
    repository = MediaFileRepository(harness.database)
    created = repository.add(
        VerifiedMediaFileFacts(
            current_path=path.absolute(),
            file_size=1_000,
            extension=".mkv",
            resolution=resolution,
            codec="x264",
            source="BluRay",
            observed_at=NOW,
        ),
        movie_id,
    )
    if status is MediaFileStatus.MISSING:
        repository.mark_missing(created.id)


def test_list_movies_returns_empty_library(harness) -> None:
    assert SqliteMovieLibraryReadRepository(harness.database).list_movies(
        limit=100,
        offset=0,
    ) == ()


def test_list_movies_is_newest_first_with_id_tie_break_and_file_counts(
    harness,
    tmp_path: Path,
) -> None:
    oldest = _create_movie(harness, 1, added=NOW - timedelta(days=1))
    tied_first = _create_movie(harness, 2, added=NOW)
    tied_second = _create_movie(harness, 3, added=NOW)
    _add_file(harness, oldest.id, tmp_path / "old-1.mkv")
    _add_file(harness, oldest.id, tmp_path / "old-2.mkv")
    _add_file(harness, tied_first.id, tmp_path / "new.mkv")

    result = SqliteMovieLibraryReadRepository(harness.database).list_movies(
        limit=100,
        offset=0,
    )

    assert [item.movie.id for item in result] == [tied_second.id, tied_first.id, oldest.id]
    assert [item.media_file_count for item in result] == [0, 1, 2]


def test_list_movies_aggregates_missing_counts_in_the_same_summary_query(
    harness,
    tmp_path: Path,
) -> None:
    movie = _create_movie(harness, 10)
    _add_file(harness, movie.id, tmp_path / "present.mkv")
    _add_file(
        harness,
        movie.id,
        tmp_path / "missing.mkv",
        status=MediaFileStatus.MISSING,
    )

    summary = SqliteMovieLibraryReadRepository(harness.database).list_movies(
        limit=10,
        offset=0,
    )[0]

    assert summary.media_file_count == 2
    assert summary.missing_file_count == 1


def test_list_movies_paginates_over_stable_default_order(harness) -> None:
    for movie_id in range(1, 6):
        _create_movie(harness, movie_id, added=NOW + timedelta(minutes=movie_id))
    repository = SqliteMovieLibraryReadRepository(harness.database)

    first_page = repository.list_movies(limit=2, offset=0)
    second_page = repository.list_movies(limit=2, offset=2)

    assert [item.movie.external_id for item in first_page] == ["5", "4"]
    assert [item.movie.external_id for item in second_page] == ["3", "2"]


def test_get_details_returns_optional_metadata_and_zero_files(harness) -> None:
    movie = _create_movie(
        harness,
        1,
        genres=("Drama", "Science Fiction"),
        overview=None,
        rating=None,
    )

    result = SqliteMovieLibraryReadRepository(harness.database).get_movie_details(movie.id)

    assert result is not None
    assert result.movie.genres == ("Drama", "Science Fiction")
    assert result.movie.overview is None
    assert result.media_files == ()


def test_get_details_returns_multiple_qualities_and_controlled_statuses(
    harness,
    tmp_path: Path,
) -> None:
    movie = _create_movie(harness, 1)
    _add_file(harness, movie.id, tmp_path / "1080p.mkv")
    _add_file(
        harness,
        movie.id,
        tmp_path / "2160p.mkv",
        resolution="2160p",
        status=MediaFileStatus.MISSING,
    )

    result = SqliteMovieLibraryReadRepository(harness.database).get_movie_details(movie.id)

    assert result is not None
    assert [media.resolution for media in result.media_files] == ["1080p", "2160p"]
    assert [media.status for media in result.media_files] == [
        MediaFileStatus.PRESENT,
        MediaFileStatus.MISSING,
    ]


def test_get_details_unknown_movie_returns_none(harness) -> None:
    assert (
        SqliteMovieLibraryReadRepository(harness.database).get_movie_details(999) is None
    )


def test_list_uses_one_aggregate_select_instead_of_per_movie_queries(harness) -> None:
    for movie_id in range(1, 11):
        _create_movie(harness, movie_id)
    statements: list[str] = []

    with harness.database.connection() as connection:
        connection.set_trace_callback(statements.append)
        repository = SqliteMovieLibraryReadRepository(
            harness.database,
            connection=connection,
        )
        repository.list_movies(limit=100, offset=0)

    selects = [statement for statement in statements if statement.lstrip().upper().startswith("SELECT")]
    assert len(selects) == 1
    assert "COUNT(" in selects[0].upper()
    assert "LEFT JOIN" in selects[0].upper()


def test_details_uses_one_snapshot_connection_and_two_bounded_selects(
    harness,
    tmp_path: Path,
) -> None:
    movie = _create_movie(harness, 1)
    _add_file(harness, movie.id, tmp_path / "movie.mkv")
    statements: list[str] = []

    with harness.database.connection() as connection:
        connection.set_trace_callback(statements.append)
        repository = SqliteMovieLibraryReadRepository(
            harness.database,
            connection=connection,
        )
        repository.get_movie_details(movie.id)

    selects = [statement for statement in statements if statement.lstrip().upper().startswith("SELECT")]
    assert len(selects) == 2


def test_raw_sqlite_failures_are_translated_to_controlled_query_error(
    harness,
) -> None:
    connection = harness.database.connect()
    repository = SqliteMovieLibraryReadRepository(
        harness.database,
        connection=connection,
    )
    connection.close()

    with pytest.raises(CatalogQueryError, match="read movie library"):
        repository.list_movies(limit=10, offset=0)


def test_detail_sqlite_failure_is_translated_to_controlled_query_error(harness) -> None:
    connection = harness.database.connect()
    repository = SqliteMovieLibraryReadRepository(
        harness.database,
        connection=connection,
    )
    connection.close()

    with pytest.raises(CatalogQueryError, match="details"):
        repository.get_movie_details(1)


@pytest.mark.parametrize(
    ("limit", "offset", "message"),
    ((0, 0, "limit"), (10, -1, "offset")),
)
def test_repository_rejects_invalid_pagination(
    harness,
    limit: int,
    offset: int,
    message: str,
) -> None:
    repository = SqliteMovieLibraryReadRepository(harness.database)

    with pytest.raises(ValueError, match=message):
        repository.list_movies(limit=limit, offset=offset)


def test_repository_rejects_invalid_movie_id(harness) -> None:
    repository = SqliteMovieLibraryReadRepository(harness.database)

    with pytest.raises(ValueError, match="movie_id"):
        repository.get_movie_details(0)
