from __future__ import annotations

from dataclasses import dataclass

from PySide6.QtCore import Qt
from PySide6.QtTest import QTest
from PySide6.QtWidgets import QLabel, QLineEdit, QPushButton

from dropsort.application.configuration.localization import UiLanguage
from dropsort.application.configuration.metadata_credentials import (
    MetadataCredentialOrigin,
    MetadataCredentialStatus,
)
from dropsort.application.configuration.theme import UiTheme
from dropsort.ui.common.rating import provider_rating_stars
from dropsort.ui.library.library_view import LibraryView
from dropsort.ui.library.movie_card import MovieCard
from dropsort.ui.main_window.window import MainWindow
from dropsort.ui.movie_details.details_view import MovieDetailsView
from dropsort.ui.settings.settings_view import SettingsView


@dataclass
class LocalActions:
    items: tuple
    calls: int = 0

    def list_movies(self):
        self.calls += 1
        return self.items

    def get_movie_details(self, movie_id):
        raise AssertionError(f"details should not be requested by search: {movie_id}")


class SettingsActions:
    def __init__(self) -> None:
        self.language = UiLanguage.ENGLISH
        self.theme = UiTheme.MAIN

    def metadata_credential_status(self):
        return MetadataCredentialStatus(False, MetadataCredentialOrigin.NOT_CONFIGURED)

    def apply_tmdb_session_token(self, _token):
        raise AssertionError

    def clear_tmdb_session_token(self):
        raise AssertionError

    def current_ui_language(self):
        return self.language

    def set_ui_language(self, language):
        self.language = language
        return language

    def current_ui_theme(self):
        return self.theme

    def set_ui_theme(self, theme):
        self.theme = theme
        return theme


def test_provider_rating_visual_rounds_to_half_stars_without_personal_state(qapp) -> None:
    assert provider_rating_stars(10.0) == "★★★★★"
    assert provider_rating_stars(7.1) == "★★★½☆"
    assert provider_rating_stars(5.0) == "★★½☆☆"
    assert provider_rating_stars(0.2) == "☆☆☆☆☆"
    assert provider_rating_stars(None) == ""


def test_movie_card_and_details_keep_provider_numeric_rating_read_only(
    qapp,
    movie_item_factory,
    movie_details_factory,
) -> None:
    card = MovieCard(movie_item_factory(rating=7.1))
    assert card.findChild(QLabel, "movieRatingStars").text() == "★★★½☆"
    assert card.findChild(QLabel, "movieRatingLabel").text() == "7.1 / 10"

    details = MovieDetailsView()
    details.set_movie(movie_details_factory(rating=7.1))
    assert details.findChild(QLabel, "detailsRatingStars").text() == "★★★½☆"
    assert details.findChild(QLabel, "detailsRatingValue").text() == "TMDB 7.1 / 10"
    details.set_movie(movie_details_factory(rating=None))
    assert details.findChild(QLabel, "detailsRatingStars").isHidden()
    assert details.findChild(QLabel, "detailsRatingValue").text() == "TMDB rating unavailable"


def test_library_search_matches_title_original_title_and_year_without_provider_calls(
    qapp,
    movie_item_factory,
) -> None:
    items = (
        movie_item_factory(title="Interstellar", original_title="Interstellar", year=2014),
        movie_item_factory(movie_id=2, title="The Prestige", original_title="Prestige", year=2006),
    )
    actions = LocalActions(items)
    view = LibraryView(actions)

    view.show_library()
    assert view.card_count == 2
    assert view.search_suggestions() == ("Interstellar", "The Prestige", "Prestige")

    view.set_search_query("2014")
    assert view.card_count == 1
    view.set_search_query("prestige")
    assert view.card_count == 1
    view.set_search_query("not a local movie")
    assert view.card_count == 0
    assert "No movies found" in view.findChild(QLabel, "libraryStateLabel").text()
    assert actions.calls == 1


def test_sidebar_search_keeps_shell_geometry_but_is_interactive_only_in_library(
    qapp,
    movie_item_factory,
) -> None:
    items = (movie_item_factory(title="Interstellar", year=2014),)
    actions = LocalActions(items)
    window = MainWindow(
        actions,
        settings_actions=SettingsActions(),
        load_on_show=False,
    )
    search = window.findChild(QLineEdit, "librarySearchInput")
    assert search is not None

    window.show_library()
    assert search.isHidden() is False
    assert search.isEnabled()
    search.setText("inter")
    assert window.library_view.card_count == 1
    assert actions.calls == 1

    window.show_settings()
    assert search.isHidden() is False
    assert not search.isEnabled()
    assert search.text() == ""
    search.setText("inter")  # programmatic signal must still be side-effect free
    assert search.text() == ""
    assert window.current_section == "settings"
    assert window.library_view._search_query == ""
    assert actions.calls == 1
    window.close()


def test_search_escape_closes_suggestions_before_clearing_text(qapp, movie_item_factory) -> None:
    window = MainWindow(
        LocalActions((movie_item_factory(title="Interstellar"),)),
        load_on_show=False,
    )
    window.show_library()
    search = window.findChild(QLineEdit, "librarySearchInput")
    assert search is not None
    search.setText("inter")
    search.completer().complete()
    qapp.processEvents()
    QTest.keyClick(search, Qt.Key.Key_Escape)
    assert search.text() == "inter"
    QTest.keyClick(search, Qt.Key.Key_Escape)
    assert search.text() == ""
    window.close()


def test_language_toggle_has_semantic_accessibility_and_persists_selection(qapp) -> None:
    actions = SettingsActions()
    view = SettingsView(actions)
    toggle = view.findChild(type(view.language_toggle), "languageToggle")
    assert toggle is not None
    assert toggle.accessibleName() == "Language"
    assert "English" in toggle.accessibleDescription()
    toggle.arabic_button.click()
    assert actions.language is UiLanguage.ARABIC
    assert qapp.layoutDirection() is Qt.LayoutDirection.RightToLeft
    assert toggle.arabic_button.isChecked()
    toggle.english_button.click()
    assert actions.language is UiLanguage.ENGLISH
    assert qapp.layoutDirection() is Qt.LayoutDirection.LeftToRight


def test_tmdb_card_is_localized_and_has_one_guide_action(qapp) -> None:
    actions = SettingsActions()
    view = SettingsView(actions)
    info = view.findChild(QLabel, "tmdbInfoBarTitle")
    setup = view.findChild(QPushButton, "tmdbSetupGuideButton")
    open_button = view.findChild(QPushButton, "openTmdbButton")
    assert info is not None and info.text() == "TMDB Metadata"
    assert setup is not None and setup.text() == "Setup Guide"
    assert open_button is None
    assert "token" not in info.text().casefold()
    view.language_toggle.arabic_button.click()
    assert "TMDB" in info.text() and info.text() != "TMDB Metadata"
