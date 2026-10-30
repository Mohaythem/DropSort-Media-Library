from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime
from enum import StrEnum
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
class RegisterLocalMovieFileCommand:
    """Explicit user authorization to register one local movie file without metadata."""

    parsed_media: ParsedMedia
    file_path: Path
    file_size: int
    observed_at: datetime

    def __post_init__(self) -> None:
        _validate_local_file_command(
            self.parsed_media,
            self.file_path,
            self.file_size,
            self.observed_at,
        )


class MetadataEnrichmentOutcome(StrEnum):
    READY = "READY"
    PENDING = "PENDING"
    FAILED = "FAILED"
    NEEDS_MATCH = "NEEDS_MATCH"
    IDENTITY_COLLISION = "IDENTITY_COLLISION"


@dataclass(frozen=True, slots=True)
class MovieMetadataEnrichmentResult:
    movie: Movie
    outcome: MetadataEnrichmentOutcome
    collision_movie_id: int | None = None
    failure_code: str | None = None

    def __post_init__(self) -> None:
        if not isinstance(self.movie, Movie):
            raise ValueError("movie must be Movie")
        if not isinstance(self.outcome, MetadataEnrichmentOutcome):
            raise ValueError("outcome must be MetadataEnrichmentOutcome")
        if self.outcome is MetadataEnrichmentOutcome.IDENTITY_COLLISION:
            if (
                isinstance(self.collision_movie_id, bool)
                or not isinstance(self.collision_movie_id, int)
                or self.collision_movie_id <= 0
                or self.collision_movie_id == self.movie.id
            ):
                raise ValueError("identity collision must expose the other MovieId")
        elif self.collision_movie_id is not None:
            raise ValueError("only identity collision may expose another MovieId")



@dataclass(frozen=True, slots=True)
class MovieFileIngestionResult:
    movie: Movie
    media_file: MediaFile
    enrichment: MovieMetadataEnrichmentResult | None = None


def _validate_local_file_command(
    parsed_media: ParsedMedia,
    file_path: Path,
    file_size: int,
    observed_at: datetime,
) -> None:
    if not isinstance(parsed_media, ParsedMedia):
        raise ValueError("parsed_media must be ParsedMedia")
    if parsed_media.media_type is not MediaType.MOVIE:
        raise ValueError("only MOVIE media can be registered in the movie catalog")
    if not isinstance(file_path, Path) or not file_path.is_absolute():
        raise ValueError("file_path must be an absolute Path")
    if isinstance(file_size, bool) or not isinstance(file_size, int) or file_size < 0:
        raise ValueError("file_size must be a non-negative integer")
    if not isinstance(observed_at, datetime) or observed_at.tzinfo is None:
        raise ValueError("observed_at must be a timezone-aware datetime")
