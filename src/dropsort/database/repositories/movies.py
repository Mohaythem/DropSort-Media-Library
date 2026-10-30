from __future__ import annotations

from datetime import datetime
import json
import sqlite3

from dropsort.database.connection.sqlite import Database
from dropsort.library.movies import (
    CatalogDataError,
    CatalogIntegrityError,
    CatalogRecordNotFoundError,
    MetadataStatus,
    Movie,
    MovieCatalogData,
    MovieIdentityConflictError,
)


class SqliteMovieRepository:
    def __init__(
        self,
        database: Database,
        *,
        connection: sqlite3.Connection | None = None,
    ) -> None:
        self.database = database
        self._connection = connection

    def get_by_id(self, movie_id: int) -> Movie | None:
        row = self._fetchone("SELECT * FROM movies WHERE id = ?", (movie_id,))
        return None if row is None else movie_from_row(row)

    def get_by_external_id(self, provider: str, external_id: str) -> Movie | None:
        provider = _required_text(provider, "provider")
        external_id = _required_text(external_id, "external_id")
        row = self._fetchone(
            "SELECT * FROM movies WHERE provider = ? AND external_id = ?",
            (provider, external_id),
        )
        return None if row is None else movie_from_row(row)

    def create(self, data: MovieCatalogData, *, now: datetime) -> Movie:
        _require_aware(now)
        sql = """
            INSERT INTO movies(
                provider, external_id, title, original_title, year, overview, genres,
                runtime_minutes, rating, poster_path, metadata_status,
                date_added, created_at, updated_at
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
        """
        values = (*_metadata_values(data), now.isoformat(), now.isoformat(), now.isoformat())
        try:
            movie_id = self._insert(sql, values)
        except sqlite3.IntegrityError as error:
            if (
                data.provider is not None
                and data.external_id is not None
                and self.get_by_external_id(data.provider, data.external_id) is not None
            ):
                raise MovieIdentityConflictError(
                    f"movie identity already exists: {data.provider}:{data.external_id}"
                ) from error
            raise CatalogIntegrityError("movie catalog constraint failed") from error
        movie = self.get_by_id(movie_id)
        if movie is None:
            raise CatalogRecordNotFoundError(movie_id)
        return movie

    def update_metadata(
        self,
        movie_id: int,
        data: MovieCatalogData,
        *,
        now: datetime,
    ) -> Movie:
        _require_aware(now)
        current = self.get_by_id(movie_id)
        if current is None:
            raise CatalogRecordNotFoundError(movie_id)
        if (current.provider, current.external_id) != (data.provider, data.external_id):
            raise MovieIdentityConflictError("metadata refresh cannot change movie identity")
        sql = """
            UPDATE movies
               SET title = ?, original_title = ?, year = ?, overview = ?, genres = ?,
                   runtime_minutes = ?, rating = ?, poster_path = ?, metadata_status = ?,
                   updated_at = ?
             WHERE id = ?
        """
        self._execute(
            sql,
            (*_descriptive_values(data), now.isoformat(), movie_id),
        )
        return self._require(movie_id)

    def attach_external_metadata(
        self,
        movie_id: int,
        data: MovieCatalogData,
        *,
        now: datetime,
    ) -> Movie:
        _require_aware(now)
        if data.provider is None or data.external_id is None:
            raise ValueError("external metadata must contain an identity")
        if data.metadata_status is not MetadataStatus.READY:
            raise ValueError("attached external metadata must be READY")
        current = self._require(movie_id)
        if current.provider is not None and (
            current.provider,
            current.external_id,
        ) != (data.provider, data.external_id):
            raise MovieIdentityConflictError(
                "external identity replacement requires explicit conflict resolution"
            )
        try:
            self._execute(
                """
                UPDATE movies
                   SET provider = ?, external_id = ?, title = ?, original_title = ?,
                       year = ?, overview = ?, genres = ?, runtime_minutes = ?,
                       rating = ?, poster_path = ?, metadata_status = ?, updated_at = ?
                 WHERE id = ?
                """,
                (*_metadata_values(data), now.isoformat(), movie_id),
            )
        except sqlite3.IntegrityError as error:
            raise MovieIdentityConflictError(
                f"movie identity already exists: {data.provider}:{data.external_id}"
            ) from error
        return self._require(movie_id)

    def update_metadata_status(
        self,
        movie_id: int,
        status: MetadataStatus,
        *,
        now: datetime,
    ) -> Movie:
        _require_aware(now)
        if not isinstance(status, MetadataStatus):
            raise ValueError("status must be MetadataStatus")
        current = self._require(movie_id)
        if status is MetadataStatus.READY and current.provider is None:
            raise CatalogIntegrityError("READY metadata requires an external identity")
        self._execute(
            "UPDATE movies SET metadata_status = ?, updated_at = ? WHERE id = ?",
            (status.value, now.isoformat(), movie_id),
        )
        return self._require(movie_id)

    def list_all(self) -> tuple[Movie, ...]:
        rows = self._fetchall("SELECT * FROM movies ORDER BY date_added, id", ())
        return tuple(movie_from_row(row) for row in rows)

    def count_all(self) -> int:
        row = self._fetchone("SELECT COUNT(*) AS count FROM movies", ())
        return 0 if row is None else int(row["count"])

    def list_page(self, *, after_id: int, limit: int) -> tuple[Movie, ...]:
        if isinstance(after_id, bool) or not isinstance(after_id, int) or after_id < 0:
            raise ValueError("after_id must be a non-negative integer")
        if isinstance(limit, bool) or not isinstance(limit, int) or limit <= 0:
            raise ValueError("limit must be a positive integer")
        rows = self._fetchall(
            "SELECT * FROM movies WHERE id > ? ORDER BY id LIMIT ?",
            (after_id, limit),
        )
        return tuple(movie_from_row(row) for row in rows)

    def _require(self, movie_id: int) -> Movie:
        movie = self.get_by_id(movie_id)
        if movie is None:
            raise CatalogRecordNotFoundError(movie_id)
        return movie


    def _fetchone(self, sql: str, values: tuple[object, ...]) -> sqlite3.Row | None:
        if self._connection is not None:
            return self._connection.execute(sql, values).fetchone()
        with self.database.connection() as conn:
            return conn.execute(sql, values).fetchone()

    def _fetchall(self, sql: str, values: tuple[object, ...]) -> list[sqlite3.Row]:
        if self._connection is not None:
            return self._connection.execute(sql, values).fetchall()
        with self.database.connection() as conn:
            return conn.execute(sql, values).fetchall()

    def _insert(self, sql: str, values: tuple[object, ...]) -> int:
        if self._connection is not None:
            cursor = self._connection.execute(sql, values)
            return int(cursor.lastrowid)
        with self.database.transaction() as conn:
            cursor = conn.execute(sql, values)
            return int(cursor.lastrowid)

    def _execute(self, sql: str, values: tuple[object, ...]) -> None:
        if self._connection is not None:
            cursor = self._connection.execute(sql, values)
        else:
            with self.database.transaction() as conn:
                cursor = conn.execute(sql, values)
        if cursor.rowcount != 1:
            raise CatalogRecordNotFoundError(values[-1])

