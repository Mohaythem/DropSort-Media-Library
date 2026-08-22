from __future__ import annotations

from pathlib import Path
from collections.abc import Callable
from typing import Protocol

from dropsort.media.discovery.models import DiscoveredMedia, DiscoveryProgress


class DiscoveryCancellation(Protocol):
    def is_cancelled(self) -> bool: ...


DiscoveryProgressCallback = Callable[[DiscoveryProgress], None]


class MediaDiscoveryScanner(Protocol):
    def scan(
        self,
        root: Path,
        *,
        recursive: bool = True,
        progress: DiscoveryProgressCallback | None = None,
        cancellation: DiscoveryCancellation | None = None,
    ) -> tuple[DiscoveredMedia, ...]: ...
