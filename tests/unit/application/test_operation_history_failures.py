from __future__ import annotations

from dataclasses import replace
from pathlib import Path

import pytest

from dropsort.application.dto.operation_history import OperationHistoryQuery, UndoEligibilityCode
from dropsort.application.errors import (
    OperationHistoryError,
    UndoExecutionError,
    UndoPreviewNotFoundError,
    UndoPreviewStaleError,
)
from dropsort.application.use_cases.operation_history import (
    GetOperationDetails,
    ListOperationHistory,
    UndoFileOperation,
    RecoverFileOperation,
)
from dropsort.core.operations import FileOperationService
from dropsort.core.safety import PathPolicy
from dropsort.database import Database, MigrationRunner
from dropsort.database.repositories import (
    FileOperationRepository,
    MediaFileRepository,
    SqliteOperationJournalReadRepository,
    SqliteOperationStore,
)
from dropsort.library.operations import OperationJournalQueryError


class FailingJournal:
    def list_operations(self, *, limit: int, offset: int):
        raise OperationJournalQueryError("db")

    def get_operation(self, operation_id: str):
        raise OperationJournalQueryError("db")

    def latest_relevant_for_media_file(self, media_file_id: int):
        raise OperationJournalQueryError("db")


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
    source = first / "Movie.mkv"
    source.write_bytes(b"movie")
    media_id = media.create(source, 5)
    plan = service.plan_move(source, second / source.name, media_file_id=media_id)
    service.execute(plan.operation_id)
    journal = SqliteOperationJournalReadRepository(database)
    return database, media, operations, store, journal, plan.operation_id, first, second


def test_query_failures_and_invalid_identity_are_translated() -> None:
    with pytest.raises(OperationHistoryError):
        ListOperationHistory(FailingJournal()).execute(OperationHistoryQuery())
    with pytest.raises(OperationHistoryError):
        GetOperationDetails(FailingJournal()).execute("operation")
    with pytest.raises(ValueError):
        GetOperationDetails(FailingJournal()).execute("")


def test_invalid_journal_timestamp_is_controlled(tmp_path: Path) -> None:
    _database, _media, _operations, _store, journal, operation_id, *_roots = _setup(tmp_path)
    snapshot = journal.get_operation(operation_id)
    assert snapshot is not None

    class BadTimestampJournal:
        def get_operation(self, _operation_id: str):
            return replace(snapshot, record=replace(snapshot.record, created_at="not-a-time"))

    with pytest.raises(OperationHistoryError, match="timestamp"):
        GetOperationDetails(BadTimestampJournal()).execute(operation_id)


def test_preview_registry_eviction_discard_and_unknown_confirmation(tmp_path: Path) -> None:
    _database, media, _operations, store, journal, operation_id, *_roots = _setup(tmp_path)
    undo = UndoFileOperation(journal, media, store)
    undo._MAX_PREVIEWS = 1
    first = undo.prepare_preview(operation_id)
    second = undo.prepare_preview(operation_id)
    with pytest.raises(UndoPreviewNotFoundError):
        undo.confirm(first.preview_id)
    undo.discard_preview(second.preview_id)
    with pytest.raises(UndoPreviewNotFoundError):
        undo.confirm(second.preview_id)


def test_latest_operation_query_failure_is_controlled(tmp_path: Path) -> None:
    _database, media, _operations, store, journal, operation_id, *_roots = _setup(tmp_path)
    snapshot = journal.get_operation(operation_id)
    assert snapshot is not None

    class LatestFailure:
        def get_operation(self, _operation_id: str):
            return snapshot

        def latest_relevant_for_media_file(self, media_file_id: int):
            raise OperationJournalQueryError("db")

    undo = UndoFileOperation(LatestFailure(), media, store)
    with pytest.raises(OperationHistoryError, match="ordering"):
        undo.prepare_preview(operation_id)


def test_historical_root_removal_is_unsafe_without_journal(tmp_path: Path) -> None:
    database, media, _operations, store, journal, operation_id, first, _second = _setup(tmp_path)
    first.rmdir()
    undo = UndoFileOperation(journal, media, store)
    with pytest.raises(Exception) as caught:
        undo.prepare_preview(operation_id)
    assert getattr(caught.value, "code", None) is UndoEligibilityCode.UNSAFE_PATH
    with database.connection() as connection:
        assert connection.execute("SELECT COUNT(*) FROM file_operations").fetchone()[0] == 1


def test_changed_second_evaluation_is_stale_and_never_journaled(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    database, media, _operations, store, journal, operation_id, *_roots = _setup(tmp_path)
    undo = UndoFileOperation(journal, media, store)
    preview = undo.prepare_preview(operation_id)
    original = undo._evaluate

    def changed(value: str):
        prepared = original(value)
        return replace(prepared, source_identity=replace(prepared.source_identity, mtime_ns=0))

    monkeypatch.setattr(undo, "_evaluate", changed)
    with pytest.raises(UndoPreviewStaleError):
        undo.confirm(preview.preview_id)
    with database.connection() as connection:
        assert connection.execute("SELECT COUNT(*) FROM file_operations").fetchone()[0] == 1


def test_reverse_journal_creation_failure_is_controlled(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    database, media, operations, store, journal, operation_id, *_roots = _setup(tmp_path)
    undo = UndoFileOperation(journal, media, store)
    preview = undo.prepare_preview(operation_id)
    monkeypatch.setattr(
        operations,
        "create",
        lambda *_args, **_kwargs: (_ for _ in ()).throw(RuntimeError("db down")),
    )
    with pytest.raises(UndoExecutionError):
        undo.confirm(preview.preview_id)
    with database.connection() as connection:
        assert connection.execute("SELECT COUNT(*) FROM file_operations").fetchone()[0] == 1


def test_recovery_with_removed_historical_root_is_read_only_and_not_actionable(tmp_path: Path) -> None:
    _database, _media, _operations, store, journal, operation_id, first, _second = _setup(tmp_path)
    first.rmdir()
    recovery = RecoverFileOperation(journal, store)

    assessment = recovery.inspect(operation_id)

    assert assessment.action_available is False
    assert assessment.disposition.value == "UNSAFE_DESTINATION"
