from __future__ import annotations

from collections.abc import Callable
from pathlib import Path

from PySide6.QtCore import Qt, Signal
from PySide6.QtGui import QCloseEvent
from PySide6.QtWidgets import (
    QDialog,
    QFileDialog,
    QFormLayout,
    QHBoxLayout,
    QLabel,
    QLineEdit,
    QPushButton,
    QVBoxLayout,
    QWidget,
)

from dropsort.application.dto.organization import (
    OrganizationOperation,
    OrganizationPreview,
    OrganizationResult,
)
from dropsort.application.errors import (
    OrganizationExecutionError,
    OrganizationPreviewStaleError,
    OrganizationRecoveryRequiredError,
    OrganizationValidationError,
    OrganizationValidationCode,
)
from dropsort.ui.common.formatting import format_file_size
from dropsort.ui.common.icon import FluentIconName, set_fluent_icon
from dropsort.ui.common.tasks import QtTaskRunner, TaskRunner
from dropsort.ui.common.theme import SPACE_LARGE, SPACE_MEDIUM, SPACE_SMALL
from dropsort.ui.contracts import OrganizationUiActions
from dropsort.ui.localization import TextId, UiLocalizer


FolderPicker = Callable[[QWidget], str]


class OrganizeFileDialog(QDialog):
    """Exact preview and explicit one-shot authorization for one physical file."""

    organization_succeeded = Signal(object)

    def __init__(
        self,
        actions: OrganizationUiActions,
        *,
        media_file_id: int,
        current_path: Path,
        file_size: int,
        runner: TaskRunner | None = None,
        folder_picker: FolderPicker | None = None,
        localizer: UiLocalizer | None = None,
        parent=None,
    ) -> None:
        super().__init__(parent)
        self._actions = actions
        self._localizer = localizer or UiLocalizer()
        self._media_file_id = media_file_id
        self._current_path = current_path
        self._runner = runner or QtTaskRunner()
        self._folder_picker = folder_picker or (
            lambda parent: _pick_folder(parent, self._localizer)
        )
        self._destination_root: Path | None = None
        self._preview: OrganizationPreview | None = None
        self._token = 0
        self._executing = False
        self.setWindowTitle(self._localizer.text(TextId.ORGANIZE_TITLE))
        self.setWindowModality(Qt.WindowModality.WindowModal)
        self.resize(760, 560)

        layout = QVBoxLayout(self)
        layout.setContentsMargins(SPACE_LARGE, SPACE_LARGE, SPACE_LARGE, SPACE_LARGE)
        layout.setSpacing(SPACE_MEDIUM)
        heading = QLabel()
        heading.setProperty("role", "screenHeading")
        self._localizer.bind_text(heading, TextId.ORGANIZE_TITLE)
        layout.addWidget(heading)
        guidance = QLabel()
        guidance.setProperty("role", "muted")
        guidance.setWordWrap(True)
        self._localizer.bind_text(guidance, TextId.ORGANIZE_GUIDANCE)
        layout.addWidget(guidance)

        layout.addWidget(_section_label(self._localizer.text(TextId.ORGANIZE_FROM)))
        self._from_path = _path_label(str(current_path), "organizationFromPath")
        self._localizer.mark_ltr(self._from_path)
        layout.addWidget(self._from_path)
        current_folder = _path_label(str(current_path.parent), "organizationCurrentFolder")
        self._localizer.mark_ltr(current_folder)
        current_folder.setProperty("role", "muted")
        layout.addWidget(current_folder)

        destination_controls = QHBoxLayout()
        self._choose_button = QPushButton()
        self._choose_button.setObjectName("chooseOrganizationDestinationButton")
        self._choose_button.setProperty("role", "secondaryAction")
        set_fluent_icon(self._choose_button, FluentIconName.OPEN_FOLDER)
        self._choose_button.clicked.connect(self.choose_destination)
        self._localizer.bind_text(self._choose_button, TextId.CHOOSE_DESTINATION)
        destination_controls.addWidget(self._choose_button)
        self._filename = QLineEdit(current_path.name)
        self._localizer.mark_ltr(self._filename)
        self._filename.setObjectName("organizationFilenameInput")
        self._filename.setAccessibleName("Destination filename")
        self._filename.textChanged.connect(self._invalidate_preview_for_edit)
        destination_controls.addWidget(self._filename, 1)
        self._refresh_button = QPushButton()
        self._refresh_button.setObjectName("refreshOrganizationPreviewButton")
        self._refresh_button.setProperty("role", "secondaryAction")
        set_fluent_icon(self._refresh_button, FluentIconName.REFRESH)
        self._refresh_button.clicked.connect(self.refresh_preview)
        self._refresh_button.setEnabled(False)
        self._localizer.bind_text(self._refresh_button, TextId.REFRESH_PREVIEW)
        destination_controls.addWidget(self._refresh_button)
        layout.addLayout(destination_controls)

        layout.addWidget(_section_label(self._localizer.text(TextId.ORGANIZE_TO)))
        self._to_path = _path_label("Choose a destination folder", "organizationToPath")
        layout.addWidget(self._to_path)
        self._to_folder = _path_label("", "organizationDestinationFolder")
        self._to_folder.setProperty("role", "muted")
        layout.addWidget(self._to_folder)

        facts = QFormLayout()
        facts.setSpacing(SPACE_SMALL)
        self._operation = QLabel(self._localizer.text(TextId.NOT_VALIDATED))
        self._operation.setObjectName("organizationOperationValue")
        facts.addRow(self._localizer.text(TextId.ORGANIZE_OPERATION), self._operation)
        size = QLabel(format_file_size(file_size))
        size.setObjectName("organizationFileSizeValue")
        facts.addRow(self._localizer.text(TextId.ORGANIZE_FILE_SIZE), size)
        self._volumes = QLabel(self._localizer.text(TextId.NOT_VALIDATED))
        self._volumes.setObjectName("organizationVolumesValue")
        facts.addRow(self._localizer.text(TextId.ORGANIZE_VOLUMES), self._volumes)
        self._transfer = QLabel(self._localizer.text(TextId.NOT_VALIDATED))
        self._transfer.setObjectName("organizationTransferValue")
        self._transfer.setWordWrap(True)
        facts.addRow(self._localizer.text(TextId.ORGANIZE_TRANSFER), self._transfer)
        layout.addLayout(facts)

        self._state = QLabel(self._localizer.text(TextId.ORGANIZE_READY))
        self._state.setObjectName("organizationStateLabel")
        self._state.setWordWrap(True)
        layout.addWidget(self._state)
        layout.addStretch(1)

        actions_layout = QHBoxLayout()
        actions_layout.addStretch(1)
        self._cancel_button = QPushButton()
        self._cancel_button.setObjectName("cancelOrganizationButton")
        self._cancel_button.setProperty("role", "secondaryAction")
        self._cancel_button.clicked.connect(self.reject)
        self._localizer.bind_text(self._cancel_button, TextId.CANCEL)
        actions_layout.addWidget(self._cancel_button)
        self._confirm_button = QPushButton()
        self._confirm_button.setObjectName("confirmOrganizationButton")
        self._confirm_button.setProperty("role", "organizationConfirm")
        set_fluent_icon(self._confirm_button, FluentIconName.ORGANIZE)
        self._confirm_button.setEnabled(False)
        self._confirm_button.clicked.connect(self.confirm)
        self._localizer.bind_text(self._confirm_button, TextId.CONFIRM_MOVE_RENAME)
        actions_layout.addWidget(self._confirm_button)
        layout.addLayout(actions_layout)

    @property
    def state_message(self) -> str:
        return self._state.text()

    @property
    def is_executing(self) -> bool:
        return self._executing

    def choose_destination(self) -> None:
        if self._executing:
            return
        selected = self._folder_picker(self)
        if not selected:
            return
        self._destination_root = Path(selected)
        self.refresh_preview()

    def refresh_preview(self) -> None:
        if self._executing or self._destination_root is None:
            return
        self._token += 1
        token = self._token
        self._discard_current_preview()
        self._confirm_button.setEnabled(False)
        self._choose_button.setEnabled(False)
        self._refresh_button.setEnabled(False)
        self._state.setText(self._localizer.text(TextId.ORGANIZE_VALIDATING))
        destination_root = self._destination_root
        filename = self._filename.text()
        self._runner.submit(
            token,
            lambda: self._actions.prepare_organization(
                self._media_file_id,
                destination_root,
                filename,
            ),
            self._preview_succeeded,
            self._preview_failed,
        )

    def confirm(self) -> None:
        if self._executing or self._preview is None:
            return
        self._executing = True
        preview_id = self._preview.preview_id
        self._confirm_button.setEnabled(False)
        self._cancel_button.setEnabled(False)
        self._choose_button.setEnabled(False)
        self._filename.setEnabled(False)
        self._refresh_button.setEnabled(False)
        self._state.setText(self._localizer.text(TextId.ORGANIZE_RUNNING))
        token = self._token
        self._runner.submit(
            token,
            lambda: self._actions.confirm_organization(preview_id),
            self._confirmation_succeeded,
            self._confirmation_failed,
        )

    def _preview_succeeded(self, token: int, value: object) -> None:
        if token != self._token or self._executing:
            if isinstance(value, OrganizationPreview):
                self._actions.discard_organization_preview(value.preview_id)
            return
        self._choose_button.setEnabled(True)
        self._refresh_button.setEnabled(True)
        if not isinstance(value, OrganizationPreview):
            self._state.setText(self._localizer.text(TextId.ORGANIZE_INVALID_PREVIEW))
            return
        self._preview = value
        self._to_path.setText(value.destination_path)
        self._localizer.mark_ltr(self._to_path)
        self._to_folder.setText(str(Path(value.destination_path).parent))
        self._operation.setText(value.operation.value)
        self._volumes.setText(f"{value.source_volume} → {value.destination_volume}")
        if value.same_volume:
            self._transfer.setText(self._localizer.text(TextId.ORGANIZE_SAME_DRIVE))
        else:
            self._transfer.setText(self._localizer.text(TextId.ORGANIZE_CROSS_DRIVE))
        self._state.setText(self._localizer.text(TextId.ORGANIZE_VALID))
        self._confirm_button.setText(_confirmation_label(value.operation, self._localizer))
        self._confirm_button.setEnabled(True)

    def _preview_failed(self, token: int, error: BaseException) -> None:
        if token != self._token or self._executing:
            return
        self._choose_button.setEnabled(True)
        self._refresh_button.setEnabled(self._destination_root is not None)
        self._preview = None
        self._state.setText(_preview_error_message(error, self._localizer))

    def _confirmation_succeeded(self, token: int, value: object) -> None:
        if token != self._token:
            return
        self._executing = False
        if not isinstance(value, OrganizationResult):
            self._state.setText(self._localizer.text(TextId.ORGANIZE_RESULT_INVALID))
            return
        self._state.setText(self._localizer.text(TextId.ORGANIZE_COMPLETE))
        self._cancel_button.setText(self._localizer.text(TextId.CLOSE))
        self._cancel_button.setEnabled(True)
        self.organization_succeeded.emit(value)

    def _confirmation_failed(self, token: int, error: BaseException) -> None:
        if token != self._token:
            return
        self._executing = False
        self._cancel_button.setEnabled(True)
        self._cancel_button.setText(self._localizer.text(TextId.CLOSE))
        self._state.setText(_confirmation_error_message(error, self._localizer))

    def _invalidate_preview_for_edit(self) -> None:
        if self._executing or self._preview is None:
            return
        self._discard_current_preview()
        self._confirm_button.setEnabled(False)
        self._refresh_button.setEnabled(self._destination_root is not None)
        self._state.setText(self._localizer.text(TextId.ORGANIZE_FILENAME_CHANGED))

    def invalidate_pending_delivery(self) -> None:
        self._token += 1

    def wait_for_pending_tasks(self) -> None:
        waiter = getattr(self._runner, "wait_for_done", None)
        if callable(waiter):
            waiter()

    def _discard_current_preview(self) -> None:
        preview = self._preview
        self._preview = None
        if preview is not None:
            self._actions.discard_organization_preview(preview.preview_id)

    def reject(self) -> None:
        if self._executing:
            return
        self._discard_current_preview()
        super().reject()

    def closeEvent(self, event: QCloseEvent) -> None:
        if self._executing:
            event.ignore()
            return
        self._discard_current_preview()
        self.invalidate_pending_delivery()
        super().closeEvent(event)


