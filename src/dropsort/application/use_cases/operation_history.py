from __future__ import annotations

from collections import OrderedDict
from dataclasses import dataclass
from datetime import datetime
import hashlib
import os
from pathlib import Path
import threading
from uuid import uuid4

from dropsort.application.dto.operation_history import (
    OperationDetails,
    OperationHistoryItem,
    OperationHistoryQuery,
    OperationKind,
    OperationStatus,
    RecoveryAssessment,
    RecoveryDisposition,
    RecoveryResult,
    UndoEligibilityCode,
    UndoPreview,
    UndoResult,
)
from dropsort.application.errors import (
    OperationHistoryError,
    OperationHistoryNotFoundError,
    RecoveryActionUnavailableError,
    UndoAlreadyConfirmedError,
    UndoExecutionError,
    UndoNotEligibleError,
    UndoPreviewNotFoundError,
    UndoPreviewStaleError,
    UndoRecoveryRequiredError,
)
from dropsort.core.file_engine.transfer import SafeTransferEngine
from dropsort.core.operations import (
    FileOperationService,
    OperationState,
    OperationType,
    RecoveryService,
    RecoverySituation,
)
from dropsort.core.operations.errors import DatabaseCommitError
from dropsort.core.operations.ports import OperationStore
from dropsort.core.safety.errors import (
    CaseInsensitiveCollisionError,
    DestinationExistsError,
    FileSafetyError,
    LinkTraversalError,
    SameFileError,
    SourceChangedError,
    SourceMissingError,
    UnsafePathError,
)
from dropsort.core.safety.path_policy import PathPolicy, SourceIdentity
from dropsort.library.movies import MediaFileRepository
from dropsort.library.operations import (
    OperationJournalError,
    OperationJournalReadRepository,
    OperationJournalSnapshot,
)


class SaveOperationHistory:
    """Write a human-readable export without touching media or journal rows."""

    def execute(self, items: tuple[OperationHistoryItem, ...], path: str | Path) -> None:
        if not str(path).strip():
            raise ValueError("export path must be non-empty")
        destination = Path(path)
        destination.write_text(format_operation_history(items), encoding="utf-8")


def format_operation_history(items: tuple[OperationHistoryItem, ...]) -> str:
    """Return the exact human-readable text used by Save and clipboard Copy."""

    lines: list[str] = ["DropSort Operations Log", ""]
    for item in items:
        lines.extend(
            (
                f"{_operation_label(item.operation.value)} — {_status_label(item.state.value)}",
                item.movie_title or "Unlinked media operation",
                f"From: {item.source_path}",
                f"To: {item.destination_path}",
                f"Timestamp: {item.created_at.astimezone().strftime('%Y-%m-%d %H:%M:%S')}",
                f"Operation ID: {item.operation_id}",
                "",
            )
        )
    return "\n".join(lines)


def _operation_label(value: str) -> str:
    return {"MOVE": "Move", "RENAME": "Rename"}.get(value, value.title())


def _status_label(value: str) -> str:
    return {
        "PLANNED": "Planned",
        "VALIDATED": "Validated",
        "EXECUTING": "In progress",
        "FS_VERIFIED": "Verified",
        "COMMITTED": "Completed",
        "FAILED": "Failed",
        "RECOVERY_REQUIRED": "Recovery required",
    }.get(value, value.title())


class ListOperationHistory:
    def __init__(self, repository: OperationJournalReadRepository) -> None:
        self._repository = repository

    def execute(
        self,
        query: OperationHistoryQuery | None = None,
    ) -> tuple[OperationHistoryItem, ...]:
        request = query or OperationHistoryQuery()
        try:
            snapshots = self._repository.list_operations(
                limit=request.limit,
                offset=request.offset,
            )
            return tuple(_to_history_item(snapshot) for snapshot in snapshots)
        except OperationJournalError as error:
            raise OperationHistoryError("could not read operation history") from error


class GetOperationDetails:
    def __init__(self, repository: OperationJournalReadRepository) -> None:
        self._repository = repository

    def execute(self, operation_id: str) -> OperationDetails:
        snapshot = _require_snapshot(self._repository, operation_id)
        record = snapshot.record
        return OperationDetails(
            history=_to_history_item(snapshot),
            current_catalog_path=None
            if snapshot.current_catalog_path is None
            else str(snapshot.current_catalog_path),
            strategy=record.strategy,
            error_code=record.error_code,
            error_message=record.error_message,
            reversed_by_operation_id=snapshot.reversed_by_operation_id,
            source_size=record.source_size,
            destination_size=record.destination_size,
            destination_sha256=record.destination_sha256,
        )


