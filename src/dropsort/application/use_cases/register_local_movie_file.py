from __future__ import annotations

from collections.abc import Callable
from datetime import datetime, timezone
from pathlib import Path

from dropsort.application.dto.catalog import (
    MovieFileIngestionResult,
    RegisterLocalMovieFileCommand,
)
from dropsort.library.movies import (
    CatalogRecordNotFoundError,
    CatalogUnitOfWork,
    MetadataStatus,
    MovieCatalogData,
    VerifiedMediaFileFacts,
)


class RegisterLocalMovieFile:
    """Commit local Movie and MediaFile identities before optional enrichment."""

    def __init__(
        self,
        unit_of_work_factory: Callable[[], CatalogUnitOfWork],
        *,
        now: Callable[[], datetime] | None = None,
    ) -> None:
        self._unit_of_work_factory = unit_of_work_factory
        self._now = now or (lambda: datetime.now(timezone.utc))

    def execute(
        self,
        command: RegisterLocalMovieFileCommand,
    ) -> MovieFileIngestionResult:
        if not isinstance(command, RegisterLocalMovieFileCommand):
            raise ValueError("command must be RegisterLocalMovieFileCommand")
        catalog_now = self._now()
        if catalog_now.tzinfo is None:
            raise ValueError("catalog clock must return a timezone-aware datetime")
        movie_data = MovieCatalogData(
            provider=None,
            external_id=None,
            title=_fallback_title(command),
            original_title=None,
            year=command.parsed_media.year,
            overview=None,
            genres=(),
            runtime_minutes=None,
            rating=None,
            poster_reference=None,
            metadata_status=MetadataStatus.PENDING,
        )
        file_facts = VerifiedMediaFileFacts(
            current_path=command.file_path,
            file_size=command.file_size,
            extension=command.parsed_media.extension,
            resolution=command.parsed_media.resolution,
            codec=command.parsed_media.codec,
            source=command.parsed_media.source,
            observed_at=command.observed_at,
        )

        with self._unit_of_work_factory() as unit_of_work:
            existing = unit_of_work.media_files.get_by_path(file_facts.current_path)
            if existing is not None and existing.movie_id is not None:
                movie = unit_of_work.movies.get_by_id(existing.movie_id)
                if movie is None:
                    raise CatalogRecordNotFoundError(existing.movie_id)
                return MovieFileIngestionResult(movie=movie, media_file=existing)

            movie = unit_of_work.movies.create(movie_data, now=catalog_now)
            if existing is None:
                media_file = unit_of_work.media_files.add(file_facts, movie.id)
            else:
                media_file = unit_of_work.media_files.link_to_movie(existing.id, movie.id)
                media_file = unit_of_work.media_files.refresh_verified_facts(
                    media_file.id,
                    file_facts,
                )
            return MovieFileIngestionResult(movie=movie, media_file=media_file)


def _fallback_title(command: RegisterLocalMovieFileCommand) -> str:
    parsed_title = command.parsed_media.title
    if isinstance(parsed_title, str) and parsed_title.strip():
        return " ".join(parsed_title.split())
    filename_title = Path(command.parsed_media.original_name).stem.strip()
    return filename_title or command.file_path.stem or "Unknown Movie"
