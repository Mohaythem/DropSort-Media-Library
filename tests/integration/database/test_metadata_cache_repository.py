from datetime import datetime, timedelta, timezone

import pytest

from dropsort.database.repositories import MetadataCacheRepository
from dropsort.metadata.cache import CacheRecord, CachedMetadataProvider
from dropsort.metadata.contracts import MovieCandidate, MovieMetadata, MovieSearchQuery


class FakeSqliteCachedProvider:
    provider_name = "provider-a"

    def __init__(self) -> None:
        self.search_calls = 0

    def search_movies(self, query: MovieSearchQuery) -> tuple[MovieCandidate, ...]:
        self.search_calls += 1
        return (
            MovieCandidate(
                provider=self.provider_name,
                external_id="1",
                title=query.title,
                original_title=None,
                year=query.year,
                overview=None,
                rating=None,
                poster_reference=None,
            ),
        )

    def get_movie(self, external_id: str) -> MovieMetadata:
        raise AssertionError("not used in this integration test")


def test_metadata_cache_repository_round_trips_and_upserts_json(harness) -> None:
    repository = MetadataCacheRepository(harness.database)
    now = datetime(2026, 8, 11, 12, 0, tzinfo=timezone.utc)
    first = CacheRecord(
        provider="provider-a",
        cache_key="search:movie:",
        payload='{"version":1}',
        fetched_at=now,
        expires_at=now + timedelta(days=1),
    )
    updated = CacheRecord(
        provider="provider-a",
        cache_key="search:movie:",
        payload='{"version":2}',
        fetched_at=now + timedelta(hours=1),
        expires_at=now + timedelta(days=2),
    )

    repository.put(first)
    repository.put(updated)

    assert repository.get("provider-a", "search:movie:") == updated
    with harness.database.connection() as conn:
        count = conn.execute(
            "SELECT COUNT(*) AS count FROM metadata_cache WHERE provider = ? AND cache_key = ?",
            ("provider-a", "search:movie:"),
        ).fetchone()["count"]
    assert count == 1


def test_metadata_cache_repository_scopes_same_key_by_provider(harness) -> None:
    repository = MetadataCacheRepository(harness.database)
    now = datetime(2026, 8, 11, 12, 0, tzinfo=timezone.utc)
    for provider in ("provider-a", "provider-b"):
        repository.put(
            CacheRecord(
                provider=provider,
                cache_key="movie:1",
                payload=f'{{"provider":"{provider}"}}',
                fetched_at=now,
                expires_at=now + timedelta(days=1),
            )
        )

    first = repository.get("provider-a", "movie:1")
    second = repository.get("provider-b", "movie:1")

    assert first is not None
    assert second is not None
    assert first.payload != second.payload


def test_metadata_cache_repository_treats_malformed_timestamps_as_invalid(harness) -> None:
    repository = MetadataCacheRepository(harness.database)
    with harness.database.transaction() as conn:
        conn.execute(
            """
            INSERT INTO metadata_cache(provider, cache_key, payload, fetched_at, expires_at)
            VALUES ('provider-a', 'movie:bad', '{}', 'not-a-date', 'also-not-a-date')
            """
        )

    assert repository.get("provider-a", "movie:bad") is None


def test_new_cache_service_instance_reads_normalized_search_from_sqlite(harness) -> None:
    repository = MetadataCacheRepository(harness.database)
    now = datetime(2026, 8, 11, 12, 0, tzinfo=timezone.utc)
    query = MovieSearchQuery("The Movie", 2024)
    online_provider = FakeSqliteCachedProvider()
    first_service = CachedMetadataProvider(online_provider, repository, now=lambda: now)
    expected = first_service.search_movies(query)

    offline_provider = FakeSqliteCachedProvider()
    second_service = CachedMetadataProvider(offline_provider, repository, now=lambda: now)
    actual = second_service.search_movies(query)

    assert actual == expected
    assert online_provider.search_calls == 1
    assert offline_provider.search_calls == 0


def test_repository_connection_override_and_naive_timestamp_guard(harness) -> None:
    repository = MetadataCacheRepository(harness.database)
    now = datetime(2026, 8, 11, 12, 0, tzinfo=timezone.utc)
    record = CacheRecord(
        provider="provider-a",
        cache_key="movie:1",
        payload="{}",
        fetched_at=now,
        expires_at=now + timedelta(days=1),
    )
    with harness.database.transaction() as conn:
        repository.put(record, conn=conn)
        assert repository.get("provider-a", "movie:1", conn=conn) == record

    naive = CacheRecord(
        provider="provider-a",
        cache_key="movie:2",
        payload="{}",
        fetched_at=datetime(2026, 8, 11, 12, 0),
        expires_at=datetime(2026, 8, 12, 12, 0),
    )
    with pytest.raises(ValueError, match="timezone-aware"):
        repository.put(naive)
