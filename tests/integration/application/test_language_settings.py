from __future__ import annotations

from dropsort.application.configuration.localization import UiLanguage, UiLanguageSettings
from dropsort.database import Database, MigrationRunner
from dropsort.database.repositories.settings import SqliteUiLanguageRepository
from dropsort.application.configuration.theme import SIDEBAR_DEFAULT_WIDTH, UiSidebarSettings
from dropsort.database.repositories.settings import SqliteUiSidebarRepository


def test_ui_language_defaults_to_english_and_round_trips_through_settings(tmp_path) -> None:
    database = Database(tmp_path / "language.db")
    MigrationRunner(database).migrate()
    settings = UiLanguageSettings(SqliteUiLanguageRepository(database))

    assert settings.current_language() is UiLanguage.ENGLISH

    settings.set_language(UiLanguage.ARABIC)

    assert UiLanguageSettings(
        SqliteUiLanguageRepository(database)
    ).current_language() is UiLanguage.ARABIC


def test_invalid_persisted_language_falls_back_to_english(tmp_path) -> None:
    database = Database(tmp_path / "invalid-language.db")
    MigrationRunner(database).migrate()
    with database.connection() as connection:
        connection.execute(
            "INSERT INTO settings(key, value, updated_at) VALUES (?, ?, ?)",
            ("ui.language", "unsupported", "2026-08-15T00:00:00+00:00"),
        )

    settings = UiLanguageSettings(SqliteUiLanguageRepository(database))

    assert settings.current_language() is UiLanguage.ENGLISH


def test_sidebar_width_defaults_and_round_trips_through_settings(tmp_path) -> None:
    database = Database(tmp_path / "sidebar.db")
    MigrationRunner(database).migrate()
    settings = UiSidebarSettings(SqliteUiSidebarRepository(database))

    assert settings.current_width() == SIDEBAR_DEFAULT_WIDTH
    settings.set_width(96)

    assert UiSidebarSettings(SqliteUiSidebarRepository(database)).current_width() == 96
