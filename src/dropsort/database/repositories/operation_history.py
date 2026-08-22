from __future__ import annotations

from pathlib import Path
import sqlite3

from dropsort.core.operations import OperationState
from dropsort.database.connection.sqlite import Database
from dropsort.database.repositories.file_operations import file_operation_from_row
from dropsort.library.operations import OperationJournalQueryError, OperationJournalSnapshot


_SNAPSHOT_SELECT = """
    SELECT fo.*, m.title AS movie_title, mf.current_path AS current_catalog_path,
           (
               SELECT reverse.id
                FROM file_operations AS reverse
                WHERE reverse.reverses_operation_id = fo.id
                ORDER BY julianday(reverse.created_at) DESC,
                         reverse.created_at DESC,
                         reverse.rowid DESC
                LIMIT 1
           ) AS reversed_by_operation_id
      FROM file_operations AS fo
      LEFT JOIN media_files AS mf ON mf.id = fo.media_file_id
      LEFT JOIN movies AS m ON m.id = mf.movie_id
"""


class SqliteOperationJournalReadRepository:
    """Read-only SQLite adapter for operation history and dynamic undo checks."""

    def __init__(
        self,
        database: Database,
        *,
        connection: sqlite3.Connection | None = None,
    ) -> None:
        self.database = database
        self._connection = connection

    def list_operations(
        self,
        *,
        limit: int,
        offset: int,
    ) -> tuple[OperationJournalSnapshot, ...]:
        _validate_limit(limit)
        _validate_offset(offset)
        sql = (
            _SNAPSHOT_SELECT
            + """
             ORDER BY julianday(fo.created_at) DESC,
                      fo.created_at DESC,
                      fo.rowid DESC
             LIMIT ? OFFSET ?
            """
        )
        try:
            return tuple(self._to_snapshot(row) for row in self._fetchall(sql, (limit, offset)))
        except sqlite3.Error as error:
            raise OperationJournalQueryError("could not read operation history") from error

    def get_operation(self, operation_id: str) -> OperationJournalSnapshot | None:
        _validate_operation_id(operation_id)
        try:
            row = self._fetchone(_SNAPSHOT_SELECT + " WHERE fo.id = ?", (operation_id,))
            return None if row is None else self._to_snapshot(row)
        except sqlite3.Error as error:
            raise OperationJournalQueryError("could not read operation details") from error

    def latest_relevant_for_media_file(
        self,
        media_file_id: int,
    ) -> OperationJournalSnapshot | None:
        if isinstance(media_file_id, bool) or not isinstance(media_file_id, int) or media_file_id <= 0:
            raise ValueError("media_file_id must be a positive integer")
        sql = (
            _SNAPSHOT_SELECT
            + """
             WHERE fo.media_file_id = ? AND fo.state <> ?
             ORDER BY julianday(fo.created_at) DESC,
                      fo.created_at DESC,
                      fo.rowid DESC
             LIMIT 1
            """
        )
        try:
            row = self._fetchone(sql, (media_file_id, OperationState.FAILED.value))
            return None if row is None else self._to_snapshot(row)
        except sqlite3.Error as error:
            raise OperationJournalQueryError("could not read current operation state") from error

    def _fetchone(self, sql: str, values: tuple[object, ...]) -> sqlite3.Row | None:
        if self._connection is not None:
            return self._connection.execute(sql, values).fetchone()
        with self.database.connection() as connection:
            return connection.execute(sql, values).fetchone()

    def _fetchall(self, sql: str, values: tuple[object, ...]) -> list[sqlite3.Row]:
        if self._connection is not None:
            return self._connection.execute(sql, values).fetchall()
        with self.database.connection() as connection:
            return connection.execute(sql, values).fetchall()

    @staticmethod
    def _to_snapshot(row: sqlite3.Row) -> OperationJournalSnapshot:
        current_path = row["current_catalog_path"]
        return OperationJournalSnapshot(
            record=file_operation_from_row(row),
            movie_title=row["movie_title"],
            current_catalog_path=None if current_path is None else Path(current_path),
            reversed_by_operation_id=row["reversed_by_operation_id"],
        )


def _validate_limit(value: int) -> None:
    if isinstance(value, bool) or not isinstance(value, int) or value <= 0:
        raise ValueError("limit must be a positive integer")


def _validate_offset(value: int) -> None:
    if isinstance(value, bool) or not isinstance(value, int) or value < 0:
        raise ValueError("offset must be a non-negative integer")


def _validate_operation_id(value: str) -> None:
    if not isinstance(value, str) or not value.strip():
        raise ValueError("operation_id must be non-empty text")
