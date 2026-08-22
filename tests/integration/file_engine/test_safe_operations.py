from __future__ import annotations

import errno
from pathlib import Path
import shutil
import sqlite3

import pytest

from dropsort.core.operations.errors import DatabaseCommitError
from dropsort.core.operations.models import OperationState
from dropsort.core.safety.errors import (
    CaseInsensitiveCollisionError,
    DestinationExistsError,
    SameFileError,
    SourceMissingError,
    UnsafePathError,
)


def _write(path: Path, data: bytes) -> Path:
    path.write_bytes(data)
    return path


def test_destination_already_exists_preserves_both_files(harness, media_bytes: bytes) -> None:
    source = _write(harness.source_root / "movie.mkv", media_bytes)
    destination = _write(harness.destination_root / "movie.mkv", b"existing")

    with pytest.raises(DestinationExistsError):
        harness.service.plan_move(source, destination)

    assert source.read_bytes() == media_bytes
    assert destination.read_bytes() == b"existing"
    assert harness.operations.list_nonterminal() == []


def test_source_disappears_after_plan_fails_without_destination(harness, media_bytes: bytes) -> None:
    source = _write(harness.source_root / "movie.mkv", media_bytes)
    destination = harness.destination_root / "movie.mkv"
    plan = harness.service.plan_move(source, destination)
    source.unlink()  # external change by test setup, not DropSort

    with pytest.raises(SourceMissingError):
        harness.service.execute(plan.operation_id)

    record = harness.operations.get(plan.operation_id)
    assert record.state is OperationState.RECOVERY_REQUIRED
    assert not destination.exists()


def test_permission_denied_keeps_source_and_marks_failed(harness, media_bytes: bytes, monkeypatch) -> None:
    source = _write(harness.source_root / "movie.mkv", media_bytes)
    destination = harness.destination_root / "movie.mkv"
    plan = harness.service.plan_move(source, destination)

    def deny(*args, **kwargs):
        raise PermissionError(errno.EACCES, "permission denied")

    monkeypatch.setattr(harness.service.engine, "_create_hardlink_destination", deny)

    with pytest.raises(PermissionError):
        harness.service.execute(plan.operation_id)

    assert source.exists()
    assert not destination.exists()
    assert harness.operations.get(plan.operation_id).state is OperationState.FAILED


def test_destination_drive_unavailable_after_plan_keeps_source(harness, media_bytes: bytes) -> None:
    source = _write(harness.source_root / "movie.mkv", media_bytes)
    destination = harness.destination_root / "movie.mkv"
    plan = harness.service.plan_move(source, destination)
    shutil.rmtree(harness.destination_root)

    with pytest.raises(UnsafePathError):
        harness.service.execute(plan.operation_id)

    assert source.exists()
    assert harness.operations.get(plan.operation_id).state is OperationState.FAILED


def test_interrupted_after_destination_creation_preserves_both(harness, media_bytes: bytes, monkeypatch) -> None:
    source = _write(harness.source_root / "movie.mkv", media_bytes)
    destination = harness.destination_root / "movie.mkv"
    plan = harness.service.plan_move(source, destination)

    def interrupt(*args, **kwargs):
        raise OSError("simulated interruption")

    monkeypatch.setattr(harness.service.engine, "_remove_source", interrupt)

    with pytest.raises(OSError, match="simulated interruption"):
        harness.service.execute(plan.operation_id)

    assert source.exists()
    assert destination.exists()
    assert source.read_bytes() == destination.read_bytes() == media_bytes
    assert harness.operations.get(plan.operation_id).state is OperationState.RECOVERY_REQUIRED


