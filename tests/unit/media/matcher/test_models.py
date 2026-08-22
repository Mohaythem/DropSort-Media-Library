from __future__ import annotations

from dataclasses import FrozenInstanceError

import pytest

from dropsort.media.matcher import (
    CandidateScore,
    MatchDecision,
    MatchReason,
    MatchStatus,
)
from dropsort.metadata.contracts import MovieCandidate


@pytest.fixture
def candidate() -> MovieCandidate:
    return MovieCandidate(
        provider="provider-a",
        external_id="1",
        title="Movie",
        original_title=None,
        year=2024,
        overview=None,
        rating=None,
        poster_reference=None,
    )


def test_match_models_are_immutable(candidate: MovieCandidate) -> None:
    score = CandidateScore(candidate, 1.0, (MatchReason.TITLE_EXACT,), ())
    decision = MatchDecision(
        MatchStatus.MATCHED,
        candidate,
        1.0,
        (MatchReason.TITLE_EXACT,),
        (score,),
    )

    with pytest.raises(FrozenInstanceError):
        score.score = 0.0  # type: ignore[misc]
    with pytest.raises(FrozenInstanceError):
        decision.confidence = 0.0  # type: ignore[misc]


@pytest.mark.parametrize("value", [-0.01, 1.01, float("nan"), float("inf")])
def test_candidate_score_rejects_invalid_confidence(
    candidate: MovieCandidate,
    value: float,
) -> None:
    with pytest.raises(ValueError, match="score"):
        CandidateScore(candidate, value, (), ())


def test_candidate_score_rejects_non_candidate() -> None:
    with pytest.raises(ValueError, match="candidate"):
        CandidateScore(None, 0.0, (), ())  # type: ignore[arg-type]


def test_candidate_score_rejects_unstructured_reasons(
    candidate: MovieCandidate,
) -> None:
    with pytest.raises(ValueError, match="reasons"):
        CandidateScore(candidate, 0.5, ("TITLE_EXACT",), ())  # type: ignore[arg-type]


def test_decision_rejects_non_enum_status(candidate: MovieCandidate) -> None:
    score = CandidateScore(candidate, 1.0, (MatchReason.TITLE_EXACT,), ())

    with pytest.raises(ValueError, match="status"):
        MatchDecision(
            "MATCHED",  # type: ignore[arg-type]
            candidate,
            1.0,
            (MatchReason.TITLE_EXACT,),
            (score,),
        )


def test_decision_rejects_non_candidate_selection(candidate: MovieCandidate) -> None:
    score = CandidateScore(candidate, 1.0, (), ())

    with pytest.raises(ValueError, match="candidate"):
        MatchDecision(
            MatchStatus.MATCHED,
            "not-a-candidate",  # type: ignore[arg-type]
            1.0,
            (),
            (score,),
        )


def test_decision_rejects_non_score_rankings(candidate: MovieCandidate) -> None:
    with pytest.raises(ValueError, match="ranked_candidates"):
        MatchDecision(
            MatchStatus.MATCHED,
            candidate,
            1.0,
            (),
            (candidate,),  # type: ignore[arg-type]
        )


def test_matched_decision_requires_candidate() -> None:
    with pytest.raises(ValueError, match="require a candidate"):
        MatchDecision(MatchStatus.MATCHED, None, 0.9, (), ())


def test_no_match_cannot_select_candidate(candidate: MovieCandidate) -> None:
    score = CandidateScore(candidate, 0.5, (), ())

    with pytest.raises(ValueError, match="NO_MATCH"):
        MatchDecision(MatchStatus.NO_MATCH, candidate, 0.5, (), (score,))


def test_review_requires_top_ranked_candidate(candidate: MovieCandidate) -> None:
    score = CandidateScore(candidate, 0.7, (), ())
    other = MovieCandidate(
        provider="provider-a",
        external_id="2",
        title="Other",
        original_title=None,
        year=2024,
        overview=None,
        rating=None,
        poster_reference=None,
    )

    with pytest.raises(ValueError, match="top-ranked"):
        MatchDecision(MatchStatus.REVIEW_REQUIRED, other, 0.7, (), (score,))
