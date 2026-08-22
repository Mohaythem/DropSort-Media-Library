from __future__ import annotations

import errno
from pathlib import Path
import sqlite3

import pytest

from dropsort.application.dto.organization import OrganizationOperation
from dropsort.application.errors import (
    OrganizationAlreadyConfirmedError,
    OrganizationExecutionError,
    OrganizationPreviewStaleError,
    OrganizationPreviewNotFoundError,
    OrganizationRecoveryRequiredError,
    OrganizationValidationError,
    OrganizationValidationCode,
)
from dropsort.application.use_cases.organize_media_file import OrganizeMediaFile
from dropsort.core.file_engine.transfer import SafeTransferEngine
from dropsort.core.operations import OperationState
from dropsort.database import Database, MigrationRunner
from dropsort.database.repositories import (
    FileOperationRepository,
    MediaFileRepository,
    SqliteOperationStore,
)


def _build_actions(
    tmp_path: Path,
    *,
    engine: SafeTransferEngine | None = None,
    media_files: MediaFileRepository | None = None,
) -> tuple[OrganizeMediaFile, MediaFileRepository, FileOperationRepository, Path, Path]:
    source_root = tmp_path / "source"
    destination_root = tmp_path / "destination"
    source_root.mkdir()
    destination_root.mkdir()
    database = Database(tmp_path / "library.db")
    MigrationRunner(database).migrate()
    catalog = media_files or MediaFileRepository(database)
    operations = FileOperationRepository(database)
    store = SqliteOperationStore(database, operations, catalog)
    return OrganizeMediaFile(catalog, store, engine=engine), catalog, operations, source_root, destination_root


def _operation_count(database: Database) -> int:
    with database.connection() as connection:
        return int(connection.execute("SELECT COUNT(*) FROM file_operations").fetchone()[0])


def test_preview_is_exact_and_performs_zero_mutation(tmp_path: Path, media_bytes: bytes) -> None:
    actions, media_files, _operations, source_root, destination_root = _build_actions(tmp_path)
    source = source_root / "Phase5A Test.mkv"
    source.write_bytes(media_bytes)
    media_file_id = media_files.create(source, len(media_bytes))

    preview = actions.prepare_preview(
        media_file_id,
        destination_root,
        "Phase5A Test.mkv",
    )

    assert preview.source_path == str(source.resolve())
    assert preview.destination_path == str((destination_root / source.name).resolve())
    assert preview.operation is OrganizationOperation.MOVE
    assert preview.file_size == len(media_bytes)
    assert preview.same_volume is True
    assert source.read_bytes() == media_bytes
    assert not (destination_root / source.name).exists()
    assert media_files.get_path(media_file_id) == source
    assert _operation_count(media_files.database) == 0


def test_invalid_media_identity_missing_catalog_record_and_unknown_preview_are_controlled(
    tmp_path: Path,
) -> None:
    actions, media_files, _operations, _source_root, destination_root = _build_actions(tmp_path)

    for media_file_id in (0, True, 999):
        with pytest.raises(OrganizationValidationError):
            actions.prepare_preview(media_file_id, destination_root, "Movie.mkv")
    with pytest.raises(OrganizationPreviewNotFoundError):
        actions.confirm("unknown-preview")
    assert _operation_count(media_files.database) == 0


def test_preview_classifies_rename_and_move_plus_rename(tmp_path: Path) -> None:
    actions, media_files, _operations, source_root, destination_root = _build_actions(tmp_path)
    source = source_root / "Original.mkv"
    source.write_bytes(b"movie")
    media_file_id = media_files.create(source, 5)

    rename = actions.prepare_preview(media_file_id, source_root, "Renamed.mkv")
    moved_and_renamed = actions.prepare_preview(media_file_id, destination_root, "Renamed.mkv")

    assert rename.operation is OrganizationOperation.RENAME
    assert moved_and_renamed.operation is OrganizationOperation.MOVE_AND_RENAME
    assert _operation_count(media_files.database) == 0


