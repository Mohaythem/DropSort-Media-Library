from __future__ import annotations

from PySide6.QtCore import QDate
from PySide6.QtWidgets import QFrame, QLineEdit, QPushButton

from dropsort.application.configuration.theme import SIDEBAR_DEFAULT_WIDTH
from dropsort.ui.common.theme import NAVIGATION_ITEM_HEIGHT
from dropsort.ui.main_window.window import MainWindow
from dropsort.ui.movie_details.details_view import MovieDetailsView
from tests.unit.ui.test_phase24_ui_foundation import SettingsActions
from tests.unit.ui.test_personal_library_ui import FakePersonalActions


def _library(movie_item_factory, movie_details_factory):
    return type(
        "Library",
        (),
        {
            "list_movies": lambda self: (movie_item_factory(movie_id=1),),
            "get_movie_details": lambda self, _movie_id: movie_details_factory(),
        },
    )()


def test_make_shell_keeps_search_in_sidebar_and_details_owns_back(
    qapp, movie_item_factory, movie_details_factory
) -> None:
    window = MainWindow(
        _library(movie_item_factory, movie_details_factory), load_on_show=False
    )
    header = window.findChild(QFrame, "appHeader")
    search = window.findChild(QLineEdit, "librarySearchInput")
    back = window.findChild(QPushButton, "sidebarBackButton")
    pane = window.findChild(QPushButton, "sidebarPaneToggleButton")

    assert header is None
    assert search is not None and search.parentWidget() is window._sidebar_search_wrap
    assert window._sidebar_search_wrap.parentWidget() is window.sidebar
    assert back is None
    assert pane is None

    window.show_movie_details(1)
    assert not window.findChild(QPushButton, "detailsBackButton").isHidden()
    window.navigate_back()
    assert window.current_section == "library"
    window.close()


def test_make_shell_is_fixed_and_has_no_compact_search_popup(
    qapp, movie_item_factory, movie_details_factory
) -> None:
    window = MainWindow(
        _library(movie_item_factory, movie_details_factory), load_on_show=False
    )
    window.show()
    qapp.processEvents()

    nav = window.findChild(QPushButton, "libraryNavButton")
    compact_search = window.findChild(QPushButton, "sidebarSearchButton")
    assert nav is not None and nav.height() == NAVIGATION_ITEM_HEIGHT
    assert nav.text()
    assert compact_search is None
    assert window.sidebar.minimumWidth() == SIDEBAR_DEFAULT_WIDTH
    assert window.sidebar.maximumWidth() == SIDEBAR_DEFAULT_WIDTH
    window.close()


def test_phase29_date_picker_starts_on_today_with_today_selected(
    qapp, movie_details_factory
) -> None:
    view = MovieDetailsView()
    view.set_movie(movie_details_factory(media_files=()))
    today = QDate.currentDate()
    assert view._watch_date.date() == today
    assert view._watch_date.calendarWidget().selectedDate() == today
    assert view._selected_watch_date() == today

    view._watch_date.setDate(QDate(2026, 8, 16))
    assert view._selected_watch_date() == QDate(2026, 8, 16)


def test_phase29_tmdb_settings_and_check_library_surfaces_are_single_panels(
    qapp, movie_item_factory, movie_details_factory
) -> None:
    window = MainWindow(
        _library(movie_item_factory, movie_details_factory),
        settings_actions=SettingsActions(),
        load_on_show=False,
    )
    assert window.settings_view is not None
    tmdb = window.settings_view.findChild(QFrame, "tmdbSettingsCard")
    assert tmdb is not None
    assert tmdb.findChild(QFrame, "tmdbInfoBar") is None
    assert tmdb.findChild(QLineEdit, "tmdbTokenInput") is not None
    assert tmdb.findChild(QPushButton, "openTmdbButton") is None
    assert len(tmdb.findChildren(QPushButton, "tmdbSetupGuideButton")) == 1
    assert window.check_library_page.findChild(QFrame, "checkLibrarySummaryPanel") is not None
    assert window.check_library_page._issues_scroll.maximumHeight() == 220
    window.close()


def test_make_shell_state_refreshes_persistent_search_and_back_navigation(
    qapp, movie_item_factory, movie_details_factory
) -> None:
    window = MainWindow(
        _library(movie_item_factory, movie_details_factory),
        personal_actions=FakePersonalActions(),
        settings_actions=SettingsActions(),
        load_on_show=False,
    )
    window._set_search_suggestions("not a tuple")
    window.show_personal_library()
    window._set_search_suggestions(("Interstellar", 2026, "Prestige"))
    assert window._search_field.text() == ""
    assert not window._search_field.isEnabled()
    window._search_field.setText("Inter")
    assert window._search_query == ""
    assert window.current_section == "personal"

    window.show_library()
    window._set_search_suggestions(("Prestige",))
    window._search_suggestion_activated("Prestige")
    assert window._search_query == "Prestige"

    window._refresh_shell_text()
    window._set_header_section(window._localizer.language, search_visible=False)
    assert not window._search_field.isHidden()
    assert not window._search_field.isEnabled()
    window.show_check_library_from_library()
    window.navigate_back()
    assert window.current_section == "library"
    window.close()
