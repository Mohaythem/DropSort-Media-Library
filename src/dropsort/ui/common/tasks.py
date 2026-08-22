from __future__ import annotations

from collections.abc import Callable
from typing import Protocol

from PySide6.QtCore import QObject, QThread, Signal, Slot


Task = Callable[[], object]
ProgressReporter = Callable[[object], None]
ProgressTask = Callable[[ProgressReporter], object]
SuccessCallback = Callable[[int, object], None]
FailureCallback = Callable[[int, BaseException], None]
ProgressCallback = Callable[[int, object], None]


class TaskRunner(Protocol):
    def submit(
        self,
        token: int,
        task: Task,
        on_success: SuccessCallback,
        on_failure: FailureCallback,
    ) -> None: ...

    def submit_progressive(
        self,
        token: int,
        task: ProgressTask,
        on_progress: ProgressCallback,
        on_success: SuccessCallback,
        on_failure: FailureCallback,
    ) -> None: ...


class TaskSignals(QObject):
    succeeded = Signal(int, object)
    failed = Signal(int, object)
    finished = Signal()


class FunctionTaskWorker(QObject):
    """Run a backend callable only; this object never owns or touches widgets."""

    def __init__(self, token: int, task: Task) -> None:
        super().__init__()
        self._token = token
        self._task = task
        self.signals = TaskSignals(self)

    @Slot()
    def run(self) -> None:
        try:
            result = self._task()
        except BaseException as error:
            self.signals.failed.emit(self._token, error)
        else:
            self.signals.succeeded.emit(self._token, result)
        finally:
            self.signals.finished.emit()


class _TaskDelivery(QObject):
    finished = Signal(int)

    def __init__(
        self,
        identity: int,
        thread: _FunctionTaskThread,
        on_success: SuccessCallback,
        on_failure: FailureCallback,
        on_progress: ProgressCallback | None = None,
    ) -> None:
        super().__init__()
        self._identity = identity
        self._thread = thread
        self._on_success = on_success
        self._on_failure = on_failure
        self._on_progress = on_progress

    @Slot(int, object)
    def deliver_progress(self, token: int, value: object) -> None:
        if self._on_progress is not None:
            self._on_progress(token, value)

    @Slot()
    def deliver(self) -> None:
        try:
            if self._thread.error is not None:
                self._on_failure(self._thread.token, self._thread.error)
            else:
                self._on_success(self._thread.token, self._thread.result)
        finally:
            self.finished.emit(self._identity)


class _FunctionTaskThread(QThread):
    """Own one callable and publish its outcome only after QThread finishes."""

    def __init__(self, token: int, task: Task) -> None:
        super().__init__()
        self.token = token
        self.task = task
        self.result: object | None = None
        self.error: BaseException | None = None

    def run(self) -> None:
        try:
            self.result = self.task()
        except BaseException as error:
            self.error = error


class _ProgressTaskThread(_FunctionTaskThread):
    progressed = Signal(int, object)

    def __init__(self, token: int, task: ProgressTask) -> None:
        super().__init__(token, lambda: None)
        self._progress_task = task

    def run(self) -> None:
        try:
            self.result = self._progress_task(
                lambda value: self.progressed.emit(self.token, value)
            )
        except BaseException as error:
            self.error = error


class QtTaskRunner(QObject):
    """Small QThread boundary for blocking scan, metadata, and catalog calls."""

    def __init__(self, parent: QObject | None = None) -> None:
        super().__init__(parent)
        self._active: dict[
            int,
            tuple[_FunctionTaskThread, _TaskDelivery],
        ] = {}
        self._task_identity = 0

    def submit(
        self,
        token: int,
        task: Task,
        on_success: SuccessCallback,
        on_failure: FailureCallback,
    ) -> None:
        thread = _FunctionTaskThread(token, task)
        self._start(thread, on_success, on_failure)

    def submit_progressive(
        self,
        token: int,
        task: ProgressTask,
        on_progress: ProgressCallback,
        on_success: SuccessCallback,
        on_failure: FailureCallback,
    ) -> None:
        thread = _ProgressTaskThread(token, task)
        self._start(thread, on_success, on_failure, on_progress)

    def _start(
        self,
        thread: _FunctionTaskThread,
        on_success: SuccessCallback,
        on_failure: FailureCallback,
        on_progress: ProgressCallback | None = None,
    ) -> None:
        self._task_identity += 1
        identity = self._task_identity
        delivery = _TaskDelivery(identity, thread, on_success, on_failure, on_progress)
        delivery.moveToThread(self.thread())
        if isinstance(thread, _ProgressTaskThread):
            thread.progressed.connect(delivery.deliver_progress)
        thread.finished.connect(delivery.deliver)
        delivery.finished.connect(self._finish)
        self._active[identity] = (thread, delivery)
        self._live_runners.add(self)
        thread.start()

    def _finish(self, identity: int) -> None:
        active = self._active.pop(identity, None)
        if active is None:
            return
        if not self._active:
            self._live_runners.discard(self)

    def wait_for_done(self) -> None:
        """Orderly application shutdown; backend work never needs the UI thread."""
        for thread, _delivery in tuple(self._active.values()):
            thread.wait()

    _live_runners: set[QtTaskRunner] = set()
