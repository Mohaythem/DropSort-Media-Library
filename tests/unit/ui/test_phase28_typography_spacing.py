from __future__ import annotations

from pathlib import Path

from PySide6.QtCore import QSize, Qt
from PySide6.QtWidgets import QLineEdit, QPushButton

from dropsort.application.configuration.localization import UiLanguage
from dropsort.ui.common.theme import (
    BODY_SIZE,
    CONTROL_HEIGHT,
    H4_SIZE,
    H5_SIZE,
    HEADING_WEIGHT,
    ICON_SIZE,
    ICON_TEXT_GAP,
    NAVIGATION_ITEM_HEIGHT,
    PAGE_TITLE_SIZE,
    SCREEN_HEADING_SIZE,
    SECTION_HEADING_SIZE,
    SMALL_SIZE,
    ThemeId,
    application_stylesheet,
    apply_theme,
)
from dropsort.ui.common.icon import FluentIconName
from dropsort.ui.main_window.window import MainWindow
from dropsort.ui.localization import UiLocalizer


def _library(movie_item_factory, movie_details_factory):
    return type(
        "Library",
        (),
        {
            "list_movies": lambda self: (movie_item_factory(),),
            "get_movie_details": lambda self, _movie_id: movie_details_factory(),
        },
    )()


def test_phase28_tokens_define_compact_desktop_hierarchy() -> None:
    assert BODY_SIZE == 14
    assert HEADING_WEIGHT == 600
    assert PAGE_TITLE_SIZE == 24
    assert SCREEN_HEADING_SIZE == 24
    assert SECTION_HEADING_SIZE == 16
    assert H4_SIZE == 16
    assert H5_SIZE == 14
    assert SMALL_SIZE == 12
    assert CONTROL_HEIGHT == 36
    assert NAVIGATION_ITEM_HEIGHT == 42
    assert ICON_SIZE == 16
    assert ICON_TEXT_GAP == 8


def test_phase28_stylesheet_exposes_shared_roles_and_metrics() -> None:
    stylesheet = application_stylesheet(ThemeId.MAIN)
    assert 'QLabel[role="primary"]' in stylesheet
    assert 'QLabel[role="secondary"]' in stylesheet
    assert 'QLabel[role="tertiary"]' in stylesheet
    assert "min-height: 36px" in stylesheet
    assert "min-height: 42px" in stylesheet
    assert "border-radius: 4px" in stylesheet
    assert "border-radius: 8px" in stylesheet


def test_phase28_sidebar_and_search_keep_shared_hit_targets(
    qapp, movie_item_factory, movie_details_factory
) -> None:
    apply_theme(qapp, ThemeId.MAIN)
    window = MainWindow(
        _library(movie_item_factory, movie_details_factory),
        load_on_show=False,
    )
    nav = window.findChild(QPushButton, "checkLibraryNavButton")
    search = window.findChild(QLineEdit, "librarySearchInput")

    assert nav is not None
    assert nav.minimumHeight() == NAVIGATION_ITEM_HEIGHT
    assert nav.iconSize() == QSize(ICON_SIZE, ICON_SIZE)
    assert nav.property("dropsortIconName") == FluentIconName.CHECK_LIBRARY.value
    assert search is not None
    assert search.minimumHeight() == CONTROL_HEIGHT
    window.close()


def test_phase28_arabic_sidebar_preserves_rtl_and_hit_targets(
    qapp, movie_item_factory, movie_details_factory
) -> None:
    localizer = UiLocalizer(UiLanguage.ARABIC)
    window = MainWindow(
        _library(movie_item_factory, movie_details_factory),
        localizer=localizer,
        load_on_show=False,
    )
    nav = window.findChild(QPushButton, "checkLibraryNavButton")

    assert qapp.layoutDirection() is Qt.LayoutDirection.RightToLeft
    assert nav is not None
    assert nav.text() != "Check Library"
    assert nav.minimumHeight() == NAVIGATION_ITEM_HEIGHT
    assert nav.iconSize() == QSize(ICON_SIZE, ICON_SIZE)
    window.close()
    localizer.set_language(UiLanguage.ENGLISH)


def test_phase28_removes_migrated_unicode_icon_fallbacks() -> None:
    root = Path(__file__).parents[3] / "src" / "dropsort" / "ui"
    for relative in (
        "personal_library/personal_library_view.py",
        "scan/import_review_row.py",
        "movie_details/details_view.py",
        "localization.py",
    ):
        source = (root / relative).read_text(encoding="utf-8")
        assert all(symbol not in source for symbol in ("✦", "←", "▦", "×"))
