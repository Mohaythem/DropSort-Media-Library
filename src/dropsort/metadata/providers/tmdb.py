from __future__ import annotations

from collections.abc import Mapping
from datetime import date
import json
import math
import os
from typing import Any
from urllib.parse import quote, urlencode, urlsplit

from dropsort.metadata.contracts import (
    MetadataAuthenticationError,
    MetadataRateLimitError,
    MetadataResponseError,
    MetadataUnavailableError,
    MovieCandidate,
    MovieMetadata,
    MovieSearchQuery,
)
from dropsort.metadata.providers.http import HttpResponse, HttpTransport, UrllibHttpTransport


class TmdbMetadataProvider:
    provider_name = "tmdb"
    credential_environment_variable = "DROPSORT_TMDB_READ_ACCESS_TOKEN"

    def __init__(
        self,
        *,
        read_access_token: str,
        transport: HttpTransport | None = None,
        timeout_seconds: float = 10.0,
        language: str = "en-US",
        base_url: str = "https://api.themoviedb.org/3",
    ) -> None:
        if not isinstance(read_access_token, str) or not read_access_token.strip():
            raise MetadataAuthenticationError("TMDB read access token is not configured")
        if (
            isinstance(timeout_seconds, bool)
            or not isinstance(timeout_seconds, (int, float))
            or not math.isfinite(timeout_seconds)
            or timeout_seconds <= 0
        ):
            raise ValueError("timeout_seconds must be a finite positive number")
        parsed_base_url = urlsplit(base_url)
        if parsed_base_url.scheme.lower() != "https" or not parsed_base_url.netloc:
            raise ValueError("base_url must be an HTTPS URL")
        self._read_access_token = read_access_token.strip()
        self._transport = transport or UrllibHttpTransport()
        self._timeout_seconds = float(timeout_seconds)
        self._language = language
        self._base_url = base_url.rstrip("/")

    @classmethod
    def from_environment(
        cls,
        *,
        transport: HttpTransport | None = None,
        timeout_seconds: float = 10.0,
        language: str = "en-US",
    ) -> TmdbMetadataProvider:
        token = os.environ.get(cls.credential_environment_variable)
        if token is None or not token.strip():
            raise MetadataAuthenticationError(
                f"{cls.credential_environment_variable} is not configured"
            )
        return cls(
            read_access_token=token,
            transport=transport,
            timeout_seconds=timeout_seconds,
            language=language,
        )

    def search_movies(self, query: MovieSearchQuery) -> tuple[MovieCandidate, ...]:
        parameters: dict[str, str] = {
            "query": query.title,
            "include_adult": "false",
            "language": self._language,
            "page": "1",
        }
        if query.year is not None:
            parameters["primary_release_year"] = str(query.year)
        payload = self._request_json("/search/movie", parameters)
        if not isinstance(payload, Mapping):
            raise MetadataResponseError("TMDB search response must be an object")
        results = payload.get("results")
        if not isinstance(results, list):
            raise MetadataResponseError("TMDB search response has no results list")
        return tuple(self._normalize_candidate(item) for item in results)

    def get_movie(self, external_id: str) -> MovieMetadata:
        if not isinstance(external_id, str) or not external_id.strip():
            raise ValueError("external_id must be a non-empty string")
        payload = self._request_json(
            f"/movie/{quote(external_id.strip(), safe='')}",
            {"append_to_response": "credits", "language": self._language},
        )
        if not isinstance(payload, Mapping):
            raise MetadataResponseError("TMDB movie response must be an object")
        try:
            provider_id = _required_int(payload, "id")
            title = _required_text(payload, "title")
        except ValueError as error:
            raise MetadataResponseError(
                "TMDB movie response has invalid required fields"
            ) from error
        genres = _genres(payload.get("genres"))
        director, cast = _credits(payload.get("credits"))
        return MovieMetadata(
            provider=self.provider_name,
            external_id=str(provider_id),
            title=title,
            original_title=_optional_text(payload.get("original_title")),
            year=_year(payload.get("release_date")),
            overview=_optional_text(payload.get("overview")),
            genres=genres,
            runtime_minutes=_positive_int(payload.get("runtime")),
            rating=_rating(payload.get("vote_average")),
            director=director,
            cast=cast,
            poster_reference=_optional_text(payload.get("poster_path")),
        )

    def _normalize_candidate(self, item: object) -> MovieCandidate:
        if not isinstance(item, Mapping):
            raise MetadataResponseError("TMDB search result must be an object")
        try:
            provider_id = _required_int(item, "id")
            title = _required_text(item, "title")
        except ValueError as error:
            raise MetadataResponseError(
                "TMDB search result has invalid required fields"
            ) from error
        return MovieCandidate(
            provider=self.provider_name,
            external_id=str(provider_id),
            title=title,
            original_title=_optional_text(item.get("original_title")),
            year=_year(item.get("release_date")),
            overview=_optional_text(item.get("overview")),
            rating=_rating(item.get("vote_average")),
            poster_reference=_optional_text(item.get("poster_path")),
        )

    def _request_json(self, path: str, parameters: Mapping[str, str]) -> object:
        url = f"{self._base_url}{path}?{urlencode(parameters)}"
        try:
            response = self._transport.get(
                url,
                headers={
                    "Accept": "application/json",
                    "Authorization": f"Bearer {self._read_access_token}",
                },
                timeout=self._timeout_seconds,
            )
        except (OSError, TimeoutError) as error:
            raise MetadataUnavailableError("TMDB is unavailable") from error
        self._validate_status(response)
        try:
            return json.loads(response.body.decode("utf-8"))
        except (UnicodeDecodeError, json.JSONDecodeError) as error:
            raise MetadataResponseError("TMDB returned invalid JSON") from error

    @staticmethod
    def _validate_status(response: HttpResponse) -> None:
        if 200 <= response.status < 300:
            return
        if response.status in {401, 403}:
            raise MetadataAuthenticationError("TMDB rejected the configured credential")
        if response.status == 429:
            raise MetadataRateLimitError("TMDB rate limit reached")
        if 500 <= response.status < 600:
            raise MetadataUnavailableError(f"TMDB service error ({response.status})")
        raise MetadataResponseError(f"TMDB HTTP error ({response.status})")


