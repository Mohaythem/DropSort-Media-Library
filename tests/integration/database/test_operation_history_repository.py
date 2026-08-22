from __future__ import annotations

from pathlib import Path

import pytest

from dropsort.core.operations import FileOperationService, OperationState
from dropsort.core.operations.errors import InvalidOperationStateError
from dropsort.core.operations.models import FileOperationPlan, OperationType
from dropsort.core.safety import PathPolicy
from dropsort.database import Database, MigrationRunner
from dropsort.database.repositories import (
    FileOperationRepository,
    MediaFileRepository,
    SqliteOperationJournalReadRepository,
    SqliteOperationStore,
)


def _setup(tmp_path: Path):
    first = tmp_path / "first"
    second = tmp_path / "second"
    first.mkdir()
    second.mkdir()
    database = Database(tmp_path / "history.db")
    MigrationRunner(database).migrate()
    media = MediaFileRepository(database)
    operations = FileOperationRepository(database)
    store = SqliteOperationStore(database, operations, media)
    service = FileOperationService(PathPolicy((first, second)), store)
    reads = SqliteOperationJournalReadRepository(database)
    return database, media, operations, service, reads, first, second


def test_history_is_newest_first_bounded_and_contains_movie_context(tmp_path: Path) -> None:
    database, media, _operations, service, reads, first, second = _setup(tmp_path)
    source = first / "Movie.mkv"
    source.write_bytes(b"movie")
    media_id = media.create(source, 5)
    with database.transaction() as connection:
        movie_id = int(
            connection.execute(
                """
                INSERT INTO movies(provider, external_id, title, genres, date_added, created_at, updated_at)
                VALUES ('tmdb', '1', 'The Movie', '[]', '2026-01-01T00:00:00+00:00',
                        '2026-01-01T00:00:00+00:00', '2026-01-01T00:00:00+00:00')
                """
            ).lastrowid
        )
        connection.execute("UPDATE media_files SET movie_id = ? WHERE id = ?", (movie_id, media_id))
    first_plan = service.plan_move(source, second / source.name, media_file_id=media_id)
    service.execute(first_plan.operation_id)
    reverse = service.create_reverse_plan(first_plan.operation_id)
    service.execute(reverse.operation_id)

    page = reads.list_operations(limit=1, offset=0)
    older = reads.list_operations(limit=1, offset=1)

    assert [entry.record.id for entry in page] == [reverse.operation_id]
    assert [entry.record.id for entry in older] == [first_plan.operation_id]
    assert page[0].movie_title == "The Movie"
    assert page[0].current_catalog_path == source
    assert page[0].record.reverses_operation_id == first_plan.operation_id
    assert older[0].reversed_by_operation_id == reverse.operation_id


def test_details_and_latest_relevant_operation_are_deterministic(tmp_path: Path) -> None:
    _database, media, operations, service, reads, first, second = _setup(tmp_path)
    source = first / "Movie.mkv"
    source.write_bytes(b"movie")
    media_id = media.create(source, 5)
    committed = service.plan_move(source, second / source.name, media_file_id=media_id)
    service.execute(committed.operation_id)

    failed_source = second / "Other.mkv"
    failed_source.write_bytes(b"other")
    failed = service.plan_move(failed_source, first / "Other.mkv", media_file_id=media_id)
    operations.transition(failed.operation_id, OperationState.FAILED)

    snapshot = reads.get_operation(committed.operation_id)
    latest = reads.latest_relevant_for_media_file(media_id)

    assert snapshot is not None
    assert snapshot.record.id == committed.operation_id
    assert latest is not None
    assert latest.record.id == committed.operation_id
    assert reads.get_operation("missing") is None


def test_repository_rejects_invalid_query_values(tmp_path: Path) -> None:
    *_unused, reads, _first, _second = _setup(tmp_path)
    for limit, offset in ((0, 0), (1, -1), (True, 0)):
        with pytest.raises(ValueError):
            reads.list_operations(limit=limit, offset=offset)  # type: ignore[arg-type]
    with pytest.raises(ValueError):
        reads.latest_relevant_for_media_file(0)
    with pytest.raises(ValueError):
        reads.get_operation("")


def test_connection_injected_repository_and_sql_failures_are_controlled(tmp_path: Path) -> None:
    database, _media, _operations, _service, _reads, _first, _second = _setup(tmp_path)
    from dropsort.library.operations import OperationJournalQueryError

    with database.connection() as connection:
        injected = SqliteOperationJournalReadRepository(database, connection=connection)
        assert injected.list_operations(limit=10, offset=0) == ()
        connection.execute("DROP TABLE file_operations")
        with pytest.raises(OperationJournalQueryError):
            injected.list_operations(limit=10, offset=0)
        with pytest.raises(OperationJournalQueryError):
            injected.get_operation("operation")
        with pytest.raises(OperationJournalQueryError):
            injected.latest_relevant_for_media_file(1)


def test_second_reverse_journal_is_rejected_atomically(tmp_path: Path) -> None:
    _database, media, operations, service, _reads, first, second = _setup(tmp_path)
    source = first / "Movie.mkv"
    source.write_bytes(b"movie")
    media_id = media.create(source, 5)
    original = service.plan_move(source, second / source.name, media_file_id=media_id)
    service.execute(original.operation_id)
    first_reverse = service.create_reverse_plan(original.operation_id)

    with pytest.raises(InvalidOperationStateError):
        operations.create(
            FileOperationPlan(
                operation_id="competing-reverse",
                operation_type=OperationType.MOVE,
                source=second / source.name,
                destination=source,
                media_file_id=media_id,
                reverses_operation_id=original.operation_id,
            )
        )

    assert operations.get(first_reverse.operation_id).reverses_operation_id == original.operation_id
