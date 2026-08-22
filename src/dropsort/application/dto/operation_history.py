from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime
from enum import StrEnum


MAX_OPERATION_HISTORY_PAGE_SIZE = 500


class OperationKind(StrEnum):
    MOVE = "MOVE"
    RENAME = "RENAME"


class OperationStatus(StrEnum):
    PLANNED = "PLANNED"
    VALIDATED = "VALIDATED"
    EXECUTING = "EXECUTING"
    FS_VERIFIED = "FS_VERIFIED"
    COMMITTED = "COMMITTED"
    FAILED = "FAILED"
    RECOVERY_REQUIRED = "RECOVERY_REQUIRED"


class UndoEligibilityCode(StrEnum):
    ELIGIBLE = "ELIGIBLE"
    NOT_COMMITTED = "NOT_COMMITTED"
    NO_MEDIA_FILE = "NO_MEDIA_FILE"
    ALREADY_REVERSED = "ALREADY_REVERSED"
    SUPERSEDED = "SUPERSEDED"
    CATALOG_PATH_CHANGED = "CATALOG_PATH_CHANGED"
    SOURCE_MISSING = "SOURCE_MISSING"
    SOURCE_CHANGED = "SOURCE_CHANGED"
    DESTINATION_EXISTS = "DESTINATION_EXISTS"
    CASE_COLLISION = "CASE_COLLISION"
    SAME_FILE = "SAME_FILE"
    LINK_TRAVERSAL = "LINK_TRAVERSAL"
    UNSAFE_PATH = "UNSAFE_PATH"
    INVALID_OPERATION = "INVALID_OPERATION"


class RecoveryDisposition(StrEnum):
    NOT_REQUIRED = "NOT_REQUIRED"
    NOT_ACTIONABLE = "NOT_ACTIONABLE"
    SAFE_TO_MARK_FAILED = "SAFE_TO_MARK_FAILED"
    VERIFIED_DESTINATION_ONLY = "VERIFIED_DESTINATION_ONLY"
    AMBIGUOUS_BOTH_EXIST = "AMBIGUOUS_BOTH_EXIST"
    AMBIGUOUS_NEITHER_EXISTS = "AMBIGUOUS_NEITHER_EXISTS"
    UNSAFE_DESTINATION = "UNSAFE_DESTINATION"


@dataclass(frozen=True, slots=True)
class OperationHistoryQuery:
    limit: int = MAX_OPERATION_HISTORY_PAGE_SIZE
    offset: int = 0

    def __post_init__(self) -> None:
        if (
            isinstance(self.limit, bool)
            or not isinstance(self.limit, int)
            or not 1 <= self.limit <= MAX_OPERATION_HISTORY_PAGE_SIZE
        ):
            raise ValueError(
                f"limit must be an integer from 1 through {MAX_OPERATION_HISTORY_PAGE_SIZE}"
            )
        if isinstance(self.offset, bool) or not isinstance(self.offset, int) or self.offset < 0:
            raise ValueError("offset must be a non-negative integer")


@dataclass(frozen=True, slots=True)
class OperationHistoryItem:
    operation_id: str
    operation: OperationKind
    state: OperationStatus
    source_path: str
    destination_path: str
    media_file_id: int | None
    movie_title: str | None
    created_at: datetime
    updated_at: datetime
    reverses_operation_id: str | None

    def __post_init__(self) -> None:
        _required_text(self.operation_id, "operation_id")
        if not isinstance(self.operation, OperationKind):
            raise ValueError("operation must be OperationKind")
        if not isinstance(self.state, OperationStatus):
            raise ValueError("state must be OperationStatus")
        _required_text(self.source_path, "source_path")
        _required_text(self.destination_path, "destination_path")
        if self.media_file_id is not None:
            _positive_id(self.media_file_id, "media_file_id")
        if self.movie_title is not None:
            _required_text(self.movie_title, "movie_title")
        _aware_datetime(self.created_at, "created_at")
        _aware_datetime(self.updated_at, "updated_at")
        if self.reverses_operation_id is not None:
            _required_text(self.reverses_operation_id, "reverses_operation_id")


@dataclass(frozen=True, slots=True)
class OperationDetails:
    history: OperationHistoryItem
    current_catalog_path: str | None
    strategy: str | None
    error_code: str | None
    error_message: str | None
    reversed_by_operation_id: str | None
    source_size: int | None
    destination_size: int | None
    destination_sha256: str | None

    def __post_init__(self) -> None:
        if not isinstance(self.history, OperationHistoryItem):
            raise ValueError("history must be OperationHistoryItem")
        for value, name in (
            (self.current_catalog_path, "current_catalog_path"),
            (self.strategy, "strategy"),
            (self.error_code, "error_code"),
            (self.error_message, "error_message"),
            (self.reversed_by_operation_id, "reversed_by_operation_id"),
            (self.destination_sha256, "destination_sha256"),
        ):
            if value is not None:
                _required_text(value, name)
        for value, name in (
            (self.source_size, "source_size"),
            (self.destination_size, "destination_size"),
        ):
            if value is not None and (
                isinstance(value, bool) or not isinstance(value, int) or value < 0
            ):
                raise ValueError(f"{name} must be a non-negative integer when present")


