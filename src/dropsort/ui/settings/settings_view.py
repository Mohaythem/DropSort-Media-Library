from __future__ import annotations

from collections.abc import Callable
from pathlib import Path

from PySide6.QtCore import Qt, Signal
from PySide6.QtGui import QPixmap
from PySide6.QtWidgets import (
    QFrame,
    QButtonGroup,
    QComboBox,
    QGridLayout,
    QHBoxLayout,
    QLabel,
    QLineEdit,
    QPushButton,
    QMessageBox,
    QScrollArea,
    QSizePolicy,
    QVBoxLayout,
    QWidget,
)

from dropsort.application.configuration.metadata_credentials import (
    MetadataCredentialOrigin,
    MetadataCredentialStatus,
)
from dropsort.ui.common.theme import SPACE_4, SPACE_36, SPACE_LARGE, SPACE_MEDIUM, SPACE_SMALL
from dropsort.ui.common.icon import FluentIconName, set_fluent_icon
from dropsort.ui.contracts import SettingsUiActions
from dropsort.application.configuration.localization import UiLanguage
from dropsort.application.configuration.theme import SELECTABLE_THEMES, UiTheme
from dropsort.ui.localization import TextId, UiLocalizer


TMDB_ATTRIBUTION_NOTICE = (
    "This product uses the TMDB API but is not endorsed or certified by TMDB."
)
TMDB_LOGO_PATH = Path(__file__).parent.parent / "assets" / "tmdb" / "blue_long.svg"


class LanguageToggle(QFrame):
    """A compact two-state language control with semantic accessibility."""

    language_selected = Signal(object)

    def __init__(self, language: UiLanguage, *, localizer: UiLocalizer, parent=None) -> None:
        super().__init__(parent)
        self.setObjectName("languageToggle")
        self.setAccessibleName(localizer.text(TextId.LANGUAGE_TITLE))
        self.setAccessibleDescription(localizer.text(TextId.LANGUAGE_ACCESSIBLE))
        layout = QHBoxLayout(self)
        layout.setContentsMargins(SPACE_4, SPACE_4, SPACE_4, SPACE_4)
        layout.setSpacing(SPACE_4)
        self._button_group = QButtonGroup(self)
        self._button_group.setExclusive(True)
        self.english_button = QPushButton(
            localizer.text(TextId.LANGUAGE_ENGLISH), self
        )
        self.english_button.setObjectName("languageEnglishButton")
        self.english_button.setCheckable(True)
        self.english_button.setAccessibleName(localizer.text(TextId.LANGUAGE_ENGLISH))
        self.arabic_button = QPushButton(
            localizer.text(TextId.LANGUAGE_ARABIC), self
        )
        self.arabic_button.setObjectName("languageArabicButton")
        self.arabic_button.setCheckable(True)
        self.arabic_button.setAccessibleName(localizer.text(TextId.LANGUAGE_ARABIC))
        self._button_group.addButton(self.english_button)
        self._button_group.addButton(self.arabic_button)
        layout.addWidget(self.english_button)
        layout.addWidget(self.arabic_button)
        self.english_button.clicked.connect(
            lambda: self.language_selected.emit(UiLanguage.ENGLISH)
        )
        self.arabic_button.clicked.connect(
            lambda: self.language_selected.emit(UiLanguage.ARABIC)
        )
        self.set_language(language)

    def set_language(self, language: UiLanguage) -> None:
        english = language is UiLanguage.ENGLISH
        self.english_button.setChecked(english)
        self.arabic_button.setChecked(not english)

    def retranslate(self, localizer: UiLocalizer) -> None:
        self.setAccessibleName(localizer.text(TextId.LANGUAGE_TITLE))
        self.setAccessibleDescription(localizer.text(TextId.LANGUAGE_ACCESSIBLE))
        self.english_button.setText(localizer.text(TextId.LANGUAGE_ENGLISH))
        self.arabic_button.setText(localizer.text(TextId.LANGUAGE_ARABIC))