def test_db_failure_after_filesystem_success_is_recoverable(harness, media_bytes: bytes, monkeypatch) -> None:
    source = _write(harness.source_root / "movie.mkv", media_bytes)
    destination = harness.destination_root / "movie.mkv"
    media_file_id = harness.media_files.create(source, len(media_bytes))
    plan = harness.service.plan_move(source, destination, media_file_id=media_file_id)
    original_update = harness.media_files.update_path

    def fail_update(*args, **kwargs):
        raise sqlite3.OperationalError("simulated database failure")

    monkeypatch.setattr(harness.media_files, "update_path", fail_update)

    with pytest.raises(DatabaseCommitError):
        harness.service.execute(plan.operation_id)

    assert not source.exists()
    assert destination.exists()
    assert harness.media_files.get_path(media_file_id) == source
    assert harness.operations.get(plan.operation_id).state is OperationState.FS_VERIFIED

    monkeypatch.setattr(harness.media_files, "update_path", original_update)
    recovered = harness.recovery.reconcile(plan.operation_id)
    assert recovered.state is OperationState.COMMITTED
    assert harness.media_files.get_path(media_file_id) == destination


def test_same_file_is_rejected_before_journaled_execution(harness, media_bytes: bytes) -> None:
    source = _write(harness.source_root / "movie.mkv", media_bytes)
    with pytest.raises(SameFileError):
        harness.service.plan_move(source, source)
    assert source.read_bytes() == media_bytes


def test_path_outside_roots_is_rejected(harness, media_bytes: bytes, tmp_path: Path) -> None:
    source = _write(harness.source_root / "movie.mkv", media_bytes)
    outside = tmp_path / "outside"
    outside.mkdir()
    with pytest.raises(UnsafePathError):
        harness.service.plan_move(source, outside / "movie.mkv")
    assert source.exists()


def test_case_insensitive_collision_is_rejected(harness, media_bytes: bytes) -> None:
    source = _write(harness.source_root / "source.mkv", media_bytes)
    _write(harness.destination_root / "Movie.MKV", b"existing")
    with pytest.raises(CaseInsensitiveCollisionError):
        harness.service.plan_move(source, harness.destination_root / "movie.mkv")
    assert source.exists()


def test_restart_after_fs_success_before_db_commit_reconciles(harness, media_bytes: bytes, monkeypatch) -> None:
    source = _write(harness.source_root / "movie.mkv", media_bytes)
    destination = harness.destination_root / "movie.mkv"
    media_file_id = harness.media_files.create(source, len(media_bytes))
    plan = harness.service.plan_move(source, destination, media_file_id=media_file_id)
    original_update = harness.media_files.update_path

    monkeypatch.setattr(
        harness.media_files,
        "update_path",
        lambda *args, **kwargs: (_ for _ in ()).throw(sqlite3.OperationalError("crash boundary")),
    )
    with pytest.raises(DatabaseCommitError):
        harness.service.execute(plan.operation_id)
    monkeypatch.setattr(harness.media_files, "update_path", original_update)

    # New service/recovery objects simulate a fresh application process.
    from dropsort.core.operations import FileOperationService, RecoveryService
    from dropsort.core.safety import PathPolicy
    from dropsort.database.repositories import FileOperationRepository, MediaFileRepository, SqliteOperationStore

    operations = FileOperationRepository(harness.database)
    media_files = MediaFileRepository(harness.database)
    store = SqliteOperationStore(harness.database, operations, media_files)
    service = FileOperationService(
        PathPolicy([harness.source_root, harness.destination_root]),
        store,
    )
    recovery = RecoveryService(
        store, PathPolicy([harness.source_root, harness.destination_root])
    )
    recovered = recovery.reconcile(plan.operation_id)

    assert recovered.state is OperationState.COMMITTED
    assert media_files.get_path(media_file_id) == destination
    assert destination.read_bytes() == media_bytes


def test_both_files_after_interruption_remain_untouched_on_recovery(harness, media_bytes: bytes, monkeypatch) -> None:
    source = _write(harness.source_root / "movie.mkv", media_bytes)
    destination = harness.destination_root / "movie.mkv"
    plan = harness.service.plan_move(source, destination)
    monkeypatch.setattr(
        harness.service.engine,
        "_remove_source",
        lambda *args, **kwargs: (_ for _ in ()).throw(OSError("interrupted")),
    )
    with pytest.raises(OSError):
        harness.service.execute(plan.operation_id)

    recovered = harness.recovery.reconcile(plan.operation_id)
    assert recovered.state is OperationState.RECOVERY_REQUIRED
    assert source.read_bytes() == media_bytes
    assert destination.read_bytes() == media_bytes


