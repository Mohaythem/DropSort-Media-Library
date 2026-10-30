from __future__ import annotations

import math

from PySide6.QtCore import Qt, QSize, Signal
from PySide6.QtWidgets import (
    QComboBox,
    QFrame,
    QGridLayout,
    QHBoxLayout,
    QLabel,
    QPushButton,
    QSizePolicy,
    QWidget,
)

from dropsort.application.dto.movie_import import (
    ImportProposalReason,
    ImportProposalStatus,
    MovieImportProposal,
)
from dropsort.metadata.contracts import MovieCandidate
from dropsort.ui.common.formatting import to_western_numerals
from dropsort.ui.common.icon import FluentIconName, set_fluent_icon
from dropsort.ui.common.theme import SPACE_4, SPACE_8, SPACE_12, SPACE_SMALL
from dropsort.ui.localization import TextId, UiLocalizer


IMPORT_TITLE_MIN_WIDTH = 240
IMPORT_YEAR_WIDTH = 72
IMPORT_RESOLUTION_WIDTH = 88
IMPORT_STATUS_WIDTH = 156
IMPORT_ACTION_WIDTH = 272
IMPORT_ACTION_HEIGHT = 38

_STATUS_TEXT = {
    ImportProposalStatus.MATCH_PROPOSED: TextId.IMPORT_MATCH_PROPOSED,
    ImportProposalStatus.REVIEW_REQUIRED: TextId.IMPORT_REVIEW_REQUIRED,
    ImportProposalStatus.NO_MATCH: TextId.IMPORT_NO_MATCH,
    ImportProposalStatus.METADATA_UNAVAILABLE: TextId.IMPORT_METADATA_UNAVAILABLE,
    ImportProposalStatus.ALREADY_IN_LIBRARY: TextId.IMPORT_ALREADY_LIBRARY,
    ImportProposalStatus.MANUAL_SELECTION: TextId.IMPORT_MANUAL_SELECTED,
}


