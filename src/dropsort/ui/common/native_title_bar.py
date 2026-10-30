from __future__ import annotations

import ctypes
from dataclasses import dataclass
import sys

from PySide6.QtWidgets import QApplication, QWidget

from dropsort.application.configuration.theme import UiTheme
from dropsort.ui.common.theme import THEMES


DWMWA_USE_IMMERSIVE_DARK_MODE = 20
DWMWA_BORDER_COLOR = 34
DWMWA_CAPTION_COLOR = 35
DWMWA_TEXT_COLOR = 36


@dataclass(frozen=True, slots=True)
class NativeTitleBarPalette:
    dark: bool
    caption: int
    text: int
    border: int


def native_title_bar_palette(
    theme: UiTheme | str,
    *,
    active: bool = True,
) -> NativeTitleBarPalette:
    """Map semantic DropSort colors to Win32 COLORREF values."""

    try:
        selected = UiTheme(theme)
    except (TypeError, ValueError):
        selected = UiTheme.SLATE
    if selected is UiTheme.MAIN:
        selected = UiTheme.SLATE
    colors = THEMES[selected]
    return NativeTitleBarPalette(
        dark=selected is not UiTheme.LIGHT,
        caption=_colorref(colors.surface),
        text=_colorref(colors.text),
        border=_colorref(colors.border if active else colors.surface),
    )


def apply_native_title_bar(
    window: QWidget,
    theme: UiTheme | str,
    *,
    active: bool = True,
) -> tuple[int, ...]:
    """Theme the native Windows frame without replacing its caption controls.

    Unsupported DWM attributes are ignored individually so older Windows
    versions retain the native system frame and behavior.
    """

    if not _native_title_bar_supported():
        return ()
    try:
        hwnd = int(window.winId())
    except (RuntimeError, TypeError, ValueError):
        return ()
    if not hwnd:
        return ()

    palette = native_title_bar_palette(theme, active=active)
    attributes = (
        (DWMWA_USE_IMMERSIVE_DARK_MODE, int(palette.dark)),
        (DWMWA_CAPTION_COLOR, palette.caption),
        (DWMWA_TEXT_COLOR, palette.text),
        (DWMWA_BORDER_COLOR, palette.border),
    )
    applied: list[int] = []
    for attribute, value in attributes:
        if _set_dwm_attribute(hwnd, attribute, value):
            applied.append(attribute)
    return tuple(applied)


def _set_dwm_attribute(hwnd: int, attribute: int, value: int) -> bool:
    try:
        from ctypes import wintypes

        dwmapi = ctypes.WinDLL("dwmapi")
        setter = dwmapi.DwmSetWindowAttribute
        setter.argtypes = (
            wintypes.HWND,
            wintypes.DWORD,
            ctypes.c_void_p,
            wintypes.DWORD,
        )
        setter.restype = ctypes.c_long
        native_value = ctypes.c_int(value)
        result = setter(
            wintypes.HWND(hwnd),
            wintypes.DWORD(attribute),
            ctypes.byref(native_value),
            ctypes.sizeof(native_value),
        )
    except (AttributeError, OSError, TypeError, ValueError):
        return False
    return result == 0


def _native_title_bar_supported() -> bool:
    return sys.platform == "win32" and QApplication.platformName() != "offscreen"


def _colorref(hex_color: str) -> int:
    value = hex_color.removeprefix("#")
    if len(value) != 6:
        raise ValueError("title-bar colors must use six-digit RGB hex values")
    red = int(value[0:2], 16)
    green = int(value[2:4], 16)
    blue = int(value[4:6], 16)
    return red | (green << 8) | (blue << 16)
