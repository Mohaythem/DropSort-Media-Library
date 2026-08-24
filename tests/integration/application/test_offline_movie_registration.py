from __future__ import annotations

from datetime import datetime, timezone
from pathlib import Path

import pytest

from dropsort.application.dto import (
    MetadataEnrichmentOutcome,
    RegisterLocalMovieFileCommand,
)
from dropsort.application.use_cases import EnrichMovieMetadata, RegisterLocalMovieFile
from dropsort.database import Database
from dropsort.database.repositories import (
    MediaFileRepository,
    SqliteCatalogUnitOfWork,
    SqliteMovieRepository,
)
from dropsort.library.movies import (
    CatalogIntegrityError,
    MetadataStatus,
)
from dropsort.media.parser import MediaType, ParsedMedia
from dropsort.metadata.contracts import (
    MetadataResponseError,
    MetadataUnavailableError,
    MovieCandidate,
    MovieMetadata,
)


NOW = datetime(2026, 8, 24, 12, 0, tzinfo=timezone.utc)


def _parsed(
    title: str = "The Matrix",
    year: int | None = 1999,
    *,
    resolution: str = "1080p",
) -> ParsedMedia:
    return ParsedMedia(
        original_name=f"{title}.{year or ''}.mkv",
        media_type=MediaType.MOVIE,
        title=title,
        year=year,
        resolution=resolution,
        source="BluRay",
        codec="x264",
        extension=".mkv",
    )


def _command(
    path: Path,
    *,
    parsed: ParsedMedia | None = None,
    size: int = 123,
) -> RegisterLocalMovieFileCommand:
    return RegisterLocalMovieFileCommand(
        parsed_media=parsed or _parsed(),
        file_path=path.absolute(),
        file_size=size,
        observed_at=NOW,
    )


def _registrar(harness) -> RegisterLocalMovieFile:
    return RegisterLocalMovieFile(
        lambda: SqliteCatalogUnitOfWork(harness.database),
        now=lambda: NOW,
    )


def _register(harness, path: Path, *, parsed: ParsedMedia | None = None):
    return _registrar(harness).execute(_command(path, parsed=parsed))


def _metadata(external_id: str = "603", title: str = "The Matrix") -> MovieMetadata:
    return MovieMetadata(
        provider="tmdb",
        external_id=external_id,
        title=title,
        original_title=title,
        year=1999,
        overview="overview",
        genres=("Action",),
        runtime_minutes=136,
        rating=8.2,
        director=None,
        cast=(),
        poster_reference="/poster.jpg",
    )


class Provider:
    provider_name = "tmdb"

    def __init__(
        self,
        *,
        external_id: str = "603",
        error: Exception | None = None,
        transaction_active=lambda: False,
    ) -> None:
        self.external_id = external_id
        self.error = error
        self.transaction_active = transaction_active
        self.detail_calls = 0

    def search_movies(self, query):
        raise AssertionError("enrichment by selected candidate does not search")

    def get_movie(self, external_id: str) -> MovieMetadata:
        assert self.transaction_active() is False
        self.detail_calls += 1
        if self.error is not None:
            raise self.error
        return _metadata(self.external_id)


def _candidate(external_id: str = "603") -> MovieCandidate:
    return MovieCandidate(
        "tmdb",
        external_id,
        "The Matrix",
        "The Matrix",
        1999,
        None,
        8.2,
        "/poster.jpg",
    )


def _enricher(harness, provider: Provider, *, poster_actions=None) -> EnrichMovieMetadata:
    return EnrichMovieMetadata(
        provider,
        lambda: SqliteCatalogUnitOfWork(harness.database),
        poster_actions=poster_actions,
        now=lambda: NOW,
    )


def _counts(harness) -> tuple[int, int, int]:
    with harness.database.connection() as connection:
        return (
            connection.execute("SELECT COUNT(*) FROM movies").fetchone()[0],
            connection.execute("SELECT COUNT(*) FROM media_files").fetchone()[0],
            connection.execute("SELECT COUNT(*) FROM file_operations").fetchone()[0],
        )


