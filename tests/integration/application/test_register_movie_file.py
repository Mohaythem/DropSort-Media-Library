from __future__ import annotations

from datetime import datetime, timedelta, timezone
from pathlib import Path
import sqlite3

import pytest

from dropsort.application.dto import RegisterMovieFileCommand
from dropsort.application.use_cases import RegisterMovieFile
from dropsort.database.repositories import (
    MediaFileRepository,
    SqliteCatalogUnitOfWork,
    SqliteMovieRepository,
)
from dropsort.library.movies import MediaFileAssociationConflict
from dropsort.media.parser import MediaType, ParsedMedia
from dropsort.metadata.contracts import MovieMetadata


NOW = datetime(2026, 8, 11, 12, 0, tzinfo=timezone.utc)


def _metadata(
    *,
    external_id: str = "155",
    title: str = "The Dark Knight",
    overview: str | None = "Original overview",
    rating: float | None = 8.5,
) -> MovieMetadata:
    return MovieMetadata(
        provider="tmdb",
        external_id=external_id,
        title=title,
        original_title=title,
        year=2008,
        overview=overview,
        genres=("Drama", "Action"),
        runtime_minutes=152,
        rating=rating,
        director="Christopher Nolan",
        cast=("Christian Bale",),
        poster_reference="/poster.jpg",
    )


def _parsed(
    *,
    resolution: str = "1080p",
    codec: str = "x264",
) -> ParsedMedia:
    return ParsedMedia(
        original_name="The.Dark.Knight.2008.1080p.mkv",
        media_type=MediaType.MOVIE,
        title="The Dark Knight",
        year=2008,
        resolution=resolution,
        source="BluRay",
        codec=codec,
        extension=".mkv",
    )


def _command(
    path: Path,
    *,
    metadata: MovieMetadata | None = None,
    parsed: ParsedMedia | None = None,
    file_size: int = 123,
    observed_at: datetime = NOW,
) -> RegisterMovieFileCommand:
    return RegisterMovieFileCommand(
        metadata=metadata or _metadata(),
        parsed_media=parsed or _parsed(),
        file_path=path.absolute(),
        file_size=file_size,
        observed_at=observed_at,
    )


def _use_case(harness, now: datetime = NOW) -> RegisterMovieFile:
    return RegisterMovieFile(
        lambda: SqliteCatalogUnitOfWork(harness.database),
        now=lambda: now,
    )


def _counts(harness) -> tuple[int, int]:
    with harness.database.connection() as conn:
        movie_count = conn.execute("SELECT COUNT(*) AS n FROM movies").fetchone()["n"]
        file_count = conn.execute("SELECT COUNT(*) AS n FROM media_files").fetchone()["n"]
    return movie_count, file_count


def test_new_movie_and_file_are_ingested_in_one_catalog_transaction(
    harness,
    tmp_path: Path,
) -> None:
    result = _use_case(harness).execute(_command(tmp_path / "Movie.mkv"))

    assert result.movie.provider == "tmdb"
    assert result.movie.external_id == "155"
    assert result.media_file.movie_id == result.movie.id
    assert _counts(harness) == (1, 1)


def test_repeated_identical_ingestion_is_idempotent(harness, tmp_path: Path) -> None:
    command = _command(tmp_path / "Movie.mkv")
    use_case = _use_case(harness)

    first = use_case.execute(command)
    second = use_case.execute(command)

    assert second.movie.id == first.movie.id
    assert second.media_file.id == first.media_file.id
    assert _counts(harness) == (1, 1)


def test_repeated_scan_refreshes_verified_facts_without_changing_path(
    harness,
    tmp_path: Path,
) -> None:
    original_path = (tmp_path / "Movie.MKV").absolute()
    first = _use_case(harness).execute(_command(original_path))
    later = NOW + timedelta(days=1)
    alias_command = _command(
        original_path.with_name("movie.mkv"),
        parsed=_parsed(resolution="2160p", codec="x265"),
        file_size=999,
        observed_at=later,
    )

    refreshed = _use_case(harness, later).execute(alias_command)

    assert refreshed.media_file.id == first.media_file.id
    assert refreshed.media_file.current_path == original_path
    assert refreshed.media_file.file_size == 999
    assert refreshed.media_file.resolution == "2160p"
    assert refreshed.media_file.last_seen_at == later
    assert _counts(harness) == (1, 1)


def test_same_movie_can_ingest_second_physical_quality(harness, tmp_path: Path) -> None:
    use_case = _use_case(harness)
    first = use_case.execute(_command(tmp_path / "Movie.1080p.mkv"))
    second = use_case.execute(
        _command(
            tmp_path / "Movie.2160p.mkv",
            parsed=_parsed(resolution="2160p", codec="x265"),
            file_size=456,
        )
    )

    assert first.movie.id == second.movie.id
    assert first.media_file.id != second.media_file.id
    assert _counts(harness) == (1, 2)


