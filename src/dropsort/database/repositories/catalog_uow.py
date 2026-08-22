from __future__ import annotations

from contextlib import AbstractContextManager
import sqlite3
from types import TracebackType

from dropsort.database.connection.sqlite import Database
from dropsort.database.repositories.media_files import MediaFileRepository
from dropsort.database.repositories.movies import SqliteMovieRepository


class SqliteCatalogUnitOfWork:
    """Bind catalog repositories to one existing SQLite transaction boundary."""

    def __init__(self, database: Database) -> None:
        self.database = database
        self._transaction: AbstractContextManager[sqlite3.Connection] | None = None

    def __enter__(self) -> SqliteCatalogUnitOfWork:
        if self._transaction is not None:
            raise RuntimeError("catalog unit of work is already active")
        self._transaction = self.database.transaction()
        connection = self._transaction.__enter__()
        self.movies = SqliteMovieRepository(self.database, connection=connection)
        self.media_files = MediaFileRepository(self.database, connection=connection)
        return self

    def __exit__(
        self,
        exc_type: type[BaseException] | None,
        exc_value: BaseException | None,
        traceback: TracebackType | None,
    ) -> bool | None:
        if self._transaction is None:
            raise RuntimeError("catalog unit of work is not active")
        transaction = self._transaction
        self._transaction = None
        return transaction.__exit__(exc_type, exc_value, traceback)

