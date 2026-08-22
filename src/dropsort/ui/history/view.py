from __future__ import annotations

from datetime import datetime

from PySide6.QtCore import QSize, Qt, Signal
from PySide6.QtGui import QCloseEvent, QFontMetrics, QKeySequence, QShortcut
from PySide6.QtWidgets import (
    QApplication,
    QDialog,
    QFileDialog,
    QFormLayout,
    QFrame,
    QHBoxLayout,
    QLabel,
    QPushButton,
    QScrollArea,
    QSizePolicy,
    QToolButton,
    QVBoxLayout,
    QWidget,
)

from dropsort.application.dto.operation_history import (
    MAX_OPERATION_HISTORY_PAGE_SIZE,
    OperationDetails,
    OperationHistoryItem,
    OperationHistoryQuery,
    OperationStatus,
    RecoveryAssessment,
    RecoveryResult,
    UndoPreview,
    UndoResult,
)
from dropsort.application.errors import (
    OperationHistoryError,
    RecoveryActionUnavailableError,
    UndoError,
    UndoNotEligibleError,
    UndoRecoveryRequiredError,
)
from dropsort.application.use_cases.operation_history import format_operation_history
from dropsort.ui.common.formatting import format_datetime, format_file_size
from dropsort.ui.common.icon import FluentIconName, set_fluent_icon
from dropsort.ui.common.tasks import QtTaskRunner, TaskRunner
from dropsort.ui.common.theme import (
    SPACE_4,
    SPACE_12,
    SPACE_36,
    SPACE_LARGE,
    SPACE_MEDIUM,
    SPACE_SMALL,
)
from dropsort.ui.contracts import OperationHistoryUiActions
from dropsort.ui.localization import TextId, UiLocalizer


def _status_icon_name(status: OperationStatus) -> FluentIconName:
    if status is OperationStatus.COMMITTED:
        return FluentIconName.MARK_WATCHED
    if status is OperationStatus.FAILED:
        return FluentIconName.FAILED
    if status is OperationStatus.RECOVERY_REQUIRED:
        return FluentIconName.WARNING
    return FluentIconName.INFO


