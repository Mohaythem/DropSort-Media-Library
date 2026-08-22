from __future__ import annotations

from pathlib import Path
from enum import StrEnum

from PySide6.QtCore import Qt, Signal
from PySide6.QtGui import QCloseEvent
from PySide6.QtWidgets import (
    QDialog,
    QFileDialog,
    QFrame,
    QGridLayout,
    QHBoxLayout,
    QLabel,
    QProgressBar,
    QPushButton,
    QScrollArea,
    QVBoxLayout,
    QWidget,
)

from dropsort.application.dto.reconciliation import (
    LibraryReconciliationProgress,
    RelinkPreview,
    RelinkResult,
)
from dropsort.application.dto.library_health import (
    LibraryHealthProgress,
    MetadataHealthIssue,
    MetadataHealthItem,
    MetadataHealthStatus,
    MetadataProviderError,
)
from dropsort.application.errors import (
    LibraryReconciliationCancelled,
    RelinkError,
    RelinkValidationError,
)
from dropsort.application.use_cases import ReconciliationCancellation
from dropsort.media.parser import SUPPORTED_VIDEO_EXTENSIONS
from dropsort.ui.common.tasks import TaskRunner
from dropsort.ui.common.icon import FluentIconName, set_fluent_icon
from dropsort.ui.common.theme import SPACE_4, SPACE_8, SPACE_12, SPACE_16, SPACE_24
from dropsort.ui.contracts import ReconciliationUiActions
from dropsort.ui.localization import TextId, UiLocalizer


def _progress_percentage(checked: int, total: int) -> int:
    if total <= 0:
        return 100 if checked else 0
    return min(100, max(0, round(checked * 100 / total)))


