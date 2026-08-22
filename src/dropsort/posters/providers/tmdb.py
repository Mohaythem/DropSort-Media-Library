from __future__ import annotations

import re

from dropsort.application.configuration import SessionTmdbCredentials
from dropsort.metadata.providers.http import HttpTransport, UrllibHttpTransport
from dropsort.posters.contracts import PosterAsset, PosterRequest, detect_image_format
from dropsort.posters.errors import (
    PosterAuthenticationError,
    PosterResponseError,
    PosterTooLargeError,
    PosterUnavailableError,
)


DEFAULT_MAXIMUM_POSTER_BYTES = 8 * 1024 * 1024
_TMDB_REFERENCE = re.compile(r"^/[A-Za-z0-9._-]+\.(?:jpe?g|png)$", re.IGNORECASE)


class TmdbPosterSource:
    def __init__(
        self,
        credentials: SessionTmdbCredentials,
        *,
        transport: HttpTransport | None = None,
        timeout_seconds: float = 10.0,
        maximum_bytes: int = DEFAULT_MAXIMUM_POSTER_BYTES,
        base_url: str = "https://image.tmdb.org/t/p/w500",
    ) -> None:
        if timeout_seconds <= 0:
            raise ValueError("timeout_seconds must be positive")
        if maximum_bytes <= 0:
            raise ValueError("maximum_bytes must be positive")
        self._credentials = credentials
        self._transport = transport or UrllibHttpTransport()
        self._timeout_seconds = float(timeout_seconds)
        self._maximum_bytes = maximum_bytes
        self._base_url = base_url.rstrip("/")

    def fetch(self, request: PosterRequest) -> PosterAsset:
        if request.provider != "tmdb" or not _TMDB_REFERENCE.fullmatch(request.reference):
            raise ValueError("invalid TMDB poster reference")
        token = self._credentials.access_token()
        if token is None:
            raise PosterAuthenticationError("TMDB is not configured")
        try:
            response = self._transport.get(
                f"{self._base_url}{request.reference}",
                headers={"Accept": "image/jpeg,image/png", "Authorization": f"Bearer {token}"},
                timeout=self._timeout_seconds,
                max_bytes=self._maximum_bytes,
            )
        except (OSError, TimeoutError) as error:
            raise PosterUnavailableError("TMDB poster service is unavailable") from error
        if len(response.body) > self._maximum_bytes:
            raise PosterTooLargeError("poster exceeds the configured size limit")
        if response.status in {401, 403}:
            raise PosterAuthenticationError("TMDB rejected the configured credential")
        if response.status >= 500 or response.status == 429:
            raise PosterUnavailableError("TMDB poster service is unavailable")
        if not 200 <= response.status < 300:
            raise PosterResponseError("TMDB returned an unexpected poster response")
        image_format = detect_image_format(response.body)
        return PosterAsset(image_format, response.body)

    def __repr__(self) -> str:
        return f"{type(self).__name__}(credential=<redacted>)"
