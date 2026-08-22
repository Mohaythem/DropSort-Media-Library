from __future__ import annotations

from itertools import permutations

import pytest

from dropsort.media.matcher import (
    AMBIGUITY_MARGIN,
    AUTO_MATCH_THRESHOLD,
    REVIEW_THRESHOLD,
    MatchReason,
    MatchStatus,
    MovieMatcher,
)
from dropsort.media.parser import MediaType, ParsedMedia
from dropsort.metadata.contracts import MovieCandidate


def _parsed(
    title: str | None,
    year: int | None,
    *,
    media_type: MediaType = MediaType.MOVIE,
) -> ParsedMedia:
    return ParsedMedia("movie.mkv", media_type, title, year, None, None, None, ".mkv")


def _candidate(
    external_id: str,
    title: str,
    year: int | None,
    *,
    provider: str = "provider-a",
    original_title: str | None = None,
) -> MovieCandidate:
    return MovieCandidate(
        provider=provider,
        external_id=external_id,
        title=title,
        original_title=original_title,
        year=year,
        overview=None,
        rating=None,
        poster_reference=None,
    )


@pytest.mark.parametrize(
    ("title", "year"),
    [("The Dark Knight", 2008), ("Interstellar", 2014), ("1917", 2019)],
)
def test_exact_title_and_year_are_informationally_matched(title: str, year: int) -> None:
    candidate = _candidate("1", title, year)

    decision = MovieMatcher().match(_parsed(title, year), [candidate])

    assert decision.status is MatchStatus.MATCHED
    assert decision.candidate == candidate
    assert decision.confidence == 1.0
    assert MatchReason.TITLE_EXACT in decision.reasons
    assert MatchReason.YEAR_EXACT in decision.reasons


def test_remake_year_selects_the_correct_candidate() -> None:
    original = _candidate("1982", "The Thing", 1982)
    remake = _candidate("2011", "The Thing", 2011)

    decision = MovieMatcher().match(_parsed("The Thing", 1982), [remake, original])

    assert decision.status is MatchStatus.MATCHED
    assert decision.candidate == original
    assert decision.ranked_candidates[0].candidate == original
    assert MatchReason.YEAR_CONFLICT in decision.ranked_candidates[1].penalties


def test_missing_year_with_multiple_exact_title_versions_requires_review() -> None:
    candidates = [
        _candidate("1982", "The Thing", 1982),
        _candidate("2011", "The Thing", 2011),
    ]

    decision = MovieMatcher().match(_parsed("The Thing", None), candidates)

    assert decision.status is MatchStatus.REVIEW_REQUIRED
    assert MatchReason.AMBIGUOUS_TOP_CANDIDATES in decision.reasons


def test_missing_year_with_one_clear_exact_title_can_match_informationally() -> None:
    candidate = _candidate("2014", "Interstellar", 2014)

    decision = MovieMatcher().match(_parsed("Interstellar", None), [candidate])

    assert decision.status is MatchStatus.MATCHED
    assert decision.confidence >= AUTO_MATCH_THRESHOLD
    assert MatchReason.PARSED_YEAR_MISSING in decision.reasons


def test_exact_title_with_wrong_year_is_not_matched() -> None:
    decision = MovieMatcher().match(
        _parsed("The Thing", 1982),
        [_candidate("2011", "The Thing", 2011)],
    )

    assert decision.status is MatchStatus.NO_MATCH
    assert decision.candidate is None
    assert MatchReason.YEAR_CONFLICT in decision.reasons


def test_similar_but_wrong_title_is_not_matched_by_year_alone() -> None:
    decision = MovieMatcher().match(
        _parsed("Interstellar", 2014),
        [_candidate("1", "Interstate", 2014)],
    )

    assert decision.status is MatchStatus.NO_MATCH
    assert decision.confidence < REVIEW_THRESHOLD


def test_even_strong_fuzzy_title_similarity_requires_review() -> None:
    decision = MovieMatcher().match(
        _parsed("Alien", 1986),
        [_candidate("1", "Aliens", 1986)],
    )

    assert decision.status is MatchStatus.REVIEW_REQUIRED
    assert decision.confidence < AUTO_MATCH_THRESHOLD
    assert MatchReason.TITLE_STRONG in decision.reasons


@pytest.mark.parametrize(
    ("parsed_title", "candidate_title"),
    [
        ("Spider Man", "Spider-Man"),
        ("Wall E", "WALL-E"),
        ("Mission Impossible", "Mission: Impossible"),
        ("Blade Runner 2049", "Blade Runner 2049"),
    ],
)
def test_punctuation_and_numeric_titles_match(
    parsed_title: str,
    candidate_title: str,
) -> None:
    decision = MovieMatcher().match(
        _parsed(parsed_title, 2024),
        [_candidate("1", candidate_title, 2024)],
    )

    assert decision.status is MatchStatus.MATCHED


def test_original_title_can_win_without_replacing_localized_title() -> None:
    candidate = _candidate(
        "1",
        "The Lives of Others",
        2006,
        original_title="Das Leben der Anderen",
    )

    decision = MovieMatcher().match(_parsed("Das Leben der Anderen", 2006), [candidate])

    assert decision.status is MatchStatus.MATCHED
    assert decision.candidate.title == "The Lives of Others"
    assert MatchReason.ORIGINAL_TITLE_EXACT in decision.reasons


