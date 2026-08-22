from __future__ import annotations

import logging
import os
from hashlib import sha256
from pathlib import Path
import time

from PySide6.QtCore import (
    QCoreApplication,
    QEventLoop,
    QLockFile,
    QObject,
    QStandardPaths,
    Signal,
)
from PySide6.QtNetwork import QLocalServer, QLocalSocket


LOGGER = logging.getLogger(__name__)

ACTIVATE_MESSAGE = b"ACTIVATE\n"
_MAX_MESSAGE_BYTES = 64
_IPC_TIMEOUT_MS = 750


def default_server_name() -> str:
    """Return a stable, user-scoped local IPC name for the current session."""

    user = os.environ.get("USERNAME") or os.environ.get("USER") or "default"
    session = os.environ.get("SESSIONNAME") or "default"
    safe_user = "".join(character if character.isalnum() else "_" for character in user)
    safe_session = "".join(
        character if character.isalnum() else "_" for character in session
    )
    return f"DropSort.SingleInstance.{safe_user}.{safe_session}"


def default_lock_path(server_name: str) -> Path:
    temp_root = QStandardPaths.writableLocation(
        QStandardPaths.StandardLocation.TempLocation
    )
    if not temp_root:
        temp_root = str(Path.home() / "AppData" / "Local" / "Temp")
    identity = sha256(server_name.encode("utf-8")).hexdigest()[:24]
    return Path(temp_root) / "DropSort" / f"single-instance-{identity}.lock"


class SingleInstanceCoordinator(QObject):
    """Own the one DropSort process and forward activation requests to it."""

    activation_requested = Signal()

    def __init__(
        self,
        server_name: str | None = None,
        *,
        lock_path: Path | str | None = None,
        parent: QObject | None = None,
    ) -> None:
        super().__init__(parent)
        self._server_name = server_name or default_server_name()
        if not self._server_name.strip():
            raise ValueError("server_name must not be empty")
        self._server = QLocalServer(self)
        self._server.newConnection.connect(self._accept_connections)
        self._lock_path = Path(lock_path) if lock_path is not None else default_lock_path(
            self._server_name
        )
        self._lock_path.parent.mkdir(parents=True, exist_ok=True)
        self._lock = QLockFile(str(self._lock_path))
        self._sockets: set[QLocalSocket] = set()
        self._is_primary = False
        self._closed = False

    @property
    def server_name(self) -> str:
        return self._server_name

    @property
    def is_primary(self) -> bool:
        return self._is_primary

    def acquire(self) -> bool:
        """Atomically claim the endpoint, or notify the existing owner."""

        if self._closed:
            return False
        if self._is_primary:
            return True
        if self._lock.tryLock(0):
            if self._listen() or (
                QLocalServer.removeServer(self._server_name) and self._listen()
            ):
                self._is_primary = True
                LOGGER.info("DropSort single-instance primary acquired")
                return True
            self._lock.unlock()
            LOGGER.warning("Could not listen on DropSort single-instance endpoint")
            return False
        if self._is_primary:
            return True
        if self._send_activation():
            LOGGER.info("DropSort secondary instance sent activation")
            return False

        # QLockFile checks the owner PID and removes a stale lock safely.
        # Retry exactly once after that check; a healthy owner remains locked.
        if not self._lock.removeStaleLockFile():
            LOGGER.warning("Could not acquire DropSort single-instance endpoint")
            return False
        if not self._lock.tryLock(0):
            return False
        if self._listen() or (
            QLocalServer.removeServer(self._server_name) and self._listen()
        ):
            self._is_primary = True
            LOGGER.info("DropSort stale single-instance endpoint recovered")
            return True
        self._lock.unlock()
        if self._send_activation():
            LOGGER.info("DropSort secondary instance sent activation after retry")
        return False

    def close(self) -> None:
        if self._closed:
            return
        self._closed = True
        if self._is_primary:
            self._server.close()
            for socket in tuple(self._sockets):
                socket.abort()
                socket.deleteLater()
            self._sockets.clear()
            QLocalServer.removeServer(self._server_name)
            self._lock.unlock()
            self._is_primary = False

    def _listen(self) -> bool:
        try:
            return bool(self._server.listen(self._server_name))
        except Exception:
            LOGGER.warning("DropSort single-instance listen failed", exc_info=True)
            return False

    def _send_activation(self) -> bool:
        socket = QLocalSocket()
        try:
            socket.connectToServer(self._server_name)
            deadline = time.monotonic() + (_IPC_TIMEOUT_MS / 1000)
            while (
                socket.state() != QLocalSocket.LocalSocketState.ConnectedState
                and time.monotonic() < deadline
            ):
                QCoreApplication.processEvents(
                    QEventLoop.ProcessEventsFlag.AllEvents,
                    25,
                )
            if socket.state() != QLocalSocket.LocalSocketState.ConnectedState:
                socket.abort()
                return False
            if socket.write(ACTIVATE_MESSAGE) != len(ACTIVATE_MESSAGE):
                socket.abort()
                return False
            socket.flush()
            while socket.bytesToWrite() and time.monotonic() < deadline:
                QCoreApplication.processEvents(
                    QEventLoop.ProcessEventsFlag.AllEvents,
                    25,
                )
            if socket.bytesToWrite():
                socket.abort()
                return False
            socket.disconnectFromServer()
            socket.abort()
            return True
        except Exception:
            LOGGER.warning("DropSort activation request failed", exc_info=True)
            socket.abort()
            return False

    def _accept_connections(self) -> None:
        while self._server.hasPendingConnections():
            socket = self._server.nextPendingConnection()
            if socket is None:
                return
            self._sockets.add(socket)
            socket.readyRead.connect(
                lambda socket=socket: self._read_message(socket)
            )
            socket.disconnected.connect(
                lambda socket=socket: self._socket_disconnected(socket)
            )
            if socket.bytesAvailable():
                self._read_message(socket)

    def _socket_disconnected(self, socket: QLocalSocket) -> None:
        self._sockets.discard(socket)
        socket.deleteLater()

    def _read_message(self, socket: QLocalSocket) -> None:
        payload = bytes(socket.read(_MAX_MESSAGE_BYTES + 1))
        if len(payload) > _MAX_MESSAGE_BYTES:
            socket.disconnectFromServer()
            return
        for message in payload.splitlines():
            if message == b"ACTIVATE":
                self.activation_requested.emit()
        socket.disconnectFromServer()
