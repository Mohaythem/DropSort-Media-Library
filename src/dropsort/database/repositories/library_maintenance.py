from __future__ import annotations

import sqlite3

from dropsort.database.connection.sqlite import Database
from dropsort.library.movies import (
    CatalogClearCounts,
    CatalogMaintenanceBlockedError,
    CatalogMaintenanceError,
)


_TERMINAL_OPERATION_STATES = ("COMMITTED", "FAILED")


class SqliteLibraryMaintenanceRepository:
    """Atomic catalog reset that preserves immutable operation history."""

    def __init__(self, database: Database) -> None:
        self._database = database

    def clear_catalog(self) -> CatalogClearCounts:
        try:
            with self._database.transaction() as connection:
                unresolved = connection.execute(
                    """
                    SELECT COUNT(*)
                      FROM file_operations
                     WHERE state NOT IN (?, ?)
                    """,
                    _TERMINAL_OPERATION_STATES,
                ).fetchone()[0]
                if unresolved:
                    raise CatalogMaintenanceBlockedError(
                        "an unresolved file operation must be completed before clearing the library"
                    )
                counts = CatalogClearCounts(
                    movies=_count_movies_without_personal_state(connection),
                    media_files=_count(connection, "media_files"),
                    metadata_entries=_count(connection, "metadata_cache"),
                )
                connection.execute("DELETE FROM metadata_cache")
                connection.execute("DELETE FROM media_files")
                connection.execute(
                    """
                    DELETE FROM movies
                     WHERE NOT EXISTS (
                               SELECT 1
                                 FROM watch_events AS we
                                WHERE we.movie_id = movies.id
                           )
                       AND NOT EXISTS (
                               SELECT 1
                                 FROM movie_personal_state AS ps
                                WHERE ps.movie_id = movies.id
                                  AND (
                                      ps.preference <> 'NO_OPINION'
                                      OR ps.watchlist_added_at IS NOT NULL
                                  )
                           )
                    """
                )
                if connection.execute("PRAGMA foreign_key_check").fetchone() is not None:
                    raise CatalogMaintenanceError("catalog clear violated a foreign key")
                return counts
        except CatalogMaintenanceBlockedError:
            raise
        except CatalogMaintenanceError:
            raise
        except sqlite3.Error as error:
            raise CatalogMaintenanceError(
                "the local catalog could not be cleared atomically"
            ) from error


def _count(connection: sqlite3.Connection, table: str) -> int:
    return int(connection.execute(f"SELECT COUNT(*) FROM {table}").fetchone()[0])


def _count_movies_without_personal_state(connection: sqlite3.Connection) -> int:
    return int(
        connection.execute(
            """
            SELECT COUNT(*)
              FROM movies
             WHERE NOT EXISTS (
                       SELECT 1 FROM watch_events AS we WHERE we.movie_id = movies.id
                   )
               AND NOT EXISTS (
                       SELECT 1
                         FROM movie_personal_state AS ps
                        WHERE ps.movie_id = movies.id
                          AND (
                              ps.preference <> 'NO_OPINION'
                              OR ps.watchlist_added_at IS NOT NULL
                          )
                   )
            """
        ).fetchone()[0]
    )
