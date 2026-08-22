from __future__ import annotations

from pathlib import Path
import os

import pytest

import dropsort.media.discovery.scanner as scanner_module
from dropsort.media.discovery import (
    DiscoveryClassification,
    DiscoveryErrorCode,
    DiscoveryRootError,
    ReadOnlyMediaScanner,
)


def _write(path: Path, content: bytes = b"media") -> Path:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(content)
    return path


def test_empty_folder_returns_no_discoveries(tmp_path: Path) -> None:
    assert ReadOnlyMediaScanner().scan(tmp_path) == ()


def test_recursive_scan_discovers_movies_in_root_and_nested_folders(tmp_path: Path) -> None:
    root_movie = _write(tmp_path / "The.Dark.Knight.2008.1080p.mkv", b"123")
    nested_movie = _write(tmp_path / "nested" / "Interstellar.2014.2160p.mp4", b"12345")

    result = ReadOnlyMediaScanner().scan(tmp_path, recursive=True)

    assert [item.path for item in result] == [nested_movie.absolute(), root_movie.absolute()]
    assert all(
        item.classification is DiscoveryClassification.MOVIE_CANDIDATE
        for item in result
    )
    assert [item.file_size for item in result] == [5, 3]


def test_manual_ui_regression_multiple_ordinary_movie_folders_are_not_scan_errors(
    tmp_path: Path,
) -> None:
    expected = {
        _write(
            tmp_path
            / "Prisoners (2013) [1080p]"
            / "Prisoners.1080p.BluRay.x264.YIFY.mp4"
        ).absolute(),
        _write(
            tmp_path
            / "All.Quiet.on.the.Western.Front.2022.release"
            / "All.Quiet.on.the.Western.Front.2022.1080p.mkv"
        ).absolute(),
        _write(
            tmp_path
            / "and justice for all"
            / "And.Justice.for.All.1979.1080p.mkv"
        ).absolute(),
    }

    result = ReadOnlyMediaScanner().scan(tmp_path, recursive=True)

    assert {item.path for item in result} == expected
    assert all(
        item.classification is DiscoveryClassification.MOVIE_CANDIDATE
        for item in result
    )
    assert not any(item.path.parent == tmp_path for item in result)


