from __future__ import annotations

from dropsort.application.configuration.theme import UiTheme
from dropsort.ui.common import native_title_bar
from dropsort.ui.common.native_title_bar import (
    DWMWA_BORDER_COLOR,
    DWMWA_CAPTION_COLOR,
    DWMWA_TEXT_COLOR,
    DWMWA_USE_IMMERSIVE_DARK_MODE,
    apply_native_title_bar,
    native_title_bar_palette,
)


class WindowHandle:
    def winId(self) -> int:
        return 4242


def test_native_title_bar_palettes_follow_all_selectable_themes() -> None:
    slate = native_title_bar_palette(UiTheme.SLATE)
    dark = native_title_bar_palette(UiTheme.DARK)
    light = native_title_bar_palette(UiTheme.LIGHT)

    assert slate.dark is True
    assert dark.dark is True
    assert light.dark is False
    assert len({slate.caption, dark.caption, light.caption}) == 3
    assert len({slate.text, dark.text, light.text}) == 3
    assert native_title_bar_palette(UiTheme.MAIN) == slate
    assert native_title_bar_palette("unsupported") == slate


def test_inactive_native_border_retains_themed_caption_and_text() -> None:
    active = native_title_bar_palette(UiTheme.SLATE, active=True)
    inactive = native_title_bar_palette(UiTheme.SLATE, active=False)

    assert inactive.caption == active.caption
    assert inactive.text == active.text
    assert inactive.border != active.border


def test_native_title_bar_applies_supported_dwm_attributes_individually(
    monkeypatch,
) -> None:
    calls: list[tuple[int, int, int]] = []
    monkeypatch.setattr(native_title_bar, "_native_title_bar_supported", lambda: True)
    monkeypatch.setattr(
        native_title_bar,
        "_set_dwm_attribute",
        lambda hwnd, attribute, value: calls.append((hwnd, attribute, value))
        or attribute != DWMWA_TEXT_COLOR,
    )

    applied = apply_native_title_bar(WindowHandle(), UiTheme.DARK)

    assert [attribute for _hwnd, attribute, _value in calls] == [
        DWMWA_USE_IMMERSIVE_DARK_MODE,
        DWMWA_CAPTION_COLOR,
        DWMWA_TEXT_COLOR,
        DWMWA_BORDER_COLOR,
    ]
    assert applied == (
        DWMWA_USE_IMMERSIVE_DARK_MODE,
        DWMWA_CAPTION_COLOR,
        DWMWA_BORDER_COLOR,
    )
    assert {hwnd for hwnd, _attribute, _value in calls} == {4242}