class OperationHistoryView(QWidget):
    catalog_changed = Signal()

    def __init__(
        self,
        actions: OperationHistoryUiActions,
        *,
        runner: TaskRunner | None = None,
        localizer: UiLocalizer | None = None,
        parent=None,
    ) -> None:
        super().__init__(parent)
        self._actions = actions
        self._localizer = localizer or UiLocalizer()
        self._runner = runner or QtTaskRunner()
        self._token = 0
        self._rows: list[QFrame] = []
        self._dialogs: set[OperationDetailsDialog] = set()
        self._items: tuple[OperationHistoryItem, ...] = ()
        self._has_snapshot = False
        self._has_rendered_snapshot = False
        self._history_refresh_active = False

        layout = QVBoxLayout(self)
        layout.setContentsMargins(SPACE_36, SPACE_36, SPACE_36, SPACE_36)
        layout.setSpacing(SPACE_MEDIUM)
        header = QHBoxLayout()
        header.setSpacing(SPACE_SMALL)
        heading = QLabel()
        heading.setProperty("role", "screenHeading")
        self._localizer.bind_text(heading, TextId.HISTORY_TITLE)
        header.addWidget(heading)
        header.addStretch(1)
        refresh = QPushButton()
        refresh.setObjectName("refreshOperationHistoryButton")
        refresh.setProperty("role", "secondaryAction")
        set_fluent_icon(refresh, FluentIconName.REFRESH)
        refresh.clicked.connect(self.refresh)
        self._localizer.bind_text(refresh, TextId.REFRESH)
        header.addWidget(refresh)
        copy = QPushButton()
        copy.setObjectName("copyOperationHistoryButton")
        copy.setProperty("role", "secondaryAction")
        set_fluent_icon(copy, FluentIconName.COPY)
        self._localizer.bind_text(copy, TextId.HISTORY_COPY)
        copy.clicked.connect(self.copy_selected)
        header.addWidget(copy)
        save = QPushButton()
        save.setObjectName("saveOperationHistoryButton")
        save.setProperty("role", "primaryAction")
        set_fluent_icon(save, FluentIconName.SAVE)
        self._localizer.bind_text(save, TextId.HISTORY_SAVE)
        save.clicked.connect(self.save_log)
        header.addWidget(save)
        shortcut = QShortcut(QKeySequence("Ctrl+S"), self)
        shortcut.setObjectName("saveOperationHistoryShortcut")
        shortcut.activated.connect(self.save_log)
        layout.addLayout(header)

        guidance = QLabel()
        self._localizer.bind_text(guidance, TextId.HISTORY_GUIDANCE)
        guidance.setProperty("role", "muted")
        guidance.setWordWrap(True)
        layout.addWidget(guidance)
        self._state = QLabel()
        self._state.setObjectName("operationHistoryStateLabel")
        self._state.setWordWrap(True)
        layout.addWidget(self._state)

        scroll = QScrollArea()
        scroll.setWidgetResizable(True)
        container = QWidget()
        container.setObjectName("operationHistoryContainer")
        self._rows_layout = QVBoxLayout(container)
        self._rows_layout.setAlignment(Qt.AlignmentFlag.AlignTop)
        self._rows_layout.setSpacing(SPACE_12)
        scroll.setWidget(container)
        layout.addWidget(scroll, 1)

    @property
    def row_count(self) -> int:
        return len(self._rows)

    @property
    def active_dialogs(self) -> tuple[OperationDetailsDialog, ...]:
        return tuple(self._dialogs)

    def activate(self) -> None:
        """Show the existing log snapshot without reloading on every visit."""

        if self._has_snapshot or self._history_refresh_active:
            return
        self.refresh()

    def refresh(self) -> None:
        if self._history_refresh_active:
            return
        self._history_refresh_active = True
        self._token += 1
        token = self._token
        # A stale rendered snapshot stays completely stable while refreshing.
        # Only a true first load needs a visible loading state.
        if not self._has_rendered_snapshot:
            self._state.setText(self._localizer.text(TextId.HISTORY_LOADING))
        self._runner.submit(
            token,
            lambda: self._actions.list_operation_history(
                OperationHistoryQuery(limit=MAX_OPERATION_HISTORY_PAGE_SIZE)
            ),
            self._history_loaded,
            self._history_failed,
        )

    def _history_loaded(self, token: int, value: object) -> None:
        if token != self._token:
            return
        self._history_refresh_active = False
        if not isinstance(value, tuple) or any(
            not isinstance(item, OperationHistoryItem) for item in value
        ):
            self._state.setText(self._localizer.text(TextId.HISTORY_INVALID))
            return
        incoming = tuple(value[:MAX_OPERATION_HISTORY_PAGE_SIZE])
        self._has_snapshot = True
        if self._has_rendered_snapshot and incoming == self._items:
            # No data changed: preserve every row widget and layout position.
            self._state.setText(
                self._localizer.text(TextId.HISTORY_EMPTY) if not incoming else ""
            )
            return
        self._items = incoming
        self._has_rendered_snapshot = True
        self._clear_rows()
        for item in self._items:
            row = self._row(item)
            self._rows.append(row)
            self._rows_layout.addWidget(row)
        self._state.setText(
            self._localizer.text(TextId.HISTORY_EMPTY) if not self._items else ""
        )

    def _history_failed(self, token: int, _error: BaseException) -> None:
        if token == self._token:
            self._history_refresh_active = False
            self._state.setText(self._localizer.text(TextId.HISTORY_READ_ERROR))

    def _row(self, item: OperationHistoryItem) -> QFrame:
        row = QFrame()
        row.setObjectName(f"operationHistoryRow_{item.operation_id}")
        row.setProperty("role", "operationRow")
        layout = QVBoxLayout(row)
        layout.setContentsMargins(SPACE_12, SPACE_12, SPACE_12, SPACE_12)
        layout.setSpacing(SPACE_4)
        top = QHBoxLayout()
        top.setSpacing(SPACE_SMALL)
        title = QLabel(
            item.movie_title or self._localizer.text(TextId.HISTORY_UNLINKED)
        )
        title.setObjectName(f"operationHistoryTitle_{item.operation_id}")
        title.setProperty("role", "rowTitle")
        top.addWidget(title, 1)
        state = QLabel(
            f"{_operation_text(self._localizer, item.operation.value)}  ·  "
            f"{_status_text(self._localizer, item.state.value)}"
        )
        state.setObjectName(f"operationHistoryState_{item.operation_id}")
        state.setProperty("operationState", item.state.value)
        status_icon = QToolButton()
        status_icon.setObjectName(f"operationStatusIcon_{item.operation_id}")
        status_icon.setAutoRaise(True)
        status_icon.setEnabled(False)
        status_icon.setAccessibleName(_status_text(self._localizer, item.state.value))
        set_fluent_icon(status_icon, _status_icon_name(item.state))
        top.addWidget(status_icon)
        top.addWidget(state)
        layout.addLayout(top)
        path = ElidedPathLabel(
            f"{self._localizer.text(TextId.HISTORY_FROM)}: {item.source_path}  →  "
            f"{self._localizer.text(TextId.HISTORY_TO)}: {item.destination_path}"
        )
        path.setObjectName(f"operationHistoryPath_{item.operation_id}")
        path.setAccessibleName(
            self._localizer.text(TextId.ACCESSIBILITY_OPERATION_PATHS)
        )
        self._localizer.mark_ltr(path)
        layout.addWidget(path)
        footer = QHBoxLayout()
        footer.setSpacing(SPACE_SMALL)
        timestamp = QLabel(format_datetime(item.created_at))
        timestamp.setObjectName(f"operationHistoryTimestamp_{item.operation_id}")
        timestamp.setProperty("role", "muted")
        footer.addWidget(timestamp)
        if item.reverses_operation_id is not None:
            reverse = QLabel(self._localizer.text(TextId.HISTORY_REVERSE))
            reverse.setProperty("role", "muted")
            footer.addWidget(reverse)
        footer.addStretch(1)
        details = QPushButton(self._localizer.text(TextId.DETAILS))
        details.setObjectName(f"operationDetailsButton_{item.operation_id}")
        details.setProperty("role", "secondaryAction")
        set_fluent_icon(details, FluentIconName.OPERATION_DETAILS)
        details.clicked.connect(
            lambda _checked=False, operation_id=item.operation_id: self._select_and_load(
                operation_id
            )
        )
        footer.addWidget(details)
        layout.addLayout(footer)
        return row

    def _select_and_load(self, operation_id: str) -> None:
        self._load_details(operation_id)

    def copy_selected(self) -> None:
        # Copy mirrors Save exactly: same complete log, same ordering, same
        # human-readable formatting, with the clipboard as the destination.
        if not self._items:
            self._state.setText(self._localizer.text(TextId.HISTORY_EMPTY))
            return
        QApplication.clipboard().setText(format_operation_history(self._items))
        self._state.setText(self._localizer.text(TextId.HISTORY_COPY_SUCCESS))

    def save_log(self) -> None:
        if not self._items:
            self._state.setText(self._localizer.text(TextId.HISTORY_EMPTY))
            return
        path, _filter = QFileDialog.getSaveFileName(
            self,
            self._localizer.text(TextId.HISTORY_SAVE),
            "dropsort-operations.txt",
            "Text files (*.txt)",
        )
        if not path:
            return
        saver = getattr(self._actions, "save_operation_history", None)
        if not callable(saver):
            self._state.setText(self._localizer.text(TextId.HISTORY_SAVE_ERROR))
            return
        try:
            saver(self._items, path)
        except (OSError, ValueError):
            self._state.setText(self._localizer.text(TextId.HISTORY_SAVE_ERROR))
            return
        self._state.setText(self._localizer.text(TextId.HISTORY_SAVE_SUCCESS))

    def _load_details(self, operation_id: str) -> None:
        # A details request supersedes any pending list refresh token.
        self._history_refresh_active = False
        self._token += 1
        token = self._token
        self._state.setText(self._localizer.text(TextId.HISTORY_LOADING_DETAILS))
        self._runner.submit(
            token,
            lambda: self._actions.get_operation_details(operation_id),
            self._details_loaded,
            self._history_failed,
        )

    def _details_loaded(self, token: int, value: object) -> None:
        if token != self._token:
            return
        if not isinstance(value, OperationDetails):
            self._state.setText(self._localizer.text(TextId.HISTORY_INVALID_DETAILS))
            return
        self._state.clear()
        dialog = OperationDetailsDialog(
            self._actions,
            value,
            runner=self._runner,
            localizer=self._localizer,
            parent=self,
        )
        dialog.operation_changed.connect(self._operation_changed)
        dialog.finished.connect(lambda _result, active=dialog: self._dialogs.discard(active))
        self._dialogs.add(dialog)
        dialog.show()

    def _operation_changed(self, _value: object) -> None:
        self.catalog_changed.emit()
        self.refresh()

    def invalidate_snapshot(self) -> None:
        """Mark history stale while preserving its currently painted rows."""

        self._has_snapshot = False

    def _clear_rows(self) -> None:
        for row in self._rows:
            row.hide()
            row.setParent(None)
        self._rows.clear()

    def invalidate_pending_tasks(self) -> None:
        self._history_refresh_active = False
        self._token += 1
        for dialog in tuple(self._dialogs):
            dialog.invalidate_pending_tasks()

    def wait_for_pending_tasks(self) -> None:
        waiter = getattr(self._runner, "wait_for_done", None)
        if callable(waiter):
            waiter()
        for dialog in tuple(self._dialogs):
            dialog.invalidate_pending_tasks()


