from __future__ import annotations

from collections.abc import Callable
import os
from pathlib import Path
import stat
import subprocess

from dropsort.library.playback.errors import (
    InvalidMediaFileError,
    LocalMediaActionError,
    LocalMediaLaunchError,
    MissingMediaFileError,
)


StartFile = Callable[[str], None]
ExplorerLauncher = Callable[[tuple[str, ...]], None]


class WindowsLocalMediaActions:
    """Argument-safe Windows access actions with no catalog or media mutation."""

    def __init__(
        self,
        *,
        start_file: StartFile | None = None,
        explorer_launcher: ExplorerLauncher | None = None,
    ) -> None:
        self._start_file = start_file or _start_with_file_association
        self._explorer_launcher = explorer_launcher or _launch_explorer

    def play(self, media_path: Path) -> None:
        validated = _validate_regular_media_file(media_path)
        try:
            self._start_file(str(validated))
        except (OSError, LocalMediaActionError) as error:
            raise LocalMediaLaunchError(
                "Windows could not play the selected media file"
            ) from error

    def open_folder(self, media_path: Path) -> None:
        validated = _validate_regular_media_file(media_path)
        arguments = ("explorer.exe", "/select,", str(validated))
        try:
            self._explorer_launcher(arguments)
        except (OSError, LocalMediaActionError) as error:
            raise LocalMediaLaunchError(
                "Windows could not open the media file's folder"
            ) from error


def _validate_regular_media_file(media_path: Path) -> Path:
    if not isinstance(media_path, Path) or not media_path.is_absolute():
        raise InvalidMediaFileError("media path must be an absolute Path")
    try:
        information = os.lstat(media_path)
    except FileNotFoundError as error:
        raise MissingMediaFileError("cataloged media file is missing") from error
    except OSError as error:
        raise LocalMediaActionError("media path could not be inspected") from error

    attributes = getattr(information, "st_file_attributes", 0) or 0
    reparse_flag = getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0)
    if (
        stat.S_ISLNK(information.st_mode)
        or (reparse_flag and attributes & reparse_flag)
        or not stat.S_ISREG(information.st_mode)
    ):
        raise InvalidMediaFileError("cataloged media path is not a regular file")
    return media_path


def _start_with_file_association(path: str) -> None:
    start_file = getattr(os, "startfile", None)
    if start_file is None:
        raise LocalMediaLaunchError("Windows file associations are unavailable")
    start_file(path)


def _launch_explorer(arguments: tuple[str, ...]) -> None:
    subprocess.Popen(arguments, shell=False, close_fds=True)