@dataclass(frozen=True, slots=True)
class UndoEligibility:
    eligible: bool
    code: UndoEligibilityCode
    explanation: str

    def __post_init__(self) -> None:
        if not isinstance(self.eligible, bool):
            raise ValueError("eligible must be bool")
        if not isinstance(self.code, UndoEligibilityCode):
            raise ValueError("code must be UndoEligibilityCode")
        _required_text(self.explanation, "explanation")
        if self.eligible != (self.code is UndoEligibilityCode.ELIGIBLE):
            raise ValueError("eligible must agree with the eligibility code")


@dataclass(frozen=True, slots=True)
class UndoPreview:
    preview_id: str
    operation_id: str
    media_file_id: int
    source_path: str
    destination_path: str
    operation: OperationKind
    same_volume: bool
    file_size: int
    source_volume: str
    destination_volume: str
    warnings: tuple[str, ...]

    def __post_init__(self) -> None:
        _required_text(self.preview_id, "preview_id")
        _required_text(self.operation_id, "operation_id")
        _positive_id(self.media_file_id, "media_file_id")
        _required_text(self.source_path, "source_path")
        _required_text(self.destination_path, "destination_path")
        if not isinstance(self.operation, OperationKind):
            raise ValueError("operation must be OperationKind")
        if not isinstance(self.same_volume, bool):
            raise ValueError("same_volume must be bool")
        if isinstance(self.file_size, bool) or not isinstance(self.file_size, int) or self.file_size < 0:
            raise ValueError("file_size must be a non-negative integer")
        if not isinstance(self.source_volume, str) or not isinstance(self.destination_volume, str):
            raise ValueError("volume labels must be strings")
        if not isinstance(self.warnings, tuple) or any(
            not isinstance(value, str) or not value for value in self.warnings
        ):
            raise ValueError("warnings must be stable non-empty identifiers")


@dataclass(frozen=True, slots=True)
class UndoResult:
    original_operation_id: str
    reverse_operation_id: str
    media_file_id: int
    source_path: str
    destination_path: str
    strategy: str

    def __post_init__(self) -> None:
        for value, name in (
            (self.original_operation_id, "original_operation_id"),
            (self.reverse_operation_id, "reverse_operation_id"),
            (self.source_path, "source_path"),
            (self.destination_path, "destination_path"),
            (self.strategy, "strategy"),
        ):
            _required_text(value, name)
        _positive_id(self.media_file_id, "media_file_id")


@dataclass(frozen=True, slots=True)
class RecoveryAssessment:
    operation_id: str
    state: OperationStatus
    disposition: RecoveryDisposition
    source_exists: bool
    destination_exists: bool
    action_available: bool
    explanation: str

    def __post_init__(self) -> None:
        _required_text(self.operation_id, "operation_id")
        if not isinstance(self.state, OperationStatus):
            raise ValueError("state must be OperationStatus")
        if not isinstance(self.disposition, RecoveryDisposition):
            raise ValueError("disposition must be RecoveryDisposition")
        if not all(
            isinstance(value, bool)
            for value in (self.source_exists, self.destination_exists, self.action_available)
        ):
            raise ValueError("recovery flags must be bool")
        _required_text(self.explanation, "explanation")
        actionable = {
            RecoveryDisposition.SAFE_TO_MARK_FAILED,
            RecoveryDisposition.VERIFIED_DESTINATION_ONLY,
        }
        if self.action_available != (self.disposition in actionable):
            raise ValueError("action_available must agree with disposition")


@dataclass(frozen=True, slots=True)
class RecoveryResult:
    operation_id: str
    state: OperationStatus

    def __post_init__(self) -> None:
        _required_text(self.operation_id, "operation_id")
        if not isinstance(self.state, OperationStatus):
            raise ValueError("state must be OperationStatus")


def _required_text(value: str, field_name: str) -> None:
    if not isinstance(value, str) or not value.strip():
        raise ValueError(f"{field_name} must be non-empty text")


def _positive_id(value: int, field_name: str) -> None:
    if isinstance(value, bool) or not isinstance(value, int) or value <= 0:
        raise ValueError(f"{field_name} must be a positive integer")


def _aware_datetime(value: datetime, field_name: str) -> None:
    if not isinstance(value, datetime) or value.tzinfo is None:
        raise ValueError(f"{field_name} must be a timezone-aware datetime")
