from __future__ import annotations

from pathlib import Path

from dropsort.media.discovery.models import DiscoveryErrorCode, DiscoveryProgress


class DiscoveryCancelled(Exception):
    """Cooperative cancellation observed before discovery completed."""

    def __init__(self, progress: DiscoveryProgress) -> None:
        self.progress = progress
        super().__init__("media discovery was cancelled")


class DiscoveryRootError(Exception):
    """The selected scan root cannot be read safely."""

    def __init__(self, path: Path, code: DiscoveryErrorCode, message: str) -> None:
        self.path = path
        self.code = code
        super().__init__(message)
