from __future__ import annotations

from dataclasses import dataclass, field
from pathlib import Path
from typing import Callable

from PySide6.QtWidgets import QApplication, QLabel, QLineEdit, QPushButton

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
)
from dropsort.ui.organization.dialog import OrganizeFileDialog


class ImmediateRunner:
    def submit(self, token: int, task, on_success, on_failure) -> None:
        try:
            on_success(token, task())
        except BaseException as error:
            on_failure(token, error)

    def wait_for_done(self) -> None:
        pass


@dataclass
class PendingTask:
    token: int
    task: Callable[[], object]
    on_success: Callable[[int, object], None]
    on_failure: Callable[[int, BaseException], None]


class DeferredRunner:
    def __init__(self) -> None:
        self.tasks: list[PendingTask] = []

    def submit(self, token: int, task, on_success, on_failure) -> None:
        self.tasks.append(PendingTask(token, task, on_success, on_failure))


class WaitRecordingRunner(ImmediateRunner):
    def __init__(self) -> None:
        self.waited = False

    def wait_for_done(self) -> None:
        self.waited = True


@dataclass
class FakeOrganizationActions:
    preview: OrganizationPreview
    result: OrganizationResult
    prepare_error: BaseException | None = None
    confirm_error: BaseException | None = None
    prepare_calls: list[tuple[int, Path, str]] = field(default_factory=list)
    confirm_calls: list[str] = field(default_factory=list)
    discarded: list[str] = field(default_factory=list)

    def prepare_organization(self, media_file_id, destination_root, destination_filename):
        self.prepare_calls.append((media_file_id, destination_root, destination_filename))
        if self.prepare_error is not None:
            raise self.prepare_error
        return self.preview

    def confirm_organization(self, preview_id):
        self.confirm_calls.append(preview_id)
        if self.confirm_error is not None:
            raise self.confirm_error
        return self.result

    def discard_organization_preview(self, preview_id):
        self.discarded.append(preview_id)


def _preview(**overrides: object) -> OrganizationPreview:
    values: dict[str, object] = {
        "preview_id": "preview-1",
        "media_file_id": 10,
        "source_path": r"D:\Incoming\Movie.mkv",
        "destination_path": r"D:\Movies\Movie.mkv",
        "operation": OrganizationOperation.MOVE,
        "same_volume": True,
        "file_size": 1_500_000_000,
        "source_volume": "D:\\",
        "destination_volume": "D:\\",
        "warnings": (),
    }
    values.update(overrides)
    return OrganizationPreview(**values)  # type: ignore[arg-type]


def _result() -> OrganizationResult:
    return OrganizationResult(
        "operation-1", 10, r"D:\Incoming\Movie.mkv", r"D:\Movies\Movie.mkv", "hardlink-unlink"
    )


def _button(dialog: OrganizeFileDialog, name: str) -> QPushButton:
    button = dialog.findChild(QPushButton, name)
    assert button is not None
    return button


def _label(dialog: OrganizeFileDialog, name: str) -> str:
    label = dialog.findChild(QLabel, name)
    assert label is not None
    return label.text()


def _dialog(actions, runner, picker=lambda _parent: r"D:\Movies") -> OrganizeFileDialog:
    return OrganizeFileDialog(
        actions,
        media_file_id=10,
        current_path=Path(r"D:\Incoming\Movie.mkv"),
        file_size=1_500_000_000,
        runner=runner,
        folder_picker=picker,
    )


def test_opening_and_canceling_dialog_perform_zero_backend_work(qapp: QApplication) -> None:
    actions = FakeOrganizationActions(_preview(), _result())
    dialog = _dialog(actions, ImmediateRunner(), lambda _parent: "")

    assert actions.prepare_calls == []
    assert actions.confirm_calls == []
    assert actions.discarded == []
    assert _button(dialog, "confirmOrganizationButton").isEnabled() is False
    dialog.choose_destination()
    dialog.reject()

    assert actions.prepare_calls == []
    assert actions.confirm_calls == []


def test_destination_selection_builds_exact_read_only_preview(qapp: QApplication) -> None:
    actions = FakeOrganizationActions(_preview(), _result())
    dialog = _dialog(actions, ImmediateRunner())

    dialog.choose_destination()

    assert actions.prepare_calls == [(10, Path(r"D:\Movies"), "Movie.mkv")]
    assert _label(dialog, "organizationFromPath") == r"D:\Incoming\Movie.mkv"
    assert _label(dialog, "organizationToPath") == r"D:\Movies\Movie.mkv"
    assert _label(dialog, "organizationOperationValue") == "MOVE"
    assert "Same-drive" in _label(dialog, "organizationTransferValue")
    assert _button(dialog, "confirmOrganizationButton").text() == "Move File"
    assert _button(dialog, "confirmOrganizationButton").isEnabled() is True
    assert actions.confirm_calls == []


def test_filename_edit_invalidates_preview_until_explicit_refresh(qapp: QApplication) -> None:
    actions = FakeOrganizationActions(
        _preview(destination_path=r"D:\Movies\Renamed.mkv", operation=OrganizationOperation.MOVE_AND_RENAME),
        _result(),
    )
    dialog = _dialog(actions, ImmediateRunner())
    dialog.choose_destination()
    filename = dialog.findChild(QLineEdit, "organizationFilenameInput")
    assert filename is not None

    filename.setText("Renamed.mkv")

    assert _button(dialog, "confirmOrganizationButton").isEnabled() is False
    assert "changed" in dialog.state_message.casefold()
    assert actions.discarded == ["preview-1"]
    dialog.refresh_preview()
    assert actions.prepare_calls[-1] == (10, Path(r"D:\Movies"), "Renamed.mkv")
    assert _button(dialog, "confirmOrganizationButton").text() == "Move & Rename File"


