from __future__ import annotations

from collections.abc import Callable
import logging
from pathlib import Path

from PySide6.QtCore import QTimer, Qt, Signal
from PySide6.QtGui import QCloseEvent
from PySide6.QtWidgets import (
    QCheckBox,
    QFileDialog,
    QFrame,
    QHBoxLayout,
    QLabel,
    QProgressBar,
    QPushButton,
    QScrollArea,
    QSizePolicy,
    QVBoxLayout,
    QWidget,
)

from dropsort.application.dto.import_review import (
    ImportReviewProgress,
    ImportReviewSession,
    ImportReviewStage,
    ImportReviewSummary,
)
from dropsort.application.dto.movie_import import (
    ConfirmMovieImportCommand,
    ImportProposalReason,
    ImportProposalStatus,
    MovieImportProposal,
)
from dropsort.application.errors import (
    ImportReviewCancelled,
    MovieImportCatalogError,
    MovieImportMetadataError,
)
from dropsort.application.use_cases.prepare_folder_import_review import ImportReviewCancellation
from dropsort.media.discovery.errors import DiscoveryRootError
from dropsort.media.discovery.models import DiscoveryErrorCode
from dropsort.ui.common.tasks import QtTaskRunner, TaskRunner
from dropsort.ui.common.icon import FluentIconName, set_fluent_icon
from dropsort.ui.common.theme import SPACE_16, SPACE_24, SPACE_36, SPACE_MEDIUM
from dropsort.ui.contracts import ImportUiActions
from dropsort.ui.scan.import_review_row import ImportReviewRow
from dropsort.ui.scan.manual_search_dialog import ManualSearchDialog
from dropsort.ui.localization import TextId, UiLocalizer


LOGGER = logging.getLogger(__name__)
FolderPicker = Callable[[QWidget], str]
REVIEW_ROW_BATCH_SIZE = 25


