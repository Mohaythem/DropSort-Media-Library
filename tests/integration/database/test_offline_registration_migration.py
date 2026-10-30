from __future__ import annotations

from pathlib import Path
import shutil
import sqlite3

import pytest

from dropsort.database import Database, MigrationRunner


PROJECT_MIGRATIONS = (
    Path(__file__).parents[3] / "src" / "dropsort" / "database" / "migrations"
)


def _copy_migrations(target: Path, versions: range) -> None:
    target.mkdir()
    for version in versions:
        prefix = f"{version:04d}_"
        for source in PROJECT_MIGRATIONS.glob(f"{prefix}*.sql"):
            shutil.copyfile(source, target / source.name)


def _legacy_database(tmp_path: Path) -> tuple[Database, MigrationRunner, Path]:
    migration_dir = tmp_path / "migrations"
    _copy_migrations(migration_dir, range(1, 5))
    database = Database(tmp_path / "legacy.sqlite3")
    runner = MigrationRunner(database, migration_dir)
    runner.migrate()
    with database.transaction() as connection:
        connection.execute(
            """
            INSERT INTO movies(
                id, provider, external_id, title, original_title, year, overview,
                runtime_minutes, rating, poster_path, date_added, created_at, updated_at, genres
            ) VALUES (
                7, 'tmdb', '155', 'The Dark Knight', 'The Dark Knight', 2008,
                'overview', 152, 8.5, '/poster.jpg',
                '2026-01-01T00:00:00+00:00', '2026-01-02T00:00:00+00:00',
                '2026-01-03T00:00:00+00:00', '["Drama"]'
            )
            """
        )
        connection.execute(
            r"""
            INSERT INTO media_files(
                id, movie_id, current_path, path_key, file_size, extension,
                status, discovered_at, last_seen_at
            ) VALUES (
                8, 7, 'D:\Movies\Movie.mkv', 'd:\movies\movie.mkv', 123,
                '.mkv', 'PRESENT', '2026-01-04T00:00:00+00:00',
                '2026-01-05T00:00:00+00:00'
            )
            """
        )
        connection.execute(
            """
            INSERT INTO movie_personal_state(
                movie_id, preference, watchlist_added_at, created_at, updated_at
            ) VALUES (
                7, 'LIKED', '2026-01-06T00:00:00+00:00',
                '2026-01-06T00:00:00+00:00', '2026-01-06T00:00:00+00:00'
            )
            """
        )
        connection.execute(
            """
            INSERT INTO watch_events(id, movie_id, watched_at, created_at)
            VALUES (
                9, 7, '2026-01-07T00:00:00+00:00',
                '2026-01-07T00:00:00+00:00'
            )
            """
        )
        connection.execute(
            r"""
            INSERT INTO file_operations(
                id, operation_type, source_path, destination_path, state,
                media_file_id, source_dev, source_ino, created_at, updated_at
            ) VALUES (
                'evidence', 'MOVE', 'D:\Old.mkv', 'D:\Movies\Movie.mkv',
                'COMMITTED', 8, '11303722373345406024', '99',
                '2026-01-08T00:00:00+00:00', '2026-01-08T00:00:00+00:00'
            )
            """
        )
    return database, runner, migration_dir


def test_offline_registration_migration_preserves_all_stable_identity_and_evidence(
    tmp_path: Path,
) -> None:
    database, runner, migration_dir = _legacy_database(tmp_path)
    shutil.copyfile(
        PROJECT_MIGRATIONS / "0005_offline_movie_registration.up.sql",
        migration_dir / "0005_offline_movie_registration.up.sql",
    )

    runner.migrate()

    with database.connection() as connection:
        movie = connection.execute("SELECT * FROM movies WHERE id = 7").fetchone()
        media = connection.execute("SELECT * FROM media_files WHERE id = 8").fetchone()
        personal = connection.execute(
            "SELECT * FROM movie_personal_state WHERE movie_id = 7"
        ).fetchone()
        watch = connection.execute("SELECT * FROM watch_events WHERE id = 9").fetchone()
        operation = connection.execute(
            "SELECT * FROM file_operations WHERE id = 'evidence'"
        ).fetchone()
        violations = connection.execute("PRAGMA foreign_key_check").fetchall()
        version = connection.execute(
            "SELECT MAX(version) FROM schema_migrations"
        ).fetchone()[0]

    assert movie is not None
    assert movie["provider"] == "tmdb"
    assert movie["external_id"] == "155"
    assert movie["metadata_status"] == "READY"
    assert movie["date_added"] == "2026-01-01T00:00:00+00:00"
    assert movie["created_at"] == "2026-01-02T00:00:00+00:00"
    assert movie["updated_at"] == "2026-01-03T00:00:00+00:00"
    assert media is not None and (media["id"], media["movie_id"]) == (8, 7)
    assert personal is not None and personal["preference"] == "LIKED"
    assert watch is not None and (watch["id"], watch["movie_id"]) == (9, 7)
    assert operation is not None
    assert operation["media_file_id"] == 8
    assert operation["source_dev"] == "11303722373345406024"
    assert violations == []
    assert version == 5


