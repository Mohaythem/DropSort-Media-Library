from __future__ import annotations

import errno
import os
from pathlib import Path

import pytest

from dropsort.application.dto.operation_history import (
    OperationHistoryQuery,
    OperationStatus,
    RecoveryDisposition,
    UndoEligibilityCode,
)
from dropsort.application.errors import (
    OperationHistoryNotFoundError,
    RecoveryActionUnavailableError,
    UndoAlreadyConfirmedError,
    UndoExecutionError,
    UndoNotEligibleError,
    UndoPreviewStaleError,
    UndoRecoveryRequiredError,
)
from dropsort.application.use_cases.operation_history import (
    GetOperationDetails,
    ListOperationHistory,
    RecoverFileOperation,
    UndoFileOperation,
)
from dropsort.core.file_engine.transfer import SafeTransferEngine
from dropsort.core.operations import FileOperationService, OperationState
from dropsort.core.safety import PathPolicy
from dropsort.database import Database, MigrationRunner
from dropsort.database.repositories import (
    FileOperationRepository,
    MediaFileRepository,
    SqliteOperationJournalReadRepository,
    SqliteOperationStore,
)


def _setup(tmp_path: Path, *, engine: SafeTransferEngine | None = None):
    source_root = tmp_path / "source"
    destination_root = tmp_path / "destination"
    third_root = tmp_path / "third"
    source_root.mkdir()
    destination_root.mkdir()
    third_root.mkdir()
    database = Database(tmp_path / "history.db")
    MigrationRunner(database).migrate()
    media = MediaFileRepository(database)
    operations = FileOperationRepository(database)
    store = SqliteOperationStore(database, operations, media)
    reads = SqliteOperationJournalReadRepository(database)
    policy = PathPolicy((source_root, destination_root, third_root))
    service = FileOperationService(policy, store, engine=engine)
    undo = UndoFileOperation(reads, media, store, engine=engine)
    recovery = RecoverFileOperation(reads, store)
    return database, media, operations, service, reads, undo, recovery, source_root, destination_root, third_root


def _committed_move(tmp_path: Path, *, engine: SafeTransferEngine | None = None):
    values = _setup(tmp_path, engine=engine)
    _database, media, operations, service, reads, undo, recovery, source_root, destination_root, third_root = values
    source = source_root / "Movie.mkv"
    source.write_bytes(b"movie-content")
    media_id = media.create(source, len(b"movie-content"))
    plan = service.plan_move(source, destination_root / source.name, media_file_id=media_id)
    service.execute(plan.operation_id)
    return values, plan.operation_id, media_id, source, destination_root / source.name


def _journal_count(database: Database) -> int:
    with database.connection() as connection:
        return int(connection.execute("SELECT COUNT(*) FROM file_operations").fetchone()[0])


def test_history_and_details_are_read_only_and_control_not_found(tmp_path: Path) -> None:
    values, operation_id, _media_id, source, destination = _committed_move(tmp_path)
    database, _media, _operations, _service, reads, *_rest = values
    before = database.path.read_bytes()

    history = ListOperationHistory(reads).execute(OperationHistoryQuery(limit=10))
    details = GetOperationDetails(reads).execute(operation_id)

    assert history[0].operation_id == operation_id
    assert details.history.state is OperationStatus.COMMITTED
    assert details.history.source_path == str(source.resolve())
    assert details.history.destination_path == str(destination.resolve())
    assert database.path.read_bytes() == before
    with pytest.raises(OperationHistoryNotFoundError):
        GetOperationDetails(reads).execute("missing")


