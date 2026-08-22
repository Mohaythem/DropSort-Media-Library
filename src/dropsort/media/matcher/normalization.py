from __future__ import annotations

from difflib import SequenceMatcher
import unicodedata


def normalize_title(value: str | None) -> str:
    """Normalize case, compatibility characters, punctuation, and whitespace."""

    if not isinstance(value, str):
        return ""
    normalized = unicodedata.normalize("NFKC", value).casefold()
    characters = (
        character if character.isalnum() else " "
        for character in normalized
    )
    return " ".join("".join(characters).split())


def title_similarity(left: str | None, right: str | None) -> float:
    normalized_left = normalize_title(left)
    normalized_right = normalize_title(right)
    if not normalized_left or not normalized_right:
        return 0.0
    return SequenceMatcher(None, normalized_left, normalized_right, autojunk=False).ratio()

