from __future__ import annotations

from contextlib import contextmanager
import sqlite3
from typing import Iterator

from dropsort.database.connection.sqlite import Database
from dropsort.database.repositories.media_files import media_file_from_row
from dropsort.database.repositories.movies import movie_from_row
from dropsort.library.movies import (
    CatalogQueryError,
    MovieDetailsSnapshot,
    MovieSummary,
)


_SUMMARY_COLUMNS_SQL = """
    SELECT m.*, COUNT(mf.id) AS media_file_count,
           COALESCE(SUM(CASE WHEN mf.status = 'MISSING' THEN 1 ELSE 0 END), 0)
               AS missing_file_count
      FROM movies AS m
      LEFT JOIN media_files AS mf ON mf.movie_id = m.id
"""

_SUMMARY_SQL = _SUMMARY_COLUMNS_SQL + """
     GROUP BY m.id
     ORDER BY julianday(m.date_added) DESC, m.id DESC
     LIMIT ? OFFSET ?
"""

_MOVIE_SUMMARY_SQL = _SUMMARY_COLUMNS_SQL + """
     WHERE m.id = ?
     GROUP BY m.id
"""


class SqliteMovieLibraryReadRepository:
    """SQLite read adapter for local-library presentation queries."""

    def __init__(
        self,
        database: Database,
        *,
        connection: sqlite3.Connection | None = None,
    ) -> None:
        self.database = database
        self._connection = connection

    def list_movies(self, *, limit: int, offset: int) -> tuple[MovieSummary, ...]:
        _validate_limit(limit)
        if isinstance(offset, bool) or not isinstance(offset, int) or offset < 0:
            raise ValueError("offset must be a non-negative integer")
        try:
            rows = self._fetchall(_SUMMARY_SQL, (limit, offset))
            return tuple(
                MovieSummary(
                    movie=movie_from_row(row),
                    media_file_count=row["media_file_count"],
                    missing_file_count=row["missing_file_count"],
                )
                for row in rows
            )
        except sqlite3.Error as error:
            raise CatalogQueryError("could not read movie library summaries") from error

    def get_movie_summary(self, movie_id: int) -> MovieSummary | None:
        if isinstance(movie_id, bool) or not isinstance(movie_id, int) or movie_id <= 0:
            raise ValueError("movie_id must be a positive integer")
        try:
            row = self._fetchone(_MOVIE_SUMMARY_SQL, (movie_id,))
            if row is None:
                return None
            return MovieSummary(
                movie=movie_from_row(row),
                media_file_count=row["media_file_count"],
                missing_file_count=row["missing_file_count"],
            )
        except sqlite3.Error as error:
            raise CatalogQueryError("could not read movie library summary") from error

    def get_movie_details(self, movie_id: int) -> MovieDetailsSnapshot | None:
        if isinstance(movie_id, bool) or not isinstance(movie_id, int) or movie_id <= 0:
            raise ValueError("movie_id must be a positive integer")
        try:
            with self._snapshot_connection() as connection:
                movie_row = connection.execute(
                    "SELECT * FROM movies WHERE id = ?",
                    (movie_id,),
                ).fetchone()
                if movie_row is None:
                    return None
                media_rows = connection.execute(
                    "SELECT * FROM media_files WHERE movie_id = ? ORDER BY id",
                    (movie_id,),
                ).fetchall()
            return MovieDetailsSnapshot(
                movie=movie_from_row(movie_row),
                media_files=tuple(media_file_from_row(row) for row in media_rows),
            )
        except sqlite3.Error as error:
            raise CatalogQueryError("could not read movie library details") from error

    def _fetchall(
        self,
        sql: str,
        values: tuple[object, ...],
    ) -> list[sqlite3.Row]:
        if self._connection is not None:
            return self._connection.execute(sql, values).fetchall()
        with self.database.connection() as connection:
            return connection.execute(sql, values).fetchall()

    def _fetchone(
        self,
        sql: str,
        values: tuple[object, ...],
    ) -> sqlite3.Row | None:
        if self._connection is not None:
            return self._connection.execute(sql, values).fetchone()
        with self.database.connection() as connection:
            return connection.execute(sql, values).fetchone()

    @contextmanager
    def _snapshot_connection(self) -> Iterator[sqlite3.Connection]:
        if self._connection is not None:
            yield self._connection
            return
        with self.database.connection() as connection:
            connection.execute("BEGIN")
            try:
                yield connection
            finally:
                connection.rollback()


def _validate_limit(value: int) -> None:
    if isinstance(value, bool) or not isinstance(value, int) or value <= 0:
        raise ValueError("limit must be a positive integer")
