from __future__ import annotations

from dropsort.application.dto.library import (
    MediaFileAvailability,
    MediaFileDetails,
    MovieDetails,
    MovieMetadataStatus,
    MovieListItem,
)
from dropsort.library.movies import MovieDetailsSnapshot, MovieSummary


def to_list_item(summary: MovieSummary) -> MovieListItem:
    movie = summary.movie
    return MovieListItem(
        movie_id=movie.id,
        provider=movie.provider,
        title=movie.title,
        original_title=movie.original_title,
        year=movie.year,
        rating=movie.rating,
        poster_reference=movie.poster_reference,
        media_file_count=summary.media_file_count,
        date_added=movie.date_added,
        missing_file_count=summary.missing_file_count,
        metadata_status=MovieMetadataStatus(movie.metadata_status.value),
    )


def to_movie_details(snapshot: MovieDetailsSnapshot) -> MovieDetails:
    movie = snapshot.movie
    files = tuple(
        MediaFileDetails(
            media_file_id=media_file.id,
            current_path=str(media_file.current_path),
            file_size=media_file.file_size,
            extension=media_file.extension,
            resolution=media_file.resolution,
            codec=media_file.codec,
            source=media_file.source,
            status=MediaFileAvailability(media_file.status.value),
        )
        for media_file in snapshot.media_files
    )
    return MovieDetails(
        movie_id=movie.id,
        provider=movie.provider,
        external_id=movie.external_id,
        title=movie.title,
        original_title=movie.original_title,
        year=movie.year,
        overview=movie.overview,
        genres=movie.genres,
        runtime_minutes=movie.runtime_minutes,
        rating=movie.rating,
        metadata_status=MovieMetadataStatus(movie.metadata_status.value),
        poster_reference=movie.poster_reference,
        date_added=movie.date_added,
        media_files=files,
    )
