from __future__ import annotations

from dataclasses import replace
import inspect

import pytest

from PySide6.QtCore import Qt
from PySide6.QtTest import QTest
from PySide6.QtWidgets import QBoxLayout, QFrame, QLabel, QLineEdit, QPushButton, QSizePolicy

from dropsort.application.configuration.localization import UiLanguage
from dropsort.application.configuration.theme import (
    SIDEBAR_DEFAULT_WIDTH,
    UiTheme,
)
from dropsort.ui.common.theme import (
    SPACE_4,
    SPACE_8,
    SPACE_12,
    SPACE_16,
    SPACE_24,
    SPACE_36,
    SPACE_48,
    application_stylesheet,
)
from dropsort.ui.main_window.window import MainWindow, NavigationButton
from dropsort.ui.movie_details.details_view import (
    MovieDetailsView,
    ResponsiveDetailsColumns,
    ResponsiveDetailsHero,
)
from dropsort.ui.settings.settings_view import SettingsView
from dropsort.ui.library.movie_card import MovieCard
from dropsort.ui.library.movie_grid import MovieGrid
from tests.unit.ui.test_phase24_ui_foundation import SettingsActions
from tests.unit.ui.test_personal_library_ui import FakePersonalActions, ImmediateRunner


def _library(movie_item_factory, movie_details_factory):
    item = movie_item_factory(title="The Wind Rises")
    details = movie_details_factory(movie_id=item.movie_id, title=item.title)
    return type(
        "Library",
        (),
        {
            "list_movies": lambda self: (item,),
            "get_movie_details": lambda self, _movie_id: details,
        },
    )()


def test_navigation_definition_is_data_driven_and_settings_is_footer_item() -> None:
    items = MainWindow.NAVIGATION_ITEMS
    assert tuple(item.item_id for item in items) == (
        "library",
        "personal",
        "import",
        "check_library",
        "settings",
    )
    assert all(
        item.text_id and item.tooltip_id and item.icon and item.destination
        for item in items
    )
    assert items[-1].placement == "footer"
    assert all(item.placement == "primary" for item in items[:-1])


def test_shell_uses_reference_widths_top_edge_content_and_bottom_settings(
    qapp, movie_item_factory, movie_details_factory
) -> None:
    window = MainWindow(
        _library(movie_item_factory, movie_details_factory),
        settings_actions=SettingsActions(),
        load_on_show=False,
    )
    assert SIDEBAR_DEFAULT_WIDTH == 272
    assert window._splitter.widget(0) is window.sidebar
    content_shell = window._splitter.widget(1)
    assert content_shell.objectName() == "contentShell"
    assert content_shell.findChild(QFrame, "appHeader") is None
    assert window.sidebar.findChild(QLabel, "brandLabel").text() == "DropSort"
    assert window.sidebar.minimumWidth() == SIDEBAR_DEFAULT_WIDTH
    assert window.sidebar.maximumWidth() == SIDEBAR_DEFAULT_WIDTH

    sidebar_layout = window.sidebar.layout()
    assert sidebar_layout.indexOf(window._sidebar_footer) == sidebar_layout.count() - 1
    assert window._settings_button.parentWidget() is window._sidebar_footer
    assert sidebar_layout.itemAt(sidebar_layout.count() - 2).spacerItem() is not None
    window.close()


def test_library_search_cannot_route_from_settings_or_details(
    qapp, movie_item_factory, movie_details_factory
) -> None:
    window = MainWindow(
        _library(movie_item_factory, movie_details_factory),
        settings_actions=SettingsActions(),
        load_on_show=False,
    )
    search = window.findChild(QLineEdit, "librarySearchInput")
    window.show_settings()
    assert search is not None and not search.isHidden()
    assert not search.isEnabled()
    search.setText("Wind")
    assert search.text() == ""
    assert window.current_section == "settings"
    assert window.library_view._search_query == ""

    search.clear()
    window.show_movie_details(1)
    assert not search.isHidden()
    assert not search.isEnabled()
    search.setText("Wind")
    assert search.text() == ""
    assert window.current_section == "details"
    assert window.library_view._search_query == ""
    window.close()


