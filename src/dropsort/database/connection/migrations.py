from __future__ import annotations

from pathlib import Path
import re
import sqlite3

from dropsort.database.connection.sqlite import Database


_MIGRATION_RE = re.compile(r"^(?P<version>\d+)_.*\.up\.sql$")


class MigrationRunner:
    def __init__(self, database: Database, migration_dir: Path | None = None) -> None:
        self.database = database
        self.migration_dir = migration_dir or Path(__file__).parents[1] / "migrations"

    def migrate(self) -> None:
        self._ensure_migration_table()
        applied = self._applied_versions()
        for path in self._up_migrations():
            version = self._version(path)
            if version not in applied:
                self._apply_atomic(path, version)

    def _ensure_migration_table(self) -> None:
        with self.database.transaction() as conn:
            conn.execute(
                """
                CREATE TABLE IF NOT EXISTS schema_migrations (
                    version INTEGER PRIMARY KEY,
                    filename TEXT NOT NULL,
                    applied_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
                )
                """
            )

    def _apply_atomic(self, path: Path, version: int) -> None:
        sql = path.read_text(encoding="utf-8")
        escaped_name = path.name.replace("'", "''")
        script = (
            "BEGIN IMMEDIATE;\n"
            f"{sql}\n"
            "INSERT INTO schema_migrations(version, filename) "
            f"VALUES ({version}, '{escaped_name}');\n"
            "COMMIT;"
        )
        conn = self.database.connect()
        try:
            conn.executescript(script)
        except sqlite3.DatabaseError:
            if conn.in_transaction:
                conn.rollback()
            raise
        finally:
            conn.close()

    def _applied_versions(self) -> set[int]:
        with self.database.connection() as conn:
            rows = conn.execute("SELECT version FROM schema_migrations").fetchall()
        return {int(row["version"]) for row in rows}

    def _up_migrations(self) -> list[Path]:
        paths = [path for path in self.migration_dir.glob("*.up.sql") if _MIGRATION_RE.match(path.name)]
        return sorted(paths, key=self._version)

    @staticmethod
    def _version(path: Path) -> int:
        match = _MIGRATION_RE.match(path.name)
        if match is None:
            raise ValueError(f"Invalid migration filename: {path.name}")
        return int(match.group("version"))
