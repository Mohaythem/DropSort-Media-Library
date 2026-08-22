from __future__ import annotations

from collections.abc import Callable
from dataclasses import replace
from pathlib import Path
import os
import stat

from dropsort.media.discovery.contracts import (
    DiscoveryCancellation,
    DiscoveryProgressCallback,
)
from dropsort.media.discovery.errors import DiscoveryCancelled, DiscoveryRootError
from dropsort.media.discovery.models import (
    DiscoveryClassification,
    DiscoveryErrorCode,
    DiscoveryIssue,
    DiscoveryProgress,
    DiscoveredMedia,
)
from dropsort.media.parser import (
    MediaType,
    ParsedMedia,
    is_supported_video_filename,
    parse_media_filename,
)


Parser = Callable[[str], ParsedMedia]


class ReadOnlyMediaScanner:
    """Discover media without following links or mutating filesystem state."""

    def __init__(
        self,
        *,
        parser: Parser = parse_media_filename,
        progress_interval: int = 32,
    ) -> None:
        if (
            isinstance(progress_interval, bool)
            or not isinstance(progress_interval, int)
            or progress_interval <= 0
        ):
            raise ValueError("progress_interval must be a positive integer")
        self._parser = parser
        self._progress_interval = progress_interval

    def scan(
        self,
        root: Path,
        *,
        recursive: bool = True,
        progress: DiscoveryProgressCallback | None = None,
        cancellation: DiscoveryCancellation | None = None,
    ) -> tuple[DiscoveredMedia, ...]:
        if not isinstance(root, Path):
            raise ValueError("root must be a Path")
        if not isinstance(recursive, bool):
            raise ValueError("recursive must be a boolean")
        tracker = _ProgressTracker(progress, self._progress_interval)
        tracker.emit(force=True)
        _raise_if_cancelled(cancellation, tracker)
        absolute_root = Path(os.path.abspath(root))
        root_info = _validate_root(absolute_root)
        root_identity = _identity(root_info)
        visited = {root_identity}
        pending = [(absolute_root, root_identity)]
        discoveries: list[DiscoveredMedia] = []

        while pending:
            _raise_if_cancelled(cancellation, tracker)
            directory, expected_identity = pending.pop()
            tracker.advance(directories_seen=1)
            try:
                current_info = _scan_directory_stat(directory)
            except OSError as error:
                if directory == absolute_root:
                    raise _root_runtime_error(absolute_root, error) from error
                discoveries.append(DiscoveredMedia.error(directory, _stat_issue(error)))
                tracker.advance(errors=1)
                continue
            if stat.S_ISLNK(current_info.st_mode) or _is_reparse(current_info):
                if directory == absolute_root:
                    raise DiscoveryRootError(
                        absolute_root,
                        DiscoveryErrorCode.ROOT_LINK_NOT_ALLOWED,
                        "scan root became a link or reparse point",
                    )
                discoveries.append(
                    DiscoveredMedia.error(
                        directory,
                        DiscoveryIssue(
                            DiscoveryErrorCode.LINK_SKIPPED,
                            "link or reparse directory was not followed",
                        ),
                    )
                )
                tracker.advance(errors=1)
                continue
            if (
                not stat.S_ISDIR(current_info.st_mode)
                or _identity(current_info) != expected_identity
            ):
                if directory == absolute_root:
                    raise DiscoveryRootError(
                        absolute_root,
                        DiscoveryErrorCode.STAT_FAILED,
                        "scan root identity changed before enumeration",
                    )
                discoveries.append(
                    DiscoveredMedia.error(
                        directory,
                        DiscoveryIssue(
                            DiscoveryErrorCode.STAT_FAILED,
                            "directory identity changed before enumeration",
                        ),
                    )
                )
                tracker.advance(errors=1)
                continue
            try:
                entries = _read_entries(directory, cancellation, tracker)
            except OSError as error:
                if directory == absolute_root:
                    raise _root_runtime_error(absolute_root, error) from error
                discoveries.append(
                    DiscoveredMedia.error(directory, _directory_issue(error))
                )
                tracker.advance(errors=1)
                continue

            child_directories: list[tuple[Path, tuple[int, int]]] = []
            for entry in entries:
                _raise_if_cancelled(cancellation, tracker)
                path = Path(os.path.abspath(entry.path))
                try:
                    info = _entry_stat(entry)
                except OSError as error:
                    discoveries.append(DiscoveredMedia.error(path, _stat_issue(error)))
                    tracker.advance(entries_seen=1, errors=1)
                    continue

                if stat.S_ISLNK(info.st_mode) or _is_reparse(info):
                    discoveries.append(
                        DiscoveredMedia.error(
                            path,
                            DiscoveryIssue(
                                DiscoveryErrorCode.LINK_SKIPPED,
                                "link or reparse entry was not followed",
                            ),
                        )
                    )
                    tracker.advance(entries_seen=1, errors=1)
                elif stat.S_ISDIR(info.st_mode):
                    if not recursive:
                        tracker.advance(entries_seen=1)
                        continue
                    try:
                        directory_info = _directory_stat(path)
                    except OSError as error:
                        discoveries.append(
                            DiscoveredMedia.error(path, _stat_issue(error))
                        )
                        tracker.advance(entries_seen=1, errors=1)
                        continue
                    if stat.S_ISLNK(directory_info.st_mode) or _is_reparse(
                        directory_info
                    ):
                        discoveries.append(
                            DiscoveredMedia.error(
                                path,
                                DiscoveryIssue(
                                    DiscoveryErrorCode.LINK_SKIPPED,
                                    "link or reparse entry was not followed",
                                ),
                            )
                        )
                        tracker.advance(entries_seen=1, errors=1)
                        continue
                    if not stat.S_ISDIR(directory_info.st_mode):
                        discoveries.append(
                            DiscoveredMedia.error(
                                path,
                                DiscoveryIssue(
                                    DiscoveryErrorCode.STAT_FAILED,
                                    "directory type changed during inspection",
                                ),
                            )
                        )
                        tracker.advance(entries_seen=1, errors=1)
                        continue
                    identity = _directory_identity(directory_info)
                    if identity in visited:
                        discoveries.append(
                            DiscoveredMedia.error(
                                path,
                                DiscoveryIssue(
                                    DiscoveryErrorCode.LOOP_SKIPPED,
                                    "directory identity was already visited",
                                ),
                            )
                        )
                        tracker.advance(entries_seen=1, errors=1)
                        continue
                    visited.add(identity)
                    child_directories.append((path, identity))
                    tracker.advance(entries_seen=1)
                elif stat.S_ISREG(info.st_mode) and is_supported_video_filename(entry.name):
                    discovery = self._parse(path, entry.name, info.st_size)
                    discoveries.append(discovery)
                    increments = {
                        "entries_seen": 1,
                        "supported_media_found": 1,
                    }
                    if discovery.classification is DiscoveryClassification.MOVIE_CANDIDATE:
                        increments["movie_candidates"] = 1
                    elif discovery.classification is DiscoveryClassification.TV_EPISODE_SKIPPED:
                        increments["tv_episodes_skipped"] = 1
                    elif discovery.classification is DiscoveryClassification.UNKNOWN_MEDIA:
                        increments["unknown_media"] = 1
                    else:
                        increments["errors"] = 1
                    tracker.advance(**increments)
                else:
                    tracker.advance(entries_seen=1)

            pending.extend(
                reversed(
                    sorted(
                        child_directories,
                        key=lambda item: _path_order(item[0]),
                    )
                )
            )

        tracker.emit(force=True)
        return tuple(sorted(discoveries, key=lambda item: _path_order(item.path)))

    def _parse(self, path: Path, filename: str, file_size: int) -> DiscoveredMedia:
        try:
            parsed = self._parser(filename)
        except Exception as error:
            return DiscoveredMedia.error(
                path,
                DiscoveryIssue(
                    DiscoveryErrorCode.PARSE_FAILED,
                    f"filename parser failed: {type(error).__name__}",
                ),
            )
        classification = {
            MediaType.MOVIE: DiscoveryClassification.MOVIE_CANDIDATE,
            MediaType.TV_EPISODE: DiscoveryClassification.TV_EPISODE_SKIPPED,
            MediaType.UNKNOWN: DiscoveryClassification.UNKNOWN_MEDIA,
        }[parsed.media_type]
        return DiscoveredMedia(path, file_size, parsed, classification, None)