def test_neither_source_nor_destination_marks_recovery_required(harness, media_bytes: bytes) -> None:
    source = _write(harness.source_root / "movie.mkv", media_bytes)
    destination = harness.destination_root / "movie.mkv"
    plan = harness.service.plan_move(source, destination)
    harness.operations.transition(plan.operation_id, OperationState.EXECUTING)
    source.unlink()  # external disappearance to model catastrophic/unknown state

    recovered = harness.recovery.reconcile(plan.operation_id)
    assert recovered.state is OperationState.RECOVERY_REQUIRED
    assert not source.exists()
    assert not destination.exists()


def test_cross_volume_fallback_copies_verifies_and_removes_source(harness, media_bytes: bytes, monkeypatch) -> None:
    source = _write(harness.source_root / "movie.mkv", media_bytes)
    destination = harness.destination_root / "movie.mkv"
    plan = harness.service.plan_move(source, destination)

    def cross_volume(*args, **kwargs):
        raise OSError(errno.EXDEV, "cross-device link")

    monkeypatch.setattr(harness.service.engine, "_create_hardlink_destination", cross_volume)
    record = harness.service.execute(plan.operation_id)

    assert record.state is OperationState.COMMITTED
    assert record.strategy == "copy-sha256-fsync-finalize-unlink"
    assert record.destination_sha256 is not None
    assert not source.exists()
    assert destination.read_bytes() == media_bytes


def test_committed_operation_can_create_reverse_plan(harness, media_bytes: bytes) -> None:
    source = _write(harness.source_root / "movie.mkv", media_bytes)
    destination = harness.destination_root / "movie.mkv"
    plan = harness.service.plan_move(source, destination)
    committed = harness.service.execute(plan.operation_id)

    reverse = harness.service.create_reverse_plan(committed.id)
    reverse_record = harness.operations.get(reverse.operation_id)
    assert reverse.source == destination
    assert reverse.destination == source
    assert reverse_record.state is OperationState.VALIDATED
    assert reverse_record.reverses_operation_id == committed.id


def test_rename_uses_same_safety_pipeline(harness, media_bytes: bytes) -> None:
    source = _write(harness.source_root / "old.mkv", media_bytes)
    destination = harness.source_root / "new.mkv"
    plan = harness.service.plan_rename(source, destination)
    record = harness.service.execute(plan.operation_id)

    assert record.state is OperationState.COMMITTED
    assert not source.exists()
    assert destination.read_bytes() == media_bytes


def test_fs_verified_is_persisted_before_source_removal(harness, media_bytes: bytes, monkeypatch) -> None:
    source = _write(harness.source_root / "movie-before-remove.mkv", media_bytes)
    destination = harness.destination_root / "movie-before-remove.mkv"
    plan = harness.service.plan_move(source, destination)
    original_remove = harness.service.engine._remove_source

    def assert_journal_then_remove(path: Path) -> None:
        record = harness.operations.get(plan.operation_id)
        assert record.state is OperationState.FS_VERIFIED
        assert record.destination_size == len(media_bytes)
        original_remove(path)

    monkeypatch.setattr(harness.service.engine, "_remove_source", assert_journal_then_remove)
    record = harness.service.execute(plan.operation_id)
    assert record.state is OperationState.COMMITTED


def test_source_identity_change_after_plan_is_rejected(harness, media_bytes: bytes) -> None:
    from dropsort.core.safety.errors import SourceChangedError

    source = _write(harness.source_root / "changing.mkv", media_bytes)
    destination = harness.destination_root / "changing.mkv"
    plan = harness.service.plan_move(source, destination)
    source.write_bytes(media_bytes + b"changed")

    with pytest.raises(SourceChangedError):
        harness.service.execute(plan.operation_id)

    assert source.exists()
    assert not destination.exists()
    assert harness.operations.get(plan.operation_id).state is OperationState.FAILED