def _metadata_values(data: MovieCatalogData) -> tuple[object, ...]:
    return (
        data.provider,
        data.external_id,
        *_descriptive_values(data),
    )


def _descriptive_values(data: MovieCatalogData) -> tuple[object, ...]:
    return (
        data.title,
        data.original_title,
        data.year,
        data.overview,
        json.dumps(list(data.genres), ensure_ascii=False, separators=(",", ":")),
        data.runtime_minutes,
        data.rating,
        data.poster_reference,
        data.metadata_status.value,
    )


def movie_from_row(row: sqlite3.Row) -> Movie:
    """Normalize one SQLite movie row for database-layer repository reuse."""
    try:
        decoded_genres = json.loads(row["genres"])
        if not isinstance(decoded_genres, list) or any(
            not isinstance(genre, str) for genre in decoded_genres
        ):
            raise ValueError("genres must be a string list")
        data = MovieCatalogData(
            provider=row["provider"],
            external_id=row["external_id"],
            title=row["title"],
            original_title=row["original_title"],
            year=row["year"],
            overview=row["overview"],
            genres=tuple(decoded_genres),
            runtime_minutes=row["runtime_minutes"],
            rating=row["rating"],
            poster_reference=row["poster_path"],
            metadata_status=MetadataStatus(row["metadata_status"]),
        )
        return Movie(
            id=row["id"],
            data=data,
            date_added=_decode_datetime(row["date_added"]),
            created_at=_decode_datetime(row["created_at"]),
            updated_at=_decode_datetime(row["updated_at"]),
        )
    except (json.JSONDecodeError, TypeError, ValueError) as error:
        raise CatalogDataError(
            f"invalid movie catalog data or genres for row {row['id']}"
        ) from error


def _decode_datetime(value: object) -> datetime:
    if not isinstance(value, str):
        raise ValueError("timestamp must be text")
    decoded = datetime.fromisoformat(value)
    _require_aware(decoded)
    return decoded


def _require_aware(value: datetime) -> None:
    if not isinstance(value, datetime) or value.tzinfo is None:
        raise ValueError("timestamp must be a timezone-aware datetime")


def _required_text(value: str, field_name: str) -> str:
    if not isinstance(value, str) or not value.strip():
        raise ValueError(f"{field_name} must be a non-empty string")
    return value.strip()
