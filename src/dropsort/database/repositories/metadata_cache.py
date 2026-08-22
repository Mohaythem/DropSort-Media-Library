from __future__ import annotations

from datetime import datetime
import sqlite3

from dropsort.database.connection.sqlite import Database
from dropsort.metadata.cache import CacheRecord


class MetadataCacheRepository:
    def __init__(self, database: Database) -> None:
        self.database = database

    def get(
        self,
        provider: str,
        cache_key: str,
        *,
        conn: sqlite3.Connection | None = None,
    ) -> CacheRecord | None:
        sql = "SELECT * FROM metadata_cache WHERE provider = ? AND cache_key = ?"
        if conn is not None:
            row = conn.execute(sql, (provider, cache_key)).fetchone()
        else:
            with self.database.connection() as db_conn:
                row = db_conn.execute(sql, (provider, cache_key)).fetchone()
        if row is None:
            return None
        try:
            fetched_at = datetime.fromisoformat(row["fetched_at"])
            expires_at = datetime.fromisoformat(row["expires_at"])
        except (TypeError, ValueError):
            return None
        if fetched_at.tzinfo is None or expires_at.tzinfo is None:
            return None
        return CacheRecord(
            provider=row["provider"],
            cache_key=row["cache_key"],
            payload=row["payload"],
            fetched_at=fetched_at,
            expires_at=expires_at,
        )

    def put(
        self,
        record: CacheRecord,
        *,
        conn: sqlite3.Connection | None = None,
    ) -> None:
        if record.fetched_at.tzinfo is None or record.expires_at.tzinfo is None:
            raise ValueError("metadata cache timestamps must be timezone-aware")
        sql = """
            INSERT INTO metadata_cache(provider, cache_key, payload, fetched_at, expires_at)
            VALUES (?, ?, ?, ?, ?)
            ON CONFLICT(provider, cache_key) DO UPDATE SET
                payload = excluded.payload,
                fetched_at = excluded.fetched_at,
                expires_at = excluded.expires_at
        """
        values = (
            record.provider,
            record.cache_key,
            record.payload,
            record.fetched_at.isoformat(),
            record.expires_at.isoformat(),
        )
        if conn is not None:
            conn.execute(sql, values)
        else:
            with self.database.transaction() as tx:
                tx.execute(sql, values)
