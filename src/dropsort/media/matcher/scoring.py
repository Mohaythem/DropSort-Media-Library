from __future__ import annotations

from dropsort.media.matcher.models import CandidateScore, MatchReason
from dropsort.media.matcher.normalization import normalize_title, title_similarity
from dropsort.media.parser import ParsedMedia
from dropsort.metadata.contracts import MovieCandidate


EXACT_TITLE_SCORE = 0.86
EXACT_ORIGINAL_TITLE_SCORE = 0.82
STRONG_TITLE_SIMILARITY = 0.90
STRONG_TITLE_BASE_SCORE = 0.55
STRONG_TITLE_SIMILARITY_WEIGHT = 0.14
YEAR_EXACT_BONUS = 0.14
YEAR_CONFLICT_PENALTY = 0.48
CANDIDATE_YEAR_MISSING_PENALTY = 0.08


def score_candidate(parsed: ParsedMedia, candidate: MovieCandidate) -> CandidateScore:
    reasons: list[MatchReason] = []
    penalties: list[MatchReason] = []
    title_score, title_reason = _title_score(parsed.title, candidate)
    if title_reason is MatchReason.WEAK_TITLE_SIMILARITY:
        penalties.append(title_reason)
    else:
        reasons.append(title_reason)

    score = title_score
    if parsed.year is None:
        penalties.append(MatchReason.PARSED_YEAR_MISSING)
    elif candidate.year is None:
        score -= CANDIDATE_YEAR_MISSING_PENALTY
        penalties.append(MatchReason.CANDIDATE_YEAR_MISSING)
    elif parsed.year == candidate.year:
        score += YEAR_EXACT_BONUS
        reasons.append(MatchReason.YEAR_EXACT)
    else:
        score -= YEAR_CONFLICT_PENALTY
        penalties.append(MatchReason.YEAR_CONFLICT)

    return CandidateScore(
        candidate=candidate,
        score=round(min(1.0, max(0.0, score)), 6),
        reasons=tuple(reasons),
        penalties=tuple(penalties),
    )


def _title_score(
    parsed_title: str | None,
    candidate: MovieCandidate,
) -> tuple[float, MatchReason]:
    normalized_parsed = normalize_title(parsed_title)
    if not normalized_parsed:
        return 0.0, MatchReason.WEAK_TITLE_SIMILARITY

    candidate_similarity = title_similarity(normalized_parsed, candidate.title)
    original_similarity = title_similarity(normalized_parsed, candidate.original_title)
    if candidate_similarity == 1.0:
        return EXACT_TITLE_SCORE, MatchReason.TITLE_EXACT
    if original_similarity == 1.0:
        return EXACT_ORIGINAL_TITLE_SCORE, MatchReason.ORIGINAL_TITLE_EXACT

    if candidate_similarity >= original_similarity:
        similarity = candidate_similarity
        strong_reason = MatchReason.TITLE_STRONG
    else:
        similarity = original_similarity
        strong_reason = MatchReason.ORIGINAL_TITLE_STRONG

    if similarity >= STRONG_TITLE_SIMILARITY:
        return (
            STRONG_TITLE_BASE_SCORE + STRONG_TITLE_SIMILARITY_WEIGHT * similarity,
            strong_reason,
        )
    return 0.50 * similarity, MatchReason.WEAK_TITLE_SIMILARITY
