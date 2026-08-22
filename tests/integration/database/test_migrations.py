from pathlib import Path
import sqlite3

import pytest

from dropsort.core.operations.models import (
    FileOperationPlan,
    OperationState,
    OperationType,
    OperationUpdate,
)
from dropsort.database import Database, MigrationRunner
from dropsort.database.repositories import FileOperationRepository


def test_initial_migration_is_idempotent(tmp_path: Path) -> None:
    database = Database(tmp_path / "db.sqlite3")
    runner = MigrationRunner(database)
    runner.migrate()
    runner.migrate()

    with database.connection() as conn:
        tables = {
            row["name"]
            for row in conn.execute("SELECT name FROM sqlite_master WHERE type='table'").fetchall()
        }
        version_count = conn.execute("SELECT COUNT(*) AS n FROM schema_migrations").fetchone()["n"]

    assert {
        "movies",
        "media_files",
        "metadata_cache",
        "file_operations",
        "watched_folders",
        "settings",
        "schema_migrations",
    }.issubset(tables)
    assert version_count == 4


def test_catalog_migration_adds_genres_and_media_file_movie_index(harness) -> None:
    with harness.database.connection() as conn:
        movie_columns = {
            row["name"]: row for row in conn.execute("PRAGMA table_info('movies')").fetchall()
        }
        media_indexes = {
            row["name"]
            for row in conn.execute("PRAGMA index_list('media_files')").fetchall()
        }
        movie_indexes = conn.execute("PRAGMA index_list('movies')").fetchall()
        unique_movie_indexes = {
            tuple(
                column["name"]
                for column in conn.execute(
                    f"PRAGMA index_info('{row['name']}')"
                ).fetchall()
            )
            for row in movie_indexes
            if row["unique"] == 1
        }
        media_foreign_keys = conn.execute("PRAGMA foreign_key_list('media_files')").fetchall()

    assert movie_columns["genres"]["notnull"] == 1
    assert movie_columns["genres"]["dflt_value"] == "'[]'"
    assert "idx_media_files_movie_id" in media_indexes
    assert ("provider", "external_id") in unique_movie_indexes
    assert any(
        row["table"] == "movies"
        and row["from"] == "movie_id"
        and row["to"] == "id"
        and row["on_delete"] == "SET NULL"
        for row in media_foreign_keys
    )


def test_catalog_migration_preserves_existing_movie_and_file_records(tmp_path: Path) -> None:
    project_migrations = Path(__file__).parents[3] / "src" / "dropsort" / "database" / "migrations"
    migration_dir = tmp_path / "migrations"
    migration_dir.mkdir()
    for name in (
        "0001_initial.up.sql",
        "0002_portable_filesystem_identity.up.sql",
    ):
        (migration_dir / name).write_text(
            (project_migrations / name).read_text(encoding="utf-8"),
            encoding="utf-8",
        )

    database = Database(tmp_path / "legacy-catalog.sqlite3")
    runner = MigrationRunner(database, migration_dir)
    runner.migrate()
    with database.transaction() as conn:
        conn.execute(
            """
            INSERT INTO movies(
                id, provider, external_id, title, date_added, created_at, updated_at
            ) VALUES (7, 'tmdb', '155', 'The Dark Knight', 'date', 'created', 'updated')
            """
        )
        conn.execute(
            """
            INSERT INTO media_files(
                id, movie_id, current_path, path_key, file_size, discovered_at, last_seen_at
            ) VALUES (8, 7, 'D:\\Movies\\Movie.mkv', 'd:\\movies\\movie.mkv', 123, 'd', 's')
            """
        )

    name = "0003_movie_catalog.up.sql"
    (migration_dir / name).write_text(
        (project_migrations / name).read_text(encoding="utf-8"),
        encoding="utf-8",
    )
    runner.migrate()

    with database.connection() as conn:
        movie = conn.execute("SELECT * FROM movies WHERE id = 7").fetchone()
        media_file = conn.execute("SELECT * FROM media_files WHERE id = 8").fetchone()
        violations = conn.execute("PRAGMA foreign_key_check").fetchall()

    assert movie is not None
    assert movie["genres"] == "[]"
    assert media_file is not None
    assert media_file["movie_id"] == 7
    assert violations == []


