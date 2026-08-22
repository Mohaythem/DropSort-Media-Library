from __future__ import annotations

from datetime import datetime, timezone
from hashlib import sha256
import json
from pathlib import Path
import shutil
import sqlite3
import sys

from dropsort.application.bootstrap.desktop import (
    create_import_actions,
    create_operation_history_actions,
    create_organization_actions,
    create_reconciliation_actions,
)
from dropsort.application.configuration import SessionTmdbCredentials
from dropsort.application.errors import RelinkValidationCode, RelinkValidationError
from dropsort.core.operations import OperationState
from dropsort.database import Database, MigrationRunner
from dropsort.database.repositories import (
    FileOperationRepository,
    MediaFileRepository,
    SqliteMovieRepository,
)
from dropsort.library.movies import (
    MediaFileStatus,
    MovieCatalogData,
    VerifiedMediaFileFacts,
)
from dropsort.library.playback import WindowsLocalMediaActions
from dropsort.media.discovery import DiscoveryCancelled, ReadOnlyMediaScanner


NOW = datetime(2026, 8, 13, 12, 0, tzinfo=timezone.utc)


class Cancellation:
    def __init__(self) -> None:
        self.cancelled = False

    def is_cancelled(self) -> bool:
        return self.cancelled


def require(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)


def digest(path: Path) -> str:
    return sha256(path.read_bytes()).hexdigest()


def journal_count(database: Database) -> int:
    with database.connection() as connection:
        return int(connection.execute("SELECT COUNT(*) FROM file_operations").fetchone()[0])


