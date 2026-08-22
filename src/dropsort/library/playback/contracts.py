from __future__ import annotations

from pathlib import Path
from typing import Protocol


class LocalMediaActions(Protocol):
    """Explicit read/access actions for one cataloged physical media file."""

    def play(self, media_path: Path) -> None: ...

    def open_folder(self, media_path: Path) -> None: ...