def test_undo_preview_is_read_only_exact_and_confirmation_creates_linked_reverse(
    tmp_path: Path,
) -> None:
    values, operation_id, media_id, original_source, current = _committed_move(tmp_path)
    database, media, operations, _service, _reads, undo, *_rest = values
    before_count = _journal_count(database)

    preview = undo.prepare_preview(operation_id)

    assert preview.source_path == str(current.resolve())
    assert preview.destination_path == str(original_source.resolve())
    assert preview.media_file_id == media_id
    assert _journal_count(database) == before_count
    assert current.exists() and not original_source.exists()

    result = undo.confirm(preview.preview_id)

    assert _journal_count(database) == before_count + 1
    reverse = operations.get(result.reverse_operation_id)
    assert reverse.reverses_operation_id == operation_id
    assert reverse.state is OperationState.COMMITTED
    assert original_source.read_bytes() == b"movie-content"
    assert not current.exists()
    assert media.get_path(media_id) == original_source
    assert operations.get(operation_id).state is OperationState.COMMITTED
    with pytest.raises(UndoAlreadyConfirmedError):
        undo.confirm(preview.preview_id)
    assert _journal_count(database) == before_count + 1


def test_undo_preserves_movie_association_and_technical_metadata(tmp_path: Path) -> None:
    values, operation_id, media_id, original_source, _current = _committed_move(tmp_path)
    database, media, *_rest = values
    with database.transaction() as connection:
        movie_id = int(
            connection.execute(
                """
                INSERT INTO movies(provider, external_id, title, genres, date_added, created_at, updated_at)
                VALUES ('tmdb', '99', 'Movie', '[\"Drama\"]', ?, ?, ?)
                """,
                ("2026-01-01T00:00:00+00:00",) * 3,
            ).lastrowid
        )
        connection.execute(
            """
            UPDATE media_files SET movie_id = ?, resolution = '2160p', codec = 'x265', source = 'BluRay'
            WHERE id = ?
            """,
            (movie_id, media_id),
        )
    undo = values[5]
    undo.confirm(undo.prepare_preview(operation_id).preview_id)

    record = media.get_by_id(media_id)
    assert record is not None
    assert record.current_path == original_source
    assert (record.movie_id, record.resolution, record.codec, record.source) == (
        movie_id,
        "2160p",
        "x265",
        "BluRay",
    )


def test_out_of_order_undo_is_blocked_without_new_journal(tmp_path: Path) -> None:
    values, first_id, media_id, original_source, middle = _committed_move(tmp_path)
    database, media, _operations, service, _reads, undo, _recovery, _a, _b, third = values
    second = service.plan_move(middle, third / middle.name, media_file_id=media_id)
    service.execute(second.operation_id)
    count = _journal_count(database)

    with pytest.raises(UndoNotEligibleError) as caught:
        undo.prepare_preview(first_id)

    assert caught.value.code is UndoEligibilityCode.SUPERSEDED
    assert _journal_count(database) == count
    assert media.get_path(media_id) == third / middle.name
    assert not original_source.exists()


def test_wrong_catalog_path_changed_identity_and_occupied_reverse_target_are_blocked(
    tmp_path: Path,
) -> None:
    values, operation_id, media_id, original_source, current = _committed_move(tmp_path)
    database, media, _operations, _service, _reads, undo, *_rest = values
    count = _journal_count(database)
    with database.transaction() as connection:
        media.update_path(media_id, current.with_name("catalog-only.mkv"), conn=connection)
    with pytest.raises(UndoNotEligibleError) as catalog_error:
        undo.prepare_preview(operation_id)
    assert catalog_error.value.code is UndoEligibilityCode.CATALOG_PATH_CHANGED

    with database.transaction() as connection:
        media.update_path(media_id, current, conn=connection)
    current.write_bytes(b"replacement")
    with pytest.raises(UndoNotEligibleError) as identity_error:
        undo.prepare_preview(operation_id)
    assert identity_error.value.code is UndoEligibilityCode.SOURCE_CHANGED

    current.unlink()
    current.write_bytes(b"movie-content")
    # Restore the persisted verification evidence to this explicit replacement for the collision branch.
    info = current.stat()
    with database.transaction() as connection:
        connection.execute(
            """
            UPDATE file_operations SET destination_size = ?, destination_mtime_ns = ?,
                destination_dev = ?, destination_ino = ? WHERE id = ?
            """,
            (info.st_size, info.st_mtime_ns, str(info.st_dev), str(info.st_ino), operation_id),
        )
    original_source.write_bytes(b"do-not-overwrite")
    with pytest.raises(UndoNotEligibleError) as collision_error:
        undo.prepare_preview(operation_id)
    assert collision_error.value.code is UndoEligibilityCode.DESTINATION_EXISTS
    assert original_source.read_bytes() == b"do-not-overwrite"
    assert _journal_count(database) == count


