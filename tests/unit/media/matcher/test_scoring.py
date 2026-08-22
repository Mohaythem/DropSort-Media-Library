from __future__ import annotations

import pytest

from dropsort.media.matcher import MatchReason, score_candidate
from dropsort.media.parser import MediaType, ParsedMedia
from dropsort.metadata.contracts import MovieCandidate


def _parsed(title: str | None, year: int | None) -> ParsedMedia:
    return ParsedMedia("movie.mkv", MediaType.MOVIE, title, year, None, None, None, ".mkv")


def _candidate(
    title: str,
    year: int | None,
    *,
    original_title: str | None = None,
) -> MovieCandidate:
    return MovieCandidate(
        provider="provider-a",
        external_id=f"{title}:{year}",
        title=title,
        original_title=original_title,
        year=year,
        overview=None,
        rating=None,
        poster_reference=None,
    )


def test_exact_title_and_year_produce_maximum_explainable_score() -> None:
    score = score_candidate(
        _parsed("The Dark Knight", 2008),
        _candidate("The Dark Knight", 2008),
    )

    assert score.score == 1.0
    assert score.reasons == (MatchReason.TITLE_EXACT, MatchReason.YEAR_EXACT)
    assert score.penalties == ()


def test_original_title_exact_match_is_explicit_and_strong() -> None:
    score = score_candidate(
        _parsed("Das Leben der Anderen", 2006),
        _candidate(
            "The Lives of Others",
            2006,
            original_title="Das Leben der Anderen",
        ),
    )

    assert score.score >= 0.9
    assert MatchReason.ORIGINAL_TITLE_EXACT in score.reasons
    assert MatchReason.TITLE_EXACT not in score.reasons


def test_year_conflict_strongly_reduces_an_exact_title_score() -> None:
    exact_year = score_candidate(_parsed("The Thing", 1982), _candidate("The Thing", 1982))
    wrong_year = score_candidate(_parsed("The Thing", 1982), _candidate("The Thing", 2011))

    assert wrong_year.score < exact_year.score
    assert wrong_year.score < 0.6
    assert MatchReason.YEAR_CONFLICT in wrong_year.penalties


def test_candidate_missing_year_reduces_certainty() -> None:
    complete = score_candidate(_parsed("Interstellar", 2014), _candidate("Interstellar", 2014))
    missing = score_candidate(_parsed("Interstellar", 2014), _candidate("Interstellar", None))

    assert missing.score < complete.score
    assert MatchReason.CANDIDATE_YEAR_MISSING in missing.penalties


def test_missing_parsed_year_does_not_invent_year_evidence() -> None:
    score = score_candidate(_parsed("Interstellar", None), _candidate("Interstellar", 2014))

    assert MatchReason.YEAR_EXACT not in score.reasons
    assert MatchReason.PARSED_YEAR_MISSING in score.penalties


def test_similar_but_wrong_title_remains_weak_despite_exact_year() -> None:
    score = score_candidate(_parsed("Interstellar", 2014), _candidate("Interstate", 2014))

    assert score.score < 0.6
    assert MatchReason.WEAK_TITLE_SIMILARITY in score.penalties


def test_strong_fuzzy_title_remains_below_auto_match_threshold() -> None:
    score = score_candidate(_parsed("Alien", 1986), _candidate("Aliens", 1986))

    assert MatchReason.TITLE_STRONG in score.reasons
    assert score.score < 0.85


def test_strong_fuzzy_original_title_is_explicit_but_conservative() -> None:
    score = score_candidate(
        _parsed("Das Leben der Andere", 2006),
        _candidate(
            "The Lives of Others",
            2006,
            original_title="Das Leben der Anderen",
        ),
    )

    assert MatchReason.ORIGINAL_TITLE_STRONG in score.reasons
    assert score.score < 0.85


@pytest.mark.parametrize(
    ("title", "year"),
    [("1917", 2019), ("Blade Runner 2049", 2017), ("2001 A Space Odyssey", 1968)],
)
def test_numeric_titles_are_preserved_as_title_evidence(title: str, year: int) -> None:
    score = score_candidate(_parsed(title, year), _candidate(title, year))

    assert score.score == 1.0
    assert MatchReason.TITLE_EXACT in score.reasons


@pytest.mark.parametrize(
    ("parsed_title", "candidate_title"),
    [("", "Movie"), (None, "Movie"), ("Movie", "Something Else")],
)
def test_score_is_always_in_documented_range(
    parsed_title: str | None,
    candidate_title: str,
) -> None:
    score = score_candidate(_parsed(parsed_title, 2024), _candidate(candidate_title, 2024))

    assert 0.0 <= score.score <= 1.0