class OperationDetailsDialog(QDialog):
    operation_changed = Signal(object)

    def __init__(
        self,
        actions: OperationHistoryUiActions,
        details: OperationDetails,
        *,
        runner: TaskRunner | None = None,
        localizer: UiLocalizer | None = None,
        parent=None,
    ) -> None:
        super().__init__(parent)
        self._actions = actions
        self._localizer = localizer or UiLocalizer()
        self._details = details
        self._runner = runner or QtTaskRunner()
        self._token = 0
        self._recovery: RecoveryAssessment | None = None
        self._preview_dialogs: set[UndoPreviewDialog] = set()
        self.setWindowTitle(self._localizer.text(TextId.OPERATION_DETAILS))
        self.setWindowModality(Qt.WindowModality.WindowModal)
        self.resize(760, 580)

        layout = QVBoxLayout(self)
        layout.setContentsMargins(SPACE_LARGE, SPACE_LARGE, SPACE_LARGE, SPACE_LARGE)
        layout.setSpacing(SPACE_MEDIUM)
        heading = QLabel(self._localizer.text(TextId.OPERATION_DETAILS))
        heading.setProperty("role", "screenHeading")
        layout.addWidget(heading)
        form = QFormLayout()
        form.setSpacing(SPACE_SMALL)
        form.addRow(
            self._localizer.text(TextId.HISTORY_FIELD_STATE),
            _value(
                _status_text(self._localizer, details.history.state.value),
                "operationDetailsState",
            ),
        )
        form.addRow(
            self._localizer.text(TextId.HISTORY_FIELD_OPERATION),
            _value(
                _operation_text(self._localizer, details.history.operation.value),
                "operationDetailsType",
            ),
        )
        form.addRow(self._localizer.text(TextId.HISTORY_FIELD_SOURCE), _path(details.history.source_path, "operationDetailsSourcePath"))
        form.addRow(
            self._localizer.text(TextId.HISTORY_FIELD_DESTINATION),
            _path(details.history.destination_path, "operationDetailsDestinationPath"),
        )
        form.addRow(self._localizer.text(TextId.HISTORY_FIELD_STRATEGY), _value(details.strategy or self._localizer.text(TextId.HISTORY_NOT_RECORDED), "operationDetailsStrategy"))
        form.addRow(
            self._localizer.text(TextId.HISTORY_FIELD_CURRENT_PATH),
            _path(details.current_catalog_path or self._localizer.text(TextId.HISTORY_NOT_LINKED), "operationDetailsCurrentPath"),
        )
        form.addRow(
            self._localizer.text(TextId.HISTORY_FIELD_CREATED),
            _value(_format_timestamp(details.history.created_at), "operationDetailsCreatedAt"),
        )
        layout.addLayout(form)
        if details.error_code or details.error_message:
            error = QLabel(f"{details.error_code or 'Operation error'}: {details.error_message or ''}")
            error.setObjectName("operationDetailsError")
            error.setProperty("role", "error")
            error.setWordWrap(True)
            layout.addWidget(error)
        self._state = QLabel(self._localizer.text(TextId.HISTORY_READ_ONLY))
        self._state.setObjectName("operationDetailsActionState")
        self._state.setWordWrap(True)
        layout.addWidget(self._state)
        layout.addStretch(1)

        buttons = QHBoxLayout()
        self._undo = QPushButton(self._localizer.text(TextId.PREVIEW_UNDO))
        self._undo.setObjectName("prepareUndoButton")
        self._undo.setProperty("role", "organizationAction")
        set_fluent_icon(self._undo, FluentIconName.BACK)
        self._undo.setEnabled(
            details.history.state is OperationStatus.COMMITTED
            and details.history.media_file_id is not None
            and details.reversed_by_operation_id is None
        )
        self._undo.clicked.connect(self.prepare_undo)
        buttons.addWidget(self._undo)
        self._inspect = QPushButton(self._localizer.text(TextId.INSPECT_RECOVERY))
        self._inspect.setObjectName("inspectRecoveryButton")
        self._inspect.setProperty("role", "secondaryAction")
        set_fluent_icon(self._inspect, FluentIconName.OPERATION_DETAILS)
        self._inspect.setEnabled(
            details.history.state
            in {
                OperationStatus.EXECUTING,
                OperationStatus.FS_VERIFIED,
                OperationStatus.RECOVERY_REQUIRED,
            }
        )
        self._inspect.clicked.connect(self.inspect_recovery)
        buttons.addWidget(self._inspect)
        self._recover = QPushButton(self._localizer.text(TextId.ATTEMPT_RECOVERY))
        self._recover.setObjectName("attemptRecoveryButton")
        self._recover.setProperty("role", "organizationConfirm")
        set_fluent_icon(self._recover, FluentIconName.REFRESH)
        self._recover.setEnabled(False)
        self._recover.clicked.connect(self.attempt_recovery)
        buttons.addWidget(self._recover)
        buttons.addStretch(1)
        close = QPushButton(self._localizer.text(TextId.CLOSE))
        close.setProperty("role", "secondaryAction")
        close.clicked.connect(self.reject)
        buttons.addWidget(close)
        layout.addLayout(buttons)

    @property
    def active_preview_dialogs(self) -> tuple[UndoPreviewDialog, ...]:
        return tuple(self._preview_dialogs)

    def prepare_undo(self) -> None:
        self._token += 1
        token = self._token
        self._undo.setEnabled(False)
        self._state.setText(self._localizer.text(TextId.UNDO_REVALIDATING))
        operation_id = self._details.history.operation_id
        self._runner.submit(
            token,
            lambda: self._actions.prepare_undo(operation_id),
            self._undo_prepared,
            self._undo_failed,
        )

    def _undo_prepared(self, token: int, value: object) -> None:
        if token != self._token:
            if isinstance(value, UndoPreview):
                self._actions.discard_undo_preview(value.preview_id)
            return
        if not isinstance(value, UndoPreview):
            self._state.setText(self._localizer.text(TextId.UNDO_INVALID))
            return
        self._state.setText(self._localizer.text(TextId.UNDO_PREPARED))
        dialog = UndoPreviewDialog(
            self._actions,
            value,
            runner=self._runner,
            localizer=self._localizer,
            parent=self,
        )
        dialog.undo_succeeded.connect(self._undo_succeeded)
        dialog.finished.connect(lambda _result, active=dialog: self._preview_dialogs.discard(active))
        self._preview_dialogs.add(dialog)
        dialog.show()

    def _undo_failed(self, token: int, error: BaseException) -> None:
        if token != self._token:
            return
        if isinstance(error, UndoNotEligibleError):
            self._state.setText(_undo_ineligible_message(error))
        elif isinstance(error, OperationHistoryError):
            self._state.setText(self._localizer.text(TextId.UNDO_VERIFY_FAILED))
        else:
            self._state.setText(self._localizer.text(TextId.UNDO_PREPARE_FAILED))

    def _undo_succeeded(self, value: object) -> None:
        self._state.setText(self._localizer.text(TextId.UNDO_COMPLETED))
        self.operation_changed.emit(value)

    def inspect_recovery(self) -> None:
        self._token += 1
        token = self._token
        self._inspect.setEnabled(False)
        self._recover.setEnabled(False)
        operation_id = self._details.history.operation_id
        self._runner.submit(
            token,
            lambda: self._actions.inspect_recovery(operation_id),
            self._recovery_inspected,
            self._recovery_failed,
        )

    def _recovery_inspected(self, token: int, value: object) -> None:
        if token != self._token:
            return
        self._inspect.setEnabled(True)
        if not isinstance(value, RecoveryAssessment):
            self._state.setText(self._localizer.text(TextId.RECOVERY_INVALID))
            return
        self._recovery = value
        self._state.setText(value.explanation)
        self._recover.setEnabled(value.action_available)

    def _recovery_failed(self, token: int, _error: BaseException) -> None:
        if token == self._token:
            self._inspect.setEnabled(True)
            self._state.setText(self._localizer.text(TextId.RECOVERY_INSPECT_FAILED))

    def attempt_recovery(self) -> None:
        if self._recovery is None or not self._recovery.action_available:
            return
        self._token += 1
        token = self._token
        self._recover.setEnabled(False)
        operation_id = self._details.history.operation_id
        self._runner.submit(
            token,
            lambda: self._actions.attempt_recovery(operation_id),
            self._recovery_succeeded,
            self._recovery_failed,
        )

    def _recovery_succeeded(self, token: int, value: object) -> None:
        if token != self._token:
            return
        if not isinstance(value, RecoveryResult):
            self._state.setText("DropSort received an invalid recovery result.")
            return
        self._state.setText(
            self._localizer.text(TextId.RECOVERY_COMPLETE, state=value.state.value)
        )
        self.operation_changed.emit(value)

    def invalidate_pending_tasks(self) -> None:
        self._history_refresh_active = False
        self._token += 1
        for dialog in tuple(self._preview_dialogs):
            dialog.invalidate_pending_tasks()

    def reject(self) -> None:
        self.invalidate_pending_tasks()
        super().reject()

    def closeEvent(self, event: QCloseEvent) -> None:
        self.invalidate_pending_tasks()
        super().closeEvent(event)


