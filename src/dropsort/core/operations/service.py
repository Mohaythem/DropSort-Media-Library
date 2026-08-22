from __future__ import annotations

from pathlib import Path
from uuid import uuid4

from dropsort.core.file_engine.transfer import SafeTransferEngine
from dropsort.core.operations.errors import InvalidOperationStateError
from dropsort.core.operations.models import (
    FileOperationPlan,
    FileOperationRecord,
    OperationState,
    OperationType,
    OperationUpdate,
)
from dropsort.core.operations.ports import OperationStore
from dropsort.core.safety.errors import SourceChangedError
from dropsort.core.safety.path_policy import PathPolicy, SourceIdentity


class FileOperationService:
    """Application-facing entry point for journaled media Move/Rename mutations."""

    def __init__(
        self,
        path_policy: PathPolicy,
        store: OperationStore,
        engine: SafeTransferEngine | None = None,
    ) -> None:
        self.path_policy = path_policy
        self.store = store
        self.engine = engine or SafeTransferEngine()

    def plan_move(
        self,
        source: Path,
        destination: Path,
        *,
        media_file_id: int | None = None,
        reverses_operation_id: str | None = None,
        expected_source_identity: SourceIdentity | None = None,
    ) -> FileOperationPlan:
        return self._plan(FileOperationPlan(
            operation_id=str(uuid4()),
            operation_type=OperationType.MOVE,
            source=source,
            destination=destination,
            media_file_id=media_file_id,
            reverses_operation_id=reverses_operation_id,
        ), expected_source_identity=expected_source_identity)

    def plan_rename(
        self,
        source: Path,
        destination: Path,
        *,
        media_file_id: int | None = None,
        reverses_operation_id: str | None = None,
        expected_source_identity: SourceIdentity | None = None,
    ) -> FileOperationPlan:
        return self._plan(FileOperationPlan(
            operation_id=str(uuid4()),
            operation_type=OperationType.RENAME,
            source=source,
            destination=destination,
            media_file_id=media_file_id,
            reverses_operation_id=reverses_operation_id,
        ), expected_source_identity=expected_source_identity)

    def _plan(
        self,
        requested: FileOperationPlan,
        *,
        expected_source_identity: SourceIdentity | None = None,
    ) -> FileOperationPlan:
        canonical_source, canonical_destination, identity = self.path_policy.validate_plan(
            requested.source, requested.destination, requested.operation_type
        )
        if expected_source_identity is not None and identity != expected_source_identity:
            raise SourceChangedError("Source identity changed after preview")
        plan = FileOperationPlan(
            operation_id=requested.operation_id,
            operation_type=requested.operation_type,
            source=canonical_source,
            destination=canonical_destination,
            media_file_id=requested.media_file_id,
            reverses_operation_id=requested.reverses_operation_id,
        )
        self.store.create(plan)
        self.store.transition(
            plan.operation_id,
            OperationState.VALIDATED,
            OperationUpdate(
                source_size=identity.size,
                source_mtime_ns=identity.mtime_ns,
                source_dev=identity.dev,
                source_ino=identity.ino,
            ),
        )
        return plan

    def execute(self, operation_id: str) -> FileOperationRecord:
        record = self.store.get(operation_id)
        if record.state is not OperationState.VALIDATED:
            raise InvalidOperationStateError(f"Operation must be VALIDATED, got {record.state.value}")
        self.store.transition(operation_id, OperationState.EXECUTING)
        record = self.store.get(operation_id)
        try:
            identity = self.path_policy.revalidate_record(record)
            prepared = self.engine.prepare(record.source, record.destination, identity, record.id)
            self.store.transition(
                record.id,
                OperationState.FS_VERIFIED,
                OperationUpdate(
                    destination_size=prepared.destination_size,
                    destination_mtime_ns=prepared.destination_mtime_ns,
                    destination_dev=prepared.destination_dev,
                    destination_ino=prepared.destination_ino,
                    destination_sha256=prepared.destination_sha256,
                    strategy=prepared.strategy,
                ),
            )
            self.engine.finalize_source_removal(record.source, record.destination, identity, prepared)
        except Exception as exc:
            self._record_execution_failure(record.id, exc)
            raise
        return self.store.commit_verified(record.id)

    def create_reverse_plan(
        self,
        operation_id: str,
        *,
        expected_source_identity: SourceIdentity | None = None,
    ) -> FileOperationPlan:
        record = self.store.get(operation_id)
        if record.state is not OperationState.COMMITTED:
            raise InvalidOperationStateError("Only committed operations can be reversed")
        planner = self.plan_rename if record.operation_type is OperationType.RENAME else self.plan_move
        return planner(
            record.destination,
            record.source,
            media_file_id=record.media_file_id,
            reverses_operation_id=record.id,
            expected_source_identity=expected_source_identity,
        )

    def _record_execution_failure(self, operation_id: str, exc: Exception) -> None:
        record = self.store.get(operation_id)
        source_exists = record.source.exists()
        destination_exists = record.destination.exists()
        if record.state is OperationState.EXECUTING and source_exists and not destination_exists:
            target_state = OperationState.FAILED
        else:
            target_state = OperationState.RECOVERY_REQUIRED
        self.store.transition(
            operation_id,
            target_state,
            OperationUpdate(error_code=type(exc).__name__, error_message=str(exc)),
        )
