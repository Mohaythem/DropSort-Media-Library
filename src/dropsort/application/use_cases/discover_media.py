from __future__ import annotations

from pathlib import Path

from dropsort.media.discovery.contracts import (
    DiscoveryCancellation,
    DiscoveryProgressCallback,
    MediaDiscoveryScanner,
)
from dropsort.media.discovery.models import DiscoveredMedia


class DiscoverMedia:
    """Application boundary for caller-authorized, read-only folder discovery."""

    def __init__(self, scanner: MediaDiscoveryScanner) -> None:
        self._scanner = scanner

    def execute(
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
        if progress is None and cancellation is None:
            return self._scanner.scan(root, recursive=recursive)
        return self._scanner.scan(
            root,
            recursive=recursive,
            progress=progress,
            cancellation=cancellation,
        )