@pytest.mark.parametrize(
    "filename",
    (
        "",
        "../escape.mkv",
        r"nested\escape.mkv",
        "CON.mkv",
        "trailing-space .mkv ",
        "different.mp4",
        "bad?.mkv",
        f"{'x' * 252}.mkv",
        "trailing-dot.mkv.",
        "CON .mkv",
    ),
)
def test_preview_rejects_unsafe_or_extension_changing_filename(
    tmp_path: Path,
    filename: str,
) -> None:
    actions, media_files, _operations, source_root, destination_root = _build_actions(tmp_path)
    source = source_root / "Movie.mkv"
    source.write_bytes(b"movie")
    media_file_id = media_files.create(source, 5)

    with pytest.raises(OrganizationValidationError):
        actions.prepare_preview(media_file_id, destination_root, filename)

    assert source.exists()
    assert _operation_count(media_files.database) == 0


def test_explicit_confirmation_commits_one_journaled_move_and_catalog_path(
    tmp_path: Path,
    media_bytes: bytes,
) -> None:
    actions, media_files, operations, source_root, destination_root = _build_actions(tmp_path)
    source = source_root / "Movie.mkv"
    destination = destination_root / "Movie.mkv"
    source.write_bytes(media_bytes)
    media_file_id = media_files.create(source, len(media_bytes))
    preview = actions.prepare_preview(media_file_id, destination_root, destination.name)

    result = actions.confirm(preview.preview_id)

    assert result.destination_path == str(destination.resolve())
    assert result.strategy == "hardlink-unlink"
    assert not source.exists()
    assert destination.read_bytes() == media_bytes
    assert media_files.get_path(media_file_id) == destination
    record = operations.get(result.operation_id)
    assert record.state is OperationState.COMMITTED
    assert record.media_file_id == media_file_id
    assert _operation_count(media_files.database) == 1
    with pytest.raises(OrganizationAlreadyConfirmedError):
        actions.confirm(preview.preview_id)
    assert _operation_count(media_files.database) == 1


def test_source_replacement_after_preview_is_rejected_before_journaling(tmp_path: Path) -> None:
    actions, media_files, _operations, source_root, destination_root = _build_actions(tmp_path)
    source = source_root / "Movie.mkv"
    source.write_bytes(b"original")
    media_file_id = media_files.create(source, len(b"original"))
    preview = actions.prepare_preview(media_file_id, destination_root, source.name)
    source.write_bytes(b"replacement with different identity")

    with pytest.raises(OrganizationPreviewStaleError):
        actions.confirm(preview.preview_id)

    assert source.read_bytes() == b"replacement with different identity"
    assert not (destination_root / source.name).exists()
    assert media_files.get_path(media_file_id) == source
    assert _operation_count(media_files.database) == 0


def test_destination_created_after_preview_is_rejected_without_overwrite(tmp_path: Path) -> None:
    actions, media_files, _operations, source_root, destination_root = _build_actions(tmp_path)
    source = source_root / "Movie.mkv"
    destination = destination_root / source.name
    source.write_bytes(b"source")
    media_file_id = media_files.create(source, 6)
    preview = actions.prepare_preview(media_file_id, destination_root, source.name)
    destination.write_bytes(b"existing")

    with pytest.raises(OrganizationPreviewStaleError):
        actions.confirm(preview.preview_id)

    assert source.read_bytes() == b"source"
    assert destination.read_bytes() == b"existing"
    assert _operation_count(media_files.database) == 0


def test_preview_preserves_precise_collision_and_same_file_reasons(tmp_path: Path) -> None:
    actions, media_files, _operations, source_root, destination_root = _build_actions(tmp_path)
    source = source_root / "Movie.MKV"
    source.write_bytes(b"source")
    media_file_id = media_files.create(source, 6)
    (destination_root / "existing.mkv").write_bytes(b"existing")
    (destination_root / "Other.MKV").write_bytes(b"other")

    cases = (
        (destination_root, "existing.mkv", OrganizationValidationCode.DESTINATION_EXISTS),
        (destination_root, "other.mkv", OrganizationValidationCode.CASE_COLLISION),
        (source_root, "Movie.MKV", OrganizationValidationCode.SAME_FILE),
    )
    for root, filename, expected_code in cases:
        with pytest.raises(OrganizationValidationError) as caught:
            actions.prepare_preview(media_file_id, root, filename)
        assert caught.value.code is expected_code

    assert _operation_count(media_files.database) == 0


