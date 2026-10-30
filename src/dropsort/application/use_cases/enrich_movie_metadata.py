from __future__ import annotations

from collections.abc import Callable
from datetime import datetime, timezone

from dropsort.application.dto.catalog import (
    MetadataEnrichmentOutcome,
    MovieMetadataEnrichmentResult,
)
from dropsort.library.movies import (
    CatalogRecordNotFoundError,
    CatalogUnitOfWork,
    MetadataStatus,
    Movie,
    MovieCatalogData,
    MovieIdentityConflictError,
)
from dropsort.metadata.contracts import (
    MetadataAuthenticationError,
    MetadataError,
    MetadataProvider,
    MetadataRateLimitError,
    MetadataResponseError,
    MetadataUnavailableError,
    MovieCandidate,
    MovieMetadata,
)
from dropsort.posters import PosterActions, PosterRequest


class EnrichMovieMetadata:
    """Fetch externally, then attach metadata to the same stable local MovieId."""

    def __init__(
        self,
        provider: MetadataProvider,
        unit_of_work_factory: Callable[[], CatalogUnitOfWork],
        *,
        poster_actions: PosterActions | None = None,
        now: Callable[[], datetime] | None = None,
    ) -> None:
        self._provider = provider
        self._unit_of_work_factory = unit_of_work_factory
        self._poster_actions = poster_actions
        self._now = now or (lambda: datetime.now(timezone.utc))

    def execute(
        self,
        movie_id: int,
        candidate: MovieCandidate,
    ) -> MovieMetadataEnrichmentResult:
        _validate_movie_id(movie_id)
        if not isinstance(candidate, MovieCandidate):
            raise ValueError("candidate must be MovieCandidate")
        if candidate.provider.casefold() != self._provider.provider_name.casefold():
            return self._set_status(
                movie_id,
                MetadataStatus.FAILED,
                MetadataEnrichmentOutcome.FAILED,
                "PROVIDER_MISMATCH",
            )

        try:
            metadata = self._provider.get_movie(candidate.external_id)
        except MetadataResponseError:
            return self._set_status(
                movie_id,
                MetadataStatus.FAILED,
                MetadataEnrichmentOutcome.FAILED,
                "INVALID_PROVIDER_RESPONSE",
            )
        except (
            MetadataAuthenticationError,
            MetadataRateLimitError,
            MetadataUnavailableError,
            MetadataError,
        ) as error:
            return self._set_status(
                movie_id,
                MetadataStatus.PENDING,
                MetadataEnrichmentOutcome.PENDING,
                type(error).__name__,
            )

        if not isinstance(metadata, MovieMetadata) or (
            metadata.provider.casefold(),
            metadata.external_id,
        ) != (candidate.provider.casefold(), candidate.external_id):
            return self._set_status(
                movie_id,
                MetadataStatus.FAILED,
                MetadataEnrichmentOutcome.FAILED,
                "IDENTITY_MISMATCH",
            )

        data = _catalog_data(metadata)
        catalog_now = self._require_now()
        with self._unit_of_work_factory() as unit_of_work:
            current = unit_of_work.movies.get_by_id(movie_id)
            if current is None:
                raise CatalogRecordNotFoundError(movie_id)
            owner = unit_of_work.movies.get_by_external_id(
                data.provider,
                data.external_id,
            )
            if owner is not None and owner.id != movie_id:
                collision = unit_of_work.movies.update_metadata_status(
                    movie_id,
                    MetadataStatus.NEEDS_MATCH,
                    now=catalog_now,
                )
                return MovieMetadataEnrichmentResult(
                    collision,
                    MetadataEnrichmentOutcome.IDENTITY_COLLISION,
                    collision_movie_id=owner.id,
                    failure_code="IDENTITY_COLLISION",
                )
            if current.provider is not None and (
                current.provider.casefold(),
                current.external_id,
            ) != (data.provider.casefold(), data.external_id):
                needs_match = unit_of_work.movies.update_metadata_status(
                    movie_id,
                    MetadataStatus.NEEDS_MATCH,
                    now=catalog_now,
                )
                return MovieMetadataEnrichmentResult(
                    needs_match,
                    MetadataEnrichmentOutcome.NEEDS_MATCH,
                    failure_code="IDENTITY_REPLACEMENT_REQUIRES_REVIEW",
                )
            try:
                ready = unit_of_work.movies.attach_external_metadata(
                    movie_id,
                    data,
                    now=catalog_now,
                )
            except MovieIdentityConflictError:
                owner = unit_of_work.movies.get_by_external_id(
                    data.provider,
                    data.external_id,
                )
                if owner is None or owner.id == movie_id:
                    raise
                collision = unit_of_work.movies.update_metadata_status(
                    movie_id,
                    MetadataStatus.NEEDS_MATCH,
                    now=catalog_now,
                )
                return MovieMetadataEnrichmentResult(
                    collision,
                    MetadataEnrichmentOutcome.IDENTITY_COLLISION,
                    collision_movie_id=owner.id,
                    failure_code="IDENTITY_COLLISION",
                )

        self._load_poster_after_catalog_commit(ready)
        return MovieMetadataEnrichmentResult(
            ready,
            MetadataEnrichmentOutcome.READY,
        )

    def mark_needs_match(
        self,
        movie_id: int,
        *,
        failure_code: str = "NO_CONFIDENT_MATCH",
    ) -> MovieMetadataEnrichmentResult:
        return self._set_status(
            movie_id,
            MetadataStatus.NEEDS_MATCH,
            MetadataEnrichmentOutcome.NEEDS_MATCH,
            failure_code,
        )

    def _set_status(
        self,
        movie_id: int,
        status: MetadataStatus,
        outcome: MetadataEnrichmentOutcome,
        failure_code: str,
    ) -> MovieMetadataEnrichmentResult:
        _validate_movie_id(movie_id)
        with self._unit_of_work_factory() as unit_of_work:
            movie = unit_of_work.movies.update_metadata_status(
                movie_id,
                status,
                now=self._require_now(),
            )
        return MovieMetadataEnrichmentResult(
            movie,
            outcome,
            failure_code=failure_code,
        )

    def _load_poster_after_catalog_commit(self, movie: Movie) -> None:
        if (
            self._poster_actions is None
            or movie.provider is None
            or movie.poster_reference is None
        ):
            return
        try:
            self._poster_actions.load_poster(
                PosterRequest(movie.provider, movie.poster_reference)
            )
        except Exception:
            return

    def _require_now(self) -> datetime:
        value = self._now()
        if not isinstance(value, datetime) or value.tzinfo is None:
            raise ValueError("enrichment clock must return a timezone-aware datetime")
        return value


def _catalog_data(metadata: MovieMetadata) -> MovieCatalogData:
    return MovieCatalogData(
        provider=metadata.provider,
        external_id=metadata.external_id,
        title=metadata.title,
        original_title=metadata.original_title,
        year=metadata.year,
        overview=metadata.overview,
        genres=metadata.genres,
        runtime_minutes=metadata.runtime_minutes,
        rating=metadata.rating,
        poster_reference=metadata.poster_reference,
        metadata_status=MetadataStatus.READY,
    )


def _validate_movie_id(movie_id: int) -> None:
    if isinstance(movie_id, bool) or not isinstance(movie_id, int) or movie_id <= 0:
        raise ValueError("movie_id must be a positive integer")
