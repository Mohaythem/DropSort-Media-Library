from __future__ import annotations

from pathlib import Path
import os
import stat

import pytest

import dropsort.media.discovery.scanner as scanner_module
from dropsort.media.discovery import (
    DiscoveryErrorCode,
    DiscoveryRootError,
    ReadOnlyMediaScanner,
)


def test_scanner_validates_direct_inputs(tmp_path: Path) -> None:
    scanner = ReadOnlyMediaScanner()

    with pytest.raises(ValueError, match="root"):
        scanner.scan(str(tmp_path))  # type: ignore[arg-type]
    with pytest.raises(ValueError, match="recursive"):
        scanner.scan(tmp_path, recursive=1)  # type: ignore[arg-type]


@pytest.mark.parametrize(
    ("error", "code"),
    (
        (PermissionError("denied"), DiscoveryErrorCode.PERMISSION_DENIED),
        (OSError("broken"), DiscoveryErrorCode.STAT_FAILED),
    ),
)
def test_root_stat_failures_are_controlled(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    error: OSError,
    code: DiscoveryErrorCode,
) -> None:
    monkeypatch.setattr(scanner_module.os, "lstat", lambda _: (_ for _ in ()).throw(error))

    with pytest.raises(DiscoveryRootError) as caught:
        ReadOnlyMediaScanner().scan(tmp_path)

    assert caught.value.code is code


