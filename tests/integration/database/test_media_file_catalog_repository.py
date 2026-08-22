from __future__ import annotations

from datetime import datetime, timedelta, timezone
from pathlib import Path

import pytest

from dropsort.database.repositories import MediaFileRepository, SqliteMovieRepository
from dropsort.library.movies import (
    CatalogIntegrityError,
    CatalogRecordNotFoundError,
    MediaFileAssociationConflict,
    MediaFilePathConflictError,
    MediaFileStatus,
    MovieCatalogData,
    VerifiedMediaFileFacts,
)


NOW = datetime(2026, 8, 11, 12, 0, tzinfo=timezone.utc)


def _movie_id(harness, external_id: str = "155") -> int:
    data = MovieCatalogData(
        provider="tmdb",
        external_id=external_id,
        title=f"Movie {external_id}",
        original_title=None,
        year=2024,
        overview=None,
        genres=(),
        runtime_minutes=None,
        rating=None,
        poster_reference=None,
    )
    return SqliteMovieRepository(harness.database).create(data, now=NOW).id


def _facts(path: Path, **overrides: object) -> VerifiedMediaFileFacts:
    values: dict[str, object] = {
        "current_path": path.absolute(),
        "file_size": 123,
        "extension": ".mkv",
        "resolution": "1080p",
        "codec": "x264",
        "source": "BluRay",
        "observed_at": NOW,
    }
    values.update(overrides)
    return VerifiedMediaFileFacts(**values)  # type: ignore[arg-type]


def test_add_get_and_list_media_file_for_movie(harness, tmp_path: Path) -> None:
    repository = MediaFileRepository(harness.database)
    movie_id = _movie_id(harness)
    facts = _facts(tmp_path / "Movie.mkv")

    created = repository.add(facts, movie_id)

    assert repository.get_by_id(created.id) == created
    assert repository.get_by_path(facts.current_path) == created
    assert repository.list_for_movie(movie_id) == (created,)
    assert created.status is MediaFileStatus.PRESENT


def test_windows_case_alias_cannot_create_duplicate_media_file(
    harness,
    tmp_path: Path,
) -> None:
    repository = MediaFileRepository(harness.database)
    movie_id = _movie_id(harness)
    first_path = (tmp_path / "Movie.MKV").absolute()
    alias_path = first_path.with_name("movie.mkv")
    created = repository.add(_facts(first_path), movie_id)

    assert repository.get_by_path(alias_path) == created
    with pytest.raises(MediaFilePathConflictError):
        repository.add(_facts(alias_path), movie_id)

    assert repository.list_for_movie(movie_id) == (created,)


def test_same_movie_supports_multiple_physical_files(harness, tmp_path: Path) -> None:
    repository = MediaFileRepository(harness.database)
    movie_id = _movie_id(harness)

    first = repository.add(_facts(tmp_path / "Movie.1080p.mkv"), movie_id)
    second = repository.add(
        _facts(
            tmp_path / "Movie.2160p.mkv",
            file_size=456,
            resolution="2160p",
            codec="x265",
        ),
        movie_id,
    )

    assert first.id != second.id
    assert repository.list_for_movie(movie_id) == (first, second)


def test_unassigned_phase1_record_can_be_linked_explicitly(harness, tmp_path: Path) -> None:
    repository = MediaFileRepository(harness.database)
    movie_id = _movie_id(harness)
    path = (tmp_path / "Unassigned.mkv").absolute()
    media_file_id = repository.create(path, 123)

    linked = repository.link_to_movie(media_file_id, movie_id)

    assert linked.movie_id == movie_id
    assert linked.current_path == path


def test_same_physical_file_cannot_silently_switch_movies(harness, tmp_path: Path) -> None:
    repository = MediaFileRepository(harness.database)
    first_movie_id = _movie_id(harness, "1")
    second_movie_id = _movie_id(harness, "2")
    media_file = repository.add(_facts(tmp_path / "Movie.mkv"), first_movie_id)

    with pytest.raises(MediaFileAssociationConflict):
        repository.link_to_movie(media_file.id, second_movie_id)

    stored = repository.get_by_id(media_file.id)
    assert stored is not None
    assert stored.movie_id == first_movie_id


def test_refresh_updates_verified_facts_but_never_current_path_or_association(
    harness,
    tmp_path: Path,
) -> None:
    repository = MediaFileRepository(harness.database)
    movie_id = _movie_id(harness)
    original_path = (tmp_path / "Movie.MKV").absolute()
    created = repository.add(_facts(original_path), movie_id)
    later = NOW + timedelta(days=1)
    alias_facts = _facts(
        original_path.with_name("movie.mkv"),
        file_size=999,
        resolution="2160p",
        codec="x265",
        source="WEB-DL",
        observed_at=later,
    )

    refreshed = repository.refresh_verified_facts(created.id, alias_facts)

    assert refreshed.current_path == original_path
    assert refreshed.movie_id == movie_id
    assert refreshed.file_size == 999
    assert refreshed.resolution == "2160p"
    assert refreshed.last_seen_at == later


def test_refresh_rejects_facts_for_a_different_physical_path(
    harness,
    tmp_path: Path,
) -> None:
    repository = MediaFileRepository(harness.database)
    media_file = repository.add(_facts(tmp_path / "Movie.mkv"), _movie_id(harness))

    with pytest.raises(MediaFilePathConflictError, match="different path"):
        repository.refresh_verified_facts(
            media_file.id,
            _facts(tmp_path / "Other.mkv"),
        )

    assert repository.get_by_id(media_file.id) == media_file


def test_present_missing_state_round_trips_without_filesystem_access(
    harness,
    tmp_path: Path,
) -> None:
    repository = MediaFileRepository(harness.database)
    media_file = repository.add(_facts(tmp_path / "Movie.mkv"), _movie_id(harness))

    missing = repository.mark_missing(media_file.id)
    present = repository.mark_present(media_file.id, observed_at=NOW + timedelta(hours=1))

    assert missing.status is MediaFileStatus.MISSING
    assert present.status is MediaFileStatus.PRESENT
    assert present.last_seen_at == NOW + timedelta(hours=1)


def test_missing_media_file_mutations_are_controlled(harness, tmp_path: Path) -> None:
    repository = MediaFileRepository(harness.database)
    facts = _facts(tmp_path / "Movie.mkv")

    assert repository.get_by_id(999) is None
    assert repository.get_by_path(facts.current_path) is None
    with pytest.raises(CatalogRecordNotFoundError):
        repository.refresh_verified_facts(999, facts)
    with pytest.raises(CatalogRecordNotFoundError):
        repository.mark_missing(999)
    with pytest.raises(CatalogRecordNotFoundError):
        repository.mark_present(999, observed_at=NOW)


def test_invalid_movie_foreign_key_is_a_controlled_integrity_error(
    harness,
    tmp_path: Path,
) -> None:
    repository = MediaFileRepository(harness.database)

    with pytest.raises(CatalogIntegrityError):
        repository.add(_facts(tmp_path / "Movie.mkv"), 999)

    media_file_id = repository.create((tmp_path / "Unassigned.mkv").absolute(), 1)
    with pytest.raises(CatalogIntegrityError):
        repository.link_to_movie(media_file_id, 999)

    assert repository.get_by_id(media_file_id).movie_id is None  # type: ignore[union-attr]
