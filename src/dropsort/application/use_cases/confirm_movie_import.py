from __future__ import annotations

from collections.abc import Callable
from datetime import datetime, timezone
from typing import Protocol

from dropsort.application.dto.catalog import (
    MovieFileIngestionResult,
    RegisterMovieFileCommand,
)
from dropsort.application.dto.movie_import import ConfirmMovieImportCommand
from dropsort.application.errors import MovieImportCatalogError, MovieImportMetadataError
from dropsort.library.movies import CatalogError
from dropsort.metadata.contracts import MetadataError, MetadataProvider


class MovieFileRegistrar(Protocol):
    def execute(self, command: RegisterMovieFileCommand) -> MovieFileIngestionResult: ...


class ConfirmMovieImport:
    """Persist only an explicit caller confirmation; never organize the physical file."""

    def __init__(
        self,
        provider: MetadataProvider,
        registrar: MovieFileRegistrar,
        *,
        now: Callable[[], datetime] | None = None,
    ) -> None:
        self._provider = provider
        self._registrar = registrar
        self._now = now or (lambda: datetime.now(timezone.utc))

    def execute(self, command: ConfirmMovieImportCommand) -> MovieFileIngestionResult:
        if not isinstance(command, ConfirmMovieImportCommand):
            raise ValueError("command must be ConfirmMovieImportCommand")
        candidate = command.chosen_candidate
        if candidate.provider != self._provider.provider_name:
            raise MovieImportMetadataError("confirmed candidate provider is unavailable")
        try:
            metadata = self._provider.get_movie(candidate.external_id)
        except MetadataError as error:
            raise MovieImportMetadataError("movie metadata details are unavailable") from error
        if (metadata.provider, metadata.external_id) != (
            candidate.provider,
            candidate.external_id,
        ):
            raise MovieImportMetadataError(
                "movie metadata identity does not match the confirmed candidate"
            )
        observed_at = self._now()
        if not isinstance(observed_at, datetime) or observed_at.tzinfo is None:
            raise ValueError("import clock must return a timezone-aware datetime")
        discovery = command.proposal.discovery
        parsed = discovery.parsed_media
        assert parsed is not None
        assert discovery.file_size is not None
        registration = RegisterMovieFileCommand(
            metadata=metadata,
            parsed_media=parsed,
            file_path=discovery.path,
            file_size=discovery.file_size,
            observed_at=observed_at,
        )
        try:
            return self._registrar.execute(registration)
        except CatalogError as error:
            raise MovieImportCatalogError("movie catalog import failed") from error