def test_sidebar_search_escape_clears_query_without_leaving_library(
    qapp, movie_item_factory, movie_details_factory
) -> None:
    window = MainWindow(
        _library(movie_item_factory, movie_details_factory), load_on_show=False
    )
    window.show()
    window._search_field.setText("Wind")
    qapp.processEvents()
    assert window._search_field.text() == "Wind"
    QTest.keyClick(window._search_field, Qt.Key.Key_Escape)
    qapp.processEvents()
    assert window._search_field.text() == ""
    assert window.current_section == "library"
    window.close()


def test_tmdb_is_one_card_with_one_guide_and_history_is_content_height(qapp) -> None:
    view = SettingsView(SettingsActions())
    tmdb = view.findChild(QFrame, "tmdbSettingsCard")
    assert tmdb is not None
    assert tmdb.findChild(QFrame, "tmdbInfoBar") is None
    assert tmdb.findChild(QFrame, "tmdbCredentialPanel") is None
    assert tmdb.findChild(QPushButton, "openTmdbButton") is None
    assert len(tmdb.findChildren(QPushButton, "tmdbSetupGuideButton")) == 1
    history = view.findChild(QFrame, "historyRecoverySettingsCard")
    assert history is not None
    assert history.sizePolicy().verticalPolicy() == QSizePolicy.Policy.Maximum


def test_movie_details_uses_single_personal_and_media_surfaces(
    qapp, movie_details_factory
) -> None:
    view = MovieDetailsView(
        personal_actions=FakePersonalActions(), personal_runner=ImmediateRunner()
    )
    view.set_movie(movie_details_factory())
    personal = view.findChild(QFrame, "personalStatePanel")
    assert personal is not None
    assert all(
        group.property("role") == "personalSection"
        for name in (
            "personalPreferenceGroup",
            "personalWatchlistGroup",
            "personalWatchingGroup",
        )
        if (group := personal.findChild(QFrame, name)) is not None
    )
    assert not any(
        child.property("role") == "panel"
        for child in personal.findChildren(QFrame)
    )
    assert view.findChild(QFrame, "detailsHero") is not None
    assert view.findChild(QFrame, "mediaFilesPanel") is not None


def test_movie_details_columns_reflow_at_desktop_breakpoint(
    qapp, movie_details_factory
) -> None:
    view = MovieDetailsView()
    view.set_movie(movie_details_factory(media_files=()))
    columns = view._details_columns
    media = view.findChild(QFrame, "mediaFilesPanel")
    columns.resize(960, 600)
    columns._reflow()
    qapp.processEvents()
    assert columns.layout().direction() == QBoxLayout.Direction.LeftToRight
    assert columns.layout().itemAt(0).widget() is view._personal_panel
    assert columns.layout().itemAt(1).widget() is media

    columns.resize(720, 600)
    columns._reflow()
    qapp.processEvents()
    assert columns.layout().direction() == QBoxLayout.Direction.TopToBottom
    assert columns.layout().itemAt(0).widget() is view._personal_panel
    assert columns.layout().itemAt(1).widget() is media


def test_movie_details_real_viewport_width_drives_desktop_columns(
    qapp, movie_details_factory
) -> None:
    view = MovieDetailsView()
    view.set_movie(movie_details_factory(media_files=()))
    view.resize(1280, 820)
    view.show()
    qapp.processEvents()

    assert view._body.width() >= 820
    assert view._details_columns.layout().direction() == QBoxLayout.Direction.LeftToRight
    hero = view.findChild(QFrame, "detailsHero")
    assert hero is not None
    assert hero.layout().direction() == QBoxLayout.Direction.LeftToRight

    view.resize(760, 820)
    qapp.processEvents()
    assert view._details_columns.layout().direction() == QBoxLayout.Direction.TopToBottom
    view.close()


def test_movie_details_is_bounded_and_long_paths_never_create_horizontal_page_scroll(
    qapp, movie_details_factory
) -> None:
    details = movie_details_factory()
    long_path = (
        "D:\\films_test\\AnimeSanka.com\\K T\\Bluray - 1080p - Ar - X265\\"
        + "very-long-folder-name-" * 10
        + "\\All.Quiet.on.the.Western.Front.2022.GERMAN.720p.NF.WEBRip.900MB.x264-GalaxyRG.mkv"
    )
    long_file = replace(details.media_files[0], current_path=long_path)
    details = replace(details, media_files=(long_file,))
    view = MovieDetailsView()
    view.set_movie(details)
    view.resize(920, 760)
    view.show()
    qapp.processEvents()

    assert view._body.maximumWidth() == 1152
    assert view._body.width() <= view._scroll.viewport().width()
    assert view._scroll.horizontalScrollBarPolicy() == Qt.ScrollBarPolicy.ScrollBarAlwaysOff
    path = view.findChild(QLabel, "mediaPathLabel")
    assert path is not None
    assert path.minimumWidth() == 0

    view.resize(1600, 900)
    qapp.processEvents()
    assert view._body.width() <= 1152
    view.close()