def test_existing_unassigned_media_row_is_linked_without_path_change(
    harness,
    tmp_path: Path,
) -> None:
    path = (tmp_path / "Existing.mkv").absolute()
    media_file_id = MediaFileRepository(harness.database).create(path, 123)

    result = _use_case(harness).execute(_command(path))

    assert result.media_file.id == media_file_id
    assert result.media_file.movie_id == result.movie.id
    assert result.media_file.current_path == path


def test_same_file_different_movie_is_conflict_and_new_movie_rolls_back(
    harness,
    tmp_path: Path,
) -> None:
    path = tmp_path / "Movie.mkv"
    first = _use_case(harness).execute(_command(path))
    conflicting = _command(
        path,
        metadata=_metadata(external_id="999", title="Different Movie"),
    )

    with pytest.raises(MediaFileAssociationConflict):
        _use_case(harness).execute(conflicting)

    assert _counts(harness) == (1, 1)
    assert SqliteMovieRepository(harness.database).get_by_external_id("tmdb", "999") is None
    stored = MediaFileRepository(harness.database).get_by_id(first.media_file.id)
    assert stored is not None
    assert stored.movie_id == first.movie.id


def test_media_insert_database_failure_rolls_back_new_movie(
    harness,
    tmp_path: Path,
    monkeypatch,
) -> None:
    def fail_add(*args, **kwargs):
        raise sqlite3.OperationalError("simulated media insert failure")

    monkeypatch.setattr(MediaFileRepository, "add", fail_add)

    with pytest.raises(sqlite3.OperationalError, match="simulated media insert failure"):
        _use_case(harness).execute(_command(tmp_path / "Movie.mkv"))

    assert _counts(harness) == (0, 0)


def test_metadata_refresh_updates_movie_without_mutating_file_path(
    harness,
    tmp_path: Path,
) -> None:
    path = (tmp_path / "Movie.mkv").absolute()
    first = _use_case(harness).execute(_command(path))
    updated_metadata = _metadata(
        title="The Dark Knight Updated",
        overview=None,
        rating=None,
    )

    refreshed = _use_case(harness, NOW + timedelta(hours=1)).execute(
        _command(path, metadata=updated_metadata)
    )

    assert refreshed.movie.id == first.movie.id
    assert refreshed.movie.title == "The Dark Knight Updated"
    assert refreshed.movie.data.overview is None
    assert refreshed.movie.data.rating is None
    assert refreshed.media_file.current_path == first.media_file.current_path


def test_ingestion_persists_missing_optional_metadata_as_null(
    harness,
    tmp_path: Path,
) -> None:
    metadata = MovieMetadata(
        provider="tmdb",
        external_id="empty-optionals",
        title="Movie",
        original_title=None,
        year=None,
        overview=None,
        genres=(),
        runtime_minutes=None,
        rating=None,
        director=None,
        cast=(),
        poster_reference=None,
    )

    result = _use_case(harness).execute(
        _command(tmp_path / "Movie.mkv", metadata=metadata)
    )

    assert result.movie.data.original_title is None
    assert result.movie.data.year is None
    assert result.movie.data.overview is None
    assert result.movie.data.genres == ()
    assert result.movie.data.runtime_minutes is None
    assert result.movie.data.rating is None
    assert result.movie.data.poster_reference is None


def test_invalid_or_non_movie_registration_is_rejected_without_database_writes(
    harness,
    tmp_path: Path,
) -> None:
    tv = ParsedMedia(
        "Show.S01E01.mkv",
        MediaType.TV_EPISODE,
        "Show",
        None,
        None,
        None,
        None,
        ".mkv",
    )

    with pytest.raises(ValueError, match="MOVIE"):
        _command(tmp_path / "Show.mkv", parsed=tv)
    with pytest.raises(ValueError, match="absolute"):
        RegisterMovieFileCommand(_metadata(), _parsed(), Path("relative.mkv"), 123, NOW)

    assert _counts(harness) == (0, 0)


def test_naive_catalog_clock_fails_before_transaction(harness, tmp_path: Path) -> None:
    use_case = RegisterMovieFile(
        lambda: SqliteCatalogUnitOfWork(harness.database),
        now=lambda: datetime(2026, 8, 11, 12, 0),
    )

    with pytest.raises(ValueError, match="timezone-aware"):
        use_case.execute(_command(tmp_path / "Movie.mkv"))

    assert _counts(harness) == (0, 0)