def test_no_candidates_returns_controlled_no_match() -> None:
    decision = MovieMatcher().match(_parsed("Movie", 2024), [])

    assert decision.status is MatchStatus.NO_MATCH
    assert decision.candidate is None
    assert decision.confidence == 0.0
    assert decision.ranked_candidates == ()
    assert decision.reasons == (MatchReason.NO_CANDIDATES,)


@pytest.mark.parametrize("title", [None, "", "   "])
def test_empty_or_invalid_parsed_title_returns_controlled_no_match(
    title: str | None,
) -> None:
    decision = MovieMatcher().match(
        _parsed(title, 2024),
        [_candidate("1", "Movie", 2024)],
    )

    assert decision.status is MatchStatus.NO_MATCH
    assert decision.candidate is None
    assert decision.reasons == (MatchReason.INVALID_PARSED_TITLE,)


def test_non_movie_input_is_never_matched() -> None:
    decision = MovieMatcher().match(
        _parsed("Show", 2024, media_type=MediaType.TV_EPISODE),
        [_candidate("1", "Show", 2024)],
    )

    assert decision.status is MatchStatus.NO_MATCH
    assert decision.reasons == (MatchReason.MEDIA_TYPE_NOT_MOVIE,)


def test_close_competing_candidates_require_review() -> None:
    candidates = [
        _candidate("localized", "Movie Name", 2024),
        _candidate("original", "Localized Name", 2024, original_title="Movie Name"),
    ]

    decision = MovieMatcher().match(_parsed("Movie Name", 2024), candidates)

    score_gap = (
        decision.ranked_candidates[0].score
        - decision.ranked_candidates[1].score
    )
    assert score_gap < AMBIGUITY_MARGIN
    assert decision.status is MatchStatus.REVIEW_REQUIRED
    assert MatchReason.AMBIGUOUS_TOP_CANDIDATES in decision.reasons


def test_candidate_order_does_not_change_decision_or_ranking() -> None:
    candidates = (
        _candidate("1982", "The Thing", 1982),
        _candidate("2011", "The Thing", 2011),
        _candidate("other", "A Different Thing", 1982),
    )
    matcher = MovieMatcher()

    decisions = {
        matcher.match(_parsed("The Thing", 1982), ordering)
        for ordering in permutations(candidates)
    }

    assert len(decisions) == 1


def test_duplicate_identity_does_not_create_false_ambiguity() -> None:
    candidate = _candidate("1", "Interstellar", 2014)
    matcher = MovieMatcher()

    unique = matcher.match(_parsed("Interstellar", 2014), [candidate])
    duplicated = matcher.match(_parsed("Interstellar", 2014), [candidate, candidate])

    assert duplicated == unique
    assert len(duplicated.ranked_candidates) == 1


def test_external_ids_are_only_unique_within_provider_namespace() -> None:
    candidates = [
        _candidate("1", "Movie", 2024, provider="provider-a"),
        _candidate("1", "Movie", 2024, provider="provider-b"),
    ]

    decision = MovieMatcher().match(_parsed("Movie", 2024), candidates)

    assert len(decision.ranked_candidates) == 2
    assert decision.status is MatchStatus.REVIEW_REQUIRED


def test_missing_candidate_year_requires_review_even_for_exact_title() -> None:
    decision = MovieMatcher().match(
        _parsed("Movie", 2024),
        [_candidate("1", "Movie", None)],
    )

    assert decision.status is MatchStatus.REVIEW_REQUIRED
    assert MatchReason.CANDIDATE_YEAR_MISSING in decision.reasons


def test_exact_original_title_without_year_requires_review() -> None:
    decision = MovieMatcher().match(
        _parsed("Das Leben der Anderen", None),
        [
            _candidate(
                "1",
                "The Lives of Others",
                2006,
                original_title="Das Leben der Anderen",
            )
        ],
    )

    assert decision.status is MatchStatus.REVIEW_REQUIRED
    assert MatchReason.ORIGINAL_TITLE_EXACT in decision.reasons


def test_rating_overview_and_poster_do_not_affect_identity_score() -> None:
    sparse = _candidate("1", "Movie", 2024)
    rich = MovieCandidate(
        provider="provider-a",
        external_id="2",
        title="Movie",
        original_title=None,
        year=2024,
        overview="Highly promoted result",
        rating=10.0,
        poster_reference="/popular.jpg",
    )

    decision = MovieMatcher().match(_parsed("Movie", 2024), [sparse, rich])

    assert decision.ranked_candidates[0].score == decision.ranked_candidates[1].score
    assert decision.status is MatchStatus.REVIEW_REQUIRED


def test_thresholds_are_ordered_and_conservative() -> None:
    assert 0.0 < REVIEW_THRESHOLD < AUTO_MATCH_THRESHOLD <= 1.0
    assert 0.0 < AMBIGUITY_MARGIN < AUTO_MATCH_THRESHOLD


def test_matcher_is_deterministic_for_repeated_calls() -> None:
    parsed = _parsed("Movie", 2024)
    candidates = [_candidate("1", "Movie", 2024)]
    matcher = MovieMatcher()

    assert matcher.match(parsed, candidates) == matcher.match(parsed, candidates)