class UndoPreviewDialog(QDialog):
    undo_succeeded = Signal(object)

    def __init__(
        self,
        actions: OperationHistoryUiActions,
        preview: UndoPreview,
        *,
        runner: TaskRunner | None = None,
        localizer: UiLocalizer | None = None,
        parent=None,
    ) -> None:
        super().__init__(parent)
        self._actions = actions
        self._localizer = localizer or UiLocalizer()
        self._preview = preview
        self._runner = runner or QtTaskRunner()
        self._token = 0
        self._executing = False
        self._completed = False
        self.setWindowTitle(self._localizer.text(TextId.UNDO_PREVIEW))
        self.setWindowModality(Qt.WindowModality.WindowModal)
        self.resize(720, 460)
        layout = QVBoxLayout(self)
        layout.setContentsMargins(SPACE_LARGE, SPACE_LARGE, SPACE_LARGE, SPACE_LARGE)
        layout.setSpacing(SPACE_MEDIUM)
        heading = QLabel(self._localizer.text(TextId.UNDO_PREVIEW))
        heading.setProperty("role", "screenHeading")
        layout.addWidget(heading)
        warning = QLabel(self._localizer.text(TextId.UNDO_WARNING))
        warning.setWordWrap(True)
        warning.setProperty("role", "muted")
        layout.addWidget(warning)
        form = QFormLayout()
        form.addRow(self._localizer.text(TextId.ORGANIZE_FROM), _path(preview.source_path, "undoPreviewFromPath"))
        form.addRow(self._localizer.text(TextId.ORGANIZE_TO), _path(preview.destination_path, "undoPreviewToPath"))
        form.addRow(self._localizer.text(TextId.HISTORY_FIELD_OPERATION), _value(preview.operation.value, "undoPreviewOperation"))
        form.addRow(self._localizer.text(TextId.HISTORY_FIELD_FILE_SIZE), _value(format_file_size(preview.file_size), "undoPreviewFileSize"))
        transfer = (
            "Same-drive safe transfer"
            if preview.same_volume
            else "Cross-drive copy, flush, SHA-256 verify, finalize, then source removal"
        )
        form.addRow(self._localizer.text(TextId.HISTORY_FIELD_TRANSFER), _value(transfer, "undoPreviewTransfer"))
        layout.addLayout(form)
        self._state = QLabel(self._localizer.text(TextId.UNDO_NO_CHANGE))
        self._state.setObjectName("undoPreviewState")
        self._state.setWordWrap(True)
        layout.addWidget(self._state)
        layout.addStretch(1)
        buttons = QHBoxLayout()
        buttons.addStretch(1)
        self._cancel = QPushButton(self._localizer.text(TextId.CANCEL))
        self._cancel.setProperty("role", "secondaryAction")
        self._cancel.clicked.connect(self.reject)
        buttons.addWidget(self._cancel)
        self._confirm = QPushButton(self._localizer.text(TextId.CONFIRM_UNDO))
        self._confirm.setObjectName("confirmUndoButton")
        self._confirm.setProperty("role", "organizationConfirm")
        set_fluent_icon(self._confirm, FluentIconName.ORGANIZE)
        self._confirm.clicked.connect(self.confirm)
        buttons.addWidget(self._confirm)
        layout.addLayout(buttons)

    @property
    def state_message(self) -> str:
        return self._state.text()

    def confirm(self) -> None:
        if self._executing or self._completed:
            return
        self._executing = True
        self._token += 1
        token = self._token
        self._confirm.setEnabled(False)
        self._cancel.setEnabled(False)
        self._state.setText(self._localizer.text(TextId.UNDO_RUNNING))
        self._runner.submit(
            token,
            lambda: self._actions.confirm_undo(self._preview.preview_id),
            self._confirmed,
            self._confirmation_failed,
        )

    def _confirmed(self, token: int, value: object) -> None:
        if token != self._token:
            return
        self._executing = False
        if not isinstance(value, UndoResult):
            self._state.setText(self._localizer.text(TextId.UNDO_RESULT_INVALID))
            self._cancel.setText(self._localizer.text(TextId.CLOSE))
            self._cancel.setEnabled(True)
            return
        self._completed = True
        self._state.setText(self._localizer.text(TextId.UNDO_CATALOG_UPDATED))
        self._cancel.setText(self._localizer.text(TextId.CLOSE))
        self._cancel.setEnabled(True)
        self.undo_succeeded.emit(value)

    def _confirmation_failed(self, token: int, error: BaseException) -> None:
        if token != self._token:
            return
        self._executing = False
        self._cancel.setEnabled(True)
        if isinstance(error, UndoRecoveryRequiredError):
            self._state.setText(self._localizer.text(TextId.UNDO_RECOVERY_REQUIRED))
        elif isinstance(error, UndoError):
            self._state.setText(self._localizer.text(TextId.UNDO_FAILED))
        else:
            self._state.setText(self._localizer.text(TextId.UNDO_FAILED_GENERIC))

    def reject(self) -> None:
        if self._executing:
            return
        if not self._completed:
            self._actions.discard_undo_preview(self._preview.preview_id)
        super().reject()

    def closeEvent(self, event: QCloseEvent) -> None:
        if self._executing:
            event.ignore()
            return
        super().closeEvent(event)

    def invalidate_pending_tasks(self) -> None:
        self._history_refresh_active = False
        self._token += 1


