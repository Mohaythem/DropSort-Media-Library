from __future__ import annotations

from dropsort.application.dto.library import MovieDetails
from dropsort.application.errors import LibraryQueryError, MovieNotFoundError
from dropsort.application.use_cases._library_mapping import to_movie_details
from dropsort.library.movies import CatalogError, MovieLibraryReadRepository


class GetMovieDetails:
    def __init__(self, repository: MovieLibraryReadRepository) -> None:
        self._repository = repository

    def execute(self, movie_id: int) -> MovieDetails:
        if isinstance(movie_id, bool) or not isinstance(movie_id, int) or movie_id <= 0:
            raise ValueError("movie_id must be a positive integer")
        try:
            snapshot = self._repository.get_movie_details(movie_id)
        except CatalogError as error:
            raise LibraryQueryError("could not read the local movie library") from error
        if snapshot is None:
            raise MovieNotFoundError(f"movie {movie_id} was not found")
        return to_movie_details(snapshot)
