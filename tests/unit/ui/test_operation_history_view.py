from __future__ import annotations

from dataclasses import dataclass, field
from datetime import UTC, datetime

from PySide6.QtCore import Qt
from PySide6.QtTest import QTest
from PySide6.QtWidgets import QApplication, QLabel, QPushButton

from dropsort.application.dto.operation_history import (
    OperationDetails,
    OperationHistoryItem,
    OperationHistoryQuery,
    OperationKind,
    OperationStatus,
    RecoveryAssessment,
    RecoveryDisposition,
    RecoveryResult,
    UndoPreview,
    UndoResult,
)
from dropsort.application.errors import UndoNotEligibleError
from dropsort.application.errors import (
    OperationHistoryError,
    UndoExecutionError,
    UndoRecoveryRequiredError,
)
from dropsort.application.dto.operation_history import UndoEligibilityCode
from dropsort.ui.history.view import (
    OperationDetailsDialog,
    OperationHistoryView,
    UndoPreviewDialog,
)


class ImmediateRunner:
    def submit(self, token, task, on_success, on_failure) -> None:
        try:
            on_success(token, task())
        except BaseException as error:
            on_failure(token, error)


@dataclass
class DeferredTask:
    token: int
    task: object
    on_success: object
    on_failure: object


class DeferredRunner:
    def __init__(self) -> None:
        self.tasks: list[DeferredTask] = []
        self.waited = False

    def submit(self, token, task, on_success, on_failure) -> None:
        self.tasks.append(DeferredTask(token, task, on_success, on_failure))

    def wait_for_done(self) -> None:
        self.waited = True


def _item(**overrides: object) -> OperationHistoryItem:
    values: dict[str, object] = {
        "operation_id": "operation-1",
        "operation": OperationKind.MOVE,
        "state": OperationStatus.COMMITTED,
        "source_path": r"D:\Incoming\Movie.mkv",
        "destination_path": r"D:\Movies\Movie.mkv",
        "media_file_id": 10,
        "movie_title": "Movie",
        "created_at": datetime(2026, 8, 12, 12, tzinfo=UTC),
        "updated_at": datetime(2026, 8, 12, 12, 1, tzinfo=UTC),
        "reverses_operation_id": None,
    }
    values.update(overrides)
    return OperationHistoryItem(**values)  # type: ignore[arg-type]


def _details(**overrides: object) -> OperationDetails:
    values: dict[str, object] = {
        "history": _item(),
        "current_catalog_path": r"D:\Movies\Movie.mkv",
        "strategy": "hardlink-unlink",
        "error_code": None,
        "error_message": None,
        "reversed_by_operation_id": None,
        "source_size": 100,
        "destination_size": 100,
        "destination_sha256": None,
    }
    values.update(overrides)
    return OperationDetails(**values)  # type: ignore[arg-type]


def _preview() -> UndoPreview:
    return UndoPreview(
        "preview-1",
        "operation-1",
        10,
        r"D:\Movies\Movie.mkv",
        r"D:\Incoming\Movie.mkv",
        OperationKind.MOVE,
        True,
        100,
        "D:\\",
        "D:\\",
        (),
    )


def _undo_result() -> UndoResult:
    return UndoResult(
        "operation-1",
        "operation-2",
        10,
        r"D:\Movies\Movie.mkv",
        r"D:\Incoming\Movie.mkv",
        "hardlink-unlink",
    )


