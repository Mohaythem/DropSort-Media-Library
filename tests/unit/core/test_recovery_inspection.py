from __future__ import annotations

import hashlib
from pathlib import Path

from dropsort.core.operations import OperationState, RecoverySituation
from dropsort.core.operations.models import OperationUpdate


def test_terminal_and_preexecution_inspection_are_read_only(harness, media_bytes: bytes) -> None:
    source = harness.source_root / "Movie.mkv"
    source.write_bytes(media_bytes)
    planned = harness.service.plan_move(source, harness.destination_root / source.name)
    validated = harness.recovery.inspect(planned.operation_id)
    reconciled = harness.recovery.reconcile(planned.operation_id)

    assert validated.situation is RecoverySituation.NOT_ACTIONABLE
    assert validated.can_reconcile is False
    assert reconciled.state is OperationState.VALIDATED
    committed = harness.service.execute(planned.operation_id)
    terminal = harness.recovery.inspect(committed.id)
    assert terminal.situation is RecoverySituation.NOT_REQUIRED
    assert harness.recovery.reconcile(committed.id).state is OperationState.COMMITTED


def test_reconcile_pending_handles_multiple_nonterminal_records(harness, media_bytes: bytes) -> None:
    first = harness.source_root / "First.mkv"
    second = harness.source_root / "Second.mkv"
    first.write_bytes(media_bytes)
    second.write_bytes(media_bytes)
    first_plan = harness.service.plan_move(first, harness.destination_root / first.name)
    second_plan = harness.service.plan_move(second, harness.destination_root / second.name)
    harness.operations.transition(first_plan.operation_id, OperationState.EXECUTING)

    results = harness.recovery.reconcile_pending()

    assert [record.id for record in results] == [first_plan.operation_id, second_plan.operation_id]
    assert [record.state for record in results] == [OperationState.FAILED, OperationState.VALIDATED]


def test_source_only_verified_conflict_becomes_recovery_required(harness, media_bytes: bytes) -> None:
    source = harness.source_root / "Movie.mkv"
    source.write_bytes(media_bytes)
    plan = harness.service.plan_move(source, harness.destination_root / source.name)
    harness.operations.transition(plan.operation_id, OperationState.EXECUTING)
    harness.operations.transition(plan.operation_id, OperationState.FS_VERIFIED)

    result = harness.recovery.reconcile(plan.operation_id)

    assert result.state is OperationState.RECOVERY_REQUIRED
    assert source.read_bytes() == media_bytes


def test_destination_only_without_evidence_is_unsafe(harness, media_bytes: bytes) -> None:
    source = harness.source_root / "Movie.mkv"
    destination = harness.destination_root / source.name
    source.write_bytes(media_bytes)
    plan = harness.service.plan_move(source, destination)
    harness.operations.transition(plan.operation_id, OperationState.EXECUTING)
    destination.write_bytes(media_bytes)
    source.unlink()

    inspection = harness.recovery.inspect(plan.operation_id)
    result = harness.recovery.reconcile(plan.operation_id)

    assert inspection.situation is RecoverySituation.DESTINATION_UNSAFE_OR_CHANGED
    assert result.state is OperationState.RECOVERY_REQUIRED
    assert destination.read_bytes() == media_bytes


def test_verification_mismatch_branches_and_sha256_are_conservative(
    harness,
    media_bytes: bytes,
) -> None:
    destination = harness.destination_root / "Movie.mkv"
    destination.write_bytes(media_bytes)
    info = destination.stat()
    source = harness.source_root / "Movie.mkv"
    source.write_bytes(media_bytes)
    plan = harness.service.plan_move(source, destination.with_name("Unused.mkv"))
    record = harness.operations.transition(plan.operation_id, OperationState.EXECUTING)

    base = OperationUpdate(
        destination_size=info.st_size,
        destination_mtime_ns=info.st_mtime_ns,
        destination_dev=info.st_dev,
        destination_ino=info.st_ino,
        destination_sha256=hashlib.sha256(media_bytes).hexdigest(),
    )
    record = harness.operations.transition(record.id, OperationState.FS_VERIFIED, base)
    from dataclasses import replace

    assert harness.recovery._destination_matches_verification(
        replace(record, destination=destination)
    ) is True
    assert harness.recovery._destination_matches_verification(
        replace(record, destination=destination, destination_size=info.st_size + 1)
    ) is False
    assert harness.recovery._destination_matches_verification(
        replace(record, destination=destination, destination_mtime_ns=info.st_mtime_ns + 1)
    ) is False
    assert harness.recovery._destination_matches_verification(
        replace(record, destination=destination, destination_dev=info.st_dev + 1)
    ) is False
    assert harness.recovery._destination_matches_verification(
        replace(record, destination=destination, destination_ino=info.st_ino + 1)
    ) is False
    assert harness.recovery._destination_matches_verification(
        replace(record, destination=destination, destination_sha256="wrong")
    ) is False
