from __future__ import annotations

from dataclasses import dataclass
from enum import StrEnum
from pathlib import Path


class AvailabilityInspectionStatus(StrEnum):
    PRESENT = "PRESENT"
    MISSING = "MISSING"
    ERROR = "ERROR"


@dataclass(frozen=True, slots=True)
class MediaFileIdentity:
    size: int
    mtime_ns: int
    ctime_ns: int
    dev: int
    ino: int


@dataclass(frozen=True, slots=True)
class AvailabilityInspection:
    path: Path
    status: AvailabilityInspectionStatus
    identity: MediaFileIdentity | None = None
    error_code: str | None = None

    @property
    def size(self) -> int | None:
        return None if self.identity is None else self.identity.size
