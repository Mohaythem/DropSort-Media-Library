from __future__ import annotations

import hashlib
import os
from pathlib import Path

from dropsort.core.operations.errors import DatabaseCommitError
from dropsort.core.operations.models import (
    FileOperationRecord,
    OperationState,
    OperationUpdate,
    RecoveryInspection,
    RecoverySituation,
)
from dropsort.core.operations.ports import OperationStore
from dropsort.core.safety.errors import FileSafetyError
from dropsort.core.safety.path_policy import PathPolicy


class RecoveryService:
    """Reconciles durable journal state without deleting ambiguous files."""

    _BUFFER_SIZE = 4 * 1024 * 1024

    def __init__(self, store: OperationStore, path_policy: PathPolicy) -> None:
        self.store = store
        self.path_policy = path_policy

    def reconcile_pending(self) -> list[FileOperationRecord]:
        return [self.reconcile(record.id) for record in self.store.list_nonterminal()]

    def inspect(self, operation_id: str) -> RecoveryInspection:
        """Read filesystem state without changing files, catalog paths, or journal state."""
        record = self.store.get(operation_id)
        if record.state in {OperationState.COMMITTED, OperationState.FAILED}:
            return self._inspection(
                record,
                RecoverySituation.NOT_REQUIRED,
                False,
                "This operation is terminal and requires no recovery action",
            )
        if record.state in {OperationState.PLANNED, OperationState.VALIDATED}:
            return self._inspection(
                record,
                RecoverySituation.NOT_ACTIONABLE,
                False,
                "The operation has not entered filesystem execution",
            )

        source_exists = os.path.lexists(record.source)
        destination_exists = os.path.lexists(record.destination)
        if source_exists and destination_exists:
            return RecoveryInspection(
                record.id,
                record.state,
                RecoverySituation.BOTH_EXIST,
                True,
                True,
                False,
                "Both source and destination exist; DropSort will preserve both",
            )
        if not source_exists and not destination_exists:
            return RecoveryInspection(
                record.id,
                record.state,
                RecoverySituation.NEITHER_EXISTS,
                False,
                False,
                False,
                "Neither source nor destination exists; automatic recovery is unsafe",
            )
        if source_exists:
            actionable = record.state is OperationState.EXECUTING
            return RecoveryInspection(
                record.id,
                record.state,
                RecoverySituation.SOURCE_ONLY_EXECUTING
                if actionable
                else RecoverySituation.NOT_ACTIONABLE,
                True,
                False,
                actionable,
                "The intact source can be retained and this interrupted operation marked failed"
                if actionable
                else "The source-only filesystem state conflicts with the durable journal",
            )

        try:
            self.path_policy.validate_existing_recovery_path(record.destination)
            verified = self._destination_matches_verification(record)
        except (FileSafetyError, OSError):
            verified = False
        if not verified:
            return RecoveryInspection(
                record.id,
                record.state,
                RecoverySituation.DESTINATION_UNSAFE_OR_CHANGED,
                False,
                True,
                False,
                "The destination is unsafe or no longer matches recorded verification evidence",
            )
        actionable = record.state in {
            OperationState.EXECUTING,
            OperationState.FS_VERIFIED,
            OperationState.RECOVERY_REQUIRED,
        }
        return RecoveryInspection(
            record.id,
            record.state,
            RecoverySituation.DESTINATION_ONLY_VERIFIED
            if actionable
            else RecoverySituation.NOT_ACTIONABLE,
            False,
            True,
            actionable,
            "The verified destination can be committed to the catalog"
            if actionable
            else "The verified destination does not have an actionable journal state",
        )

    def reconcile(self, operation_id: str) -> FileOperationRecord:
        record = self.store.get(operation_id)
        if record.state in {OperationState.COMMITTED, OperationState.FAILED}:
            return record
        if record.state in {OperationState.PLANNED, OperationState.VALIDATED}:
            return record
        inspection = self.inspect(operation_id)
        if inspection.situation is RecoverySituation.BOTH_EXIST:
            return self._require_recovery(record, "Both source and destination exist; preserving both")
        if inspection.situation is RecoverySituation.NEITHER_EXISTS:
            return self._require_recovery(record, "Neither source nor destination exists")
        if inspection.situation is RecoverySituation.SOURCE_ONLY_EXECUTING:
            return self.store.transition(
                record.id,
                OperationState.FAILED,
                OperationUpdate(
                    error_code="InterruptedBeforeDestination",
                    error_message="Source is intact and destination is absent",
                ),
            )
        if inspection.source_exists and not inspection.destination_exists:
            return self._require_recovery(record, "Verified journal state conflicts with filesystem")
        if inspection.situation is RecoverySituation.DESTINATION_UNSAFE_OR_CHANGED:
            return self._require_recovery(record, "Destination violates recovery path policy")
        if inspection.situation is not RecoverySituation.DESTINATION_ONLY_VERIFIED:
            return self._require_recovery(record, inspection.message)
        if record.state in {OperationState.EXECUTING, OperationState.RECOVERY_REQUIRED}:
            record = self.store.transition(record.id, OperationState.FS_VERIFIED)
        try:
            return self.store.commit_verified(record.id)
        except DatabaseCommitError:
            return self.store.get(record.id)

    def _destination_matches_verification(self, record: FileOperationRecord) -> bool:
        evidence = (
            record.destination_size,
            record.destination_mtime_ns,
            record.destination_dev,
            record.destination_ino,
        )
        if any(value is None for value in evidence):
            return False
        info = record.destination.stat()
        if info.st_size != record.destination_size:
            return False
        if info.st_mtime_ns != record.destination_mtime_ns:
            return False
        if info.st_dev != record.destination_dev:
            return False
        if info.st_ino != record.destination_ino:
            return False
        if record.destination_sha256 is not None:
            return self._sha256(record.destination) == record.destination_sha256
        return True

    @classmethod
    def _sha256(cls, path: Path) -> str:
        digest = hashlib.sha256()
        with path.open("rb") as handle:
            while chunk := handle.read(cls._BUFFER_SIZE):
                digest.update(chunk)
        return digest.hexdigest()

    def _require_recovery(self, record: FileOperationRecord, message: str) -> FileOperationRecord:
        if record.state is OperationState.RECOVERY_REQUIRED:
            return record
        return self.store.transition(
            record.id,
            OperationState.RECOVERY_REQUIRED,
            OperationUpdate(error_code="AmbiguousFilesystemState", error_message=message),
        )

    @staticmethod
    def _inspection(
        record: FileOperationRecord,
        situation: RecoverySituation,
        can_reconcile: bool,
        message: str,
    ) -> RecoveryInspection:
        return RecoveryInspection(
            record.id,
            record.state,
            situation,
            os.path.lexists(record.source),
            os.path.lexists(record.destination),
            can_reconcile,
            message,
        )
