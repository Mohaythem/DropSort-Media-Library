from __future__ import annotations

import json
from urllib.parse import parse_qs, urlsplit

import pytest

from dropsort.metadata.contracts import (
    MetadataAuthenticationError,
    MetadataRateLimitError,
    MetadataResponseError,
    MetadataUnavailableError,
    MovieCandidate,
    MovieMetadata,
    MovieSearchQuery,
)
from dropsort.metadata.providers.http import HttpResponse
from dropsort.metadata.providers.tmdb import TmdbMetadataProvider


class StubTransport:
    def __init__(self, *outcomes: HttpResponse | Exception) -> None:
        self.outcomes = list(outcomes)
        self.requests: list[tuple[str, dict[str, str], float]] = []

    def get(self, url: str, *, headers: dict[str, str], timeout: float) -> HttpResponse:
        self.requests.append((url, headers, timeout))
        outcome = self.outcomes.pop(0)
        if isinstance(outcome, Exception):
            raise outcome
        return outcome


def _json_response(payload: object, status: int = 200) -> HttpResponse:
    return HttpResponse(
        status=status,
        body=json.dumps(payload).encode("utf-8"),
        headers={},
    )


def _provider(transport: StubTransport, *, token: str = "fake-test-token") -> TmdbMetadataProvider:
    return TmdbMetadataProvider(
        read_access_token=token,
        transport=transport,
        timeout_seconds=4.5,
    )


def test_search_normalizes_multiple_candidates_and_year_aware_request() -> None:
    transport = StubTransport(
        _json_response(
            {
                "results": [
                    {
                        "id": 155,
                        "title": "The Dark Knight",
                        "original_title": "The Dark Knight",
                        "release_date": "2008-07-16",
                        "overview": "Batman faces the Joker.",
                        "vote_average": 8.5,
                        "poster_path": "/dark-knight.jpg",
                    },
                    {
                        "id": 364,
                        "title": "Batman",
                        "original_title": "Batman",
                        "release_date": "1989-06-21",
                        "overview": "",
                        "vote_average": None,
                        "poster_path": None,
                    },
                ]
            }
        )
    )

    candidates = _provider(transport).search_movies(
        MovieSearchQuery(title="The Dark Knight", year=2008)
    )

    assert candidates == (
        MovieCandidate(
            provider="tmdb",
            external_id="155",
            title="The Dark Knight",
            original_title="The Dark Knight",
            year=2008,
            overview="Batman faces the Joker.",
            rating=8.5,
            poster_reference="/dark-knight.jpg",
        ),
        MovieCandidate(
            provider="tmdb",
            external_id="364",
            title="Batman",
            original_title="Batman",
            year=1989,
            overview=None,
            rating=None,
            poster_reference=None,
        ),
    )
    url, headers, timeout = transport.requests[0]
    query = parse_qs(urlsplit(url).query)
    assert urlsplit(url).path == "/3/search/movie"
    assert query == {
        "include_adult": ["false"],
        "language": ["en-US"],
        "page": ["1"],
        "primary_release_year": ["2008"],
        "query": ["The Dark Knight"],
    }
    assert headers["Authorization"] == "Bearer fake-test-token"
    assert timeout == 4.5


def test_search_without_year_omits_provider_year_parameter() -> None:
    transport = StubTransport(_json_response({"results": []}))

    assert _provider(transport).search_movies(MovieSearchQuery(title="Movie")) == ()

    query = parse_qs(urlsplit(transport.requests[0][0]).query)
    assert "primary_release_year" not in query


@pytest.mark.parametrize(
    ("release_date", "expected_year"),
    [
        ("2024-03-01", 2024),
        ("", None),
        (None, None),
        ("not-a-date", None),
        ("2024-99-99", None),
        (2024, None),
    ],
)
def test_search_year_normalization_is_conservative(
    release_date: object, expected_year: int | None
) -> None:
    transport = StubTransport(
        _json_response(
            {
                "results": [
                    {
                        "id": 1,
                        "title": "Movie",
                        "release_date": release_date,
                    }
                ]
            }
        )
    )

    candidate = _provider(transport).search_movies(MovieSearchQuery("Movie"))[0]

    assert candidate.year == expected_year