def _required_int(payload: Mapping[str, Any], key: str) -> int:
    value = payload.get(key)
    if isinstance(value, bool) or not isinstance(value, int) or value <= 0:
        raise ValueError(key)
    return value


def _required_text(payload: Mapping[str, Any], key: str) -> str:
    value = payload.get(key)
    if not isinstance(value, str) or not value.strip():
        raise ValueError(key)
    return value.strip()


def _optional_text(value: object) -> str | None:
    return value.strip() if isinstance(value, str) and value.strip() else None


def _year(value: object) -> int | None:
    if not isinstance(value, str):
        return None
    try:
        return date.fromisoformat(value).year
    except ValueError:
        return None


def _rating(value: object) -> float | None:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        return None
    rating = float(value)
    return rating if math.isfinite(rating) and 0 <= rating <= 10 else None


def _positive_int(value: object) -> int | None:
    return value if isinstance(value, int) and not isinstance(value, bool) and value > 0 else None


def _genres(value: object) -> tuple[str, ...]:
    if value is None:
        return ()
    if not isinstance(value, list):
        raise MetadataResponseError("TMDB genres must be a list")
    return tuple(
        name
        for item in value
        if isinstance(item, Mapping)
        if (name := _optional_text(item.get("name"))) is not None
    )


def _credits(value: object) -> tuple[str | None, tuple[str, ...]]:
    if value is None:
        return None, ()
    if not isinstance(value, Mapping):
        raise MetadataResponseError("TMDB credits must be an object")
    crew = value.get("crew", [])
    cast_items = value.get("cast", [])
    if not isinstance(crew, list) or not isinstance(cast_items, list):
        raise MetadataResponseError("TMDB credits contain invalid lists")
    director = next(
        (
            name
            for item in crew
            if isinstance(item, Mapping) and item.get("job") == "Director"
            if (name := _optional_text(item.get("name"))) is not None
        ),
        None,
    )
    cast = tuple(
        name
        for item in cast_items
        if isinstance(item, Mapping)
        if (name := _optional_text(item.get("name"))) is not None
    )
    return director, cast
