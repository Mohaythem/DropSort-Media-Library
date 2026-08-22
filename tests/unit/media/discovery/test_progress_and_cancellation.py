from __future__ import annotations

from pathlib import Path

import pytest

from dropsort.media.discovery import (
    DiscoveryCancelled,
    DiscoveryProgress,
    ReadOnlyMediaScanner,
)


class Cancellation:
    def __init__(self) -> None:
        self.cancelled = False

    def cancel(self) -> None:
        self.cancelled = True

    def is_cancelled(self) -> bool:
        return self.cancelled


def _touch(path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(b"x")


def test_discovery_progress_is_monotonic_and_final_counts_are_exact(tmp_path: Path) -> None:
    _touch(tmp_path / "Movie.One.2020.mkv")
    _touch(tmp_path / "nested" / "Show.S01E01.mkv")
    _touch(tmp_path / "nested" / "1080p.x264.mp4")
    _touch(tmp_path / "notes.txt")
    progress: list[DiscoveryProgress] = []

    result = ReadOnlyMediaScanner(progress_interval=2).scan(
        tmp_path,
        progress=progress.append,
    )

    assert len(result) == 3
    assert progress[0] == DiscoveryProgress()
    assert progress[-1] == DiscoveryProgress(
        directories_seen=2,
        entries_seen=5,
        supported_media_found=3,
        movie_candidates=1,
        tv_episodes_skipped=1,
        unknown_media=1,
        errors=0,
    )
    for previous, current in zip(progress, progress[1:], strict=False):
        assert current.directories_seen >= previous.directories_seen
        assert current.entries_seen >= previous.entries_seen
        assert current.supported_media_found >= previous.supported_media_found
        assert current.movie_candidates >= previous.movie_candidates
        assert current.tv_episodes_skipped >= previous.tv_episodes_skipped
        assert current.unknown_media >= previous.unknown_media
        assert current.errors >= previous.errors


def test_progress_is_count_throttled_in_large_directory(tmp_path: Path) -> None:
    for index in range(100):
        _touch(tmp_path / f"note-{index:03}.txt")
    progress: list[DiscoveryProgress] = []

    ReadOnlyMediaScanner(progress_interval=16).scan(tmp_path, progress=progress.append)

    assert progress[-1].entries_seen == 100
    assert 3 <= len(progress) <= 10


def test_cancel_before_first_directory_discards_partial_results(tmp_path: Path) -> None:
    _touch(tmp_path / "Movie.2024.mkv")
    cancellation = Cancellation()
    cancellation.cancel()

    with pytest.raises(DiscoveryCancelled) as caught:
        ReadOnlyMediaScanner().scan(tmp_path, cancellation=cancellation)

    assert caught.value.progress == DiscoveryProgress()


def test_cancel_mid_nested_traversal_stops_with_monotonic_snapshot(tmp_path: Path) -> None:
    for index in range(40):
        _touch(tmp_path / "nested" / f"Movie.{2000 + index}.mkv")
    cancellation = Cancellation()
    progress: list[DiscoveryProgress] = []

    def observe(value: DiscoveryProgress) -> None:
        progress.append(value)
        if value.entries_seen >= 8:
            cancellation.cancel()

    with pytest.raises(DiscoveryCancelled) as caught:
        ReadOnlyMediaScanner(progress_interval=4).scan(
            tmp_path,
            progress=observe,
            cancellation=cancellation,
        )

    assert caught.value.progress.entries_seen < 41
    assert caught.value.progress == progress[-1]


def test_progress_model_rejects_invalid_or_incoherent_counts() -> None:
    with pytest.raises(ValueError, match="non-negative"):
        DiscoveryProgress(entries_seen=-1)
    with pytest.raises(ValueError, match="supported"):
        DiscoveryProgress(entries_seen=1, supported_media_found=2)
    with pytest.raises(ValueError, match="classified"):
        DiscoveryProgress(entries_seen=2, supported_media_found=1, movie_candidates=2)
