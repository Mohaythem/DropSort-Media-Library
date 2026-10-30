from __future__ import annotations

from pathlib import Path

import pytest

from dropsort.application.bootstrap.desktop import create_import_actions
from dropsort.application.dto.movie_import import (
    ConfirmMovieImportCommand,
    ImportProposalStatus,
)
from dropsort.database import Database, MigrationRunner
from dropsort.library.movies import MetadataStatus
from dropsort.metadata.contracts import (
    MetadataAuthenticationError,
    MetadataUnavailableError,
    MovieCandidate,
    MovieSearchQuery,
)


class FailingSearchProvider:
    provider_name = "tmdb"

    def __init__(self, error: Exception) -> None:
        self.error = error
        self.detail_calls = 0

    def search_movies(self, query: MovieSearchQuery):
        raise self.error

    def get_movie(self, external_id: str):
        self.detail_calls += 1
        raise AssertionError("local-only Add must not request details")


class ZeroResultProvider:
    provider_name = "tmdb"

    def __init__(self) -> None:
        self.detail_calls = 0

    def search_movies(self, query: MovieSearchQuery):
        return ()

    def get_movie(self, external_id: str):
        self.detail_calls += 1
        raise AssertionError("zero-result Add must not request details")


class AmbiguousProvider(ZeroResultProvider):
    def search_movies(self, query: MovieSearchQuery):
        return (
            MovieCandidate("tmdb", "1", query.title, None, query.year, None, None, None),
            MovieCandidate("tmdb", "2", query.title, None, query.year, None, None, None),
        )


def _prepare(tmp_path: Path, provider):
    root = tmp_path / "movies"
    root.mkdir()
    media_path = root / "Local.Movie.2024.mkv"
    original = b"offline local media"
    media_path.write_bytes(original)
    database = Database(tmp_path / "dropsort.db")
    MigrationRunner(database).migrate()
    actions = create_import_actions(database, provider=provider)
    session = actions.prepare_import_review(root, True)
    assert len(session.items) == 1
    return database, actions, session.items[0], media_path, original


@pytest.mark.parametrize(
    "error",
    [
        MetadataUnavailableError("offline"),
        MetadataAuthenticationError("TMDB is not configured"),
    ],
)
def test_provider_failure_or_missing_credential_does_not_block_explicit_local_add(
    tmp_path: Path,
    error: Exception,
) -> None:
    provider = FailingSearchProvider(error)
    database, actions, proposal, media_path, original = _prepare(tmp_path, provider)

    assert proposal.status is ImportProposalStatus.METADATA_UNAVAILABLE
    result = actions.confirm_movie_import(ConfirmMovieImportCommand(proposal))

    assert result.movie.metadata_status is MetadataStatus.PENDING
    assert result.movie.provider is None
    assert result.media_file.movie_id == result.movie.id
    assert provider.detail_calls == 0
    assert media_path.read_bytes() == original
    with database.connection() as connection:
        assert connection.execute("SELECT COUNT(*) FROM movies").fetchone()[0] == 1
        assert connection.execute("SELECT COUNT(*) FROM media_files").fetchone()[0] == 1
        assert connection.execute("SELECT COUNT(*) FROM file_operations").fetchone()[0] == 0


def test_zero_results_still_adds_locally_and_marks_needs_match(tmp_path: Path) -> None:
    provider = ZeroResultProvider()
    _, actions, proposal, media_path, original = _prepare(tmp_path, provider)

    assert proposal.status is ImportProposalStatus.NO_MATCH
    result = actions.confirm_movie_import(ConfirmMovieImportCommand(proposal))

    assert result.movie.metadata_status is MetadataStatus.NEEDS_MATCH
    assert result.movie.provider is None
    assert result.enrichment is not None
    assert provider.detail_calls == 0
    assert media_path.read_bytes() == original


def test_ambiguous_result_can_register_without_selecting_identity(
    tmp_path: Path,
) -> None:
    provider = AmbiguousProvider()
    _, actions, proposal, _, _ = _prepare(tmp_path, provider)

    assert proposal.status is ImportProposalStatus.REVIEW_REQUIRED
    result = actions.confirm_movie_import(ConfirmMovieImportCommand(proposal))

    assert result.movie.metadata_status is MetadataStatus.NEEDS_MATCH
    assert result.movie.provider is None
    assert provider.detail_calls == 0
