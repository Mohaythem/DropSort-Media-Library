from __future__ import annotations

from threading import Event

from PySide6.QtCore import QEventLoop, QThread, QTimer
from PySide6.QtWidgets import QApplication

from dropsort.ui.common.tasks import FunctionTaskWorker, QtTaskRunner


def test_worker_delivers_success_with_session_token() -> None:
    worker = FunctionTaskWorker(7, lambda: "done")
    results: list[tuple[int, object]] = []
    worker.signals.succeeded.connect(lambda token, value: results.append((token, value)))

    worker.run()

    assert results == [(7, "done")]


def test_worker_delivers_failure_without_touching_widgets() -> None:
    error = RuntimeError("technical detail")

    def fail() -> object:
        raise error

    worker = FunctionTaskWorker(8, fail)
    failures: list[tuple[int, BaseException]] = []
    worker.signals.failed.connect(lambda token, value: failures.append((token, value)))

    worker.run()

    assert failures == [(8, error)]


def test_qt_runner_executes_backend_off_ui_thread_and_delivers_on_ui_thread(
    qapp: QApplication,
) -> None:
    ui_thread = QThread.currentThread()
    task_threads: list[QThread] = []
    callback_threads: list[QThread] = []
    loop = QEventLoop()
    timed_out: list[bool] = []
    timeout = QTimer()
    timeout.setSingleShot(True)
    timeout.timeout.connect(lambda: (timed_out.append(True), loop.quit()))

    def task() -> str:
        task_threads.append(QThread.currentThread())
        return "ok"

    def succeeded(_token: int, _result: object) -> None:
        callback_threads.append(QThread.currentThread())
        loop.quit()

    runner = QtTaskRunner()
    runner.submit(1, task, succeeded, lambda _token, _error: loop.quit())
    timeout.start(2_000)
    loop.exec()
    timeout.stop()

    assert timed_out == []
    assert task_threads and task_threads[0] != ui_thread
    assert callback_threads == [ui_thread]


def test_qt_runner_delivers_backend_failure_on_ui_thread(qapp: QApplication) -> None:
    ui_thread = QThread.currentThread()
    callback_threads: list[QThread] = []
    failures: list[str] = []
    loop = QEventLoop()
    timeout = QTimer()
    timeout.setSingleShot(True)
    timeout.timeout.connect(loop.quit)

    def fail() -> object:
        raise RuntimeError("failure")

    def failed(_token: int, error: BaseException) -> None:
        callback_threads.append(QThread.currentThread())
        failures.append(str(error))
        loop.quit()

    runner = QtTaskRunner()
    runner.submit(1, fail, lambda _token, _result: loop.quit(), failed)
    timeout.start(2_000)
    loop.exec()
    timeout.stop()

    assert failures == ["failure"]
    assert callback_threads == [ui_thread]


def test_runner_can_wait_for_backend_shutdown_without_running_callbacks_off_thread(
    qapp: QApplication,
) -> None:
    release = Event()
    callbacks: list[str] = []
    runner = QtTaskRunner()
    runner.submit(
        4,
        lambda: (release.wait(), "finished")[1],
        lambda _token, result: callbacks.append(str(result)),
        lambda _token, _error: callbacks.append("failed"),
    )

    release.set()
    runner.wait_for_done()
    qapp.processEvents()

    assert callbacks == ["finished"]


def test_qt_runner_delivers_progress_on_ui_thread(qapp: QApplication) -> None:
    ui_thread = QThread.currentThread()
    progress_threads: list[QThread] = []
    values: list[int] = []
    loop = QEventLoop()
    runner = QtTaskRunner()

    def task(report) -> str:
        report(1)
        report(2)
        return "done"

    runner.submit_progressive(
        9,
        task,
        lambda _token, value: (
            progress_threads.append(QThread.currentThread()),
            values.append(value),
        ),
        lambda _token, _result: loop.quit(),
        lambda _token, _error: loop.quit(),
    )
    QTimer.singleShot(2_000, loop.quit)
    loop.exec()

    assert values == [1, 2]
    assert progress_threads == [ui_thread, ui_thread]