@dataclass
class FakeHistoryActions:
    items: tuple[OperationHistoryItem, ...] = field(default_factory=lambda: (_item(),))
    details: OperationDetails = field(default_factory=_details)
    preview: UndoPreview = field(default_factory=_preview)
    undo_result: UndoResult = field(default_factory=_undo_result)
    recovery: RecoveryAssessment = field(
        default_factory=lambda: RecoveryAssessment(
            "operation-1",
            OperationStatus.COMMITTED,
            RecoveryDisposition.NOT_REQUIRED,
            False,
            True,
            False,
            "No recovery required",
        )
    )
    calls: list[str] = field(default_factory=list)
    prepare_error: BaseException | None = None

    def list_operation_history(self, query: OperationHistoryQuery | None = None):
        self.calls.append("list")
        return self.items

    def get_operation_details(self, operation_id: str):
        self.calls.append(f"details:{operation_id}")
        return self.details

    def prepare_undo(self, operation_id: str):
        self.calls.append(f"prepare:{operation_id}")
        if self.prepare_error is not None:
            raise self.prepare_error
        return self.preview

    def confirm_undo(self, preview_id: str):
        self.calls.append(f"confirm:{preview_id}")
        return self.undo_result

    def discard_undo_preview(self, preview_id: str):
        self.calls.append(f"discard:{preview_id}")

    def inspect_recovery(self, operation_id: str):
        self.calls.append(f"inspect:{operation_id}")
        return self.recovery

    def attempt_recovery(self, operation_id: str):
        self.calls.append(f"recover:{operation_id}")
        return RecoveryResult(operation_id, OperationStatus.COMMITTED)


def _button(widget, name: str) -> QPushButton:
    value = widget.findChild(QPushButton, name)
    assert value is not None
    return value


def _label(widget, name: str) -> QLabel:
    value = widget.findChild(QLabel, name)
    assert value is not None
    return value


def test_history_loads_in_background_and_renders_state_movie_and_paths(qapp: QApplication) -> None:
    actions = FakeHistoryActions()
    view = OperationHistoryView(actions, runner=ImmediateRunner())

    view.refresh()

    assert actions.calls == ["list"]
    assert view.row_count == 1
    assert "Completed" in _label(view, "operationHistoryState_operation-1").text()
    assert "Movie" in _label(view, "operationHistoryTitle_operation-1").text()
    assert r"D:\Movies\Movie.mkv" in _label(view, "operationHistoryPath_operation-1").text()


def test_history_activation_reuses_rendered_snapshot_without_navigation_reload(
    qapp: QApplication,
) -> None:
    actions = FakeHistoryActions()
    view = OperationHistoryView(actions, runner=ImmediateRunner())

    view.activate()
    first_row = view._rows[0]
    view.activate()

    assert actions.calls == ["list"]
    assert view._rows[0] is first_row


def test_history_activation_coalesces_pending_load_and_stale_snapshot_keeps_rows(
    qapp: QApplication,
) -> None:
    actions = FakeHistoryActions()
    runner = DeferredRunner()
    view = OperationHistoryView(actions, runner=runner)

    view.activate()
    view.activate()
    assert len(runner.tasks) == 1

    pending = runner.tasks.pop(0)
    pending.on_success(pending.token, actions.list_operation_history(OperationHistoryQuery()))
    first_row = view._rows[0]

    view.invalidate_snapshot()
    assert view._rows[0] is first_row
    state_before = view._state.text()
    view.activate()
    assert len(runner.tasks) == 1
    assert view._rows[0] is first_row
    assert view._state.text() == state_before

    stale = runner.tasks.pop(0)
    stale.on_success(stale.token, actions.list_operation_history(OperationHistoryQuery()))
    assert view._rows[0] is first_row
    assert view._state.text() == state_before


def test_details_are_loaded_without_undo_or_recovery_side_effects(qapp: QApplication) -> None:
    actions = FakeHistoryActions()
    view = OperationHistoryView(actions, runner=ImmediateRunner())
    view.refresh()

    QTest.mouseClick(_button(view, "operationDetailsButton_operation-1"), Qt.MouseButton.LeftButton)

    assert actions.calls == ["list", "details:operation-1"]
    dialog = next(iter(view.active_dialogs))
    assert isinstance(dialog, OperationDetailsDialog)
    assert _label(dialog, "operationDetailsSourcePath").text() == r"D:\Incoming\Movie.mkv"
    assert _label(dialog, "operationDetailsDestinationPath").text() == r"D:\Movies\Movie.mkv"
    assert "hardlink-unlink" in _label(dialog, "operationDetailsStrategy").text()