class ImportReviewRow(QFrame):
    """Stable five-region review row with bounded secondary information."""

    confirm_requested = Signal(object, object)
    dismiss_requested = Signal(object)
    settings_requested = Signal()
    manual_search_requested = Signal(object, object)

    def __init__(
        self,
        proposal: MovieImportProposal,
        parent=None,
        *,
        localizer: UiLocalizer | None = None,
    ) -> None:
        super().__init__(parent)
        self._localizer = localizer or UiLocalizer()
        self.proposal = proposal
        self.setObjectName("importReviewRow")

        layout = QGridLayout(self)
        layout.setContentsMargins(SPACE_12, SPACE_SMALL, SPACE_12, SPACE_SMALL)
        layout.setHorizontalSpacing(SPACE_12)
        layout.setVerticalSpacing(SPACE_8)
        layout.setColumnStretch(0, 1)
        layout.setColumnMinimumWidth(0, IMPORT_TITLE_MIN_WIDTH)
        layout.setColumnMinimumWidth(1, IMPORT_YEAR_WIDTH)
        layout.setColumnMinimumWidth(2, IMPORT_RESOLUTION_WIDTH)
        layout.setColumnMinimumWidth(3, IMPORT_STATUS_WIDTH)
        layout.setColumnMinimumWidth(4, IMPORT_ACTION_WIDTH)
        self.setSizePolicy(
            QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Minimum
        )

        title = proposal.discovery.path.name
        parsed = proposal.discovery.parsed_media
        if parsed and parsed.title:
            title = parsed.title
        self.title_label = QLabel(title, self)
        self.title_label.setObjectName("importTitleLabel")
        self.title_label.setProperty("role", "rowTitle")
        self.title_label.setMinimumWidth(0)
        self.title_label.setSizePolicy(
            QSizePolicy.Policy.Ignored, QSizePolicy.Policy.Preferred
        )
        self.title_label.setWordWrap(False)
        self.title_label.setToolTip(title)
        layout.addWidget(self.title_label, 0, 0, Qt.AlignmentFlag.AlignTop)

        self.year_label = QLabel(_compact_year(parsed.year if parsed else None), self)
        self.year_label.setObjectName("importYearLabel")
        self.year_label.setProperty("role", "muted")
        self.year_label.setProperty("importColumn", True)
        self.year_label.setFixedWidth(IMPORT_YEAR_WIDTH)
        self.year_label.setAlignment(
            Qt.AlignmentFlag.AlignTop | Qt.AlignmentFlag.AlignLeft
        )
        self._localizer.mark_ltr(self.year_label)
        layout.addWidget(self.year_label, 0, 1, Qt.AlignmentFlag.AlignTop)

        self.resolution_label = QLabel(
            (parsed.resolution if parsed else None) or "--", self
        )
        self.resolution_label.setObjectName("importResolutionLabel")
        self.resolution_label.setProperty("role", "muted")
        self.resolution_label.setProperty("importColumn", True)
        self.resolution_label.setFixedWidth(IMPORT_RESOLUTION_WIDTH)
        self.resolution_label.setAlignment(
            Qt.AlignmentFlag.AlignTop | Qt.AlignmentFlag.AlignLeft
        )
        self._localizer.mark_ltr(self.resolution_label)
        layout.addWidget(self.resolution_label, 0, 2, Qt.AlignmentFlag.AlignTop)

        self._status_text_id = _status_text_id(proposal)
        self._status = QLabel(self._localizer.text(self._status_text_id), self)
        self._status.setObjectName("importStatusLabel")
        self._status.setProperty("proposalStatus", proposal.status.value)
        self._status.setProperty("importColumn", True)
        self._status.setFixedWidth(IMPORT_STATUS_WIDTH)
        self._status.setWordWrap(True)
        self._status.setAlignment(
            Qt.AlignmentFlag.AlignTop | Qt.AlignmentFlag.AlignLeft
        )
        layout.addWidget(self._status, 0, 3, Qt.AlignmentFlag.AlignTop)

        action_host = QWidget(self)
        action_host.setObjectName("importActionHost")
        action_host.setProperty("importColumn", True)
        action_host.setFixedWidth(IMPORT_ACTION_WIDTH)
        action_host.setFixedHeight(IMPORT_ACTION_HEIGHT)
        action_layout = QHBoxLayout(action_host)
        action_layout.setContentsMargins(0, 0, 0, 0)
        action_layout.setSpacing(SPACE_4)
        action_layout.setAlignment(
            Qt.AlignmentFlag.AlignRight | Qt.AlignmentFlag.AlignVCenter
        )

        confirmable = (
            proposal.discovery.classification.value == "MOVIE_CANDIDATE"
            and proposal.status is not ImportProposalStatus.ALREADY_IN_LIBRARY
        )

        self.import_button = QPushButton(action_host)
        self.import_button.setObjectName("confirmImportButton")
        self.import_button.setProperty("role", "primaryAction")
        self.import_button.setSizePolicy(
            QSizePolicy.Policy.Minimum, QSizePolicy.Policy.Fixed
        )
        self.import_button.setFixedHeight(IMPORT_ACTION_HEIGHT)
        set_fluent_icon(self.import_button, FluentIconName.ADD_MOVIES)
        action_layout.addWidget(self.import_button)
        self.import_button.clicked.connect(self._request_confirmation)
        self._localizer.bind_text(self.import_button, TextId.IMPORT_ADD_ACTION)
        self.import_button.setToolTip(self._localizer.text(TextId.ADD_TO_LIBRARY))
        self.import_button.setVisible(confirmable)

        self.manual_search_button = QPushButton(action_host)
        self.manual_search_button.setObjectName("editSearchButton")
        self.manual_search_button.setProperty("role", "secondaryAction")
        self.manual_search_button.setSizePolicy(
            QSizePolicy.Policy.Minimum, QSizePolicy.Policy.Fixed
        )
        self.manual_search_button.setFixedHeight(IMPORT_ACTION_HEIGHT)
        set_fluent_icon(self.manual_search_button, FluentIconName.SEARCH)
        action_layout.addWidget(self.manual_search_button)
        self._localizer.bind_text(
            self.manual_search_button, TextId.IMPORT_SEARCH_ACTION
        )
        self.manual_search_button.setToolTip(
            self._localizer.text(TextId.EDIT_SEARCH)
        )
        self.manual_search_button.setVisible(
            proposal.discovery.classification.value == "MOVIE_CANDIDATE"
            and proposal.status
            in {
                ImportProposalStatus.NO_MATCH,
                ImportProposalStatus.METADATA_UNAVAILABLE,
            }
        )
        self.manual_search_button.clicked.connect(
            lambda: self.manual_search_requested.emit(self.proposal, self)
        )

        authentication_missing = (
            ImportProposalReason.METADATA_AUTHENTICATION in set(proposal.reasons)
        )
        self.settings_button = QPushButton(action_host)
        self.settings_button.setObjectName("openMetadataSettingsButton")
        self.settings_button.setProperty("role", "iconAction")
        self.settings_button.setSizePolicy(
            QSizePolicy.Policy.Fixed, QSizePolicy.Policy.Fixed
        )
        self.settings_button.setFixedSize(
            IMPORT_ACTION_HEIGHT, IMPORT_ACTION_HEIGHT
        )
        self.settings_button.setContentsMargins(0, 0, 0, 0)
        self.settings_button.setIconSize(QSize(16, 16))
        set_fluent_icon(self.settings_button, FluentIconName.SETTINGS)
        self.settings_button.setText("")
        self.settings_button.clicked.connect(self.settings_requested.emit)
        self.settings_button.setToolTip(self._localizer.text(TextId.OPEN_SETTINGS))
        action_layout.addStretch(1)
        action_layout.addWidget(self.settings_button)
        self.settings_button.setVisible(authentication_missing)

        self.dismiss_button = QPushButton(action_host)
        self.dismiss_button.setObjectName("dismissProposalButton")
        self.dismiss_button.setProperty("role", "iconAction")
        self.dismiss_button.setFixedSize(
            IMPORT_ACTION_HEIGHT, IMPORT_ACTION_HEIGHT
        )
        self.dismiss_button.setContentsMargins(0, 0, 0, 0)
        self.dismiss_button.setIconSize(QSize(16, 16))
        set_fluent_icon(self.dismiss_button, FluentIconName.DELETE)
        self.dismiss_button.setText("")
        self.dismiss_button.setToolTip(self._localizer.text(TextId.DISMISS_PROPOSAL))
        self.dismiss_button.clicked.connect(lambda: self.dismiss_requested.emit(self))
        action_layout.addWidget(self.dismiss_button)
        layout.addWidget(action_host, 0, 4, 3, 1, Qt.AlignmentFlag.AlignVCenter)

        self._action_buttons = (
            (self.import_button, 72),
            (self.manual_search_button, 88),
        )
        self._refresh_action_button_sizes()
        self._localizer.bind_retranslator(self, self._retranslate)

        self._candidate_divider = QFrame(self)
        self._candidate_divider.setObjectName("importCandidateDivider")
        self._candidate_divider.setProperty("role", "rowDivider")
        self._candidate_divider.setFixedHeight(1)

        self.candidate_selector = QComboBox(self)
        self.candidate_selector.setObjectName("candidateSelector")
        self.candidate_selector.setSizePolicy(
            QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Fixed
        )
        self.candidate_selector.setMinimumWidth(0)
        self.candidate_selector.setMinimumHeight(IMPORT_ACTION_HEIGHT)
        self.candidate_selector.setMinimumContentsLength(20)
        self.candidate_selector.setSizeAdjustPolicy(
            QComboBox.SizeAdjustPolicy.AdjustToMinimumContentsLengthWithIcon
        )
        self._populate_candidates(proposal)
        if proposal.proposed_candidate is not None:
            for index in range(self.candidate_selector.count()):
                if self.candidate_selector.itemData(index) == proposal.proposed_candidate:
                    self.candidate_selector.setCurrentIndex(index)
                    break
        show_candidate = bool(proposal.candidates)
        self._candidate_divider.setVisible(show_candidate)
        self.candidate_selector.setVisible(show_candidate)
        self.candidate_selector.setProperty("role", "candidateReviewControl")
        layout.addWidget(self._candidate_divider, 1, 0, 1, 4)
        layout.addWidget(
            self.candidate_selector,
            2,
            0,
            1,
            4,
            Qt.AlignmentFlag.AlignVCenter,
        )
        self.setMinimumHeight(self.sizeHint().height())

    @property
    def selected_candidate(self) -> MovieCandidate | None:
        value = self.candidate_selector.currentData()
        return value if isinstance(value, MovieCandidate) else None

    @property
    def status_text(self) -> str:
        return self._status.text()

    @property
    def explanation_text(self) -> str:
        return ""

    @property
    def can_import(self) -> bool:
        return not self.import_button.isHidden() and self.import_button.isEnabled()

    def mark_imported(self) -> None:
        self._status_text_id = TextId.ADDED_TO_LIBRARY
        self._status.setText(self._localizer.text(TextId.ADDED_TO_LIBRARY))
        self.import_button.setEnabled(False)
        self.candidate_selector.setEnabled(False)

    def mark_import_failed(self, message: str) -> None:
        self._status_text_id = None
        self._status.setText(message)
        self.import_button.setEnabled(True)
        self.candidate_selector.setEnabled(True)

    def set_manual_proposal(self, proposal: MovieImportProposal) -> None:
        """Replace the informational proposal after an explicit result selection."""
        self.proposal = proposal
        self._populate_candidates(proposal)
        self._candidate_divider.setVisible(True)
        self.candidate_selector.setVisible(True)
        self.import_button.setVisible(True)
        self.import_button.setEnabled(True)
        self.manual_search_button.setVisible(False)
        self._status.setProperty("proposalStatus", proposal.status.value)
        self._status_text_id = TextId.IMPORT_MANUAL_SELECTED
        self._status.setText(self._localizer.text(TextId.IMPORT_MANUAL_SELECTED))
        self.style().unpolish(self._status)
        self.style().polish(self._status)

    def _populate_candidates(self, proposal: MovieImportProposal) -> None:
        """Render candidates by rating without mutating matching decisions."""

        self.candidate_selector.clear()
        for candidate in _display_candidates(proposal.candidates):
            label = _candidate_label(candidate)
            self.candidate_selector.addItem(label, candidate)
            self.candidate_selector.setItemData(
                self.candidate_selector.count() - 1,
                label,
                Qt.ItemDataRole.ToolTipRole,
            )
        if proposal.proposed_candidate is not None:
            for index in range(self.candidate_selector.count()):
                if self.candidate_selector.itemData(index) == proposal.proposed_candidate:
                    self.candidate_selector.setCurrentIndex(index)
                    break

    def _refresh_action_button_sizes(self, _language=None) -> None:
        for button, baseline in self._action_buttons:
            button.setMinimumWidth(max(baseline, button.sizeHint().width()))

    def _refresh_action_tooltips(self, _language=None) -> None:
        self.import_button.setToolTip(self._localizer.text(TextId.ADD_TO_LIBRARY))
        self.manual_search_button.setToolTip(
            self._localizer.text(TextId.EDIT_SEARCH)
        )
        self.settings_button.setToolTip(self._localizer.text(TextId.OPEN_SETTINGS))
        self.dismiss_button.setToolTip(
            self._localizer.text(TextId.DISMISS_PROPOSAL)
        )

    def _retranslate_status(self, _language=None) -> None:
        if self._status_text_id is not None:
            self._status.setText(self._localizer.text(self._status_text_id))

    def _retranslate(self, language) -> None:
        self._refresh_action_button_sizes(language)
        self._refresh_action_tooltips(language)
        self._retranslate_status(language)

    def _request_confirmation(self) -> None:
        candidate = self.selected_candidate
        self.import_button.setEnabled(False)
        self.candidate_selector.setEnabled(False)
        self._status_text_id = TextId.ADDING_TO_LIBRARY
        self._status.setText(self._localizer.text(TextId.ADDING_TO_LIBRARY))
        self.confirm_requested.emit(self.proposal, candidate)