def test_recovery_rejects_tampered_verified_cross_volume_destination(
    harness, media_bytes: bytes, monkeypatch
) -> None:
    source = _write(harness.source_root / "tamper.mkv", media_bytes)
    destination = harness.destination_root / "tamper.mkv"
    plan = harness.service.plan_move(source, destination)

    def cross_volume(*args, **kwargs):
        raise OSError(errno.EXDEV, "cross-device link")

    monkeypatch.setattr(harness.service.engine, "_create_hardlink_destination", cross_volume)
    monkeypatch.setattr(
        harness.service.engine,
        "_remove_source",
        lambda *args, **kwargs: (_ for _ in ()).throw(OSError("stop after verification")),
    )
    with pytest.raises(OSError):
        harness.service.execute(plan.operation_id)

    # Simulate an external actor replacing/changing the verified destination before restart recovery.
    destination.write_bytes(b"X" * len(media_bytes))
    source.unlink()
    recovered = harness.recovery.reconcile(plan.operation_id)

    assert recovered.state is OperationState.RECOVERY_REQUIRED
    assert destination.read_bytes() == b"X" * len(media_bytes)


def test_failed_cross_volume_copy_cleans_only_its_temp_file(harness, media_bytes: bytes, monkeypatch) -> None:
    source = _write(harness.source_root / "copy-fail.mkv", media_bytes)
    destination = harness.destination_root / "copy-fail.mkv"
    plan = harness.service.plan_move(source, destination)

    monkeypatch.setattr(
        harness.service.engine,
        "_create_hardlink_destination",
        lambda *args, **kwargs: (_ for _ in ()).throw(OSError(errno.EXDEV, "cross-device")),
    )

    def fail_copy(source_path: Path, temp_path: Path) -> str:
        temp_path.write_bytes(b"partial")
        raise OSError("simulated copy failure")

    monkeypatch.setattr(harness.service.engine, "_copy_and_hash", fail_copy)
    with pytest.raises(OSError, match="simulated copy failure"):
        harness.service.execute(plan.operation_id)

    assert source.read_bytes() == media_bytes
    assert not destination.exists()
    assert list(harness.destination_root.glob("*.tmp")) == []
    assert harness.operations.get(plan.operation_id).state is OperationState.FAILED


def test_restart_while_executing_with_both_paths_preserves_both(harness, media_bytes: bytes) -> None:
    source = _write(harness.source_root / "restart-both.mkv", media_bytes)
    destination = harness.destination_root / "restart-both.mkv"
    plan = harness.service.plan_move(source, destination)
    harness.operations.transition(plan.operation_id, OperationState.EXECUTING)
    # Simulate the crash point immediately after destination creation but before FS_VERIFIED is persisted.
    import os
    os.link(source, destination)

    recovered = harness.recovery.reconcile(plan.operation_id)
    assert recovered.state is OperationState.RECOVERY_REQUIRED
    assert source.read_bytes() == destination.read_bytes() == media_bytes


def test_executing_with_intact_source_and_no_destination_recovers_to_failed(harness, media_bytes: bytes) -> None:
    source = _write(harness.source_root / "pre-destination.mkv", media_bytes)
    destination = harness.destination_root / "pre-destination.mkv"
    plan = harness.service.plan_move(source, destination)
    harness.operations.transition(plan.operation_id, OperationState.EXECUTING)

    recovered = harness.recovery.reconcile(plan.operation_id)
    assert recovered.state is OperationState.FAILED
    assert source.read_bytes() == media_bytes
    assert not destination.exists()


def test_persistent_db_failure_during_recovery_leaves_fs_verified(harness, media_bytes: bytes, monkeypatch) -> None:
    source = _write(harness.source_root / "db-still-down.mkv", media_bytes)
    destination = harness.destination_root / "db-still-down.mkv"
    media_file_id = harness.media_files.create(source, len(media_bytes))
    plan = harness.service.plan_move(source, destination, media_file_id=media_file_id)

    def fail_update(*args, **kwargs):
        raise sqlite3.OperationalError("database remains unavailable")

    monkeypatch.setattr(harness.media_files, "update_path", fail_update)
    with pytest.raises(DatabaseCommitError):
        harness.service.execute(plan.operation_id)

    recovered = harness.recovery.reconcile(plan.operation_id)
    assert recovered.state is OperationState.FS_VERIFIED
    assert harness.media_files.get_path(media_file_id) == source
    assert not source.exists()
    assert destination.exists()