class LibraryFileCheckDialog(QDialog):
    completed = Signal(object)

    class State(StrEnum):
        IDLE = "IDLE"
        RUNNING = "RUNNING"
        COMPLETED_SUCCESS = "COMPLETED_SUCCESS"
        COMPLETED_WITH_ISSUES = "COMPLETED_WITH_ISSUES"
        COMPLETED = "COMPLETED_SUCCESS"
        FAILED = "FAILED"
        CANCELLED = "CANCELLED"

    def __init__(
        self,
        actions: ReconciliationUiActions,
        runner: TaskRunner,
        parent=None,
        *,
        localizer: UiLocalizer | None = None,
    ) -> None:
        super().__init__(parent)
        self.setObjectName("libraryCheckDialog")
        self.setMinimumSize(420, 220)
        self.resize(460, 250)
        self._localizer = localizer or UiLocalizer()
        self.setWindowTitle(self._localizer.text(TextId.CHECK_FILES_TITLE))
        self._actions = actions
        self._runner = runner
        self._token = 0
        self._cancellation: ReconciliationCancellation | None = None
        self._state = self.State.IDLE
        self._external_job = False
        self._queued_behind_reconciliation = False
        self._last_health_result: LibraryHealthProgress | None = None
        self._last_file_result: LibraryReconciliationProgress | None = None
        layout = QVBoxLayout(self)
        layout.setContentsMargins(SPACE_24, SPACE_16, SPACE_24, SPACE_16)
        layout.setSpacing(SPACE_12)
        self._status = QLabel()
        self._status.setObjectName("libraryCheckStatusLabel")
        self._status.setProperty("role", "heading")
        layout.addWidget(self._status)

        self._idle_description = QLabel()
        self._idle_description.setObjectName("libraryCheckIdleDescription")
        self._idle_description.setProperty("role", "secondary")
        self._idle_description.setWordWrap(True)
        self._localizer.bind_text(
            self._idle_description, TextId.CHECK_LIBRARY_IDLE_DESCRIPTION
        )
        layout.addWidget(self._idle_description)

        self._failure_description = QLabel()
        self._failure_description.setObjectName("libraryCheckFailureLabel")
        self._failure_description.setProperty("role", "secondary")
        self._failure_description.setWordWrap(True)
        self._failure_description.hide()
        self._localizer.bind_text(
            self._failure_description, TextId.CHECK_LIBRARY_FAILURE_DESCRIPTION
        )
        layout.addWidget(self._failure_description)

        progress_row = QHBoxLayout()
        self._progress = QProgressBar()
        self._progress.setObjectName("libraryCheckProgressBar")
        self._progress.setTextVisible(False)
        progress_row.addWidget(self._progress, 1)
        self._progress_percent = QLabel("0%")
        self._progress_percent.setObjectName("libraryCheckPercentageLabel")
        self._progress_percent.setMinimumWidth(42)
        self._progress_percent.setAlignment(Qt.AlignmentFlag.AlignRight | Qt.AlignmentFlag.AlignVCenter)
        self._progress_percent.setLayoutDirection(Qt.LayoutDirection.LeftToRight)
        self._localizer.mark_ltr(self._progress_percent)
        progress_row.addWidget(self._progress_percent)
        layout.addLayout(progress_row)

        self._summary_panel = QFrame()
        self._summary_panel.setObjectName("checkLibrarySummaryPanel")
        self._summary_panel.setProperty("role", "panel")
        summary_layout = QVBoxLayout(self._summary_panel)
        summary_layout.setContentsMargins(SPACE_12, SPACE_8, SPACE_12, SPACE_8)
        summary_layout.setSpacing(SPACE_4)
        self._summary = QLabel()
        self._summary.setObjectName("libraryCheckSummaryLabel")
        self._summary.setProperty("role", "secondary")
        self._summary.setWordWrap(True)
        summary_layout.addWidget(self._summary)
        self._summary_panel.hide()
        layout.addWidget(self._summary_panel)

        self._issues = QLabel()
        self._issues.setObjectName("libraryCheckIssuesLabel")
        self._issues.hide()
        layout.addWidget(self._issues)

        self._issues_scroll = QScrollArea()
        self._issues_scroll.setObjectName("checkLibraryIssuesScroll")
        self._issues_scroll.setWidgetResizable(True)
        self._issues_host = QWidget()
        self._issues_layout = QVBoxLayout(self._issues_host)
        self._issues_layout.setContentsMargins(0, 0, 0, 0)
        self._issues_layout.setSpacing(SPACE_8)
        self._issues_layout.setAlignment(Qt.AlignmentFlag.AlignTop)
        self._issues_scroll.setWidget(self._issues_host)
        self._issues_scroll.hide()
        layout.addWidget(self._issues_scroll, 1)

        buttons = QHBoxLayout()
        self._start = QPushButton()
        self._start.setObjectName("startLibraryCheckButton")
        self._start.setProperty("role", "primaryAction")
        set_fluent_icon(self._start, FluentIconName.CHECK_LIBRARY)
        self._cancel = QPushButton()
        self._cancel.setObjectName("cancelLibraryCheckButton")
        self._cancel.setProperty("role", "secondaryAction")
        self._localizer.bind_text(self._start, TextId.CHECK_LIBRARY_FILES)
        self._localizer.bind_text(self._cancel, TextId.CHECK_FILES_CANCEL)
        self._close = QPushButton()
        self._close.setObjectName("closeLibraryCheckButton")
        self._close.setProperty("role", "secondaryAction")
        self._localizer.bind_text(self._close, TextId.CLOSE)
        self._close.clicked.connect(self.accept)
        buttons.addWidget(self._start)
        buttons.addWidget(self._cancel)
        buttons.addWidget(self._close)
        layout.addLayout(buttons)
        self._start.clicked.connect(self.start_check)
        self._cancel.clicked.connect(self.cancel_check)
        self._set_idle_state()

    @property
    def status_text(self) -> str:
        return self._status.text()

    @property
    def state(self) -> "LibraryFileCheckDialog.State":
        return self._state

    @property
    def is_running(self) -> bool:
        return self._state is self.State.RUNNING

    def start_check(self) -> None:
        self._token += 1
        token = self._token
        self._cancellation = ReconciliationCancellation()
        self._external_job = False
        self._queued_behind_reconciliation = False
        self._state = self.State.RUNNING
        self._reset_result_panel()
        self._set_running_state()
        action = getattr(self._actions, "check_library", None)
        if not callable(action):
            action = self._actions.reconcile_library_files
        self._runner.submit_progressive(
            token,
            lambda report: action(
                progress=report,
                cancellation=self._cancellation,
            ),
            self._on_progress,
            self._on_success,
            self._on_failure,
        )

    def attach_to_existing(
        self,
        token: int,
        cancellation: ReconciliationCancellation,
    ) -> None:
        """Subscribe this dialog to the already-running coalesced check."""
        self._token = token
        self._cancellation = cancellation
        self._external_job = True
        self._state = self.State.RUNNING
        self._queued_behind_reconciliation = False
        self._set_running_state()

    def wait_for_automatic_file_check(self) -> None:
        """Queue the explicit metadata-aware check behind startup reconciliation."""
        self._state = self.State.IDLE
        self._queued_behind_reconciliation = True
        self._set_idle_state()
        self._start.setEnabled(False)
        self._status.setText(self._localizer.text(TextId.CHECK_FILES_ALREADY_RUNNING))

    def cancel_check(self) -> None:
        if self._state is not self.State.RUNNING or self._cancellation is None:
            return
        self._cancellation.cancel()
        self._status.setText(self._localizer.text(TextId.CHECK_FILES_CANCELLING))
        self._cancel.setEnabled(False)

    def invalidate_pending(self) -> None:
        self._token += 1
        if not self._external_job:
            self.cancel_check()
        else:
            self._cancellation = None
            self._state = self.State.IDLE
            self._set_idle_state()

    def closeEvent(self, event: QCloseEvent) -> None:
        self.invalidate_pending()
        super().closeEvent(event)

    def keyPressEvent(self, event) -> None:
        """Keep Escape safe while a check is active and useful when idle."""

        if event.key() == Qt.Key.Key_Escape:
            if self.is_running:
                self.cancel_check()
            else:
                self.reject()
            event.accept()
            return
        super().keyPressEvent(event)

    def _on_progress(self, token: int, value: object) -> None:
        if token != self._token or not isinstance(
            value, (LibraryReconciliationProgress, LibraryHealthProgress)
        ):
            return
        self._render(value)

    def _on_success(self, token: int, value: object) -> None:
        if token != self._token or not isinstance(
            value, (LibraryReconciliationProgress, LibraryHealthProgress)
        ):
            return
        if isinstance(value, LibraryHealthProgress):
            self._last_health_result = value
            has_issues = self._health_has_issues(value)
            self._state = (
                self.State.COMPLETED_WITH_ISSUES
                if has_issues
                else self.State.COMPLETED_SUCCESS
            )
        else:
            self._last_file_result = value
            self._state = (
                self.State.COMPLETED_WITH_ISSUES
                if value.missing or value.errors
                else self.State.COMPLETED_SUCCESS
            )
        # Render the authoritative terminal counters as well as the final state.
        self._render(value)
        if isinstance(value, LibraryHealthProgress):
            self._render_health_summary(value)
            self._render_health_details(value)
            self._status.setText(
                self._localizer.text(
                    TextId.CHECK_LIBRARY_COMPLETE_TITLE
                    if self._health_has_issues(value)
                    else TextId.CHECK_LIBRARY_HEALTHY_TITLE
                )
            )
            self._set_terminal_controls(
                start_text=TextId.CHECK_LIBRARY_AGAIN,
                start_role="dialogSecondaryAction",
                close_role="dialogCloseAction",
            )
        else:
            self._render_file_summary(value)
            self._status.setText(
                self._localizer.text(TextId.CHECK_FILES_COMPLETE,
                                     present=value.present,
                                     missing=value.missing,
                                     errors=value.errors)
            )
            self._set_terminal_controls(
                start_text=TextId.CHECK_LIBRARY_AGAIN,
                start_role="dialogSecondaryAction",
                close_role="dialogCloseAction",
            )
        self._cancellation = None
        self._external_job = False
        self.completed.emit(value)

    def _on_failure(self, token: int, error: BaseException) -> None:
        if token != self._token:
            return
        if isinstance(error, LibraryReconciliationCancelled):
            self._state = self.State.CANCELLED
            self._reset_result_panel()
            self._summary.setText(
                self._localizer.text(TextId.CHECK_LIBRARY_CANCELLED_DESCRIPTION)
            )
            self._summary_panel.show()
            self._status.setText(self._localizer.text(TextId.CHECK_FILES_CANCELLED))
            start_text = TextId.CHECK_LIBRARY_AGAIN
            start_role = "dialogSecondaryAction"
        else:
            self._state = self.State.FAILED
            self._reset_result_panel()
            self._status.setText(self._localizer.text(TextId.CHECK_LIBRARY_FAILURE_TITLE))
            self._failure_description.show()
            start_text = TextId.CHECK_LIBRARY_TRY_AGAIN
            start_role = "primaryAction"
        self._set_terminal_controls(
            start_text=start_text,
            start_role=start_role,
            close_role="dialogCloseAction" if self._state is self.State.CANCELLED else "secondaryAction",
        )
        self._cancellation = None
        self._external_job = False

    def _render(
        self,
        value: LibraryReconciliationProgress | LibraryHealthProgress,
        *,
        prefix: str | None = None,
    ) -> None:
        if isinstance(value, LibraryHealthProgress):
            prefix = prefix or self._localizer.text(TextId.CHECK_LIBRARY_RUNNING_STATUS)
            self._progress.setMaximum(max(1, value.total))
            self._progress.setValue(value.checked)
            self._progress_percent.setText(f"{_progress_percentage(value.checked, value.total)}%")
            self._status.setText(prefix)
            return
        prefix = prefix or self._localizer.text(TextId.CHECK_FILES_RUNNING)
        self._progress.setMaximum(max(1, value.total))
        self._progress.setValue(value.checked)
        self._progress_percent.setText(f"{_progress_percentage(value.checked, value.total)}%")
        self._status.setText(prefix)

    def _render_health_summary(self, value: LibraryHealthProgress) -> None:
        lines = [
            self._localizer.text(
                TextId.CHECK_LIBRARY_FILES_CHECKED,
                count=value.file_progress.checked,
            )
        ]
        if value.missing:
            lines.append(self._localizer.text(
                TextId.CHECK_LIBRARY_MISSING_FILES_SUMMARY,
                count=value.missing,
            ))
        elif not value.errors:
            lines.append(self._localizer.text(TextId.CHECK_LIBRARY_ALL_FILES_PRESENT))
        if value.errors:
            lines.append(self._localizer.text(
                TextId.CHECK_LIBRARY_FILE_ERRORS_SUMMARY,
                count=value.errors,
            ))
        if value.metadata_total:
            if value.metadata_complete == value.metadata_total and not value.metadata_issues:
                lines.append(self._localizer.text(TextId.CHECK_LIBRARY_METADATA_COMPLETE_SUMMARY))
            if value.metadata_issues:
                lines.append(self._localizer.text(
                    TextId.CHECK_LIBRARY_METADATA_ISSUES_SUMMARY,
                    count=value.metadata_issues,
                ))
            if value.metadata_repaired:
                lines.append(self._localizer.text(
                    TextId.CHECK_LIBRARY_REPAIRED_SUMMARY,
                    count=value.metadata_repaired,
                ))
            if value.metadata_needs_review:
                lines.append(self._localizer.text(
                    TextId.CHECK_LIBRARY_NEEDS_ATTENTION_SUMMARY,
                    count=value.metadata_needs_review,
                ))
            if value.metadata_provider_unavailable:
                lines.append(self._localizer.text(
                    TextId.CHECK_LIBRARY_PROVIDER_UNAVAILABLE_SUMMARY,
                    count=value.metadata_provider_unavailable,
                ))
        self._summary.setText("\n".join(lines))
        self._summary_panel.show()

    def _render_file_summary(self, value: LibraryReconciliationProgress) -> None:
        lines = [self._localizer.text(
            TextId.CHECK_LIBRARY_FILES_CHECKED,
            count=value.checked,
        )]
        if value.missing:
            lines.append(self._localizer.text(
                TextId.CHECK_LIBRARY_MISSING_FILES_SUMMARY,
                count=value.missing,
            ))
        elif not value.errors:
            lines.append(self._localizer.text(TextId.CHECK_LIBRARY_ALL_FILES_PRESENT))
        if value.errors:
            lines.append(self._localizer.text(
                TextId.CHECK_LIBRARY_FILE_ERRORS_SUMMARY,
                count=value.errors,
            ))
        self._summary.setText("\n".join(lines))
        self._summary_panel.show()

    def _render_health_details(self, value: LibraryHealthProgress) -> None:
        self._clear_issue_rows()
        if not self._health_has_issues(value):
            self._issues.hide()
            self._issues_scroll.hide()
            return
        self._issues.setText(self._localizer.text(TextId.CHECK_LIBRARY_ISSUES_SECTION))
        self._issues.show()
        for item in value.items:
            self._issues_layout.addWidget(self._health_issue_row(item))
        self._issues_scroll.setVisible(bool(value.items))

    def _health_issue_row(self, item: MetadataHealthItem) -> QFrame:
        row = QFrame()
        row.setObjectName("checkLibraryIssueRow")
        row_layout = QGridLayout(row)
        row_layout.setContentsMargins(SPACE_12, SPACE_8, SPACE_12, SPACE_8)
        row_layout.setHorizontalSpacing(SPACE_12)
        row_layout.setVerticalSpacing(SPACE_4)
        title = QLabel(item.title)
        title.setObjectName("checkLibraryIssueTitle")
        title.setWordWrap(True)
        row_layout.addWidget(title, 0, 0, 1, 2)
        issue_label = QLabel(self._localizer.text(TextId.CHECK_LIBRARY_ISSUE))
        issue_label.setProperty("role", "secondary")
        issue_detail = QLabel(self._issue_text(item))
        issue_detail.setObjectName("checkLibraryIssueDetail")
        issue_detail.setWordWrap(True)
        row_layout.addWidget(issue_label, 1, 0)
        row_layout.addWidget(issue_detail, 1, 1)
        outcome_label = QLabel(self._localizer.text(TextId.CHECK_LIBRARY_OUTCOME))
        outcome_label.setProperty("role", "secondary")
        outcome = QLabel(self._outcome_text(item))
        outcome.setObjectName("checkLibraryIssueOutcome")
        outcome.setWordWrap(True)
        row_layout.addWidget(outcome_label, 2, 0)
        row_layout.addWidget(outcome, 2, 1)
        return row

    def _issue_text(self, item: MetadataHealthItem) -> str:
        issue_ids = {
            MetadataHealthIssue.OVERVIEW: TextId.CHECK_LIBRARY_ISSUE_OVERVIEW,
            MetadataHealthIssue.RUNTIME: TextId.CHECK_LIBRARY_ISSUE_RUNTIME,
            MetadataHealthIssue.GENRES: TextId.CHECK_LIBRARY_ISSUE_GENRES,
            MetadataHealthIssue.YEAR: TextId.CHECK_LIBRARY_ISSUE_YEAR,
            MetadataHealthIssue.POSTER: TextId.CHECK_LIBRARY_ISSUE_POSTER,
            MetadataHealthIssue.NEEDS_MATCH: TextId.CHECK_LIBRARY_ISSUE_NEEDS_MATCH,
        }
        return ", ".join(
            self._localizer.text(issue_ids[issue]) for issue in item.issues
        ) or self._localizer.text(TextId.CHECK_LIBRARY_NEEDS_ATTENTION)

    def _outcome_text(self, item: MetadataHealthItem) -> str:
        if item.repaired_fields:
            issue_ids = {
                MetadataHealthIssue.OVERVIEW: TextId.CHECK_LIBRARY_ISSUE_OVERVIEW,
                MetadataHealthIssue.RUNTIME: TextId.CHECK_LIBRARY_ISSUE_RUNTIME,
                MetadataHealthIssue.GENRES: TextId.CHECK_LIBRARY_ISSUE_GENRES,
                MetadataHealthIssue.YEAR: TextId.CHECK_LIBRARY_ISSUE_YEAR,
                MetadataHealthIssue.POSTER: TextId.CHECK_LIBRARY_ISSUE_POSTER,
                MetadataHealthIssue.NEEDS_MATCH: TextId.CHECK_LIBRARY_ISSUE_NEEDS_MATCH,
            }
            repaired = ", ".join(
                self._localizer.text(issue_ids[issue]) for issue in item.repaired_fields
            )
            return self._localizer.text(TextId.CHECK_LIBRARY_REPAIRED, fields=repaired)
        if item.provider_error is not None:
            return self._localizer.text(TextId.CHECK_LIBRARY_RESULT_PROVIDER_UNAVAILABLE)
        if item.status in {
            MetadataHealthStatus.NEEDS_MATCH,
            MetadataHealthStatus.PROVIDER_VALUE_UNAVAILABLE,
            MetadataHealthStatus.INCOMPLETE,
        }:
            return self._localizer.text(TextId.CHECK_LIBRARY_NEEDS_ATTENTION)
        if item.status is MetadataHealthStatus.MISSING_POSTER:
            return self._localizer.text(TextId.CHECK_LIBRARY_NOT_REPAIRED)
        return ""

    def _format_health_item(self, item: MetadataHealthItem) -> str:
        """Return a compact compatibility representation for non-widget callers."""

        issue = self._issue_text(item)
        outcome = self._outcome_text(item)
        return f"{item.title}: {issue} — {outcome}" if outcome else f"{item.title}: {issue}"

    def _clear_issue_rows(self) -> None:
        while self._issues_layout.count():
            item = self._issues_layout.takeAt(0)
            widget = item.widget()
            if widget is not None:
                widget.hide()
                widget.setParent(None)

    def _reset_result_panel(self) -> None:
        self._summary_panel.hide()
        self._summary.clear()
        self._issues.clear()
        self._issues.hide()
        self._issues_scroll.hide()
        self._clear_issue_rows()

    def _health_has_issues(self, value: LibraryHealthProgress) -> bool:
        return bool(
            value.metadata_issues
            or value.metadata_needs_review
            or value.metadata_provider_unavailable
            or any(item.status is not MetadataHealthStatus.COMPLETE for item in value.items)
        )

    def _set_idle_state(self) -> None:
        self._status.setText(self._localizer.text(TextId.CHECK_FILES_READY))
        self._idle_description.show()
        self._failure_description.hide()
        self._progress.hide()
        self._progress_percent.hide()
        self._reset_result_panel()
        self._start.show()
        self._start.setEnabled(not self._queued_behind_reconciliation)
        self._set_role(self._start, "primaryAction")
        self._localizer.bind_text(self._start, TextId.CHECK_LIBRARY_FILES)
        self._cancel.hide()
        self._cancel.setEnabled(False)
        self._close.show()
        self._close.setEnabled(True)
        self._set_role(self._close, "secondaryAction")
        self.resize(460, 250)

    def _set_running_state(self) -> None:
        self._status.setText(self._localizer.text(TextId.CHECK_LIBRARY_RUNNING_STATUS))
        self._idle_description.hide()
        self._failure_description.hide()
        self._summary_panel.hide()
        self._issues.hide()
        self._issues_scroll.hide()
        self._progress.show()
        self._progress_percent.show()
        self._start.hide()
        self._start.setEnabled(False)
        self._cancel.show()
        self._cancel.setEnabled(True)
        self._close.hide()
        self._close.setEnabled(False)
        self.resize(560, 300)

    def _set_terminal_controls(
        self,
        *,
        start_text: TextId,
        start_role: str,
        close_role: str,
    ) -> None:
        self._idle_description.hide()
        self._progress.hide()
        self._progress_percent.hide()
        self._start.show()
        self._start.setEnabled(True)
        self._set_role(self._start, start_role)
        self._localizer.bind_text(self._start, start_text)
        self._cancel.hide()
        self._cancel.setEnabled(False)
        self._close.show()
        self._close.setEnabled(True)
        self._set_role(self._close, close_role)
        self.resize(560, 440 if self._issues_scroll.isVisible() else 330)

    @staticmethod
    def _set_role(widget: QWidget, role: str) -> None:
        widget.setProperty("role", role)
        widget.style().unpolish(widget)
        widget.style().polish(widget)


