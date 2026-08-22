from __future__ import annotations

from PySide6.QtCore import Qt, Signal
from PySide6.QtGui import QCloseEvent
from PySide6.QtWidgets import (
    QDialog,
    QFormLayout,
    QHBoxLayout,
    QLabel,
    QLineEdit,
    QPushButton,
    QScrollArea,
    QSizePolicy,
    QVBoxLayout,
    QWidget,
)

from dropsort.application.dto.manual_search import ManualMovieSearchResult, ManualMovieSearchRequest
from dropsort.media.discovery.models import DiscoveredMedia
from dropsort.metadata.contracts import MovieCandidate
from dropsort.ui.common.tasks import QtTaskRunner, TaskRunner
from dropsort.ui.common.icon import FluentIconName, set_fluent_icon
from dropsort.ui.common.theme import SPACE_4, SPACE_8, SPACE_16, SPACE_24
from dropsort.ui.localization import TextId, UiLocalizer
from dropsort.ui.scan.manual_search_result_card import ManualSearchResultCard


class _ManualResultsScroll(QScrollArea):
    """Scrollable result host with a narrow compatibility API for old callers."""

    def __init__(self, parent=None) -> None:
        super().__init__(parent)
        self._cards: list[ManualSearchResultCard] = []
        self._current_index = -1

    def set_cards(self, cards: list[ManualSearchResultCard]) -> None:
        self._cards = cards
        self._current_index = 0 if cards else -1

    def count(self) -> int:
        return len(self._cards)

    def item(self, index: int) -> ManualSearchResultCard:
        return self._cards[index]

    def setCurrentRow(self, index: int) -> None:
        self._current_index = index if 0 <= index < len(self._cards) else -1

    def currentItem(self) -> ManualSearchResultCard | None:
        if 0 <= self._current_index < len(self._cards):
            return self._cards[self._current_index]
        return None