def main(root: Path) -> dict[str, object]:
    root = root.absolute()
    require(not root.exists(), "verification root must not already exist")
    root.mkdir(parents=True)
    source_root = root / "source"
    destination_root = root / "destination"
    scan_root = root / "scan unicode Ω"
    source_root.mkdir()
    destination_root.mkdir()
    scan_root.mkdir()
    database = Database(root / "verification.db")
    MigrationRunner(database).migrate()
    media_files = MediaFileRepository(database)
    movies = SqliteMovieRepository(database)
    movie = movies.create(
        MovieCatalogData(
            "tmdb", "phase6b-1", "Phase Six Movie", "Phase Six Movie", 2026,
            None, ("Drama",), 90, 8.0, None,
        ),
        now=NOW,
    )

    scan_files = tuple(scan_root / f"Scan.Movie.{2020 + index}.mkv" for index in range(6))
    for index, path in enumerate(scan_files):
        path.write_bytes(f"scan-{index}".encode())
    scan_hashes = {str(path): digest(path) for path in scan_files}
    cancellation = Cancellation()
    cancelled_progress = None

    def cancel_after_progress(progress) -> None:
        nonlocal cancelled_progress
        cancelled_progress = progress
        if progress.entries_seen >= 1:
            cancellation.cancelled = True

    try:
        ReadOnlyMediaScanner(progress_interval=1).scan(
            scan_root, progress=cancel_after_progress, cancellation=cancellation
        )
        raise AssertionError("scan cancellation was not observed")
    except DiscoveryCancelled:
        pass
    discoveries = ReadOnlyMediaScanner(progress_interval=1).scan(scan_root)
    require(len(discoveries) == 6, "restart scan did not return all fixtures")
    require({str(path): digest(path) for path in scan_files} == scan_hashes, "scan changed media")
    require(journal_count(database) == 0, "scan created journal rows")

    source = source_root / "Phase.Six.Movie.2026.mkv"
    source.write_bytes(b"phase-six-organize-content")
    original_hash = digest(source)
    media = media_files.add(
        VerifiedMediaFileFacts(source.absolute(), source.stat().st_size, ".mkv", "1080p", "x264", "BluRay", NOW),
        movie.id,
    )
    organization = create_organization_actions(database)
    preview = organization.prepare_organization(media.id, destination_root, source.name)
    require(source.exists() and journal_count(database) == 0, "organize preview mutated state")
    organized = organization.confirm_organization(preview.preview_id)
    destination = destination_root / source.name
    require(destination.is_file() and not source.exists(), "organize filesystem result incorrect")
    require(digest(destination) == original_hash, "organize changed content")
    require(media_files.get_path(media.id) == destination, "organize catalog path incorrect")
    require(FileOperationRepository(database).get(organized.operation_id).state is OperationState.COMMITTED, "organize not committed")

    history = create_operation_history_actions(database)
    history_items = history.list_operation_history()
    require(history_items and history_items[0].operation_id == organized.operation_id, "history missing organize")
    undo_preview = history.prepare_undo(organized.operation_id)
    require(destination.exists() and not source.exists() and journal_count(database) == 1, "undo preview mutated")
    undo = history.confirm_undo(undo_preview.preview_id)
    require(source.is_file() and not destination.exists(), "undo did not restore original path")
    require(digest(source) == original_hash, "undo changed content")
    require(media_files.get_path(media.id) == source, "undo catalog path incorrect")
    require(journal_count(database) == 2, "undo did not create one reverse journal row")
    reverse = FileOperationRepository(database).get(undo.reverse_operation_id)
    require(reverse.reverses_operation_id == organized.operation_id, "undo reverse link missing")
    require(FileOperationRepository(database).get(organized.operation_id).state is OperationState.COMMITTED, "undo rewrote history")

    played: list[str] = []
    explorer: list[tuple[str, ...]] = []
    local = WindowsLocalMediaActions(start_file=played.append, explorer_launcher=explorer.append)
    local.play(source)
    local.open_folder(source)
    require(played == [str(source)], "play boundary did not receive exact path")
    require(explorer == [("explorer.exe", "/select,", str(source))], "open-folder arguments unsafe")
    require(digest(source) == original_hash, "local actions changed media")

    relink_old = source_root / "Relink.Movie.2026.mkv"
    relink_new = source_root / "Relink Movie 2026.mkv"
    relink_old.write_bytes(b"relink-content")
    relink_movie = movies.create(
        MovieCatalogData("tmdb", "phase6b-2", "Relink Movie", None, 2026, None, (), None, None, None),
        now=NOW,
    )
    relinked_row = media_files.add(
        VerifiedMediaFileFacts(relink_old.absolute(), relink_old.stat().st_size, ".mkv", "1080p", "x264", "BluRay", NOW),
        relink_movie.id,
    )
    relink_id = relinked_row.id
    relink_hash = digest(relink_old)
    relink_old.rename(relink_new)
    before_relink_journal = journal_count(database)
    reconciliation = create_reconciliation_actions(database)
    checked = reconciliation.reconcile_library_files()
    require(checked.missing >= 1, "reconciliation did not detect missing file")
    require(media_files.get_by_id(relink_id).status is MediaFileStatus.MISSING, "missing status not persisted")
    relink_preview = reconciliation.prepare_media_relink(relink_id, relink_new.absolute())
    relink_result = reconciliation.confirm_media_relink(relink_preview.preview_id)
    after_relink = media_files.get_by_id(relink_id)
    require(relink_result.id == relink_id, "relink changed media ID")
    require(after_relink.movie_id == relink_movie.id, "relink changed movie association")
    require((after_relink.resolution, after_relink.codec, after_relink.source) == ("1080p", "x264", "BluRay"), "relink changed technical metadata")
    require(after_relink.current_path == relink_new and after_relink.status is MediaFileStatus.PRESENT, "relink catalog result incorrect")
    require(digest(relink_new) == relink_hash and not relink_old.exists(), "relink mutated physical files")
    require(journal_count(database) == before_relink_journal, "relink created journal row")

    missing_for_block = source_root / "Relink.Movie.2026.missing.mkv"
    blocked_row = media_files.add(
        VerifiedMediaFileFacts(missing_for_block.absolute(), len(b"relink-content"), ".mkv", None, None, None, NOW),
        relink_movie.id,
    )
    media_files.mark_missing(blocked_row.id)
    wrong = source_root / "Relink.Movie.2026.wrong.mkv"
    wrong.write_bytes(b"wrong-size")
    try:
        reconciliation.prepare_media_relink(blocked_row.id, wrong.absolute())
        raise AssertionError("wrong-size candidate was accepted")
    except RelinkValidationError as error:
        require(error.code is RelinkValidationCode.SIZE_MISMATCH, "wrong candidate returned unexpected code")
    try:
        reconciliation.prepare_media_relink(blocked_row.id, relink_new.absolute())
        raise AssertionError("catalog-owned candidate was accepted")
    except RelinkValidationError as error:
        require(error.code is RelinkValidationCode.CATALOG_CONFLICT, "catalog conflict returned unexpected code")

    offline_file = scan_root / "Offline.Movie.2026.mkv"
    offline_file.write_bytes(b"offline")
    credentials = SessionTmdbCredentials(environment={})
    require(not credentials.status().configured, "clean profile unexpectedly has TMDB credential")
    offline_session = create_import_actions(database, credentials=credentials).prepare_import_review(scan_root, True)
    require(any(item.status.value == "METADATA_UNAVAILABLE" for item in offline_session.items), "offline metadata fallback not controlled")

    existing_copy = root / "existing-copy.db"
    shutil.copy2(database.path, existing_copy)
    before_copy_hash = digest(existing_copy)
    MigrationRunner(Database(existing_copy)).migrate()
    with Database(existing_copy).connection() as connection:
        require(connection.execute("SELECT COUNT(*) FROM movies").fetchone()[0] >= 2, "existing DB data unavailable")
        require(connection.execute("SELECT MAX(version) FROM schema_migrations").fetchone()[0] == 3, "existing DB migration version incorrect")
    require(digest(existing_copy) == before_copy_hash, "current existing DB was unexpectedly rewritten")

    invalid = root / "invalid.db"
    invalid.write_bytes(b"not a sqlite database")
    invalid_before = invalid.read_bytes()
    try:
        MigrationRunner(Database(invalid)).migrate()
        raise AssertionError("invalid database was accepted")
    except sqlite3.DatabaseError:
        pass
    require(invalid.read_bytes() == invalid_before, "invalid DB was replaced")

    return {
        "scan_cancelled": cancellation.cancelled,
        "scan_cancel_progress_seen": cancelled_progress is not None,
        "scan_restart_count": len(discoveries),
        "scan_zero_mutation": True,
        "organize_preview_zero_mutation": True,
        "organize_committed": True,
        "undo_reverse_operation": True,
        "immutable_history": True,
        "play_open_folder_safe": True,
        "missing_detected": True,
        "relink_catalog_only": True,
        "wrong_size_blocked": True,
        "catalog_conflict_blocked": True,
        "offline_controlled": True,
        "existing_database_preserved": True,
        "invalid_database_preserved": True,
        "final_journal_rows": journal_count(database),
    }


if __name__ == "__main__":
    if len(sys.argv) != 2:
        raise SystemExit("usage: Phase6BVerifier.exe <new-contained-sandbox-path>")
    print(json.dumps(main(Path(sys.argv[1])), sort_keys=True))
