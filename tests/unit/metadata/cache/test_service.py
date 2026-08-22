from __future__ import annotations

from datetime import datetime, timedelta, timezone

import pytest

from dropsort.metadata.cache import (
    CacheRecord,
    CachedMetadataProvider,
    movie_cache_key,
    search_cache_key,
)
from dropsort.metadata.contracts import (
    MetadataResponseError,
    MetadataUnavailableError,
    MovieCandidate,
    MovieMetadata,
    MovieSearchQuery,
)


NOW = datetime(2026, 8, 11, 12, 0, tzinfo=timezone.utc)


class MemoryCacheStore:
    def __init__(self) -> None:
        self.records: dict[tuple[str, str], CacheRecord] = {}
        self.put_calls = 0

    def get(self, provider: str, cache_key: str) -> CacheRecord | None:
        return self.records.get((provider, cache_key))

    def put(self, record: CacheRecord) -> None:
        self.put_calls += 1
        self.records[(record.provider, record.cache_key)] = record


class FakeProvider:
    def __init__(self, provider_name: str = "provider-a") -> None:
        self.provider_name = provider_name
        self.search_calls = 0
        self.movie_calls = 0
        self.failure: Exception | None = None

    def search_movies(self, query: MovieSearchQuery) -> tuple[MovieCandidate, ...]:
        self.search_calls += 1
        if self.failure is not None:
            raise self.failure
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
        self.movie_calls += 1
        if self.failure is not None:
            raise self.failure
        return MovieMetadata(
            provider=self.provider_name,
            external_id=external_id,
            title="Movie",
            original_title=None,
            year=2024,
            overview=None,
            genres=("Drama",),
            runtime_minutes=120,
            rating=None,
            director=None,
            cast=(),
            poster_reference=None,
        )


def _cached(
    provider: FakeProvider,
    store: MemoryCacheStore,
    clock: list[datetime],
) -> CachedMetadataProvider:
    return CachedMetadataProvider(
        provider,
        store,
        search_ttl=timedelta(days=1),
        detail_ttl=timedelta(days=7),
        now=lambda: clock[0],
    )


def test_cache_miss_calls_provider_then_cache_hit_avoids_provider() -> None:
    provider = FakeProvider()
    store = MemoryCacheStore()
    cached = _cached(provider, store, [NOW])
    query = MovieSearchQuery("The Movie", 2024)

    first = cached.search_movies(query)
    second = cached.search_movies(query)

    assert first == second
    assert provider.search_calls == 1
    assert store.put_calls == 1


def test_movie_detail_cache_hit_avoids_provider() -> None:
    provider = FakeProvider()
    store = MemoryCacheStore()
    cached = _cached(provider, store, [NOW])

    first = cached.get_movie("1")
    second = cached.get_movie("1")

    assert first == second
    assert provider.movie_calls == 1


def test_expired_cache_performs_provider_lookup_and_replaces_entry() -> None:
    provider = FakeProvider()
    store = MemoryCacheStore()
    clock = [NOW]
    cached = _cached(provider, store, clock)
    query = MovieSearchQuery("Movie")
    cached.search_movies(query)
    clock[0] = NOW + timedelta(days=2)

    cached.search_movies(query)

    assert provider.search_calls == 2
    assert store.put_calls == 2


def test_corrupted_json_cache_does_not_crash_and_is_replaced() -> None:
    provider = FakeProvider()
    store = MemoryCacheStore()
    query = MovieSearchQuery("Movie")
    key = search_cache_key(query)
    store.records[(provider.provider_name, key)] = CacheRecord(
        provider=provider.provider_name,
        cache_key=key,
        payload="{not-json",
        fetched_at=NOW,
        expires_at=NOW + timedelta(days=1),
    )

    result = _cached(provider, store, [NOW]).search_movies(query)

    assert result[0].title == "Movie"
    assert provider.search_calls == 1
    assert store.put_calls == 1


@pytest.mark.parametrize(
    "payload",
    [
        "[]",
        '{"kind":"search","version":1,"items":{}}',
        '{"kind":"search","version":1,"items":[null]}',
        '{"kind":"search","version":1,"items":[{"provider":"provider-a"}]}',
        '{"kind":"search","version":1,"items":[{"provider":"provider-a","external_id":"1","title":"Movie","original_title":null,"year":10000,"overview":null,"rating":null,"poster_reference":null}]}',
        '{"kind":"search","version":1,"items":[{"provider":"provider-b","external_id":"1","title":"Movie","original_title":null,"year":null,"overview":null,"rating":null,"poster_reference":null}]}',
    ],
)
def test_structurally_invalid_search_cache_is_replaced(payload: str) -> None:
    provider = FakeProvider()
    store = MemoryCacheStore()
    query = MovieSearchQuery("Movie")
    key = search_cache_key(query)
    store.records[(provider.provider_name, key)] = CacheRecord(
        provider=provider.provider_name,
        cache_key=key,
        payload=payload,
        fetched_at=NOW,
        expires_at=NOW + timedelta(days=1),
    )

    result = _cached(provider, store, [NOW]).search_movies(query)

    assert result[0].provider == provider.provider_name
    assert provider.search_calls == 1


