from __future__ import annotations

from PySide6.QtCore import Qt
from PySide6.QtWidgets import QFrame, QPushButton

from dropsort.application.configuration.theme import SIDEBAR_DEFAULT_WIDTH
from dropsort.ui.main_window.window import MainWindow
from tests.unit.ui.test_sidebar import SettingsActions, _library


def _window(movie_item_factory, movie_details_factory) -> MainWindow:
    return MainWindow(
        _library(movie_item_factory, movie_details_factory),
        settings_actions=SettingsActions(),
        load_on_show=False,
    )


def test_expanded_sidebar_matches_prototype_structure_and_spacing(
    qapp, movie_item_factory, movie_details_factory
) -> None:
    window = _window(movie_item_factory, movie_details_factory)
    window._set_navigation_checked("library")

    assert window.sidebar.layout().getContentsMargins() == (8, 12, 8, 12)
    assert window._sidebar_top_row.height() == 42
    assert window._sidebar_top_row.layout().getContentsMargins() == (12, 0, 12, 0)
    assert window.findChild(QFrame, "sidebarPaneToggleRow") is None
    assert window._sidebar_search_wrap.layout().getContentsMargins() == (12, 0, 12, 0)
    assert window._sidebar_primary_navigation.layout().getContentsMargins() == (0, 0, 0, 0)
    assert window._sidebar_primary_navigation.layout().spacing() == 4
    assert window._sidebar_footer.layout().getContentsMargins() == (0, 0, 0, 0)
    assert window._settings_button.parentWidget() is window._sidebar_footer
    assert window._sidebar_search_wrap.isHidden() is False

    library = window.findChild(QPushButton, "libraryNavButton")
    assert library is not None
    assert library.property("role") == "navigationItem"
    assert library.isChecked()
    accent = library.findChild(QFrame, "navigationAccent")
    assert accent is not None and not accent.isHidden()
    assert (accent.width(), accent.height()) == (3, 24)
    window.setLayoutDirection(Qt.LayoutDirection.RightToLeft)
    qapp.processEvents()
    assert accent.x() == library.width() - accent.width()
    window.close()


def test_shell_has_no_compact_mode_or_collapse_controls(
    qapp, movie_item_factory, movie_details_factory
) -> None:
    window = _window(movie_item_factory, movie_details_factory)
    assert window.sidebar.minimumWidth() == SIDEBAR_DEFAULT_WIDTH
    assert window.sidebar.maximumWidth() == SIDEBAR_DEFAULT_WIDTH
    assert window.findChild(QPushButton, "sidebarPaneToggleButton") is None
    assert window.findChild(QPushButton, "sidebarSearchButton") is None
    assert window.findChild(QFrame, "compactSearchPopup") is None
    for button in window._navigation_buttons.values():
        assert button.height() == 42
        assert button.text()
    window.close()


def test_selected_navigation_style_overrides_generic_checked_border() -> None:
    from dropsort.ui.common.theme import application_stylesheet

    stylesheet = application_stylesheet()
    selector = 'QFrame#sidebar QPushButton[role="navigationItem"]:checked'
    assert selector in stylesheet
    assert 'QFrame#sidebar[compact="true"]' not in stylesheet
    assert "QFrame#navigationAccent" in stylesheet
    assert 'QFrame#sidebar QPushButton[role="navigationItem"]:rtl' in stylesheet
    selected_rule = stylesheet.split(selector, 1)[1].split("}", 1)[0]
    assert "border: none" in selected_rule
    assert "border: 1px solid" not in selected_rule
