from __future__ import annotations

from datetime import datetime, timedelta, timezone

import pytest

from dropsort.database.repositories import SqliteMovieRepository
from dropsort.library.movies import (
    CatalogDataError,
    CatalogRecordNotFoundError,
    MovieCatalogData,
    MovieIdentityConflictError,
)


NOW = datetime(2026, 8, 11, 12, 0, tzinfo=timezone.utc)


def _data(**overrides: object) -> MovieCatalogData:
    values: dict[str, object] = {
        "provider": "tmdb",
        "external_id": "155",
        "title": "The Dark Knight",
        "original_title": "The Dark Knight",
        "year": 2008,
        "overview": "Original overview",
        "genres": ("Drama", "Action"),
        "runtime_minutes": 152,
        "rating": 8.5,
        "poster_reference": "/original.jpg",
    }
    values.update(overrides)
    return MovieCatalogData(**values)  # type: ignore[arg-type]


def test_create_and_read_movie_without_leaking_sqlite_rows(harness) -> None:
    repository = SqliteMovieRepository(harness.database)

    created = repository.create(_data(), now=NOW)

    assert created.id > 0
    assert repository.get_by_id(created.id) == created
    assert repository.get_by_external_id("tmdb", "155") == created
    assert repository.list_all() == (created,)


def test_count_and_keyset_page_are_bounded_and_validate_inputs(harness) -> None:
    repository = SqliteMovieRepository(harness.database)
    first = repository.create(_data(external_id="1"), now=NOW)
    second = repository.create(_data(external_id="2"), now=NOW)

    assert repository.count_all() == 2
    assert repository.list_page(after_id=0, limit=1) == (first,)
    assert repository.list_page(after_id=first.id, limit=10) == (second,)

    with pytest.raises(ValueError, match="after_id"):
        repository.list_page(after_id=-1, limit=1)
    with pytest.raises(ValueError, match="after_id"):
        repository.list_page(after_id=True, limit=1)
    with pytest.raises(ValueError, match="limit"):
        repository.list_page(after_id=0, limit=0)
    with pytest.raises(ValueError, match="limit"):
        repository.list_page(after_id=0, limit=True)


def test_movie_repository_supports_a_shared_connection_boundary(harness) -> None:
    with harness.database.connection() as connection:
        repository = SqliteMovieRepository(harness.database, connection=connection)
        created = repository.create(_data(external_id="shared"), now=NOW)

        assert repository.get_by_id(created.id) == created
        assert repository.count_all() == 1
        assert repository.list_page(after_id=0, limit=5) == (created,)
        refreshed = repository.update_metadata(
            created.id,
            _data(external_id="shared", overview="Shared connection update"),
            now=NOW,
        )
        assert refreshed.overview == "Shared connection update"


def test_count_all_handles_an_empty_repository_fetch_result(harness, monkeypatch) -> None:
    repository = SqliteMovieRepository(harness.database)
    monkeypatch.setattr(repository, "_fetchone", lambda *_args: None)

    assert repository.count_all() == 0


def test_same_title_and_year_with_different_provider_identity_are_distinct(harness) -> None:
    repository = SqliteMovieRepository(harness.database)
    first = repository.create(_data(), now=NOW)
    second = repository.create(
        _data(provider="provider-b", external_id="155"),
        now=NOW,
    )

    assert first.id != second.id
    assert len(repository.list_all()) == 2


def test_duplicate_provider_external_id_is_a_controlled_conflict(harness) -> None:
    repository = SqliteMovieRepository(harness.database)
    repository.create(_data(), now=NOW)

    with pytest.raises(MovieIdentityConflictError):
        repository.create(_data(title="A Different Title"), now=NOW)

    assert len(repository.list_all()) == 1


def test_metadata_refresh_updates_descriptive_fields_and_preserves_identity_dates(
    harness,
) -> None:
    repository = SqliteMovieRepository(harness.database)
    created = repository.create(_data(), now=NOW)
    later = NOW + timedelta(days=1)
    refreshed_data = _data(
        title="The Dark Knight — Updated",
        overview="Updated overview",
        genres=("Crime", "Drama"),
        runtime_minutes=153,
        rating=8.6,
        poster_reference="/updated.jpg",
    )

    refreshed = repository.update_metadata(created.id, refreshed_data, now=later)

    assert refreshed.id == created.id
    assert refreshed.provider == created.provider
    assert refreshed.external_id == created.external_id
    assert refreshed.title == "The Dark Knight — Updated"
    assert refreshed.data.genres == ("Crime", "Drama")
    assert refreshed.date_added == created.date_added
    assert refreshed.created_at == created.created_at
    assert refreshed.updated_at == later


def test_metadata_refresh_cannot_change_movie_provider_identity(harness) -> None:
    repository = SqliteMovieRepository(harness.database)
    created = repository.create(_data(), now=NOW)

    with pytest.raises(MovieIdentityConflictError):
        repository.update_metadata(
            created.id,
            _data(external_id="different"),
            now=NOW + timedelta(hours=1),
        )


def test_optional_metadata_and_unicode_genres_round_trip(harness) -> None:
    repository = SqliteMovieRepository(harness.database)
    data = _data(
        original_title=None,
        year=None,
        overview=None,
        genres=("دراما", "Science Fiction"),
        runtime_minutes=None,
        rating=None,
        poster_reference=None,
    )

    created = repository.create(data, now=NOW)

    assert repository.get_by_id(created.id).data == data  # type: ignore[union-attr]


def test_missing_movie_reads_none_and_update_is_controlled(harness) -> None:
    repository = SqliteMovieRepository(harness.database)

    assert repository.get_by_id(999) is None
    assert repository.get_by_external_id("tmdb", "missing") is None
    with pytest.raises(CatalogRecordNotFoundError):
        repository.update_metadata(999, _data(), now=NOW)


def test_malformed_genres_json_is_a_controlled_catalog_data_error(harness) -> None:
    with harness.database.transaction() as conn:
        cursor = conn.execute(
            """
            INSERT INTO movies(
                provider, external_id, title, genres, date_added, created_at, updated_at
            ) VALUES ('tmdb', 'bad', 'Movie', '{bad', ?, ?, ?)
            """,
            (NOW.isoformat(), NOW.isoformat(), NOW.isoformat()),
        )
        movie_id = int(cursor.lastrowid)

    with pytest.raises(CatalogDataError, match="genres"):
        SqliteMovieRepository(harness.database).get_by_id(movie_id)