def test_undo_requires_preview_then_one_explicit_confirmation(qapp: QApplication) -> None:
    actions = FakeHistoryActions()
    dialog = OperationDetailsDialog(actions, _details(), runner=ImmediateRunner())
    changed: list[object] = []
    dialog.operation_changed.connect(changed.append)

    _button(dialog, "prepareUndoButton").click()

    assert actions.calls == ["prepare:operation-1"]
    preview_dialog = next(iter(dialog.active_preview_dialogs))
    assert isinstance(preview_dialog, UndoPreviewDialog)
    assert _label(preview_dialog, "undoPreviewFromPath").text() == r"D:\Movies\Movie.mkv"
    assert _label(preview_dialog, "undoPreviewToPath").text() == r"D:\Incoming\Movie.mkv"
    assert "confirm" not in " ".join(actions.calls)

    _button(preview_dialog, "confirmUndoButton").click()
    _button(preview_dialog, "confirmUndoButton").click()

    assert actions.calls.count("confirm:preview-1") == 1
    assert changed == [_undo_result()]
    assert "completed" in preview_dialog.state_message.casefold()


def test_ineligible_undo_has_controlled_message_and_no_preview(qapp: QApplication) -> None:
    actions = FakeHistoryActions(
        prepare_error=UndoNotEligibleError(
            UndoEligibilityCode.SUPERSEDED,
            "raw internal detail",
        )
    )
    dialog = OperationDetailsDialog(actions, _details(), runner=ImmediateRunner())

    _button(dialog, "prepareUndoButton").click()

    assert dialog.active_preview_dialogs == ()
    assert "not currently safe" in _label(dialog, "operationDetailsActionState").text().casefold()
    assert "raw internal detail" not in _label(dialog, "operationDetailsActionState").text()


def test_ineligible_reason_codes_have_stable_human_explanations(qapp: QApplication) -> None:
    for code, expected in (
        (UndoEligibilityCode.ALREADY_REVERSED, "already exists"),
        (UndoEligibilityCode.SUPERSEDED, "later operation"),
        (UndoEligibilityCode.SOURCE_CHANGED, "file changed"),
        (UndoEligibilityCode.DESTINATION_EXISTS, "old path is occupied"),
        (UndoEligibilityCode.UNSAFE_PATH, "unavailable or unsafe"),
    ):
        actions = FakeHistoryActions(prepare_error=UndoNotEligibleError(code, "raw"))
        dialog = OperationDetailsDialog(actions, _details(), runner=ImmediateRunner())
        dialog.prepare_undo()
        message = _label(dialog, "operationDetailsActionState").text().casefold()
        assert expected in message
        assert "raw" not in message


def test_ambiguous_recovery_preserves_both_and_has_no_action_button(qapp: QApplication) -> None:
    assessment = RecoveryAssessment(
        "operation-1",
        OperationStatus.RECOVERY_REQUIRED,
        RecoveryDisposition.AMBIGUOUS_BOTH_EXIST,
        True,
        True,
        False,
        "Both source and destination exist; DropSort will preserve both",
    )
    actions = FakeHistoryActions(
        details=_details(history=_item(state=OperationStatus.RECOVERY_REQUIRED)),
        recovery=assessment,
    )
    dialog = OperationDetailsDialog(actions, actions.details, runner=ImmediateRunner())

    _button(dialog, "inspectRecoveryButton").click()

    assert "preserve both" in _label(dialog, "operationDetailsActionState").text().casefold()
    assert _button(dialog, "attemptRecoveryButton").isEnabled() is False
    assert not any(call.startswith("recover:") for call in actions.calls)