@dataclass(frozen=True, slots=True)
class _PreparedUndo:
    preview: UndoPreview
    policy: PathPolicy
    source_identity: SourceIdentity


class UndoFileOperation:
    """Prepare and explicitly execute an exact reverse through the Phase 1 pipeline."""

    _MAX_PREVIEWS = 256

    def __init__(
        self,
        journal: OperationJournalReadRepository,
        media_files: MediaFileRepository,
        operation_store: OperationStore,
        *,
        engine: SafeTransferEngine | None = None,
        execution_lock: threading.Lock | None = None,
    ) -> None:
        self._journal = journal
        self._media_files = media_files
        self._operation_store = operation_store
        self._engine = engine
        self._lock = threading.Lock()
        self._execution_lock = execution_lock or threading.Lock()
        self._prepared: OrderedDict[str, _PreparedUndo] = OrderedDict()
        self._consumed: OrderedDict[str, None] = OrderedDict()

    def prepare_preview(self, operation_id: str) -> UndoPreview:
        prepared = self._evaluate(operation_id)
        with self._lock:
            self._prepared[prepared.preview.preview_id] = prepared
            while len(self._prepared) > self._MAX_PREVIEWS:
                self._prepared.popitem(last=False)
        return prepared.preview

    def discard_preview(self, preview_id: str) -> None:
        with self._lock:
            self._prepared.pop(preview_id, None)

    def confirm(self, preview_id: str) -> UndoResult:
        with self._lock:
            if preview_id in self._consumed:
                raise UndoAlreadyConfirmedError("This undo preview was already confirmed")
            prepared = self._prepared.pop(preview_id, None)
            if prepared is None:
                raise UndoPreviewNotFoundError("This undo preview is no longer available")
            self._consumed[preview_id] = None
            while len(self._consumed) > self._MAX_PREVIEWS:
                self._consumed.popitem(last=False)
        with self._execution_lock:
            return self._execute(prepared)

    def _evaluate(self, operation_id: str) -> _PreparedUndo:
        snapshot = _require_snapshot(self._journal, operation_id)
        record = snapshot.record
        if record.state is not OperationState.COMMITTED:
            raise UndoNotEligibleError(
                UndoEligibilityCode.NOT_COMMITTED,
                "Only a committed operation can be reversed",
            )
        if record.media_file_id is None:
            raise UndoNotEligibleError(
                UndoEligibilityCode.NO_MEDIA_FILE,
                "This operation is not linked to a current cataloged media file",
            )
        if snapshot.reversed_by_operation_id is not None:
            raise UndoNotEligibleError(
                UndoEligibilityCode.ALREADY_REVERSED,
                "This operation already has a later reverse operation",
            )
        try:
            latest = self._journal.latest_relevant_for_media_file(record.media_file_id)
        except OperationJournalError as error:
            raise OperationHistoryError("could not verify current operation ordering") from error
        if latest is None or latest.record.id != record.id:
            raise UndoNotEligibleError(
                UndoEligibilityCode.SUPERSEDED,
                "A later journal operation represents this media file",
            )
        media_file = self._media_files.get_by_id(record.media_file_id)
        if media_file is None or _windows_key(media_file.current_path) != _windows_key(record.destination):
            raise UndoNotEligibleError(
                UndoEligibilityCode.CATALOG_PATH_CHANGED,
                "The catalog no longer identifies the operation destination as current",
            )

        policy = _historical_path_policy(record.source, record.destination)
        try:
            canonical_source, canonical_destination, identity = policy.validate_plan(
                record.destination,
                record.source,
                record.operation_type,
            )
        except FileSafetyError as error:
            raise _undo_validation_error(error) from error
        expected = (
            record.destination_size,
            record.destination_mtime_ns,
            record.destination_dev,
            record.destination_ino,
        )
        actual = (identity.size, identity.mtime_ns, identity.dev, identity.ino)
        if None in expected or actual != expected:
            raise UndoNotEligibleError(
                UndoEligibilityCode.SOURCE_CHANGED,
                "The current file no longer matches the verified operation destination",
            )
        if record.destination_sha256 is not None and _sha256(canonical_source) != record.destination_sha256:
            raise UndoNotEligibleError(
                UndoEligibilityCode.SOURCE_CHANGED,
                "The current file content no longer matches the verified operation destination",
            )
        destination_owner = self._media_files.get_by_path(canonical_destination)
        if destination_owner is not None and destination_owner.id != record.media_file_id:
            raise UndoNotEligibleError(
                UndoEligibilityCode.DESTINATION_EXISTS,
                "Another cataloged media file owns the recorded reverse destination",
            )
        destination_info = canonical_destination.parent.stat()
        same_volume = identity.dev == destination_info.st_dev
        preview = UndoPreview(
            preview_id=str(uuid4()),
            operation_id=record.id,
            media_file_id=record.media_file_id,
            source_path=str(canonical_source),
            destination_path=str(canonical_destination),
            operation=OperationKind(record.operation_type.value),
            same_volume=same_volume,
            file_size=identity.size,
            source_volume=_volume_label(canonical_source, identity.dev),
            destination_volume=_volume_label(canonical_destination, destination_info.st_dev),
            warnings=() if same_volume else ("CROSS_VOLUME",),
        )
        return _PreparedUndo(preview, policy, identity)

    def _execute(self, prepared: _PreparedUndo) -> UndoResult:
        try:
            current = self._evaluate(prepared.preview.operation_id)
        except UndoNotEligibleError as error:
            raise UndoPreviewStaleError(str(error)) from error
        if (
            current.preview.source_path != prepared.preview.source_path
            or current.preview.destination_path != prepared.preview.destination_path
            or current.source_identity != prepared.source_identity
        ):
            raise UndoPreviewStaleError("The exact reverse operation changed after preview")

        service = FileOperationService(
            current.policy,
            self._operation_store,
            engine=self._engine,
        )
        try:
            plan = service.create_reverse_plan(
                prepared.preview.operation_id,
                expected_source_identity=prepared.source_identity,
            )
        except (FileSafetyError, SourceChangedError) as error:
            raise UndoPreviewStaleError(str(error)) from error
        except Exception as error:
            raise UndoExecutionError(
                "DropSort could not durably create and validate the reverse-operation journal"
            ) from error
        try:
            record = service.execute(plan.operation_id)
        except DatabaseCommitError as error:
            raise UndoRecoveryRequiredError(
                plan.operation_id,
                "The reverse filesystem operation completed but catalog recovery is required",
            ) from error
        except Exception as error:
            try:
                record = self._operation_store.get(plan.operation_id)
            except Exception as state_error:
                raise UndoRecoveryRequiredError(
                    plan.operation_id,
                    "Reverse-operation state became unavailable after execution started",
                ) from state_error
            if record.state in {OperationState.FS_VERIFIED, OperationState.RECOVERY_REQUIRED}:
                raise UndoRecoveryRequiredError(
                    plan.operation_id,
                    "The reverse operation reached a recoverable filesystem state",
                ) from error
            raise UndoExecutionError(str(error)) from error
        if record.state is not OperationState.COMMITTED or record.strategy is None:
            raise UndoRecoveryRequiredError(
                record.id,
                "The reverse operation did not reach a committed state",
            )
        return UndoResult(
            original_operation_id=prepared.preview.operation_id,
            reverse_operation_id=record.id,
            media_file_id=prepared.preview.media_file_id,
            source_path=str(record.source),
            destination_path=str(record.destination),
            strategy=record.strategy,
        )


