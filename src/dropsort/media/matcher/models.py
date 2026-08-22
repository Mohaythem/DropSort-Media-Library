from __future__ import annotations

from dataclasses import dataclass
from enum import StrEnum
import math

from dropsort.metadata.contracts import MovieCandidate


class MatchStatus(StrEnum):
    """Informational identity decision; never filesystem authorization."""

    MATCHED = "MATCHED"
    REVIEW_REQUIRED = "REVIEW_REQUIRED"
    NO_MATCH = "NO_MATCH"


class MatchReason(StrEnum):
    TITLE_EXACT = "TITLE_EXACT"
    TITLE_STRONG = "TITLE_STRONG"
    ORIGINAL_TITLE_EXACT = "ORIGINAL_TITLE_EXACT"
    ORIGINAL_TITLE_STRONG = "ORIGINAL_TITLE_STRONG"
    WEAK_TITLE_SIMILARITY = "WEAK_TITLE_SIMILARITY"
    YEAR_EXACT = "YEAR_EXACT"
    YEAR_CONFLICT = "YEAR_CONFLICT"
    PARSED_YEAR_MISSING = "PARSED_YEAR_MISSING"
    CANDIDATE_YEAR_MISSING = "CANDIDATE_YEAR_MISSING"
    AMBIGUOUS_TOP_CANDIDATES = "AMBIGUOUS_TOP_CANDIDATES"
    BELOW_AUTO_MATCH_THRESHOLD = "BELOW_AUTO_MATCH_THRESHOLD"
    NO_CANDIDATES = "NO_CANDIDATES"
    INVALID_PARSED_TITLE = "INVALID_PARSED_TITLE"
    MEDIA_TYPE_NOT_MOVIE = "MEDIA_TYPE_NOT_MOVIE"
    CLEAR_WINNER = "CLEAR_WINNER"


@dataclass(frozen=True, slots=True)
class CandidateScore:
    candidate: MovieCandidate
    score: float
    reasons: tuple[MatchReason, ...]
    penalties: tuple[MatchReason, ...]

    def __post_init__(self) -> None:
        if not isinstance(self.candidate, MovieCandidate):
            raise ValueError("candidate must be a MovieCandidate")
        _validate_confidence(self.score, "score")
        _validate_reason_tuple(self.reasons, "reasons")
        _validate_reason_tuple(self.penalties, "penalties")


@dataclass(frozen=True, slots=True)
class MatchDecision:
    """A match opinion only. It grants no permission to organize or mutate files."""

    status: MatchStatus
    candidate: MovieCandidate | None
    confidence: float
    reasons: tuple[MatchReason, ...]
    ranked_candidates: tuple[CandidateScore, ...]

    def __post_init__(self) -> None:
        if not isinstance(self.status, MatchStatus):
            raise ValueError("status must be a MatchStatus")
        if self.candidate is not None and not isinstance(self.candidate, MovieCandidate):
            raise ValueError("candidate must be a MovieCandidate or None")
        _validate_confidence(self.confidence, "confidence")
        _validate_reason_tuple(self.reasons, "reasons")
        if not isinstance(self.ranked_candidates, tuple) or any(
            not isinstance(item, CandidateScore) for item in self.ranked_candidates
        ):
            raise ValueError("ranked_candidates must contain CandidateScore values")
        if self.status is MatchStatus.NO_MATCH and self.candidate is not None:
            raise ValueError("NO_MATCH cannot select a candidate")
        if self.status is not MatchStatus.NO_MATCH and self.candidate is None:
            raise ValueError("MATCHED and REVIEW_REQUIRED require a candidate")
        if self.candidate is not None and (
            not self.ranked_candidates
            or self.ranked_candidates[0].candidate != self.candidate
        ):
            raise ValueError("selected candidate must be the top-ranked candidate")


def _validate_confidence(value: float, field_name: str) -> None:
    if (
        isinstance(value, bool)
        or not isinstance(value, (int, float))
        or not math.isfinite(value)
        or not 0.0 <= value <= 1.0
    ):
        raise ValueError(f"{field_name} must be a finite number from 0 through 1")


def _validate_reason_tuple(value: tuple[MatchReason, ...], field_name: str) -> None:
    if not isinstance(value, tuple) or any(
        not isinstance(item, MatchReason) for item in value
    ):
        raise ValueError(f"{field_name} must contain MatchReason values")
