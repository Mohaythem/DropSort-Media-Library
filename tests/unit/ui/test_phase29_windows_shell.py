from __future__ import annotations

from PySide6.QtCore import QDate, Qt
from PySide6.QtWidgets import QFrame, QLabel, QLineEdit, QPushButton, QWidget

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


def test_make_shell_removes_sidebar_search_and_details_owns_back(
    qapp, movie_item_factory, movie_details_factory
) -> None:
    window = MainWindow(
        _library(movie_item_factory, movie_details_factory), load_on_show=False
    )
    header = window.findChild(QFrame, "appHeader")
    search = window.findChild(QLineEdit, "libraryPageSearchInput")
    back = window.findChild(QPushButton, "sidebarBackButton")
    pane = window.findChild(QPushButton, "sidebarPaneToggleButton")

    assert header is None
    assert window.findChild(QLineEdit, "librarySearchInput") is None
    assert not hasattr(window, "_sidebar_search_wrap")
    assert search is not None and search.parentWidget() is window.library_view
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


def test_native_windows_frame_and_full_width_wordmark_are_stable(
    qapp, movie_item_factory, movie_details_factory
) -> None:
    window = MainWindow(
        _library(movie_item_factory, movie_details_factory), load_on_show=False
    )
    window.show()
    qapp.processEvents()
    chrome = window.findChild(QWidget, "customTitleBar")
    assert chrome is None
    assert not window.windowFlags() & Qt.WindowType.FramelessWindowHint
    assert window.windowFlags() & Qt.WindowType.WindowSystemMenuHint
    assert window.windowFlags() & Qt.WindowType.WindowMinimizeButtonHint
    assert window.windowFlags() & Qt.WindowType.WindowMaximizeButtonHint
    assert window.windowFlags() & Qt.WindowType.WindowCloseButtonHint
    brand = window.findChild(QLabel, "brandLabel")
    assert brand is not None and brand.text() == "DropSort"
    assert brand.alignment() & Qt.AlignmentFlag.AlignHCenter
    assert window.sidebar.findChildren(QLabel, "brandLabel") == [brand]
    brand_center = brand.mapTo(window.sidebar, brand.rect().center()).x()
    assert abs(brand_center - window.sidebar.rect().center().x()) <= 1
    assert window._sidebar_top_row.height() > NAVIGATION_ITEM_HEIGHT
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


def test_page_search_state_is_independent_and_back_navigation_is_preserved(
    qapp, movie_item_factory, movie_details_factory
) -> None:
    window = MainWindow(
        _library(movie_item_factory, movie_details_factory),
        personal_actions=FakePersonalActions(),
        settings_actions=SettingsActions(),
        load_on_show=False,
    )
    assert window.findChild(QLineEdit, "librarySearchInput") is None
    library_search = window.library_view._search
    assert window.personal_view is not None
    personal_search = window.personal_view._search
    window.show_personal_library()
    personal_search.setText("Inter")
    assert personal_search.text() == "Inter"
    assert library_search.text() == ""
    assert window.current_section == "personal"

    window.show_library()
    library_search.setText("Prestige")
    assert window.library_view._search_query == "Prestige"
    assert personal_search.text() == "Inter"
    window.show_check_library_from_library()
    assert window._stack.currentWidget() is window.check_library_page
    window.navigate_back()
    assert window.current_section == "library"
    assert library_search.text() == "Prestige"
    window.close()
