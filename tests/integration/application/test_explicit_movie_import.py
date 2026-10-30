from __future__ import annotations

from datetime import datetime, timezone
from pathlib import Path

import pytest

from dropsort.application.dto.movie_import import ConfirmMovieImportCommand
from dropsort.application.dto import MetadataEnrichmentOutcome
from dropsort.application.use_cases import (
    ConfirmMovieImport,
    EnrichMovieMetadata,
    ProposeMovieImport,
    RegisterLocalMovieFile,
)
from dropsort.database.repositories import (
    MediaFileRepository,
    SqliteCatalogUnitOfWork,
)
from dropsort.media.discovery import DiscoveryClassification, DiscoveredMedia
from dropsort.media.matcher import MovieMatcher
from dropsort.media.parser import MediaType, ParsedMedia
from dropsort.metadata.contracts import MovieCandidate, MovieMetadata, MovieSearchQuery


NOW = datetime(2026, 8, 11, 12, 0, tzinfo=timezone.utc)


class Provider:
    provider_name = "tmdb"

    def __init__(self, external_id: str = "1") -> None:
        self.external_id = external_id

    def search_movies(self, query: MovieSearchQuery) -> tuple[MovieCandidate, ...]:
        return (
            MovieCandidate(
                "tmdb", self.external_id, "Movie", None, 2024, None, None, None
            ),
        )

    def get_movie(self, external_id: str) -> MovieMetadata:
        return MovieMetadata(
            "tmdb", external_id, "Movie", None, 2024, None, ("Drama",), 100, 7.0,
            None, (), None,
        )


def _discovery(path: Path, resolution: str = "1080p") -> DiscoveredMedia:
    return DiscoveredMedia(
        path.absolute(),
        123,
        ParsedMedia(path.name, MediaType.MOVIE, "Movie", 2024, resolution, "BluRay", "x264", ".mkv"),
        DiscoveryClassification.MOVIE_CANDIDATE,
        None,
    )


def _proposal_command(harness, provider: Provider, discovery: DiscoveredMedia):
    proposal = ProposeMovieImport(
        provider,
        MovieMatcher(),
        MediaFileRepository(harness.database),
    ).execute(discovery)
    return ConfirmMovieImportCommand(proposal, proposal.proposed_candidate)


def _confirmer(harness, provider: Provider) -> ConfirmMovieImport:
    unit_of_work_factory = lambda: SqliteCatalogUnitOfWork(harness.database)
    registrar = RegisterLocalMovieFile(unit_of_work_factory, now=lambda: NOW)
    enricher = EnrichMovieMetadata(
        provider,
        unit_of_work_factory,
        now=lambda: NOW,
    )
    return ConfirmMovieImport(registrar, enricher, now=lambda: NOW)


def _confirm(harness, provider: Provider, discovery: DiscoveredMedia):
    return _confirmer(harness, provider).execute(
        _proposal_command(harness, provider, discovery)
    )


def _counts(harness) -> tuple[int, int]:
    with harness.database.connection() as connection:
        return (
            connection.execute("SELECT COUNT(*) FROM movies").fetchone()[0],
            connection.execute("SELECT COUNT(*) FROM media_files").fetchone()[0],
        )


def test_confirmed_import_creates_catalog_association_and_is_idempotent(
    harness,
    tmp_path: Path,
) -> None:
    provider = Provider()
    discovery = _discovery(tmp_path / "Movie.mkv")
    command = _proposal_command(harness, provider, discovery)
    confirmer = _confirmer(harness, provider)

    first = confirmer.execute(command)
    second = confirmer.execute(command)

    assert first.movie.id == second.movie.id
    assert first.media_file.id == second.media_file.id
    assert _counts(harness) == (1, 1)


def test_second_local_file_keeps_its_own_ids_when_external_identity_collides(
    harness,
    tmp_path: Path,
) -> None:
    provider = Provider()

    first = _confirm(harness, provider, _discovery(tmp_path / "1080p.mkv"))
    second = _confirm(
        harness,
        provider,
        _discovery(tmp_path / "2160p.mkv", "2160p"),
    )

    assert first.movie.id != second.movie.id
    assert first.media_file.id != second.media_file.id
    assert second.enrichment is not None
    assert second.enrichment.outcome is MetadataEnrichmentOutcome.IDENTITY_COLLISION
    assert second.enrichment.collision_movie_id == first.movie.id
    assert _counts(harness) == (2, 2)


def test_already_in_library_path_short_circuits_before_metadata_search(
    harness,
    tmp_path: Path,
) -> None:
    provider = Provider()
    discovery = _discovery(tmp_path / "Movie.MKV")
    imported = _confirm(harness, provider, discovery)
    alias = _discovery(tmp_path / "movie.mkv")

    proposal = ProposeMovieImport(
        provider,
        MovieMatcher(),
        MediaFileRepository(harness.database),
    ).execute(alias)

    assert proposal.existing_media_file_id == imported.media_file.id


def test_matched_proposal_alone_performs_zero_catalog_writes(
    harness,
    tmp_path: Path,
) -> None:
    provider = Provider()

    proposal = ProposeMovieImport(
        provider,
        MovieMatcher(),
        MediaFileRepository(harness.database),
    ).execute(_discovery(tmp_path / "Movie.mkv"))

    assert proposal.proposed_candidate is not None
    assert _counts(harness) == (0, 0)


def test_same_file_repeat_never_replaces_existing_local_identity(
    harness,
    tmp_path: Path,
) -> None:
    provider = Provider()
    conflicting = Provider("2")
    discovery = _discovery(tmp_path / "Movie.mkv")
    stale_command = _proposal_command(harness, conflicting, discovery)
    original = _confirm(harness, provider, discovery)

    repeated = _confirmer(harness, conflicting).execute(stale_command)

    assert repeated.movie.id == original.movie.id
    assert repeated.media_file.id == original.media_file.id
    assert repeated.movie.metadata_status.value == "NEEDS_MATCH"
    assert _counts(harness) == (1, 1)
