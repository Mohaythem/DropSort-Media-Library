from __future__ import annotations

import pytest

from dropsort.application.configuration import SessionTmdbCredentials
from dropsort.metadata.providers.http import HttpResponse
from dropsort.posters import PosterRequest
from dropsort.posters.errors import (
    PosterAuthenticationError,
    PosterResponseError,
    PosterTooLargeError,
    PosterUnavailableError,
)
from dropsort.posters.providers.tmdb import TmdbPosterSource


class StubTransport:
    def __init__(self, outcome: HttpResponse | Exception) -> None:
        self.outcome = outcome
        self.calls: list[tuple[str, dict[str, str], float, int | None]] = []

    def get(self, url, *, headers, timeout, max_bytes=None):
        self.calls.append((url, headers, timeout, max_bytes))
        if isinstance(self.outcome, Exception):
            raise self.outcome
        return self.outcome


def response(body: bytes, status: int = 200, content_type: str = "image/png") -> HttpResponse:
    return HttpResponse(status, body, {"Content-Type": content_type})


def test_success_uses_bounded_transport_and_runtime_credential(png_bytes: bytes) -> None:
    credentials = SessionTmdbCredentials(environment={})
    credentials.set_session_token("session-token-value-123456789012345")
    transport = StubTransport(response(png_bytes))
    source = TmdbPosterSource(credentials, transport=transport, timeout_seconds=4, maximum_bytes=999)

    asset = source.fetch(PosterRequest("tmdb", "/poster.png"))

    assert asset.content == png_bytes
    url, headers, timeout, maximum = transport.calls[0]
    assert url == "https://image.tmdb.org/t/p/w500/poster.png"
    assert headers["Authorization"].startswith("Bearer ")
    assert "session-token" not in repr(source)
    assert timeout == 4
    assert maximum == 999


@pytest.mark.parametrize("reference", ("poster.jpg", "/nested/poster.jpg", "/poster.gif"))
def test_invalid_tmdb_reference_never_calls_transport(reference: str) -> None:
    transport = StubTransport(response(b"unused"))
    source = TmdbPosterSource(SessionTmdbCredentials(environment={}), transport=transport)

    with pytest.raises(ValueError, match="TMDB poster reference"):
        source.fetch(PosterRequest("tmdb", reference))
    assert transport.calls == []


def test_missing_credential_is_controlled() -> None:
    source = TmdbPosterSource(SessionTmdbCredentials(environment={}), transport=StubTransport(response(b"x")))

    with pytest.raises(PosterAuthenticationError, match="not configured"):
        source.fetch(PosterRequest("tmdb", "/poster.jpg"))


@pytest.mark.parametrize(
    ("outcome", "error"),
    (
        (TimeoutError(), PosterUnavailableError),
        (OSError(), PosterUnavailableError),
        (response(b"x", 401), PosterAuthenticationError),
        (response(b"x", 403), PosterAuthenticationError),
        (response(b"x", 500), PosterUnavailableError),
        (response(b"x", 404), PosterResponseError),
    ),
)
def test_network_and_http_failures_are_translated(outcome, error) -> None:
    credentials = SessionTmdbCredentials(environment={})
    credentials.set_session_token("session-token-value-123456789012345")
    source = TmdbPosterSource(credentials, transport=StubTransport(outcome))

    with pytest.raises(error):
        source.fetch(PosterRequest("tmdb", "/poster.jpg"))


def test_invalid_truncated_and_oversized_content_is_rejected(png_bytes: bytes) -> None:
    credentials = SessionTmdbCredentials(environment={})
    credentials.set_session_token("session-token-value-123456789012345")
    for body in (b"not-image", png_bytes[:-5]):
        with pytest.raises(PosterResponseError):
            TmdbPosterSource(credentials, transport=StubTransport(response(body))).fetch(
                PosterRequest("tmdb", "/poster.png")
            )
    with pytest.raises(PosterTooLargeError):
        TmdbPosterSource(credentials, transport=StubTransport(response(png_bytes)), maximum_bytes=10).fetch(
            PosterRequest("tmdb", "/poster.png")
        )
