from __future__ import annotations

from pathlib import Path
import re
import sqlite3

from dropsort.database.connection.sqlite import Database


_MIGRATION_RE = re.compile(r"^(?P<version>\d+)_.*\.up\.sql$")
_FOREIGN_KEYS_OFF_MARKER = "-- dropsort: foreign_keys_off"


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

    def rollback_latest(self) -> int | None:
        """Apply the latest down migration without exposing a partial schema."""

        self._ensure_migration_table()
        applied = self._applied_versions()
        if not applied:
            return None
        version = max(applied)
        up_path = next(
            (path for path in self._up_migrations() if self._version(path) == version),
            None,
        )
        if up_path is None:
            raise FileNotFoundError(f"missing up migration for version {version}")
        down_path = up_path.with_name(up_path.name.replace(".up.sql", ".down.sql"))
        if not down_path.is_file():
            raise FileNotFoundError(f"missing down migration for version {version}")
        self._apply_atomic(down_path, version, remove_version=True)
        return version

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

    def _apply_atomic(
        self,
        path: Path,
        version: int,
        *,
        remove_version: bool = False,
    ) -> None:
        sql = path.read_text(encoding="utf-8")
        foreign_keys_off = _FOREIGN_KEYS_OFF_MARKER in sql.splitlines()[:5]
        conn = self.database.connect()
        try:
            if foreign_keys_off:
                conn.execute("PRAGMA foreign_keys = OFF")
                if conn.execute("PRAGMA foreign_keys").fetchone()[0] != 0:
                    raise sqlite3.DatabaseError("could not disable foreign keys for migration")
            conn.executescript(f"BEGIN IMMEDIATE;\n{sql}\n")
            violations = conn.execute("PRAGMA foreign_key_check").fetchall()
            if violations:
                raise sqlite3.IntegrityError(
                    f"migration {version} introduced foreign-key violations"
                )
            if remove_version:
                cursor = conn.execute(
                    "DELETE FROM schema_migrations WHERE version = ?",
                    (version,),
                )
                if cursor.rowcount != 1:
                    raise sqlite3.DatabaseError(
                        f"migration version {version} is not applied"
                    )
            else:
                conn.execute(
                    "INSERT INTO schema_migrations(version, filename) VALUES (?, ?)",
                    (version, path.name),
                )
            conn.commit()
        except sqlite3.DatabaseError:
            if conn.in_transaction:
                conn.rollback()
            raise
        finally:
            if conn.in_transaction:
                conn.rollback()
            if foreign_keys_off:
                conn.execute("PRAGMA foreign_keys = ON")
                if conn.execute("PRAGMA foreign_keys").fetchone()[0] != 1:
                    conn.close()
                    raise sqlite3.DatabaseError(
                        "foreign-key enforcement was not restored after migration"
                    )
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
