from __future__ import annotations

from dataclasses import dataclass

from PySide6.QtCore import Qt
from PySide6.QtTest import QTest
from PySide6.QtWidgets import QApplication, QPushButton

from dropsort.ui.main_window.window import MainWindow
from tests.unit.ui.test_operation_history_view import FakeHistoryActions, ImmediateRunner


@dataclass
class EmptyLibrary:
    def list_movies(self):
        return ()

    def get_movie_details(self, movie_id: int):
        raise AssertionError


def test_main_window_exposes_history_from_settings_only(qapp: QApplication) -> None:
    history = FakeHistoryActions()

    class Settings:
        def metadata_credential_status(self):
            from dropsort.application.configuration.metadata_credentials import MetadataCredentialOrigin, MetadataCredentialStatus
            return MetadataCredentialStatus(False, MetadataCredentialOrigin.NOT_CONFIGURED)

        def current_ui_language(self):
            from dropsort.application.configuration.localization import UiLanguage
            return UiLanguage.ENGLISH

        def current_ui_theme(self):
            from dropsort.application.configuration.theme import UiTheme
            return UiTheme.DEEP_INK

        def set_ui_theme(self, theme):
            return theme

    window = MainWindow(
        EmptyLibrary(),
        operation_history_actions=history,
        settings_actions=Settings(),
        task_runner=ImmediateRunner(),
        load_on_show=False,
    )
    assert window.findChild(QPushButton, "historyNavButton") is None
    button = window.findChild(QPushButton, "viewOperationHistoryButton")
    assert button is not None

    window.show_settings()
    QTest.mouseClick(button, Qt.MouseButton.LeftButton)

    assert window.current_section == "history"
    assert window.history_view is not None
    assert history.calls == ["list"]

    # Ordinary navigation away and back must reuse the already-rendered log.
    window.show_settings()
    window.show_history()
    assert history.calls == ["list"]


def test_window_without_history_has_no_history_navigation(qapp: QApplication) -> None:
    window = MainWindow(EmptyLibrary(), load_on_show=False)
    assert window.findChild(QPushButton, "historyNavButton") is None
    assert window.history_view is None
    window.show_history()
    assert window.current_section == "library"