class RecoverFileOperation:
    """Expose only read inspection and explicitly requested safe reconciliation."""

    def __init__(
        self,
        journal: OperationJournalReadRepository,
        operation_store: OperationStore,
        *,
        execution_lock: threading.Lock | None = None,
    ) -> None:
        self._journal = journal
        self._operation_store = operation_store
        self._lock = execution_lock or threading.Lock()

    def inspect(self, operation_id: str) -> RecoveryAssessment:
        snapshot = _require_snapshot(self._journal, operation_id)
        try:
            policy = _historical_path_policy(snapshot.record.source, snapshot.record.destination)
        except UndoNotEligibleError:
            return RecoveryAssessment(
                operation_id=snapshot.record.id,
                state=OperationStatus(snapshot.record.state.value),
                disposition=RecoveryDisposition.UNSAFE_DESTINATION,
                source_exists=os.path.lexists(snapshot.record.source),
                destination_exists=os.path.lexists(snapshot.record.destination),
                action_available=False,
                explanation="The historical source or destination root is unavailable or unsafe",
            )
        service = RecoveryService(self._operation_store, policy)
        inspection = service.inspect(operation_id)
        disposition = _recovery_disposition(inspection.situation)
        return RecoveryAssessment(
            operation_id=inspection.operation_id,
            state=OperationStatus(inspection.state.value),
            disposition=disposition,
            source_exists=inspection.source_exists,
            destination_exists=inspection.destination_exists,
            action_available=inspection.can_reconcile,
            explanation=inspection.message,
        )

    def attempt(self, operation_id: str) -> RecoveryResult:
        with self._lock:
            assessment = self.inspect(operation_id)
            if not assessment.action_available:
                raise RecoveryActionUnavailableError(
                    "This filesystem state has no safe automatic recovery action"
                )
            snapshot = _require_snapshot(self._journal, operation_id)
            try:
                policy = _historical_path_policy(snapshot.record.source, snapshot.record.destination)
                record = RecoveryService(self._operation_store, policy).reconcile(operation_id)
            except Exception as error:
                raise OperationHistoryError("could not safely reconcile this operation") from error
            return RecoveryResult(record.id, OperationStatus(record.state.value))


