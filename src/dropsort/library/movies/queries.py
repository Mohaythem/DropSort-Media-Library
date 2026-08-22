from __future__ import annotations

from dataclasses import dataclass

from dropsort.library.movies.models import MediaFile, Movie


@dataclass(frozen=True, slots=True)
class MovieSummary:
    """Domain read projection for a movie and its physical-file count."""

    movie: Movie
    media_file_count: int
    missing_file_count: int = 0

    def __post_init__(self) -> None:
        if not isinstance(self.movie, Movie):
            raise ValueError("movie must be Movie")
        if (
            isinstance(self.media_file_count, bool)
            or not isinstance(self.media_file_count, int)
            or self.media_file_count < 0
        ):
            raise ValueError("media_file_count must be a non-negative integer")
        if (
            isinstance(self.missing_file_count, bool)
            or not isinstance(self.missing_file_count, int)
            or not 0 <= self.missing_file_count <= self.media_file_count
        ):
            raise ValueError("missing_file_count must be within the media-file count")


@dataclass(frozen=True, slots=True)
class MovieDetailsSnapshot:
    """Coherent local-catalog snapshot of one movie and its physical files."""

    movie: Movie
    media_files: tuple[MediaFile, ...]

    def __post_init__(self) -> None:
        if not isinstance(self.movie, Movie):
            raise ValueError("movie must be Movie")
        if not isinstance(self.media_files, tuple) or any(
            not isinstance(media_file, MediaFile) for media_file in self.media_files
        ):
            raise ValueError("media_files must be a tuple of MediaFile values")
        if any(media_file.movie_id != self.movie.id for media_file in self.media_files):
            raise ValueError("all media files must belong to the snapshot movie")