def test_destination_created_or_source_changed_after_preview_is_stale_without_journal(
    tmp_path: Path,
) -> None:
    values, operation_id, _media_id, original_source, current = _committed_move(tmp_path)
    database, *_prefix, undo, _recovery, _a, _b, _c = values
    preview = undo.prepare_preview(operation_id)
    original_source.write_bytes(b"collision")

    with pytest.raises(UndoPreviewStaleError):
        undo.confirm(preview.preview_id)

    assert original_source.read_bytes() == b"collision"
    assert current.read_bytes() == b"movie-content"
    assert _journal_count(database) == 1


def test_simulated_cross_volume_undo_reuses_copy_hash_pipeline(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    engine = SafeTransferEngine()
    values, operation_id, media_id, original_source, current = _committed_move(tmp_path, engine=engine)
    _database, media, operations, _service, _reads, undo, *_rest = values
    monkeypatch.setattr(
        engine,
        "_create_hardlink_destination",
        lambda *_args: (_ for _ in ()).throw(OSError(errno.EXDEV, "cross-volume")),
    )

    result = undo.confirm(undo.prepare_preview(operation_id).preview_id)

    reverse = operations.get(result.reverse_operation_id)
    assert reverse.strategy == "copy-sha256-fsync-finalize-unlink"
    assert reverse.destination_sha256 is not None
    assert media.get_path(media_id) == original_source
    assert original_source.read_bytes() == b"movie-content"
    assert not current.exists()


def test_recovery_inspection_preserves_both_and_disables_action(tmp_path: Path, monkeypatch) -> None:
    values = _setup(tmp_path)
    _database, media, operations, service, _reads, _undo, recovery, first, second, _third = values
    source = first / "Movie.mkv"
    destination = second / source.name
    source.write_bytes(b"movie")
    media_id = media.create(source, 5)
    plan = service.plan_move(source, destination, media_file_id=media_id)
    monkeypatch.setattr(
        service.engine,
        "_remove_source",
        lambda *_args: (_ for _ in ()).throw(OSError("interrupted")),
    )
    with pytest.raises(OSError):
        service.execute(plan.operation_id)

    assessment = recovery.inspect(plan.operation_id)

    assert assessment.disposition is RecoveryDisposition.AMBIGUOUS_BOTH_EXIST
    assert assessment.action_available is False
    assert source.read_bytes() == destination.read_bytes() == b"movie"
    with pytest.raises(RecoveryActionUnavailableError):
        recovery.attempt(plan.operation_id)
    assert operations.get(plan.operation_id).state is OperationState.RECOVERY_REQUIRED


def test_verified_destination_only_can_be_explicitly_reconciled(tmp_path: Path, monkeypatch) -> None:
    values = _setup(tmp_path)
    _database, media, operations, service, _reads, _undo, recovery, first, second, _third = values
    source = first / "Movie.mkv"
    destination = second / source.name
    source.write_bytes(b"movie")
    media_id = media.create(source, 5)
    plan = service.plan_move(source, destination, media_file_id=media_id)
    original_commit = service.store.commit_verified
    monkeypatch.setattr(
        service.store,
        "commit_verified",
        lambda *_args: (_ for _ in ()).throw(RuntimeError("simulated DB boundary")),
    )
    with pytest.raises(RuntimeError):
        service.execute(plan.operation_id)
    monkeypatch.setattr(service.store, "commit_verified", original_commit)

    assessment = recovery.inspect(plan.operation_id)
    result = recovery.attempt(plan.operation_id)

    assert assessment.disposition is RecoveryDisposition.VERIFIED_DESTINATION_ONLY
    assert assessment.action_available is True
    assert result.state is OperationStatus.COMMITTED
    assert operations.get(plan.operation_id).state is OperationState.COMMITTED
    assert media.get_path(media_id) == destination


def test_failed_and_missing_operations_are_controlled(tmp_path: Path) -> None:
    values = _setup(tmp_path)
    _database, _media, _operations, _service, _reads, undo, recovery, *_roots = values
    with pytest.raises(OperationHistoryNotFoundError):
        undo.prepare_preview("missing")
    with pytest.raises(OperationHistoryNotFoundError):
        recovery.inspect("missing")


def test_uncommitted_unlinked_and_already_reversed_operations_are_not_eligible(
    tmp_path: Path,
) -> None:
    values = _setup(tmp_path)
    _database, media, _operations, service, _reads, undo, _recovery, first, second, _third = values
    source = first / "Movie.mkv"
    source.write_bytes(b"movie")
    planned = service.plan_move(source, second / source.name)
    with pytest.raises(UndoNotEligibleError) as uncommitted:
        undo.prepare_preview(planned.operation_id)
    assert uncommitted.value.code is UndoEligibilityCode.NOT_COMMITTED

    service.execute(planned.operation_id)
    with pytest.raises(UndoNotEligibleError) as unlinked:
        undo.prepare_preview(planned.operation_id)
    assert unlinked.value.code is UndoEligibilityCode.NO_MEDIA_FILE

    linked_source = first / "Linked.mkv"
    linked_source.write_bytes(b"linked")
    media_id = media.create(linked_source, 6)
    linked = service.plan_move(linked_source, second / linked_source.name, media_file_id=media_id)
    service.execute(linked.operation_id)
    reverse = service.create_reverse_plan(linked.operation_id)
    service.execute(reverse.operation_id)
    with pytest.raises(UndoNotEligibleError) as reversed_error:
        undo.prepare_preview(linked.operation_id)
    assert reversed_error.value.code is UndoEligibilityCode.ALREADY_REVERSED


def test_rename_undo_uses_reverse_rename_and_restores_original_name(tmp_path: Path) -> None:
    values = _setup(tmp_path)
    _database, media, operations, service, _reads, undo, _recovery, first, _second, _third = values
    source = first / "Original.mkv"
    renamed = first / "Renamed.mkv"
    source.write_bytes(b"rename")
    media_id = media.create(source, 6)
    plan = service.plan_rename(source, renamed, media_file_id=media_id)
    service.execute(plan.operation_id)

    result = undo.confirm(undo.prepare_preview(plan.operation_id).preview_id)

    reverse = operations.get(result.reverse_operation_id)
    assert reverse.operation_type.value == "RENAME"
    assert reverse.reverses_operation_id == plan.operation_id
    assert source.read_bytes() == b"rename"
    assert not renamed.exists()


def test_missing_current_file_and_casefold_reverse_collision_are_controlled(
    tmp_path: Path,
) -> None:
    values, operation_id, _media_id, original_source, current = _committed_move(tmp_path)
    database, *_prefix, undo, _recovery, _a, _b, _c = values
    count = _journal_count(database)
    current.unlink()
    with pytest.raises(UndoNotEligibleError) as missing:
        undo.prepare_preview(operation_id)
    assert missing.value.code is UndoEligibilityCode.SOURCE_MISSING

    current.write_bytes(b"movie-content")
    info = current.stat()
    with database.transaction() as connection:
        connection.execute(
            """
            UPDATE file_operations SET destination_size = ?, destination_mtime_ns = ?,
                destination_dev = ?, destination_ino = ? WHERE id = ?
            """,
            (info.st_size, info.st_mtime_ns, str(info.st_dev), str(info.st_ino), operation_id),
        )
    collision = original_source.with_name(original_source.name.swapcase())
    collision.write_bytes(b"collision")
    with pytest.raises(UndoNotEligibleError) as collision_error:
        undo.prepare_preview(operation_id)
    assert collision_error.value.code in {
        UndoEligibilityCode.CASE_COLLISION,
        UndoEligibilityCode.DESTINATION_EXISTS,
    }
    assert collision.read_bytes() == b"collision"
    assert _journal_count(database) == count


def test_permission_failure_during_reverse_keeps_current_file_and_catalog_authoritative(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    engine = SafeTransferEngine()
    values, operation_id, media_id, original_source, current = _committed_move(tmp_path, engine=engine)
    database, media, operations, _service, _reads, undo, *_rest = values
    preview = undo.prepare_preview(operation_id)
    monkeypatch.setattr(
        engine,
        "_create_hardlink_destination",
        lambda *_args: (_ for _ in ()).throw(PermissionError("denied")),
    )

    with pytest.raises(UndoExecutionError):
        undo.confirm(preview.preview_id)

    assert current.read_bytes() == b"movie-content"
    assert not original_source.exists()
    assert media.get_path(media_id) == current
    assert _journal_count(database) == 2
    reverse = operations.get(operations.list_nonterminal()[0].id) if operations.list_nonterminal() else None
    assert reverse is None


def test_reverse_db_commit_failure_is_recoverable_without_losing_journal_link(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    values, operation_id, media_id, original_source, current = _committed_move(tmp_path)
    _database, media, operations, _service, _reads, undo, recovery, *_roots = values
    preview = undo.prepare_preview(operation_id)
    original_update = media.update_path
    monkeypatch.setattr(
        media,
        "update_path",
        lambda *_args, **_kwargs: (_ for _ in ()).throw(KeyError("db update failed")),
    )

    with pytest.raises(UndoRecoveryRequiredError) as caught:
        undo.confirm(preview.preview_id)

    reverse_id = caught.value.operation_id
    reverse = operations.get(reverse_id)
    assert reverse.reverses_operation_id == operation_id
    assert reverse.state is OperationState.FS_VERIFIED
    assert original_source.read_bytes() == b"movie-content"
    assert not current.exists()
    assert media.get_path(media_id) == current
    monkeypatch.setattr(media, "update_path", original_update)
    assert recovery.inspect(reverse_id).action_available is True
    assert recovery.attempt(reverse_id).state is OperationStatus.COMMITTED
    assert media.get_path(media_id) == original_source


def test_reverse_source_removal_failure_preserves_both_and_requires_manual_review(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    engine = SafeTransferEngine()
    values, operation_id, media_id, original_source, current = _committed_move(tmp_path, engine=engine)
    _database, media, operations, _service, _reads, undo, recovery, *_roots = values
    preview = undo.prepare_preview(operation_id)
    monkeypatch.setattr(
        engine,
        "_remove_source",
        lambda *_args: (_ for _ in ()).throw(OSError("unlink failed")),
    )

    with pytest.raises(UndoRecoveryRequiredError) as caught:
        undo.confirm(preview.preview_id)

    reverse = operations.get(caught.value.operation_id)
    assert reverse.state is OperationState.RECOVERY_REQUIRED
    assert reverse.reverses_operation_id == operation_id
    assert original_source.read_bytes() == current.read_bytes() == b"movie-content"
    assert media.get_path(media_id) == current
    assessment = recovery.inspect(reverse.id)
    assert assessment.disposition is RecoveryDisposition.AMBIGUOUS_BOTH_EXIST
    assert assessment.action_available is False


def test_neither_exists_and_tampered_destination_have_no_recovery_action(tmp_path: Path) -> None:
    values = _setup(tmp_path)
    _database, media, operations, service, _reads, _undo, recovery, first, second, _third = values
    source = first / "Movie.mkv"
    destination = second / source.name
    source.write_bytes(b"movie")
    media_id = media.create(source, 5)
    plan = service.plan_move(source, destination, media_file_id=media_id)
    operations.transition(plan.operation_id, OperationState.EXECUTING)
    source.unlink()
    neither = recovery.inspect(plan.operation_id)
    assert neither.disposition is RecoveryDisposition.AMBIGUOUS_NEITHER_EXISTS
    assert neither.action_available is False

    source.write_bytes(b"movie")
    second_plan = service.plan_move(source, destination, media_file_id=media_id)
    operations.transition(second_plan.operation_id, OperationState.EXECUTING)
    destination.write_bytes(b"tampered")
    source.unlink()
    unsafe = recovery.inspect(second_plan.operation_id)
    assert unsafe.disposition is RecoveryDisposition.UNSAFE_DESTINATION
    assert unsafe.action_available is False


def test_source_only_executing_recovery_explicitly_marks_failed_without_file_mutation(
    tmp_path: Path,
) -> None:
    values = _setup(tmp_path)
    _database, media, operations, service, _reads, _undo, recovery, first, second, _third = values
    source = first / "Movie.mkv"
    source.write_bytes(b"movie")
    media_id = media.create(source, 5)
    plan = service.plan_move(source, second / source.name, media_file_id=media_id)
    operations.transition(plan.operation_id, OperationState.EXECUTING)

    assessment = recovery.inspect(plan.operation_id)
    result = recovery.attempt(plan.operation_id)

    assert assessment.disposition is RecoveryDisposition.SAFE_TO_MARK_FAILED
    assert assessment.action_available is True
    assert result.state is OperationStatus.FAILED
    assert source.read_bytes() == b"movie"
    assert media.get_path(media_id) == source


def test_failed_reverse_still_blocks_duplicate_reverse_journal(tmp_path: Path) -> None:
    values, operation_id, _media_id, _original_source, _current = _committed_move(tmp_path)
    database, _media, operations, service, _reads, undo, *_rest = values
    reverse = service.create_reverse_plan(operation_id)
    operations.transition(reverse.operation_id, OperationState.FAILED)
    count = _journal_count(database)

    with pytest.raises(UndoNotEligibleError) as caught:
        undo.prepare_preview(operation_id)

    assert caught.value.code is UndoEligibilityCode.ALREADY_REVERSED
    assert _journal_count(database) == count


def test_store_serializes_competing_reverse_creation(tmp_path: Path) -> None:
    values, operation_id, _media_id, _original_source, _current = _committed_move(tmp_path)
    database, _media, _operations, service, _reads, _undo, *_rest = values
    service.create_reverse_plan(operation_id)

    with pytest.raises(Exception, match="already has reverse journal"):
        service.create_reverse_plan(operation_id)

    assert _journal_count(database) == 2


def test_cross_volume_digest_detects_content_tampering_even_with_restored_identity_facts(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    engine = SafeTransferEngine()
    monkeypatch.setattr(
        engine,
        "_create_hardlink_destination",
        lambda *_args: (_ for _ in ()).throw(OSError(errno.EXDEV, "cross-volume")),
    )
    values, operation_id, _media_id, _original_source, current = _committed_move(
        tmp_path,
        engine=engine,
    )
    _database, _media, operations, _service, _reads, undo, *_rest = values
    record = operations.get(operation_id)
    assert record.destination_sha256 is not None
    current.write_bytes(b"tampered-data")
    assert current.stat().st_size == record.destination_size
    os.utime(current, ns=(record.destination_mtime_ns, record.destination_mtime_ns))

    with pytest.raises(UndoNotEligibleError) as caught:
        undo.prepare_preview(operation_id)

    assert caught.value.code is UndoEligibilityCode.SOURCE_CHANGED