def _validate_root(root: Path) -> os.stat_result:
    info: os.stat_result | None = None
    for component in _root_components(root):
        info = _root_lstat(component, root)
        if stat.S_ISLNK(info.st_mode) or _is_reparse(info):
            raise DiscoveryRootError(
                root,
                DiscoveryErrorCode.ROOT_LINK_NOT_ALLOWED,
                f"scan root traverses a link or reparse point: {component}",
            )
    if info is None:
        info = _root_lstat(root, root)
    if not stat.S_ISDIR(info.st_mode):
        raise DiscoveryRootError(
            root,
            DiscoveryErrorCode.ROOT_NOT_DIRECTORY,
            f"scan root is not a directory: {root}",
        )
    return info


def _root_components(root: Path) -> tuple[Path, ...]:
    if not root.anchor:
        return (root,)
    current = Path(root.anchor)
    components: list[Path] = []
    for part in root.parts[1:]:
        current = current / part
        components.append(current)
    return tuple(components)


def _root_lstat(component: Path, root: Path) -> os.stat_result:
    try:
        return os.lstat(component)
    except FileNotFoundError as error:
        raise DiscoveryRootError(
            root,
            DiscoveryErrorCode.ROOT_MISSING,
            f"scan root does not exist: {root}",
        ) from error
    except PermissionError as error:
        raise DiscoveryRootError(
            root,
            DiscoveryErrorCode.PERMISSION_DENIED,
            f"scan root is not accessible: {root}",
        ) from error
    except OSError as error:
        raise DiscoveryRootError(
            root,
            DiscoveryErrorCode.STAT_FAILED,
            f"scan root could not be inspected: {root}",
        ) from error