def test_local_registration_is_pending_uses_parsed_fallback_and_needs_no_provider(
    harness,
    tmp_path: Path,
) -> None:
    result = _register(harness, tmp_path / "The.Matrix.1999.mkv")

    assert result.movie.provider is None
    assert result.movie.external_id is None
    assert result.movie.metadata_status is MetadataStatus.PENDING
    assert (result.movie.title, result.movie.year) == ("The Matrix", 1999)
    assert result.media_file.movie_id == result.movie.id
    assert _counts(harness) == (1, 1, 0)

def test_registration_survives_restart_and_enriches_the_same_stable_ids(
    harness,
    tmp_path: Path,
) -> None:
    path = tmp_path / "Restart.Proof.2024.mkv"
    path.write_bytes(b"local-media-evidence")
    local = _register(harness, path, parsed=_parsed("Restart Proof", 2024))

    restarted = Database(harness.database.path)
    persisted_movie = SqliteMovieRepository(restarted).get_by_id(local.movie.id)
    persisted_files = MediaFileRepository(restarted).list_for_movie(local.movie.id)

    assert persisted_movie is not None
    assert persisted_movie.id == local.movie.id
    assert persisted_movie.metadata_status is MetadataStatus.PENDING
    assert [media_file.id for media_file in persisted_files] == [local.media_file.id]

    enriched = EnrichMovieMetadata(
        Provider(),
        lambda: SqliteCatalogUnitOfWork(restarted),
        now=lambda: NOW,
    ).execute(local.movie.id, _candidate())

    assert enriched.outcome is MetadataEnrichmentOutcome.READY
    assert enriched.movie.id == local.movie.id
    assert MediaFileRepository(restarted).list_for_movie(local.movie.id)[0].id == (
        local.media_file.id
    )
    assert path.read_bytes() == b"local-media-evidence"



def test_same_path_and_windows_case_alias_are_idempotent(harness, tmp_path: Path) -> None:
    registrar = _registrar(harness)
    first = registrar.execute(_command(tmp_path / "Movie.MKV"))
    second = registrar.execute(_command(tmp_path / "movie.mkv"))

    assert second.movie.id == first.movie.id
    assert second.media_file.id == first.media_file.id
    assert _counts(harness) == (1, 1, 0)


def test_existing_unlinked_media_file_keeps_its_id_when_registered(
    harness,
    tmp_path: Path,
) -> None:
    path = (tmp_path / "Existing.mkv").absolute()
    media_file_id = harness.media_files.create(path, 123)

    result = _registrar(harness).execute(_command(path))

    assert result.media_file.id == media_file_id
    assert result.media_file.movie_id == result.movie.id
    assert _counts(harness) == (1, 1, 0)


def test_media_file_failure_rolls_back_new_provisional_movie(
    harness,
    tmp_path: Path,
    monkeypatch,
) -> None:
    def fail_add(self, facts, movie_id):
        raise CatalogIntegrityError("injected media-file failure")

    monkeypatch.setattr(MediaFileRepository, "add", fail_add)

    with pytest.raises(CatalogIntegrityError):
        _registrar(harness).execute(_command(tmp_path / "Rollback.mkv"))

    assert _counts(harness) == (0, 0, 0)


def test_registration_is_catalog_only_and_preserves_physical_path_and_bytes(
    harness,
    tmp_path: Path,
) -> None:
    path = tmp_path / "Unchanged.mkv"
    content = b"physical movie bytes stay unchanged"
    path.write_bytes(content)

    result = _registrar(harness).execute(_command(path, size=len(content)))

    assert result.media_file.current_path == path
    assert path.read_bytes() == content
    assert _counts(harness) == (1, 1, 0)


