from __future__ import annotations

from dropsort.application.dto.library import MovieListItem
from dropsort.application.errors import LibraryQueryError, MovieNotFoundError
from dropsort.application.use_cases._library_mapping import to_list_item
from dropsort.library.movies import CatalogError, MovieLibraryReadRepository


class GetMovieListItem:
    """Read one local Library projection by stable MovieId."""

    def __init__(self, repository: MovieLibraryReadRepository) -> None:
        self._repository = repository

    def execute(self, movie_id: int) -> MovieListItem:
        if isinstance(movie_id, bool) or not isinstance(movie_id, int) or movie_id <= 0:
            raise ValueError("movie_id must be a positive integer")
        try:
            summary = self._repository.get_movie_summary(movie_id)
        except CatalogError as error:
            raise LibraryQueryError("could not read the local movie library") from error
        if summary is None:
            raise MovieNotFoundError(f"movie {movie_id} was not found")
        return to_list_item(summary)
