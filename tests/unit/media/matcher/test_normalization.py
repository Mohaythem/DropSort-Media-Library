from __future__ import annotations

import pytest

from dropsort.media.matcher import normalize_title, title_similarity


@pytest.mark.parametrize(
    ("value", "expected"),
    [
        ("The Dark Knight", "the dark knight"),
        ("  THE   DARK KNIGHT  ", "the dark knight"),
        ("Spider-Man", "spider man"),
        ("Wall-E", "wall e"),
        ("Mission: Impossible", "mission impossible"),
        ("Blade_Runner.2049", "blade runner 2049"),
        ("2001: A Space Odyssey", "2001 a space odyssey"),
        ("ＡＢＣ １２３", "abc 123"),
        ("", ""),
        (None, ""),
    ],
)
def test_title_normalization_is_conservative_and_stable(
    value: str | None,
    expected: str,
) -> None:
    assert normalize_title(value) == expected


def test_normalization_does_not_remove_articles_or_title_numbers() -> None:
    assert normalize_title("The 1917") == "the 1917"
    assert normalize_title("A 2001 Story") == "a 2001 story"


def test_similarity_uses_normalized_titles_and_stays_bounded() -> None:
    assert title_similarity("Spider Man", "Spider-Man") == 1.0
    assert title_similarity("Mission Impossible", "Mission: Impossible") == 1.0
    assert 0.0 <= title_similarity("Interstellar", "Interstate") <= 1.0
    assert title_similarity("", "Movie") == 0.0

