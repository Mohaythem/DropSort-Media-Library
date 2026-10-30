from __future__ import annotations

from typing import Protocol
from collections.abc import Callable
from datetime import datetime, timezone

from dropsort.application.dto.catalog import (
    MovieFileIngestionResult,
    MovieMetadataEnrichmentResult,
    RegisterLocalMovieFileCommand,
)
from dropsort.application.dto.movie_import import (
    ConfirmMovieImportCommand,
    ImportProposalStatus,
)
from dropsort.application.errors import MovieImportCatalogError
from dropsort.library.movies import CatalogError
from dropsort.metadata.contracts import MovieCandidate


class LocalMovieFileRegistrar(Protocol):
    def execute(
        self,
        command: RegisterLocalMovieFileCommand,
    ) -> MovieFileIngestionResult: ...


class MovieMetadataEnricher(Protocol):
    def execute(
        self,
        movie_id: int,
        candidate: MovieCandidate,
    ) -> MovieMetadataEnrichmentResult: ...

    def mark_needs_match(
        self,
        movie_id: int,
        *,
        failure_code: str = "NO_CONFIDENT_MATCH",
    ) -> MovieMetadataEnrichmentResult: ...


class ConfirmMovieImport:
    """Commit explicit local registration before optional metadata enrichment."""

    def __init__(
        self,
        registrar: LocalMovieFileRegistrar,
        enricher: MovieMetadataEnricher,
        *,
        now: Callable[[], datetime] | None = None,
    ) -> None:
        self._registrar = registrar
        self._enricher = enricher
        self._now = now or (lambda: datetime.now(timezone.utc))

    def execute(self, command: ConfirmMovieImportCommand) -> MovieFileIngestionResult:
        """Synchronous compatibility path: Transaction A, then Transaction B."""

        registration = self.register(command)
        return self.enrich(command, registration)

    def register(
        self,
        command: ConfirmMovieImportCommand,
    ) -> MovieFileIngestionResult:
        """Commit Transaction A and return before any metadata provider call."""

        if not isinstance(command, ConfirmMovieImportCommand):
            raise ValueError("command must be ConfirmMovieImportCommand")
        discovery = command.proposal.discovery
        parsed = discovery.parsed_media
        assert parsed is not None
        assert discovery.file_size is not None
        observed_at = self._now()
        if not isinstance(observed_at, datetime) or observed_at.tzinfo is None:
            raise ValueError("import clock must return a timezone-aware datetime")
        registration_command = RegisterLocalMovieFileCommand(
            parsed_media=parsed,
            file_path=discovery.path,
            file_size=discovery.file_size,
            observed_at=observed_at,
        )
        try:
            return self._registrar.execute(registration_command)
        except CatalogError as error:
            raise MovieImportCatalogError("movie catalog import failed") from error

    def enrich(
        self,
        command: ConfirmMovieImportCommand,
        registration: MovieFileIngestionResult,
    ) -> MovieFileIngestionResult:
        """Run optional Transaction B after the local-success boundary."""

        if not isinstance(command, ConfirmMovieImportCommand):
            raise ValueError("command must be ConfirmMovieImportCommand")
        if not isinstance(registration, MovieFileIngestionResult):
            raise ValueError("registration must be MovieFileIngestionResult")

        enrichment: MovieMetadataEnrichmentResult | None = None
        try:
            if command.chosen_candidate is not None:
                enrichment = self._enricher.execute(
                    registration.movie.id,
                    command.chosen_candidate,
                )
            elif command.proposal.status is ImportProposalStatus.NO_MATCH:
                enrichment = self._enricher.mark_needs_match(
                    registration.movie.id,
                    failure_code="NO_CONFIDENT_MATCH",
                )
            elif command.proposal.status is ImportProposalStatus.REVIEW_REQUIRED:
                enrichment = self._enricher.mark_needs_match(
                    registration.movie.id,
                    failure_code="AMBIGUOUS_MATCH",
                )
        except CatalogError:
            enrichment = None

        movie = registration.movie if enrichment is None else enrichment.movie
        return MovieFileIngestionResult(
            movie=movie,
            media_file=registration.media_file,
            enrichment=enrichment,
        )
