from __future__ import annotations

import pytest
from pathlib import Path

from dropsort.application.dto.movie_import import (
    ConfirmMovieImportCommand,
    ImportProposalReason,
    ImportProposalStatus,
    MovieImportProposal,
)


def _candidate(external_id="155", title="The Dark Knight", year=2008):
    from dropsort.metadata.contracts import MovieCandidate
    return MovieCandidate("tmdb", external_id, title, None, year, None, None, None)


def _discovery(path: Path, *, tv: bool = False):
    from dropsort.media.discovery import DiscoveryClassification, DiscoveredMedia
    from dropsort.media.parser import MediaType, ParsedMedia
    parsed = ParsedMedia(
        "Show.S01E01.mkv" if tv else "Movie.2008.mkv",
        MediaType.TV_EPISODE if tv else MediaType.MOVIE,
        "Show" if tv else "Movie",
        None if tv else 2008,
        None, None, None, ".mkv",
    )
    return DiscoveredMedia(path.absolute(), 10, parsed, DiscoveryClassification.TV_EPISODE_SKIPPED if tv else DiscoveryClassification.MOVIE_CANDIDATE, None)


def test_manual_selection_proposal_is_confirmable_only_with_its_candidate(tmp_path: Path) -> None:
    candidate = _candidate("579", "The Wind Rises", 2013)
    proposal = MovieImportProposal(
        ImportProposalStatus.MANUAL_SELECTION,
        _discovery(tmp_path / "movie.mkv"),
        (candidate,),
        None,
        None,
        (ImportProposalReason.MANUAL_SELECTION,),
        None,
    )
    command = ConfirmMovieImportCommand(proposal, candidate)
    assert command.chosen_candidate == candidate
    with pytest.raises(ValueError, match="proposal candidates"):
        ConfirmMovieImportCommand(proposal, _candidate("other"))


def test_manual_selection_requires_movie_discovery_and_candidates(tmp_path: Path) -> None:
    tv = _discovery(tmp_path / "show.mkv", tv=True)
    with pytest.raises(ValueError, match="movie discovery"):
        MovieImportProposal(
            ImportProposalStatus.MANUAL_SELECTION,
            tv,
            (_candidate(),),
            None,
            None,
            (ImportProposalReason.MANUAL_SELECTION,),
            None,
        )

    with pytest.raises(ValueError, match="requires candidates"):
        MovieImportProposal(
            ImportProposalStatus.MANUAL_SELECTION,
            _discovery(tmp_path / "movie.mkv"),
            (), None, None, (ImportProposalReason.MANUAL_SELECTION,), None,
        )