def test_search_missing_optional_metadata_normalizes_to_none() -> None:
    transport = StubTransport(_json_response({"results": [{"id": 1, "title": "Movie"}]}))

    candidate = _provider(transport).search_movies(MovieSearchQuery("Movie"))[0]

    assert candidate.original_title is None
    assert candidate.year is None
    assert candidate.overview is None
    assert candidate.rating is None
    assert candidate.poster_reference is None


@pytest.mark.parametrize(
    "item",
    [
        None,
        {"title": "Missing ID"},
        {"id": "1", "title": "Wrong ID type"},
        {"id": 0, "title": "Invalid ID"},
        {"id": 1},
        {"id": 1, "title": 123},
    ],
)
def test_search_rejects_malformed_required_provider_fields(item: object) -> None:
    transport = StubTransport(_json_response({"results": [item]}))

    with pytest.raises(MetadataResponseError):
        _provider(transport).search_movies(MovieSearchQuery("Movie"))


def test_movie_details_are_normalized_with_appended_credits() -> None:
    transport = StubTransport(
        _json_response(
            {
                "id": 155,
                "title": "The Dark Knight",
                "original_title": "The Dark Knight",
                "release_date": "2008-07-16",
                "overview": "Batman faces the Joker.",
                "genres": [{"id": 28, "name": "Action"}, {"id": 80, "name": "Crime"}],
                "runtime": 152,
                "vote_average": 8.5,
                "poster_path": "/dark-knight.jpg",
                "credits": {
                    "crew": [
                        {"job": "Producer", "name": "Producer Name"},
                        {"job": "Director", "name": "Christopher Nolan"},
                    ],
                    "cast": [
                        {"name": "Christian Bale"},
                        {"name": "Heath Ledger"},
                    ],
                },
            }
        )
    )

    metadata = _provider(transport).get_movie("155")

    assert metadata == MovieMetadata(
        provider="tmdb",
        external_id="155",
        title="The Dark Knight",
        original_title="The Dark Knight",
        year=2008,
        overview="Batman faces the Joker.",
        genres=("Action", "Crime"),
        runtime_minutes=152,
        rating=8.5,
        director="Christopher Nolan",
        cast=("Christian Bale", "Heath Ledger"),
        poster_reference="/dark-knight.jpg",
    )
    url = urlsplit(transport.requests[0][0])
    assert url.path == "/3/movie/155"
    assert parse_qs(url.query)["append_to_response"] == ["credits"]


def test_movie_details_allow_missing_optional_metadata() -> None:
    transport = StubTransport(_json_response({"id": 1, "title": "Movie"}))

    metadata = _provider(transport).get_movie("1")

    assert metadata.original_title is None
    assert metadata.year is None
    assert metadata.overview is None
    assert metadata.genres == ()
    assert metadata.runtime_minutes is None
    assert metadata.rating is None
    assert metadata.director is None
    assert metadata.cast == ()
    assert metadata.poster_reference is None


@pytest.mark.parametrize(
    "payload",
    [
        [],
        {"title": "Missing ID"},
        {"id": 0, "title": "Invalid ID"},
        {"id": 1},
        {"id": 1, "title": 123},
        {"id": 1, "title": "Movie", "genres": {}},
        {"id": 1, "title": "Movie", "credits": []},
        {"id": 1, "title": "Movie", "credits": {"crew": {}, "cast": []}},
    ],
)
def test_movie_details_reject_unexpected_payloads(payload: object) -> None:
    with pytest.raises(MetadataResponseError):
        _provider(StubTransport(_json_response(payload))).get_movie("1")


def test_movie_details_ignore_malformed_optional_items_and_values() -> None:
    transport = StubTransport(
        _json_response(
            {
                "id": 1,
                "title": "Movie",
                "genres": [None, {"name": "Drama"}, {"name": 123}],
                "runtime": True,
                "vote_average": float("nan"),
                "credits": {
                    "crew": [None, {"job": "Director", "name": 123}],
                    "cast": [None, {"name": "Actor"}, {"name": 123}],
                },
            }
        )
    )

    metadata = _provider(transport).get_movie("1")

    assert metadata.genres == ("Drama",)
    assert metadata.runtime_minutes is None
    assert metadata.rating is None
    assert metadata.director is None
    assert metadata.cast == ("Actor",)


