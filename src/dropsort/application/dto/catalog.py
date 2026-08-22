from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime
from pathlib import Path

from dropsort.library.movies import MediaFile, Movie
from dropsort.media.parser.models import MediaType, ParsedMedia
from dropsort.metadata.contracts import MovieMetadata


@dataclass(frozen=True, slots=True)
class RegisterMovieFileCommand:
    """Explicit intent to associate verified file facts with normalized movie metadata."""

    metadata: MovieMetadata
    parsed_media: ParsedMedia
    file_path: Path
    file_size: int
    observed_at: datetime

    def __post_init__(self) -> None:
        if not isinstance(self.metadata, MovieMetadata):
            raise ValueError("metadata must be MovieMetadata")
        if not isinstance(self.parsed_media, ParsedMedia):
            raise ValueError("parsed_media must be ParsedMedia")
        if self.parsed_media.media_type is not MediaType.MOVIE:
            raise ValueError("only MOVIE media can be registered in the movie catalog")
        if not isinstance(self.file_path, Path) or not self.file_path.is_absolute():
            raise ValueError("file_path must be an absolute Path")
        if (
            isinstance(self.file_size, bool)
            or not isinstance(self.file_size, int)
            or self.file_size < 0
        ):
            raise ValueError("file_size must be a non-negative integer")
        if not isinstance(self.observed_at, datetime) or self.observed_at.tzinfo is None:
            raise ValueError("observed_at must be a timezone-aware datetime")


@dataclass(frozen=True, slots=True)
class MovieFileIngestionResult:
    movie: Movie
    media_file: MediaFile

