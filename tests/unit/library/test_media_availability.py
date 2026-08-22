from __future__ import annotations

import errno
from pathlib import Path
import stat

import pytest

from dropsort.library.availability import (
    AvailabilityInspectionStatus,
    NoFollowMediaFileInspector,
)


def test_regular_file_is_present_with_stable_identity(tmp_path: Path) -> None:
    path = tmp_path / "Movie.mkv"
    path.write_bytes(b"movie")

    result = NoFollowMediaFileInspector().inspect(path)

    assert result.status is AvailabilityInspectionStatus.PRESENT
    assert result.path == path.absolute()
    assert result.size == 5
    assert result.identity is not None


@pytest.mark.parametrize("kind", ("missing", "directory"))
def test_absent_or_non_file_target_is_confirmed_missing(tmp_path: Path, kind: str) -> None:
    path = tmp_path / "Movie.mkv"
    if kind == "directory":
        path.mkdir()

    result = NoFollowMediaFileInspector().inspect(path)

    assert result.status is AvailabilityInspectionStatus.MISSING
    assert result.identity is None


def test_inspection_error_is_not_misclassified_as_missing(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    path = (tmp_path / "Movie.mkv").absolute()

    def fail_lstat(_path: object):
        raise PermissionError(errno.EACCES, "denied")

    monkeypatch.setattr("dropsort.library.availability.inspector.os.lstat", fail_lstat)

    result = NoFollowMediaFileInspector().inspect(path)

    assert result.status is AvailabilityInspectionStatus.ERROR
    assert result.error_code == "INSPECTION_FAILED"


def test_link_or_reparse_target_is_missing_without_following_it(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    path = (tmp_path / "Movie.mkv").absolute()
    target = tmp_path / "target.mkv"
    target.write_bytes(b"do-not-read")
    fake = target.stat()
    monkeypatch.setattr("dropsort.library.availability.inspector.os.lstat", lambda _path: fake)
    monkeypatch.setattr(
        "dropsort.library.availability.inspector._is_link_or_reparse",
        lambda _info: True,
    )

    result = NoFollowMediaFileInspector().inspect(path)

    assert result.status is AvailabilityInspectionStatus.MISSING
    assert result.error_code == "UNSAFE_LINK"


def test_regular_file_mode_is_required_even_when_other_fields_look_valid(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    path = (tmp_path / "Movie.mkv").absolute()
    fake = type(
        "FakeStat",
        (),
        {
            "st_mode": stat.S_IFDIR,
            "st_size": 10,
            "st_mtime_ns": 1,
            "st_dev": 2,
            "st_ino": 3,
            "st_file_attributes": 0,
        },
    )()
    monkeypatch.setattr("dropsort.library.availability.inspector.os.lstat", lambda _path: fake)

    assert (
        NoFollowMediaFileInspector().inspect(path).status
        is AvailabilityInspectionStatus.MISSING
    )


def test_inspector_rejects_non_path_and_relative_values() -> None:
    for value in ("movie.mkv", Path("movie.mkv")):
        with pytest.raises(ValueError, match="absolute"):
            NoFollowMediaFileInspector().inspect(value)  # type: ignore[arg-type]
