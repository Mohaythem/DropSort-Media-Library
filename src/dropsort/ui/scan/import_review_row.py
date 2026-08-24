from __future__ import annotations

from PySide6.QtCore import Qt, Signal
from PySide6.QtWidgets import (
    QComboBox,
    QFrame,
    QHBoxLayout,
    QLabel,
    QPushButton,
    QSizePolicy,
    QVBoxLayout,
    QWidget,
)

from dropsort.application.dto.movie_import import (
    ImportProposalReason,
    ImportProposalStatus,
    MovieImportProposal,
)
from dropsort.metadata.contracts import MovieCandidate
from dropsort.ui.common.formatting import format_rating, format_year
from dropsort.ui.common.icon import FluentIconName, set_fluent_icon
from dropsort.ui.common.theme import SPACE_4, SPACE_12, SPACE_SMALL
from dropsort.ui.localization import TextId, UiLocalizer


_STATUS_TEXT = {
    ImportProposalStatus.MATCH_PROPOSED: TextId.IMPORT_MATCH_PROPOSED,
    ImportProposalStatus.REVIEW_REQUIRED: TextId.IMPORT_REVIEW_REQUIRED,
    ImportProposalStatus.NO_MATCH: TextId.IMPORT_NO_MATCH,
    ImportProposalStatus.METADATA_UNAVAILABLE: TextId.IMPORT_METADATA_UNAVAILABLE,
    ImportProposalStatus.ALREADY_IN_LIBRARY: TextId.IMPORT_ALREADY_LIBRARY,
    ImportProposalStatus.MANUAL_SELECTION: TextId.IMPORT_MANUAL_SELECTED,
}