def _read_entries(
    directory: Path,
    cancellation: DiscoveryCancellation | None = None,
    tracker: _ProgressTracker | None = None,
) -> tuple[os.DirEntry[str], ...]:
    with os.scandir(directory) as entries:
        collected: list[os.DirEntry[str]] = []
        for index, entry in enumerate(entries, start=1):
            collected.append(entry)
            if index % 32 == 0 and tracker is not None:
                _raise_if_cancelled(cancellation, tracker)
        return tuple(
            sorted(collected, key=lambda entry: (entry.name.casefold(), entry.name))
        )


def _entry_stat(entry: os.DirEntry[str]) -> os.stat_result:
    return entry.stat(follow_symlinks=False)


def _directory_stat(path: Path) -> os.stat_result:
    """Reinspect a directory path for stable Windows filesystem identity."""
    return os.lstat(path)


def _scan_directory_stat(path: Path) -> os.stat_result:
    """Revalidate a scheduled directory immediately before enumeration."""
    return os.lstat(path)


def _directory_identity(info: os.stat_result) -> tuple[int, int]:
    return _identity(info)


def _identity(info: os.stat_result) -> tuple[int, int]:
    return info.st_dev, info.st_ino


def _is_reparse(info: os.stat_result) -> bool:
    attributes = getattr(info, "st_file_attributes", 0) or 0
    marker = getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0)
    return bool(marker and attributes & marker)


def _directory_issue(error: OSError) -> DiscoveryIssue:
    if isinstance(error, PermissionError):
        return DiscoveryIssue(DiscoveryErrorCode.PERMISSION_DENIED, "directory access denied")
    if isinstance(error, FileNotFoundError):
        return DiscoveryIssue(DiscoveryErrorCode.DISAPPEARED, "directory disappeared")
    return DiscoveryIssue(
        DiscoveryErrorCode.DIRECTORY_READ_FAILED,
        f"directory read failed: {type(error).__name__}",
    )


def _stat_issue(error: OSError) -> DiscoveryIssue:
    if isinstance(error, PermissionError):
        return DiscoveryIssue(DiscoveryErrorCode.PERMISSION_DENIED, "file access denied")
    if isinstance(error, FileNotFoundError):
        return DiscoveryIssue(DiscoveryErrorCode.DISAPPEARED, "file disappeared")
    return DiscoveryIssue(
        DiscoveryErrorCode.STAT_FAILED,
        f"file stat failed: {type(error).__name__}",
    )


def _root_runtime_error(root: Path, error: OSError) -> DiscoveryRootError:
    if isinstance(error, FileNotFoundError):
        code = DiscoveryErrorCode.ROOT_MISSING
        message = "scan root disappeared during discovery"
    elif isinstance(error, PermissionError):
        code = DiscoveryErrorCode.PERMISSION_DENIED
        message = "scan root became inaccessible during discovery"
    else:
        code = DiscoveryErrorCode.STAT_FAILED
        message = "scan root could not be safely revalidated"
    return DiscoveryRootError(root, code, message)


def _sort_paths(paths: list[Path]) -> list[Path]:
    return sorted(paths, key=_path_order)


def _path_order(path: Path) -> tuple[str, str]:
    value = str(path)
    return value.casefold(), value


class _ProgressTracker:
    def __init__(
        self,
        callback: DiscoveryProgressCallback | None,
        interval: int,
    ) -> None:
        self._callback = callback
        self._interval = interval
        self._work_since_emit = 0
        self.current = DiscoveryProgress()
        self._last_emitted: DiscoveryProgress | None = None

    def advance(self, **increments: int) -> None:
        values = {
            field: getattr(self.current, field) + amount
            for field, amount in increments.items()
        }
        self.current = replace(self.current, **values)
        self._work_since_emit += 1
        self.emit()

    def emit(self, *, force: bool = False) -> None:
        if self._callback is None:
            return
        if not force and self._work_since_emit < self._interval:
            return
        if self.current == self._last_emitted:
            return
        self._callback(self.current)
        self._last_emitted = self.current
        self._work_since_emit = 0


def _raise_if_cancelled(
    cancellation: DiscoveryCancellation | None,
    tracker: _ProgressTracker,
) -> None:
    if cancellation is not None and cancellation.is_cancelled():
        tracker.emit(force=True)
        raise DiscoveryCancelled(tracker.current)
