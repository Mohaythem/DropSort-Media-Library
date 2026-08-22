from __future__ import annotations

from pathlib import Path

import pytest

from dropsort.application.bootstrap.desktop import create_import_actions
from dropsort.application.dto.movie_import import ConfirmMovieImportCommand, ImportProposalStatus
from dropsort.database import Database, MigrationRunner
from dropsort.database.repositories import MediaFileRepository, SqliteMovieRepository
from dropsort.application.errors import ImportReviewCancelled
from dropsort.application.use_cases import ImportReviewCancellation
from dropsort.metadata.contracts import MovieCandidate, MovieMetadata, MovieSearchQuery


class FakeMetadataProvider:
    provider_name = "tmdb"

    def search_movies(self, query: MovieSearchQuery) -> tuple[MovieCandidate, ...]:
        return (
            MovieCandidate(
                provider="tmdb",
                external_id="155",
                title=query.title,
                original_title=None,
                year=query.year,
                overview=None,
                rating=8.5,
                poster_reference=None,
            ),
        )

    def get_movie(self, external_id: str) -> MovieMetadata:
        return MovieMetadata(
            provider="tmdb",
            external_id=external_id,
            title="The Dark Knight",
            original_title=None,
            year=2008,
            overview=None,
            genres=("Action",),
            runtime_minutes=152,
            rating=8.5,
            director=None,
            cast=(),
            poster_reference=None,
        )


def test_composed_desktop_import_is_explicit_catalog_only_and_keeps_file_unchanged(
    tmp_path: Path,
) -> None:
    root = tmp_path / "movies"
    root.mkdir()
    media_path = root / "The.Dark.Knight.2008.1080p.BluRay.x264.mkv"
    original_bytes = b"unchanged movie bytes"
    media_path.write_bytes(original_bytes)
    database = Database(tmp_path / "dropsort.db")
    MigrationRunner(database).migrate()
    actions = create_import_actions(database, provider=FakeMetadataProvider())

    session = actions.prepare_import_review(root, True)

    assert len(session.items) == 1
    proposal = session.items[0]
    assert proposal.status is ImportProposalStatus.MATCH_PROPOSED
    assert SqliteMovieRepository(database).list_all() == ()
    assert MediaFileRepository(database).get_by_path(media_path) is None
    assert media_path.read_bytes() == original_bytes
    with database.connection() as connection:
        assert connection.execute("SELECT COUNT(*) FROM file_operations").fetchone()[0] == 0

    result = actions.confirm_movie_import(
        ConfirmMovieImportCommand(proposal, proposal.proposed_candidate)
    )

    assert result.media_file.current_path == media_path
    assert len(SqliteMovieRepository(database).list_all()) == 1
    assert MediaFileRepository(database).get_by_path(media_path) is not None
    assert media_path.read_bytes() == original_bytes


def test_cancelled_composed_review_creates_no_catalog_or_journal_rows(
    tmp_path: Path,
) -> None:
    root = tmp_path / "movies"
    root.mkdir()
    for index in range(20):
        (root / f"Movie.{2000 + index}.mkv").write_bytes(b"read only")
    database = Database(tmp_path / "dropsort.db")
    MigrationRunner(database).migrate()
    actions = create_import_actions(database, provider=FakeMetadataProvider())
    cancellation = ImportReviewCancellation()

    def progress(value) -> None:
        if value.entries_seen >= 4:
            cancellation.cancel()

    with pytest.raises(ImportReviewCancelled):
        actions.prepare_import_review(
            root,
            True,
            progress=progress,
            cancellation=cancellation,
        )

    assert SqliteMovieRepository(database).list_all() == ()
    with database.connection() as connection:
        assert connection.execute("SELECT COUNT(*) FROM media_files").fetchone()[0] == 0
        assert connection.execute("SELECT COUNT(*) FROM file_operations").fetchone()[0] == 0
    assert len(tuple(root.glob("*.mkv"))) == 20
