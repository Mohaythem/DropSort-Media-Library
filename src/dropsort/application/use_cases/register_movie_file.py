from __future__ import annotations

from collections.abc import Callable
from datetime import datetime, timezone

from dropsort.application.dto.catalog import (
    MovieFileIngestionResult,
    RegisterMovieFileCommand,
)
from dropsort.library.movies import (
    CatalogUnitOfWork,
    MediaFileAssociationConflict,
    MovieCatalogData,
    VerifiedMediaFileFacts,
)


class RegisterMovieFile:
    """Persist an explicit catalog association without authorizing filesystem work."""

    def __init__(
        self,
        unit_of_work_factory: Callable[[], CatalogUnitOfWork],
        *,
        now: Callable[[], datetime] | None = None,
    ) -> None:
        self._unit_of_work_factory = unit_of_work_factory
        self._now = now or (lambda: datetime.now(timezone.utc))

    def execute(self, command: RegisterMovieFileCommand) -> MovieFileIngestionResult:
        catalog_now = self._now()
        if catalog_now.tzinfo is None:
            raise ValueError("catalog clock must return a timezone-aware datetime")
        movie_data = _movie_data(command)
        file_facts = _file_facts(command)

        with self._unit_of_work_factory() as unit_of_work:
            movie = unit_of_work.movies.get_by_external_id(
                movie_data.provider,
                movie_data.external_id,
            )
            if movie is None:
                movie = unit_of_work.movies.create(movie_data, now=catalog_now)
            else:
                movie = unit_of_work.movies.update_metadata(
                    movie.id,
                    movie_data,
                    now=catalog_now,
                )

            media_file = unit_of_work.media_files.get_by_path(file_facts.current_path)
            if media_file is None:
                media_file = unit_of_work.media_files.add(file_facts, movie.id)
            else:
                if media_file.movie_id not in {None, movie.id}:
                    raise MediaFileAssociationConflict(
                        f"media file {media_file.id} is linked to movie {media_file.movie_id}"
                    )
                if media_file.movie_id is None:
                    media_file = unit_of_work.media_files.link_to_movie(
                        media_file.id,
                        movie.id,
                    )
                media_file = unit_of_work.media_files.refresh_verified_facts(
                    media_file.id,
                    file_facts,
                )

            return MovieFileIngestionResult(movie=movie, media_file=media_file)


def _movie_data(command: RegisterMovieFileCommand) -> MovieCatalogData:
    metadata = command.metadata
    return MovieCatalogData(
        provider=metadata.provider,
        external_id=metadata.external_id,
        title=metadata.title,
        original_title=metadata.original_title,
        year=metadata.year,
        overview=metadata.overview,
        genres=metadata.genres,
        runtime_minutes=metadata.runtime_minutes,
        rating=metadata.rating,
        poster_reference=metadata.poster_reference,
    )


def _file_facts(command: RegisterMovieFileCommand) -> VerifiedMediaFileFacts:
    parsed = command.parsed_media
    return VerifiedMediaFileFacts(
        current_path=command.file_path,
        file_size=command.file_size,
        extension=parsed.extension,
        resolution=parsed.resolution,
        codec=parsed.codec,
        source=parsed.source,
        observed_at=command.observed_at,
    )

