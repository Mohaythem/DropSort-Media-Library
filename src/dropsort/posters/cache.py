from __future__ import annotations

from hashlib import sha256
import os
from pathlib import Path
import stat
from threading import RLock
import time
from uuid import uuid4

from dropsort.posters.contracts import PosterAsset, PosterRequest
from dropsort.posters.errors import PosterResponseError


DEFAULT_MAXIMUM_CACHE_BYTES = 256 * 1024 * 1024
_SUFFIXES = {"jpeg": ".jpg", "png": ".png"}
_ACCESS_TICK_NS = 1_000_000


def poster_cache_key(request: PosterRequest) -> str:
    identity = f"{request.provider}\0{request.reference}".encode("utf-8")
    return sha256(identity).hexdigest()


class PosterAssetCache:
    """Bounded application-owned cache; never receives user media paths."""

    def __init__(
        self,
        root: Path,
        *,
        maximum_bytes: int = DEFAULT_MAXIMUM_CACHE_BYTES,
    ) -> None:
        if not isinstance(root, Path) or not root.is_absolute():
            raise ValueError("cache root must be an absolute Path")
        if isinstance(maximum_bytes, bool) or not isinstance(maximum_bytes, int) or maximum_bytes <= 0:
            raise ValueError("maximum_bytes must be a positive integer")
        if root.exists():
            info = os.lstat(root)
            attributes = getattr(info, "st_file_attributes", 0) or 0
            reparse = getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0)
            if stat.S_ISLNK(info.st_mode) or (reparse and attributes & reparse):
                raise ValueError("cache root must not be a link or reparse point")
            if not stat.S_ISDIR(info.st_mode):
                raise ValueError("cache root must be a directory")
        self._root = root
        self._maximum_bytes = maximum_bytes
        self._lock = RLock()
        self._prepared = False
        self._last_access_ns = 0

    @property
    def root(self) -> Path:
        return self._root

    def get(self, request: PosterRequest) -> PosterAsset | None:
        key = poster_cache_key(request)
        with self._lock:
            self._prepare_root()
            for image_format, suffix in _SUFFIXES.items():
                path = self._root / f"{key}{suffix}"
                if not _is_regular_unlinked_file(path):
                    continue
                try:
                    asset = PosterAsset(image_format, path.read_bytes())
                except (OSError, PosterResponseError, ValueError):
                    path.unlink(missing_ok=True)
                    continue
                self._touch(path)
                return asset
        return None

    def put(self, request: PosterRequest, asset: PosterAsset) -> Path:
        key = poster_cache_key(request)
        final_path = self._root / f"{key}{_SUFFIXES[asset.image_format]}"
        temporary = self._root / f".{key}.{uuid4().hex}.tmp"
        with self._lock:
            self._prepare_root()
            try:
                with temporary.open("xb") as stream:
                    stream.write(asset.content)
                    stream.flush()
                    os.fsync(stream.fileno())
                os.replace(temporary, final_path)
                self._touch(final_path)
            finally:
                temporary.unlink(missing_ok=True)
            self._evict()
        return final_path

    def clear(self) -> int:
        """Remove only regular cache assets directly inside the validated cache root."""
        with self._lock:
            self._prepare_root()
            _validate_cache_root(self._root)
            root_identity = _filesystem_identity(self._root)
            removed = 0
            for path in tuple(self._root.iterdir()):
                _require_same_cache_root(self._root, root_identity)
                if (
                    _is_regular_unlinked_file(path)
                    and path.suffix.casefold() in {".jpg", ".png", ".tmp"}
                ):
                    _require_same_cache_root(self._root, root_identity)
                    path.unlink(missing_ok=True)
                    removed += 1
            return removed

    def _touch(self, path: Path) -> None:
        timestamp = max(time.time_ns(), self._last_access_ns + _ACCESS_TICK_NS)
        os.utime(path, ns=(timestamp, timestamp))
        self._last_access_ns = timestamp

    def _prepare_root(self) -> None:
        if self._prepared:
            _validate_cache_root(self._root)
            return
        if self._root.exists():
            _validate_cache_root(self._root)
        self._root.mkdir(parents=True, exist_ok=True)
        for partial in self._root.glob("*.tmp"):
            if _is_regular_unlinked_file(partial):
                partial.unlink(missing_ok=True)
        for partial in self._root.glob(".*.tmp"):
            if _is_regular_unlinked_file(partial):
                partial.unlink(missing_ok=True)
        self._prepared = True

    def _evict(self) -> None:
        files = [
            path
            for path in self._root.iterdir()
            if _is_regular_unlinked_file(path)
            and path.suffix.casefold() in {".jpg", ".png"}
        ]
        sizes = {path: path.stat().st_size for path in files}
        total = sum(sizes.values())
        ordered = sorted(files, key=lambda path: (path.stat().st_mtime_ns, path.name))
        for path in ordered:
            if total <= self._maximum_bytes:
                break
            size = sizes[path]
            path.unlink(missing_ok=True)
            total -= size


def _validate_cache_root(root: Path) -> None:
    info = os.lstat(root)
    attributes = getattr(info, "st_file_attributes", 0) or 0
    reparse = getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0)
    if stat.S_ISLNK(info.st_mode) or (reparse and attributes & reparse):
        raise ValueError("cache root must not be a link or reparse point")
    if not stat.S_ISDIR(info.st_mode):
        raise ValueError("cache root must be a directory")


def _is_regular_unlinked_file(path: Path) -> bool:
    try:
        info = os.lstat(path)
    except FileNotFoundError:
        return False
    attributes = getattr(info, "st_file_attributes", 0) or 0
    reparse = getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0)
    if stat.S_ISLNK(info.st_mode) or (reparse and attributes & reparse):
        path.unlink(missing_ok=True)
        return False
    return stat.S_ISREG(info.st_mode)


def _filesystem_identity(path: Path) -> tuple[int, int, int]:
    info = os.lstat(path)
    return (info.st_dev, info.st_ino, info.st_mode)


def _require_same_cache_root(
    root: Path,
    expected: tuple[int, int, int],
) -> None:
    _validate_cache_root(root)
    if _filesystem_identity(root) != expected:
        raise ValueError("cache root changed during cleanup")
