from __future__ import annotations

from dataclasses import dataclass
import os
from pathlib import Path
import stat

from dropsort.core.operations.models import FileOperationRecord, OperationType
from dropsort.core.safety.errors import (
    CaseInsensitiveCollisionError,
    DestinationExistsError,
    InvalidRenameError,
    LinkTraversalError,
    SameFileError,
    SourceChangedError,
    SourceMissingError,
    UnsafePathError,
)


@dataclass(frozen=True, slots=True)
class SourceIdentity:
    size: int
    mtime_ns: int
    dev: int
    ino: int


class PathPolicy:
    """Windows-oriented path safety rules, testable on other hosts."""

    def __init__(self, approved_roots: list[Path] | tuple[Path, ...]) -> None:
        if not approved_roots:
            raise ValueError("At least one approved root is required")
        roots: list[Path] = []
        for root in approved_roots:
            raw = self._absolute(root)
            self._assert_no_link_components(raw)
            resolved = raw.resolve(strict=True)
            if not resolved.is_dir():
                raise ValueError(f"Approved root is not a directory: {root}")
            roots.append(resolved)
        self._roots = tuple(roots)

    @property
    def approved_roots(self) -> tuple[Path, ...]:
        return self._roots

    def validate_plan(
        self,
        source: Path,
        destination: Path,
        operation_type: OperationType,
    ) -> tuple[Path, Path, SourceIdentity]:
        source_raw = self._absolute(source)
        destination_raw = self._absolute(destination)
        self._assert_no_link_components(source_raw)
        self._assert_no_link_components(destination_raw.parent)

        if not source_raw.exists() or not source_raw.is_file():
            raise SourceMissingError(str(source))

        source_resolved = source_raw.resolve(strict=True)
        destination_resolved = destination_raw.resolve(strict=False)
        self._assert_approved(source_resolved)
        self._assert_approved(destination_resolved)

        if self._windows_key(source_resolved) == self._windows_key(destination_resolved):
            raise SameFileError(f"Source and destination are the same Windows path: {source}")

        if operation_type is OperationType.RENAME:
            if self._windows_key(source_resolved.parent) != self._windows_key(destination_resolved.parent):
                raise InvalidRenameError("RENAME must stay in the same directory; use MOVE otherwise")

        if not destination_resolved.parent.exists() or not destination_resolved.parent.is_dir():
            raise UnsafePathError(f"Destination parent is unavailable: {destination_resolved.parent}")

        if destination_raw.exists():
            try:
                if os.path.samefile(source_raw, destination_raw):
                    raise SameFileError(f"Source and destination identify the same file: {source}")
            except FileNotFoundError:
                pass

        self._assert_no_casefold_collision(destination_raw)
        if destination_raw.exists():
            raise DestinationExistsError(str(destination_raw))
        return source_resolved, destination_resolved, self.identity(source_resolved)

    def validate_existing_recovery_path(self, path: Path) -> Path:
        """Validate an existing recovery target without requiring the old source."""
        raw = self._absolute(path)
        self._assert_no_link_components(raw)
        if not raw.exists() or not raw.is_file():
            raise UnsafePathError(f"Recovery path is unavailable: {raw}")
        resolved = raw.resolve(strict=True)
        self._assert_approved(resolved)
        return resolved

    def revalidate_record(self, record: FileOperationRecord) -> SourceIdentity:
        source, destination, identity = self.validate_plan(
            record.source, record.destination, record.operation_type
        )
        if source != record.source.resolve(strict=True):
            raise SourceChangedError("Source canonical path changed")
        if destination != record.destination.resolve(strict=False):
            raise UnsafePathError("Destination canonical path changed")
        expected = (record.source_size, record.source_mtime_ns, record.source_dev, record.source_ino)
        actual = (identity.size, identity.mtime_ns, identity.dev, identity.ino)
        if None not in expected and actual != expected:
            raise SourceChangedError("Source identity changed after planning")
        return identity

    @staticmethod
    def identity(path: Path) -> SourceIdentity:
        info = path.stat()
        return SourceIdentity(
            size=info.st_size,
            mtime_ns=info.st_mtime_ns,
            dev=info.st_dev,
            ino=info.st_ino,
        )

    def _assert_approved(self, candidate: Path) -> None:
        key = self._windows_key(candidate)
        for root in self._roots:
            root_key = self._windows_key(root)
            try:
                common = os.path.commonpath((root_key, key))
            except ValueError:
                continue
            if common == root_key:
                return
        raise UnsafePathError(f"Path is outside approved roots: {candidate}")

    @staticmethod
    def _absolute(path: Path) -> Path:
        return Path(os.path.abspath(os.path.expanduser(str(path))))

    @staticmethod
    def _windows_key(path: Path) -> str:
        return os.path.normpath(str(path)).casefold()

    def _assert_no_casefold_collision(self, destination: Path) -> None:
        desired = destination.name.casefold()
        with os.scandir(destination.parent) as entries:
            for entry in entries:
                if entry.name.casefold() == desired and entry.name != destination.name:
                    raise CaseInsensitiveCollisionError(
                        f"Windows case-insensitive destination collision: {entry.name}"
                    )

    def _assert_no_link_components(self, path: Path) -> None:
        current = Path(path.anchor) if path.anchor else Path()
        for part in path.parts[1:] if path.anchor else path.parts:
            current = current / part
            if not current.exists():
                break
            info = os.lstat(current)
            if stat.S_ISLNK(info.st_mode) or self._is_windows_reparse(info):
                raise LinkTraversalError(f"Link/reparse traversal is not allowed: {current}")

    @staticmethod
    def _is_windows_reparse(info: os.stat_result) -> bool:
        attrs = getattr(info, "st_file_attributes", 0)
        marker = getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0)
        return bool(marker and attrs & marker)