class ElidedPathLabel(QLabel):
    """Keep full technical paths in the tooltip while bounding the row width."""

    def __init__(self, full_text: str, parent=None) -> None:
        super().__init__(parent)
        self._full_text = full_text
        self.setMinimumWidth(0)
        self.setSizePolicy(QSizePolicy.Policy.Ignored, QSizePolicy.Policy.Preferred)
        self.setWordWrap(False)
        self.setToolTip(full_text)
        self.setTextInteractionFlags(
            Qt.TextInteractionFlag.TextSelectableByMouse
            | Qt.TextInteractionFlag.TextSelectableByKeyboard
        )
        self._render_elided()

    def resizeEvent(self, event) -> None:
        self._render_elided()
        super().resizeEvent(event)

    def _render_elided(self) -> None:
        if self.width() <= 0:
            self.setText(self._full_text)
            return
        self.setText(
            QFontMetrics(self.font()).elidedText(
                self._full_text,
                Qt.TextElideMode.ElideMiddle,
                max(120, self.width()),
            )
        )


def _operation_text(localizer: UiLocalizer, value: str) -> str:
    return localizer.text(
        {
            "MOVE": TextId.HISTORY_OPERATION_MOVE,
            "RENAME": TextId.HISTORY_OPERATION_RENAME,
        }.get(value, TextId.HISTORY_OPERATION_MOVE)
    ) if value in {"MOVE", "RENAME"} else value.title()