def test_source_missing_preview_has_controlled_reason_and_no_journal(tmp_path: Path) -> None:
    actions, media_files, _operations, source_root, destination_root = _build_actions(tmp_path)
    source = source_root / "Movie.mkv"
    source.write_bytes(b"movie")
    media_file_id = media_files.create(source, 5)
    source.unlink()

    with pytest.raises(OrganizationValidationError) as caught:
        actions.prepare_preview(media_file_id, destination_root, source.name)

    assert caught.value.code is OrganizationValidationCode.SOURCE_MISSING
    assert _operation_count(media_files.database) == 0


def test_rename_and_move_plus_rename_execute_through_the_same_journaled_pipeline(
    tmp_path: Path,
) -> None:
    actions, media_files, operations, source_root, destination_root = _build_actions(tmp_path)
    renamed_source = source_root / "Rename Me.mkv"
    renamed_source.write_bytes(b"rename")
    rename_id = media_files.create(renamed_source, 6)
    rename_preview = actions.prepare_preview(rename_id, source_root, "Renamed.mkv")
    rename_result = actions.confirm(rename_preview.preview_id)

    move_source = source_root / "Move Me.mkv"
    move_source.write_bytes(b"move-and-rename")
    move_id = media_files.create(move_source, len(b"move-and-rename"))
    move_preview = actions.prepare_preview(move_id, destination_root, "Moved.mkv")
    move_result = actions.confirm(move_preview.preview_id)

    assert operations.get(rename_result.operation_id).operation_type.value == "RENAME"
    assert (source_root / "Renamed.mkv").read_bytes() == b"rename"
    assert operations.get(move_result.operation_id).operation_type.value == "MOVE"
    assert (destination_root / "Moved.mkv").read_bytes() == b"move-and-rename"
    assert media_files.get_path(rename_id) == source_root / "Renamed.mkv"
    assert media_files.get_path(move_id) == destination_root / "Moved.mkv"


def test_old_preview_cannot_move_path_after_catalog_changes(tmp_path: Path) -> None:
    actions, media_files, _operations, source_root, destination_root = _build_actions(tmp_path)
    source = source_root / "Movie.mkv"
    source.write_bytes(b"movie")
    media_file_id = media_files.create(source, 5)
    preview = actions.prepare_preview(media_file_id, destination_root, source.name)
    new_catalog_path = source_root / "Externally Relinked.mkv"
    with media_files.database.transaction() as connection:
        media_files.update_path(media_file_id, new_catalog_path, conn=connection)

    with pytest.raises(OrganizationPreviewStaleError):
        actions.confirm(preview.preview_id)

    assert source.exists()
    assert not (destination_root / source.name).exists()
    assert _operation_count(media_files.database) == 0


def test_catalog_destination_conflict_is_rejected_before_and_after_preview(
    tmp_path: Path,
) -> None:
    actions, media_files, _operations, source_root, destination_root = _build_actions(tmp_path)
    source = source_root / "Movie.mkv"
    source.write_bytes(b"movie")
    media_file_id = media_files.create(source, 5)
    destination = destination_root / source.name
    conflicting_id = media_files.create(destination, 0)

    with pytest.raises(OrganizationValidationError) as caught:
        actions.prepare_preview(media_file_id, destination_root, source.name)
    assert caught.value.code is OrganizationValidationCode.DESTINATION_EXISTS
    assert _operation_count(media_files.database) == 0

    with media_files.database.transaction() as connection:
        connection.execute("DELETE FROM media_files WHERE id = ?", (conflicting_id,))
    preview = actions.prepare_preview(media_file_id, destination_root, source.name)
    media_files.create(destination, 0)

    with pytest.raises(OrganizationPreviewStaleError):
        actions.confirm(preview.preview_id)
    assert source.exists()
    assert not destination.exists()
    assert _operation_count(media_files.database) == 0


def test_discarded_preview_cannot_authorize_or_create_a_journal(tmp_path: Path) -> None:
    actions, media_files, _operations, source_root, destination_root = _build_actions(tmp_path)
    source = source_root / "Movie.mkv"
    source.write_bytes(b"movie")
    media_file_id = media_files.create(source, 5)
    preview = actions.prepare_preview(media_file_id, destination_root, source.name)

    actions.discard_preview(preview.preview_id)

    with pytest.raises(OrganizationPreviewNotFoundError):
        actions.confirm(preview.preview_id)
    assert source.exists()
    assert _operation_count(media_files.database) == 0


