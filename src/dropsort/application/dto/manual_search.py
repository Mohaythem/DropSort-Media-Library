from __future__ import annotations

from dataclasses import dataclass

from dropsort.metadata.contracts import MovieCandidate, MovieSearchQuery


@dataclass(frozen=True, slots=True)
class ManualMovieSearchRequest:
    title: str
    year: int | None = None

    def __post_init__(self) -> None:
        if not isinstance(self.title, str) or not self.title.strip():
            raise ValueError("search title must be non-empty")
        title = " ".join(self.title.split())
        if not title:
            raise ValueError("search title must be non-empty")
        object.__setattr__(self, "title", title)
        if self.year is not None and (
            isinstance(self.year, bool)
            or not isinstance(self.year, int)
            or not 1000 <= self.year <= 9999
        ):
            raise ValueError("year must be a four-digit movie year")

    def to_provider_query(self) -> MovieSearchQuery:
        return MovieSearchQuery(self.title, self.year)


@dataclass(frozen=True, slots=True)
class ManualMovieSearchResult:
    query: ManualMovieSearchRequest
    candidates: tuple[MovieCandidate, ...]

    def __post_init__(self) -> None:
        if not isinstance(self.candidates, tuple):
            raise ValueError("candidates must be a tuple")
