from __future__ import annotations

from datetime import datetime, timezone
from pathlib import Path

import pytest

from dropsort.application.dto.movie_import import ConfirmMovieImportCommand
from dropsort.application.errors import MovieImportCatalogError, MovieImportMetadataError
from dropsort.application.use_cases import ConfirmMovieImport, ProposeMovieImport
from dropsort.library.movies import CatalogIntegrityError
from dropsort.media.discovery import DiscoveryClassification, DiscoveredMedia
from dropsort.media.matcher import MovieMatcher
from dropsort.media.parser import MediaType, ParsedMedia
from dropsort.metadata.contracts import (
    MetadataUnavailableError,
    MovieCandidate,
    MovieMetadata,
    MovieSearchQuery,
)


NOW = datetime(2026, 8, 11, 12, 0, tzinfo=timezone.utc)


class Provider:
    provider_name = "tmdb"

    def __init__(self) -> None:
        self.candidate = MovieCandidate("tmdb", "1", "Movie", None, 2024, None, None, None)
        self.detail_error: Exception | None = None
        self.detail_calls: list[str] = []

    def search_movies(self, query: MovieSearchQuery) -> tuple[MovieCandidate, ...]:
        return (self.candidate,)

    def get_movie(self, external_id: str) -> MovieMetadata:
        self.detail_calls.append(external_id)
        if self.detail_error is not None:
            raise self.detail_error
        return MovieMetadata(
            provider="tmdb",
            external_id=external_id,
            title="Movie",
            original_title=None,
            year=2024,
            overview=None,
            genres=("Drama",),
            runtime_minutes=100,
            rating=7.0,
            director=None,
            cast=(),
            poster_reference=None,
        )


class Lookup:
    def get_by_path(self, path: Path):
        return None


class Registrar:
    def __init__(self) -> None:
        self.commands = []
        self.error: Exception | None = None

    def execute(self, command):
        self.commands.append(command)
        if self.error is not None:
            raise self.error
        return object()


def _discovery(tmp_path: Path) -> DiscoveredMedia:
    return DiscoveredMedia(
        (tmp_path / "Movie.2024.mkv").absolute(),
        123,
        ParsedMedia("Movie.2024.mkv", MediaType.MOVIE, "Movie", 2024, "1080p", "BluRay", "x264", ".mkv"),
        DiscoveryClassification.MOVIE_CANDIDATE,
        None,
    )


def _command(tmp_path: Path, provider: Provider) -> ConfirmMovieImportCommand:
    proposal = ProposeMovieImport(provider, MovieMatcher(), Lookup()).execute(
        _discovery(tmp_path)
    )
    return ConfirmMovieImportCommand(proposal, provider.candidate)


def test_explicit_confirmation_loads_details_and_delegates_verified_facts(tmp_path: Path) -> None:
    provider = Provider()
    registrar = Registrar()

    result = ConfirmMovieImport(provider, registrar, now=lambda: NOW).execute(
        _command(tmp_path, provider)
    )

    assert result is not None
    assert provider.detail_calls == ["1"]
    assert len(registrar.commands) == 1
    command = registrar.commands[0]
    assert command.file_path == (tmp_path / "Movie.2024.mkv").absolute()
    assert command.file_size == 123
    assert command.observed_at == NOW


def test_confirmation_validates_command_provider_and_clock(tmp_path: Path) -> None:
    provider = Provider()
    command = _command(tmp_path, provider)

    with pytest.raises(ValueError, match="command"):
        ConfirmMovieImport(provider, Registrar()).execute(object())  # type: ignore[arg-type]
    provider.provider_name = "other"  # type: ignore[misc]
    with pytest.raises(MovieImportMetadataError, match="provider"):
        ConfirmMovieImport(provider, Registrar()).execute(command)
    provider.provider_name = "tmdb"  # type: ignore[misc]
    with pytest.raises(ValueError, match="clock"):
        ConfirmMovieImport(
            provider,
            Registrar(),
            now=lambda: datetime(2026, 8, 11),
        ).execute(command)


def test_detail_failure_is_translated_and_never_calls_registrar(tmp_path: Path) -> None:
    provider = Provider()
    command = _command(tmp_path, provider)
    provider.detail_error = MetadataUnavailableError("offline")
    registrar = Registrar()

    with pytest.raises(MovieImportMetadataError):
        ConfirmMovieImport(provider, registrar, now=lambda: NOW).execute(command)

    assert registrar.commands == []


def test_catalog_failure_is_translated(tmp_path: Path) -> None:
    provider = Provider()
    registrar = Registrar()
    registrar.error = CatalogIntegrityError("failed")

    with pytest.raises(MovieImportCatalogError):
        ConfirmMovieImport(provider, registrar, now=lambda: NOW).execute(
            _command(tmp_path, provider)
        )


def test_invalid_provider_detail_identity_is_rejected(tmp_path: Path) -> None:
    provider = Provider()
    command = _command(tmp_path, provider)
    original_get_movie = provider.get_movie

    def wrong_detail(_: str) -> MovieMetadata:
        metadata = original_get_movie("different")
        return metadata

    provider.get_movie = wrong_detail  # type: ignore[method-assign]

    with pytest.raises(MovieImportMetadataError, match="identity"):
        ConfirmMovieImport(provider, Registrar(), now=lambda: NOW).execute(command)
