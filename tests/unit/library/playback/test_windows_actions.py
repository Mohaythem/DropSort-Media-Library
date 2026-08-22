from __future__ import annotations

from hashlib import sha256
from pathlib import Path

import pytest

import dropsort.library.playback.windows as windows_module
from dropsort.library.playback import (
    InvalidMediaFileError,
    LocalMediaActionError,
    LocalMediaLaunchError,
    MissingMediaFileError,
    WindowsLocalMediaActions,
)


class RecordingLauncher:
    def __init__(self) -> None:
        self.played: list[str] = []
        self.explorer_arguments: list[tuple[str, ...]] = []

    def play(self, path: str) -> None:
        self.played.append(path)

    def explorer(self, arguments: tuple[str, ...]) -> None:
        self.explorer_arguments.append(arguments)


@pytest.mark.parametrize(
    "name",
    (
        "Movie With Spaces.mkv",
        "Movie & (Director's Cut) #1.mkv",
        "Fílm 日本語, Final.mkv",
    ),
)
def test_play_treats_special_windows_path_as_data(tmp_path: Path, name: str) -> None:
    media = tmp_path / name
    media.write_bytes(b"unchanged movie")
    launcher = RecordingLauncher()
    actions = WindowsLocalMediaActions(
        start_file=launcher.play,
        explorer_launcher=launcher.explorer,
    )

    actions.play(media)

    assert launcher.played == [str(media)]


def test_open_folder_selects_the_exact_existing_file_with_argument_list(
    tmp_path: Path,
) -> None:
    media = tmp_path / "Movie, Special & Safe.mkv"
    media.write_bytes(b"movie")
    launcher = RecordingLauncher()
    actions = WindowsLocalMediaActions(
        start_file=launcher.play,
        explorer_launcher=launcher.explorer,
    )

    actions.open_folder(media)

    assert launcher.explorer_arguments == [
        ("explorer.exe", "/select,", str(media)),
    ]
    assert Path(launcher.explorer_arguments[0][-1]).parent == tmp_path


@pytest.mark.parametrize("operation", ("play", "open_folder"))
def test_missing_media_file_is_controlled(tmp_path: Path, operation: str) -> None:
    actions = WindowsLocalMediaActions(
        start_file=lambda _path: None,
        explorer_launcher=lambda _arguments: None,
    )

    with pytest.raises(MissingMediaFileError):
        getattr(actions, operation)(tmp_path / "missing.mkv")


@pytest.mark.parametrize("operation", ("play", "open_folder"))
def test_directory_is_rejected_as_invalid_media(tmp_path: Path, operation: str) -> None:
    actions = WindowsLocalMediaActions(
        start_file=lambda _path: None,
        explorer_launcher=lambda _arguments: None,
    )

    with pytest.raises(InvalidMediaFileError):
        getattr(actions, operation)(tmp_path)


def test_play_translates_no_association_or_toctou_launch_failure(tmp_path: Path) -> None:
    media = tmp_path / "movie.mkv"
    media.write_bytes(b"movie")

    def fail_after_validation(_path: str) -> None:
        media.unlink()
        raise FileNotFoundError("association target disappeared")

    actions = WindowsLocalMediaActions(
        start_file=fail_after_validation,
        explorer_launcher=lambda _arguments: None,
    )

    with pytest.raises(LocalMediaLaunchError) as captured:
        actions.play(media)

    assert "play" in str(captured.value).casefold()


def test_open_folder_translates_explorer_failure(tmp_path: Path) -> None:
    media = tmp_path / "movie.mkv"
    media.write_bytes(b"movie")

    def fail(_arguments: tuple[str, ...]) -> None:
        raise PermissionError("explorer unavailable")

    actions = WindowsLocalMediaActions(
        start_file=lambda _path: None,
        explorer_launcher=fail,
    )

    with pytest.raises(LocalMediaLaunchError) as captured:
        actions.open_folder(media)

    assert "folder" in str(captured.value).casefold()


def test_local_actions_never_modify_media_bytes_or_path(tmp_path: Path) -> None:
    media = tmp_path / "immutable-media.mkv"
    media.write_bytes(b"DropSort must not mutate this media")
    before_path = media.resolve(strict=True)
    before_size = media.stat().st_size
    before_hash = sha256(media.read_bytes()).hexdigest()
    launcher = RecordingLauncher()
    actions = WindowsLocalMediaActions(
        start_file=launcher.play,
        explorer_launcher=launcher.explorer,
    )

    actions.play(media)
    actions.open_folder(media)

    assert media.resolve(strict=True) == before_path
    assert media.stat().st_size == before_size
    assert sha256(media.read_bytes()).hexdigest() == before_hash


def test_relative_or_non_path_values_are_rejected() -> None:
    actions = WindowsLocalMediaActions(
        start_file=lambda _path: None,
        explorer_launcher=lambda _arguments: None,
    )

    with pytest.raises(InvalidMediaFileError):
        actions.play(Path("relative.mkv"))
    with pytest.raises(InvalidMediaFileError):
        actions.play("C:/movie.mkv")  # type: ignore[arg-type]


def test_inspection_permission_failure_is_controlled(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    media = tmp_path / "movie.mkv"
    media.write_bytes(b"movie")
    monkeypatch.setattr(windows_module.os, "lstat", lambda _path: (_ for _ in ()).throw(PermissionError("denied")))
    actions = WindowsLocalMediaActions(
        start_file=lambda _path: None,
        explorer_launcher=lambda _arguments: None,
    )

    with pytest.raises(LocalMediaActionError) as captured:
        actions.play(media)

    assert type(captured.value) is LocalMediaActionError


def test_default_launch_helpers_use_native_apis_without_shell_interpolation(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    media = tmp_path / "Movie & Safe.mkv"
    media.write_bytes(b"movie")
    played: list[str] = []
    explorer_calls: list[tuple[tuple[str, ...], bool, bool]] = []
    monkeypatch.setattr(windows_module.os, "startfile", played.append)
    monkeypatch.setattr(
        windows_module.subprocess,
        "Popen",
        lambda arguments, *, shell, close_fds: explorer_calls.append(
            (arguments, shell, close_fds)
        ),
    )
    actions = WindowsLocalMediaActions()

    actions.play(media)
    actions.open_folder(media)

    assert played == [str(media)]
    assert explorer_calls == [
        (("explorer.exe", "/select,", str(media)), False, True),
    ]


def test_missing_native_file_association_api_is_a_controlled_launch_error(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    media = tmp_path / "movie.mkv"
    media.write_bytes(b"movie")
    monkeypatch.delattr(windows_module.os, "startfile", raising=False)
    actions = WindowsLocalMediaActions(
        explorer_launcher=lambda _arguments: None,
    )

    with pytest.raises(LocalMediaLaunchError):
        actions.play(media)
