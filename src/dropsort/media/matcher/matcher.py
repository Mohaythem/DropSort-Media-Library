from __future__ import annotations

from collections.abc import Iterable

from dropsort.media.matcher.models import (
    CandidateScore,
    MatchDecision,
    MatchReason,
    MatchStatus,
)
from dropsort.media.matcher.normalization import normalize_title
from dropsort.media.matcher.scoring import score_candidate
from dropsort.media.parser import MediaType, ParsedMedia
from dropsort.metadata.contracts import MovieCandidate


# These thresholds classify identity evidence only. They never authorize file operations.
AUTO_MATCH_THRESHOLD = 0.85
REVIEW_THRESHOLD = 0.60
AMBIGUITY_MARGIN = 0.08


class MovieMatcher:
    def match(
        self,
        parsed: ParsedMedia,
        candidates: Iterable[MovieCandidate],
    ) -> MatchDecision:
        if parsed.media_type is not MediaType.MOVIE:
            return _empty_decision(MatchReason.MEDIA_TYPE_NOT_MOVIE)
        if not normalize_title(parsed.title):
            return _empty_decision(MatchReason.INVALID_PARSED_TITLE)

        unique_candidates = _deduplicate(candidates)
        if not unique_candidates:
            return _empty_decision(MatchReason.NO_CANDIDATES)

        ranked = tuple(
            sorted(
                (score_candidate(parsed, candidate) for candidate in unique_candidates),
                key=_score_sort_key,
            )
        )
        winner = ranked[0]
        evidence = winner.reasons + winner.penalties
        if winner.score < REVIEW_THRESHOLD:
            return MatchDecision(
                status=MatchStatus.NO_MATCH,
                candidate=None,
                confidence=winner.score,
                reasons=evidence,
                ranked_candidates=ranked,
            )

        if _is_ambiguous(ranked):
            return MatchDecision(
                status=MatchStatus.REVIEW_REQUIRED,
                candidate=winner.candidate,
                confidence=winner.score,
                reasons=evidence + (MatchReason.AMBIGUOUS_TOP_CANDIDATES,),
                ranked_candidates=ranked,
            )

        if winner.score < AUTO_MATCH_THRESHOLD:
            return MatchDecision(
                status=MatchStatus.REVIEW_REQUIRED,
                candidate=winner.candidate,
                confidence=winner.score,
                reasons=evidence + (MatchReason.BELOW_AUTO_MATCH_THRESHOLD,),
                ranked_candidates=ranked,
            )

        return MatchDecision(
            status=MatchStatus.MATCHED,
            candidate=winner.candidate,
            confidence=winner.score,
            reasons=evidence + (MatchReason.CLEAR_WINNER,),
            ranked_candidates=ranked,
        )


def _empty_decision(reason: MatchReason) -> MatchDecision:
    return MatchDecision(
        status=MatchStatus.NO_MATCH,
        candidate=None,
        confidence=0.0,
        reasons=(reason,),
        ranked_candidates=(),
    )


def _is_ambiguous(ranked: tuple[CandidateScore, ...]) -> bool:
    return (
        len(ranked) > 1
        and ranked[0].score - ranked[1].score < AMBIGUITY_MARGIN
    )


def _deduplicate(candidates: Iterable[MovieCandidate]) -> tuple[MovieCandidate, ...]:
    unique: dict[tuple[str, str], MovieCandidate] = {}
    for candidate in candidates:
        identity = (candidate.provider, candidate.external_id)
        current = unique.get(identity)
        if current is None or _candidate_sort_key(candidate) < _candidate_sort_key(current):
            unique[identity] = candidate
    return tuple(unique.values())


def _score_sort_key(score: CandidateScore) -> tuple[object, ...]:
    return (-score.score, *_candidate_sort_key(score.candidate))


def _candidate_sort_key(candidate: MovieCandidate) -> tuple[object, ...]:
    return (
        candidate.provider.casefold(),
        candidate.provider,
        candidate.external_id,
        normalize_title(candidate.title),
        candidate.title,
        normalize_title(candidate.original_title),
        candidate.original_title or "",
        candidate.year if candidate.year is not None else -1,
        candidate.overview or "",
        candidate.rating if candidate.rating is not None else -1.0,
        candidate.poster_reference or "",
    )