def _candidate_label(candidate: MovieCandidate) -> str:
    return (
        f"{candidate.title} ({_compact_year(candidate.year)})"
        f"    {_compact_rating(candidate.rating)}"
    )


def _display_candidates(candidates: tuple[MovieCandidate, ...]) -> tuple[MovieCandidate, ...]:
    """Stable presentation order: usable ratings descending, then unrated."""

    rated: list[tuple[MovieCandidate, float]] = []
    unrated: list[MovieCandidate] = []
    for candidate in candidates:
        rating = candidate.rating
        if (
            not isinstance(rating, bool)
            and isinstance(rating, (int, float))
            and math.isfinite(float(rating))
        ):
            rated.append((candidate, float(rating)))
        else:
            unrated.append(candidate)
    rated.sort(key=lambda item: item[1], reverse=True)
    return tuple(candidate for candidate, _rating in rated) + tuple(unrated)


def _compact_year(value: int | None) -> str:
    return str(value) if value is not None else "--"


def _compact_rating(value: float | None) -> str:
    return to_western_numerals(f"{value:.1f}/10") if value is not None else "--"


def _status_text_id(proposal: MovieImportProposal) -> TextId:
    reasons = set(proposal.reasons)
    if ImportProposalReason.TV_EPISODE_NOT_SUPPORTED in reasons:
        return TextId.IMPORT_TV_SKIPPED
    if ImportProposalReason.UNKNOWN_MEDIA in reasons:
        return TextId.IMPORT_UNKNOWN
    if ImportProposalReason.DISCOVERY_ERROR in reasons:
        return TextId.IMPORT_SCAN_ERROR
    return _STATUS_TEXT[proposal.status]
