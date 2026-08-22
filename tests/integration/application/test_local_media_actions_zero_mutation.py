from __future__ import annotations

from datetime import UTC, datetime
from hashlib import sha256
from pathlib import Path

from dropsort.database.repositories import MediaFileRepository, SqliteMovieRepository
from dropsort.library.movies import MovieCatalogData, VerifiedMediaFileFacts
from dropsort.library.playback import WindowsLocalMediaActions


def _catalog_snapshot(harness) -> tuple[tuple[object, ...], ...]:
    with harness.database.connection() as connection:
        return tuple(
            tuple(row)
            for table in ("movies", "media_files", "file_operations")
            for row in connection.execute(f"SELECT * FROM {table} ORDER BY rowid")
        )


def test_play_and_open_folder_leave_catalog_journal_and_media_unchanged(
    harness,
    tmp_path: Path,
) -> None:
    media_path = tmp_path / "Safe Movie & (Final).mkv"
    media_path.write_bytes(b"immutable physical media")
    observed = datetime(2026, 8, 12, tzinfo=UTC)
    movie = SqliteMovieRepository(harness.database).create(
        MovieCatalogData(
            provider="tmdb",
            external_id="safe-4d",
            title="Safe Movie",
            original_title=None,
            year=2026,
            overview=None,
            genres=(),
            runtime_minutes=None,
            rating=None,
            poster_reference=None,
        ),
        now=observed,
    )
    MediaFileRepository(harness.database).add(
        VerifiedMediaFileFacts(
            current_path=media_path,
            file_size=media_path.stat().st_size,
            extension=".mkv",
            resolution=None,
            codec=None,
            source=None,
            observed_at=observed,
        ),
        movie.id,
    )
    launched: list[object] = []
    actions = WindowsLocalMediaActions(
        start_file=lambda path: launched.append(path),
        explorer_launcher=lambda arguments: launched.append(arguments),
    )
    before_catalog = _catalog_snapshot(harness)
    before_hash = sha256(media_path.read_bytes()).hexdigest()

    actions.play(media_path)
    actions.open_folder(media_path)

    assert len(launched) == 2
    assert _catalog_snapshot(harness) == before_catalog
    assert media_path.is_file()
    assert sha256(media_path.read_bytes()).hexdigest() == before_hash
