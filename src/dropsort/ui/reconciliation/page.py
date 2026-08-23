from __future__ import annotations

from enum import StrEnum

from PySide6.QtCore import Qt, Signal
from PySide6.QtGui import QKeyEvent
from PySide6.QtWidgets import (
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

from dropsort.application.dto.library_health import (
    LibraryHealthProgress,
    MetadataHealthIssue,
    MetadataHealthItem,
    MetadataHealthStatus,
)
from dropsort.application.dto.reconciliation import LibraryReconciliationProgress
from dropsort.application.errors import LibraryReconciliationCancelled
from dropsort.application.use_cases import ReconciliationCancellation
from dropsort.ui.common.tasks import TaskRunner
from dropsort.ui.common.icon import FluentIconName, set_fluent_icon
from dropsort.ui.common.theme import SPACE_4, SPACE_12, SPACE_8, SPACE_16, SPACE_24, SPACE_36
from dropsort.ui.contracts import ReconciliationUiActions
from dropsort.ui.localization import TextId, UiLocalizer


def _percentage(checked: int, total: int) -> int:
    if total <= 0:
        return 100 if checked else 0
    return min(100, max(0, round(checked * 100 / total)))


class LibraryCheckPage(QWidget):
    """Persistent, non-modal presentation for the explicit library check."""

    back_requested = Signal()
    start_requested = Signal()
    completed = Signal(object)
    progress_changed = Signal(object)

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
        actions: ReconciliationUiActions | None,
        runner: TaskRunner,
        parent=None,
        *,
        localizer: UiLocalizer | None = None,
    ) -> None:
        super().__init__(parent)
        self.setObjectName("libraryCheckPage")
        self.setFocusPolicy(Qt.FocusPolicy.StrongFocus)
        self._localizer = localizer or UiLocalizer()
        self._actions = actions
        self._runner = runner
        self._token = 0
        self._cancellation: ReconciliationCancellation | None = None
        self._state = self.State.IDLE
        self._external_job = False
        self._queued_behind_reconciliation = False
        self._last_value: LibraryReconciliationProgress | LibraryHealthProgress | None = None

        layout = QVBoxLayout(self)
        self.setMaximumWidth(560)
        layout.setContentsMargins(SPACE_36, SPACE_36, SPACE_36, SPACE_36)
        layout.setSpacing(SPACE_12)

        self._title = QLabel()
        self._title.setObjectName("libraryCheckPageTitle")
        self._title.setProperty("role", "heading")
        self._localizer.bind_text(self._title, TextId.CHECK_FILES_TITLE)
        layout.addWidget(self._title)

        self._description = QLabel()
        self._description.setObjectName("libraryCheckPageDescription")
        self._description.setProperty("role", "secondary")
        self._description.setWordWrap(True)
        self._localizer.bind_text(
            self._description, TextId.CHECK_LIBRARY_IDLE_DESCRIPTION
        )
        layout.addWidget(self._description)

        self._status = QLabel()
        self._status.setObjectName("libraryCheckPageStatusLabel")
        self._status.setProperty("role", "secondary")
        self._status.setWordWrap(True)
        layout.addWidget(self._status)

        progress_row = QHBoxLayout()
        self._progress = QProgressBar()
        self._progress.setObjectName("libraryCheckPageProgressBar")
        self._progress.setTextVisible(False)
        self._progress.setAccessibleName("Check Library progress")
        progress_row.addWidget(self._progress, 1)
        self._progress_percent = QLabel("0%")
        self._progress_percent.setObjectName("libraryCheckPagePercentageLabel")
        self._progress_percent.setMinimumWidth(42)
        self._progress_percent.setAlignment(
            Qt.AlignmentFlag.AlignRight | Qt.AlignmentFlag.AlignVCenter
        )
        self._localizer.mark_ltr(self._progress_percent)
        self._progress_percent.setAccessibleName("Check Library progress percentage")
        progress_row.addWidget(self._progress_percent)
        layout.addLayout(progress_row)

        self._summary_panel = QFrame()
        self._summary_panel.setObjectName("checkLibrarySummaryPanel")
        self._summary_panel.setProperty("role", "panel")
        summary_layout = QGridLayout(self._summary_panel)
        summary_layout.setContentsMargins(SPACE_16, SPACE_12, SPACE_16, SPACE_12)
        summary_layout.setHorizontalSpacing(SPACE_24)
        self._passed = QLabel()
        self._passed.setObjectName("libraryCheckPassedLabel")
        self._passed.setProperty("role", "success")
        self._passed.setAccessibleName("Passed")
        self._needs_attention = QLabel()
        self._needs_attention.setObjectName("libraryCheckNeedsAttentionLabel")
        self._needs_attention.setProperty("role", "warning")
        self._needs_attention.setAccessibleName("Needs attention")
        summary_layout.addWidget(self._passed, 0, 0)
        summary_layout.addWidget(self._needs_attention, 0, 1)
        layout.addWidget(self._summary_panel)

        self._positive = QLabel()
        self._positive.setObjectName("libraryCheckPositiveLabel")
        self._positive.setProperty("role", "success")
        self._positive.setWordWrap(True)
        layout.addWidget(self._positive)

        self._failure = QLabel()
        self._failure.setObjectName("libraryCheckPageFailureLabel")
        self._failure.setProperty("role", "error")
        self._failure.setWordWrap(True)
        layout.addWidget(self._failure)

        self._issues_heading = QLabel()
        self._issues_heading.setObjectName("libraryCheckIssuesHeading")
        self._issues_heading.setProperty("role", "sectionHeading")
        layout.addWidget(self._issues_heading)

        self._issues_scroll = QScrollArea()
        self._issues_scroll.setObjectName("libraryCheckPageIssuesScroll")
        self._issues_scroll.setWidgetResizable(True)
        self._issues_host = QWidget()
        self._issues_layout = QVBoxLayout(self._issues_host)
        self._issues_layout.setContentsMargins(0, 0, 0, 0)
        self._issues_layout.setSpacing(SPACE_8)
        self._issues_layout.setAlignment(Qt.AlignmentFlag.AlignTop)
        self._issues_scroll.setWidget(self._issues_host)
        self._issues_scroll.setMaximumHeight(220)
        layout.addWidget(self._issues_scroll)

        buttons = QHBoxLayout()
        self._start = QPushButton()
        self._start.setObjectName("startLibraryCheckPageButton")
        self._start.setProperty("role", "primaryAction")
        self._start.setAccessibleName("Check Library")
        set_fluent_icon(self._start, FluentIconName.CHECK_LIBRARY)
        self._localizer.bind_text(self._start, TextId.CHECK_LIBRARY_FILES)
        self._start.clicked.connect(self.request_start)
        buttons.addWidget(self._start)
        self._cancel = QPushButton()
        self._cancel.setObjectName("cancelLibraryCheckPageButton")
        self._cancel.setProperty("role", "secondaryAction")
        self._cancel.setAccessibleName("Cancel Check Library")
        self._localizer.bind_text(self._cancel, TextId.CHECK_FILES_CANCEL)
        self._cancel.clicked.connect(self.cancel_check)
        buttons.addWidget(self._cancel)
        buttons.addStretch(1)
        layout.addLayout(buttons)
        layout.addStretch(1)

        self._localizer.language_changed.connect(lambda _language: self._refresh_dynamic_text())
        self._set_idle_state()

    @property
    def state(self) -> "LibraryCheckPage.State":
        return self._state

    @property
    def status_text(self) -> str:
        return self._status.text()

    @property
    def is_running(self) -> bool:
        return self._state is self.State.RUNNING

    def request_start(self) -> None:
        if not self.is_running:
            self.start_requested.emit()

    def start_check(self) -> None:
        if self.is_running:
            return
        self._token += 1
        token = self._token
        self._cancellation = ReconciliationCancellation()
        self._external_job = False
        self._queued_behind_reconciliation = False
        self._state = self.State.RUNNING
        self._last_value = None
        self._set_running_state()
        action = getattr(self._actions, "check_library", None) if self._actions else None
        if not callable(action):
            action = getattr(self._actions, "reconcile_library_files", None) if self._actions else None
        if not callable(action):
            self._on_failure(token, RuntimeError("library check action unavailable"))
            return
        self._runner.submit_progressive(
            token,
            lambda report: action(progress=report, cancellation=self._cancellation),
            self._on_progress,
            self._on_success,
            self._on_failure,
        )

    def attach_to_existing(
        self, token: int, cancellation: ReconciliationCancellation
    ) -> None:
        if self.is_running:
            return
        self._token = token
        self._cancellation = cancellation
        self._external_job = True
        self._queued_behind_reconciliation = False
        self._state = self.State.RUNNING
        self._set_running_state()

    def wait_for_automatic_file_check(self) -> None:
        self._state = self.State.IDLE
        self._queued_behind_reconciliation = True
        self._set_idle_state()
        self._start.setEnabled(False)
        self._status.setText(self._localizer.text(TextId.CHECK_FILES_ALREADY_RUNNING))

    def cancel_check(self) -> None:
        if self._state is not self.State.RUNNING or self._cancellation is None:
            return
        if self._cancel.isEnabled():
            self._cancellation.cancel()
            self._status.setText(self._localizer.text(TextId.CHECK_FILES_CANCELLING))
            self._cancel.setEnabled(False)

    def invalidate_pending(self) -> None:
        self._token += 1
        if self._external_job:
            self._cancellation = None
            self._state = self.State.IDLE
            self._set_idle_state()
        else:
            self.cancel_check()

    def keyPressEvent(self, event: QKeyEvent) -> None:
        if event.key() == Qt.Key.Key_Escape:
            if self.is_running:
                self.cancel_check()
            else:
                self.back_requested.emit()
            event.accept()
            return
        super().keyPressEvent(event)

    def _on_progress(self, token: int, value: object) -> None:
        if token != self._token or not isinstance(
            value, (LibraryReconciliationProgress, LibraryHealthProgress)
        ):
            return
        self._last_value = value
        self._render_progress(value)
        self.progress_changed.emit(value)

    def _on_success(self, token: int, value: object) -> None:
        if token != self._token or not isinstance(
            value, (LibraryReconciliationProgress, LibraryHealthProgress)
        ):
            return
        self._last_value = value
        attention = self._attention_count(value)
        self._state = (
            self.State.COMPLETED_WITH_ISSUES if attention else self.State.COMPLETED_SUCCESS
        )
        self._render_progress(value)
        self._render_result(value)
        self._cancellation = None
        self._external_job = False
        self.completed.emit(value)

    def _on_failure(self, token: int, error: BaseException) -> None:
        if token != self._token:
            return
        self._cancellation = None
        self._external_job = False
        self._last_value = None
        if isinstance(error, LibraryReconciliationCancelled):
            self._state = self.State.CANCELLED
            self._description.hide()
            self._failure.setProperty("role", "secondary")
            self._failure.setText(
                self._localizer.text(TextId.CHECK_LIBRARY_CANCELLED_DESCRIPTION)
            )
            self._failure.show()
            self._status.setText(self._localizer.text(TextId.CHECK_FILES_CANCELLED))
        else:
            self._state = self.State.FAILED
            self._description.hide()
            self._failure.setProperty("role", "error")
            self._failure.setText(
                self._localizer.text(TextId.CHECK_LIBRARY_FAILURE_DESCRIPTION)
            )
            self._failure.show()
            self._status.setText(self._localizer.text(TextId.CHECK_LIBRARY_FAILURE_TITLE))
        self._set_terminal_controls(
            TextId.CHECK_LIBRARY_AGAIN
            if self._state is self.State.CANCELLED
            else TextId.CHECK_LIBRARY_TRY_AGAIN
        )

    def _render_progress(
        self, value: LibraryReconciliationProgress | LibraryHealthProgress
    ) -> None:
        self._progress.setMaximum(max(1, value.total))
        self._progress.setValue(value.checked)
        self._progress_percent.setText(f"{_percentage(value.checked, value.total)}%")
        if isinstance(value, LibraryHealthProgress):
            self._status.setText(self._localizer.text(TextId.CHECK_LIBRARY_RUNNING_STATUS))
        else:
            self._status.setText(self._localizer.text(TextId.CHECK_FILES_RUNNING))
        if value.checked and value.total:
            self._progress_count = f"{value.checked} / {value.total} {self._localizer.text(TextId.CHECK_LIBRARY_CHECKED)}"
        else:
            self._progress_count = ""
        self._description.setText(self._progress_count)

    def _render_result(
        self, value: LibraryReconciliationProgress | LibraryHealthProgress
    ) -> None:
        attention = self._attention_count(value)
        passed = max(0, value.total - attention)
        self._passed.setText(
            f"{passed} {self._localizer.text(TextId.CHECK_LIBRARY_PASSED)}"
        )
        self._needs_attention.setText(
            f"{attention} {self._localizer.text(TextId.CHECK_LIBRARY_NEEDS_ATTENTION)}"
        )
        self._passed.setAccessibleName(
            f"{passed} {self._localizer.text(TextId.CHECK_LIBRARY_PASSED)}"
        )
        self._needs_attention.setAccessibleName(
            f"{attention} {self._localizer.text(TextId.CHECK_LIBRARY_NEEDS_ATTENTION)}"
        )
        self._summary_panel.show()
        self._description.hide()
        self._failure.hide()
        self._positive.setVisible(attention == 0)
        if attention == 0:
            self._positive.setText(self._localizer.text(TextId.CHECK_LIBRARY_NO_ISSUES))
        self._clear_issue_rows()
        issue_count = 0
        if isinstance(value, LibraryHealthProgress):
            for item in value.items:
                if item.status is not MetadataHealthStatus.COMPLETE:
                    self._issues_layout.addWidget(self._issue_row(item))
                    issue_count += 1
        if value.missing or value.errors:
            file_issue = self._file_issue_row(value)
            if file_issue is not None:
                self._issues_layout.insertWidget(0, file_issue)
                issue_count += 1
        self._issues_heading.setText(self._localizer.text(TextId.CHECK_LIBRARY_NEEDS_ATTENTION))
        self._issues_heading.setVisible(issue_count > 0)
        self._issues_scroll.setVisible(issue_count > 0)
        self._status.setText(self._localizer.text(TextId.CHECK_LIBRARY_COMPLETE_TITLE))
        self._set_terminal_controls(TextId.CHECK_LIBRARY_AGAIN)

    def _file_issue_row(
        self, value: LibraryReconciliationProgress | LibraryHealthProgress
    ) -> QFrame | None:
        missing = value.missing
        errors = value.errors
        if not missing and not errors:
            return None
        details: list[str] = []
        if missing:
            details.append(
                self._localizer.text(TextId.CHECK_LIBRARY_MISSING_FILES_SUMMARY, count=missing)
            )
        if errors:
            details.append(
                self._localizer.text(TextId.CHECK_LIBRARY_FILE_ERRORS_SUMMARY, count=errors)
            )
        return self._simple_issue_row(
            self._localizer.text(TextId.CHECK_LIBRARY_FILES_SECTION), ", ".join(details)
        )

    def _issue_row(self, item: MetadataHealthItem) -> QFrame:
        return self._simple_issue_row(item.title, self._issue_text(item), self._outcome_text(item))

    def _simple_issue_row(self, title: str, issue: str, outcome: str = "") -> QFrame:
        row = QFrame()
        row.setObjectName("checkLibraryIssueRow")
        row.setAccessibleName(f"{title}: {issue}")
        row_layout = QGridLayout(row)
        row_layout.setContentsMargins(SPACE_12, SPACE_8, SPACE_12, SPACE_8)
        row_layout.setHorizontalSpacing(SPACE_12)
        row_layout.setVerticalSpacing(SPACE_4)
        title_label = QLabel(title)
        title_label.setObjectName("checkLibraryIssueTitle")
        title_label.setWordWrap(True)
        row_layout.addWidget(title_label, 0, 0, 1, 2)
        issue_label = QLabel(issue)
        issue_label.setObjectName("checkLibraryIssueDetail")
        issue_label.setWordWrap(True)
        row_layout.addWidget(issue_label, 1, 0, 1, 2)
        if outcome:
            outcome_label = QLabel(outcome)
            outcome_label.setObjectName("checkLibraryIssueOutcome")
            outcome_label.setProperty("role", "secondary")
            outcome_label.setWordWrap(True)
            row_layout.addWidget(outcome_label, 2, 0, 1, 2)
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
        return ", ".join(self._localizer.text(issue_ids[issue]) for issue in item.issues)

    def _outcome_text(self, item: MetadataHealthItem) -> str:
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

    def _attention_count(
        self, value: LibraryReconciliationProgress | LibraryHealthProgress
    ) -> int:
        file_attention = value.missing + value.errors
        if not isinstance(value, LibraryHealthProgress):
            return file_attention
        item_attention = sum(
            item.status is not MetadataHealthStatus.COMPLETE for item in value.items
        )
        return file_attention + max(
            value.metadata_issues, value.metadata_needs_review,
            value.metadata_provider_unavailable, item_attention,
        )

    def _clear_issue_rows(self) -> None:
        while self._issues_layout.count():
            item = self._issues_layout.takeAt(0)
            widget = item.widget()
            if widget is not None:
                widget.deleteLater()

    def _set_idle_state(self) -> None:
        self._status.setText(self._localizer.text(TextId.CHECK_FILES_READY))
        self._description.show()
        self._progress.hide()
        self._progress_percent.hide()
        self._summary_panel.hide()
        self._positive.hide()
        self._failure.hide()
        self._issues_heading.hide()
        self._issues_scroll.hide()
        self._start.show()
        self._start.setEnabled(not self._queued_behind_reconciliation and self._actions is not None)
        self._localizer.bind_text(self._start, TextId.CHECK_LIBRARY_FILES)
        self._cancel.hide()
        self._cancel.setEnabled(False)

    def _set_running_state(self) -> None:
        self._description.show()
        self._progress.show()
        self._progress_percent.show()
        self._summary_panel.hide()
        self._positive.hide()
        self._failure.hide()
        self._issues_heading.hide()
        self._issues_scroll.hide()
        self._start.hide()
        self._cancel.show()
        self._cancel.setEnabled(True)

    def _set_terminal_controls(self, start_text: TextId) -> None:
        self._progress.hide()
        self._progress_percent.hide()
        self._start.show()
        self._start.setEnabled(self._actions is not None)
        self._localizer.bind_text(self._start, start_text)
        self._cancel.hide()
        self._cancel.setEnabled(False)

    def _refresh_dynamic_text(self) -> None:
        if self._state is self.State.IDLE:
            self._set_idle_state()
        elif self._state is self.State.RUNNING and self._last_value is not None:
            self._render_progress(self._last_value)
        elif self._state in {
            self.State.COMPLETED_SUCCESS,
            self.State.COMPLETED_WITH_ISSUES,
        } and self._last_value is not None:
            self._render_result(self._last_value)