@pytest.mark.parametrize("width,height", ((1280, 800), (1440, 900), (1600, 1000)))
def test_fixed_sidebar_metrics_hold_at_reference_desktop_sizes(
    qapp, movie_item_factory, movie_details_factory, width, height
) -> None:
    window = MainWindow(
        _library(movie_item_factory, movie_details_factory), load_on_show=False
    )
    window.resize(width, height)
    window.show()
    qapp.processEvents()
    assert window.sidebar.width() == SIDEBAR_DEFAULT_WIDTH
    assert window._search_field.height() == 36
    assert all(button.height() == 42 for button in window._navigation_buttons.values())
    assert window._splitter.widget(1).width() == width - SIDEBAR_DEFAULT_WIDTH
    window.close()


def test_settings_cards_remain_one_content_height_column(qapp) -> None:
    view = SettingsView(SettingsActions())
    view.resize(1200, 900)
    view.show()
    qapp.processEvents()
    for index, card in enumerate(view._setting_cards):
        row, column, _row_span, _column_span = view._cards_grid.getItemPosition(
            view._cards_grid.indexOf(card)
        )
        assert row == index
        assert column == 0


def test_watch_history_remove_is_natural_width(qapp, movie_details_factory) -> None:
    view = MovieDetailsView(
        personal_actions=FakePersonalActions(), personal_runner=ImmediateRunner()
    )
    view.set_movie(movie_details_factory())
    remove_buttons = [
        button
        for button in view.findChildren(QPushButton)
        if button.objectName().startswith("removeWatchEventButton_")
    ]
    assert remove_buttons
    assert all(
        button.sizePolicy().horizontalPolicy() == QSizePolicy.Policy.Maximum
        for button in remove_buttons
    )


def test_spacing_themes_and_arabic_shell_construct(
    qapp, movie_item_factory, movie_details_factory
) -> None:
    assert (SPACE_4, SPACE_8, SPACE_12, SPACE_16, SPACE_24, SPACE_36, SPACE_48) == (
        4,
        8,
        12,
        16,
        24,
        36,
        48,
    )
    assert all(application_stylesheet(theme) for theme in UiTheme)
    window = MainWindow(
        _library(movie_item_factory, movie_details_factory),
        settings_actions=SettingsActions(),
        load_on_show=False,
    )
    window._localizer.set_language(UiLanguage.ARABIC)
    assert qapp.layoutDirection() is Qt.LayoutDirection.RightToLeft
    assert not window._search_field.isHidden()
    assert window.sidebar.minimumWidth() == SIDEBAR_DEFAULT_WIDTH
    window._localizer.set_language(UiLanguage.ENGLISH)
    window.close()


def test_navigation_and_details_do_not_mutate_layout_on_show() -> None:
    assert "showEvent" not in ResponsiveDetailsHero.__dict__
    assert "showEvent" not in ResponsiveDetailsColumns.__dict__
    assert "showEvent" not in MovieCard.__dict__

    selection_source = inspect.getsource(NavigationButton._selection_changed)
    assert "unpolish" not in selection_source
    assert ".polish(" not in selection_source

    busy_source = inspect.getsource(MovieDetailsView._set_personal_action_busy)
    assert "unpolish" not in busy_source
    assert ".polish(" not in busy_source

    saved_source = inspect.getsource(MovieDetailsView._personal_saved)
    failed_source = inspect.getsource(MovieDetailsView._personal_failed)
    assert "singleShot" not in saved_source
    assert "singleShot" not in failed_source

    header_source = inspect.getsource(MainWindow._set_header_section)
    assert "_sidebar_search_wrap.show" not in header_source
    assert "_search_field.show" not in header_source

    grid_relayout_source = inspect.getsource(MovieGrid._relayout)
    assert "takeAt(" not in grid_relayout_source
    assert "while self._layout.count()" not in grid_relayout_source