@pytest.mark.parametrize(
    "payload",
    [
        '{"kind":"search","version":1,"items":[]}',
        '{"kind":"movie","version":1,"item":null}',
        '{"kind":"movie","version":1,"item":{"provider":"provider-a"}}',
        '{"kind":"movie","version":1,"item":{"provider":"provider-b","external_id":"1","title":"Movie","original_title":null,"year":2024,"overview":null,"genres":[],"runtime_minutes":120,"rating":null,"director":null,"cast":[],"poster_reference":null}}',
        '{"kind":"movie","version":1,"item":{"provider":"provider-a","external_id":"different-id","title":"Movie","original_title":null,"year":2024,"overview":null,"genres":[],"runtime_minutes":120,"rating":null,"director":null,"cast":[],"poster_reference":null}}',
    ],
)
def test_structurally_invalid_movie_cache_is_replaced(payload: str) -> None:
    provider = FakeProvider()
    store = MemoryCacheStore()
    key = movie_cache_key("1")
    store.records[(provider.provider_name, key)] = CacheRecord(
        provider=provider.provider_name,
        cache_key=key,
        payload=payload,
        fetched_at=NOW,
        expires_at=NOW + timedelta(days=1),
    )

    result = _cached(provider, store, [NOW]).get_movie("1")

    assert result.provider == provider.provider_name
    assert provider.movie_calls == 1


def test_search_and_movie_cache_keys_cannot_collide() -> None:
    query = MovieSearchQuery(title="movie:123", year=2024)

    assert search_cache_key(query).startswith("search:")
    assert movie_cache_key("movie:123").startswith("movie:")
    assert search_cache_key(query) != movie_cache_key("movie:123")
    assert search_cache_key(query) == search_cache_key(query)


def test_movie_cache_key_rejects_empty_external_id() -> None:
    with pytest.raises(ValueError, match="external_id"):
        movie_cache_key(" ")


def test_provider_namespaces_cannot_collide() -> None:
    store = MemoryCacheStore()
    clock = [NOW]
    first_provider = FakeProvider("provider-a")
    second_provider = FakeProvider("provider-b")
    query = MovieSearchQuery("Movie")

    first = _cached(first_provider, store, clock).search_movies(query)
    second = _cached(second_provider, store, clock).search_movies(query)

    assert first[0].provider == "provider-a"
    assert second[0].provider == "provider-b"
    assert len(store.records) == 2


def test_provider_mismatch_is_rejected_before_cache_write() -> None:
    class MismatchedProvider(FakeProvider):
        def search_movies(self, query: MovieSearchQuery) -> tuple[MovieCandidate, ...]:
            return (
                MovieCandidate(
                    provider="wrong-provider",
                    external_id="1",
                    title=query.title,
                    original_title=None,
                    year=None,
                    overview=None,
                    rating=None,
                    poster_reference=None,
                ),
            )

        def get_movie(self, external_id: str) -> MovieMetadata:
            metadata = super().get_movie(external_id)
            return MovieMetadata(
                provider="wrong-provider",
                external_id=metadata.external_id,
                title=metadata.title,
                original_title=metadata.original_title,
                year=metadata.year,
                overview=metadata.overview,
                genres=metadata.genres,
                runtime_minutes=metadata.runtime_minutes,
                rating=metadata.rating,
                director=metadata.director,
                cast=metadata.cast,
                poster_reference=metadata.poster_reference,
            )

    cached = _cached(MismatchedProvider(), MemoryCacheStore(), [NOW])

    with pytest.raises(MetadataResponseError, match="another provider"):
        cached.search_movies(MovieSearchQuery("Movie"))
    with pytest.raises(MetadataResponseError, match="another provider"):
        cached.get_movie("1")


