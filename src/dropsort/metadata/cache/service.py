from __future__ import annotations

from collections.abc import Callable, Mapping
from dataclasses import dataclass
from datetime import datetime, timedelta, timezone
import json
from typing import Protocol
import unicodedata

from dropsort.metadata.contracts import (
    MetadataProvider,
    MetadataResponseError,
    MovieCandidate,
    MovieMetadata,
    MovieSearchQuery,
)


DEFAULT_SEARCH_TTL = timedelta(days=1)
DEFAULT_DETAIL_TTL = timedelta(days=7)
MAX_CACHE_TTL = timedelta(days=180)
_CACHE_VERSION = 1


@dataclass(frozen=True, slots=True)
class CacheRecord:
    provider: str
    cache_key: str
    payload: str
    fetched_at: datetime
    expires_at: datetime


class MetadataCacheStore(Protocol):
    def get(self, provider: str, cache_key: str) -> CacheRecord | None: ...

    def put(self, record: CacheRecord) -> None: ...


class CachedMetadataProvider:
    """Provider-neutral read-through cache. Expired records are never served."""

    def __init__(
        self,
        provider: MetadataProvider,
        store: MetadataCacheStore,
        *,
        search_ttl: timedelta = DEFAULT_SEARCH_TTL,
        detail_ttl: timedelta = DEFAULT_DETAIL_TTL,
        now: Callable[[], datetime] | None = None,
    ) -> None:
        if search_ttl <= timedelta(0) or detail_ttl <= timedelta(0):
            raise ValueError("cache TTL values must be positive")
        if search_ttl > MAX_CACHE_TTL or detail_ttl > MAX_CACHE_TTL:
            raise ValueError("cache TTL values cannot exceed 180 days")
        self._provider = provider
        self._store = store
        self._search_ttl = search_ttl
        self._detail_ttl = detail_ttl
        self._now = now or (lambda: datetime.now(timezone.utc))

    @property
    def provider_name(self) -> str:
        return self._provider.provider_name

    def search_movies(self, query: MovieSearchQuery) -> tuple[MovieCandidate, ...]:
        key = search_cache_key(query)
        cached = self._valid_payload(key)
        if cached is not None:
            candidates = _decode_search(cached, self.provider_name)
            if candidates is not None:
                return candidates

        candidates = self._provider.search_movies(query)
        if any(candidate.provider != self.provider_name for candidate in candidates):
            raise MetadataResponseError("provider returned a candidate for another provider")
        self._put(key, _encode_search(candidates), self._search_ttl)
        return candidates

    def get_movie(self, external_id: str) -> MovieMetadata:
        key = movie_cache_key(external_id)
        cached = self._valid_payload(key)
        if cached is not None:
            metadata = _decode_movie(cached, self.provider_name, external_id.strip())
            if metadata is not None:
                return metadata

        metadata = self._provider.get_movie(external_id)
        if metadata.provider != self.provider_name:
            raise MetadataResponseError("provider returned movie metadata for another provider")
        if metadata.external_id != external_id.strip():
            raise MetadataResponseError("provider returned metadata for another external ID")
        self._put(key, _encode_movie(metadata), self._detail_ttl)
        return metadata

    def _valid_payload(self, key: str) -> str | None:
        record = self._store.get(self.provider_name, key)
        if record is None:
            return None
        now = self._now()
        if now.tzinfo is None:
            raise ValueError("cache clock must return a timezone-aware datetime")
        if record.expires_at.tzinfo is None:
            return None
        return record.payload if record.expires_at > now else None

    def _put(self, key: str, payload: str, ttl: timedelta) -> None:
        fetched_at = self._now()
        if fetched_at.tzinfo is None:
            raise ValueError("cache clock must return a timezone-aware datetime")
        self._store.put(
            CacheRecord(
                provider=self.provider_name,
                cache_key=key,
                payload=payload,
                fetched_at=fetched_at,
                expires_at=fetched_at + ttl,
            )
        )


def search_cache_key(query: MovieSearchQuery) -> str:
    normalized_title = " ".join(
        unicodedata.normalize("NFKC", query.title).casefold().split()
    )
    encoded_title = json.dumps(normalized_title, ensure_ascii=False, separators=(",", ":"))
    return f"search:{encoded_title}:{query.year if query.year is not None else ''}"


def movie_cache_key(external_id: str) -> str:
    if not isinstance(external_id, str) or not external_id.strip():
        raise ValueError("external_id must be a non-empty string")
    encoded_id = json.dumps(str(external_id), ensure_ascii=False, separators=(",", ":"))
    return f"movie:{encoded_id}"


def _encode_search(candidates: tuple[MovieCandidate, ...]) -> str:
    payload = {
        "items": [_candidate_dict(candidate) for candidate in candidates],
        "kind": "search",
        "version": _CACHE_VERSION,
    }
    return _json_dump(payload)


