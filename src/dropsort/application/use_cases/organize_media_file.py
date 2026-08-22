from __future__ import annotations

from dataclasses import dataclass
from collections import OrderedDict
import os
from pathlib import Path
import threading
from uuid import uuid4

from dropsort.application.dto.organization import (
    OrganizationOperation,
    OrganizationPreview,
    OrganizationResult,
)
from dropsort.application.errors import (
    OrganizationAlreadyConfirmedError,
    OrganizationExecutionError,
    OrganizationPreviewNotFoundError,
    OrganizationPreviewStaleError,
    OrganizationRecoveryRequiredError,
    OrganizationValidationError,
    OrganizationValidationCode,
)
from dropsort.core.operations import FileOperationService, OperationState, OperationType
from dropsort.core.operations.errors import DatabaseCommitError
from dropsort.core.operations.ports import OperationStore
from dropsort.core.file_engine.transfer import SafeTransferEngine
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


_INVALID_WINDOWS_FILENAME_CHARS = frozenset('<>:"/\\|?*')
_RESERVED_WINDOWS_NAMES = frozenset(
    {"CON", "PRN", "AUX", "NUL"}
    | {f"COM{number}" for number in range(1, 10)}
    | {f"LPT{number}" for number in range(1, 10)}
)


@dataclass(frozen=True, slots=True)
class _PreparedOrganization:
    preview: OrganizationPreview
    policy: PathPolicy
    source: Path
    destination: Path
    source_identity: SourceIdentity
    operation_type: OperationType


class OrganizeMediaFile:
    """Prepare read-only previews and execute only an exact explicit confirmation."""

    _MAX_PREPARED_PREVIEWS = 256

    def __init__(
        self,
        media_files: MediaFileRepository,
        operation_store: OperationStore,
        *,
        engine: SafeTransferEngine | None = None,
        execution_lock: threading.Lock | None = None,
    ) -> None:
        self._media_files = media_files
        self._operation_store = operation_store
        self._engine = engine
        self._lock = threading.Lock()
        self._execution_lock = execution_lock or threading.Lock()
        self._prepared: OrderedDict[str, _PreparedOrganization] = OrderedDict()
        self._consumed: OrderedDict[str, None] = OrderedDict()

    def prepare_preview(
        self,
        media_file_id: int,
        destination_root: Path,
        destination_filename: str,
    ) -> OrganizationPreview:
        if isinstance(media_file_id, bool) or not isinstance(media_file_id, int) or media_file_id <= 0:
            raise OrganizationValidationError("Media file identity is invalid")
        media_file = self._media_files.get_by_id(media_file_id)
        if media_file is None:
            raise OrganizationValidationError("The cataloged media file is unavailable")
        source = media_file.current_path
        try:
            filename = _validate_destination_filename(destination_filename, source.suffix)
            root = Path(destination_root)
            policy = PathPolicy((source.parent, root))
            destination = root / filename
            operation_type, presentation_operation = _operation_types(source, destination)
            canonical_source, canonical_destination, identity = policy.validate_plan(
                source,
                destination,
                operation_type,
            )
            destination_info = canonical_destination.parent.stat()
        except (FileSafetyError, OSError, ValueError) as error:
            raise _translate_validation_error(error) from error

        catalog_conflict = self._media_files.get_by_path(canonical_destination)
        if catalog_conflict is not None and catalog_conflict.id != media_file_id:
            raise OrganizationValidationError(
                "Another cataloged media file already owns the destination path",
                OrganizationValidationCode.DESTINATION_EXISTS,
            )

        same_volume = identity.dev == destination_info.st_dev
        preview_id = str(uuid4())
        preview = OrganizationPreview(
            preview_id=preview_id,
            media_file_id=media_file_id,
            source_path=str(canonical_source),
            destination_path=str(canonical_destination),
            operation=presentation_operation,
            same_volume=same_volume,
            file_size=identity.size,
            source_volume=_volume_label(canonical_source, identity.dev),
            destination_volume=_volume_label(canonical_destination, destination_info.st_dev),
            warnings=() if same_volume else ("CROSS_VOLUME",),
        )
        prepared = _PreparedOrganization(
            preview=preview,
            policy=policy,
            source=canonical_source,
            destination=canonical_destination,
            source_identity=identity,
            operation_type=operation_type,
        )
        with self._lock:
            self._prepared[preview_id] = prepared
            while len(self._prepared) > self._MAX_PREPARED_PREVIEWS:
                self._prepared.popitem(last=False)
        return preview

    def discard_preview(self, preview_id: str) -> None:
        """Forget an unconfirmed preview without journaling or touching the filesystem."""
        with self._lock:
            self._prepared.pop(preview_id, None)

    def confirm(self, preview_id: str) -> OrganizationResult:
        with self._lock:
            if preview_id in self._consumed:
                raise OrganizationAlreadyConfirmedError("This preview was already confirmed")
            prepared = self._prepared.pop(preview_id, None)
            if prepared is None:
                raise OrganizationPreviewNotFoundError("This preview is no longer available")
            self._consumed[preview_id] = None
            while len(self._consumed) > self._MAX_PREPARED_PREVIEWS:
                self._consumed.popitem(last=False)

        with self._execution_lock:
            return self._execute_prepared(prepared)

    def _execute_prepared(self, prepared: _PreparedOrganization) -> OrganizationResult:
        current = self._media_files.get_by_id(prepared.preview.media_file_id)
        if current is None or _windows_key(current.current_path) != _windows_key(prepared.source):
            raise OrganizationPreviewStaleError(
                "The cataloged media path changed after preview"
            )
        destination_owner = self._media_files.get_by_path(prepared.destination)
        if destination_owner is not None and destination_owner.id != prepared.preview.media_file_id:
            raise OrganizationPreviewStaleError(
                "Another cataloged media file claimed the destination after preview"
            )
        service = FileOperationService(prepared.policy, self._operation_store, engine=self._engine)
        planner = (
            service.plan_rename
            if prepared.operation_type is OperationType.RENAME
            else service.plan_move
        )
        try:
            plan = planner(
                prepared.source,
                prepared.destination,
                media_file_id=prepared.preview.media_file_id,
                expected_source_identity=prepared.source_identity,
            )
        except (FileSafetyError, SourceChangedError) as error:
            raise OrganizationPreviewStaleError(str(error)) from error
        except Exception as error:
            raise OrganizationExecutionError(
                "DropSort could not durably create and validate the operation journal"
            ) from error

        try:
            record = service.execute(plan.operation_id)
        except DatabaseCommitError as error:
            raise OrganizationRecoveryRequiredError(
                plan.operation_id,
                "The file operation completed, but the catalog update requires recovery",
            ) from error
        except Exception as error:
            try:
                record = self._operation_store.get(plan.operation_id)
            except Exception as state_error:
                raise OrganizationRecoveryRequiredError(
                    plan.operation_id,
                    "Operation state became unavailable after execution started",
                ) from state_error
            if record.state in {OperationState.FS_VERIFIED, OperationState.RECOVERY_REQUIRED}:
                raise OrganizationRecoveryRequiredError(
                    plan.operation_id,
                    "The operation reached a recoverable filesystem state",
                ) from error
            raise OrganizationExecutionError(str(error)) from error

        if record.state is not OperationState.COMMITTED or record.strategy is None:
            raise OrganizationRecoveryRequiredError(
                record.id,
                "The operation did not reach a committed state",
            )
        return OrganizationResult(
            operation_id=record.id,
            media_file_id=prepared.preview.media_file_id,
            source_path=str(record.source),
            destination_path=str(record.destination),
            strategy=record.strategy,
        )