def _status_text(localizer: UiLocalizer, value: str) -> str:
    ids = {
        "PLANNED": TextId.HISTORY_STATUS_PLANNED,
        "VALIDATED": TextId.HISTORY_STATUS_VALIDATED,
        "EXECUTING": TextId.HISTORY_STATUS_IN_PROGRESS,
        "FS_VERIFIED": TextId.HISTORY_STATUS_VERIFIED,
        "COMMITTED": TextId.HISTORY_STATUS_COMPLETED,
        "FAILED": TextId.HISTORY_STATUS_FAILED,
        "RECOVERY_REQUIRED": TextId.HISTORY_STATUS_RECOVERY_REQUIRED,
    }
    text_id = ids.get(value)
    return localizer.text(text_id) if text_id is not None else value.title()


def _operation_plain_text(localizer: UiLocalizer, item: OperationHistoryItem) -> str:
    return "\n".join(
        (
            f"{_operation_text(localizer, item.operation.value)} — "
            f"{_status_text(localizer, item.state.value)}",
            item.movie_title or localizer.text(TextId.HISTORY_UNLINKED),
            f"{localizer.text(TextId.HISTORY_FROM)}: {item.source_path}",
            f"{localizer.text(TextId.HISTORY_TO)}: {item.destination_path}",
            format_datetime(item.created_at),
            f"{localizer.text(TextId.HISTORY_OPERATION_ID)}: {item.operation_id}",
        )
    )


