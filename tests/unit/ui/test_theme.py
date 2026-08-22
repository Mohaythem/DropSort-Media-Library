from __future__ import annotations

from PySide6.QtWidgets import QApplication

from pathlib import Path

from dropsort.ui.common.theme import (
    BODY_SIZE,
    BODY_WEIGHT,
    COLORS,
    CONTROL_HEIGHT,
    FONT_FAMILY,
    H1_SIZE,
    H2_SIZE,
    H3_SIZE,
    H4_SIZE,
    H5_SIZE,
    HEADING_WEIGHT,
    ICON_SIZE,
    ICON_TEXT_GAP,
    INTER_BOLD_FONT_PATH,
    INTER_REGULAR_FONT_PATH,
    NOTO_SANS_ARABIC_BOLD_FONT_PATH,
    NOTO_SANS_ARABIC_REGULAR_FONT_PATH,
    SMALL_SIZE,
    NAVIGATION_ITEM_HEIGHT,
    application_stylesheet,
    apply_theme,
    register_application_fonts,
    ThemeId,
    THEMES,
)


def test_official_drop_sort_palette_is_the_single_theme_source() -> None:
    assert COLORS.text == "#FFF1A6"
    assert COLORS.background == "#0B1E1B"
    assert COLORS.primary == "#013C35"
    assert COLORS.secondary == "#6B352A"
    assert COLORS.accent == "#E87454"

    stylesheet = application_stylesheet()
    for color in COLORS:
        assert color in stylesheet
    assert "#ffeeb3" not in stylesheet
    assert "#c9973c" not in stylesheet


def test_apply_theme_configures_application(qapp: QApplication) -> None:
    apply_theme(qapp)

    assert qapp.styleSheet() == application_stylesheet()
    assert qapp.property("dropsortBaseStyle") == "Fusion"
    assert qapp.property("dropsortFontFamily") in {"Inter", "Segoe UI"}
    assert qapp.property("dropsortArabicFontFamily") in {"Noto Sans Arabic", "Segoe UI"}


def test_invalid_theme_identifier_falls_back_to_main(qapp: QApplication) -> None:
    assert application_stylesheet("not-a-theme") == application_stylesheet(ThemeId.MAIN)
    apply_theme(qapp, "not-a-theme")
    assert qapp.property("dropsortTheme") == ThemeId.MAIN.value


def test_exactly_four_user_facing_themes_are_available() -> None:
    assert tuple(THEMES) == (
        ThemeId.MAIN,
        ThemeId.DARK,
        ThemeId.SLATE,
        ThemeId.LIGHT,
    )
    assert THEMES[ThemeId.DARK].background == "#1C1D1F"
    assert THEMES[ThemeId.DARK].surface == "#232528"
    assert THEMES[ThemeId.DARK].primary == "#D9A441"
    assert THEMES[ThemeId.DARK].accent == "#C86A4A"
    assert THEMES[ThemeId.SLATE].background == "#18212B"
    assert THEMES[ThemeId.SLATE].surface == "#202C38"
    assert THEMES[ThemeId.SLATE].primary == "#B3C9DD"
    assert THEMES[ThemeId.SLATE].accent == "#D97757"
    assert THEMES[ThemeId.SLATE].text == "#E3E2E3"
    assert THEMES[ThemeId.SLATE].background != THEMES[ThemeId.DARK].background
    assert THEMES[ThemeId.SLATE].primary != THEMES[ThemeId.DARK].primary
    assert THEMES[ThemeId.LIGHT].background == "#F4F1EA"
    assert THEMES[ThemeId.LIGHT].surface == "#FBF9F4"
    assert THEMES[ThemeId.LIGHT].background != "#FFFFFF"
    assert THEMES[ThemeId.LIGHT].accent == "#D97757"
    assert THEMES[ThemeId.LIGHT].sidebar != THEMES[ThemeId.LIGHT].primary
    assert THEMES[ThemeId.LIGHT].sidebar == "#EEEAE3"
    stylesheet = application_stylesheet(ThemeId.LIGHT)
    assert "#F4F1EA" in stylesheet
    assert "QCalendarWidget QWidget#qt_calendar_navigationbar" in stylesheet
    assert "background: transparent;" in stylesheet.split("QCalendarWidget QToolButton", 1)[1].split("}", 1)[0]


def test_all_theme_roles_are_emitted_as_shared_semantic_tokens() -> None:
    for theme_id, tokens in THEMES.items():
        stylesheet = application_stylesheet(theme_id)
        for token in tokens:
            assert token in stylesheet


def test_selected_action_foregrounds_use_readable_theme_text() -> None:
    stylesheet = application_stylesheet(ThemeId.MAIN)
    assert 'QPushButton[role="primaryAction"]' in stylesheet
    assert f"color: {THEMES[ThemeId.MAIN].text};" in stylesheet
    assert f"color: {THEMES[ThemeId.MAIN].primary};" not in stylesheet.split(
        'QPushButton[role="primaryAction"]', 1
    )[1].split("}", 1)[0]


def test_typography_tokens_define_the_authoritative_inter_scale() -> None:
    assert FONT_FAMILY == "Inter"
    assert BODY_WEIGHT == 400
    assert HEADING_WEIGHT == 600
    assert BODY_SIZE == 14
    assert H1_SIZE == 28
    assert H2_SIZE == 20
    assert H3_SIZE == 20
    assert H4_SIZE == 16
    assert H5_SIZE == 14
    assert SMALL_SIZE == 12
    assert CONTROL_HEIGHT == 36
    assert NAVIGATION_ITEM_HEIGHT == 42
    assert ICON_SIZE == 16
    assert ICON_TEXT_GAP == 8

    stylesheet = application_stylesheet()
    for size in (BODY_SIZE, H1_SIZE, H2_SIZE, H3_SIZE, H4_SIZE, H5_SIZE, SMALL_SIZE):
        assert f"{size:g}px" in stylesheet


def test_bundled_inter_and_arabic_fonts_register_with_regular_and_bold_roles(
    qapp: QApplication,
    tmp_path: Path,
) -> None:
    assert INTER_REGULAR_FONT_PATH.is_file()
    assert INTER_BOLD_FONT_PATH.is_file()
    assert NOTO_SANS_ARABIC_REGULAR_FONT_PATH.is_file()
    assert NOTO_SANS_ARABIC_BOLD_FONT_PATH.is_file()
    assert register_application_fonts() is True
    assert register_application_fonts((tmp_path / "missing-font.ttf",)) is False