def test_confirmation_is_one_shot_and_emits_committed_result(qapp: QApplication) -> None:
    actions = FakeOrganizationActions(_preview(), _result())
    dialog = _dialog(actions, ImmediateRunner())
    delivered: list[OrganizationResult] = []
    dialog.organization_succeeded.connect(delivered.append)
    dialog.choose_destination()

    _button(dialog, "confirmOrganizationButton").click()
    _button(dialog, "confirmOrganizationButton").click()

    assert actions.confirm_calls == ["preview-1"]
    assert delivered == [_result()]
    assert dialog.is_executing is False
    assert "completed" in dialog.state_message.casefold()


def test_stale_validation_execution_and_recovery_failures_remain_distinct(qapp: QApplication) -> None:
    cases = (
        (OrganizationValidationError("raw-collision-detail"), "could not validate"),
        (OrganizationPreviewStaleError("raw-identity-detail"), "changed after preview"),
        (OrganizationExecutionError("raw-permission-detail"), "could not complete"),
        (OrganizationRecoveryRequiredError("op-1", "raw-recovery-detail"), "recovery is required"),
    )
    for error, message in cases:
        actions = FakeOrganizationActions(_preview(), _result())
        dialog = _dialog(actions, ImmediateRunner())
        if isinstance(error, OrganizationValidationError):
            actions.prepare_error = error
            dialog.choose_destination()
        else:
            dialog.choose_destination()
            actions.confirm_error = error
            dialog.confirm()

        assert message in dialog.state_message.casefold()
        assert str(error) not in dialog.state_message


def test_stale_result_delivery_is_ignored_and_active_close_is_refused(qapp: QApplication) -> None:
    runner = DeferredRunner()
    actions = FakeOrganizationActions(_preview(), _result())
    dialog = _dialog(actions, runner)
    dialog.choose_destination()
    runner.tasks[0].on_success(runner.tasks[0].token, _preview())
    dialog.confirm()
    assert dialog.is_executing is True

    dialog.reject()
    assert dialog.result() == 0
    dialog.invalidate_pending_delivery()
    runner.tasks[1].on_success(runner.tasks[1].token, _result())

    assert "completed" not in dialog.state_message.casefold()


def test_guard_branches_invalid_payloads_and_orderly_wait_are_controlled(
    qapp: QApplication,
) -> None:
    runner = DeferredRunner()
    actions = FakeOrganizationActions(_preview(), _result())
    picker_calls: list[bool] = []
    dialog = _dialog(
        actions,
        runner,
        lambda _parent: (picker_calls.append(True), r"D:\Movies")[1],
    )

    dialog.refresh_preview()
    dialog.confirm()
    assert runner.tasks == []
    dialog.choose_destination()
    first = runner.tasks[0]
    first.on_success(first.token, object())
    assert "invalid preview" in dialog.state_message.casefold()
    dialog.refresh_preview()
    second = runner.tasks[1]
    second.on_success(second.token, _preview())
    dialog.confirm()
    assert dialog.is_executing is True
    dialog.choose_destination()
    dialog.refresh_preview()
    dialog.confirm()
    dialog.show()
    qapp.processEvents()
    dialog.close()
    assert picker_calls == [True]
    assert dialog.isVisible() is True
    runner.tasks[2].on_success(runner.tasks[2].token, object())
    assert "invalid result" in dialog.state_message.casefold()
    dialog.close()

    wait_runner = WaitRecordingRunner()
    wait_dialog = _dialog(actions, wait_runner)
    wait_dialog.wait_for_pending_tasks()
    assert wait_runner.waited is True


def test_cross_volume_preview_and_unexpected_failures_have_safe_messages(
    qapp: QApplication,
) -> None:
    actions = FakeOrganizationActions(
        _preview(
            same_volume=False,
            source_volume="D:\\",
            destination_volume="E:\\",
            warnings=("CROSS_VOLUME",),
        ),
        _result(),
    )
    dialog = _dialog(actions, ImmediateRunner())
    dialog.choose_destination()
    assert "Cross-drive" in _label(dialog, "organizationTransferValue")

    actions.confirm_error = RuntimeError("raw-unexpected-detail")
    dialog.confirm()
    assert "could not complete this operation" in dialog.state_message.casefold()
    assert "raw-unexpected-detail" not in dialog.state_message

    preview_actions = FakeOrganizationActions(
        _preview(), _result(), prepare_error=RuntimeError("raw-preview-detail")
    )
    failed_preview = _dialog(preview_actions, ImmediateRunner())
    failed_preview.choose_destination()
    assert "could not prepare" in failed_preview.state_message.casefold()
    assert "raw-preview-detail" not in failed_preview.state_message


def test_stale_preview_callbacks_and_edit_before_preview_are_inert(qapp: QApplication) -> None:
    runner = DeferredRunner()
    actions = FakeOrganizationActions(_preview(), _result())
    dialog = _dialog(actions, runner)
    filename = dialog.findChild(QLineEdit, "organizationFilenameInput")
    assert filename is not None
    filename.setText("Before Preview.mkv")
    assert "changed" not in dialog.state_message.casefold()

    dialog.choose_destination()
    task = runner.tasks[0]
    dialog.invalidate_pending_delivery()
    task.on_success(task.token, _preview())
    task.on_failure(task.token, RuntimeError("late"))

    assert _button(dialog, "confirmOrganizationButton").isEnabled() is False
    assert actions.discarded == ["preview-1"]