def test_windows_zeroed_direntry_identity_is_reinspected_before_loop_detection(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    first = _write(tmp_path / "first movie" / "First.Movie.2020.mkv")
    second = _write(tmp_path / "second movie" / "Second.Movie.2021.mkv")
    original = scanner_module._entry_stat

    def windows_like_entry_stat(entry: os.DirEntry[str]):
        info = original(entry)
        if entry.is_dir(follow_symlinks=False):
            values = list(info)
            values[1] = 0
            values[2] = 0
            return os.stat_result(values)
        return info

    monkeypatch.setattr(scanner_module, "_entry_stat", windows_like_entry_stat)

    result = ReadOnlyMediaScanner().scan(tmp_path, recursive=True)

    assert {item.path for item in result} == {first.absolute(), second.absolute()}
    assert not any(item.classification is DiscoveryClassification.ERROR for item in result)


def test_non_recursive_scan_ignores_nested_files(tmp_path: Path) -> None:
    root_movie = _write(tmp_path / "Movie.2024.mkv")
    _write(tmp_path / "nested" / "Other.2023.mkv")

    result = ReadOnlyMediaScanner().scan(tmp_path, recursive=False)

    assert [item.path for item in result] == [root_movie.absolute()]


def test_mixed_files_classify_tv_and_unknown_but_ignore_unsupported(tmp_path: Path) -> None:
    movie = _write(tmp_path / "Movie.Title.2024.mkv")
    tv = _write(tmp_path / "Show.Name.S01E02.mkv")
    unknown = _write(tmp_path / "1080p.x264.mkv")
    _write(tmp_path / "poster.jpg")
    _write(tmp_path / "subtitle.srt")

    result = ReadOnlyMediaScanner().scan(tmp_path)
    by_path = {item.path: item for item in result}

    assert set(by_path) == {movie.absolute(), tv.absolute(), unknown.absolute()}
    assert by_path[movie.absolute()].classification is DiscoveryClassification.MOVIE_CANDIDATE
    assert by_path[tv.absolute()].classification is DiscoveryClassification.TV_EPISODE_SKIPPED
    assert by_path[unknown.absolute()].classification is DiscoveryClassification.UNKNOWN_MEDIA


def test_output_is_deterministic_under_case_and_creation_order(tmp_path: Path) -> None:
    created = [
        _write(tmp_path / "zeta.2020.mkv"),
        _write(tmp_path / "Alpha.2020.mkv"),
        _write(tmp_path / "alpha2.2020.mkv"),
    ]
    scanner = ReadOnlyMediaScanner()

    first = scanner.scan(tmp_path)
    second = scanner.scan(tmp_path)

    expected = sorted((path.absolute() for path in created), key=lambda p: (str(p).casefold(), str(p)))
    assert [item.path for item in first] == expected
    assert second == first


def test_missing_root_and_root_file_are_controlled_errors(tmp_path: Path) -> None:
    scanner = ReadOnlyMediaScanner()
    missing = tmp_path / "missing"
    file_root = _write(tmp_path / "movie.mkv")

    with pytest.raises(DiscoveryRootError) as missing_error:
        scanner.scan(missing)
    with pytest.raises(DiscoveryRootError) as file_error:
        scanner.scan(file_root)

    assert missing_error.value.code is DiscoveryErrorCode.ROOT_MISSING
    assert file_error.value.code is DiscoveryErrorCode.ROOT_NOT_DIRECTORY


def test_broken_directory_is_reported_without_stopping_other_files(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    movie = _write(tmp_path / "Movie.2024.mkv")
    blocked = tmp_path / "blocked"
    blocked.mkdir()
    original = scanner_module._read_entries

    def fail_blocked(path: Path, *args):
        if path == blocked.absolute():
            raise PermissionError("denied")
        return original(path, *args)

    monkeypatch.setattr(scanner_module, "_read_entries", fail_blocked)

    result = ReadOnlyMediaScanner().scan(tmp_path)

    assert any(item.path == movie.absolute() for item in result)
    issue = next(item for item in result if item.path == blocked.absolute())
    assert issue.issue.code is DiscoveryErrorCode.PERMISSION_DENIED  # type: ignore[union-attr]


def test_file_disappearing_during_stat_is_a_controlled_item_error(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    vanished = _write(tmp_path / "Vanished.2024.mkv")
    original = scanner_module._entry_stat

    def disappear(entry: os.DirEntry[str]):
        if entry.name == vanished.name:
            raise FileNotFoundError(entry.path)
        return original(entry)

    monkeypatch.setattr(scanner_module, "_entry_stat", disappear)

    result = ReadOnlyMediaScanner().scan(tmp_path)

    assert len(result) == 1
    assert result[0].classification is DiscoveryClassification.ERROR
    assert result[0].issue.code is DiscoveryErrorCode.DISAPPEARED  # type: ignore[union-attr]


def test_parser_failure_is_contained_as_item_error(
    tmp_path: Path,
) -> None:
    file_path = _write(tmp_path / "Movie.2024.mkv")

    def broken_parser(_: object):
        raise ValueError("malformed")

    result = ReadOnlyMediaScanner(parser=broken_parser).scan(tmp_path)

    assert result[0].path == file_path.absolute()
    assert result[0].issue.code is DiscoveryErrorCode.PARSE_FAILED  # type: ignore[union-attr]


def test_repeated_directory_identity_is_skipped_to_prevent_recursion_loop(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    nested = tmp_path / "nested"
    nested.mkdir()
    root_info = os.lstat(tmp_path)
    root_identity = (root_info.st_dev, root_info.st_ino)
    monkeypatch.setattr(
        scanner_module,
        "_directory_identity",
        lambda _: root_identity,
    )

    result = ReadOnlyMediaScanner().scan(tmp_path)

    assert len(result) == 1
    assert result[0].path == nested.absolute()
    assert result[0].issue.code is DiscoveryErrorCode.LOOP_SKIPPED  # type: ignore[union-attr]


def test_symlink_entry_is_not_followed(tmp_path: Path) -> None:
    outside = tmp_path.parent / f"{tmp_path.name}-outside"
    outside.mkdir(exist_ok=True)
    _write(outside / "Outside.2024.mkv")
    link = tmp_path / "linked"
    try:
        link.symlink_to(outside, target_is_directory=True)
    except OSError as error:
        pytest.skip(f"symlinks unavailable on this host: {error}")

    result = ReadOnlyMediaScanner().scan(tmp_path)

    assert len(result) == 1
    assert result[0].path == link.absolute()
    assert result[0].issue.code is DiscoveryErrorCode.LINK_SKIPPED  # type: ignore[union-attr]


def test_linked_root_is_rejected(tmp_path: Path) -> None:
    target = tmp_path / "target"
    target.mkdir()
    link = tmp_path / "root-link"
    try:
        link.symlink_to(target, target_is_directory=True)
    except OSError as error:
        pytest.skip(f"symlinks unavailable on this host: {error}")

    with pytest.raises(DiscoveryRootError) as caught:
        ReadOnlyMediaScanner().scan(link)

    assert caught.value.code is DiscoveryErrorCode.ROOT_LINK_NOT_ALLOWED


def test_large_synthetic_tree_has_exact_deterministic_counts_and_no_duplicates(
    tmp_path: Path,
) -> None:
    expected_movies: set[Path] = set()
    for directory_index in range(20):
        directory = tmp_path / f"folder-{directory_index:02}"
        for file_index in range(50):
            if file_index < 10:
                expected_movies.add(
                    _write(
                        directory
                        / f"Movie.{directory_index:02}.{2000 + file_index}.mkv"
                    ).absolute()
                )
            elif file_index < 15:
                _write(directory / f"Show.S01E{file_index:02}.mp4")
            else:
                _write(directory / f"notes-{file_index:02}.txt")
    progress = []

    result = ReadOnlyMediaScanner(progress_interval=64).scan(
        tmp_path,
        progress=progress.append,
    )

    movie_paths = {
        item.path
        for item in result
        if item.classification is DiscoveryClassification.MOVIE_CANDIDATE
    }
    assert movie_paths == expected_movies
    assert len({item.path for item in result}) == len(result)
    assert progress[-1].directories_seen == 21
    assert progress[-1].entries_seen == 1_020
    assert progress[-1].supported_media_found == 300
    assert progress[-1].movie_candidates == 200
    assert progress[-1].tv_episodes_skipped == 100


def test_root_disappearing_at_pre_enumeration_revalidation_is_fatal(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    original = scanner_module._scan_directory_stat

    def disappear(path: Path):
        if path == tmp_path.absolute():
            raise FileNotFoundError(path)
        return original(path)

    monkeypatch.setattr(scanner_module, "_scan_directory_stat", disappear)

    with pytest.raises(DiscoveryRootError) as caught:
        ReadOnlyMediaScanner().scan(tmp_path)

    assert caught.value.code is DiscoveryErrorCode.ROOT_MISSING