def _pick_folder(parent: QWidget, localizer: UiLocalizer | None = None) -> str:
    localizer = localizer or UiLocalizer()
    return QFileDialog.getExistingDirectory(
        parent, localizer.text(TextId.CHOOSE_DESTINATION_DIALOG)
    )


def _section_label(text: str) -> QLabel:
    label = QLabel(text)
    label.setProperty("role", "sectionHeading")
    return label


def _path_label(text: str, object_name: str) -> QLabel:
    label = QLabel(text)
    label.setObjectName(object_name)
    label.setWordWrap(True)
    label.setTextInteractionFlags(Qt.TextInteractionFlag.TextSelectableByMouse | Qt.TextInteractionFlag.TextSelectableByKeyboard)
    return label


def _confirmation_label(
    operation: OrganizationOperation,
    localizer: UiLocalizer | None = None,
) -> str:
    if localizer is not None:
        return localizer.text({
            OrganizationOperation.MOVE: TextId.ORGANIZE_CONFIRM_MOVE,
            OrganizationOperation.RENAME: TextId.ORGANIZE_CONFIRM_RENAME,
            OrganizationOperation.MOVE_AND_RENAME: TextId.ORGANIZE_CONFIRM_MOVE_AND_RENAME,
        }[operation])
    return {
        OrganizationOperation.MOVE: "Move File",
        OrganizationOperation.RENAME: "Rename File",
        OrganizationOperation.MOVE_AND_RENAME: "Move & Rename File",
    }[operation]


