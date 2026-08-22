from __future__ import annotations

from datetime import datetime, timezone
from pathlib import Path
import os
import sqlite3

from dropsort.database.connection.sqlite import Database
from dropsort.library.movies import (
    CatalogDataError,
    CatalogIntegrityError,
    CatalogRecordNotFoundError,
    MediaFile,
    MediaFileAssociationConflict,
    MediaFilePathConflictError,
    MediaFileStatus,
    MediaFileStatusUpdate,
    VerifiedMediaFileFacts,
)


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat()


def path_key(path: Path) -> str:
    """Return the database identity key used for Windows-style path uniqueness."""
    return os.path.normpath(str(path.absolute())).casefold()


class MediaFileRepository:
    def __init__(
        self,
        database: Database,
        *,
        connection: sqlite3.Connection | None = None,
    ) -> None:
        self.database = database
        self._connection = connection

    def create(
        self,
        path: Path,
        file_size: int,
        *,
        conn: sqlite3.Connection | None = None,
    ) -> int:
        now = utc_now()
        sql = """
            INSERT INTO media_files(current_path, path_key, file_size, discovered_at, last_seen_at)
            VALUES (?, ?, ?, ?, ?)
        """
        values = (str(path.absolute()), path_key(path), file_size, now, now)
        active_connection = conn or self._connection
        if active_connection is not None:
            cursor = active_connection.execute(sql, values)
            return int(cursor.lastrowid)
        with self.database.transaction() as tx:
            cursor = tx.execute(sql, values)
            return int(cursor.lastrowid)

    def get_path(self, media_file_id: int, *, conn: sqlite3.Connection | None = None) -> Path:
        sql = "SELECT current_path FROM media_files WHERE id = ?"
        active_connection = conn or self._connection
        if active_connection is not None:
            row = active_connection.execute(sql, (media_file_id,)).fetchone()
        else:
            with self.database.connection() as db_conn:
                row = db_conn.execute(sql, (media_file_id,)).fetchone()
        if row is None:
            raise KeyError(media_file_id)
        return Path(row["current_path"])

    def update_path(
        self,
        media_file_id: int,
        path: Path,
        *,
        conn: sqlite3.Connection,
    ) -> None:
        cursor = conn.execute(
            "UPDATE media_files SET current_path = ?, path_key = ?, last_seen_at = ? WHERE id = ?",
            (str(path.absolute()), path_key(path), utc_now(), media_file_id),
        )
        if cursor.rowcount != 1:
            raise KeyError(media_file_id)

    def get_by_id(self, media_file_id: int) -> MediaFile | None:
        row = self._fetchone("SELECT * FROM media_files WHERE id = ?", (media_file_id,))
        return None if row is None else media_file_from_row(row)

    def get_by_path(self, path: Path) -> MediaFile | None:
        row = self._fetchone(
            "SELECT * FROM media_files WHERE path_key = ?",
            (path_key(path),),
        )
        return None if row is None else media_file_from_row(row)

    def add(self, facts: VerifiedMediaFileFacts, movie_id: int) -> MediaFile:
        sql = """
            INSERT INTO media_files(
                movie_id, current_path, path_key, file_size, extension, resolution, codec,
                source, status, discovered_at, last_seen_at
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
        """
        values = (
            movie_id,
            str(facts.current_path),
            path_key(facts.current_path),
            facts.file_size,
            facts.extension,
            facts.resolution,
            facts.codec,
            facts.source,
            MediaFileStatus.PRESENT.value,
            facts.observed_at.isoformat(),
            facts.observed_at.isoformat(),
        )
        try:
            media_file_id = self._insert(sql, values)
        except sqlite3.IntegrityError as error:
            if self.get_by_path(facts.current_path) is not None:
                raise MediaFilePathConflictError(str(facts.current_path)) from error
            raise CatalogIntegrityError("media-file relationship is invalid") from error
        media_file = self.get_by_id(media_file_id)
        if media_file is None:
            raise CatalogRecordNotFoundError(media_file_id)
        return media_file

    def refresh_verified_facts(
        self,
        media_file_id: int,
        facts: VerifiedMediaFileFacts,
    ) -> MediaFile:
        current = self.get_by_id(media_file_id)
        if current is None:
            raise CatalogRecordNotFoundError(media_file_id)
        if path_key(current.current_path) != path_key(facts.current_path):
            raise MediaFilePathConflictError("verified facts identify a different path")
        expected_path_key = path_key(current.current_path)
        rowcount = self._execute_rowcount(
            """
            UPDATE media_files
               SET file_size = ?, extension = ?, resolution = ?, codec = ?, source = ?,
                   status = ?, last_seen_at = ?
             WHERE id = ? AND path_key = ?
            """,
            (
                facts.file_size,
                facts.extension,
                facts.resolution,
                facts.codec,
                facts.source,
                MediaFileStatus.PRESENT.value,
                facts.observed_at.isoformat(),
                media_file_id,
                expected_path_key,
            ),
        )
        if rowcount != 1:
            latest = self.get_by_id(media_file_id)
            if latest is None:
                raise CatalogRecordNotFoundError(media_file_id)
            raise MediaFilePathConflictError("media-file path changed during fact refresh")
        return self._require(media_file_id)

    def link_to_movie(self, media_file_id: int, movie_id: int) -> MediaFile:
        try:
            rowcount = self._execute_rowcount(
                """
                UPDATE media_files
                   SET movie_id = ?
                 WHERE id = ? AND (movie_id IS NULL OR movie_id = ?)
                """,
                (movie_id, media_file_id, movie_id),
            )
        except sqlite3.IntegrityError as error:
            raise CatalogIntegrityError(f"movie {movie_id} does not exist") from error
        if rowcount != 1:
            current = self.get_by_id(media_file_id)
            if current is None:
                raise CatalogRecordNotFoundError(media_file_id)
            raise MediaFileAssociationConflict(
                f"media file {media_file_id} is already linked to movie {current.movie_id}"
            )
        return self._require(media_file_id)

    def mark_missing(self, media_file_id: int) -> MediaFile:
        self._update(
            "UPDATE media_files SET status = ? WHERE id = ?",
            (MediaFileStatus.MISSING.value, media_file_id),
        )
        return self._require(media_file_id)

    def mark_present(self, media_file_id: int, *, observed_at: datetime) -> MediaFile:
        if not isinstance(observed_at, datetime) or observed_at.tzinfo is None:
            raise ValueError("observed_at must be a timezone-aware datetime")
        self._update(
            "UPDATE media_files SET status = ?, last_seen_at = ? WHERE id = ?",
            (MediaFileStatus.PRESENT.value, observed_at.isoformat(), media_file_id),
        )
        return self._require(media_file_id)

    def list_for_movie(self, movie_id: int) -> tuple[MediaFile, ...]:
        rows = self._fetchall(
            "SELECT * FROM media_files WHERE movie_id = ? ORDER BY id",
            (movie_id,),
        )
        return tuple(media_file_from_row(row) for row in rows)

    def count_cataloged(self) -> int:
        row = self._fetchone("SELECT COUNT(*) AS count FROM media_files", ())
        return 0 if row is None else int(row["count"])

    def list_cataloged(self, *, after_id: int, limit: int) -> tuple[MediaFile, ...]:
        if isinstance(after_id, bool) or not isinstance(after_id, int) or after_id < 0:
            raise ValueError("after_id must be a non-negative integer")
        if isinstance(limit, bool) or not isinstance(limit, int) or limit <= 0:
            raise ValueError("limit must be a positive integer")
        rows = self._fetchall(
            "SELECT * FROM media_files WHERE id > ? ORDER BY id ASC LIMIT ?",
            (after_id, limit),
        )
        return tuple(media_file_from_row(row) for row in rows)

    def apply_status_updates(self, updates: tuple[MediaFileStatusUpdate, ...]) -> int:
        if not isinstance(updates, tuple) or any(
            not isinstance(update, MediaFileStatusUpdate) for update in updates
        ):
            raise ValueError("updates must be a tuple of MediaFileStatusUpdate values")
        if not updates:
            return 0
        applied = 0
        with self.database.transaction() as connection:
            for update in updates:
                last_seen = (
                    update.observed_at.isoformat()
                    if update.status is MediaFileStatus.PRESENT
                    else None
                )
                sql = "UPDATE media_files SET status = ?"
                values: list[object] = [update.status.value]
                if last_seen is not None:
                    sql += ", last_seen_at = ?"
                    values.append(last_seen)
                sql += " WHERE id = ? AND path_key = ?"
                values.extend((update.media_file_id, path_key(update.expected_path)))
                applied += connection.execute(sql, tuple(values)).rowcount
        return applied

    def relink(
        self,
        media_file_id: int,
        *,
        expected_path: Path,
        new_path: Path,
        observed_at: datetime,
    ) -> MediaFile:
        if not isinstance(observed_at, datetime) or observed_at.tzinfo is None:
            raise ValueError("observed_at must be a timezone-aware datetime")
        with self.database.transaction() as connection:
            try:
                cursor = connection.execute(
                    """
                    UPDATE media_files
                       SET current_path = ?, path_key = ?, status = ?, last_seen_at = ?
                     WHERE id = ? AND path_key = ? AND status = ?
                    """,
                    (
                        str(new_path.absolute()),
                        path_key(new_path),
                        MediaFileStatus.PRESENT.value,
                        observed_at.isoformat(),
                        media_file_id,
                        path_key(expected_path),
                        MediaFileStatus.MISSING.value,
                    ),
                )
            except sqlite3.IntegrityError as error:
                raise MediaFilePathConflictError(str(new_path)) from error
            if cursor.rowcount != 1:
                raise MediaFilePathConflictError(
                    "media-file path or status changed before relink"
                )
        return self._require(media_file_id)

    def _require(self, media_file_id: int) -> MediaFile:
        media_file = self.get_by_id(media_file_id)
        if media_file is None:
            raise CatalogRecordNotFoundError(media_file_id)
        return media_file

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

    def _update(self, sql: str, values: tuple[object, ...]) -> None:
        rowcount = self._execute_rowcount(sql, values)
        if rowcount != 1:
            raise CatalogRecordNotFoundError(values[-1])

    def _execute_rowcount(self, sql: str, values: tuple[object, ...]) -> int:
        if self._connection is not None:
            cursor = self._connection.execute(sql, values)
        else:
            with self.database.transaction() as conn:
                cursor = conn.execute(sql, values)
        return cursor.rowcount

def _decode_datetime(value: object) -> datetime:
    if not isinstance(value, str):
        raise ValueError("timestamp must be text")
    decoded = datetime.fromisoformat(value)
    if decoded.tzinfo is None:
        raise ValueError("timestamp must be timezone-aware")
    return decoded


def media_file_from_row(row: sqlite3.Row) -> MediaFile:
    """Normalize one SQLite media-file row for database-layer repository reuse."""
    try:
        return MediaFile(
            id=row["id"],
            movie_id=row["movie_id"],
            current_path=Path(row["current_path"]),
            file_size=row["file_size"],
            extension=row["extension"],
            resolution=row["resolution"],
            codec=row["codec"],
            source=row["source"],
            status=MediaFileStatus(row["status"]),
            discovered_at=_decode_datetime(row["discovered_at"]),
            last_seen_at=_decode_datetime(row["last_seen_at"]),
        )
    except (TypeError, ValueError) as error:
        raise CatalogDataError(f"invalid media-file catalog row {row['id']}") from error