def test_offline_movie_constraints_allow_provisional_rows_and_reject_invalid_identity(
    harness,
) -> None:
    insert = """
        INSERT INTO movies(
            provider, external_id, title, metadata_status,
            date_added, created_at, updated_at
        ) VALUES (?, ?, ?, ?, 'd', 'c', 'u')
    """
    with harness.database.transaction() as connection:
        connection.execute(insert, (None, None, "Local A", "PENDING"))
        connection.execute(insert, (None, None, "Local B", "NEEDS_MATCH"))
        connection.execute(insert, ("tmdb", "1", "Ready", "READY"))

    with harness.database.transaction() as connection:
        for values in (
            ("tmdb", None, "Half", "PENDING"),
            (None, "1", "Half", "PENDING"),
            (" ", "1", "Blank", "PENDING"),
            ("tmdb", " ", "Blank", "PENDING"),
            (None, None, "Invalid Ready", "READY"),
            (None, None, "Invalid Status", "UNKNOWN"),
            ("tmdb", "1", "Duplicate", "READY"),
        ):
            with pytest.raises(sqlite3.IntegrityError):
                connection.execute(insert, values)

    with harness.database.connection() as connection:
        provisional = connection.execute(
            "SELECT COUNT(*) FROM movies WHERE provider IS NULL"
        ).fetchone()[0]
        violations = connection.execute("PRAGMA foreign_key_check").fetchall()
    assert provisional == 2
    assert violations == []


def test_failed_parent_rebuild_restores_old_schema_data_version_and_foreign_keys(
    tmp_path: Path,
) -> None:
    database, runner, migration_dir = _legacy_database(tmp_path)
    broken = (
        PROJECT_MIGRATIONS / "0005_offline_movie_registration.up.sql"
    ).read_text(encoding="utf-8") + "\nTHIS IS INVALID SQL;\n"
    (migration_dir / "0005_broken.up.sql").write_text(broken, encoding="utf-8")

    with pytest.raises(sqlite3.DatabaseError):
        runner.migrate()

    with database.connection() as connection:
        columns = {
            row["name"] for row in connection.execute("PRAGMA table_info('movies')")
        }
        media = connection.execute(
            "SELECT id, movie_id FROM media_files WHERE id = 8"
        ).fetchone()
        version = connection.execute(
            "SELECT MAX(version) FROM schema_migrations"
        ).fetchone()[0]
        foreign_keys = connection.execute("PRAGMA foreign_keys").fetchone()[0]
        violations = connection.execute("PRAGMA foreign_key_check").fetchall()

    assert "metadata_status" not in columns
    assert media is not None and (media["id"], media["movie_id"]) == (8, 7)
    assert version == 4
    assert foreign_keys == 1
    assert violations == []


def test_offline_registration_down_migration_refuses_provisional_state_atomically(
    harness,
) -> None:
    with harness.database.transaction() as connection:
        connection.execute(
            """
            INSERT INTO movies(
                provider, external_id, title, metadata_status,
                date_added, created_at, updated_at
            ) VALUES (NULL, NULL, 'Offline', 'PENDING', 'd', 'c', 'u')
            """
        )

    with pytest.raises(sqlite3.IntegrityError):
        MigrationRunner(harness.database).rollback_latest()

    with harness.database.connection() as connection:
        columns = {
            row["name"] for row in connection.execute("PRAGMA table_info('movies')")
        }
        row = connection.execute(
            "SELECT metadata_status FROM movies WHERE title = 'Offline'"
        ).fetchone()
        version = connection.execute(
            "SELECT MAX(version) FROM schema_migrations"
        ).fetchone()[0]
        foreign_keys = connection.execute("PRAGMA foreign_keys").fetchone()[0]
        violations = connection.execute("PRAGMA foreign_key_check").fetchall()

    assert "metadata_status" in columns
    assert row is not None and row["metadata_status"] == "PENDING"
    assert version == 5
    assert foreign_keys == 1
    assert violations == []


def test_safe_down_migration_preserves_identified_movie_and_child_references(
    harness,
) -> None:
    with harness.database.transaction() as connection:
        movie_id = connection.execute(
            """
            INSERT INTO movies(
                provider, external_id, title, metadata_status,
                date_added, created_at, updated_at
            ) VALUES ('tmdb', 'safe', 'Ready', 'READY', 'd', 'c', 'u')
            """
        ).lastrowid
        media_id = connection.execute(
            r"""
            INSERT INTO media_files(
                movie_id, current_path, path_key, file_size, discovered_at, last_seen_at
            ) VALUES (?, 'D:\Ready.mkv', 'd:\ready.mkv', 1, 'd', 's')
            """,
            (movie_id,),
        ).lastrowid
        connection.execute(
            """
            INSERT INTO watch_events(id, movie_id, watched_at, created_at)
            VALUES (55, ?, 'w', 'c')
            """,
            (movie_id,),
        )

    assert MigrationRunner(harness.database).rollback_latest() == 5

    with harness.database.connection() as connection:
        columns = {
            row["name"] for row in connection.execute("PRAGMA table_info('movies')")
        }
        media = connection.execute(
            "SELECT id, movie_id FROM media_files WHERE id = ?", (media_id,)
        ).fetchone()
        watch = connection.execute(
            "SELECT id, movie_id FROM watch_events WHERE id = 55"
        ).fetchone()
        violations = connection.execute("PRAGMA foreign_key_check").fetchall()
        foreign_keys = connection.execute("PRAGMA foreign_keys").fetchone()[0]

    assert "metadata_status" not in columns
    assert media is not None and media["movie_id"] == movie_id
    assert watch is not None and watch["movie_id"] == movie_id
    assert violations == []
    assert foreign_keys == 1
