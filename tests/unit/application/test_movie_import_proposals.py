from __future__ import annotations

from dataclasses import replace
from pathlib import Path

import pytest

from dropsort.application.dto.movie_import import (
    ConfirmMovieImportCommand,
    ImportProposalReason,
    ImportProposalStatus,
    MovieImportProposal,
)
from dropsort.media.discovery import DiscoveryClassification, DiscoveredMedia
from dropsort.media.matcher import MovieMatcher
from dropsort.media.parser import MediaType, ParsedMedia
from dropsort.metadata.contracts import MovieCandidate


def _discovery(tmp_path: Path) -> DiscoveredMedia:
    parsed = ParsedMedia(
        original_name="Movie.2024.mkv",
        media_type=MediaType.MOVIE,
        title="Movie",
        year=2024,
        resolution="1080p",
        source="BluRay",
        codec="x264",
        extension=".mkv",
    )
    return DiscoveredMedia(
        path=(tmp_path / "Movie.2024.mkv").absolute(),
        file_size=100,
        parsed_media=parsed,
        classification=DiscoveryClassification.MOVIE_CANDIDATE,
        issue=None,
    )


def _candidate(external_id: str = "1") -> MovieCandidate:
    return MovieCandidate(
        provider="tmdb",
        external_id=external_id,
        title="Movie",
        original_title=None,
        year=2024,
        overview=None,
        rating=None,
        poster_reference=None,
    )


def _proposal(tmp_path: Path) -> MovieImportProposal:
    discovery = _discovery(tmp_path)
    candidate = _candidate()
    decision = MovieMatcher().match(discovery.parsed_media, (candidate,))  # type: ignore[arg-type]
    return MovieImportProposal(
        status=ImportProposalStatus.MATCH_PROPOSED,
        discovery=discovery,
        candidates=(candidate,),
        match_decision=decision,
        proposed_candidate=candidate,
        reasons=(ImportProposalReason.MATCHED_CANDIDATE,),
        existing_media_file_id=None,
    )


def test_confirm_command_requires_candidate_from_confirmable_proposal(tmp_path: Path) -> None:
    proposal = _proposal(tmp_path)

    command = ConfirmMovieImportCommand(proposal, proposal.candidates[0])

    assert command.chosen_candidate == proposal.proposed_candidate
    with pytest.raises(ValueError, match="proposal candidates"):
        ConfirmMovieImportCommand(proposal, _candidate("different"))


def test_non_confirmable_proposal_cannot_be_confirmed(tmp_path: Path) -> None:
    proposal = MovieImportProposal(
        status=ImportProposalStatus.NO_MATCH,
        discovery=_discovery(tmp_path),
        candidates=(),
        match_decision=None,
        proposed_candidate=None,
        reasons=(ImportProposalReason.NO_MATCH,),
        existing_media_file_id=None,
    )

    with pytest.raises(ValueError, match="confirmable"):
        ConfirmMovieImportCommand(proposal, _candidate())


def test_proposal_rejects_move_or_authorization_fields() -> None:
    fields = set(MovieImportProposal.__dataclass_fields__)

    assert fields.isdisjoint(
        {"destination", "destination_path", "authorized", "move_plan", "operation"}
    )


@pytest.mark.parametrize(
    ("changes", "message"),
    (
        ({"status": "MATCH_PROPOSED"}, "status"),
        ({"discovery": object()}, "discovery"),
        ({"candidates": []}, "candidates"),
        ({"match_decision": object()}, "match_decision"),
        ({"proposed_candidate": object()}, "proposed_candidate"),
        ({"reasons": ()}, "reasons"),
        ({"existing_media_file_id": -1}, "existing_media_file_id"),
    ),
)
def test_proposal_rejects_invalid_boundary_types(
    tmp_path: Path,
    changes: dict[str, object],
    message: str,
) -> None:
    with pytest.raises(ValueError, match=message):
        replace(_proposal(tmp_path), **changes)


def test_proposal_status_must_agree_with_match_decision(tmp_path: Path) -> None:
    with pytest.raises(ValueError, match="status"):
        replace(
            _proposal(tmp_path),
            status=ImportProposalStatus.REVIEW_REQUIRED,
            reasons=(ImportProposalReason.REVIEW_REQUIRED,),
        )


def test_proposal_candidates_must_preserve_decision_ranking(tmp_path: Path) -> None:
    proposal = _proposal(tmp_path)
    with pytest.raises(ValueError, match="ranking"):
        replace(
            proposal,
            candidates=proposal.candidates + (_candidate("different"),),
        )


def test_already_in_library_requires_exclusive_existing_id(tmp_path: Path) -> None:
    proposal = _proposal(tmp_path)
    with pytest.raises(ValueError, match="requires"):
        MovieImportProposal(
            ImportProposalStatus.ALREADY_IN_LIBRARY,
            proposal.discovery,
            (),
            None,
            None,
            (ImportProposalReason.ALREADY_IN_LIBRARY,),
            None,
        )
    with pytest.raises(ValueError, match="only ALREADY"):
        replace(proposal, existing_media_file_id=1)
