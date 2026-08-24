from __future__ import annotations

from datetime import datetime, timezone
from pathlib import Path
from types import SimpleNamespace

import pytest

from dropsort.application.dto.catalog import MovieFileIngestionResult
from dropsort.application.dto.movie_import import (
    ConfirmMovieImportCommand,
    ImportProposalReason,
    ImportProposalStatus,
    MovieImportProposal,
)
from dropsort.application.errors import MovieImportCatalogError
from dropsort.application.use_cases import ConfirmMovieImport
from dropsort.library.movies import CatalogIntegrityError
from dropsort.media.discovery import DiscoveryClassification, DiscoveredMedia
from dropsort.media.parser import MediaType, ParsedMedia
from dropsort.metadata.contracts import MovieCandidate


NOW = datetime(2026, 8, 24, 12, 0, tzinfo=timezone.utc)


def _proposal(
    tmp_path: Path,
    *,
    status: ImportProposalStatus = ImportProposalStatus.MANUAL_SELECTION,
) -> MovieImportProposal:
    discovery = DiscoveredMedia(
        (tmp_path / "Movie.2024.mkv").absolute(),
        123,
        ParsedMedia(
            "Movie.2024.mkv",
            MediaType.MOVIE,
            "Movie",
            2024,
            "1080p",
            "BluRay",
            "x264",
            ".mkv",
        ),
        DiscoveryClassification.MOVIE_CANDIDATE,
        None,
    )
    candidate = MovieCandidate(
        "tmdb", "1", "Movie", None, 2024, None, None, None
    )
    candidates = (candidate,) if status is ImportProposalStatus.MANUAL_SELECTION else ()
    return MovieImportProposal(
        status=status,
        discovery=discovery,
        candidates=candidates,
        match_decision=None,
        proposed_candidate=None,
        reasons=(
            ImportProposalReason.MANUAL_SELECTION
            if status is ImportProposalStatus.MANUAL_SELECTION
            else ImportProposalReason.NO_MATCH,
        ),
        existing_media_file_id=None,
    )


class Registrar:
    def __init__(self, events: list[str]) -> None:
        self.events = events
        self.commands = []
        self.error: Exception | None = None
        self.movie = SimpleNamespace(id=7)

    def execute(self, command):
        self.events.append("register")
        self.commands.append(command)
        if self.error is not None:
            raise self.error
        return MovieFileIngestionResult(
            movie=self.movie, media_file=SimpleNamespace(id=9)
        )


class Enricher:
    def __init__(self, events: list[str]) -> None:
        self.events = events
        self.calls = []
        self.result_movie = SimpleNamespace(id=7)

    def execute(self, movie_id, candidate):
        self.events.append("enrich")
        self.calls.append((movie_id, candidate))
        return SimpleNamespace(movie=self.result_movie)

    def mark_needs_match(self, movie_id, *, failure_code="NO_CONFIDENT_MATCH"):
        self.events.append("needs_match")
        self.calls.append((movie_id, failure_code))
        return SimpleNamespace(movie=self.result_movie)


def test_explicit_confirmation_registers_locally_before_candidate_enrichment(
    tmp_path: Path,
) -> None:
    events: list[str] = []
    registrar = Registrar(events)
    enricher = Enricher(events)
    proposal = _proposal(tmp_path)
    command = ConfirmMovieImportCommand(proposal, proposal.candidates[0])

    result = ConfirmMovieImport(registrar, enricher, now=lambda: NOW).execute(command)

    assert events == ["register", "enrich"]
    assert result.movie is enricher.result_movie
    assert len(registrar.commands) == 1
    local = registrar.commands[0]
    assert local.file_path == (tmp_path / "Movie.2024.mkv").absolute()
    assert local.file_size == 123
    assert local.observed_at == NOW
    assert enricher.calls == [(7, proposal.candidates[0])]


def test_confirmation_validates_command_and_clock(tmp_path: Path) -> None:
    events: list[str] = []
    registrar = Registrar(events)
    enricher = Enricher(events)

    with pytest.raises(ValueError, match="command"):
        ConfirmMovieImport(registrar, enricher).execute(object())  # type: ignore[arg-type]
    with pytest.raises(ValueError, match="clock"):
        ConfirmMovieImport(
            registrar,
            enricher,
            now=lambda: datetime(2026, 8, 24),
        ).execute(ConfirmMovieImportCommand(_proposal(tmp_path)))


def test_no_match_registers_first_then_marks_existing_movie_needs_match(
    tmp_path: Path,
) -> None:
    events: list[str] = []
    registrar = Registrar(events)
    enricher = Enricher(events)
    proposal = _proposal(tmp_path, status=ImportProposalStatus.NO_MATCH)

    result = ConfirmMovieImport(registrar, enricher, now=lambda: NOW).execute(
        ConfirmMovieImportCommand(proposal)
    )

    assert result is not None
    assert events == ["register", "needs_match"]
    assert enricher.calls == [(7, "NO_CONFIDENT_MATCH")]


def test_catalog_failure_in_transaction_a_is_translated(tmp_path: Path) -> None:
    events: list[str] = []
    registrar = Registrar(events)
    registrar.error = CatalogIntegrityError("failed")

    with pytest.raises(MovieImportCatalogError):
        ConfirmMovieImport(registrar, Enricher(events), now=lambda: NOW).execute(
            ConfirmMovieImportCommand(_proposal(tmp_path))
        )

    assert events == ["register"]


def test_enrichment_catalog_failure_does_not_reclassify_committed_registration_as_import_failure(
    tmp_path: Path,
) -> None:
    events: list[str] = []
    registrar = Registrar(events)

    class FailingEnricher(Enricher):
        def execute(self, movie_id, candidate):
            self.events.append("enrich")
            raise CatalogIntegrityError("enrichment write failed")

    result = ConfirmMovieImport(
        registrar,
        FailingEnricher(events),
        now=lambda: NOW,
    ).execute(
        ConfirmMovieImportCommand(
            _proposal(tmp_path),
            _proposal(tmp_path).candidates[0],
        )
    )

    assert events == ["register", "enrich"]
    assert result.movie is registrar.movie
    assert result.enrichment is None
