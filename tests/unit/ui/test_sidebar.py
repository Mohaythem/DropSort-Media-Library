from __future__ import annotations

from dataclasses import dataclass

from PySide6.QtWidgets import QApplication, QSplitter, QPushButton

from dropsort.application.configuration.localization import UiLanguage
from dropsort.application.configuration.metadata_credentials import (
    MetadataCredentialOrigin,
    MetadataCredentialStatus,
)
from dropsort.application.configuration.theme import (
    SIDEBAR_DEFAULT_WIDTH,
    UiTheme,
)
from dropsort.ui.main_window.window import MainWindow
from dropsort.ui.common.icon import APPLICATION_ICON_PATH, APPLICATION_ICON_SVG_PATH, application_icon


@dataclass
class SettingsActions:
    sidebar_width: int | None = None
    language: UiLanguage = UiLanguage.ENGLISH
    theme: UiTheme = UiTheme.DEEP_INK

    def metadata_credential_status(self):
        return MetadataCredentialStatus(False, MetadataCredentialOrigin.NOT_CONFIGURED)

    def apply_tmdb_session_token(self, _token):
        raise AssertionError

    def clear_tmdb_session_token(self):
        raise AssertionError

    def clear_library_data(self):
        raise AssertionError

    def current_ui_language(self):
        return self.language

    def set_ui_language(self, value):
        self.language = value
        return value

    def current_ui_theme(self):
        return self.theme

    def set_ui_theme(self, value):
        self.theme = value
        return value

    def current_sidebar_width(self):
        return self.sidebar_width

    def set_sidebar_width(self, value):
        self.sidebar_width = value
        return value


def _library(movie_item_factory, movie_details_factory):
    return type(
        "Library",
        (),
        {
            "list_movies": lambda self: (movie_item_factory(),),
            "get_movie_details": lambda self, _movie_id: movie_details_factory(),
        },
    )()


def test_sidebar_is_fixed_width_and_keeps_labels_and_search_visible(
    qapp: QApplication, movie_item_factory, movie_details_factory
) -> None:
    actions = SettingsActions()
    window = MainWindow(
        _library(movie_item_factory, movie_details_factory),
        settings_actions=actions,
        load_on_show=False,
    )
    splitter = window.findChild(QSplitter, "mainSplitter")

    assert splitter is not None
    assert splitter.count() == 2
    splitter.setSizes([56, 1000])
    qapp.processEvents()

    assert window.sidebar.minimumWidth() == SIDEBAR_DEFAULT_WIDTH
    assert window.sidebar.maximumWidth() == SIDEBAR_DEFAULT_WIDTH
    assert splitter.sizes()[0] == SIDEBAR_DEFAULT_WIDTH
    assert splitter.handleWidth() == 0
    button = window.findChild(QPushButton, "libraryNavButton")
    assert button is not None
    assert button.text() == "Library"
    assert window._search_field.isHidden() is False
    assert actions.sidebar_width is None

    window._localizer.set_language(UiLanguage.ARABIC)
    assert button.text() != "Library"
    window._localizer.set_language(UiLanguage.ENGLISH)
    assert button.text() == "Library"


def test_sidebar_ignores_legacy_saved_width_values(
    qapp: QApplication, movie_item_factory, movie_details_factory
) -> None:
    library = _library(movie_item_factory, movie_details_factory)
    actions = SettingsActions(sidebar_width=150)
    window = MainWindow(library, settings_actions=actions, load_on_show=False)
    splitter = window.findChild(QSplitter, "mainSplitter")
    assert splitter is not None
    assert splitter.sizes()[0] == SIDEBAR_DEFAULT_WIDTH

    invalid = SettingsActions(sidebar_width=-1)
    fallback = MainWindow(library, settings_actions=invalid, load_on_show=False)
    fallback_splitter = fallback.findChild(QSplitter, "mainSplitter")
    assert fallback_splitter is not None
    assert fallback_splitter.sizes()[0] == SIDEBAR_DEFAULT_WIDTH


def test_branded_application_icon_is_available_and_inherited_by_main_window(
    qapp: QApplication, movie_item_factory, movie_details_factory
) -> None:
    assert APPLICATION_ICON_PATH.is_file()
    assert APPLICATION_ICON_SVG_PATH.is_file()
    assert not application_icon().isNull()
    window = MainWindow(
        _library(movie_item_factory, movie_details_factory),
        load_on_show=False,
    )
    assert not window.windowIcon().isNull()


def test_sidebar_does_not_read_or_write_legacy_width_settings(
    qapp: QApplication, movie_item_factory, movie_details_factory
) -> None:
    class FailingSettings(SettingsActions):
        def current_sidebar_width(self):
            raise RuntimeError("settings unavailable")

        def set_sidebar_width(self, value):
            raise ValueError("settings unavailable")

    window = MainWindow(
        _library(movie_item_factory, movie_details_factory),
        settings_actions=FailingSettings(),
        load_on_show=False,
    )
    splitter = window.findChild(QSplitter, "mainSplitter")
    assert splitter is not None
    assert splitter.sizes()[0] == SIDEBAR_DEFAULT_WIDTH
    assert window.findChild(QPushButton, "libraryNavButton").text() == "Library"