def test_recovery_required_can_commit_after_external_source_removal(harness, media_bytes: bytes, monkeypatch) -> None:
    source = _write(harness.source_root / "manual-resolve.mkv", media_bytes)
    destination = harness.destination_root / "manual-resolve.mkv"
    plan = harness.service.plan_move(source, destination)
    monkeypatch.setattr(
        harness.service.engine,
        "_remove_source",
        lambda *args, **kwargs: (_ for _ in ()).throw(OSError("interrupted")),
    )
    with pytest.raises(OSError):
        harness.service.execute(plan.operation_id)
    assert harness.operations.get(plan.operation_id).state is OperationState.RECOVERY_REQUIRED

    source.unlink()  # external/manual resolution: preserve destination, remove source
    recovered = harness.recovery.reconcile(plan.operation_id)
    assert recovered.state is OperationState.COMMITTED
    assert destination.read_bytes() == media_bytes


def test_cross_volume_hash_failure_never_removes_source(harness, media_bytes: bytes, monkeypatch) -> None:
    source = _write(harness.source_root / "hash-fail.mkv", media_bytes)
    destination = harness.destination_root / "hash-fail.mkv"
    plan = harness.service.plan_move(source, destination)
    monkeypatch.setattr(
        harness.service.engine,
        "_create_hardlink_destination",
        lambda *args, **kwargs: (_ for _ in ()).throw(OSError(errno.EXDEV, "cross-device")),
    )
    monkeypatch.setattr(harness.service.engine, "_sha256", lambda path: "not-the-source-digest")

    with pytest.raises(OSError, match="SHA-256"):
        harness.service.execute(plan.operation_id)

    assert source.read_bytes() == media_bytes
    assert destination.exists()
    assert harness.operations.get(plan.operation_id).state is OperationState.RECOVERY_REQUIRED


def test_source_removal_failure_does_not_advance_authoritative_media_path(
    harness, media_bytes: bytes, monkeypatch
) -> None:
    source = _write(harness.source_root / "journal-before-unlink-db.mkv", media_bytes)
    destination = harness.destination_root / "journal-before-unlink-db.mkv"
    media_file_id = harness.media_files.create(source, len(media_bytes))
    plan = harness.service.plan_move(source, destination, media_file_id=media_file_id)

    monkeypatch.setattr(
        harness.service.engine,
        "_remove_source",
        lambda *args, **kwargs: (_ for _ in ()).throw(OSError("simulated unlink failure")),
    )

    with pytest.raises(OSError, match="simulated unlink failure"):
        harness.service.execute(plan.operation_id)

    assert source.exists()
    assert destination.exists()
    assert harness.media_files.get_path(media_file_id) == source
    assert harness.operations.get(plan.operation_id).state is OperationState.RECOVERY_REQUIRED


def test_recovery_rejects_destination_replaced_by_symlink(
    harness, media_bytes: bytes, monkeypatch, tmp_path: Path
) -> None:
    source = _write(harness.source_root / "symlink-recovery.mkv", media_bytes)
    destination = harness.destination_root / "symlink-recovery.mkv"
    media_file_id = harness.media_files.create(source, len(media_bytes))
    plan = harness.service.plan_move(source, destination, media_file_id=media_file_id)

    monkeypatch.setattr(
        harness.service.engine,
        "_remove_source",
        lambda *args, **kwargs: (_ for _ in ()).throw(OSError("stop after verification")),
    )
    with pytest.raises(OSError):
        harness.service.execute(plan.operation_id)

    destination.unlink()
    outside = _write(tmp_path / "outside-recovery.mkv", media_bytes)
    try:
        destination.symlink_to(outside)
    except (OSError, NotImplementedError):
        pytest.skip("symlinks unavailable on this host")
    source.unlink()

    recovered = harness.recovery.reconcile(plan.operation_id)

    assert recovered.state is OperationState.RECOVERY_REQUIRED
    assert harness.media_files.get_path(media_file_id) == source
    assert outside.read_bytes() == media_bytes