def test_get_movie_rejects_empty_external_id_without_http() -> None:
    transport = StubTransport()

    with pytest.raises(ValueError, match="external_id"):
        _provider(transport).get_movie(" ")

    assert transport.requests == []


@pytest.mark.parametrize(
    ("status", "error_type"),
    [
        (401, MetadataAuthenticationError),
        (403, MetadataAuthenticationError),
        (429, MetadataRateLimitError),
        (500, MetadataUnavailableError),
        (503, MetadataUnavailableError),
        (400, MetadataResponseError),
        (404, MetadataResponseError),
    ],
)
def test_http_statuses_are_translated(status: int, error_type: type[Exception]) -> None:
    transport = StubTransport(_json_response({"status": "error"}, status=status))

    with pytest.raises(error_type) as exc_info:
        _provider(transport, token="secret-that-must-not-leak").search_movies(
            MovieSearchQuery("Movie")
        )

    assert "secret-that-must-not-leak" not in str(exc_info.value)


@pytest.mark.parametrize("error", [TimeoutError("timed out"), OSError("connection failed")])
def test_network_errors_are_translated(error: Exception) -> None:
    transport = StubTransport(error)

    with pytest.raises(MetadataUnavailableError) as exc_info:
        _provider(transport).search_movies(MovieSearchQuery("Movie"))

    assert exc_info.value.__cause__ is error


@pytest.mark.parametrize(
    "response",
    [
        HttpResponse(status=200, body=b"not-json", headers={}),
        HttpResponse(status=200, body=b"\xff", headers={}),
        _json_response([]),
        _json_response({"results": {}}),
    ],
)
def test_invalid_json_and_unexpected_search_payloads_are_rejected(
    response: HttpResponse,
) -> None:
    with pytest.raises(MetadataResponseError):
        _provider(StubTransport(response)).search_movies(MovieSearchQuery("Movie"))


def test_missing_environment_credential_is_controlled(monkeypatch) -> None:
    monkeypatch.delenv("DROPSORT_TMDB_READ_ACCESS_TOKEN", raising=False)

    with pytest.raises(MetadataAuthenticationError, match="DROPSORT_TMDB_READ_ACCESS_TOKEN"):
        TmdbMetadataProvider.from_environment(transport=StubTransport())


def test_environment_credential_builds_provider_without_exposing_token(monkeypatch) -> None:
    transport = StubTransport(_json_response({"results": []}))
    monkeypatch.setenv("DROPSORT_TMDB_READ_ACCESS_TOKEN", "environment-test-token")

    provider = TmdbMetadataProvider.from_environment(transport=transport)
    provider.search_movies(MovieSearchQuery("Movie"))

    assert transport.requests[0][1]["Authorization"] == "Bearer environment-test-token"


@pytest.mark.parametrize("token", ["", " "])
def test_constructor_rejects_empty_credentials(token: str) -> None:
    with pytest.raises(MetadataAuthenticationError):
        TmdbMetadataProvider(read_access_token=token, transport=StubTransport())


def test_constructor_rejects_nonpositive_timeout() -> None:
    with pytest.raises(ValueError, match="timeout"):
        TmdbMetadataProvider(
            read_access_token="fake",
            transport=StubTransport(),
            timeout_seconds=0,
        )


@pytest.mark.parametrize("timeout", [float("nan"), float("inf")])
def test_constructor_rejects_nonfinite_timeout(timeout: float) -> None:
    with pytest.raises(ValueError, match="timeout"):
        TmdbMetadataProvider(
            read_access_token="fake",
            transport=StubTransport(),
            timeout_seconds=timeout,
        )


def test_constructor_rejects_insecure_base_url() -> None:
    with pytest.raises(ValueError, match="HTTPS"):
        TmdbMetadataProvider(
            read_access_token="fake",
            transport=StubTransport(),
            base_url="http://api.themoviedb.org/3",
        )
