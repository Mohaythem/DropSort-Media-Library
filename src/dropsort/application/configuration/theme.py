from __future__ import annotations

from enum import StrEnum
from typing import Protocol


class UiTheme(StrEnum):
    """Stable theme identifiers, including the retired Main compatibility id.

    The legacy names remain enum aliases so callers compiled against earlier
    releases continue to work.  Persisted legacy values are migrated by
    :class:`UiThemeSettings` below rather than being exposed to users.
    """

    MAIN = "main"
    DARK = "dark"
    SLATE = "slate"
    LIGHT = "light"

    # Compatibility aliases for settings/database values from earlier V1 builds.
    DEEP_INK = "main"
    CHARCOAL = "dark"
    LIGHT_BLUE = "light"


LEGACY_THEME_IDS = {
    "main": UiTheme.SLATE,
    "deep_ink": UiTheme.SLATE,
    "charcoal": UiTheme.DARK,
    "light_blue": UiTheme.LIGHT,
}

SELECTABLE_THEMES = (UiTheme.SLATE, UiTheme.DARK, UiTheme.LIGHT)


SIDEBAR_DEFAULT_WIDTH = 272
SIDEBAR_MIN_WIDTH = 56
SIDEBAR_MAX_WIDTH = 360


class UiSidebarRepository(Protocol):
    def get_width(self) -> int | None: ...

    def set_width(self, width: int) -> None: ...


class UiSidebarSettings:
    """Persist the user's navigation width; compact mode derives from it."""

    def __init__(self, repository: UiSidebarRepository) -> None:
        self._repository = repository

    def current_width(self) -> int:
        value = self._repository.get_width()
        if (
            isinstance(value, bool)
            or not isinstance(value, int)
            or not SIDEBAR_MIN_WIDTH <= value <= SIDEBAR_MAX_WIDTH
        ):
            return SIDEBAR_DEFAULT_WIDTH
        return value

    def set_width(self, width: int) -> int:
        if (
            isinstance(width, bool)
            or not isinstance(width, int)
            or not SIDEBAR_MIN_WIDTH <= width <= SIDEBAR_MAX_WIDTH
        ):
            raise ValueError("sidebar width is outside the supported range")
        self._repository.set_width(width)
        return width


class UiThemeRepository(Protocol):
    def get_theme(self) -> str | None: ...

    def set_theme(self, theme: str) -> None: ...


class UiThemeSettings:
    def __init__(self, repository: UiThemeRepository) -> None:
        self._repository = repository

    def current_theme(self) -> UiTheme:
        value = self._repository.get_theme()
        if value in LEGACY_THEME_IDS:
            migrated = LEGACY_THEME_IDS[value]
            # Persist the safe equivalent when possible.  A read failure must
            # not prevent the UI from using the migrated theme in memory.
            try:
                self._repository.set_theme(migrated.value)
            except Exception:
                pass
            return migrated
        try:
            theme = UiTheme(value) if value is not None else UiTheme.SLATE
            return UiTheme.SLATE if theme is UiTheme.MAIN else theme
        except ValueError:
            return UiTheme.SLATE

    def set_theme(self, theme: UiTheme) -> UiTheme:
        if not isinstance(theme, UiTheme):
            raise ValueError("theme must be a supported UI theme")
        if theme is UiTheme.MAIN:
            theme = UiTheme.SLATE
        self._repository.set_theme(theme.value)
        return theme