class ManualSearchDialog(QDialog):
    candidate_selected = Signal(object)

    def __init__(
        self,
        discovery: DiscoveredMedia,
        actions,
        *,
        runner: TaskRunner | None = None,
        localizer: UiLocalizer | None = None,
        parent=None,
    ) -> None:
        super().__init__(parent)
        self._actions = actions
        self._runner = runner or QtTaskRunner()
        self._localizer = localizer or UiLocalizer()
        self._token = 0
        self._discovery = discovery
        self._cards: list[ManualSearchResultCard] = []
        self._message_id: TextId | None = None
        self.setObjectName("manualSearchDialog")
        self.setModal(True)
        self.setMinimumSize(560, 420)
        self.resize(680, 640)

        layout = QVBoxLayout(self)
        layout.setContentsMargins(SPACE_24, SPACE_24, SPACE_24, SPACE_24)
        layout.setSpacing(SPACE_16)
        title = QLabel()
        self._localizer.bind_text(title, TextId.SEARCH_TMDB)
        title.setProperty("role", "h3")
        layout.addWidget(title)

        form = QFormLayout()
        detected = QLabel(discovery.parsed_media.title if discovery.parsed_media else discovery.path.name)
        detected.setObjectName("detectedTitleLabel")
        detected.setWordWrap(True)
        detected.setTextInteractionFlags(Qt.TextInteractionFlag.TextSelectableByMouse | Qt.TextInteractionFlag.TextSelectableByKeyboard)
        self._localizer.mark_ltr(detected)
        form.addRow(self._localizer.text(TextId.DETECTED_TITLE), detected)
        self.search_title = QLineEdit(detected.text())
        self.search_title.setObjectName("manualSearchTitleInput")
        self._localizer.mark_ltr(self.search_title)
        form.addRow(self._localizer.text(TextId.SEARCH_AS), self.search_title)
        self.search_year = QLineEdit()
        self.search_year.setObjectName("manualSearchYearInput")
        self.search_year.setPlaceholderText(self._localizer.text(TextId.YEAR))
        self._localizer.mark_ltr(self.search_year)
        form.addRow(self._localizer.text(TextId.YEAR), self.search_year)
        layout.addLayout(form)

        controls = QHBoxLayout()
        self.search_button = QPushButton()
        self.search_button.setObjectName("searchTmdbButton")
        self.search_button.setProperty("role", "primaryAction")
        set_fluent_icon(self.search_button, FluentIconName.SEARCH)
        self._localizer.bind_text(self.search_button, TextId.SEARCH_TMDB)
        self.search_button.clicked.connect(self.search)
        controls.addWidget(self.search_button)
        controls.addStretch(1)
        layout.addLayout(controls)

        self.error_label = QLabel()
        self.error_label.setObjectName("manualSearchError")
        self.error_label.setProperty("role", "error")
        self.error_label.setWordWrap(True)
        layout.addWidget(self.error_label)
        self.state_label = QLabel()
        self.state_label.setObjectName("manualSearchState")
        self.state_label.setProperty("role", "muted")
        self.state_label.setWordWrap(True)
        self.state_label.setVisible(False)
        layout.addWidget(self.state_label)
        self.results_heading = QLabel()
        self.results_heading.setObjectName("manualSearchResultsHeading")
        self.results_heading.setProperty("role", "sectionHeading")
        self._localizer.bind_text(self.results_heading, TextId.MANUAL_SEARCH_RESULTS)
        self.results_heading.setVisible(False)
        layout.addWidget(self.results_heading)

        self.results = _ManualResultsScroll()
        self.results.setObjectName("manualSearchResults")
        self.results.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Fixed)
        self.results.setMinimumHeight(0)
        self.results.setMaximumHeight(360)
        self.results.setVisible(False)
        self.results.setWidgetResizable(True)
        self.results.setHorizontalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAlwaysOff)
        self.results.setVerticalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAsNeeded)
        host = QWidget()
        host.setObjectName("manualSearchResultsHost")
        host.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Minimum)
        self._results_layout = QVBoxLayout(host)
        self._results_layout.setContentsMargins(SPACE_4, SPACE_4, SPACE_4, SPACE_4)
        self._results_layout.setSpacing(SPACE_8)
        self._results_layout.addStretch(1)
        self.results.setWidget(host)
        layout.addWidget(self.results)
        self._localizer.language_changed.connect(self._retranslate)

    def search(self) -> None:
        self.error_label.clear()
        self._message_id = None
        try:
            title = self.search_title.text()
            year = self.search_year.text()
            ManualMovieSearchRequest(title, int(year) if year.strip() else None)
        except (ValueError, TypeError):
            self._set_message(TextId.INVALID_YEAR)
            return
        self._token += 1
        token = self._token
        self._clear_cards()
        self.results.setVisible(False)
        self.results_heading.setVisible(False)
        self.state_label.setText(self._localizer.text(TextId.MANUAL_SEARCH_SEARCHING))
        self.state_label.setVisible(True)
        self.search_button.setEnabled(False)
        try:
            self._runner.submit(
                token,
                lambda: self._actions.manual_movie_search(title, year),
                self._search_succeeded,
                self._search_failed,
            )
        except AttributeError:
            self._search_failed(token, RuntimeError("manual search unavailable"))

    def _search_succeeded(self, token: int, value: object) -> None:
        if token != self._token or not isinstance(value, ManualMovieSearchResult):
            return
        self.search_button.setEnabled(True)
        self._clear_cards()
        self.state_label.setVisible(False)
        self.results_heading.setVisible(True)
        if not value.candidates:
            self._set_message(TextId.NO_RESULTS)
            return
        candidates = []
        seen: set[tuple[str, str]] = set()
        for candidate in value.candidates:
            identity = (candidate.provider, candidate.external_id)
            if identity in seen:
                continue
            seen.add(identity)
            candidates.append(candidate)
            if len(candidates) == 5:
                break
        for candidate in candidates:
            card = ManualSearchResultCard(candidate, self._localizer, self)
            card.selected.connect(self._select_candidate)
            self._cards.append(card)
            self._results_layout.insertWidget(self._results_layout.count() - 1, card)
        self.results.set_cards(self._cards)
        self.results.setVisible(self.results.count() > 0)
        if self.results.count() > 0:
            estimated = sum(max(card.sizeHint().height(), 130) for card in self._cards) + 8
            self.results.setFixedHeight(min(360, max(150, estimated)))
        self.adjustSize()

    def _search_failed(self, token: int, _error: BaseException) -> None:
        if token != self._token:
            return
        self.search_button.setEnabled(True)
        self._clear_cards()
        self.results.setVisible(False)
        self.results_heading.setVisible(False)
        self.state_label.setVisible(False)
        self._set_message(TextId.MANUAL_SEARCH_PROVIDER_FAILED)

    def _select_candidate(self, candidate: object) -> None:
        if isinstance(candidate, MovieCandidate) and any(
            card.candidate is candidate for card in self._cards
        ):
            self.candidate_selected.emit(candidate)
            self.accept()

    def select_current(self) -> None:
        item = self.results.currentItem()
        if item is not None:
            self._select_candidate(item.data(Qt.ItemDataRole.UserRole))

    def _clear_cards(self) -> None:
        for card in self._cards:
            self._results_layout.removeWidget(card)
            card.setParent(None)
            card.deleteLater()
        self._cards.clear()
        self.results.set_cards(self._cards)
        self.results.setFixedHeight(0)

    def _set_message(self, message_id: TextId) -> None:
        self._message_id = message_id
        self.error_label.setText(self._localizer.text(message_id))

    def _retranslate(self, _language) -> None:
        self.setWindowTitle(self._localizer.text(TextId.SEARCH_TMDB))
        self.search_year.setPlaceholderText(self._localizer.text(TextId.YEAR))
        if self._message_id is not None:
            self.error_label.setText(self._localizer.text(self._message_id))
        if not self.state_label.isHidden():
            self.state_label.setText(self._localizer.text(TextId.MANUAL_SEARCH_SEARCHING))

    def closeEvent(self, event: QCloseEvent) -> None:
        self._token += 1
        super().closeEvent(event)
