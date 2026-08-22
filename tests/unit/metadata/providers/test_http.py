from __future__ import annotations

from email.message import Message
from io import BytesIO
from urllib.error import HTTPError

import pytest

from dropsort.metadata.providers import http
from dropsort.metadata.providers.http import UrllibHttpTransport


class FakeUrlResponse:
    def __init__(self, status: int, body: bytes, headers: Message) -> None:
        self.status = status
        self._body = body
        self.headers = headers

    def read(self, size: int = -1) -> bytes:
        if size >= 0:
            return self._body[:size]
        return self._body

    def __enter__(self) -> FakeUrlResponse:
        return self

    def __exit__(self, *args: object) -> None:
        return None


def test_urllib_transport_performs_bounded_get_and_returns_bytes(monkeypatch) -> None:
    headers = Message()
    headers["Content-Type"] = "application/json"
    captured: dict[str, object] = {}

    def fake_urlopen(request, *, timeout: float):
        captured["request"] = request
        captured["timeout"] = timeout
        return FakeUrlResponse(200, b'{"ok":true}', headers)

    monkeypatch.setattr(http, "urlopen", fake_urlopen)

    response = UrllibHttpTransport().get(
        "https://example.invalid/data",
        headers={"Authorization": "Bearer fake"},
        timeout=3.0,
    )

    request = captured["request"]
    assert request.full_url == "https://example.invalid/data"
    assert request.get_method() == "GET"
    assert request.get_header("Authorization") == "Bearer fake"
    assert captured["timeout"] == 3.0
    assert response.status == 200
    assert response.body == b'{"ok":true}'
    assert response.headers["Content-Type"] == "application/json"


def test_urllib_transport_limits_success_body_read(monkeypatch) -> None:
    monkeypatch.setattr(
        http,
        "urlopen",
        lambda *args, **kwargs: FakeUrlResponse(200, b"0123456789", Message()),
    )

    response = UrllibHttpTransport().get(
        "https://example.invalid/image",
        headers={},
        timeout=3.0,
        max_bytes=4,
    )

    assert response.body == b"01234"


@pytest.mark.parametrize("with_headers", [True, False])
def test_urllib_transport_converts_http_error_to_response(monkeypatch, with_headers: bool) -> None:
    headers = Message() if with_headers else None
    if headers is not None:
        headers["Retry-After"] = "10"
    error = HTTPError(
        "https://example.invalid/data",
        429,
        "rate limited",
        headers,
        BytesIO(b'{"status":"limited"}'),
    )
    monkeypatch.setattr(http, "urlopen", lambda *args, **kwargs: (_ for _ in ()).throw(error))

    response = UrllibHttpTransport().get(
        "https://example.invalid/data",
        headers={},
        timeout=3.0,
    )

    assert response.status == 429
    assert response.body == b'{"status":"limited"}'
    assert response.headers == ({"Retry-After": "10"} if with_headers else {})