def _require_snapshot(
    repository: OperationJournalReadRepository,
    operation_id: str,
) -> OperationJournalSnapshot:
    if not isinstance(operation_id, str) or not operation_id.strip():
        raise ValueError("operation_id must be non-empty text")
    try:
        snapshot = repository.get_operation(operation_id)
    except OperationJournalError as error:
        raise OperationHistoryError("could not read operation details") from error
    if snapshot is None:
        raise OperationHistoryNotFoundError(f"operation {operation_id} was not found")
    return snapshot


def _to_history_item(snapshot: OperationJournalSnapshot) -> OperationHistoryItem:
    record = snapshot.record
    try:
        created_at = datetime.fromisoformat(record.created_at)
        updated_at = datetime.fromisoformat(record.updated_at)
    except ValueError as error:
        raise OperationHistoryError("operation journal contains an invalid timestamp") from error
    return OperationHistoryItem(
        operation_id=record.id,
        operation=OperationKind(record.operation_type.value),
        state=OperationStatus(record.state.value),
        source_path=str(record.source),
        destination_path=str(record.destination),
        media_file_id=record.media_file_id,
        movie_title=snapshot.movie_title,
        created_at=created_at,
        updated_at=updated_at,
        reverses_operation_id=record.reverses_operation_id,
    )


def _historical_path_policy(source: Path, destination: Path) -> PathPolicy:
    roots: list[Path] = []
    keys: set[str] = set()
    for root in (source.parent, destination.parent):
        key = _windows_key(root)
        if key not in keys:
            roots.append(root)
            keys.add(key)
    try:
        return PathPolicy(tuple(roots))
    except (FileSafetyError, OSError, ValueError) as error:
        raise UndoNotEligibleError(
            UndoEligibilityCode.UNSAFE_PATH,
            "The exact historical source or destination root is unavailable or unsafe",
        ) from error


def _undo_validation_error(error: FileSafetyError) -> UndoNotEligibleError:
    if isinstance(error, CaseInsensitiveCollisionError):
        code = UndoEligibilityCode.CASE_COLLISION
    elif isinstance(error, DestinationExistsError):
        code = UndoEligibilityCode.DESTINATION_EXISTS
    elif isinstance(error, SameFileError):
        code = UndoEligibilityCode.SAME_FILE
    elif isinstance(error, SourceMissingError):
        code = UndoEligibilityCode.SOURCE_MISSING
    elif isinstance(error, LinkTraversalError):
        code = UndoEligibilityCode.LINK_TRAVERSAL
    elif isinstance(error, UnsafePathError):
        code = UndoEligibilityCode.UNSAFE_PATH
    else:
        code = UndoEligibilityCode.INVALID_OPERATION
    return UndoNotEligibleError(code, str(error))


def _recovery_disposition(situation: RecoverySituation) -> RecoveryDisposition:
    mapping = {
        RecoverySituation.NOT_REQUIRED: RecoveryDisposition.NOT_REQUIRED,
        RecoverySituation.NOT_ACTIONABLE: RecoveryDisposition.NOT_ACTIONABLE,
        RecoverySituation.SOURCE_ONLY_EXECUTING: RecoveryDisposition.SAFE_TO_MARK_FAILED,
        RecoverySituation.DESTINATION_ONLY_VERIFIED: RecoveryDisposition.VERIFIED_DESTINATION_ONLY,
        RecoverySituation.BOTH_EXIST: RecoveryDisposition.AMBIGUOUS_BOTH_EXIST,
        RecoverySituation.NEITHER_EXISTS: RecoveryDisposition.AMBIGUOUS_NEITHER_EXISTS,
        RecoverySituation.DESTINATION_UNSAFE_OR_CHANGED: RecoveryDisposition.UNSAFE_DESTINATION,
    }
    return mapping[situation]


def _windows_key(path: Path) -> str:
    return os.path.normpath(str(path.absolute())).casefold()


def _volume_label(path: Path, device: int) -> str:
    return path.anchor or f"device:{device}"


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        while chunk := handle.read(4 * 1024 * 1024):
            digest.update(chunk)
    return digest.hexdigest()
