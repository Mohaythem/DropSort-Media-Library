from __future__ import annotations

import logging
from pathlib import Path

from dropsort.application.runtime import configure_runtime_logging, resolve_runtime_paths


def test_runtime_paths_are_user_scoped_and_never_depend_on_cwd(tmp_path: Path, monkeypatch) -> None:
    cwd = tmp_path / "cwd"
    home = tmp_path / "profile"
    cwd.mkdir()
    home.mkdir()
    monkeypatch.chdir(cwd)

    paths = resolve_runtime_paths({}, home=home)

    assert paths.app_data_root == home / "AppData" / "Local" / "DropSort"
    assert paths.database_path == paths.app_data_root / "dropsort.db"
    assert paths.poster_cache_path == paths.app_data_root / "poster-cache"
    assert paths.log_directory == paths.app_data_root / "logs"
    assert cwd not in paths.database_path.parents


def test_clean_first_run_creates_only_application_owned_directories(tmp_path: Path) -> None:
    paths = resolve_runtime_paths({"LOCALAPPDATA": str(tmp_path)})

    paths.ensure_directories()

    assert paths.app_data_root.is_dir()
    assert paths.poster_cache_path.is_dir()
    assert paths.log_directory.is_dir()


def test_rotating_runtime_log_redacts_credentials(tmp_path: Path) -> None:
    log_path = configure_runtime_logging(tmp_path / "logs")
    secret = "private-secret-token-value"

    logging.getLogger("dropsort.test").error(
        "Authorization: Bearer %s",
        secret,
    )
    for handler in logging.getLogger().handlers:
        handler.flush()

    text = log_path.read_text(encoding="utf-8")
    assert secret not in text
    assert "[REDACTED]" in text


def test_runtime_logging_is_idempotent_for_one_path(tmp_path: Path) -> None:
    path = tmp_path / "logs"
    first = configure_runtime_logging(path)
    second = configure_runtime_logging(path)

    assert first == second
    matching = [
        handler
        for handler in logging.getLogger().handlers
        if getattr(handler, "baseFilename", None) == str(first.absolute())
    ]
    assert len(matching) == 1
