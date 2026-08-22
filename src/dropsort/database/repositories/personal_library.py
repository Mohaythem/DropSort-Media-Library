from __future__ import annotations

from datetime import datetime, timezone
import sqlite3

from dropsort.database.connection.sqlite import Database
from dropsort.database.repositories.movies import movie_from_row
from dropsort.library.personal import (
    PersonalMovieState,
    PersonalMovieSummary,
    PersonalMovieNotFoundError,
    PersonalLibrarySection,
    PersonalPreference,
    ReadyToWatchMovie,
    WatchEvent,
    WatchEventNotFoundError,
)


class SqlitePersonalLibraryRepository:
    """SQLite adapter for personal state; it has no filesystem dependencies."""

    def __init__(self, database: Database) -> None:
        self.database = database

    def get_state(self, movie_id: int) -> PersonalMovieState:
        _validate_movie_id(movie_id)
        with self.database.connection() as connection:
            self._require_movie(connection, movie_id)
            return self._state(connection, movie_id)

    def set_preference(
        self,
        movie_id: int,
        preference: PersonalPreference,
        *,
        now: datetime,
    ) -> PersonalMovieState:
        _validate_movie_id(movie_id)
        _validate_preference(preference)
        _validate_datetime(now, "now")
        if preference is PersonalPreference.NO_OPINION:
            return self.clear_preference(movie_id, now=now)
        with self.database.transaction() as connection:
            self._require_movie(connection, movie_id)
            timestamp = _utc_iso(now)
            connection.execute(
                """
                INSERT INTO movie_personal_state(
                    movie_id, preference, watchlist_added_at, created_at, updated_at
                ) VALUES (?, ?, NULL, ?, ?)
                ON CONFLICT(movie_id) DO UPDATE SET
                    preference = excluded.preference,
                    updated_at = excluded.updated_at
                """,
                (movie_id, preference.value, timestamp, timestamp),
            )
            return self._state(connection, movie_id)

    def clear_preference(self, movie_id: int, *, now: datetime) -> PersonalMovieState:
        _validate_movie_id(movie_id)
        _validate_datetime(now, "now")
        with self.database.transaction() as connection:
            self._require_movie(connection, movie_id)
            row = connection.execute(
                "SELECT watchlist_added_at FROM movie_personal_state WHERE movie_id = ?",
                (movie_id,),
            ).fetchone()
            if row is None or row["watchlist_added_at"] is None:
                connection.execute(
                    "DELETE FROM movie_personal_state WHERE movie_id = ?",
                    (movie_id,),
                )
            else:
                connection.execute(
                    """
                    UPDATE movie_personal_state
                       SET preference = 'NO_OPINION', updated_at = ?
                     WHERE movie_id = ?
                    """,
                    (_utc_iso(now), movie_id),
                )
            return self._state(connection, movie_id)

    def record_watch(
        self,
        movie_id: int,
        *,
        watched_at: datetime,
        created_at: datetime,
    ) -> WatchEvent:
        _validate_movie_id(movie_id)
        _validate_datetime(watched_at, "watched_at")
        _validate_datetime(created_at, "created_at")
        with self.database.transaction() as connection:
            self._require_movie(connection, movie_id)
            cursor = connection.execute(
                """
                INSERT INTO watch_events(movie_id, watched_at, created_at)
                VALUES (?, ?, ?)
                """,
                (movie_id, _utc_iso(watched_at), _utc_iso(created_at)),
            )
            event_id = int(cursor.lastrowid)
            return self._event(connection, movie_id, event_id)

    def remove_watch_event(self, event_id: int) -> WatchEvent:
        _validate_event_id(event_id)
        with self.database.transaction() as connection:
            row = connection.execute(
                "SELECT movie_id FROM watch_events WHERE id = ?", (event_id,)
            ).fetchone()
            if row is None:
                raise WatchEventNotFoundError(event_id)
            event = self._event(connection, int(row["movie_id"]), event_id)
            connection.execute("DELETE FROM watch_events WHERE id = ?", (event_id,))
            return event

    def list_watch_history(self, movie_id: int) -> tuple[WatchEvent, ...]:
        _validate_movie_id(movie_id)
        with self.database.connection() as connection:
            self._require_movie(connection, movie_id)
            rows = connection.execute(
                """
                SELECT id, movie_id, watched_at, created_at
                  FROM watch_events
                 WHERE movie_id = ?
                 ORDER BY watched_at, id
                """,
                (movie_id,),
            ).fetchall()
            return _events_from_rows(rows)

    def add_to_watchlist(self, movie_id: int, *, now: datetime) -> PersonalMovieState:
        _validate_movie_id(movie_id)
        _validate_datetime(now, "now")
        with self.database.transaction() as connection:
            self._require_movie(connection, movie_id)
            timestamp = _utc_iso(now)
            connection.execute(
                """
                INSERT INTO movie_personal_state(
                    movie_id, preference, watchlist_added_at, created_at, updated_at
                ) VALUES (?, 'NO_OPINION', ?, ?, ?)
                ON CONFLICT(movie_id) DO UPDATE SET
                    watchlist_added_at = COALESCE(
                        movie_personal_state.watchlist_added_at, excluded.watchlist_added_at
                    ),
                    updated_at = CASE
                        WHEN movie_personal_state.watchlist_added_at IS NULL
                        THEN excluded.updated_at ELSE movie_personal_state.updated_at END
                """,
                (movie_id, timestamp, timestamp, timestamp),
            )
            return self._state(connection, movie_id)

    def remove_from_watchlist(self, movie_id: int, *, now: datetime) -> PersonalMovieState:
        _validate_movie_id(movie_id)
        _validate_datetime(now, "now")
        with self.database.transaction() as connection:
            self._require_movie(connection, movie_id)
            row = connection.execute(
                "SELECT preference FROM movie_personal_state WHERE movie_id = ?",
                (movie_id,),
            ).fetchone()
            if row is None or row["preference"] == PersonalPreference.NO_OPINION.value:
                connection.execute(
                    "DELETE FROM movie_personal_state WHERE movie_id = ?", (movie_id,)
                )
            else:
                connection.execute(
                    """
                    UPDATE movie_personal_state
                       SET watchlist_added_at = NULL, updated_at = ?
                     WHERE movie_id = ?
                    """,
                    (_utc_iso(now), movie_id),
                )
            return self._state(connection, movie_id)

    def list_ready_to_watch(
        self,
        *,
        limit: int,
        offset: int,
    ) -> tuple[ReadyToWatchMovie, ...]:
        if isinstance(limit, bool) or not isinstance(limit, int) or limit <= 0:
            raise ValueError("limit must be a positive integer")
        if isinstance(offset, bool) or not isinstance(offset, int) or offset < 0:
            raise ValueError("offset must be a non-negative integer")
        with self.database.connection() as connection:
            rows = connection.execute(
                """
                SELECT m.*, ps.watchlist_added_at, COUNT(mf.id) AS present_media_file_count
                  FROM movies AS m
                  JOIN movie_personal_state AS ps ON ps.movie_id = m.id
                  JOIN media_files AS mf
                    ON mf.movie_id = m.id AND mf.status = 'PRESENT'
                 WHERE ps.watchlist_added_at IS NOT NULL
                   AND NOT EXISTS (
                       SELECT 1 FROM watch_events AS we WHERE we.movie_id = m.id
                   )
                 GROUP BY m.id, ps.watchlist_added_at
                 ORDER BY ps.watchlist_added_at DESC, m.id DESC
                 LIMIT ? OFFSET ?
                """,
                (limit, offset),
            ).fetchall()
        return tuple(
            ReadyToWatchMovie(
                movie_id=int(row["id"]),
                movie=movie_from_row(row),
                present_media_file_count=int(row["present_media_file_count"]),
                watchlist_added_at=_decode_datetime(row["watchlist_added_at"]),
            )
            for row in rows
        )

    def list_movies(
        self,
        section: PersonalLibrarySection,
        *,
        limit: int,
        offset: int,
    ) -> tuple[PersonalMovieSummary, ...]:
        if not isinstance(section, PersonalLibrarySection):
            raise ValueError("section must be PersonalLibrarySection")
        if isinstance(limit, bool) or not isinstance(limit, int) or limit <= 0:
            raise ValueError("limit must be a positive integer")
        if isinstance(offset, bool) or not isinstance(offset, int) or offset < 0:
            raise ValueError("offset must be a non-negative integer")
        conditions = {
            PersonalLibrarySection.WATCHLIST: "ps.watchlist_added_at IS NOT NULL",
            PersonalLibrarySection.READY_TO_WATCH: (
                "ps.watchlist_added_at IS NOT NULL "
                "AND EXISTS (SELECT 1 FROM media_files ready_mf "
                "WHERE ready_mf.movie_id = m.id AND ready_mf.status = 'PRESENT') "
                "AND NOT EXISTS (SELECT 1 FROM watch_events ready_we "
                "WHERE ready_we.movie_id = m.id)"
            ),
            PersonalLibrarySection.LIKED: "ps.preference = 'LIKED'",
            PersonalLibrarySection.BLACKLISTED: "ps.preference = 'BLACKLISTED'",
        }
        with self.database.connection() as connection:
            rows = connection.execute(
                f"""
                SELECT m.*, COUNT(DISTINCT mf.id) AS media_file_count,
                       COALESCE(SUM(CASE WHEN mf.status = 'MISSING' THEN 1 ELSE 0 END), 0)
                           AS missing_file_count,
                       COALESCE(ps.preference, 'NO_OPINION') AS preference,
                       CASE WHEN ps.watchlist_added_at IS NULL THEN 0 ELSE 1 END AS watchlisted,
                       COUNT(DISTINCT we.id) AS watch_count,
                       MAX(we.watched_at) AS last_watched
                  FROM movies AS m
                  JOIN movie_personal_state AS ps ON ps.movie_id = m.id
                  LEFT JOIN media_files AS mf ON mf.movie_id = m.id
                  LEFT JOIN watch_events AS we ON we.movie_id = m.id
                 WHERE {conditions[section]}
                 GROUP BY m.id, ps.preference, ps.watchlist_added_at
                 ORDER BY julianday(m.date_added) DESC, m.id DESC
                 LIMIT ? OFFSET ?
                """,
                (limit, offset),
            ).fetchall()
        return tuple(
            PersonalMovieSummary(
                movie=movie_from_row(row),
                media_file_count=int(row["media_file_count"]),
                missing_file_count=int(row["missing_file_count"]),
                preference=PersonalPreference(row["preference"]),
                watchlisted=bool(row["watchlisted"]),
                watch_count=int(row["watch_count"]),
                last_watched=_optional_datetime(row["last_watched"]),
            )
            for row in rows
        )

    def _state(self, connection: sqlite3.Connection, movie_id: int) -> PersonalMovieState:
        row = connection.execute(
            """
            SELECT ps.movie_id, ps.preference, ps.watchlist_added_at,
                   ps.created_at, ps.updated_at,
                   COUNT(we.id) AS watch_count, MAX(we.watched_at) AS last_watched
              FROM movies AS m
              LEFT JOIN movie_personal_state AS ps ON ps.movie_id = m.id
              LEFT JOIN watch_events AS we ON we.movie_id = m.id
             WHERE m.id = ?
             GROUP BY m.id, ps.movie_id, ps.preference, ps.watchlist_added_at,
                      ps.created_at, ps.updated_at
            """,
            (movie_id,),
        ).fetchone()
        if row is None:
            raise PersonalMovieNotFoundError(movie_id)
        return PersonalMovieState(
            movie_id=movie_id,
            preference=PersonalPreference(row["preference"] or PersonalPreference.NO_OPINION),
            watchlist_added_at=_optional_datetime(row["watchlist_added_at"]),
            watch_count=int(row["watch_count"]),
            last_watched=_optional_datetime(row["last_watched"]),
            created_at=_optional_datetime(row["created_at"]),
            updated_at=_optional_datetime(row["updated_at"]),
        )

    def _event(
        self,
        connection: sqlite3.Connection,
        movie_id: int,
        event_id: int,
    ) -> WatchEvent:
        rows = connection.execute(
            """
            SELECT id, movie_id, watched_at, created_at
              FROM watch_events
             WHERE movie_id = ?
             ORDER BY watched_at, id
            """,
            (movie_id,),
        ).fetchall()
        events = _events_from_rows(rows)
        for event in events:
            if event.id == event_id:
                return event
        raise WatchEventNotFoundError(event_id)

    @staticmethod
    def _require_movie(connection: sqlite3.Connection, movie_id: int) -> None:
        if connection.execute("SELECT 1 FROM movies WHERE id = ?", (movie_id,)).fetchone() is None:
            raise PersonalMovieNotFoundError(movie_id)