def test_catalog_downgrade_refuses_to_discard_populated_genres(harness) -> None:
    with harness.database.transaction() as conn:
        conn.execute(
            """
            INSERT INTO movies(
                provider, external_id, title, genres, date_added, created_at, updated_at
            ) VALUES ('tmdb', '155', 'Movie', '["Drama"]', 'date', 'created', 'updated')
            """
        )
    down_path = (
        Path(__file__).parents[3]
        / "src"
        / "dropsort"
        / "database"
        / "migrations"
        / "0003_movie_catalog.down.sql"
    )
    script = f"BEGIN IMMEDIATE;\n{down_path.read_text(encoding='utf-8')}\nCOMMIT;"
    conn = harness.database.connect()
    try:
        with pytest.raises(sqlite3.IntegrityError):
            conn.executescript(script)
        if conn.in_transaction:
            conn.rollback()
    finally:
        conn.close()

    with harness.database.connection() as conn:
        row = conn.execute("SELECT genres FROM movies WHERE external_id = '155'").fetchone()
    assert row is not None
    assert row["genres"] == '["Drama"]'


def test_catalog_downgrade_preserves_records_when_genres_are_empty(harness) -> None:
    with harness.database.transaction() as conn:
        movie_cursor = conn.execute(
            """
            INSERT INTO movies(
                provider, external_id, title, date_added, created_at, updated_at
            ) VALUES ('tmdb', 'empty', 'Movie', 'date', 'created', 'updated')
            """
        )
        movie_id = int(movie_cursor.lastrowid)
        conn.execute(
            """
            INSERT INTO media_files(
                movie_id, current_path, path_key, file_size, discovered_at, last_seen_at
            ) VALUES (?, 'D:\\Movies\\Movie.mkv', 'd:\\movies\\movie.mkv', 1, 'd', 's')
            """,
            (movie_id,),
        )
    down_path = (
        Path(__file__).parents[3]
        / "src"
        / "dropsort"
        / "database"
        / "migrations"
        / "0003_movie_catalog.down.sql"
    )
    conn = harness.database.connect()
    try:
        conn.executescript(
            f"BEGIN IMMEDIATE;\n{down_path.read_text(encoding='utf-8')}\nCOMMIT;"
        )
    finally:
        conn.close()

    with harness.database.connection() as conn:
        columns = {
            row["name"] for row in conn.execute("PRAGMA table_info('movies')").fetchall()
        }
        indexes = {
            row["name"]
            for row in conn.execute("PRAGMA index_list('media_files')").fetchall()
        }
        movie = conn.execute("SELECT title FROM movies WHERE id = ?", (movie_id,)).fetchone()
        media_file = conn.execute(
            "SELECT movie_id FROM media_files WHERE movie_id = ?", (movie_id,)
        ).fetchone()
        violations = conn.execute("PRAGMA foreign_key_check").fetchall()

    assert "genres" not in columns
    assert "idx_media_files_movie_id" not in indexes
    assert movie is not None and movie["title"] == "Movie"
    assert media_file is not None and media_file["movie_id"] == movie_id
    assert violations == []