def test_safe_recovery_is_separate_explicit_action(qapp: QApplication) -> None:
    assessment = RecoveryAssessment(
        "operation-1",
        OperationStatus.FS_VERIFIED,
        RecoveryDisposition.VERIFIED_DESTINATION_ONLY,
        False,
        True,
        True,
        "The verified destination can be committed to the catalog",
    )
    actions = FakeHistoryActions(
        details=_details(history=_item(state=OperationStatus.FS_VERIFIED)),
        recovery=assessment,
    )
    dialog = OperationDetailsDialog(actions, actions.details, runner=ImmediateRunner())
    changed: list[object] = []
    dialog.operation_changed.connect(changed.append)

    _button(dialog, "inspectRecoveryButton").click()
    assert actions.calls == ["inspect:operation-1"]
    assert _button(dialog, "attemptRecoveryButton").isEnabled() is True
    _button(dialog, "attemptRecoveryButton").click()

    assert actions.calls == ["inspect:operation-1", "recover:operation-1"]
    assert changed == [RecoveryResult("operation-1", OperationStatus.COMMITTED)]


def test_stale_delivery_and_shutdown_are_safe(qapp: QApplication) -> None:
    runner = DeferredRunner()
    actions = FakeHistoryActions()
    view = OperationHistoryView(actions, runner=runner)
    view.refresh()
    assert len(runner.tasks) == 1
    task = runner.tasks[0]

    view.invalidate_pending_tasks()
    task.on_success(task.token, actions.items)  # type: ignore[operator]
    view.wait_for_pending_tasks()

    assert view.row_count == 0
    assert runner.waited is True


def test_history_empty_failure_invalid_payload_and_reverse_marker_are_controlled(
    qapp: QApplication,
) -> None:
    empty = FakeHistoryActions(items=())
    view = OperationHistoryView(empty, runner=ImmediateRunner())
    view.refresh()
    assert "no file operations" in _label(view, "operationHistoryStateLabel").text().casefold()

    failing = FakeHistoryActions()
    failing.list_operation_history = lambda query=None: (_ for _ in ()).throw(  # type: ignore[method-assign]
        OperationHistoryError("raw")
    )
    failed_view = OperationHistoryView(failing, runner=ImmediateRunner())
    failed_view.refresh()
    assert "could not read" in _label(failed_view, "operationHistoryStateLabel").text().casefold()

    invalid = OperationHistoryView(FakeHistoryActions(), runner=ImmediateRunner())
    invalid._history_loaded(0, object())
    assert "invalid" in _label(invalid, "operationHistoryStateLabel").text().casefold()

    reverse_item = _item(reverses_operation_id="original-operation")
    reverse = OperationHistoryView(FakeHistoryActions(items=(reverse_item,)), runner=ImmediateRunner())
    reverse.refresh()
    assert "Reverse operation" in [
        label.text() for label in reverse.findChildren(QLabel)
    ]


def test_invalid_details_and_history_refresh_after_changed_operation(qapp: QApplication) -> None:
    actions = FakeHistoryActions()
    view = OperationHistoryView(actions, runner=ImmediateRunner())
    view._details_loaded(0, object())
    assert "invalid operation details" in _label(view, "operationHistoryStateLabel").text().casefold()
    view._operation_changed(object())
    assert actions.calls == ["list"]


def test_details_show_error_information_and_guard_invalid_async_results(qapp: QApplication) -> None:
    details = _details(error_code="PermissionError", error_message="denied")
    actions = FakeHistoryActions(details=details)
    dialog = OperationDetailsDialog(actions, details, runner=ImmediateRunner())
    assert "PermissionError" in _label(dialog, "operationDetailsError").text()

    dialog._undo_prepared(0, object())
    assert "invalid undo preview" in _label(dialog, "operationDetailsActionState").text().casefold()
    dialog._recovery_inspected(0, object())
    assert "invalid recovery assessment" in _label(dialog, "operationDetailsActionState").text().casefold()
    dialog._recovery_succeeded(0, object())
    assert "invalid recovery result" in _label(dialog, "operationDetailsActionState").text().casefold()