def test_movie_detail_with_wrong_external_id_is_not_cached() -> None:
    class WrongMovieProvider(FakeProvider):
        def get_movie(self, external_id: str) -> MovieMetadata:
            metadata = super().get_movie(external_id)
            return MovieMetadata(
                provider=metadata.provider,
                external_id="different-id",
                title=metadata.title,
                original_title=metadata.original_title,
                year=metadata.year,
                overview=metadata.overview,
                genres=metadata.genres,
                runtime_minutes=metadata.runtime_minutes,
                rating=metadata.rating,
                director=metadata.director,
                cast=metadata.cast,
                poster_reference=metadata.poster_reference,
            )

    store = MemoryCacheStore()
    cached = _cached(WrongMovieProvider(), store, [NOW])

    with pytest.raises(MetadataResponseError, match="external ID"):
        cached.get_movie("requested-id")

    assert store.put_calls == 0


def test_provider_unavailable_with_valid_cache_returns_cached_data() -> None:
    provider = FakeProvider()
    store = MemoryCacheStore()
    cached = _cached(provider, store, [NOW])
    query = MovieSearchQuery("Movie")
    expected = cached.search_movies(query)
    provider.failure = MetadataUnavailableError("offline")

    assert cached.search_movies(query) == expected
    assert provider.search_calls == 1


def test_provider_unavailable_without_cache_raises_controlled_error() -> None:
    provider = FakeProvider()
    provider.failure = MetadataUnavailableError("offline")

    with pytest.raises(MetadataUnavailableError, match="offline"):
        _cached(provider, MemoryCacheStore(), [NOW]).search_movies(MovieSearchQuery("Movie"))


def test_provider_unavailable_with_stale_cache_does_not_hide_staleness() -> None:
    provider = FakeProvider()
    store = MemoryCacheStore()
    clock = [NOW]
    cached = _cached(provider, store, clock)
    query = MovieSearchQuery("Movie")
    cached.search_movies(query)
    clock[0] = NOW + timedelta(days=2)
    provider.failure = MetadataUnavailableError("offline")

    with pytest.raises(MetadataUnavailableError, match="offline"):
        cached.search_movies(query)


def test_corrupted_cache_plus_unavailable_provider_returns_provider_error() -> None:
    provider = FakeProvider()
    provider.failure = MetadataUnavailableError("offline")
    store = MemoryCacheStore()
    query = MovieSearchQuery("Movie")
    key = search_cache_key(query)
    store.records[(provider.provider_name, key)] = CacheRecord(
        provider=provider.provider_name,
        cache_key=key,
        payload='{"kind":"unexpected"}',
        fetched_at=NOW,
        expires_at=NOW + timedelta(days=1),
    )

    with pytest.raises(MetadataUnavailableError, match="offline"):
        _cached(provider, store, [NOW]).search_movies(query)


@pytest.mark.parametrize(
    ("search_ttl", "detail_ttl"),
    [(timedelta(0), timedelta(days=1)), (timedelta(days=1), timedelta(seconds=-1))],
)
def test_nonpositive_cache_ttls_are_rejected(
    search_ttl: timedelta, detail_ttl: timedelta
) -> None:
    with pytest.raises(ValueError, match="TTL"):
        CachedMetadataProvider(
            FakeProvider(),
            MemoryCacheStore(),
            search_ttl=search_ttl,
            detail_ttl=detail_ttl,
        )


def test_cache_ttl_cannot_exceed_provider_terms_limit() -> None:
    with pytest.raises(ValueError, match="180 days"):
        CachedMetadataProvider(
            FakeProvider(),
            MemoryCacheStore(),
            detail_ttl=timedelta(days=181),
        )


def test_naive_cache_clock_is_rejected() -> None:
    cached = CachedMetadataProvider(
        FakeProvider(),
        MemoryCacheStore(),
        now=lambda: datetime(2026, 8, 11, 12, 0),
    )

    with pytest.raises(ValueError, match="timezone-aware"):
        cached.search_movies(MovieSearchQuery("Movie"))


def test_naive_expiration_in_store_is_treated_as_invalid_cache() -> None:
    provider = FakeProvider()
    store = MemoryCacheStore()
    query = MovieSearchQuery("Movie")
    key = search_cache_key(query)
    store.records[(provider.provider_name, key)] = CacheRecord(
        provider=provider.provider_name,
        cache_key=key,
        payload="{}",
        fetched_at=NOW,
        expires_at=datetime(2026, 8, 12, 12, 0),
    )

    _cached(provider, store, [NOW]).search_movies(query)

    assert provider.search_calls == 1


def test_default_cache_clock_is_timezone_aware() -> None:
    provider = FakeProvider()
    store = MemoryCacheStore()

    CachedMetadataProvider(provider, store).search_movies(MovieSearchQuery("Movie"))

    record = next(iter(store.records.values()))
    assert record.fetched_at.tzinfo is not None
