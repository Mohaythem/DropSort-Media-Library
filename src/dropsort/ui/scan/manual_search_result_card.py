from __future__ import annotations

from PySide6.QtCore import Qt, Signal
from PySide6.QtWidgets import (
    QFrame,
    QHBoxLayout,
    QLabel,
    QPushButton,
    QSizePolicy,
    QVBoxLayout,
)

from dropsort.metadata.contracts import MovieCandidate
from dropsort.ui.common.formatting import format_rating, format_year
from dropsort.ui.common.icon import FluentIconName, set_fluent_icon
from dropsort.ui.common.theme import SPACE_8, SPACE_12, SPACE_16
from dropsort.ui.localization import TextId, UiLocalizer


class ManualSearchResultCard(QFrame):
    """A compact, semantic presentation of one provider-neutral candidate."""

    selected = Signal(object)

    def __init__(self, candidate: MovieCandidate, localizer: UiLocalizer, parent=None) -> None:
        super().__init__(parent)
        self.candidate = candidate
        self._localizer = localizer
        self.setObjectName("manualSearchResultCard")
        self.setProperty("role", "panel")
        self.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Minimum)

        layout = QVBoxLayout(self)
        layout.setContentsMargins(SPACE_12, SPACE_12, SPACE_12, SPACE_12)
        layout.setSpacing(SPACE_8)

        heading = QHBoxLayout()
        heading.setSpacing(SPACE_8)
        self.title_label = QLabel(candidate.title)
        self.title_label.setObjectName("manualSearchResultTitle")
        self.title_label.setProperty("role", "heading")
        self.title_label.setWordWrap(True)
        self.title_label.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Preferred)
        heading.addWidget(self.title_label, 1)
        self.year_label = QLabel()
        self.year_label.setObjectName("manualSearchResultYear")
        self.year_label.setProperty("role", "secondary")
        self.year_label.setAlignment(Qt.AlignmentFlag.AlignRight | Qt.AlignmentFlag.AlignTop)
        self._localizer.mark_ltr(self.year_label)
        heading.addWidget(self.year_label)
        layout.addLayout(heading)

        metadata = QHBoxLayout()
        metadata.setSpacing(SPACE_16)
        self.id_label = QLabel()
        self.id_label.setObjectName("manualSearchResultId")
        self.id_label.setProperty("role", "secondary")
        self._localizer.mark_ltr(self.id_label)
        metadata.addWidget(self.id_label)
        self.rating_label = QLabel()
        self.rating_label.setObjectName("manualSearchResultRating")
        self.rating_label.setProperty("role", "secondary")
        self._localizer.mark_ltr(self.rating_label)
        metadata.addWidget(self.rating_label)
        metadata.addStretch(1)
        layout.addLayout(metadata)

        self.overview_label = QLabel()
        self.overview_label.setObjectName("manualSearchResultOverview")
        self.overview_label.setProperty("role", "muted")
        self.overview_label.setWordWrap(True)
        self.overview_label.setTextFormat(Qt.TextFormat.PlainText)
        self.overview_label.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Preferred)
        self.overview_label.setMaximumHeight(64)
        layout.addWidget(self.overview_label)

        actions = QHBoxLayout()
        actions.addStretch(1)
        self.select_button = QPushButton()
        self.select_button.setObjectName("manualSearchSelectButton")
        self.select_button.setProperty("role", "secondaryAction")
        set_fluent_icon(self.select_button, FluentIconName.MARK_WATCHED)
        self.select_button.setMinimumWidth(88)
        self.select_button.clicked.connect(lambda: self.selected.emit(self.candidate))
        actions.addWidget(self.select_button)
        layout.addLayout(actions)

        self._localizer.language_changed.connect(self._retranslate)
        self._retranslate(self._localizer.language)

    def data(self, role: Qt.ItemDataRole):
        """Small QListWidget-item compatibility surface for existing callers."""

        if role is Qt.ItemDataRole.UserRole:
            return self.candidate
        return None

    def _retranslate(self, _language) -> None:
        self.year_label.setText(format_year(self.candidate.year))
        self.id_label.setText(f"TMDB {self.candidate.external_id}")
        self.rating_label.setText(f"{self._localizer.text(TextId.MANUAL_SEARCH_RATING)} {format_rating(self.candidate.rating)}")
        self.overview_label.setText(
            self.candidate.overview.strip()
            if self.candidate.overview and self.candidate.overview.strip()
            else self._localizer.text(TextId.MANUAL_SEARCH_NO_OVERVIEW)
        )
        self.select_button.setText(self._localizer.text(TextId.MANUAL_SEARCH_SELECT))
