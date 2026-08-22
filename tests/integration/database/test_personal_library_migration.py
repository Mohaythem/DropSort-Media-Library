from __future__ import annotations

from pathlib import Path
import sqlite3

import pytest

from dropsort.database import Database, MigrationRunner


def _migration_dir(tmp_path: Path, versions: tuple[str, ...]) -> Path:
    source = Path(__file__).parents[3] / "src" / "dropsort" / "database" / "migrations"
    destination = tmp_path / "migrations"
    destination.mkdir()
    for filename in versions:
        (destination / filename).write_text(
            (source / filename).read_text(encoding="utf-8"), encoding="utf-8"
        )
    return destination


def test_schema_v3_migrates_to_v4_without_losing_catalog_or_history(tmp_path: Path) -> None:
    migration_dir = _migration_dir(
        tmp_path,
        (
            "0001_initial.up.sql",
            "0002_portable_filesystem_identity.up.sql",
            "0003_movie_catalog.up.sql",
        ),
    )
    database = Database(tmp_path / "schema-v3.sqlite3")
    runner = MigrationRunner(database, migration_dir)
    runner.migrate()
    with database.transaction() as connection:
        movie_id = connection.execute(
            """
            INSERT INTO movies(provider, external_id, title, date_added, created_at, updated_at)
            VALUES ('tmdb', 'v3-movie', 'Movie', 'date', 'created', 'updated')
            """
        ).lastrowid
        connection.execute(
            """
            INSERT INTO media_files(
                movie_id, current_path, path_key, file_size, discovered_at, last_seen_at
            ) VALUES (?, 'C:\\Movies\\Movie.mkv', 'c:\\movies\\movie.mkv', 10, 'd', 's')
            """,
            (movie_id,),
        )
        connection.execute(
            """
            INSERT INTO file_operations(
                id, operation_type, source_path, destination_path, state, created_at, updated_at
            ) VALUES ('v3-history', 'MOVE', 'C:\\a.mkv', 'C:\\b.mkv', 'COMMITTED', 'c', 'u')
            """
        )

    source = Path(__file__).parents[3] / "src" / "dropsort" / "database" / "migrations"
    (migration_dir / "0004_personal_library_foundation.up.sql").write_text(
        (source / "0004_personal_library_foundation.up.sql").read_text(encoding="utf-8"),
        encoding="utf-8",
    )
    runner.migrate()
    runner.migrate()

    with database.connection() as connection:
        assert connection.execute("SELECT version FROM schema_migrations ORDER BY version").fetchall()[-1][0] == 4
        assert connection.execute("SELECT COUNT(*) FROM movies").fetchone()[0] == 1
        assert connection.execute("SELECT COUNT(*) FROM media_files").fetchone()[0] == 1
        assert connection.execute("SELECT COUNT(*) FROM file_operations").fetchone()[0] == 1
        assert connection.execute("PRAGMA foreign_key_check").fetchall() == []
        assert {
            row["name"] for row in connection.execute("PRAGMA index_list('watch_events')").fetchall()
        } == {"idx_watch_events_movie_watched"}


def test_personal_downgrade_refuses_to_discard_personal_rows(harness) -> None:
    with harness.database.transaction() as connection:
        movie_id = connection.execute(
            """
            INSERT INTO movies(provider, external_id, title, date_added, created_at, updated_at)
            VALUES ('tmdb', 'downgrade', 'Movie', 'date', 'created', 'updated')
            """
        ).lastrowid
        connection.execute(
            """
            INSERT INTO movie_personal_state(movie_id, preference, created_at, updated_at)
            VALUES (?, 'LIKED', 'created', 'updated')
            """,
            (movie_id,),
        )
    path = (
        Path(__file__).parents[3]
        / "src"
        / "dropsort"
        / "database"
        / "migrations"
        / "0004_personal_library_foundation.down.sql"
    )
    connection = harness.database.connect()
    try:
        with pytest.raises(sqlite3.IntegrityError):
            connection.executescript(f"BEGIN IMMEDIATE;\n{path.read_text(encoding='utf-8')}\nCOMMIT;")
        if connection.in_transaction:
            connection.rollback()
    finally:
        connection.close()

    with harness.database.connection() as connection:
        assert connection.execute("SELECT COUNT(*) FROM movie_personal_state").fetchone()[0] == 1