def _operation_types(
    source: Path,
    destination: Path,
) -> tuple[OperationType, OrganizationOperation]:
    same_parent = _windows_key(source.parent) == _windows_key(destination.parent)
    same_name = source.name == destination.name
    if same_parent:
        return OperationType.RENAME, OrganizationOperation.RENAME
    if same_name:
        return OperationType.MOVE, OrganizationOperation.MOVE
    return OperationType.MOVE, OrganizationOperation.MOVE_AND_RENAME


def _validate_destination_filename(value: str, source_extension: str) -> str:
    if not isinstance(value, str) or not value or value != value.strip():
        raise ValueError("Destination filename must be non-empty and have no edge whitespace")
    if len(value) > 255 or any(ord(character) < 32 for character in value):
        raise ValueError("Destination filename is not valid on Windows")
    if any(character in _INVALID_WINDOWS_FILENAME_CHARS for character in value):
        raise ValueError("Destination filename contains a reserved Windows character")
    if value.endswith((".", " ")):
        raise ValueError("Destination filename cannot end in a dot or space")
    if value.split(".", 1)[0].rstrip(" .").upper() in _RESERVED_WINDOWS_NAMES:
        raise ValueError("Destination filename is a reserved Windows device name")
    if Path(value).suffix.casefold() != source_extension.casefold():
        raise ValueError("Destination filename must preserve the media extension")
    return value


def _windows_key(path: Path) -> str:
    return os.path.normpath(str(path.absolute())).casefold()


def _volume_label(path: Path, device: int) -> str:
    return path.anchor or f"device:{device}"


def _translate_validation_error(error: BaseException) -> OrganizationValidationError:
    if isinstance(error, CaseInsensitiveCollisionError):
        code = OrganizationValidationCode.CASE_COLLISION
    elif isinstance(error, DestinationExistsError):
        code = OrganizationValidationCode.DESTINATION_EXISTS
    elif isinstance(error, SameFileError):
        code = OrganizationValidationCode.SAME_FILE
    elif isinstance(error, SourceMissingError):
        code = OrganizationValidationCode.SOURCE_MISSING
    elif isinstance(error, LinkTraversalError):
        code = OrganizationValidationCode.LINK_TRAVERSAL
    elif isinstance(error, UnsafePathError):
        code = OrganizationValidationCode.UNSAFE_PATH
    else:
        code = OrganizationValidationCode.INVALID_REQUEST
    return OrganizationValidationError(str(error), code)
