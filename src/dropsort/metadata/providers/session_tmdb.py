from __future__ import annotations

from collections.abc import Callable
from threading import Lock

from dropsort.application.configuration.metadata_credentials import SessionTmdbCredentials
from dropsort.metadata.contracts import (
    MetadataAuthenticationError,
    MetadataProvider,
    MovieCandidate,
    MovieMetadata,
    MovieSearchQuery,
)
from dropsort.metadata.providers.tmdb import TmdbMetadataProvider


ProviderFactory = Callable[[str], MetadataProvider]


class SessionConfiguredTmdbProvider:
    """Resolve the current credential for each request, without a restart."""

    provider_name = "tmdb"

    def __init__(
        self,
        credentials: SessionTmdbCredentials,
        *,
        provider_factory: ProviderFactory | None = None,
    ) -> None:
        self._credentials = credentials
        self._provider_factory = provider_factory or _create_tmdb_provider
        self._active_token: str | None = None
        self._active_provider: MetadataProvider | None = None
        self._lock = Lock()

    def search_movies(self, query: MovieSearchQuery) -> tuple[MovieCandidate, ...]:
        return self._provider().search_movies(query)

    def get_movie(self, external_id: str) -> MovieMetadata:
        return self._provider().get_movie(external_id)

    def _provider(self) -> MetadataProvider:
        with self._lock:
            token = self._credentials.access_token()
            if token is None:
                raise MetadataAuthenticationError("TMDB is not configured")
            if self._active_provider is None or token != self._active_token:
                self._active_provider = self._provider_factory(token)
                self._active_token = token
            return self._active_provider


def _create_tmdb_provider(token: str) -> MetadataProvider:
    return TmdbMetadataProvider(read_access_token=token)
