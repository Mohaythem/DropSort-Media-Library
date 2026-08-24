from __future__ import annotations

from datetime import datetime
from pathlib import Path
from types import TracebackType
from typing import Protocol

from dropsort.library.movies.models import (
    MediaFile,
    MediaFileStatusUpdate,
    MetadataStatus,
    Movie,
    MovieCatalogData,
    VerifiedMediaFileFacts,
)
from dropsort.library.movies.queries import MovieDetailsSnapshot, MovieSummary


class MovieRepository(Protocol):
    def get_by_id(self, movie_id: int) -> Movie | None: ...

    def get_by_external_id(self, provider: str, external_id: str) -> Movie | None: ...

    def create(self, data: MovieCatalogData, *, now: datetime) -> Movie: ...

    def update_metadata(
        self,
        movie_id: int,
        data: MovieCatalogData,
        *,
        now: datetime,
    ) -> Movie: ...

    def attach_external_metadata(
        self,
        movie_id: int,
        data: MovieCatalogData,
        *,
        now: datetime,
    ) -> Movie: ...

    def update_metadata_status(
        self,
        movie_id: int,
        status: MetadataStatus,
        *,
        now: datetime,
    ) -> Movie: ...

    def list_all(self) -> tuple[Movie, ...]: ...

    def count_all(self) -> int: ...

    def list_page(self, *, after_id: int, limit: int) -> tuple[Movie, ...]: ...


class MediaFileRepository(Protocol):
    def get_by_id(self, media_file_id: int) -> MediaFile | None: ...

    def get_by_path(self, path: Path) -> MediaFile | None: ...

    def add(self, facts: VerifiedMediaFileFacts, movie_id: int) -> MediaFile: ...

    def refresh_verified_facts(
        self,
        media_file_id: int,
        facts: VerifiedMediaFileFacts,
    ) -> MediaFile: ...

    def link_to_movie(self, media_file_id: int, movie_id: int) -> MediaFile: ...

    def mark_missing(self, media_file_id: int) -> MediaFile: ...

    def mark_present(self, media_file_id: int, *, observed_at: datetime) -> MediaFile: ...

    def list_for_movie(self, movie_id: int) -> tuple[MediaFile, ...]: ...

    def count_cataloged(self) -> int: ...

    def list_cataloged(self, *, after_id: int, limit: int) -> tuple[MediaFile, ...]: ...

    def apply_status_updates(self, updates: tuple[MediaFileStatusUpdate, ...]) -> int: ...

    def relink(
        self,
        media_file_id: int,
        *,
        expected_path: Path,
        new_path: Path,
        observed_at: datetime,
    ) -> MediaFile: ...


class MediaFileCatalogLookup(Protocol):
    def get_by_path(self, path: Path) -> MediaFile | None: ...


class MovieLibraryReadRepository(Protocol):
    def list_movies(self, *, limit: int, offset: int) -> tuple[MovieSummary, ...]: ...

    def get_movie_summary(self, movie_id: int) -> MovieSummary | None: ...

    def get_movie_details(self, movie_id: int) -> MovieDetailsSnapshot | None: ...


class CatalogUnitOfWork(Protocol):
    movies: MovieRepository
    media_files: MediaFileRepository

    def __enter__(self) -> CatalogUnitOfWork: ...

    def __exit__(
        self,
        exc_type: type[BaseException] | None,
        exc_value: BaseException | None,
        traceback: TracebackType | None,
    ) -> bool | None: ...