def test_stale_undo_preview_is_discarded_and_failure_classes_are_redacted(qapp: QApplication) -> None:
    runner = DeferredRunner()
    actions = FakeHistoryActions()
    dialog = OperationDetailsDialog(actions, _details(), runner=runner)
    dialog.prepare_undo()
    task = runner.tasks[0]
    dialog.invalidate_pending_tasks()
    task.on_success(task.token, _preview())  # type: ignore[operator]
    assert actions.calls[-1] == "discard:preview-1"

    for error, expected in (
        (OperationHistoryError("raw-history"), "could not verify"),
        (RuntimeError("raw-runtime"), "could not prepare"),
    ):
        current = FakeHistoryActions(prepare_error=error)
        failed = OperationDetailsDialog(current, _details(), runner=ImmediateRunner())
        failed.prepare_undo()
        message = _label(failed, "operationDetailsActionState").text().casefold()
        assert expected in message
        assert str(error) not in message


def test_recovery_inspection_failure_and_inert_attempt_are_controlled(qapp: QApplication) -> None:
    actions = FakeHistoryActions(details=_details(history=_item(state=OperationStatus.FS_VERIFIED)))
    actions.inspect_recovery = lambda operation_id: (_ for _ in ()).throw(  # type: ignore[method-assign]
        RuntimeError("raw-recovery")
    )
    dialog = OperationDetailsDialog(actions, actions.details, runner=ImmediateRunner())
    dialog.inspect_recovery()
    assert "could not inspect" in _label(dialog, "operationDetailsActionState").text().casefold()
    calls = list(actions.calls)
    dialog.attempt_recovery()
    assert actions.calls == calls


def test_undo_confirmation_failures_invalid_results_cancel_and_stale_delivery_are_safe(
    qapp: QApplication,
) -> None:
    cases = (
        (UndoRecoveryRequiredError("reverse-1", "raw"), "requires recovery"),
        (UndoExecutionError("raw"), "could not safely complete"),
        (RuntimeError("raw"), "could not complete"),
    )
    for error, expected in cases:
        actions = FakeHistoryActions()
        actions.confirm_undo = lambda preview_id, problem=error: (_ for _ in ()).throw(problem)  # type: ignore[method-assign]
        dialog = UndoPreviewDialog(actions, _preview(), runner=ImmediateRunner())
        dialog.confirm()
        assert expected in dialog.state_message.casefold()
        assert "raw" not in dialog.state_message

    invalid = FakeHistoryActions()
    invalid.confirm_undo = lambda preview_id: object()  # type: ignore[method-assign]
    invalid_dialog = UndoPreviewDialog(invalid, _preview(), runner=ImmediateRunner())
    invalid_dialog.confirm()
    assert "invalid result" in invalid_dialog.state_message.casefold()

    canceled = FakeHistoryActions()
    canceled_dialog = UndoPreviewDialog(canceled, _preview(), runner=ImmediateRunner())
    canceled_dialog.reject()
    assert canceled.calls == ["discard:preview-1"]

    runner = DeferredRunner()
    stale = FakeHistoryActions()
    stale_dialog = UndoPreviewDialog(stale, _preview(), runner=runner)
    stale_dialog.confirm()
    stale_dialog.reject()
    assert stale_dialog.result() == 0
    stale_dialog.invalidate_pending_tasks()
    task = runner.tasks[0]
    task.on_success(task.token, _undo_result())  # type: ignore[operator]
    task.on_failure(task.token, RuntimeError("late"))  # type: ignore[operator]
    assert "completed" not in stale_dialog.state_message.casefold()


def test_closing_details_invalidates_late_preview_delivery(qapp: QApplication) -> None:
    runner = DeferredRunner()
    actions = FakeHistoryActions()
    dialog = OperationDetailsDialog(actions, _details(), runner=runner)
    dialog.prepare_undo()
    task = runner.tasks[0]

    dialog.reject()
    task.on_success(task.token, _preview())  # type: ignore[operator]

    assert dialog.active_preview_dialogs == ()
    assert actions.calls[-1] == "discard:preview-1"