class ImportView(QWidget):
    catalog_changed = Signal()
    settings_requested = Signal()

    def __init__(
        self,
        actions: ImportUiActions,
        *,
        runner: TaskRunner | None = None,
        folder_picker: FolderPicker | None = None,
        localizer: UiLocalizer | None = None,
        parent=None,
    ) -> None:
        super().__init__(parent)
        self._actions = actions
        self._localizer = localizer or UiLocalizer()
        # The unparented runner retains active threads until they finish. Parenting it
        # to the view could destroy a QThread while read-only work is still active.
        self._runner = runner or QtTaskRunner()
        self._folder_picker = folder_picker or (
            lambda parent: _pick_folder(parent, self._localizer)
        )
        self._session_token = 0
        self._rows: list[ImportReviewRow] = []
        self._scan_active = False
        self._worker_finished = False
        self._cancellation: ImportReviewCancellation | None = None
        self._latest_progress = ImportReviewProgress(ImportReviewStage.DISCOVERING)
        self._pending_items = ()
        self._pending_summary = ImportReviewSummary()
        self._rendered_paths: set[str] = set()
        self._catalog_tasks_active = 0
        self._manual_dialog = None
        self._scan_result_ready = False

        layout = QVBoxLayout(self)
        layout.setContentsMargins(SPACE_36, SPACE_36, SPACE_36, SPACE_36)
        layout.setSpacing(SPACE_24)
        heading = QLabel()
        heading.setProperty("role", "screenHeading")
        self._localizer.bind_text(heading, TextId.ADD_MOVIES_TITLE)
        layout.addWidget(heading)
        folder_card = QFrame()
        folder_card.setObjectName("folderSelectionCard")
        folder_card.setProperty("role", "panel")
        folder_layout = QVBoxLayout(folder_card)
        folder_layout.setContentsMargins(SPACE_16, SPACE_16, SPACE_16, SPACE_16)
        folder_layout.setSpacing(SPACE_MEDIUM)

        guidance = QLabel()
        guidance.setObjectName("addMoviesGuidanceLabel")
        self._localizer.bind_text(guidance, TextId.ADD_MOVIES_GUIDANCE)
        guidance.setProperty("role", "secondary")
        guidance.setWordWrap(True)
        folder_layout.addWidget(guidance)

        folder_label = QLabel()
        folder_label.setObjectName("folderSelectionLabel")
        folder_label.setProperty("role", "rowTitle")
        self._localizer.bind_text(folder_label, TextId.ADD_MOVIES_FOLDER_LABEL)
        folder_layout.addWidget(folder_label)

        self._folder = QLabel(self._localizer.text(TextId.NO_FOLDER))
        self._folder.setObjectName("selectedFolderLabel")
        self._folder.setProperty("role", "muted")
        self._folder.setMinimumWidth(0)
        self._folder.setSizePolicy(QSizePolicy.Policy.Ignored, QSizePolicy.Policy.Preferred)
        self._folder.setWordWrap(False)
        self._folder.setTextInteractionFlags(
            Qt.TextInteractionFlag.TextSelectableByMouse
            | Qt.TextInteractionFlag.TextSelectableByKeyboard
        )
        self._localizer.mark_ltr(self._folder)
        folder_layout.addWidget(self._folder)

        controls = QHBoxLayout()
        self.recursive_checkbox = QCheckBox()
        self.recursive_checkbox.setObjectName("recursiveScanCheckbox")
        self.recursive_checkbox.setChecked(True)
        self._localizer.bind_text(self.recursive_checkbox, TextId.INCLUDE_SUBFOLDERS)
        controls.addWidget(self.recursive_checkbox)
        controls.addStretch(1)
        self._scan_button = QPushButton()
        self._scan_button.setObjectName("chooseFolderButton")
        self._scan_button.setProperty("role", "primaryAction")
        set_fluent_icon(self._scan_button, FluentIconName.ADD_MOVIES)
        self._scan_button.clicked.connect(self.choose_folder)
        self._localizer.bind_text(self._scan_button, TextId.CHOOSE_FOLDER_SCAN)
        controls.addWidget(self._scan_button)
        self.cancel_button = QPushButton()
        self.cancel_button.setObjectName("cancelScanButton")
        self.cancel_button.setProperty("role", "secondaryAction")
        self.cancel_button.clicked.connect(self.cancel_scan)
        self._localizer.bind_text(self.cancel_button, TextId.CANCEL_SCAN)
        self.cancel_button.hide()
        controls.addWidget(self.cancel_button)
        folder_layout.addLayout(controls)
        layout.addWidget(folder_card)

        self._state = QLabel(self._localizer.text(TextId.SCAN_READY))
        self._state.setObjectName("importStateLabel")
        self._state.setWordWrap(True)
        layout.addWidget(self._state)

        self._queue_empty = QLabel()
        self._queue_empty.setObjectName("importQueueEmptyLabel")
        self._queue_empty.setProperty("role", "success")
        self._queue_empty.setWordWrap(True)
        self._queue_empty.hide()
        self._localizer.language_changed.connect(
            lambda _language: self._refresh_queue_empty_text()
        )
        layout.addWidget(self._queue_empty)

        self._progress = QLabel("")
        self._progress.setObjectName("scanProgressLabel")
        self._progress.setProperty("role", "muted")
        self._progress.setWordWrap(True)
        self._progress.hide()
        layout.addWidget(self._progress)

        self.progress_bar = QProgressBar()
        self.progress_bar.setObjectName("scanProgressBar")
        self.progress_bar.setTextVisible(False)
        self.progress_bar.hide()
        layout.addWidget(self.progress_bar)

        results_heading_row = QHBoxLayout()
        results_heading_row.setSpacing(SPACE_MEDIUM)
        self._results_heading = QLabel(self._localizer.text(TextId.ADD_MOVIES_DETECTED_HEADING))
        self._results_heading.setObjectName("importResultsHeading")
        self._results_heading.setProperty("role", "sectionHeading")
        self._results_heading.hide()
        results_heading_row.addWidget(self._results_heading)
        results_heading_row.addStretch(1)
        self._results_count = QLabel()
        self._results_count.setObjectName("importResultsCount")
        self._results_count.setProperty("role", "muted")
        self._results_count.hide()
        results_heading_row.addWidget(self._results_count)
        layout.addLayout(results_heading_row)

        self._review_header = self._build_review_header()
        self._review_header.hide()
        layout.addWidget(self._review_header)

        scroll = QScrollArea()
        scroll.setObjectName("importReviewScroll")
        scroll.setWidgetResizable(True)
        self._container = QWidget()
        self._container.setObjectName("importReviewContainer")
        self._rows_layout = QVBoxLayout(self._container)
        self._rows_layout.setContentsMargins(0, 0, 0, 0)
        self._rows_layout.setSpacing(0)
        self._rows_layout.addStretch(1)
        scroll.setWidget(self._container)
        layout.addWidget(scroll, 1)


    def _build_review_header(self) -> QFrame:
        header = QFrame()
        header.setObjectName("importReviewHeader")
        row = QHBoxLayout(header)
        row.setContentsMargins(SPACE_16, SPACE_MEDIUM, SPACE_16, SPACE_MEDIUM)
        row.setSpacing(SPACE_16)

        title = QLabel()
        title.setProperty("role", "rowTitle")
        self._localizer.bind_text(title, TextId.ADD_MOVIES_RESULTS_TITLE)
        row.addWidget(title, 1)

        year = QLabel()
        year.setFixedWidth(72)
        year.setProperty("role", "muted")
        self._localizer.bind_text(year, TextId.ADD_MOVIES_RESULTS_YEAR)
        row.addWidget(year)

        resolution = QLabel()
        resolution.setFixedWidth(82)
        resolution.setProperty("role", "muted")
        self._localizer.bind_text(resolution, TextId.ADD_MOVIES_RESULTS_RESOLUTION)
        row.addWidget(resolution)

        status = QLabel()
        status.setFixedWidth(112)
        status.setProperty("role", "muted")
        self._localizer.bind_text(status, TextId.ADD_MOVIES_RESULTS_STATUS)
        row.addWidget(status)

        action = QLabel()
        action.setFixedWidth(176)
        action.setAlignment(Qt.AlignmentFlag.AlignRight | Qt.AlignmentFlag.AlignVCenter)
        action.setProperty("role", "muted")
        self._localizer.bind_text(action, TextId.ADD_MOVIES_RESULTS_ACTION)
        row.addWidget(action)
        return header

    @property
    def rows(self) -> tuple[ImportReviewRow, ...]:
        return tuple(self._rows)

    @property
    def row_count(self) -> int:
        return len(self._rows)

    @property
    def state_message(self) -> str:
        return self._state.text()

    @property
    def progress_message(self) -> str:
        return self._progress.text()

    @property
    def is_busy(self) -> bool:
        return self._scan_active

    @property
    def has_pending_catalog_work(self) -> bool:
        return self._catalog_tasks_active > 0

    def choose_folder(self) -> None:
        if self._scan_active:
            return
        selected = self._folder_picker(self)
        if not selected:
            return
        self.start_scan(Path(selected))

    def start_scan(self, root: Path) -> None:
        if self._scan_active:
            return
        self._session_token += 1
        token = self._session_token
        cancellation = ImportReviewCancellation()
        self._cancellation = cancellation
        self._scan_active = True
        self._worker_finished = False
        self._latest_progress = ImportReviewProgress(ImportReviewStage.DISCOVERING)
        self._pending_items = ()
        self._pending_summary = ImportReviewSummary()
        self._scan_result_ready = False
        self._rendered_paths.clear()
        self._clear_rows()
        self._queue_empty.hide()
        self._folder.setText(str(root))
        self._folder.setToolTip(str(root))
        self._state.setText(self._localizer.text(TextId.SCANNING_MOVIES))
        self._progress.setText(self._localizer.text(
            TextId.SCAN_PROGRESS_DISCOVERY, folders=0, files=0, movies=0
        ))
        self._progress.show()
        self.progress_bar.setRange(0, 0)
        self.progress_bar.show()
        self._scan_button.setEnabled(False)
        self.cancel_button.setEnabled(True)
        self.cancel_button.show()
        self.recursive_checkbox.setEnabled(False)
        recursive = self.recursive_checkbox.isChecked()

        def task(report):
            return self._actions.prepare_import_review(
                root,
                recursive,
                progress=report,
                cancellation=cancellation,
            )

        submit_progressive = getattr(self._runner, "submit_progressive", None)
        if callable(submit_progressive):
            submit_progressive(
                token,
                task,
                self._scan_progressed,
                self._scan_succeeded,
                self._scan_failed,
            )
        else:
            self._runner.submit(
                token,
                lambda: task(lambda _value: None),
                self._scan_succeeded,
                self._scan_failed,
            )

    def cancel_scan(self) -> None:
        if not self._scan_active or self._cancellation is None:
            return
        self._cancellation.cancel()
        self._state.setText(self._localizer.text(TextId.CANCELLING_SCAN))
        self.cancel_button.setEnabled(False)
        if self._worker_finished:
            self._show_cancelled(self._latest_progress)

    def _scan_progressed(self, token: int, value: object) -> None:
        if token != self._session_token or not self._scan_active:
            return
        if self._cancellation is not None and self._cancellation.is_cancelled():
            return
        if not isinstance(value, ImportReviewProgress):
            return
        self._latest_progress = value
        if value.stage is ImportReviewStage.DISCOVERING:
            self._state.setText(self._localizer.text(TextId.SCANNING_MOVIES))
            self.progress_bar.setRange(0, 0)
            self._progress.setText(self._localizer.text(
                TextId.SCAN_PROGRESS_DISCOVERY,
                folders=value.directories_seen,
                files=value.entries_seen,
                movies=value.movie_candidates,
            ))
        elif value.stage is ImportReviewStage.PREPARING_METADATA:
            self._state.setText(self._localizer.text(TextId.PREPARING_MATCHES))
            self.progress_bar.setRange(0, max(value.proposal_total, 1))
            self.progress_bar.setValue(value.proposal_completed)
            self._progress.setText(self._localizer.text(
                TextId.SCAN_PROGRESS_METADATA,
                done=value.proposal_completed,
                total=value.proposal_total,
            ))
        else:
            self._state.setText(self._localizer.text(TextId.BUILDING_RESULTS))
            self.progress_bar.setRange(0, max(value.proposal_total, 1))
            self.progress_bar.setValue(value.proposal_completed)
            self._progress.setText(self._localizer.text(TextId.SCAN_PROGRESS_ROWS))

    def _scan_succeeded(self, token: int, value: object) -> None:
        if token != self._session_token:
            return
        self._worker_finished = True
        if self._cancellation is not None and self._cancellation.is_cancelled():
            self._show_cancelled(self._latest_progress)
            return
        if not isinstance(value, ImportReviewSession):
            self._state.setText(self._localizer.text(TextId.INVALID_SCAN_RESULT))
            self._finish_scan_controls()
            return
        self._pending_items = value.items
        self._pending_summary = value.summary
        self._state.setText(self._localizer.text(TextId.BUILDING_RESULTS))
        self.progress_bar.setRange(0, max(len(value.items), 1))
        self.progress_bar.setValue(0)
        self._render_next_batch(token)

    def _render_next_batch(self, token: int) -> None:
        if token != self._session_token or not self._scan_active:
            return
        if self._cancellation is not None and self._cancellation.is_cancelled():
            self._show_cancelled(self._latest_progress)
            return
        batch = self._pending_items[:REVIEW_ROW_BATCH_SIZE]
        self._pending_items = self._pending_items[REVIEW_ROW_BATCH_SIZE:]
        for proposal in batch:
            identity = str(proposal.discovery.path).casefold()
            if identity in self._rendered_paths:
                continue
            if proposal.status is ImportProposalStatus.ALREADY_IN_LIBRARY:
                continue
            self._rendered_paths.add(identity)
            row = ImportReviewRow(proposal, self._container, localizer=self._localizer)
            row.confirm_requested.connect(
                lambda proposal, candidate, active_row=row: self._confirm(
                    active_row,
                    proposal,
                    candidate,
                )
            )
            row.settings_requested.connect(self.settings_requested.emit)
            row.manual_search_requested.connect(self._open_manual_search)
            row.dismiss_requested.connect(self._dismiss_row)
            self._rows_layout.insertWidget(self._rows_layout.count() - 1, row)
            self._rows.append(row)
            self._sync_review_table_visibility()
        total = len(self._rows) + len(self._pending_items)
        self.progress_bar.setRange(0, max(total, 1))
        self.progress_bar.setValue(len(self._rows))
        if self._pending_items:
            QTimer.singleShot(0, lambda: self._render_next_batch(token))
            return
        self._finish_success()

    def _open_manual_search(self, proposal: object, row: ImportReviewRow) -> None:
        if not isinstance(proposal, MovieImportProposal):
            return
        dialog = ManualSearchDialog(
            proposal.discovery,
            self._actions,
            runner=self._runner,
            localizer=self._localizer,
            parent=self,
        )
        dialog.candidate_selected.connect(
            lambda candidate, active_row=row, original=proposal: self._manual_candidate_selected(
                active_row, original, candidate
            )
        )
        dialog.show()
        self._manual_dialog = dialog

    def _manual_candidate_selected(
        self,
        row: ImportReviewRow,
        original: MovieImportProposal,
        candidate: object,
    ) -> None:
        from dropsort.metadata.contracts import MovieCandidate

        if not isinstance(candidate, MovieCandidate):
            return
        proposal = MovieImportProposal(
            status=ImportProposalStatus.MANUAL_SELECTION,
            discovery=original.discovery,
            candidates=(candidate,),
            match_decision=None,
            proposed_candidate=None,
            reasons=(ImportProposalReason.MANUAL_SELECTION,),
            existing_media_file_id=None,
        )
        row.set_manual_proposal(proposal)

    def _scan_failed(self, token: int, error: BaseException) -> None:
        if token != self._session_token:
            return
        self._worker_finished = True
        if isinstance(error, ImportReviewCancelled):
            self._show_cancelled(error.progress)
            return
        LOGGER.error(
            "Folder import review preparation failed",
            exc_info=(type(error), error, error.__traceback__),
        )
        self._state.setText(_scan_error_message(error, self._localizer))
        self._finish_scan_controls()

    def _confirm(self, row: ImportReviewRow, proposal: object, candidate: object) -> None:
        try:
            command = ConfirmMovieImportCommand(proposal=proposal, chosen_candidate=candidate)
        except ValueError:
            row.mark_import_failed(
                self._localizer.text(TextId.IMPORT_INVALID_CANDIDATE)
            )
            return
        token = self._session_token
        self._catalog_tasks_active += 1
        self._runner.submit(
            token,
            lambda: self._actions.confirm_movie_import(command),
            lambda delivered_token, _result: self._import_succeeded(delivered_token, row),
            lambda delivered_token, error: self._import_failed(delivered_token, row, error),
        )

    def _import_succeeded(self, token: int, row: ImportReviewRow) -> None:
        self._catalog_tasks_active = max(0, self._catalog_tasks_active - 1)
        if token != self._session_token:
            return
        self._remove_row(row)
        self.catalog_changed.emit()

    def _dismiss_row(self, row: object) -> None:
        if isinstance(row, ImportReviewRow):
            self._remove_row(row)

    def _remove_row(self, row: ImportReviewRow) -> None:
        if row not in self._rows:
            return
        self._rows.remove(row)
        self._rows_layout.removeWidget(row)
        row.hide()
        row.setParent(None)
        row.deleteLater()
        self._sync_review_table_visibility()
        if self._scan_result_ready and not self._rows and self._catalog_tasks_active == 0:
            self._show_all_done()

    def _import_failed(
        self,
        token: int,
        row: ImportReviewRow,
        error: BaseException,
    ) -> None:
        self._catalog_tasks_active = max(0, self._catalog_tasks_active - 1)
        if token != self._session_token:
            return
        LOGGER.error(
            "Explicit catalog import failed",
            exc_info=(type(error), error, error.__traceback__),
        )
        if isinstance(error, MovieImportMetadataError):
            message = self._localizer.text(TextId.IMPORT_DETAILS_UNAVAILABLE)
        elif isinstance(error, MovieImportCatalogError):
            message = self._localizer.text(TextId.IMPORT_CATALOG_FAILED)
        else:
            message = self._localizer.text(TextId.IMPORT_FAILED)
        row.mark_import_failed(message)

    def _clear_rows(self) -> None:
        for row in self._rows:
            self._rows_layout.removeWidget(row)
            row.hide()
            row.setParent(None)
            row.deleteLater()
        self._rows.clear()
        self._sync_review_table_visibility()


    def _sync_review_table_visibility(self) -> None:
        has_rows = bool(self._rows)
        self._results_heading.setVisible(has_rows)
        self._results_count.setVisible(has_rows)
        self._review_header.setVisible(has_rows)
        if has_rows:
            self._results_count.setText(f"{len(self._rows)} items")
        else:
            self._results_count.clear()

    def _finish_success(self) -> None:
        self._scan_result_ready = True
        summary = self._pending_summary
        if not self._rows:
            message = self._localizer.text(TextId.SCAN_COMPLETE_EMPTY)
            if summary.tv_episodes_skipped:
                message += self._localizer.text(
                    TextId.SCAN_TV_SKIPPED_SUFFIX,
                    count=summary.tv_episodes_skipped,
                )
        else:
            message = self._localizer.text(
                TextId.SCAN_COMPLETE,
                files=summary.entries_seen,
                ready=summary.ready_for_review,
                existing=summary.already_in_library,
                errors=summary.discovery_errors,
            )
        self._state.setText(message)
        self._progress.setText(self._localizer.text(
            TextId.SCAN_SUMMARY_COUNTS,
            movies=summary.movie_candidates,
            tv=summary.tv_episodes_skipped,
            unknown=summary.unknown_media,
        ))
        self.progress_bar.setRange(0, 1)
        self.progress_bar.setValue(1)
        self._finish_scan_controls()
        if self._rows:
            self._queue_empty.hide()
        else:
            self._show_all_done()

    def _show_all_done(self) -> None:
        self._refresh_queue_empty_text()
        self._queue_empty.show()

    def _refresh_queue_empty_text(self) -> None:
        self._queue_empty.setText(
            f"{self._localizer.text(TextId.ALL_DONE)}\n"
            f"{self._localizer.text(TextId.NO_MOVIES_WAITING)}"
        )

    def _show_cancelled(self, progress: ImportReviewProgress) -> None:
        self._clear_rows()
        self._pending_items = ()
        self._state.setText(self._localizer.text(
            TextId.SCAN_CANCELLED, files=progress.entries_seen
        ))
        self._progress.setText(
            f"Folders: {progress.directories_seen} · Files inspected: {progress.entries_seen}"
        )
        self.progress_bar.setRange(0, 1)
        self.progress_bar.setValue(0)
        self._finish_scan_controls()

    def _finish_scan_controls(self) -> None:
        self._scan_active = False
        self._worker_finished = False
        self._cancellation = None
        self._scan_button.setEnabled(True)
        self.recursive_checkbox.setEnabled(True)
        self.cancel_button.setEnabled(False)
        self.cancel_button.hide()

    def invalidate_pending_tasks(self) -> None:
        """Cancel work and make every late result inert before closure/replacement."""
        if self._cancellation is not None:
            self._cancellation.cancel()
        self._session_token += 1
        self._scan_active = False
        self._pending_items = ()

    def wait_for_pending_tasks(self) -> None:
        waiter = getattr(self._runner, "wait_for_done", None)
        if callable(waiter):
            waiter()

    def closeEvent(self, event: QCloseEvent) -> None:
        self.invalidate_pending_tasks()
        super().closeEvent(event)


def _pick_folder(parent: QWidget, localizer: UiLocalizer | None = None) -> str:
    localizer = localizer or UiLocalizer()
    return QFileDialog.getExistingDirectory(
        parent, localizer.text(TextId.CHOOSE_MOVIE_FOLDER)
    )


def _scan_error_message(error: BaseException, localizer: UiLocalizer | None = None) -> str:
    localizer = localizer or UiLocalizer()
    if isinstance(error, DiscoveryRootError):
        return {
            DiscoveryErrorCode.ROOT_MISSING: localizer.text(TextId.SCAN_ROOT_MISSING),
            DiscoveryErrorCode.ROOT_NOT_DIRECTORY: localizer.text(TextId.SCAN_ROOT_NOT_FOLDER),
            DiscoveryErrorCode.ROOT_LINK_NOT_ALLOWED: localizer.text(TextId.SCAN_ROOT_LINK),
            DiscoveryErrorCode.PERMISSION_DENIED: localizer.text(TextId.SCAN_PERMISSION),
        }.get(error.code, localizer.text(TextId.SCAN_SAFE_ERROR))
    return localizer.text(TextId.SCAN_FAILED)
