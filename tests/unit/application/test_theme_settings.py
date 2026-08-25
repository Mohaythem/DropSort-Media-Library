from __future__ import annotations

from dropsort.application.configuration.theme import (
    SIDEBAR_DEFAULT_WIDTH,
    SIDEBAR_MIN_WIDTH,
    UiSidebarSettings,
    UiTheme,
    UiThemeSettings,
)


class Repository:
    def __init__(self, value=None):
        self.value = value
        self.writes = []

    def get_theme(self):
        return self.value

    def set_theme(self, theme):
        self.writes.append(theme)
        self.value = theme


def test_theme_settings_persists_supported_value() -> None:
    repository = Repository()
    settings = UiThemeSettings(repository)
    assert settings.current_theme() is UiTheme.SLATE
    assert settings.set_theme(UiTheme.DARK) is UiTheme.DARK
    assert repository.writes == ["dark"]


def test_invalid_persisted_theme_falls_back_without_resetting_value() -> None:
    repository = Repository("invalid")
    assert UiThemeSettings(repository).current_theme() is UiTheme.SLATE


def test_legacy_theme_id_migrates_without_resetting_user_choice() -> None:
    repository = Repository("charcoal")

    assert UiThemeSettings(repository).current_theme() is UiTheme.DARK
    assert repository.value == "dark"
    assert repository.writes == ["dark"]


class SidebarRepository:
    def __init__(self, value=None):
        self.value = value
        self.writes = []

    def get_width(self):
        return self.value

    def set_width(self, width):
        self.value = width
        self.writes.append(width)


def test_sidebar_settings_validate_and_fallback() -> None:
    repository = SidebarRepository(150)
    settings = UiSidebarSettings(repository)
    assert settings.current_width() == 150
    assert settings.set_width(SIDEBAR_MIN_WIDTH) == SIDEBAR_MIN_WIDTH
    assert repository.writes == [SIDEBAR_MIN_WIDTH]
    assert UiSidebarSettings(SidebarRepository(-1)).current_width() == SIDEBAR_DEFAULT_WIDTH


def test_sidebar_settings_rejects_non_integer_and_out_of_range_values() -> None:
    settings = UiSidebarSettings(SidebarRepository())
    for value in (True, SIDEBAR_MIN_WIDTH - 1, 361, "272"):
        try:
            settings.set_width(value)
        except ValueError:
            pass
        else:
            raise AssertionError("invalid sidebar width was accepted")


def test_theme_settings_falls_back_when_legacy_write_fails() -> None:
    class FailingRepository(Repository):
        def set_theme(self, theme):
            raise RuntimeError("read-only settings store")

    assert UiThemeSettings(FailingRepository("deep_ink")).current_theme() is UiTheme.SLATE


def test_retired_main_theme_migrates_to_slate() -> None:
    repository = Repository("main")

    assert UiThemeSettings(repository).current_theme() is UiTheme.SLATE
    assert repository.value == "slate"


def test_theme_settings_rejects_non_theme_values() -> None:
    try:
        UiThemeSettings(Repository()).set_theme("dark")
    except ValueError:
        pass
    else:
        raise AssertionError("invalid theme was accepted")
