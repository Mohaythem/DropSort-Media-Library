from __future__ import annotations

from typing import Protocol, runtime_checkable

from dropsort.metadata.contracts.models import MovieCandidate, MovieMetadata, MovieSearchQuery


@runtime_checkable
class MetadataProvider(Protocol):
    provider_name: str

    def search_movies(self, query: MovieSearchQuery) -> tuple[MovieCandidate, ...]: ...

    def get_movie(self, external_id: str) -> MovieMetadata: ...
