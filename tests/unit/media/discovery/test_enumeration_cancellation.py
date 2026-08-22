from __future__ import annotations

from pathlib import Path

import pytest

import dropsort.media.discovery.scanner as scanner_module
from dropsort.media.discovery import DiscoveryCancelled, DiscoveryProgress


class Cancellation:
    def __init__(self) -> None:
        self.checks = 0

    def is_cancelled(self) -> bool:
        self.checks += 1
        return self.checks >= 2


def test_large_directory_enumeration_checks_cancellation_in_bounded_chunks(
    tmp_path: Path,
) -> None:
    for index in range(100):
        (tmp_path / f"file-{index:03}.txt").write_bytes(b"")
    cancellation = Cancellation()
    tracker = scanner_module._ProgressTracker(None, 32)

    with pytest.raises(DiscoveryCancelled):
        scanner_module._read_entries(tmp_path, cancellation, tracker)

    assert cancellation.checks == 2
    assert tracker.current == DiscoveryProgress()
