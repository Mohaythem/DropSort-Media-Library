from __future__ import annotations

from datetime import datetime, timezone

from dropsort.application.use_cases import GetMovieListItem
from dropsort.database.repositories import (
    MediaFileRepository,
    SqliteMovieLibraryReadRepository,
    SqliteMovieRepository,
)
from dropsort.library.movies import MovieCatalogData, VerifiedMediaFileFacts


NOW = datetime(2026, 8, 23, 12, 30, tzinfo=timezone.utc)


def test_single_movie_projection_reads_missing_count_without_listing_library(
    harness,
    tmp_path,
    monkeypatch,
) -> None:
    movie = SqliteMovieRepository(harness.database).create(
        MovieCatalogData(
            "tmdb",
            "single-item-1",
            "Single Item",
            None,
            2026,
            None,
            (),
            None,
            None,
            None,
        ),
        now=NOW,
    )
    media_files = MediaFileRepository(harness.database)
    media_file = media_files.add(
        VerifiedMediaFileFacts(
            (tmp_path / "missing.mkv").absolute(),
            10,
            ".mkv",
            "1080p",
            "x264",
            "BluRay",
            NOW,
        ),
        movie.id,
    )
    media_files.mark_missing(media_file.id)
    repository = SqliteMovieLibraryReadRepository(harness.database)
    monkeypatch.setattr(
        repository,
        "list_movies",
        lambda **_kwargs: (_ for _ in ()).throw(
            AssertionError("single-item refresh must not list the Library")
        ),
    )

    item = GetMovieListItem(repository).execute(movie.id)

    assert item.movie_id == movie.id
    assert item.media_file_count == 1
    assert item.missing_file_count == 1
    assert item.all_files_missing is True
