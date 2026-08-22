from __future__ import annotations

from collections.abc import Iterable

from dropsort.application.dto.library import MovieListItem


def movie_matches_query(item: MovieListItem, query: str) -> bool:
    """Match only local MovieListItem fields; this function has no I/O boundary."""

    terms = tuple(part.casefold() for part in query.split() if part.strip())
    if not terms:
        return True
    searchable = " ".join(
        value
        for value in (item.title, item.original_title or "", str(item.year or ""))
        if value
    ).casefold()
    return all(term in searchable for term in terms)


def filter_movie_items(
    items: Iterable[MovieListItem], query: str
) -> tuple[MovieListItem, ...]:
    return tuple(item for item in items if movie_matches_query(item, query))


def movie_search_suggestions(items: Iterable[MovieListItem]) -> tuple[str, ...]:
    values: list[str] = []
    seen: set[str] = set()
    for item in items:
        for value in (item.title, item.original_title or ""):
            normalized = value.strip()
            key = normalized.casefold()
            if normalized and key not in seen:
                seen.add(key)
                values.append(normalized)
    return tuple(values)