def test_same_title_and_year_do_not_deduplicate_distinct_local_files(
    harness,
    tmp_path: Path,
) -> None:
    first = _register(harness, tmp_path / "A.mkv")
    second = _register(harness, tmp_path / "B.mkv")

    assert first.movie.id != second.movie.id
    assert first.media_file.id != second.media_file.id
    assert _counts(harness) == (2, 2, 0)


def test_enrichment_fetches_before_transaction_and_preserves_stable_ids(
    harness,
    tmp_path: Path,
) -> None:
    local = _register(harness, tmp_path / "Movie.mkv")
    transaction_active = False

    class TrackingUnitOfWork:
        def __init__(self):
            self.delegate = SqliteCatalogUnitOfWork(harness.database)

        def __enter__(self):
            nonlocal transaction_active
            transaction_active = True
            entered = self.delegate.__enter__()
            self.movies = entered.movies
            self.media_files = entered.media_files
            return self

        def __exit__(self, *args):
            nonlocal transaction_active
            try:
                return self.delegate.__exit__(*args)
            finally:
                transaction_active = False

    provider = Provider(transaction_active=lambda: transaction_active)
    enricher = EnrichMovieMetadata(
        provider,
        TrackingUnitOfWork,
        now=lambda: NOW,
    )

    result = enricher.execute(local.movie.id, _candidate())

    assert result.outcome is MetadataEnrichmentOutcome.READY
    assert result.movie.id == local.movie.id
    assert result.movie.metadata_status is MetadataStatus.READY
    assert result.movie.external_id == "603"
    files = MediaFileRepository(harness.database).list_for_movie(local.movie.id)
    assert tuple(item.id for item in files) == (local.media_file.id,)
    assert provider.detail_calls == 1


def test_retryable_offline_failure_keeps_registered_local_state_pending(
    harness,
    tmp_path: Path,
) -> None:
    local = _register(harness, tmp_path / "Offline.mkv")
    provider = Provider(error=MetadataUnavailableError("offline"))

    result = _enricher(harness, provider).execute(local.movie.id, _candidate())

    assert result.outcome is MetadataEnrichmentOutcome.PENDING
    assert result.movie.metadata_status is MetadataStatus.PENDING
    assert result.movie.id == local.movie.id
    assert result.movie.provider is None
    assert MediaFileRepository(harness.database).get_by_id(local.media_file.id) is not None


def test_invalid_provider_data_marks_failed_without_deleting_local_state(
    harness,
    tmp_path: Path,
) -> None:
    local = _register(harness, tmp_path / "Invalid.mkv")
    provider = Provider(error=MetadataResponseError("invalid"))

    result = _enricher(harness, provider).execute(local.movie.id, _candidate())

    assert result.outcome is MetadataEnrichmentOutcome.FAILED
    assert result.movie.metadata_status is MetadataStatus.FAILED
    assert MediaFileRepository(harness.database).get_by_id(local.media_file.id) is not None


def test_no_confident_match_marks_needs_match_without_deleting_local_state(
    harness,
    tmp_path: Path,
) -> None:
    local = _register(harness, tmp_path / "NoMatch.mkv")

    result = _enricher(harness, Provider()).mark_needs_match(local.movie.id)

    assert result.outcome is MetadataEnrichmentOutcome.NEEDS_MATCH
    assert result.movie.metadata_status is MetadataStatus.NEEDS_MATCH
    assert MediaFileRepository(harness.database).get_by_id(local.media_file.id) is not None