def _value(text: str, object_name: str) -> QLabel:
    label = QLabel(text)
    label.setObjectName(object_name)
    label.setWordWrap(True)
    label.setTextInteractionFlags(Qt.TextInteractionFlag.TextSelectableByMouse | Qt.TextInteractionFlag.TextSelectableByKeyboard)
    return label


def _path(text: str, object_name: str) -> QLabel:
    label = _value(text, object_name)
    label.setTextInteractionFlags(Qt.TextInteractionFlag.TextSelectableByMouse | Qt.TextInteractionFlag.TextSelectableByKeyboard)
    label.setLayoutDirection(Qt.LayoutDirection.LeftToRight)
    label.setProperty("dropsortTechnicalLtr", True)
    return label


def _format_timestamp(value: datetime) -> str:
    return format_datetime(value)


def _undo_ineligible_message(error: UndoNotEligibleError) -> str:
    messages = {
        "NOT_COMMITTED": "Undo is not currently safe because this operation is not committed.",
        "NO_MEDIA_FILE": "Undo is not currently safe because no current catalog file is linked.",
        "ALREADY_REVERSED": "Undo is not currently safe because a reverse operation already exists.",
        "SUPERSEDED": "Undo is not currently safe because a later operation superseded it.",
        "CATALOG_PATH_CHANGED": "Undo is not currently safe because the catalog path changed.",
        "SOURCE_MISSING": "Undo is not currently safe because the current file is missing.",
        "SOURCE_CHANGED": "Undo is not currently safe because the current file changed.",
        "DESTINATION_EXISTS": "Undo is not currently safe because the exact old path is occupied.",
        "CASE_COLLISION": "Undo is not currently safe because the old path has a casing collision.",
        "SAME_FILE": "Undo is not currently safe because both paths identify the same file.",
        "LINK_TRAVERSAL": "Undo is not currently safe because a link or reparse path is involved.",
        "UNSAFE_PATH": "Undo is not currently safe because a historical path is unavailable or unsafe.",
        "INVALID_OPERATION": "Undo is not currently safe for this operation.",
    }
    return messages.get(str(error.code), "Undo is not currently safe for this operation.")
