from __future__ import annotations

import pytest

from dropsort.application.configuration.metadata_credentials import SessionTmdbCredentials
from dropsort.metadata.contracts import (
    MetadataAuthenticationError,
    MovieCandidate,
    MovieMetadata,
    MovieSearchQuery,
)
from dropsort.metadata.providers.session_tmdb import SessionConfiguredTmdbProvider


TOKEN_A = "first-session-token-1234567890123"
TOKEN_B = "second-session-token-123456789012"


class FakeProvider:
    provider_name = "tmdb"

    def __init__(self, token: str, calls: list[str]) -> None:
        self.token = token
        self.calls = calls

    def search_movies(self, query: MovieSearchQuery) -> tuple[MovieCandidate, ...]:
        self.calls.append(f"search:{self.token}")
        return ()

    def get_movie(self, external_id: str) -> MovieMetadata:
        self.calls.append(f"detail:{self.token}")
        return MovieMetadata(
            provider="tmdb",
            external_id=external_id,
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


def test_missing_credential_is_controlled_without_constructing_http_provider() -> None:
    credentials = SessionTmdbCredentials(environment={})
    created: list[str] = []
    provider = SessionConfiguredTmdbProvider(
        credentials,
        provider_factory=lambda token: (created.append(token), FakeProvider(token, []))[1],
    )

    with pytest.raises(MetadataAuthenticationError, match="not configured"):
        provider.search_movies(MovieSearchQuery("Movie"))

    assert created == []


def test_runtime_session_reconfiguration_is_used_without_restart() -> None:
    credentials = SessionTmdbCredentials(environment={})
    calls: list[str] = []
    provider = SessionConfiguredTmdbProvider(
        credentials,
        provider_factory=lambda token: FakeProvider(token, calls),
    )

    credentials.set_session_token(TOKEN_A)
    provider.search_movies(MovieSearchQuery("Movie"))
    credentials.set_session_token(TOKEN_B)
    provider.get_movie("7")

    assert calls == [f"search:{TOKEN_A}", f"detail:{TOKEN_B}"]


def test_unchanged_token_reuses_provider_instance() -> None:
    credentials = SessionTmdbCredentials(environment={})
    credentials.set_session_token(TOKEN_A)
    constructed: list[str] = []
    provider = SessionConfiguredTmdbProvider(
        credentials,
        provider_factory=lambda token: (
            constructed.append(token),
            FakeProvider(token, []),
        )[1],
    )

    provider.search_movies(MovieSearchQuery("One"))
    provider.search_movies(MovieSearchQuery("Two"))

    assert constructed == [TOKEN_A]