def test_identity_collision_preserves_movies_files_personal_history_and_operation_attribution(
    harness,
    tmp_path: Path,
) -> None:
    movie_a = _register(harness, tmp_path / "A.mkv")
    movie_b = _register(harness, tmp_path / "B.mkv")
    enricher = _enricher(harness, Provider())
    ready_b = enricher.execute(movie_b.movie.id, _candidate())
    assert ready_b.outcome is MetadataEnrichmentOutcome.READY
    with harness.database.transaction() as connection:
        for movie_id, preference in (
            (movie_a.movie.id, "LIKED"),
            (movie_b.movie.id, "BLACKLISTED"),
        ):
            connection.execute(
                """
                INSERT INTO movie_personal_state(
                    movie_id, preference, created_at, updated_at
                ) VALUES (?, ?, 'c', 'u')
                """,
                (movie_id, preference),
            )
        connection.execute(
            "INSERT INTO watch_events(id, movie_id, watched_at, created_at) VALUES (101, ?, 'w', 'c')",
            (movie_a.movie.id,),
        )
        connection.execute(
            "INSERT INTO watch_events(id, movie_id, watched_at, created_at) VALUES (102, ?, 'w', 'c')",
            (movie_b.movie.id,),
        )
        connection.execute(
            """
            INSERT INTO file_operations(
                id, operation_type, source_path, destination_path, state,
                media_file_id, created_at, updated_at
            ) VALUES ('attribution', 'MOVE', 'a', 'b', 'COMMITTED', ?, 'c', 'u')
            """,
            (movie_a.media_file.id,),
        )

    first = enricher.execute(movie_a.movie.id, _candidate())
    second = enricher.execute(movie_a.movie.id, _candidate())

    assert first.outcome is MetadataEnrichmentOutcome.IDENTITY_COLLISION
    assert first.movie.id == movie_a.movie.id
    assert first.collision_movie_id == movie_b.movie.id
    assert second.outcome is MetadataEnrichmentOutcome.IDENTITY_COLLISION
    assert second.collision_movie_id == movie_b.movie.id
    assert first.movie.metadata_status is MetadataStatus.NEEDS_MATCH
    with harness.database.connection() as connection:
        movies = connection.execute(
            "SELECT id, metadata_status FROM movies ORDER BY id"
        ).fetchall()
        files = connection.execute(
            "SELECT id, movie_id FROM media_files ORDER BY id"
        ).fetchall()
        personal = connection.execute(
            "SELECT movie_id, preference FROM movie_personal_state ORDER BY movie_id"
        ).fetchall()
        watches = connection.execute(
            "SELECT id, movie_id FROM watch_events ORDER BY id"
        ).fetchall()
        operation = connection.execute(
            "SELECT media_file_id FROM file_operations WHERE id = 'attribution'"
        ).fetchone()

    assert [(row["id"], row["metadata_status"]) for row in movies] == [
        (movie_a.movie.id, "NEEDS_MATCH"),
        (movie_b.movie.id, "READY"),
    ]
    assert [(row["id"], row["movie_id"]) for row in files] == [
        (movie_a.media_file.id, movie_a.movie.id),
        (movie_b.media_file.id, movie_b.movie.id),
    ]
    assert [(row["movie_id"], row["preference"]) for row in personal] == [
        (movie_a.movie.id, "LIKED"),
        (movie_b.movie.id, "BLACKLISTED"),
    ]
    assert [(row["id"], row["movie_id"]) for row in watches] == [
        (101, movie_a.movie.id),
        (102, movie_b.movie.id),
    ]
    assert operation is not None
    assert operation["media_file_id"] == movie_a.media_file.id
    assert _counts(harness)[2] == 1


def test_enrichment_retry_is_idempotent_and_poster_failure_cannot_undo_catalog(
    harness,
    tmp_path: Path,
) -> None:
    local = _register(harness, tmp_path / "Retry.mkv")

    class FailingPoster:
        def load_poster(self, request):
            raise RuntimeError("poster offline")

    enricher = _enricher(harness, Provider(), poster_actions=FailingPoster())
    first = enricher.execute(local.movie.id, _candidate())
    second = enricher.execute(local.movie.id, _candidate())

    assert first.outcome is MetadataEnrichmentOutcome.READY
    assert second.outcome is MetadataEnrichmentOutcome.READY
    assert second.movie.id == local.movie.id
    assert MediaFileRepository(harness.database).list_for_movie(local.movie.id)[0].id == (
        local.media_file.id
    )
    assert _counts(harness) == (1, 1, 0)
