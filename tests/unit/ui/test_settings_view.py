from __future__ import annotations

from dataclasses import dataclass

from PySide6.QtCore import Qt
from PySide6.QtWidgets import (
    QApplication,
    QComboBox,
    QLabel,
    QLineEdit,
    QMessageBox,
    QPushButton,
    QScrollArea,
    QWidget,
)

from dropsort.application.configuration.metadata_credentials import (
    MetadataCredentialOrigin,
    MetadataCredentialStatus,
)
from dropsort.application.configuration.localization import UiLanguage
from dropsort.ui.settings.settings_view import (
    TMDB_ATTRIBUTION_NOTICE,
    SettingsView,
    _confirm_clear_library,
)


VALID_TOKEN = "session-token-value-123456789012345"


@dataclass
class FakeSettingsActions:
    origin: MetadataCredentialOrigin = MetadataCredentialOrigin.NOT_CONFIGURED
    applied: list[str] | None = None
    clear_calls: int = 0
    language: UiLanguage = UiLanguage.ENGLISH

    def metadata_credential_status(self) -> MetadataCredentialStatus:
        return MetadataCredentialStatus(
            configured=self.origin is not MetadataCredentialOrigin.NOT_CONFIGURED,
            origin=self.origin,
        )

    def apply_tmdb_session_token(self, token: str) -> MetadataCredentialStatus:
        if self.applied is None:
            self.applied = []
        self.applied.append(token)
        if len(token) < 20:
            raise ValueError("invalid token")
        self.origin = MetadataCredentialOrigin.SESSION
        return self.metadata_credential_status()

    def clear_tmdb_session_token(self) -> MetadataCredentialStatus:
        self.clear_calls += 1
        self.origin = MetadataCredentialOrigin.NOT_CONFIGURED
        return self.metadata_credential_status()

    def current_ui_language(self) -> UiLanguage:
        return self.language

    def set_ui_language(self, language: UiLanguage) -> UiLanguage:
        self.language = language
        return language


def test_language_selector_switches_arabic_rtl_and_restores_english_ltr(
    qapp: QApplication,
) -> None:
    actions = FakeSettingsActions()
    view = SettingsView(actions)
    selector = view.findChild(QComboBox, "languageSelector")
    assert selector is not None

    selector.setCurrentIndex(1)

    assert actions.language is UiLanguage.ARABIC
    assert qapp.layoutDirection() is Qt.LayoutDirection.RightToLeft
    assert view.findChild(QLabel, "tmdbCredentialStatus").text() == "غير مُعد"
    assert view.token_input.layoutDirection() is Qt.LayoutDirection.LeftToRight

    selector.setCurrentIndex(0)

    assert actions.language is UiLanguage.ENGLISH
    assert qapp.layoutDirection() is Qt.LayoutDirection.LeftToRight


def test_token_field_is_masked_and_session_only_notice_is_visible(
    qapp: QApplication,
) -> None:
    view = SettingsView(FakeSettingsActions())

    assert view.token_input.echoMode() is QLineEdit.EchoMode.Password
    assert "not permanently stored" in view.session_notice.casefold()
    assert view.status_text == "Not configured"


def test_apply_token_updates_session_status_and_clears_input(
    qapp: QApplication,
) -> None:
    actions = FakeSettingsActions()
    view = SettingsView(actions)
    view.token_input.setText(VALID_TOKEN)

    view.apply_session_token()

    assert actions.applied == [VALID_TOKEN]
    assert view.token_input.text() == ""
    assert view.status_text == "Configured for this session"
    assert view.feedback_text == "TMDB metadata is ready for this session."

    selector = view.findChild(QComboBox, "languageSelector")
    assert selector is not None
    selector.setCurrentIndex(1)

    assert view.feedback_text == "بيانات TMDB جاهزة لهذه الجلسة."


def test_empty_or_invalid_token_has_clean_feedback(qapp: QApplication) -> None:
    view = SettingsView(FakeSettingsActions())

    view.apply_session_token()
    assert "enter" in view.feedback_text.casefold()

    view.token_input.setText("too-short")
    view.apply_session_token()
    assert "valid" in view.feedback_text.casefold()
    assert "too-short" not in view.feedback_text


def test_clear_session_token_updates_status(qapp: QApplication) -> None:
    actions = FakeSettingsActions(origin=MetadataCredentialOrigin.SESSION)
    view = SettingsView(actions)

    view.clear_session_token()

    assert actions.clear_calls == 1
    assert view.status_text == "Not configured"


def test_environment_configuration_is_reported_without_revealing_token(
    qapp: QApplication,
) -> None:
    view = SettingsView(
        FakeSettingsActions(origin=MetadataCredentialOrigin.ENVIRONMENT)
    )

    assert view.status_text == "Configured from environment"
    assert view.token_input.text() == ""


def test_about_credits_contains_official_tmdb_attribution_and_logo(
    qapp: QApplication,
) -> None:
    view = SettingsView(FakeSettingsActions())

    notice = view.findChild(QLabel, "tmdbAttributionNotice")
    logo = view.findChild(QLabel, "tmdbAttributionLogo")

    assert notice is not None
    assert notice.text() == TMDB_ATTRIBUTION_NOTICE
    assert TMDB_ATTRIBUTION_NOTICE == (
        "This product uses the TMDB API but is not endorsed or certified by TMDB."
    )
    assert logo is not None
    assert logo.pixmap() is not None
    assert not logo.pixmap().isNull()


def test_clear_library_requires_explicit_media_safety_confirmation(
    qapp: QApplication,
) -> None:
    confirmations: list[tuple[str, str]] = []

    def confirm(_parent, title: str, message: str) -> bool:
        confirmations.append((title, message))
        return True

    view = SettingsView(FakeSettingsActions(), confirm_clear=confirm)
    requested: list[bool] = []
    view.clear_library_requested.connect(lambda: requested.append(True))
    button = view.findChild(QPushButton, "clearLibraryDataButton")
    assert button is not None

    button.click()

    assert requested == [True]
    assert confirmations
    wording = " ".join(confirmations[0]).casefold()
    assert "physical movie files" in wording
    assert "not" in wording
    assert all(word in wording for word in ("delete", "move", "rename", "modify"))


def test_clear_library_cancelled_confirmation_emits_nothing(qapp: QApplication) -> None:
    view = SettingsView(
        FakeSettingsActions(),
        confirm_clear=lambda _parent, _title, _message: False,
    )
    requested: list[bool] = []
    view.clear_library_requested.connect(lambda: requested.append(True))

    view.request_clear_library()

    assert requested == []


def test_real_clear_confirmation_uses_button_value_equality(monkeypatch, qapp) -> None:
    class EqualToYes:
        def __eq__(self, other) -> bool:
            return other == QMessageBox.StandardButton.Yes

    monkeypatch.setattr(
        "dropsort.ui.settings.settings_view.QMessageBox.warning",
        lambda *_args, **_kwargs: EqualToYes(),
    )

    assert _confirm_clear_library(None, "Clear?", "Confirm") is True


def test_settings_surfaces_have_themeable_background_layers(qapp: QApplication) -> None:
    view = SettingsView(FakeSettingsActions())

    assert view.objectName() == "settingsView"
    assert view.findChild(QScrollArea, "settingsScrollArea") is not None
    assert view.findChild(QWidget, "settingsScrollViewport") is not None
    assert view.findChild(QWidget, "settingsContent") is not None
    assert view.findChild(QWidget, "settingsCardsHost") is not None