def test_preview_and_consumed_token_registries_are_bounded(tmp_path: Path) -> None:
    actions, media_files, _operations, source_root, destination_root = _build_actions(tmp_path)
    actions._MAX_PREPARED_PREVIEWS = 2
    source = source_root / "Movie.mkv"
    source.write_bytes(b"movie")
    media_file_id = media_files.create(source, 5)

    previews = [
        actions.prepare_preview(media_file_id, destination_root, f"Movie-{number}.mkv")
        for number in range(3)
    ]
    with pytest.raises(OrganizationPreviewNotFoundError):
        actions.confirm(previews[0].preview_id)
    actions.discard_preview(previews[1].preview_id)
    first_result = actions.confirm(previews[2].preview_id)
    assert first_result.destination_path.endswith("Movie-2.mkv")

    for number in range(2):
        current_path = media_files.get_path(media_file_id)
        preview = actions.prepare_preview(
            media_file_id,
            current_path.parent,
            f"Renamed-{number}.mkv",
        )
        actions.confirm(preview.preview_id)
    assert len(actions._consumed) == 2


def test_journal_creation_failure_is_controlled_and_never_mutates(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    actions, media_files, operations, source_root, destination_root = _build_actions(tmp_path)
    source = source_root / "Movie.mkv"
    source.write_bytes(b"movie")
    media_file_id = media_files.create(source, 5)
    preview = actions.prepare_preview(media_file_id, destination_root, source.name)
    monkeypatch.setattr(
        operations,
        "create",
        lambda *args, **kwargs: (_ for _ in ()).throw(sqlite3.OperationalError("db down")),
    )

    with pytest.raises(OrganizationExecutionError):
        actions.confirm(preview.preview_id)

    assert source.exists()
    assert not (destination_root / source.name).exists()
    assert _operation_count(media_files.database) == 0


def test_two_previews_for_same_file_cannot_create_two_physical_operations(tmp_path: Path) -> None:
    actions, media_files, _operations, source_root, destination_root = _build_actions(tmp_path)
    source = source_root / "Movie.mkv"
    source.write_bytes(b"movie")
    media_file_id = media_files.create(source, 5)
    first = actions.prepare_preview(media_file_id, destination_root, "Movie.mkv")
    second = actions.prepare_preview(media_file_id, destination_root, "Movie.mkv")

    actions.confirm(first.preview_id)
    with pytest.raises(OrganizationPreviewStaleError):
        actions.confirm(second.preview_id)

    assert _operation_count(media_files.database) == 1


def test_permission_failure_keeps_source_and_catalog_authoritative(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    engine = SafeTransferEngine()
    monkeypatch.setattr(
        engine,
        "_create_hardlink_destination",
        lambda *args: (_ for _ in ()).throw(PermissionError("denied")),
    )
    actions, media_files, operations, source_root, destination_root = _build_actions(
        tmp_path,
        engine=engine,
    )
    source = source_root / "Movie.mkv"
    source.write_bytes(b"movie")
    media_file_id = media_files.create(source, 5)
    preview = actions.prepare_preview(media_file_id, destination_root, source.name)

    with pytest.raises(OrganizationExecutionError):
        actions.confirm(preview.preview_id)

    assert source.exists()
    assert not (destination_root / source.name).exists()
    assert media_files.get_path(media_file_id) == source
    assert operations.list_nonterminal() == []


def test_organizing_one_version_preserves_movie_association_and_other_version(
    tmp_path: Path,
) -> None:
    actions, media_files, _operations, source_root, destination_root = _build_actions(tmp_path)
    first = source_root / "Movie.1080p.mkv"
    second = source_root / "Movie.2160p.mkv"
    first.write_bytes(b"1080p")
    second.write_bytes(b"2160p")
    first_id = media_files.create(first, 5)
    second_id = media_files.create(second, 5)
    with media_files.database.transaction() as connection:
        movie_id = int(
            connection.execute(
                """
                INSERT INTO movies(
                    provider, external_id, title, genres, date_added,
                    created_at, updated_at
                ) VALUES ('tmdb', '1', 'Movie', '[]', ?, ?, ?)
                """,
                ("2026-01-01T00:00:00+00:00",) * 3,
            ).lastrowid
        )
        connection.execute(
            """
            UPDATE media_files
               SET movie_id = ?, resolution = '1080p', codec = 'x264', source = 'BluRay'
             WHERE id = ?
            """,
            (movie_id, first_id),
        )
        connection.execute(
            "UPDATE media_files SET movie_id = ?, resolution = '2160p' WHERE id = ?",
            (movie_id, second_id),
        )
    preview = actions.prepare_preview(first_id, destination_root, first.name)

    actions.confirm(preview.preview_id)

    with media_files.database.connection() as connection:
        first_row = connection.execute(
            "SELECT * FROM media_files WHERE id = ?", (first_id,)
        ).fetchone()
        second_row = connection.execute(
            "SELECT * FROM media_files WHERE id = ?", (second_id,)
        ).fetchone()
        count = connection.execute("SELECT COUNT(*) FROM media_files").fetchone()[0]
    assert first_row["movie_id"] == movie_id
    assert first_row["resolution"] == "1080p"
    assert first_row["codec"] == "x264"
    assert first_row["source"] == "BluRay"
    assert Path(first_row["current_path"]) == destination_root / first.name
    assert second_row["movie_id"] == movie_id
    assert Path(second_row["current_path"]) == second
    assert count == 2


def test_cross_volume_fallback_uses_existing_safe_copy_pipeline(
    tmp_path: Path,
    media_bytes: bytes,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    engine = SafeTransferEngine()
    monkeypatch.setattr(
        engine,
        "_create_hardlink_destination",
        lambda *args: (_ for _ in ()).throw(OSError(errno.EXDEV, "cross-device")),
    )
    actions, media_files, operations, source_root, destination_root = _build_actions(
        tmp_path,
        engine=engine,
    )
    source = source_root / "Movie.mkv"
    destination = destination_root / source.name
    source.write_bytes(media_bytes)
    media_file_id = media_files.create(source, len(media_bytes))
    preview = actions.prepare_preview(media_file_id, destination_root, source.name)

    result = actions.confirm(preview.preview_id)

    assert result.strategy == "copy-sha256-fsync-finalize-unlink"
    assert destination.read_bytes() == media_bytes
    assert not source.exists()
    assert operations.get(result.operation_id).destination_sha256 is not None


def test_source_removal_failure_surfaces_recovery_required_without_catalog_advance(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    engine = SafeTransferEngine()
    monkeypatch.setattr(
        engine,
        "_remove_source",
        lambda *args: (_ for _ in ()).throw(PermissionError("source locked")),
    )
    actions, media_files, operations, source_root, destination_root = _build_actions(
        tmp_path,
        engine=engine,
    )
    source = source_root / "Movie.mkv"
    destination = destination_root / source.name
    source.write_bytes(b"movie")
    media_file_id = media_files.create(source, 5)
    preview = actions.prepare_preview(media_file_id, destination_root, source.name)

    with pytest.raises(OrganizationRecoveryRequiredError) as caught:
        actions.confirm(preview.preview_id)

    record = operations.get(caught.value.operation_id)
    assert record.state is OperationState.RECOVERY_REQUIRED
    assert source.exists() and destination.exists()
    assert media_files.get_path(media_file_id) == source


def test_database_failure_after_filesystem_success_is_reported_as_recoverable(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    actions, media_files, operations, source_root, destination_root = _build_actions(tmp_path)
    source = source_root / "Movie.mkv"
    destination = destination_root / source.name
    source.write_bytes(b"movie")
    media_file_id = media_files.create(source, 5)
    preview = actions.prepare_preview(media_file_id, destination_root, source.name)

    monkeypatch.setattr(
        media_files,
        "update_path",
        lambda *args, **kwargs: (_ for _ in ()).throw(sqlite3.OperationalError("db down")),
    )
    with pytest.raises(OrganizationRecoveryRequiredError) as caught:
        actions.confirm(preview.preview_id)

    assert not source.exists()
    assert destination.exists()
    assert media_files.get_path(media_file_id) == source
    assert operations.get(caught.value.operation_id).state is OperationState.FS_VERIFIED