def _events_from_rows(rows: list[sqlite3.Row]) -> tuple[WatchEvent, ...]:
    return tuple(
        WatchEvent(
            id=int(row["id"]),
            movie_id=int(row["movie_id"]),
            watched_at=_decode_datetime(row["watched_at"]),
            rewatch=index > 0,
            created_at=_optional_datetime(row["created_at"]),
        )
        for index, row in enumerate(rows)
    )


def _validate_movie_id(movie_id: int) -> None:
    if isinstance(movie_id, bool) or not isinstance(movie_id, int) or movie_id <= 0:
        raise ValueError("movie_id must be a positive integer")


def _validate_event_id(event_id: int) -> None:
    if isinstance(event_id, bool) or not isinstance(event_id, int) or event_id <= 0:
        raise ValueError("event_id must be a positive integer")


def _validate_preference(preference: PersonalPreference) -> None:
    if not isinstance(preference, PersonalPreference):
        raise ValueError("preference must be PersonalPreference")


def _validate_datetime(value: datetime, field_name: str) -> None:
    if not isinstance(value, datetime) or value.tzinfo is None:
        raise ValueError(f"{field_name} must be a timezone-aware datetime")


def _utc_iso(value: datetime) -> str:
    return value.astimezone(timezone.utc).isoformat()


def _decode_datetime(value: object) -> datetime:
    if not isinstance(value, str):
        raise ValueError("timestamp must be text")
    decoded = datetime.fromisoformat(value)
    if decoded.tzinfo is None:
        raise ValueError("timestamp must be timezone-aware")
    return decoded


def _optional_datetime(value: object) -> datetime | None:
    return None if value is None else _decode_datetime(value)
