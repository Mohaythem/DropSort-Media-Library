from __future__ import annotations

from datetime import datetime, timezone
import sqlite3

from dropsort.database.connection.sqlite import Database


UI_LANGUAGE_KEY = "ui.language"
UI_THEME_KEY = "ui.theme"
UI_SIDEBAR_WIDTH_KEY = "ui.sidebar_width"


class SqliteUiLanguageRepository:
    """SQLite adapter for the single persisted presentation preference."""

    def __init__(self, database: Database) -> None:
        self._database = database

    def get_language(self) -> str | None:
        try:
            with self._database.connection() as connection:
                row = connection.execute(
                    "SELECT value FROM settings WHERE key = ?",
                    (UI_LANGUAGE_KEY,),
                ).fetchone()
        except sqlite3.Error:
            return None
        return None if row is None else str(row["value"])

    def set_language(self, language: str) -> None:
        if not isinstance(language, str) or not language:
            raise ValueError("language must be non-empty text")
        updated_at = datetime.now(timezone.utc).isoformat()
        with self._database.transaction() as connection:
            connection.execute(
                """
                INSERT INTO settings(key, value, updated_at)
                VALUES (?, ?, ?)
                ON CONFLICT(key) DO UPDATE SET
                    value = excluded.value,
                    updated_at = excluded.updated_at
                """,
                (UI_LANGUAGE_KEY, language, updated_at),
            )


class SqliteUiThemeRepository:
    """SQLite adapter for the persisted application theme preference."""

    def __init__(self, database: Database) -> None:
        self._database = database

    def get_theme(self) -> str | None:
        try:
            with self._database.connection() as connection:
                row = connection.execute(
                    "SELECT value FROM settings WHERE key = ?", (UI_THEME_KEY,)
                ).fetchone()
        except sqlite3.Error:
            return None
        return None if row is None else str(row["value"])

    def set_theme(self, theme: str) -> None:
        if not isinstance(theme, str) or not theme:
            raise ValueError("theme must be non-empty text")
        updated_at = datetime.now(timezone.utc).isoformat()
        with self._database.transaction() as connection:
            connection.execute(
                """
                INSERT INTO settings(key, value, updated_at)
                VALUES (?, ?, ?)
                ON CONFLICT(key) DO UPDATE SET
                    value = excluded.value,
                    updated_at = excluded.updated_at
                """,
                (UI_THEME_KEY, theme, updated_at),
            )


class SqliteUiSidebarRepository:
    """SQLite adapter for the persisted navigation width preference."""

    def __init__(self, database: Database) -> None:
        self._database = database

    def get_width(self) -> int | None:
        try:
            with self._database.connection() as connection:
                row = connection.execute(
                    "SELECT value FROM settings WHERE key = ?",
                    (UI_SIDEBAR_WIDTH_KEY,),
                ).fetchone()
        except sqlite3.Error:
            return None
        if row is None:
            return None
        try:
            return int(row["value"])
        except (TypeError, ValueError):
            return None

    def set_width(self, width: int) -> None:
        if isinstance(width, bool) or not isinstance(width, int):
            raise ValueError("sidebar width must be an integer")
        updated_at = datetime.now(timezone.utc).isoformat()
        with self._database.transaction() as connection:
            connection.execute(
                """
                INSERT INTO settings(key, value, updated_at)
                VALUES (?, ?, ?)
                ON CONFLICT(key) DO UPDATE SET
                    value = excluded.value,
                    updated_at = excluded.updated_at
                """,
                (UI_SIDEBAR_WIDTH_KEY, str(width), updated_at),
            )