def _preview_error_message(
    error: BaseException,
    localizer: UiLocalizer | None = None,
) -> str:
    localizer = localizer or UiLocalizer()
    if isinstance(error, OrganizationValidationError):
        key = {
            OrganizationValidationCode.DESTINATION_EXISTS: TextId.ORGANIZE_ERROR_DEST_EXISTS,
            OrganizationValidationCode.CASE_COLLISION: TextId.ORGANIZE_ERROR_CASE_COLLISION,
            OrganizationValidationCode.SAME_FILE: TextId.ORGANIZE_ERROR_SAME_FILE,
            OrganizationValidationCode.SOURCE_MISSING: TextId.ORGANIZE_ERROR_SOURCE_MISSING,
            OrganizationValidationCode.LINK_TRAVERSAL: TextId.ORGANIZE_ERROR_LINK,
            OrganizationValidationCode.UNSAFE_PATH: TextId.ORGANIZE_ERROR_UNSAFE,
            OrganizationValidationCode.CATALOG_MISMATCH: TextId.ORGANIZE_ERROR_CATALOG,
        }.get(
            error.code,
            TextId.ORGANIZE_ERROR_VALIDATE,
        )
        return localizer.text(key)
    return localizer.text(TextId.ORGANIZE_ERROR_PREPARE)


def _confirmation_error_message(
    error: BaseException,
    localizer: UiLocalizer | None = None,
) -> str:
    localizer = localizer or UiLocalizer()
    if isinstance(error, OrganizationPreviewStaleError):
        return localizer.text(TextId.ORGANIZE_ERROR_STALE)
    if isinstance(error, OrganizationRecoveryRequiredError):
        return localizer.text(TextId.ORGANIZE_ERROR_RECOVERY)
    if isinstance(error, OrganizationExecutionError):
        return localizer.text(TextId.ORGANIZE_ERROR_EXECUTION)
    return localizer.text(TextId.ORGANIZE_ERROR_GENERIC)
