from __future__ import annotations

from uuid import uuid4
from pathlib import Path

import pytest
from PySide6.QtCore import QCoreApplication
from PySide6.QtNetwork import QLocalServer

from dropsort.application.runtime import single_instance
from dropsort.application.runtime.single_instance import (
    SingleInstanceCoordinator,
    default_lock_path,
    default_server_name,
)


def _server_name() -> str:
    return f"DropSort.Test.{uuid4().hex}"


def test_first_instance_acquires_primary_ownership(qapp, tmp_path) -> None:
    coordinator = SingleInstanceCoordinator(
        _server_name(), lock_path=tmp_path / "single.lock"
    )

    assert coordinator.acquire() is True
    assert coordinator.is_primary is True

    coordinator.close()


def test_default_server_identity_is_sanitized_and_lock_path_is_stable(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    monkeypatch.setenv("USERNAME", "drop-sort user")
    monkeypatch.setenv("SESSIONNAME", "Console/1")
    name = default_server_name()

    assert name == "DropSort.SingleInstance.drop_sort_user.Console_1"
    first = default_lock_path(name)
    second = default_lock_path(name)
    assert first == second
    assert first.name.startswith("single-instance-")

    monkeypatch.setattr(
        "dropsort.application.runtime.single_instance.QStandardPaths.writableLocation",
        lambda _location: "",
    )
    fallback = default_lock_path(name)
    assert fallback == Path.home() / "AppData" / "Local" / "Temp" / "DropSort" / first.name


def test_invalid_and_closed_coordinators_are_controlled(qapp, tmp_path) -> None:
    with pytest.raises(ValueError):
        SingleInstanceCoordinator(" ", lock_path=tmp_path / "invalid.lock")

    coordinator = SingleInstanceCoordinator(
        _server_name(), lock_path=tmp_path / "closed.lock"
    )
    coordinator.close()
    coordinator.close()
    assert coordinator.acquire() is False


def test_listen_exception_is_translated_to_failed_acquisition(qapp, tmp_path) -> None:
    coordinator = SingleInstanceCoordinator(
        _server_name(), lock_path=tmp_path / "listen-error.lock"
    )
    coordinator._server.listen = lambda _name: (_ for _ in ()).throw(  # type: ignore[method-assign]
        RuntimeError("listen failure")
    )

    assert coordinator.acquire() is False
    coordinator.close()


def test_stale_lock_retry_can_become_primary(qapp, tmp_path, monkeypatch) -> None:
    coordinator = SingleInstanceCoordinator(
        _server_name(), lock_path=tmp_path / "stale.lock"
    )

    class FakeLock:
        def __init__(self) -> None:
            self.attempts = 0

        def tryLock(self, _timeout: int) -> bool:
            self.attempts += 1
            return self.attempts == 2

        def removeStaleLockFile(self) -> bool:
            return True

        def unlock(self) -> None:
            return None

    coordinator._lock = FakeLock()  # type: ignore[assignment]
    monkeypatch.setattr(coordinator, "_send_activation", lambda: False)
    monkeypatch.setattr(coordinator, "_listen", lambda: True)

    assert coordinator.acquire() is True
    coordinator.close()


def test_overlong_protocol_message_is_ignored(qapp, tmp_path) -> None:
    coordinator = SingleInstanceCoordinator(
        _server_name(), lock_path=tmp_path / "payload.lock"
    )
    activations: list[str] = []
    coordinator.activation_requested.connect(lambda: activations.append("ACTIVATE"))

    class LongSocket:
        def read(self, _maximum: int) -> bytes:
            return b"x" * 65

        def disconnectFromServer(self) -> None:
            return None

    coordinator._read_message(LongSocket())  # type: ignore[arg-type]

    assert activations == []
    coordinator.close()


def test_second_instance_sends_activate_and_exits_as_secondary(qapp, tmp_path) -> None:
    name = _server_name()
    lock_path = tmp_path / "single.lock"
    primary = SingleInstanceCoordinator(name, lock_path=lock_path)
    secondary = SingleInstanceCoordinator(name, lock_path=lock_path)
    activations: list[str] = []
    primary.activation_requested.connect(lambda: activations.append("ACTIVATE"))

    assert primary.acquire() is True
    assert secondary.acquire() is False
    QCoreApplication.processEvents()

    assert activations == ["ACTIVATE"]
    assert secondary.is_primary is False

    secondary.close()
    primary.close()


def test_unknown_message_is_ignored_without_activation(qapp, tmp_path) -> None:
    primary = SingleInstanceCoordinator(
        _server_name(), lock_path=tmp_path / "single.lock"
    )
    activations: list[str] = []
    primary.activation_requested.connect(lambda: activations.append("ACTIVATE"))
    class UnknownSocket:
        def read(self, _maximum: int) -> bytes:
            return b"UNKNOWN\n"

        def disconnectFromServer(self) -> None:
            return None

    primary._read_message(UnknownSocket())  # type: ignore[arg-type]

    assert activations == []
    primary.close()


def test_primary_cleanup_allows_immediate_relaunch(qapp, tmp_path) -> None:
    name = _server_name()
    lock_path = tmp_path / "single.lock"
    first = SingleInstanceCoordinator(name, lock_path=lock_path)
    assert first.acquire() is True
    first.close()

    second = SingleInstanceCoordinator(name, lock_path=lock_path)
    assert second.acquire() is True
    second.close()


def test_stale_local_server_is_recovered_once(qapp, tmp_path) -> None:
    name = _server_name()
    stale = QLocalServer()
    assert stale.listen(name)
    stale.close()

    coordinator = SingleInstanceCoordinator(name, lock_path=tmp_path / "single.lock")
    assert coordinator.acquire() is True
    coordinator.close()
