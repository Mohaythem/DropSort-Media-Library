from __future__ import annotations

from dataclasses import dataclass
from enum import StrEnum
from pathlib import Path


class OperationType(StrEnum):
    MOVE = "MOVE"
    RENAME = "RENAME"


class OperationState(StrEnum):
    PLANNED = "PLANNED"
    VALIDATED = "VALIDATED"
    EXECUTING = "EXECUTING"
    FS_VERIFIED = "FS_VERIFIED"
    COMMITTED = "COMMITTED"
    FAILED = "FAILED"
    RECOVERY_REQUIRED = "RECOVERY_REQUIRED"


class RecoverySituation(StrEnum):
    NOT_REQUIRED = "NOT_REQUIRED"
    NOT_ACTIONABLE = "NOT_ACTIONABLE"
    SOURCE_ONLY_EXECUTING = "SOURCE_ONLY_EXECUTING"
    DESTINATION_ONLY_VERIFIED = "DESTINATION_ONLY_VERIFIED"
    BOTH_EXIST = "BOTH_EXIST"
    NEITHER_EXISTS = "NEITHER_EXISTS"
    DESTINATION_UNSAFE_OR_CHANGED = "DESTINATION_UNSAFE_OR_CHANGED"


@dataclass(frozen=True, slots=True)
class FileOperationPlan:
    operation_id: str
    operation_type: OperationType
    source: Path
    destination: Path
    media_file_id: int | None = None
    reverses_operation_id: str | None = None


@dataclass(frozen=True, slots=True)
class OperationUpdate:
    source_size: int | None = None
    source_mtime_ns: int | None = None
    source_dev: int | None = None
    source_ino: int | None = None
    destination_size: int | None = None
    destination_mtime_ns: int | None = None
    destination_dev: int | None = None
    destination_ino: int | None = None
    destination_sha256: str | None = None
    strategy: str | None = None
    error_code: str | None = None
    error_message: str | None = None


@dataclass(frozen=True, slots=True)
class FileOperationRecord:
    id: str
    operation_type: OperationType
    source: Path
    destination: Path
    state: OperationState
    media_file_id: int | None
    reverses_operation_id: str | None
    source_size: int | None
    source_mtime_ns: int | None
    source_dev: int | None
    source_ino: int | None
    destination_size: int | None
    destination_mtime_ns: int | None
    destination_dev: int | None
    destination_ino: int | None
    destination_sha256: str | None
    strategy: str | None
    error_code: str | None
    error_message: str | None
    created_at: str
    updated_at: str


@dataclass(frozen=True, slots=True)
class PreparedTransfer:
    strategy: str
    destination_size: int
    destination_mtime_ns: int
    destination_dev: int
    destination_ino: int
    destination_sha256: str | None


@dataclass(frozen=True, slots=True)
class RecoveryInspection:
    operation_id: str
    state: OperationState
    situation: RecoverySituation
    source_exists: bool
    destination_exists: bool
    can_reconcile: bool
    message: str
