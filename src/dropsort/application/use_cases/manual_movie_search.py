from __future__ import annotations

from dropsort.application.dto.manual_search import (
    ManualMovieSearchRequest,
    ManualMovieSearchResult,
)
from dropsort.metadata.contracts import MetadataProvider, MovieCandidate


MAX_MANUAL_SEARCH_RESULTS = 5


class ManualMovieSearch:
    """Provider lookup only; it never selects or imports a movie."""

    def __init__(self, provider: MetadataProvider) -> None:
        self._provider = provider

    def execute(self, title: str, year: int | str | None = None) -> ManualMovieSearchResult:
        parsed_year: int | None
        if year is None or (isinstance(year, str) and not year.strip()):
            parsed_year = None
        elif isinstance(year, str):
            if not year.isdigit() or len(year) != 4:
                raise ValueError("year must be a four-digit movie year")
            parsed_year = int(year)
        elif isinstance(year, int) and not isinstance(year, bool):
            parsed_year = year
        else:
            raise ValueError("year must be a four-digit movie year")
        request = ManualMovieSearchRequest(title, parsed_year)
        raw_candidates = self._provider.search_movies(request.to_provider_query())
        deduplicated: list[MovieCandidate] = []
        seen: set[tuple[str, str]] = set()
        for candidate in raw_candidates:
            key = (candidate.provider, candidate.external_id)
            if key in seen:
                continue
            seen.add(key)
            deduplicated.append(candidate)
        return ManualMovieSearchResult(
            request,
            tuple(deduplicated[:MAX_MANUAL_SEARCH_RESULTS]),
        )
