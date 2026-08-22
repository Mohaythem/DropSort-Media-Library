from __future__ import annotations

from dataclasses import dataclass
from typing import Mapping, Protocol
from urllib.error import HTTPError
from urllib.request import Request, urlopen


@dataclass(frozen=True, slots=True)
class HttpResponse:
    status: int
    body: bytes
    headers: Mapping[str, str]


class HttpTransport(Protocol):
    def get(
        self,
        url: str,
        *,
        headers: dict[str, str],
        timeout: float,
        max_bytes: int | None = None,
    ) -> HttpResponse: ...


class UrllibHttpTransport:
    def get(
        self,
        url: str,
        *,
        headers: dict[str, str],
        timeout: float,
        max_bytes: int | None = None,
    ) -> HttpResponse:
        request = Request(url, headers=headers, method="GET")
        try:
            with urlopen(request, timeout=timeout) as response:
                return HttpResponse(
                    status=response.status,
                    body=response.read() if max_bytes is None else response.read(max_bytes + 1),
                    headers=dict(response.headers.items()),
                )
        except HTTPError as error:
            try:
                return HttpResponse(
                    status=error.code,
                    body=error.read() if max_bytes is None else error.read(max_bytes + 1),
                    headers=dict(error.headers.items()) if error.headers is not None else {},
                )
            finally:
                error.close()
