from __future__ import annotations

from pathlib import Path
import re

from dropsort.media.parser import ParsedMedia, parse_media_filename
from dropsort.metadata.contracts import MovieSearchQuery


MAX_MOVIE_SEARCH_QUERIES = 4

_LEADING_SITE = re.compile(
    r"^\s*(?:www\.)?[\w-]+\.(?:com|net|org|io|tv|me|co)\b[\s._-]*",
    re.IGNORECASE,
)
_EDITION_SUFFIX = re.compile(
    r"\s+(?:(?:\d{1,2}(?:st|nd|rd|th)\s+)?anniversary\s+edition|"
    r"(?:extended|special|ultimate|collector'?s|director'?s|theatrical)\s+"
    r"(?:edition|cut)|remastered(?:\s+edition)?)\s*$",
    re.IGNORECASE,
)


def movie_search_queries(parsed: ParsedMedia) -> tuple[MovieSearchQuery, ...]:
    """Return a small stable search plan; these variants never imply a match."""
    if parsed.title is None:
        return ()
    titles = _deduplicate_text((parsed.title, _clean_fallback_title(parsed)))
    candidates = [MovieSearchQuery(title, parsed.year) for title in titles]
    if parsed.year is not None:
        candidates.extend(MovieSearchQuery(title, None) for title in titles)
    return _deduplicate_queries(candidates)[:MAX_MOVIE_SEARCH_QUERIES]


def _clean_fallback_title(parsed: ParsedMedia) -> str:
    original = Path(parsed.original_name).name
    without_site = _LEADING_SITE.sub("", original, count=1)
    reparsed = parse_media_filename(without_site)
    title = reparsed.title or parsed.title or ""
    return _EDITION_SUFFIX.sub("", title).strip() or parsed.title


def _deduplicate_text(values: tuple[str, ...]) -> tuple[str, ...]:
    output: list[str] = []
    seen: set[str] = set()
    for value in values:
        normalized = " ".join(value.split())
        identity = normalized.casefold()
        if normalized and identity not in seen:
            output.append(normalized)
            seen.add(identity)
    return tuple(output)


def _deduplicate_queries(
    values: list[MovieSearchQuery],
) -> tuple[MovieSearchQuery, ...]:
    output: list[MovieSearchQuery] = []
    seen: set[tuple[str, int | None]] = set()
    for value in values:
        identity = (value.title.casefold(), value.year)
        if identity not in seen:
            output.append(value)
            seen.add(identity)
    return tuple(output)
