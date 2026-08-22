from __future__ import annotations

from dataclasses import dataclass
import os
from pathlib import Path
from typing import Mapping


@dataclass(frozen=True, slots=True)
class RuntimePaths:
    app_data_root: Path
    database_path: Path
    poster_cache_path: Path
    log_directory: Path

    def ensure_directories(self) -> None:
        self.app_data_root.mkdir(parents=True, exist_ok=True)
        self.poster_cache_path.mkdir(parents=True, exist_ok=True)
        self.log_directory.mkdir(parents=True, exist_ok=True)


def resolve_runtime_paths(
    environment: Mapping[str, str] | None = None,
    *,
    home: Path | None = None,
) -> RuntimePaths:
    values = os.environ if environment is None else environment
    local_app_data = values.get("LOCALAPPDATA", "").strip()
    if local_app_data:
        local_root = Path(local_app_data).expanduser().absolute()
    else:
        user_home = (home or Path.home()).expanduser().absolute()
        local_root = user_home / "AppData" / "Local"
    app_root = local_root / "DropSort"
    return RuntimePaths(
        app_data_root=app_root,
        database_path=app_root / "dropsort.db",
        poster_cache_path=app_root / "poster-cache",
        log_directory=app_root / "logs",
    )