def test_mocked_reparse_root_is_rejected(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setattr(scanner_module, "_is_reparse", lambda _: True)

    with pytest.raises(DiscoveryRootError) as caught:
        ReadOnlyMediaScanner().scan(tmp_path)

    assert caught.value.code is DiscoveryErrorCode.ROOT_LINK_NOT_ALLOWED


def test_mocked_reparse_entry_is_reported_and_not_followed(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    (tmp_path / "Movie.2024.mkv").write_bytes(b"media")
    original_validate_root = scanner_module._validate_root
    root_info = original_validate_root(tmp_path)
    monkeypatch.setattr(scanner_module, "_validate_root", lambda _: root_info)
    calls = 0

    def child_only_reparse(_):
        nonlocal calls
        calls += 1
        return calls > 1

    monkeypatch.setattr(scanner_module, "_is_reparse", child_only_reparse)

    result = ReadOnlyMediaScanner().scan(tmp_path)

    assert result[0].issue.code is DiscoveryErrorCode.LINK_SKIPPED  # type: ignore[union-attr]


def test_reparse_in_root_ancestor_is_rejected(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    calls = 0

    def second_component_is_reparse(_: os.stat_result) -> bool:
        nonlocal calls
        calls += 1
        return calls == 2

    monkeypatch.setattr(scanner_module, "_is_reparse", second_component_is_reparse)

    with pytest.raises(DiscoveryRootError) as caught:
        ReadOnlyMediaScanner().scan(tmp_path)

    assert caught.value.code is DiscoveryErrorCode.ROOT_LINK_NOT_ALLOWED


@pytest.mark.parametrize(
    ("error", "code"),
    (
        (FileNotFoundError("gone"), DiscoveryErrorCode.DISAPPEARED),
        (OSError("broken"), DiscoveryErrorCode.DIRECTORY_READ_FAILED),
    ),
)
def test_root_directory_read_failures_are_controlled_root_errors(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    error: OSError,
    code: DiscoveryErrorCode,
) -> None:
    monkeypatch.setattr(
        scanner_module,
        "_read_entries",
        lambda *_: (_ for _ in ()).throw(error),
    )

    with pytest.raises(DiscoveryRootError) as caught:
        ReadOnlyMediaScanner().scan(tmp_path)

    expected = {
        DiscoveryErrorCode.DISAPPEARED: DiscoveryErrorCode.ROOT_MISSING,
        DiscoveryErrorCode.DIRECTORY_READ_FAILED: DiscoveryErrorCode.STAT_FAILED,
    }[code]
    assert caught.value.code is expected


@pytest.mark.parametrize(
    ("error", "code"),
    (
        (PermissionError("denied"), DiscoveryErrorCode.PERMISSION_DENIED),
        (OSError("broken"), DiscoveryErrorCode.STAT_FAILED),
    ),
)
def test_entry_stat_failures_are_controlled_items(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    error: OSError,
    code: DiscoveryErrorCode,
) -> None:
    (tmp_path / "Movie.2024.mkv").write_bytes(b"media")
    monkeypatch.setattr(
        scanner_module,
        "_entry_stat",
        lambda _: (_ for _ in ()).throw(error),
    )

    result = ReadOnlyMediaScanner().scan(tmp_path)

    assert result[0].issue.code is code  # type: ignore[union-attr]


@pytest.mark.parametrize(
    ("error", "code"),
    (
        (FileNotFoundError("gone"), DiscoveryErrorCode.DISAPPEARED),
        (PermissionError("denied"), DiscoveryErrorCode.PERMISSION_DENIED),
        (OSError("broken"), DiscoveryErrorCode.STAT_FAILED),
    ),
)
def test_directory_identity_reinspection_failures_are_controlled(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    error: OSError,
    code: DiscoveryErrorCode,
) -> None:
    nested = tmp_path / "nested"
    nested.mkdir()
    monkeypatch.setattr(
        scanner_module,
        "_directory_stat",
        lambda _: (_ for _ in ()).throw(error),
        raising=False,
    )

    result = ReadOnlyMediaScanner().scan(tmp_path)

    assert len(result) == 1
    assert result[0].path == nested.absolute()
    assert result[0].issue.code is code  # type: ignore[union-attr]


def test_directory_that_becomes_reparse_during_reinspection_is_not_followed(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    nested = tmp_path / "nested"
    nested.mkdir()
    original = scanner_module._is_reparse
    reinspection = os.lstat(nested)
    monkeypatch.setattr(
        scanner_module,
        "_directory_stat",
        lambda _: reinspection,
        raising=False,
    )
    monkeypatch.setattr(
        scanner_module,
        "_is_reparse",
        lambda info: info is reinspection or original(info),
    )

    result = ReadOnlyMediaScanner().scan(tmp_path)

    assert len(result) == 1
    assert result[0].path == nested.absolute()
    assert result[0].issue.code is DiscoveryErrorCode.LINK_SKIPPED  # type: ignore[union-attr]


def test_scheduled_directory_that_becomes_reparse_before_enumeration_is_not_followed(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    nested = tmp_path / "nested"
    nested.mkdir()
    (nested / "Movie.2024.mkv").write_bytes(b"media")
    original = scanner_module._scan_directory_stat
    nested_info = os.lstat(nested)

    def changed(path: Path):
        if path == nested.absolute():
            values = list(nested_info)
            values[0] = stat.S_IFLNK
            return os.stat_result(values)
        return original(path)

    monkeypatch.setattr(scanner_module, "_scan_directory_stat", changed)

    result = ReadOnlyMediaScanner().scan(tmp_path)

    assert len(result) == 1
    assert result[0].path == nested.absolute()
    assert result[0].issue.code is DiscoveryErrorCode.LINK_SKIPPED  # type: ignore[union-attr]


def test_scheduled_directory_identity_change_is_rejected_before_enumeration(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    nested = tmp_path / "nested"
    nested.mkdir()
    (nested / "Movie.2024.mkv").write_bytes(b"media")
    original = scanner_module._scan_directory_stat
    nested_info = os.lstat(nested)

    def replaced(path: Path):
        if path == nested.absolute():
            values = list(nested_info)
            values[1] += 1
            return os.stat_result(values)
        return original(path)

    monkeypatch.setattr(scanner_module, "_scan_directory_stat", replaced)

    result = ReadOnlyMediaScanner().scan(tmp_path)

    assert len(result) == 1
    assert result[0].path == nested.absolute()
    assert result[0].issue.code is DiscoveryErrorCode.STAT_FAILED  # type: ignore[union-attr]


def test_scheduled_directory_stat_failure_is_controlled_before_enumeration(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    nested = tmp_path / "nested"
    nested.mkdir()
    original = scanner_module._scan_directory_stat

    def fail(path: Path):
        if path == nested.absolute():
            raise PermissionError("denied")
        return original(path)

    monkeypatch.setattr(scanner_module, "_scan_directory_stat", fail)

    result = ReadOnlyMediaScanner().scan(tmp_path)

    assert len(result) == 1
    assert result[0].path == nested.absolute()
    assert result[0].issue.code is DiscoveryErrorCode.PERMISSION_DENIED  # type: ignore[union-attr]