def test_filesystem_identity_round_trips_losslessly_as_sqlite_text(
    harness, tmp_path: Path
) -> None:
    large_dev = 11303722373345406024
    large_ino = 18446744073709551615
    plan = FileOperationPlan(
        operation_id="large-filesystem-identity",
        operation_type=OperationType.MOVE,
        source=tmp_path / "source.mkv",
        destination=tmp_path / "destination.mkv",
    )
    harness.operations.create(plan)

    record = harness.operations.transition(
        plan.operation_id,
        OperationState.VALIDATED,
        OperationUpdate(
            source_dev=large_dev,
            source_ino=large_ino,
            destination_dev=large_dev,
            destination_ino=large_ino,
        ),
    )

    assert record.source_dev == large_dev
    assert record.source_ino == large_ino
    assert record.destination_dev == large_dev
    assert record.destination_ino == large_ino
    with harness.database.connection() as conn:
        row = conn.execute(
            """
            SELECT source_dev, source_ino, destination_dev, destination_ino,
                   typeof(source_dev) AS source_dev_type,
                   typeof(source_ino) AS source_ino_type,
                   typeof(destination_dev) AS destination_dev_type,
                   typeof(destination_ino) AS destination_ino_type
              FROM file_operations
             WHERE id = ?
            """,
            (plan.operation_id,),
        ).fetchone()

    assert row is not None
    assert row["source_dev"] == str(large_dev)
    assert row["source_ino"] == str(large_ino)
    assert row["destination_dev"] == str(large_dev)
    assert row["destination_ino"] == str(large_ino)
    assert {
        row["source_dev_type"],
        row["source_ino_type"],
        row["destination_dev_type"],
        row["destination_ino_type"],
    } == {"text"}


def test_legacy_integer_filesystem_identity_migrates_to_text_without_journal_loss(
    tmp_path: Path,
) -> None:
    migration_dir = tmp_path / "migrations"
    migration_dir.mkdir()
    project_migrations = Path(__file__).parents[3] / "src" / "dropsort" / "database" / "migrations"
    initial_sql = (project_migrations / "0001_initial.up.sql").read_text(encoding="utf-8")
    legacy_sql = initial_sql
    for column in ("source_dev", "source_ino", "destination_dev", "destination_ino"):
        legacy_sql = legacy_sql.replace(f"{column} TEXT", f"{column} INTEGER")
    (migration_dir / "0001_initial.up.sql").write_text(legacy_sql, encoding="utf-8")

    database = Database(tmp_path / "legacy.sqlite3")
    runner = MigrationRunner(database, migration_dir)
    runner.migrate()
    with database.transaction() as conn:
        conn.execute(
            """
            INSERT INTO media_files(
                id, current_path, path_key, file_size, discovered_at, last_seen_at
            ) VALUES (7, 'C:\\Movies\\Movie.mkv', 'c:\\movies\\movie.mkv', 123, 'now', 'now')
            """
        )
        conn.execute(
            """
            INSERT INTO file_operations(
                id, operation_type, source_path, destination_path, state, media_file_id,
                source_dev, source_ino, destination_dev, destination_ino, created_at, updated_at
            ) VALUES (
                'original', 'MOVE', 'C:\\Source.mkv', 'C:\\Destination.mkv', 'COMMITTED', 7,
                123, 456, 789, 101112, 'created', 'updated'
            )
            """
        )
        conn.execute(
            """
            INSERT INTO file_operations(
                id, operation_type, source_path, destination_path, state,
                reverses_operation_id, created_at, updated_at
            ) VALUES (
                'reverse', 'MOVE', 'C:\\Destination.mkv', 'C:\\Source.mkv', 'PLANNED',
                'original', 'created-2', 'updated-2'
            )
            """
        )

    portable_sql = (project_migrations / "0002_portable_filesystem_identity.up.sql").read_text(
        encoding="utf-8"
    )
    (migration_dir / "0002_portable_filesystem_identity.up.sql").write_text(
        portable_sql, encoding="utf-8"
    )
    runner.migrate()

    with database.connection() as conn:
        original = conn.execute(
            """
            SELECT *, typeof(source_dev) AS source_dev_type,
                      typeof(source_ino) AS source_ino_type,
                      typeof(destination_dev) AS destination_dev_type,
                      typeof(destination_ino) AS destination_ino_type
              FROM file_operations
             WHERE id = 'original'
            """
        ).fetchone()
        reverse = conn.execute(
            "SELECT reverses_operation_id FROM file_operations WHERE id = 'reverse'"
        ).fetchone()
        indexes = {
            row["name"]
            for row in conn.execute("PRAGMA index_list('file_operations')").fetchall()
        }
        foreign_key_violations = conn.execute("PRAGMA foreign_key_check").fetchall()

    assert original is not None
    assert original["media_file_id"] == 7
    assert original["source_dev"] == "123"
    assert original["source_ino"] == "456"
    assert original["destination_dev"] == "789"
    assert original["destination_ino"] == "101112"
    assert {
        original["source_dev_type"],
        original["source_ino_type"],
        original["destination_dev_type"],
        original["destination_ino_type"],
    } == {"text"}
    assert reverse is not None
    assert reverse["reverses_operation_id"] == "original"
    assert {"idx_file_operations_state", "idx_file_operations_media_file_id"} <= indexes
    assert foreign_key_violations == []


