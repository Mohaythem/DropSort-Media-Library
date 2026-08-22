from __future__ import annotations

from PySide6.QtCore import QEventLoop, QTimer
from PySide6.QtWidgets import QApplication, QWidget

from dropsort.posters import PosterAsset, PosterRequest
from dropsort.ui.posters.loader import PosterLoader


class FakeActions:
    def __init__(self, asset: PosterAsset | None) -> None:
        self.asset = asset
        self.calls = 0

    def load_poster(self, request: PosterRequest) -> PosterAsset | None:
        self.calls += 1
        return self.asset


class Receiver(QWidget):
    def __init__(self) -> None:
        super().__init__()
        self.results: list[tuple[int, PosterAsset | None]] = []

    def apply_poster(self, token: int, asset: PosterAsset | None) -> None:
        self.results.append((token, asset))


def _wait_for(loader: PosterLoader, qapp: QApplication) -> None:
    loop = QEventLoop()
    loader.idle.connect(loop.quit)
    QTimer.singleShot(2000, loop.quit)
    loop.exec()
    qapp.processEvents()


def test_loader_is_bounded_and_coalesces_duplicate_requests(qapp: QApplication, png_bytes: bytes) -> None:
    actions = FakeActions(PosterAsset("png", png_bytes))
    loader = PosterLoader(actions, maximum_workers=2)
    first = Receiver()
    second = Receiver()
    request = PosterRequest("tmdb", "/poster.png")

    loader.request(first, request, 1)
    loader.request(second, request, 2)
    _wait_for(loader, qapp)

    assert loader.maximum_workers == 2
    assert actions.calls == 1
    assert first.results == [(1, PosterAsset("png", png_bytes))]
    assert second.results == [(2, PosterAsset("png", png_bytes))]


def test_destroyed_receiver_ignores_late_result(qapp: QApplication, png_bytes: bytes) -> None:
    loader = PosterLoader(FakeActions(PosterAsset("png", png_bytes)))
    receiver = Receiver()
    loader.request(receiver, PosterRequest("tmdb", "/poster.png"), 1)
    receiver.deleteLater()
    qapp.processEvents()

    _wait_for(loader, qapp)

    assert loader.active_request_count == 0


def test_shutdown_during_active_load_is_safe(qapp: QApplication, png_bytes: bytes) -> None:
    loader = PosterLoader(FakeActions(PosterAsset("png", png_bytes)))
    receiver = Receiver()
    loader.request(receiver, PosterRequest("tmdb", "/poster.png"), 1)

    loader.shutdown()

    assert loader.active_request_count == 0
