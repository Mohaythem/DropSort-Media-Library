from __future__ import annotations

from pathlib import Path

import pytest

from dropsort.application.use_cases import DiscoverMedia
from dropsort.media.discovery import DiscoveredMedia


class FakeScanner:
    def __init__(self) -> None:
        self.calls: list[tuple[Path, bool]] = []

    def scan(self, root: Path, *, recursive: bool = True) -> tuple[DiscoveredMedia, ...]:
        self.calls.append((root, recursive))
        return ()


def test_discover_media_delegates_to_scanner_contract_without_filesystem_access() -> None:
    scanner = FakeScanner()
    root = Path(r"D:\Movies")

    result = DiscoverMedia(scanner).execute(root, recursive=False)

    assert result == ()
    assert scanner.calls == [(root, False)]


def test_discover_media_validates_path_and_recursive_flag() -> None:
    use_case = DiscoverMedia(FakeScanner())

    with pytest.raises(ValueError, match="root"):
        use_case.execute("D:\\Movies")  # type: ignore[arg-type]
    with pytest.raises(ValueError, match="recursive"):
        use_case.execute(Path(r"D:\Movies"), recursive=1)  # type: ignore[arg-type]
