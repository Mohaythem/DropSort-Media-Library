from __future__ import annotations

from dropsort.application.dto.library import MovieListItem, MovieListQuery
from dropsort.application.errors import LibraryQueryError
from dropsort.application.use_cases._library_mapping import to_list_item
from dropsort.library.movies import CatalogError, MovieLibraryReadRepository


class ListMovies:
    def __init__(self, repository: MovieLibraryReadRepository) -> None:
        self._repository = repository

    def execute(self, query: MovieListQuery | None = None) -> tuple[MovieListItem, ...]:
        request = query or MovieListQuery()
        try:
            summaries = self._repository.list_movies(
                limit=request.limit,
                offset=request.offset,
            )
        except CatalogError as error:
            raise LibraryQueryError("could not read the local movie library") from error
        return tuple(to_list_item(summary) for summary in summaries)