class SettingsView(QWidget):
    session_token_applied = Signal()
    clear_library_requested = Signal()
    language_changed = Signal(object)
    theme_changed = Signal(object)
    history_requested = Signal()

    def __init__(
        self,
        actions: SettingsUiActions,
        *,
        confirm_clear: Callable[[QWidget, str, str], bool] | None = None,
        localizer: UiLocalizer | None = None,
        parent=None,
    ) -> None:
        super().__init__(parent)
        self.setObjectName("settingsView")
        self._actions = actions
        self._localizer = localizer or UiLocalizer()
        self._confirm_clear = confirm_clear or _confirm_clear_library
        self._feedback_text_id: TextId | None = None
        outer_layout = QVBoxLayout(self)
        outer_layout.setContentsMargins(0, 0, 0, 0)
        scroll = QScrollArea()
        scroll.setObjectName("settingsScrollArea")
        scroll.setWidgetResizable(True)
        scroll.viewport().setObjectName("settingsScrollViewport")
        content = QWidget()
        content.setObjectName("settingsContent")
        content.setMaximumWidth(720)
        layout = QVBoxLayout(content)
        layout.setContentsMargins(SPACE_36, SPACE_36, SPACE_36, SPACE_36)
        layout.setSpacing(SPACE_36)
        self._setting_cards: list[QFrame] = []
        self._cards_host = QWidget()
        self._cards_host.setObjectName("settingsCardsHost")
        self._cards_grid = QGridLayout(self._cards_host)
        self._cards_grid.setContentsMargins(0, 0, 0, 0)
        self._cards_grid.setHorizontalSpacing(SPACE_MEDIUM)
        self._cards_grid.setVerticalSpacing(SPACE_MEDIUM)

        heading = QLabel()
        heading.setProperty("role", "screenHeading")
        self._localizer.bind_text(heading, TextId.SETTINGS_TITLE)
        layout.addWidget(heading)

        language_panel = QFrame()
        language_panel.setObjectName("languageSettingsSection")
        language_panel.setProperty("role", "settingSection")
        language_layout = QVBoxLayout(language_panel)
        language_layout.setContentsMargins(
            SPACE_MEDIUM, SPACE_MEDIUM, SPACE_MEDIUM, SPACE_MEDIUM
        )
        language_layout.setSpacing(SPACE_SMALL)
        language_title = QLabel()
        language_title.setProperty("role", "rowTitle")
        self._localizer.bind_text(language_title, TextId.LANGUAGE_TITLE)
        language_layout.addWidget(language_title)
        language_description = QLabel()
        language_description.setProperty("role", "muted")
        language_description.setWordWrap(True)
        self._localizer.bind_text(language_description, TextId.LANGUAGE_DESCRIPTION)
        language_layout.addWidget(language_description)
        self.language_toggle = LanguageToggle(
            self._localizer.language,
            localizer=self._localizer,
        )
        self.language_toggle.language_selected.connect(self._language_from_toggle)
        language_layout.addWidget(self.language_toggle)

        # Retained as a non-visual compatibility adapter for existing callers;
        # the user-facing control is the semantic two-state toggle above.
        self.language_selector = QComboBox()
        self.language_selector.setObjectName("languageSelector")
        self.language_selector.addItem("", UiLanguage.ENGLISH.value)
        self.language_selector.addItem("", UiLanguage.ARABIC.value)
        self._refresh_language_items()
        self.language_selector.setCurrentIndex(
            1 if self._localizer.language is UiLanguage.ARABIC else 0
        )
        self.language_selector.currentIndexChanged.connect(self._language_selected)
        language_layout.addWidget(self.language_selector)
        self.language_selector.hide()

        appearance = QFrame()
        appearance.setObjectName("appearanceSettingsCard")
        appearance.setProperty("role", "panel")
        appearance_layout = QVBoxLayout(appearance)
        appearance_layout.setContentsMargins(SPACE_MEDIUM, SPACE_MEDIUM, SPACE_MEDIUM, SPACE_MEDIUM)
        appearance_layout.setSpacing(SPACE_SMALL)
        appearance_title = QLabel()
        appearance_title.setProperty("role", "rowTitle")
        self._localizer.bind_text(appearance_title, TextId.APPEARANCE)
        appearance_layout.addWidget(appearance_title)
        theme_label = QLabel()
        theme_label.setProperty("role", "muted")
        self._localizer.bind_text(theme_label, TextId.THEME)
        appearance_layout.addWidget(theme_label)
        self.theme_selector = QComboBox()
        self.theme_selector.setObjectName("themeSelector")
        self.theme_selector.setAccessibleName(self._localizer.text(TextId.THEME))
        for theme in SELECTABLE_THEMES:
            self.theme_selector.addItem("", theme.value)
        current_theme = getattr(actions, "current_ui_theme", lambda: UiTheme.SLATE)()
        try:
            current_theme = UiTheme(current_theme)
        except (TypeError, ValueError):
            current_theme = UiTheme.SLATE
        if current_theme not in SELECTABLE_THEMES:
            current_theme = UiTheme.SLATE
        self.theme_selector.setCurrentIndex(SELECTABLE_THEMES.index(current_theme))
        self._refresh_theme_items()
        self.theme_selector.currentIndexChanged.connect(self._theme_selected)
        appearance_layout.addWidget(self.theme_selector)
        appearance_divider = QFrame()
        appearance_divider.setProperty("role", "settingDivider")
        appearance_layout.addWidget(appearance_divider)
        appearance_layout.addWidget(language_panel)
        self._add_setting_card(appearance)

        tmdb_card = QFrame()
        tmdb_card.setObjectName("tmdbSettingsCard")
        tmdb_card.setProperty("role", "panel")
        tmdb_card_layout = QVBoxLayout(tmdb_card)
        tmdb_card_layout.setContentsMargins(
            SPACE_MEDIUM, SPACE_MEDIUM, SPACE_MEDIUM, SPACE_MEDIUM
        )
        tmdb_card_layout.setSpacing(SPACE_SMALL)

        self._tmdb_info_title = QLabel()
        self._tmdb_info_title.setObjectName("tmdbInfoBarTitle")
        self._tmdb_info_title.setProperty("role", "rowTitle")
        self._localizer.bind_text(self._tmdb_info_title, TextId.TMDB_METADATA)
        tmdb_card_layout.addWidget(self._tmdb_info_title)
        self._status = QLabel()
        self._status.setObjectName("tmdbCredentialStatus")
        tmdb_card_layout.addWidget(self._status)
        self._session_notice = QLabel()
        self._session_notice.setProperty("role", "muted")
        self._session_notice.setWordWrap(True)
        self._localizer.bind_text(self._session_notice, TextId.TMDB_SESSION_NOTICE)
        tmdb_card_layout.addWidget(self._session_notice)

        self.token_input = QLineEdit()
        self.token_input.setObjectName("tmdbTokenInput")
        self.token_input.setPlaceholderText(
            self._localizer.text(TextId.TMDB_TOKEN_PLACEHOLDER)
        )
        self._localizer.mark_ltr(self.token_input)
        self.token_input.setEchoMode(QLineEdit.EchoMode.Password)
        self.token_input.setMaxLength(4096)
        tmdb_card_layout.addWidget(self.token_input)

        buttons = QHBoxLayout()
        apply_button = QPushButton()
        apply_button.setObjectName("applyTmdbTokenButton")
        apply_button.setProperty("role", "primaryAction")
        set_fluent_icon(apply_button, FluentIconName.SAVE)
        apply_button.clicked.connect(self.apply_session_token)
        self._localizer.bind_text(apply_button, TextId.TMDB_SAVE_SESSION)
        buttons.addWidget(apply_button)
        clear_button = QPushButton()
        clear_button.setObjectName("clearTmdbTokenButton")
        clear_button.setProperty("role", "secondaryAction")
        set_fluent_icon(clear_button, FluentIconName.DELETE)
        clear_button.clicked.connect(self.clear_session_token)
        self._localizer.bind_text(clear_button, TextId.TMDB_CLEAR_SESSION)
        buttons.addWidget(clear_button)
        self._tmdb_setup_button = QPushButton()
        self._tmdb_setup_button.setObjectName("tmdbSetupGuideButton")
        self._tmdb_setup_button.setProperty("role", "secondaryAction")
        set_fluent_icon(self._tmdb_setup_button, FluentIconName.INFO)
        self._localizer.bind_text(self._tmdb_setup_button, TextId.TMDB_SETUP_GUIDE)
        self._tmdb_setup_button.clicked.connect(self._show_tmdb_setup_guide)
        buttons.addWidget(self._tmdb_setup_button)
        buttons.addStretch(1)
        tmdb_card_layout.addLayout(buttons)

        self._feedback = QLabel()
        self._feedback.setObjectName("tmdbSettingsFeedback")
        self._feedback.setWordWrap(True)
        tmdb_card_layout.addWidget(self._feedback)
        self._add_setting_card(tmdb_card)

        history = QFrame()
        history.setObjectName("historyRecoverySettingsCard")
        history.setProperty("role", "panel")
        history_layout = QVBoxLayout(history)
        history_layout.setContentsMargins(SPACE_MEDIUM, SPACE_MEDIUM, SPACE_MEDIUM, SPACE_MEDIUM)
        history_layout.setSpacing(SPACE_SMALL)
        history_title = QLabel()
        history_title.setProperty("role", "rowTitle")
        self._localizer.bind_text(history_title, TextId.HISTORY_RECOVERY)
        history_layout.addWidget(history_title)
        history_description = QLabel()
        history_description.setProperty("role", "muted")
        history_description.setWordWrap(True)
        self._localizer.bind_text(history_description, TextId.HISTORY_GUIDANCE)
        history_layout.addWidget(history_description)
        view_history = QPushButton()
        view_history.setObjectName("viewOperationHistoryButton")
        view_history.setProperty("role", "secondaryAction")
        set_fluent_icon(view_history, FluentIconName.OPERATION_DETAILS)
        self._localizer.bind_text(view_history, TextId.VIEW_OPERATION_HISTORY)
        view_history.clicked.connect(self.history_requested.emit)
        history_layout.addWidget(view_history)
        self._add_setting_card(history)

        credits = QFrame()
        credits.setProperty("role", "panel")
        credits_layout = QVBoxLayout(credits)
        credits_layout.setContentsMargins(
            SPACE_MEDIUM, SPACE_MEDIUM, SPACE_MEDIUM, SPACE_MEDIUM
        )
        credits_layout.setSpacing(SPACE_SMALL)

        credits_title = QLabel()
        credits_title.setProperty("role", "rowTitle")
        credits_layout.addWidget(credits_title)
        self._localizer.bind_text(credits_title, TextId.ABOUT_CREDITS)

        tmdb_logo = QLabel()
        tmdb_logo.setObjectName("tmdbAttributionLogo")
        tmdb_logo.setAccessibleName("The Movie Database (TMDB)")
        tmdb_logo.setPixmap(
            QPixmap(str(TMDB_LOGO_PATH)).scaledToWidth(180)
        )
        credits_layout.addWidget(tmdb_logo)

        attribution = QLabel()
        attribution.setObjectName("tmdbAttributionNotice")
        attribution.setProperty("role", "muted")
        attribution.setWordWrap(True)
        self._localizer.bind_text(attribution, TextId.TMDB_NOTICE)
        credits_layout.addWidget(attribution)

        source_notice = QLabel()
        source_notice.setProperty("role", "muted")
        source_notice.setWordWrap(True)
        self._localizer.bind_text(source_notice, TextId.TMDB_SOURCE_NOTICE)
        credits_layout.addWidget(source_notice)
        self._add_setting_card(credits)

        maintenance = QFrame()
        maintenance.setObjectName("clearLibraryDangerZone")
        maintenance.setProperty("role", "panel")
        maintenance.setProperty("role", "dangerZone")
        maintenance_layout = QVBoxLayout(maintenance)
        maintenance_layout.setContentsMargins(
            SPACE_MEDIUM, SPACE_MEDIUM, SPACE_MEDIUM, SPACE_MEDIUM
        )
        maintenance_layout.setSpacing(SPACE_SMALL)
        danger_heading = QLabel()
        danger_heading.setObjectName("dangerZoneHeading")
        self._localizer.bind_text(danger_heading, TextId.DANGER_ZONE)
        maintenance_layout.addWidget(danger_heading)
        maintenance_title = QLabel()
        maintenance_title.setProperty("role", "rowTitle")
        maintenance_layout.addWidget(maintenance_title)
        self._localizer.bind_text(maintenance_title, TextId.LIBRARY_DATA)
        maintenance_notice = QLabel()
        maintenance_notice.setProperty("role", "muted")
        maintenance_notice.setWordWrap(True)
        self._localizer.bind_text(maintenance_notice, TextId.CLEAR_LIBRARY_DESCRIPTION)
        maintenance_layout.addWidget(maintenance_notice)
        clear_library = QPushButton()
        clear_library.setObjectName("clearLibraryDataButton")
        clear_library.setProperty("role", "dangerAction")
        set_fluent_icon(clear_library, FluentIconName.CLEAR_LIBRARY)
        clear_library.clicked.connect(self.request_clear_library)
        self._localizer.bind_text(clear_library, TextId.CLEAR_LIBRARY)
        maintenance_layout.addWidget(clear_library)
        self._clear_feedback = QLabel()
        self._clear_feedback.setObjectName("clearLibraryDataFeedback")
        self._clear_feedback.setWordWrap(True)
        maintenance_layout.addWidget(self._clear_feedback)
        self._add_setting_card(maintenance)
        layout.addWidget(self._cards_host)
        layout.addStretch(1)
        scroll.setWidget(content)
        outer_layout.addWidget(scroll)
        self.refresh_status()
        self._localizer.language_changed.connect(self._language_binding_changed)
        self._reflow_cards()

    def _add_setting_card(self, card: QFrame) -> None:
        card.setSizePolicy(QSizePolicy.Policy.Preferred, QSizePolicy.Policy.Maximum)
        self._setting_cards.append(card)
        self._cards_grid.addWidget(
            card,
            len(self._setting_cards) - 1,
            0,
            Qt.AlignmentFlag.AlignTop,
        )

    def _reflow_cards(self) -> None:
        # Settings is intentionally a stable single-column desktop layout.
        # Older responsive code removed/re-added every card on each resize,
        # causing a visible one-frame collapse/expand when the page opened.
        self._cards_grid.setColumnStretch(0, 1)

    @property
    def status_text(self) -> str:
        return self._status.text()

    @property
    def feedback_text(self) -> str:
        return self._feedback.text()

    @property
    def session_notice(self) -> str:
        return self._session_notice.text()

    def refresh_status(self) -> None:
        self._set_status(self._actions.metadata_credential_status())

    def apply_session_token(self) -> None:
        token = self.token_input.text()
        if not token.strip():
            self._set_feedback(TextId.TMDB_ENTER_TOKEN)
            return
        try:
            status = self._actions.apply_tmdb_session_token(token)
        except ValueError:
            self._set_feedback(TextId.TMDB_INVALID_TOKEN)
            return
        self.token_input.clear()
        self._set_status(status)
        self._set_feedback(TextId.TMDB_READY)
        self.session_token_applied.emit()

    def clear_session_token(self) -> None:
        status = self._actions.clear_tmdb_session_token()
        self.token_input.clear()
        self._set_status(status)
        self._set_feedback(TextId.TMDB_CLEARED)

    def request_clear_library(self) -> None:
        title = self._localizer.text(TextId.CLEAR_LIBRARY_TITLE)
        message = self._localizer.text(TextId.CLEAR_LIBRARY_CONFIRM)
        if self._confirm_clear(self, title, message):
            self.clear_library_requested.emit()

    def show_clear_started(self) -> None:
        self._clear_feedback.setText(self._localizer.text(TextId.CLEAR_LIBRARY_RUNNING))

    def show_clear_result(self, result) -> None:
        message = self._localizer.text(
            TextId.CLEAR_LIBRARY_RESULT,
            movies=result.movies_removed,
            files=result.media_files_removed,
        )
        if result.warning is not None:
            message += self._localizer.text(TextId.CLEAR_LIBRARY_CACHE_WARNING)
        self._clear_feedback.setText(message)

    def show_clear_error(self, message: str) -> None:
        self._clear_feedback.setText(message)

    def _set_status(self, status: MetadataCredentialStatus) -> None:
        text = {
            MetadataCredentialOrigin.NOT_CONFIGURED: TextId.TMDB_NOT_CONFIGURED,
            MetadataCredentialOrigin.ENVIRONMENT: TextId.TMDB_ENVIRONMENT,
            MetadataCredentialOrigin.SESSION: TextId.TMDB_SESSION,
        }[status.origin]
        rendered = self._localizer.text(text)
        if self._status.text() != rendered:
            self._status.setText(rendered)

    def _set_feedback(self, text_id: TextId) -> None:
        self._feedback_text_id = text_id
        self._feedback.setText(self._localizer.text(text_id))

    def _language_selected(self, index: int) -> None:
        language = UiLanguage(self.language_selector.itemData(index))
        self._apply_language(language)

    def _language_from_toggle(self, language: UiLanguage) -> None:
        self._apply_language(language)

    def _apply_language(self, language: UiLanguage) -> None:
        setter = getattr(self._actions, "set_ui_language", None)
        if callable(setter):
            language = setter(language)
        self._localizer.set_language(language)
        self.language_changed.emit(language)

    def _theme_selected(self, index: int) -> None:
        theme = UiTheme(self.theme_selector.itemData(index))
        setter = getattr(self._actions, "set_ui_theme", None)
        if callable(setter):
            theme = setter(theme)
        self.theme_changed.emit(theme)

    def _refresh_theme_items(self) -> None:
        ids = (
            TextId.THEME_SLATE,
            TextId.THEME_DARK,
            TextId.THEME_LIGHT,
        )
        for index, text_id in enumerate(ids):
            self.theme_selector.setItemText(index, self._localizer.text(text_id))

    def _refresh_language_items(self) -> None:
        self.language_selector.setItemText(
            0, self._localizer.text(TextId.LANGUAGE_ENGLISH)
        )
        self.language_selector.setItemText(
            1, self._localizer.text(TextId.LANGUAGE_ARABIC)
        )

    def _language_binding_changed(self, _language: UiLanguage) -> None:
        self.language_selector.blockSignals(True)
        try:
            self._refresh_language_items()
            self._refresh_theme_items()
            self.theme_selector.setAccessibleName(self._localizer.text(TextId.THEME))
            self.language_selector.setCurrentIndex(
                1 if self._localizer.language is UiLanguage.ARABIC else 0
            )
        finally:
            self.language_selector.blockSignals(False)
        self.language_toggle.set_language(self._localizer.language)
        self.language_toggle.retranslate(self._localizer)
        self.theme_selector.setAccessibleName(self._localizer.text(TextId.THEME))
        self._localizer.bind_text(self._tmdb_info_title, TextId.TMDB_METADATA)
        self._localizer.bind_text(self._tmdb_setup_button, TextId.TMDB_SETUP_GUIDE)
        self.token_input.setPlaceholderText(
            self._localizer.text(TextId.TMDB_TOKEN_PLACEHOLDER)
        )
        self.refresh_status()
        if self._feedback_text_id is not None:
            self._feedback.setText(self._localizer.text(self._feedback_text_id))

    def _show_tmdb_setup_guide(self) -> None:
        QMessageBox.information(
            self,
            self._localizer.text(TextId.TMDB_SETUP_GUIDE_TITLE),
            self._localizer.text(TextId.TMDB_SETUP_GUIDE_BODY),
        )

def _confirm_clear_library(parent: QWidget, title: str, message: str) -> bool:
    answer = QMessageBox.warning(
        parent,
        title,
        message,
        QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.Cancel,
        QMessageBox.StandardButton.Cancel,
    )
    return answer == QMessageBox.StandardButton.Yes
