from pathlib import Path

import pytest

from dropsort.core.operations.models import OperationType
from dropsort.core.safety.errors import (
    CaseInsensitiveCollisionError,
    DestinationExistsError,
    InvalidRenameError,
    LinkTraversalError,
    SameFileError,
    UnsafePathError,
)
from dropsort.core.safety.path_policy import PathPolicy


def test_path_outside_approved_roots_is_rejected(tmp_path: Path) -> None:
    root = tmp_path / "root"
    outside = tmp_path / "outside"
    root.mkdir()
    outside.mkdir()
    source = root / "movie.mkv"
    source.write_bytes(b"media")
    policy = PathPolicy([root])

    with pytest.raises(UnsafePathError):
        policy.validate_plan(source, outside / "movie.mkv", OperationType.MOVE)


def test_same_source_and_destination_is_rejected(tmp_path: Path) -> None:
    root = tmp_path / "root"
    root.mkdir()
    source = root / "movie.mkv"
    source.write_bytes(b"media")
    policy = PathPolicy([root])

    with pytest.raises(SameFileError):
        policy.validate_plan(source, source, OperationType.MOVE)


def test_case_insensitive_windows_collision_is_rejected(tmp_path: Path) -> None:
    root = tmp_path / "root"
    root.mkdir()
    source = root / "source.mkv"
    source.write_bytes(b"source")
    (root / "Movie.MKV").write_bytes(b"existing")
    policy = PathPolicy([root])

    with pytest.raises(CaseInsensitiveCollisionError):
        policy.validate_plan(source, root / "movie.mkv", OperationType.MOVE)


def test_exact_destination_name_raises_destination_exists(tmp_path: Path) -> None:
    root = tmp_path / "root"
    root.mkdir()
    source = root / "source.mkv"
    source.write_bytes(b"source")
    (root / "movie.mkv").write_bytes(b"existing")
    policy = PathPolicy([root])

    with pytest.raises(DestinationExistsError) as exc_info:
        policy.validate_plan(source, root / "movie.mkv", OperationType.MOVE)

    assert type(exc_info.value) is DestinationExistsError


def test_rename_cannot_change_directory(tmp_path: Path) -> None:
    root = tmp_path / "root"
    one = root / "one"
    two = root / "two"
    one.mkdir(parents=True)
    two.mkdir()
    source = one / "movie.mkv"
    source.write_bytes(b"media")
    policy = PathPolicy([root])

    with pytest.raises(InvalidRenameError):
        policy.validate_plan(source, two / "renamed.mkv", OperationType.RENAME)


def test_symlink_traversal_is_rejected_when_supported(tmp_path: Path) -> None:
    root = tmp_path / "root"
    real = root / "real"
    root.mkdir()
    real.mkdir()
    link = root / "link"
    try:
        link.symlink_to(real, target_is_directory=True)
    except (OSError, NotImplementedError):
        pytest.skip("Symlinks unavailable on this platform")
    source = real / "movie.mkv"
    source.write_bytes(b"media")
    policy = PathPolicy([root])

    with pytest.raises(LinkTraversalError):
        policy.validate_plan(link / "movie.mkv", root / "renamed.mkv", OperationType.MOVE)
