from __future__ import annotations

import os
from pathlib import Path
import stat

from dropsort.library.availability.models import (
    AvailabilityInspection,
    AvailabilityInspectionStatus,
    MediaFileIdentity,
)


class NoFollowMediaFileInspector:
    """Inspect one catalog path without resolving or following link/reparse entries."""

    def inspect(self, path: Path) -> AvailabilityInspection:
        if not isinstance(path, Path) or not path.is_absolute():
            raise ValueError("path must be an absolute Path")
        absolute = path.absolute()
        try:
            information = _lstat_without_link_components(absolute)
        except FileNotFoundError:
            return AvailabilityInspection(absolute, AvailabilityInspectionStatus.MISSING)
        except OSError:
            return AvailabilityInspection(
                absolute,
                AvailabilityInspectionStatus.ERROR,
                error_code="INSPECTION_FAILED",
            )
        if _is_link_or_reparse(information):
            return AvailabilityInspection(
                absolute,
                AvailabilityInspectionStatus.MISSING,
                error_code="UNSAFE_LINK",
            )
        if not stat.S_ISREG(information.st_mode):
            return AvailabilityInspection(
                absolute,
                AvailabilityInspectionStatus.MISSING,
                error_code="NOT_REGULAR_FILE",
            )
        return AvailabilityInspection(
            absolute,
            AvailabilityInspectionStatus.PRESENT,
            MediaFileIdentity(
                size=information.st_size,
                mtime_ns=information.st_mtime_ns,
                ctime_ns=information.st_ctime_ns,
                dev=information.st_dev,
                ino=information.st_ino,
            ),
        )


def _lstat_without_link_components(path: Path) -> os.stat_result:
    current = Path(path.anchor)
    parts = path.parts[1:] if path.anchor else path.parts
    information: os.stat_result | None = None
    for part in parts:
        current = current / part
        information = os.lstat(current)
        if _is_link_or_reparse(information):
            return information
    if information is None:
        information = os.lstat(path)
    return information


def _is_link_or_reparse(information: os.stat_result) -> bool:
    attributes = getattr(information, "st_file_attributes", 0) or 0
    marker = getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0)
    return stat.S_ISLNK(information.st_mode) or bool(marker and attributes & marker)