class ImportReviewRow(QFrame):
    """Compact review row with table-like density and explicit actions."""

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

        layout = QVBoxLayout(self)
        layout.setContentsMargins(SPACE_12, SPACE_SMALL, SPACE_12, SPACE_SMALL)
        layout.setSpacing(SPACE_SMALL)

        primary = QHBoxLayout()
        primary.setSpacing(SPACE_12)

        title_host = QWidget()
        title_host.setMinimumWidth(0)
        title_host.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Preferred)
        title_layout = QVBoxLayout(title_host)
        title_layout.setContentsMargins(0, 0, 0, 0)
        title_layout.setSpacing(SPACE_4)

        title = proposal.discovery.path.name
        parsed = proposal.discovery.parsed_media
        if parsed and parsed.title:
            title = parsed.title
        title_label = QLabel(title)
        title_label.setObjectName("importTitleLabel")
        title_label.setProperty("role", "rowTitle")
        title_label.setMinimumWidth(0)
        title_label.setSizePolicy(QSizePolicy.Policy.Ignored, QSizePolicy.Policy.Preferred)
        title_label.setWordWrap(False)
        title_label.setToolTip(title)
        title_layout.addWidget(title_label)

        filename = QLabel(proposal.discovery.path.name)
        filename.setObjectName("importFilenameLabel")
        filename.setProperty("role", "muted")
        filename.setTextInteractionFlags(
            Qt.TextInteractionFlag.TextSelectableByMouse
            | Qt.TextInteractionFlag.TextSelectableByKeyboard
        )
        self._localizer.mark_ltr(filename)
        filename.hide()
        title_layout.addWidget(filename)

        path = QLabel(str(proposal.discovery.path))
        path.setObjectName("importPathLabel")
        path.setMinimumWidth(0)
        path.setSizePolicy(QSizePolicy.Policy.Ignored, QSizePolicy.Policy.Preferred)
        path.setToolTip(str(proposal.discovery.path))
        path.setTextInteractionFlags(Qt.TextInteractionFlag.TextSelectableByMouse)
        self._localizer.mark_ltr(path)
        title_layout.addWidget(path)
        primary.addWidget(title_host, 1)

        year = QLabel(format_year(parsed.year if parsed else None))
        year.setObjectName("importYearLabel")
        year.setProperty("role", "muted")
        year.setFixedWidth(72)
        primary.addWidget(year)

        resolution = QLabel((parsed.resolution if parsed else None) or "—")
        resolution.setObjectName("importResolutionLabel")
        resolution.setProperty("role", "muted")
        resolution.setFixedWidth(82)
        self._localizer.mark_ltr(resolution)
        primary.addWidget(resolution)

        self._status = QLabel(_status_text(proposal, self._localizer))
        self._status.setObjectName("importStatusLabel")
        self._status.setProperty("proposalStatus", proposal.status.value)
        self._status.setFixedWidth(112)
        primary.addWidget(self._status)

        action_host = QWidget()
        action_host.setFixedWidth(176)
        action_layout = QHBoxLayout(action_host)
        action_layout.setContentsMargins(0, 0, 0, 0)
        action_layout.setSpacing(SPACE_4)

        confirmable = (
            proposal.discovery.classification.value == "MOVIE_CANDIDATE"
            and proposal.status is not ImportProposalStatus.ALREADY_IN_LIBRARY
        )

        self.import_button = QPushButton()
        self.import_button.setObjectName("confirmImportButton")
        self.import_button.setProperty("role", "primaryAction")
        self.import_button.setSizePolicy(QSizePolicy.Policy.Maximum, QSizePolicy.Policy.Fixed)
        set_fluent_icon(self.import_button, FluentIconName.ADD_MOVIES)
        self.import_button.setVisible(confirmable)
        self.import_button.clicked.connect(self._request_confirmation)
        self._localizer.bind_text(self.import_button, TextId.ADD_TO_LIBRARY)
        action_layout.addWidget(self.import_button)

        self.manual_search_button = QPushButton()
        self.manual_search_button.setObjectName("editSearchButton")
        self.manual_search_button.setProperty("role", "secondaryAction")
        self.manual_search_button.setSizePolicy(
            QSizePolicy.Policy.Maximum, QSizePolicy.Policy.Fixed
        )
        set_fluent_icon(self.manual_search_button, FluentIconName.SEARCH)
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
        self._localizer.bind_text(self.manual_search_button, TextId.EDIT_SEARCH)
        action_layout.addWidget(self.manual_search_button)

        authentication_missing = (
            ImportProposalReason.METADATA_AUTHENTICATION in set(proposal.reasons)
        )
        self.settings_button = QPushButton()
        self.settings_button.setObjectName("openMetadataSettingsButton")
        self.settings_button.setProperty("role", "secondaryAction")
        self.settings_button.setSizePolicy(
            QSizePolicy.Policy.Maximum, QSizePolicy.Policy.Fixed
        )
        set_fluent_icon(self.settings_button, FluentIconName.SETTINGS)
        self.settings_button.setVisible(authentication_missing)
        self.settings_button.clicked.connect(self.settings_requested.emit)
        self._localizer.bind_text(self.settings_button, TextId.OPEN_SETTINGS)
        action_layout.addWidget(self.settings_button)

        self.dismiss_button = QPushButton()
        self.dismiss_button.setObjectName("dismissProposalButton")
        self.dismiss_button.setProperty("role", "compactAction")
        self.dismiss_button.setFixedSize(32, 32)
        set_fluent_icon(self.dismiss_button, FluentIconName.DELETE)
        self.dismiss_button.setText("")
        self.dismiss_button.setToolTip(self._localizer.text(TextId.DISMISS_PROPOSAL))
        self.dismiss_button.clicked.connect(lambda: self.dismiss_requested.emit(self))
        self._localizer.language_changed.connect(
            lambda _language: self.dismiss_button.setToolTip(
                self._localizer.text(TextId.DISMISS_PROPOSAL)
            )
        )
        action_layout.addWidget(self.dismiss_button)
        primary.addWidget(action_host)
        layout.addLayout(primary)

        self.candidate_selector = QComboBox()
        self.candidate_selector.setObjectName("candidateSelector")
        for candidate in proposal.candidates:
            self.candidate_selector.addItem(_candidate_label(candidate), candidate)
        if proposal.proposed_candidate is not None:
            for index in range(self.candidate_selector.count()):
                if self.candidate_selector.itemData(index) == proposal.proposed_candidate:
                    self.candidate_selector.setCurrentIndex(index)
                    break
        needs_candidate_choice = confirmable and (
            proposal.status in {
                ImportProposalStatus.REVIEW_REQUIRED,
                ImportProposalStatus.MANUAL_SELECTION,
            }
            or len(proposal.candidates) > 1
        )
        self.candidate_selector.setVisible(needs_candidate_choice)
        layout.addWidget(self.candidate_selector)

        self._explanation = QLabel(_explanation(proposal, self._localizer))
        self._explanation.setObjectName("importExplanationLabel")
        self._explanation.setProperty("role", "muted")
        self._explanation.setWordWrap(True)
        self._explanation.setVisible(
            proposal.status is not ImportProposalStatus.MATCH_PROPOSED
            or len(proposal.candidates) > 1
        )
        layout.addWidget(self._explanation)

    @property
    def selected_candidate(self) -> MovieCandidate | None:
        value = self.candidate_selector.currentData()
        return value if isinstance(value, MovieCandidate) else None

    @property
    def status_text(self) -> str:
        return self._status.text()

    @property
    def explanation_text(self) -> str:
        return self._explanation.text()

    @property
    def can_import(self) -> bool:
        return not self.import_button.isHidden() and self.import_button.isEnabled()

    def mark_imported(self) -> None:
        self._status.setText(self._localizer.text(TextId.ADDED_TO_LIBRARY))
        self.import_button.setEnabled(False)
        self.candidate_selector.setEnabled(False)

    def mark_import_failed(self, message: str) -> None:
        self._status.setText(message)
        self.import_button.setEnabled(True)
        self.candidate_selector.setEnabled(True)

    def set_manual_proposal(self, proposal: MovieImportProposal) -> None:
        """Replace the informational proposal after an explicit result selection."""
        self.proposal = proposal
        self.candidate_selector.clear()
        for candidate in proposal.candidates:
            self.candidate_selector.addItem(_candidate_label(candidate), candidate)
        self.candidate_selector.setVisible(True)
        self._explanation.setVisible(True)
        self.import_button.setVisible(True)
        self.import_button.setEnabled(True)
        self.manual_search_button.setVisible(False)
        self._status.setProperty("proposalStatus", proposal.status.value)
        self._status.setText(self._localizer.text(TextId.IMPORT_MANUAL_SELECTED))
        self._explanation.setText(self._localizer.text(TextId.IMPORT_MANUAL_EXPLANATION))
        self.style().unpolish(self._status)
        self.style().polish(self._status)

    def _request_confirmation(self) -> None:
        candidate = self.selected_candidate
        self.import_button.setEnabled(False)
        self.candidate_selector.setEnabled(False)
        self._status.setText(self._localizer.text(TextId.ADDING_TO_LIBRARY))
        self.confirm_requested.emit(self.proposal, candidate)


