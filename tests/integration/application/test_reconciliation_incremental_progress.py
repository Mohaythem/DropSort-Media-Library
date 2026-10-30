from __future__ import annotations

from datetime import datetime, timezone

from dropsort.application.dto.library import MediaFileAvailability
from dropsort.application.use_cases import ReconcileLibraryFiles
from dropsort.database.repositories import MediaFileRepository, SqliteMovieRepository
from dropsort.library.availability import NoFollowMediaFileInspector
from dropsort.library.movies import (
    MediaFileStatus,
    MovieCatalogData,
    VerifiedMediaFileFacts,
)


NOW = datetime(2026, 8, 23, 12, 0, tzinfo=timezone.utc)


def test_committed_availability_change_reports_file_movie_and_new_status(
    harness,
    tmp_path,
) -> None:
    movie = SqliteMovieRepository(harness.database).create(
        MovieCatalogData(
            "tmdb",
            "incremental-1",
            "Incremental Movie",
            None,
            2026,
            None,
            (),
            None,
            None,
            None,
        ),
        now=NOW,
    )
    repository = MediaFileRepository(harness.database)
    missing_path = (tmp_path / "missing.mkv").absolute()
    media_file = repository.add(
        VerifiedMediaFileFacts(
            missing_path,
            5,
            ".mkv",
            "1080p",
            "x264",
            "BluRay",
            NOW,
        ),
        movie.id,
    )
    values = []

    result = ReconcileLibraryFiles(
        repository,
        NoFollowMediaFileInspector(),
        now=lambda: NOW,
        batch_size=1,
        progress_interval=1,
    ).execute(progress=values.append)

    event_progress = [value for value in values if value.changes]
    assert result.status_changes == 1
    assert len(event_progress) == 1
    assert event_progress[0].changes[0].media_file_id == media_file.id
    assert event_progress[0].changes[0].movie_id == movie.id
    assert event_progress[0].changes[0].status is MediaFileAvailability.MISSING
    assert repository.get_by_id(media_file.id).status is MediaFileStatus.MISSING