def test_portable_filesystem_identity_downgrade_rejects_oversized_values(
    harness, tmp_path: Path
) -> None:
    large_dev = 11303722373345406024
    plan = FileOperationPlan(
        operation_id="unsafe-downgrade-identity",
        operation_type=OperationType.MOVE,
        source=tmp_path / "source.mkv",
        destination=tmp_path / "destination.mkv",
    )
    harness.operations.create(plan)
    harness.operations.transition(
        plan.operation_id,
        OperationState.VALIDATED,
        OperationUpdate(source_dev=large_dev),
    )
    down_path = (
        Path(__file__).parents[3]
        / "src"
        / "dropsort"
        / "database"
        / "migrations"
        / "0002_portable_filesystem_identity.down.sql"
    )
    script = f"BEGIN IMMEDIATE;\n{down_path.read_text(encoding='utf-8')}\nCOMMIT;"
    conn = harness.database.connect()
    try:
        with pytest.raises(sqlite3.IntegrityError):
            conn.executescript(script)
        if conn.in_transaction:
            conn.rollback()
    finally:
        conn.close()

    recovered = FileOperationRepository(harness.database).get(plan.operation_id)
    assert recovered.source_dev == large_dev
    with harness.database.connection() as conn:
        row = conn.execute(
            "SELECT source_dev, typeof(source_dev) AS value_type FROM file_operations WHERE id = ?",
            (plan.operation_id,),
        ).fetchone()
    assert row is not None
    assert row["source_dev"] == str(large_dev)
    assert row["value_type"] == "text"


def test_failed_migration_rolls_back_partial_schema(tmp_path: Path) -> None:
    migration_dir = tmp_path / "migrations"
    migration_dir.mkdir()
    (migration_dir / "0001_broken.up.sql").write_text(
        "CREATE TABLE should_rollback(id INTEGER PRIMARY KEY);\n"
        "THIS IS NOT VALID SQL;\n",
        encoding="utf-8",
    )
    database = Database(tmp_path / "broken.sqlite3")
    runner = MigrationRunner(database, migration_dir)

    with pytest.raises(sqlite3.DatabaseError):
        runner.migrate()

    with database.connection() as conn:
        table = conn.execute(
            "SELECT name FROM sqlite_master WHERE type='table' AND name='should_rollback'"
        ).fetchone()
        count = conn.execute("SELECT COUNT(*) AS n FROM schema_migrations").fetchone()["n"]
    assert table is None
    assert count == 0


def test_media_file_paths_are_unique_case_insensitively(harness, tmp_path: Path) -> None:
    first = tmp_path / "Movie.MKV"
    second = tmp_path / "movie.mkv"
    first.write_bytes(b"a")
    harness.media_files.create(first, 1)

    with pytest.raises(sqlite3.IntegrityError):
        harness.media_files.create(second, 1)