def _encode_movie(metadata: MovieMetadata) -> str:
    payload = {
        "item": _metadata_dict(metadata),
        "kind": "movie",
        "version": _CACHE_VERSION,
    }
    return _json_dump(payload)


def _decode_search(payload: str, provider: str) -> tuple[MovieCandidate, ...] | None:
    decoded = _json_object(payload)
    if (
        decoded is None
        or decoded.get("kind") != "search"
        or decoded.get("version") != _CACHE_VERSION
    ):
        return None
    items = decoded.get("items")
    if not isinstance(items, list):
        return None
    try:
        candidates = tuple(_candidate_from_dict(item) for item in items)
    except (TypeError, ValueError):
        return None
    return candidates if all(item.provider == provider for item in candidates) else None


def _decode_movie(
    payload: str,
    provider: str,
    external_id: str,
) -> MovieMetadata | None:
    decoded = _json_object(payload)
    if (
        decoded is None
        or decoded.get("kind") != "movie"
        or decoded.get("version") != _CACHE_VERSION
    ):
        return None
    try:
        metadata = _metadata_from_dict(decoded.get("item"))
    except (TypeError, ValueError):
        return None
    return (
        metadata
        if metadata.provider == provider and metadata.external_id == external_id
        else None
    )


def _json_object(payload: str) -> Mapping[str, object] | None:
    try:
        decoded = json.loads(payload)
    except (TypeError, json.JSONDecodeError):
        return None
    return decoded if isinstance(decoded, Mapping) else None


def _json_dump(payload: object) -> str:
    return json.dumps(payload, ensure_ascii=False, separators=(",", ":"), sort_keys=True)


def _candidate_dict(candidate: MovieCandidate) -> dict[str, object]:
    return {
        "external_id": candidate.external_id,
        "original_title": candidate.original_title,
        "overview": candidate.overview,
        "poster_reference": candidate.poster_reference,
        "provider": candidate.provider,
        "rating": candidate.rating,
        "title": candidate.title,
        "year": candidate.year,
    }


def _metadata_dict(metadata: MovieMetadata) -> dict[str, object]:
    return {
        "cast": list(metadata.cast),
        "director": metadata.director,
        "external_id": metadata.external_id,
        "genres": list(metadata.genres),
        "original_title": metadata.original_title,
        "overview": metadata.overview,
        "poster_reference": metadata.poster_reference,
        "provider": metadata.provider,
        "rating": metadata.rating,
        "runtime_minutes": metadata.runtime_minutes,
        "title": metadata.title,
        "year": metadata.year,
    }


def _candidate_from_dict(value: object) -> MovieCandidate:
    if not isinstance(value, Mapping):
        raise TypeError("candidate cache item must be an object")
    return MovieCandidate(
        provider=_string(value, "provider"),
        external_id=_string(value, "external_id"),
        title=_string(value, "title"),
        original_title=_optional_string(value.get("original_title")),
        year=_optional_int(value.get("year")),
        overview=_optional_string(value.get("overview")),
        rating=_optional_float(value.get("rating")),
        poster_reference=_optional_string(value.get("poster_reference")),
    )


def _metadata_from_dict(value: object) -> MovieMetadata:
    if not isinstance(value, Mapping):
        raise TypeError("movie cache item must be an object")
    return MovieMetadata(
        provider=_string(value, "provider"),
        external_id=_string(value, "external_id"),
        title=_string(value, "title"),
        original_title=_optional_string(value.get("original_title")),
        year=_optional_int(value.get("year")),
        overview=_optional_string(value.get("overview")),
        genres=_string_tuple(value.get("genres")),
        runtime_minutes=_optional_int(value.get("runtime_minutes")),
        rating=_optional_float(value.get("rating")),
        director=_optional_string(value.get("director")),
        cast=_string_tuple(value.get("cast")),
        poster_reference=_optional_string(value.get("poster_reference")),
    )


def _string(value: Mapping[str, object], key: str) -> str:
    item = value.get(key)
    if not isinstance(item, str):
        raise TypeError(key)
    return item


def _optional_string(value: object) -> str | None:
    if value is None or isinstance(value, str):
        return value
    raise TypeError("optional string")


def _optional_int(value: object) -> int | None:
    if value is None or isinstance(value, int) and not isinstance(value, bool):
        return value
    raise TypeError("optional integer")


def _optional_float(value: object) -> float | None:
    if value is None:
        return None
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        raise TypeError("optional float")
    return float(value)


def _string_tuple(value: object) -> tuple[str, ...]:
    if not isinstance(value, list) or any(not isinstance(item, str) for item in value):
        raise TypeError("string list")
    return tuple(value)
