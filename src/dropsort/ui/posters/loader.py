from __future__ import annotations

from collections.abc import Callable
from typing import Protocol
import weakref

from PySide6.QtCore import QObject, QRunnable, QThreadPool, Signal, Slot

from dropsort.posters import PosterActions, PosterAsset, PosterRequest


class PosterReceiver(Protocol):
    def apply_poster(self, token: int, asset: PosterAsset | None) -> None: ...


class PosterRequestDispatcher(Protocol):
    def request(
        self,
        receiver: PosterReceiver,
        request: PosterRequest,
        token: int,
    ) -> None: ...


class _WorkerSignals(QObject):
    completed = Signal(object, object)


class _PosterRunnable(QRunnable):
    def __init__(self, actions: PosterActions, request: PosterRequest) -> None:
        super().__init__()
        self._actions = actions
        self._request = request
        self.signals = _WorkerSignals()

    @Slot()
    def run(self) -> None:
        try:
            asset = self._actions.load_poster(self._request)
        except BaseException:
            asset = None
        self.signals.completed.emit(self._request, asset)


class PosterLoader(QObject):
    """Bounded, coalescing bridge from blocking assets to UI-thread receivers."""

    idle = Signal()

    def __init__(
        self,
        actions: PosterActions,
        *,
        maximum_workers: int = 4,
        parent: QObject | None = None,
    ) -> None:
        super().__init__(parent)
        if isinstance(maximum_workers, bool) or not isinstance(maximum_workers, int) or maximum_workers <= 0:
            raise ValueError("maximum_workers must be a positive integer")
        self._actions = actions
        self._pool = QThreadPool(self)
        self._pool.setMaxThreadCount(maximum_workers)
        self._callbacks: dict[
            PosterRequest,
            list[tuple[weakref.ReferenceType[PosterReceiver], int]],
        ] = {}
        self._workers: dict[PosterRequest, _PosterRunnable] = {}
        self._accepting = True

    @property
    def maximum_workers(self) -> int:
        return self._pool.maxThreadCount()

    @property
    def active_request_count(self) -> int:
        return len(self._workers)

    @property
    def accepting_requests(self) -> bool:
        return self._accepting

    def request(
        self,
        receiver: PosterReceiver,
        request: PosterRequest,
        token: int,
    ) -> None:
        if not self._accepting:
            return
        callback = (weakref.ref(receiver), token)
        existing = self._callbacks.get(request)
        if existing is not None:
            existing.append(callback)
            return
        self._callbacks[request] = [callback]
        worker = _PosterRunnable(self._actions, request)
        worker.signals.completed.connect(self._deliver)
        self._workers[request] = worker
        self._pool.start(worker)

    @Slot(object, object)
    def _deliver(self, request: PosterRequest, asset: PosterAsset | None) -> None:
        callbacks = self._callbacks.pop(request, ())
        self._workers.pop(request, None)
        for receiver_reference, token in callbacks:
            receiver = receiver_reference()
            if receiver is None:
                continue
            try:
                receiver.apply_poster(token, asset)
            except RuntimeError:
                continue
        if not self._workers:
            self.idle.emit()

    def shutdown(self) -> None:
        self.invalidate_pending()
        self._pool.waitForDone()
        self._workers.clear()

    def invalidate_pending(self) -> None:
        """Prevent late deliveries without blocking the Qt close event."""
        self._accepting = False
        self._callbacks.clear()
