from __future__ import annotations

from PySide6.QtWidgets import QApplication

from dropsort.application.configuration.metadata_credentials import MetadataCredentialOrigin, MetadataCredentialStatus
from dropsort.application.configuration.theme import UiTheme
from dropsort.ui.settings import SettingsView


def test_settings_theme_selector_switches_without_resetting_language(qapp: QApplication) -> None:
    class SettingsActions:
        theme = UiTheme.DEEP_INK

        def metadata_credential_status(self):
            return MetadataCredentialStatus(False, MetadataCredentialOrigin.NOT_CONFIGURED)

        def current_ui_theme(self):
            return self.theme

        def set_ui_theme(self, value):
            self.theme = value
            return value

        def apply_tmdb_session_token(self, token):
            raise AssertionError

        def clear_tmdb_session_token(self):
            raise AssertionError

    actions = SettingsActions()
    view = SettingsView(actions)
    view.resize(1000, 700)
    view.resize(500, 700)
    view.theme_selector.setCurrentIndex(1)
    assert actions.theme is UiTheme.DARK
    assert view.theme_selector.currentData() == UiTheme.DARK.value


def test_theme_selector_exposes_exactly_main_dark_slate_light_labels(qapp: QApplication) -> None:
    class SettingsActions:
        def metadata_credential_status(self):
            return MetadataCredentialStatus(False, MetadataCredentialOrigin.NOT_CONFIGURED)

        def current_ui_theme(self):
            return UiTheme.MAIN

        def set_ui_theme(self, value):
            return value

    view = SettingsView(SettingsActions())

    assert view.theme_selector.count() == 4
    assert [view.theme_selector.itemText(i) for i in range(4)] == [
        "Main",
        "Dark",
        "Slate",
        "Light",
    ]