def _candidate_label(candidate: MovieCandidate) -> str:
    return " — ".join(
        (
            f"{candidate.title} ({format_year(candidate.year)})",
            candidate.provider.upper(),
            format_rating(candidate.rating),
        )
    )


def _explanation(proposal: MovieImportProposal, localizer: UiLocalizer) -> str:
    if proposal.match_decision is not None:
        confidence = round(proposal.match_decision.confidence * 100)
        reasons = ", ".join(
            " ".join(reason.value.split("_")).title()
            for reason in proposal.match_decision.reasons
        )
        return localizer.text(
            TextId.IMPORT_CONFIDENCE, confidence=confidence, reasons=reasons
        )
    reasons = set(proposal.reasons)
    if ImportProposalReason.TV_EPISODE_NOT_SUPPORTED in reasons:
        return localizer.text(TextId.IMPORT_TV_EXPLANATION)
    if ImportProposalReason.UNKNOWN_MEDIA in reasons:
        return localizer.text(TextId.IMPORT_UNKNOWN_EXPLANATION)
    if ImportProposalReason.DISCOVERY_ERROR in reasons:
        return localizer.text(TextId.IMPORT_DISCOVERY_EXPLANATION)
    if ImportProposalReason.METADATA_AUTHENTICATION in reasons:
        return localizer.text(TextId.IMPORT_AUTH_EXPLANATION)
    if ImportProposalReason.METADATA_RATE_LIMIT in reasons:
        return localizer.text(TextId.IMPORT_RATE_EXPLANATION)
    if ImportProposalReason.METADATA_RESPONSE_ERROR in reasons:
        return localizer.text(TextId.IMPORT_RESPONSE_EXPLANATION)
    messages = {
        ImportProposalStatus.NO_MATCH: TextId.IMPORT_NO_MATCH_EXPLANATION,
        ImportProposalStatus.METADATA_UNAVAILABLE: TextId.IMPORT_UNAVAILABLE_EXPLANATION,
        ImportProposalStatus.ALREADY_IN_LIBRARY: TextId.IMPORT_ALREADY_EXPLANATION,
        ImportProposalStatus.MANUAL_SELECTION: TextId.IMPORT_MANUAL_EXPLANATION,
    }
    return localizer.text(
        messages.get(proposal.status, TextId.IMPORT_REVIEW_EXPLANATION)
    )


def _status_text(proposal: MovieImportProposal, localizer: UiLocalizer) -> str:
    reasons = set(proposal.reasons)
    if ImportProposalReason.TV_EPISODE_NOT_SUPPORTED in reasons:
        return localizer.text(TextId.IMPORT_TV_SKIPPED)
    if ImportProposalReason.UNKNOWN_MEDIA in reasons:
        return localizer.text(TextId.IMPORT_UNKNOWN)
    if ImportProposalReason.DISCOVERY_ERROR in reasons:
        return localizer.text(TextId.IMPORT_SCAN_ERROR)
    return localizer.text(_STATUS_TEXT[proposal.status])