class RelinkMediaFileDialog(QDialog):
    relinked = Signal(object)

    def __init__(
        self,
        actions: ReconciliationUiActions,
        media_file_id: int,
        old_path: Path,
        runner: TaskRunner,
        parent=None,
        *,
        localizer: UiLocalizer | None = None,
    ) -> None:
        super().__init__(parent)
        self._localizer = localizer or UiLocalizer()
        self.setWindowTitle(self._localizer.text(TextId.RELINK_TITLE))
        self._actions = actions
        self._media_file_id = media_file_id
        self._old_path = old_path
        self._runner = runner
        self._token = 0
        self._preview: RelinkPreview | None = None
        layout = QVBoxLayout(self)
        self._paths = QLabel(self._localizer.text(
            TextId.RELINK_OLD_NEW,
            old=old_path,
            new="—",
        ))
        self._paths.setObjectName("relinkPathsLabel")
        self._paths.setWordWrap(True)
        self._paths.setTextInteractionFlags(Qt.TextInteractionFlag.TextSelectableByMouse | Qt.TextInteractionFlag.TextSelectableByKeyboard)
        self._localizer.mark_ltr(self._paths)
        layout.addWidget(self._paths)
        self._status = QLabel(self._localizer.text(TextId.RELINK_CHOOSE))
        self._status.setObjectName("relinkStatusLabel")
        layout.addWidget(self._status)
        buttons = QHBoxLayout()
        self._choose = QPushButton()
        self._choose.setObjectName("chooseRelinkFileButton")
        set_fluent_icon(self._choose, FluentIconName.OPEN_FOLDER)
        self._confirm = QPushButton()
        self._confirm.setObjectName("confirmRelinkButton")
        set_fluent_icon(self._confirm, FluentIconName.ORGANIZE)
        self._confirm.setEnabled(False)
        self._localizer.bind_text(self._choose, TextId.LOCATE_FILE)
        self._localizer.bind_text(self._confirm, TextId.RELINK_CONFIRM)
        buttons.addWidget(self._choose)
        buttons.addWidget(self._confirm)
        layout.addLayout(buttons)
        self._choose.clicked.connect(self.choose_file)
        self._confirm.clicked.connect(self.confirm_relink)

    @property
    def status_text(self) -> str:
        return self._status.text()

    def choose_file(self) -> None:
        suffixes = " ".join(f"*{value}" for value in sorted(SUPPORTED_VIDEO_EXTENSIONS))
        selected, _filter = QFileDialog.getOpenFileName(
            self,
            self._localizer.text(TextId.RELINK_FILE_DIALOG),
            str(self._old_path.parent),
            f"{self._localizer.text(TextId.VIDEO_FILES)} ({suffixes})",
        )
        if selected:
            self.prepare_selected_path(Path(selected).absolute())

    def prepare_selected_path(self, path: Path) -> None:
        self._token += 1
        token = self._token
        self._confirm.setEnabled(False)
        self._status.setText(self._localizer.text(TextId.RELINK_VALIDATING))
        self._runner.submit(
            token,
            lambda: self._actions.prepare_media_relink(self._media_file_id, path),
            self._on_preview,
            self._on_error,
        )

    def confirm_relink(self) -> None:
        if self._preview is None:
            return
        self._token += 1
        token = self._token
        preview_id = self._preview.preview_id
        self._choose.setEnabled(False)
        self._confirm.setEnabled(False)
        self._status.setText(self._localizer.text(TextId.RELINK_CONFIRMING))
        self._runner.submit(
            token,
            lambda: self._actions.confirm_media_relink(preview_id),
            self._on_relinked,
            self._on_error,
        )

    def invalidate_pending(self) -> None:
        self._token += 1
        if self._preview is not None:
            self._actions.discard_media_relink_preview(self._preview.preview_id)
            self._preview = None

    def closeEvent(self, event: QCloseEvent) -> None:
        self.invalidate_pending()
        super().closeEvent(event)

    def _on_preview(self, token: int, value: object) -> None:
        if token != self._token or not isinstance(value, RelinkPreview):
            return
        self._preview = value
        paths = self._localizer.text(
            TextId.RELINK_OLD_NEW,
            old=value.old_path,
            new=value.new_path,
        )
        self._paths.setText(self._localizer.text(
            TextId.RELINK_PREVIEW,
            paths=paths,
            size=value.file_size,
        ))
        self._status.setText(self._localizer.text(TextId.RELINK_VALID))
        self._confirm.setEnabled(True)

    def _on_relinked(self, token: int, value: object) -> None:
        if token != self._token or not isinstance(value, RelinkResult):
            return
        self._status.setText(self._localizer.text(TextId.RELINK_COMPLETE))
        self.accept()
        self.relinked.emit(value)

    def _on_error(self, token: int, error: BaseException) -> None:
        if token != self._token:
            return
        if isinstance(error, RelinkValidationError):
            words = " ".join(error.code.value.split("_")).title()
            self._status.setText(
                self._localizer.text(TextId.RELINK_BLOCKED, reason=words)
            )
        elif isinstance(error, RelinkError):
            self._status.setText(self._localizer.text(TextId.RELINK_STALE))
        else:
            self._status.setText(self._localizer.text(TextId.RELINK_FAILED))
        self._choose.setEnabled(True)
