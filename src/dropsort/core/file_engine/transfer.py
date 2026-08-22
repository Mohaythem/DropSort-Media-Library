from __future__ import annotations

import errno
import hashlib
import os
from pathlib import Path

from dropsort.core.operations.models import PreparedTransfer
from dropsort.core.safety.errors import DestinationExistsError, SourceChangedError
from dropsort.core.safety.path_policy import SourceIdentity


class SafeTransferEngine:
    """Low-level mechanics. Destination is verified before source removal."""

    _BUFFER_SIZE = 4 * 1024 * 1024

    def prepare(
        self,
        source: Path,
        destination: Path,
        expected: SourceIdentity,
        operation_id: str,
    ) -> PreparedTransfer:
        try:
            self._create_hardlink_destination(source, destination)
        except OSError as exc:
            if not self._should_copy_fallback(exc):
                raise
            return self._prepare_copy(source, destination, expected, operation_id)
        self._verify_hardlink(source, destination, expected)
        self._fsync_directory(destination.parent)
        return self._prepared(destination, "hardlink-unlink", None)

    def finalize_source_removal(
        self,
        source: Path,
        destination: Path,
        expected: SourceIdentity,
        prepared: PreparedTransfer,
    ) -> None:
        self._verify_source_identity(source, expected)
        self._verify_prepared_destination(destination, prepared)
        self._remove_source(source)

    def _prepare_copy(
        self,
        source: Path,
        destination: Path,
        expected: SourceIdentity,
        operation_id: str,
    ) -> PreparedTransfer:
        temp = destination.with_name(f".{destination.name}.dropsort-{operation_id}.tmp")
        if temp.exists():
            raise DestinationExistsError(f"Temporary destination already exists: {temp}")
        try:
            source_digest = self._copy_and_hash(source, temp)
            self._verify_source_identity(source, expected)
            self._finalize_no_overwrite(temp, destination)
            self._fsync_directory(destination.parent)
            destination_digest = self._sha256(destination)
            if destination_digest != source_digest:
                raise OSError("Destination SHA-256 verification failed")
            return self._prepared(destination, "copy-sha256-fsync-finalize-unlink", destination_digest)
        finally:
            if temp.exists():
                temp.unlink()

    def _copy_and_hash(self, source: Path, temp: Path) -> str:
        digest = hashlib.sha256()
        with source.open("rb") as src, temp.open("xb") as dst:
            while chunk := src.read(self._BUFFER_SIZE):
                digest.update(chunk)
                dst.write(chunk)
            dst.flush()
            os.fsync(dst.fileno())
        return digest.hexdigest()

    @staticmethod
    def _prepared(destination: Path, strategy: str, digest: str | None) -> PreparedTransfer:
        info = destination.stat()
        return PreparedTransfer(
            strategy=strategy,
            destination_size=info.st_size,
            destination_mtime_ns=info.st_mtime_ns,
            destination_dev=info.st_dev,
            destination_ino=info.st_ino,
            destination_sha256=digest,
        )

    @staticmethod
    def _verify_source_identity(source: Path, expected: SourceIdentity) -> None:
        current = source.stat()
        actual = (current.st_size, current.st_mtime_ns, current.st_dev, current.st_ino)
        planned = (expected.size, expected.mtime_ns, expected.dev, expected.ino)
        if actual != planned:
            raise SourceChangedError("Source identity changed during operation")

    @staticmethod
    def _verify_prepared_destination(destination: Path, prepared: PreparedTransfer) -> None:
        current = destination.stat()
        actual = (current.st_size, current.st_mtime_ns, current.st_dev, current.st_ino)
        verified = (
            prepared.destination_size,
            prepared.destination_mtime_ns,
            prepared.destination_dev,
            prepared.destination_ino,
        )
        if actual != verified:
            raise OSError("Destination changed after verification")

    @staticmethod
    def _create_hardlink_destination(source: Path, destination: Path) -> None:
        os.link(source, destination, follow_symlinks=False)

    @staticmethod
    def _remove_source(source: Path) -> None:
        source.unlink()

    @staticmethod
    def _verify_hardlink(source: Path, destination: Path, expected: SourceIdentity) -> None:
        if not destination.exists() or destination.stat().st_size != expected.size:
            raise OSError("Hard-link destination verification failed")
        if not os.path.samefile(source, destination):
            raise OSError("Hard-link destination is not the same file")

    @staticmethod
    def _finalize_no_overwrite(temp: Path, destination: Path) -> None:
        if os.name == "nt":
            os.rename(temp, destination)
            return
        os.link(temp, destination, follow_symlinks=False)
        temp.unlink()

    @classmethod
    def _sha256(cls, path: Path) -> str:
        digest = hashlib.sha256()
        with path.open("rb") as handle:
            while chunk := handle.read(cls._BUFFER_SIZE):
                digest.update(chunk)
        return digest.hexdigest()

    @staticmethod
    def _fsync_directory(directory: Path) -> None:
        if os.name == "nt":
            return
        fd = os.open(directory, os.O_RDONLY)
        try:
            os.fsync(fd)
        finally:
            os.close(fd)

    @staticmethod
    def _should_copy_fallback(exc: OSError) -> bool:
        fallback_errnos = {errno.EXDEV, errno.EOPNOTSUPP, errno.ENOTSUP, errno.ENOSYS, errno.EINVAL}
        return exc.errno in fallback_errnos or getattr(exc, "winerror", None) in {1, 17, 50}
