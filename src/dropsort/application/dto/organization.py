from __future__ import annotations

from dataclasses import dataclass
from enum import StrEnum


class OrganizationOperation(StrEnum):
    MOVE = "MOVE"
    RENAME = "RENAME"
    MOVE_AND_RENAME = "MOVE_AND_RENAME"


@dataclass(frozen=True, slots=True)
class OrganizationPreview:
    """Presentation-safe snapshot of one exact, read-only organization proposal."""

    preview_id: str
    media_file_id: int
    source_path: str
    destination_path: str
    operation: OrganizationOperation
    same_volume: bool
    file_size: int
    source_volume: str
    destination_volume: str
    warnings: tuple[str, ...]

    def __post_init__(self) -> None:
        _required_text(self.preview_id, "preview_id")
        _positive_id(self.media_file_id, "media_file_id")
        _required_text(self.source_path, "source_path")
        _required_text(self.destination_path, "destination_path")
        if not isinstance(self.operation, OrganizationOperation):
            raise ValueError("operation must be OrganizationOperation")
        if not isinstance(self.same_volume, bool):
            raise ValueError("same_volume must be bool")
        if isinstance(self.file_size, bool) or not isinstance(self.file_size, int) or self.file_size < 0:
            raise ValueError("file_size must be a non-negative integer")
        if not isinstance(self.source_volume, str) or not isinstance(self.destination_volume, str):
            raise ValueError("volume labels must be strings")
        if not isinstance(self.warnings, tuple) or any(
            not isinstance(warning, str) or not warning for warning in self.warnings
        ):
            raise ValueError("warnings must contain stable non-empty identifiers")


@dataclass(frozen=True, slots=True)
class OrganizationResult:
    """Successful committed organization result; never represents partial success."""

    operation_id: str
    media_file_id: int
    source_path: str
    destination_path: str
    strategy: str

    def __post_init__(self) -> None:
        _required_text(self.operation_id, "operation_id")
        _positive_id(self.media_file_id, "media_file_id")
        _required_text(self.source_path, "source_path")
        _required_text(self.destination_path, "destination_path")
        _required_text(self.strategy, "strategy")


def _required_text(value: str, field_name: str) -> None:
    if not isinstance(value, str) or not value.strip():
        raise ValueError(f"{field_name} must be non-empty text")


def _positive_id(value: int, field_name: str) -> None:
    if isinstance(value, bool) or not isinstance(value, int) or value <= 0:
        raise ValueError(f"{field_name} must be a positive integer")

