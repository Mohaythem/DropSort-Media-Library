from __future__ import annotations

from datetime import datetime, timezone
from pathlib import Path
import sqlite3

from dropsort.core.operations.errors import InvalidOperationStateError, OperationNotFoundError
from dropsort.core.operations.models import (
    FileOperationPlan,
    FileOperationRecord,
    OperationState,
    OperationType,
    OperationUpdate,
)
from dropsort.database.connection.sqlite import Database


_ALLOWED_TRANSITIONS: dict[OperationState, frozenset[OperationState]] = {
    OperationState.PLANNED: frozenset({OperationState.VALIDATED, OperationState.FAILED}),
    OperationState.VALIDATED: frozenset({OperationState.EXECUTING, OperationState.FAILED}),
    OperationState.EXECUTING: frozenset(
        {OperationState.FS_VERIFIED, OperationState.FAILED, OperationState.RECOVERY_REQUIRED}
    ),
    OperationState.FS_VERIFIED: frozenset(
        {OperationState.COMMITTED, OperationState.RECOVERY_REQUIRED}
    ),
    OperationState.RECOVERY_REQUIRED: frozenset(
        {OperationState.FS_VERIFIED, OperationState.FAILED}
    ),
    OperationState.COMMITTED: frozenset(),
    OperationState.FAILED: frozenset(),
}


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat()


def _encode_fs_id(value: int | None) -> str | None:
    return None if value is None else str(value)


def _decode_fs_id(value: object | None) -> int | None:
    return None if value is None else int(value)


class FileOperationRepository:
    def __init__(self, database: Database) -> None:
        self.database = database

    def create(
        self,
        plan: FileOperationPlan,
        *,
        conn: sqlite3.Connection | None = None,
    ) -> FileOperationRecord:
        now = utc_now()
        values = (
            plan.operation_id,
            plan.operation_type.value,
            str(plan.source),
            str(plan.destination),
            OperationState.PLANNED.value,
            plan.media_file_id,
            plan.reverses_operation_id,
            now,
            now,
        )
        sql = """
            INSERT INTO file_operations(
                id, operation_type, source_path, destination_path, state,
                media_file_id, reverses_operation_id, created_at, updated_at
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
        """
        if conn is not None:
            self._assert_reverse_available(plan, conn)
            conn.execute(sql, values)
        else:
            with self.database.transaction() as tx:
                self._assert_reverse_available(plan, tx)
                tx.execute(sql, values)
        return self.get(plan.operation_id, conn=conn)

    @staticmethod
    def _assert_reverse_available(
        plan: FileOperationPlan,
        conn: sqlite3.Connection,
    ) -> None:
        if plan.reverses_operation_id is None:
            return
        existing = conn.execute(
            "SELECT id FROM file_operations WHERE reverses_operation_id = ? LIMIT 1",
            (plan.reverses_operation_id,),
        ).fetchone()
        if existing is not None:
            raise InvalidOperationStateError(
                f"Operation {plan.reverses_operation_id} already has reverse journal {existing['id']}"
            )

    def get(self, operation_id: str, *, conn: sqlite3.Connection | None = None) -> FileOperationRecord:
        sql = "SELECT * FROM file_operations WHERE id = ?"
        if conn is not None:
            row = conn.execute(sql, (operation_id,)).fetchone()
        else:
            with self.database.connection() as db_conn:
                row = db_conn.execute(sql, (operation_id,)).fetchone()
        if row is None:
            raise OperationNotFoundError(operation_id)
        return self._to_record(row)

    def list_nonterminal(self) -> list[FileOperationRecord]:
        terminal = (OperationState.COMMITTED.value, OperationState.FAILED.value)
        with self.database.connection() as conn:
            rows = conn.execute(
                "SELECT * FROM file_operations WHERE state NOT IN (?, ?) ORDER BY created_at",
                terminal,
            ).fetchall()
        return [self._to_record(row) for row in rows]

    def transition(
        self,
        operation_id: str,
        new_state: OperationState,
        update: OperationUpdate | None = None,
        *,
        conn: sqlite3.Connection | None = None,
    ) -> FileOperationRecord:
        current = self.get(operation_id, conn=conn)
        if new_state not in _ALLOWED_TRANSITIONS[current.state]:
            raise InvalidOperationStateError(f"{current.state.value} -> {new_state.value}")
        values = self._transition_values(current, new_state, update or OperationUpdate())
        sql = """
            UPDATE file_operations
               SET state = ?, source_size = ?, source_mtime_ns = ?, source_dev = ?, source_ino = ?,
                   destination_size = ?, destination_mtime_ns = ?, destination_dev = ?, destination_ino = ?,
                   destination_sha256 = ?, strategy = ?, error_code = ?, error_message = ?, updated_at = ?
             WHERE id = ? AND state = ?
        """
        if conn is not None:
            cursor = conn.execute(sql, values)
        else:
            with self.database.transaction() as tx:
                cursor = tx.execute(sql, values)
        if cursor.rowcount != 1:
            raise InvalidOperationStateError(f"Concurrent state change for {operation_id}")
        return self.get(operation_id, conn=conn)

    @staticmethod
    def _transition_values(
        current: FileOperationRecord,
        new_state: OperationState,
        update: OperationUpdate,
    ) -> tuple[object, ...]:
        def choose(new: object | None, old: object | None) -> object | None:
            return old if new is None else new

        return (
            new_state.value,
            choose(update.source_size, current.source_size),
            choose(update.source_mtime_ns, current.source_mtime_ns),
            _encode_fs_id(choose(update.source_dev, current.source_dev)),
            _encode_fs_id(choose(update.source_ino, current.source_ino)),
            choose(update.destination_size, current.destination_size),
            choose(update.destination_mtime_ns, current.destination_mtime_ns),
            _encode_fs_id(choose(update.destination_dev, current.destination_dev)),
            _encode_fs_id(choose(update.destination_ino, current.destination_ino)),
            choose(update.destination_sha256, current.destination_sha256),
            choose(update.strategy, current.strategy),
            update.error_code,
            update.error_message,
            utc_now(),
            current.id,
            current.state.value,
        )

    @staticmethod
    def _to_record(row: sqlite3.Row) -> FileOperationRecord:
        return file_operation_from_row(row)


def file_operation_from_row(row: sqlite3.Row) -> FileOperationRecord:
    return FileOperationRecord(
        id=row["id"],
        operation_type=OperationType(row["operation_type"]),
        source=Path(row["source_path"]),
        destination=Path(row["destination_path"]),
        state=OperationState(row["state"]),
        media_file_id=row["media_file_id"],
        reverses_operation_id=row["reverses_operation_id"],
        source_size=row["source_size"],
        source_mtime_ns=row["source_mtime_ns"],
        source_dev=_decode_fs_id(row["source_dev"]),
        source_ino=_decode_fs_id(row["source_ino"]),
        destination_size=row["destination_size"],
        destination_mtime_ns=row["destination_mtime_ns"],
        destination_dev=_decode_fs_id(row["destination_dev"]),
        destination_ino=_decode_fs_id(row["destination_ino"]),
        destination_sha256=row["destination_sha256"],
        strategy=row["strategy"],
        error_code=row["error_code"],
        error_message=row["error_message"],
        created_at=row["created_at"],
        updated_at=row["updated_at"],
    )
