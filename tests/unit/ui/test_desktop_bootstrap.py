from __future__ import annotations

from pathlib import Path
import subprocess
import sys
import threading

import pytest
from PySide6.QtWidgets import QApplication

from dropsort.application.bootstrap.desktop import (
    create_import_actions,
    create_library_actions,
    create_main_window,
    create_organization_actions,
    create_operation_history_actions,
    create_reconciliation_actions,
    default_database_path,
    default_poster_cache_path,
    run_desktop_app,
)
from dropsort.application.configuration import MetadataSettings, SessionTmdbCredentials
from dropsort.application.dto.movie_import import ImportProposalStatus
from dropsort.application.errors import MovieNotFoundError
from dropsort.database import Database, MigrationRunner
from dropsort.database.repositories import FileOperationRepository, MediaFileRepository
from dropsort.metadata.contracts import MovieCandidate, MovieMetadata, MovieSearchQuery
from dropsort.metadata.providers.session_tmdb import SessionConfiguredTmdbProvider
from dropsort.ui.main_window.window import MainWindow
from dropsort.library.movies import MediaFileStatus


def test_bootstrap_composes_read_only_ui_actions_from_local_catalog(tmp_path: Path) -> None:
    database = Database(tmp_path / "library.db")
    MigrationRunner(database).migrate()

    actions = create_library_actions(database)

    assert actions.list_movies() == ()
    with pytest.raises(MovieNotFoundError):
        actions.get_movie_details(999)


def test_bootstrap_builds_window_without_leaking_sqlite_into_widgets(
    qapp: QApplication,
    tmp_path: Path,
) -> None:
    window = create_main_window(tmp_path / "desktop.db", load_on_show=False)

    assert isinstance(window, MainWindow)
    assert window.current_section == "library"


def test_desktop_bootstrap_imports_in_a_fresh_interpreter_without_circular_import() -> None:
    result = subprocess.run(
        [
            sys.executable,
            "-c",
            "from dropsort.application.bootstrap.desktop import create_main_window",
        ],
        cwd=Path(__file__).parents[3],
        capture_output=True,
        text=True,
        check=False,
    )

    assert result.returncode == 0, result.stderr


def test_bootstrap_organization_preview_is_read_only_until_explicit_confirmation(
    tmp_path: Path,
) -> None:
    source_root = tmp_path / "source"
    destination_root = tmp_path / "destination"
    source_root.mkdir()
    destination_root.mkdir()
    source = source_root / "Movie.mkv"
    source.write_bytes(b"movie")
    database = Database(tmp_path / "organization.db")
    MigrationRunner(database).migrate()
    media_files = MediaFileRepository(database)
    media_file_id = media_files.create(source, 5)
    actions = create_organization_actions(database)

    preview = actions.prepare_organization(media_file_id, destination_root, source.name)

    assert FileOperationRepository(database).list_nonterminal() == []
    assert source.exists()
    result = actions.confirm_organization(preview.preview_id)
    assert FileOperationRepository(database).get(result.operation_id).state.value == "COMMITTED"
    assert media_files.get_path(media_file_id) == destination_root / source.name


def test_bootstrap_composes_operation_history_and_explicit_undo(tmp_path: Path) -> None:
    source_root = tmp_path / "source"
    destination_root = tmp_path / "destination"
    source_root.mkdir()
    destination_root.mkdir()
    source = source_root / "Movie.mkv"
    source.write_bytes(b"movie")
    database = Database(tmp_path / "history.db")
    MigrationRunner(database).migrate()
    media_files = MediaFileRepository(database)
    media_file_id = media_files.create(source, 5)
    organization = create_organization_actions(database)
    organized = organization.confirm_organization(
        organization.prepare_organization(media_file_id, destination_root, source.name).preview_id
    )
    history = create_operation_history_actions(database)

    items = history.list_operation_history()
    details = history.get_operation_details(organized.operation_id)
    preview = history.prepare_undo(organized.operation_id)
    result = history.confirm_undo(preview.preview_id)

    assert items[0].operation_id == organized.operation_id
    assert details.current_catalog_path == str(destination_root / source.name)
    assert result.original_operation_id == organized.operation_id
    assert media_files.get_path(media_file_id) == source
    history.discard_undo_preview("missing-preview")


def test_bootstrap_history_exposes_recovery_inspection_and_action(tmp_path: Path) -> None:
    source_root = tmp_path / "source"
    destination_root = tmp_path / "destination"
    source_root.mkdir()
    destination_root.mkdir()
    source = source_root / "Movie.mkv"
    source.write_bytes(b"movie")
    database = Database(tmp_path / "recovery.db")
    MigrationRunner(database).migrate()
    media_files = MediaFileRepository(database)
    media_id = media_files.create(source, 5)
    operations = FileOperationRepository(database)
    from dropsort.core.operations import FileOperationService, OperationState
    from dropsort.core.safety import PathPolicy
    from dropsort.database.repositories import SqliteOperationStore

    store = SqliteOperationStore(database, operations, media_files)
    service = FileOperationService(PathPolicy((source_root, destination_root)), store)
    plan = service.plan_move(source, destination_root / source.name, media_file_id=media_id)
    operations.transition(plan.operation_id, OperationState.EXECUTING)
    actions = create_operation_history_actions(database)

    assessment = actions.inspect_recovery(plan.operation_id)
    result = actions.attempt_recovery(plan.operation_id)

    assert assessment.action_available is True
    assert result.state.value == "FAILED"


def test_bootstrap_can_share_one_operation_coordinator_between_organize_undo_and_recovery(
    tmp_path: Path,
) -> None:
    database = Database(tmp_path / "coordinator.db")
    MigrationRunner(database).migrate()
    coordinator = threading.Lock()
    organization = create_organization_actions(database, execution_lock=coordinator)
    history = create_operation_history_actions(database, execution_lock=coordinator)

    assert organization._organize._execution_lock is coordinator
    assert history._undo._execution_lock is coordinator
    assert history._recovery._lock is coordinator

    reconciliation = create_reconciliation_actions(
        database,
        confirmation_lock=coordinator,
    )
    assert reconciliation._relink._lock is coordinator


def test_missing_tmdb_credential_becomes_controlled_metadata_proposal(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.delenv("DROPSORT_TMDB_READ_ACCESS_TOKEN", raising=False)
    root = tmp_path / "movies"
    root.mkdir()
    (root / "Interstellar.2014.mkv").write_bytes(b"movie")
    database = Database(tmp_path / "missing-token.db")
    MigrationRunner(database).migrate()

    session = create_import_actions(database).prepare_import_review(root, True)

    assert session.items[0].status is ImportProposalStatus.METADATA_UNAVAILABLE


def test_default_database_path_uses_local_app_data_or_user_profile_fallback(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    monkeypatch.setenv("LOCALAPPDATA", str(tmp_path))
    assert default_database_path() == tmp_path / "DropSort" / "dropsort.db"

    monkeypatch.delenv("LOCALAPPDATA")
    monkeypatch.setattr("dropsort.application.runtime.paths.Path.home", lambda: tmp_path / "profile")
    assert default_database_path() == tmp_path / "profile" / "AppData" / "Local" / "DropSort" / "dropsort.db"


def test_default_poster_cache_path_is_centralized_under_local_app_data(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    monkeypatch.setenv("LOCALAPPDATA", str(tmp_path))

    assert default_poster_cache_path() == tmp_path / "DropSort" / "poster-cache"


def test_session_token_enables_later_metadata_requests_without_restart(
    tmp_path: Path,
) -> None:
    class FakeTmdb:
        provider_name = "tmdb"

        def search_movies(self, query: MovieSearchQuery) -> tuple[MovieCandidate, ...]:
            return (
                MovieCandidate(
                    provider="tmdb",
                    external_id="157336",
                    title="Interstellar",
                    original_title="Interstellar",
                    year=2014,
                    overview=None,
                    rating=None,
                    poster_reference=None,
                ),
            )

        def get_movie(self, external_id: str) -> MovieMetadata:
            raise AssertionError("detail lookup is not part of proposal generation")

    root = tmp_path / "movies"
    root.mkdir()
    (root / "Interstellar.2014.mkv").write_bytes(b"movie")
    database = Database(tmp_path / "runtime-token.db")
    MigrationRunner(database).migrate()
    credentials = SessionTmdbCredentials(environment={})
    constructed_with: list[str] = []
    provider = SessionConfiguredTmdbProvider(
        credentials,
        provider_factory=lambda token: (
            constructed_with.append(token),
            FakeTmdb(),
        )[1],
    )
    actions = create_import_actions(database, provider=provider)

    missing = actions.prepare_import_review(root, True)
    MetadataSettings(credentials).apply_tmdb_session_token(
        "session-token-value-123456789012345"
    )
    configured = actions.prepare_import_review(root, True)

    assert missing.items[0].status is ImportProposalStatus.METADATA_UNAVAILABLE
    assert configured.items[0].status is ImportProposalStatus.MATCH_PROPOSED
    assert constructed_with == ["session-token-value-123456789012345"]


def test_bootstrap_reconciliation_adapter_checks_and_relinks_catalog_only(
    tmp_path: Path,
) -> None:
    database = Database(tmp_path / "reconcile.db")
    MigrationRunner(database).migrate()
    repository = MediaFileRepository(database)
    old = (tmp_path / "Old.Movie.2020.mkv").absolute()
    media_id = repository.create(old, 5)
    repository.mark_missing(media_id)
    actions = create_reconciliation_actions(database)

    checked = actions.reconcile_library_files()

    assert checked.missing == 1
    assert repository.get_by_id(media_id).status is MediaFileStatus.MISSING  # type: ignore[union-attr]
    with pytest.raises(Exception):
        actions.prepare_media_relink(media_id, Path("relative.mkv"))
    actions.discard_media_relink_preview("absent")


def test_first_run_from_different_cwd_creates_and_migrates_database(
    qapp: QApplication,
    tmp_path: Path,
    monkeypatch,
) -> None:
    launch_cwd = tmp_path / "elsewhere"
    launch_cwd.mkdir()
    monkeypatch.chdir(launch_cwd)
    database_path = tmp_path / "profile" / "DropSort" / "dropsort.db"

    window = create_main_window(database_path, load_on_show=False)

    assert database_path.is_file()
    with Database(database_path).connection() as connection:
        assert connection.execute("SELECT COUNT(*) FROM schema_migrations").fetchone()[0] >= 1
    window.close()
    window.wait_for_pending_tasks()


def test_desktop_startup_database_failure_is_controlled_and_preserves_path(
    qapp: QApplication,
    tmp_path: Path,
    monkeypatch,
) -> None:
    blocked = tmp_path / "database-as-directory"
    blocked.mkdir()
    shown: list[tuple[str, str]] = []
    monkeypatch.setattr(
        "dropsort.application.bootstrap.desktop.QMessageBox.critical",
        lambda _parent, title, message: shown.append((title, message)),
    )

    result = run_desktop_app(blocked)

    assert result == 1
    assert blocked.is_dir()
    assert shown and "preserved" in shown[0][1]


def test_secondary_instance_exits_before_database_or_window_bootstrap(
    qapp: QApplication,
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    class SecondaryCoordinator:
        def __init__(self, *args, **kwargs) -> None:
            self.activation_requested = type("Signal", (), {"connect": lambda *_: None})()

        def acquire(self) -> bool:
            return False

        def close(self) -> None:
            raise AssertionError("secondary instance must not close as primary")

    monkeypatch.setattr(
        "dropsort.application.bootstrap.desktop.SingleInstanceCoordinator",
        SecondaryCoordinator,
    )
    monkeypatch.setattr(
        "dropsort.application.bootstrap.desktop.create_main_window",
        lambda *args, **kwargs: (_ for _ in ()).throw(
            AssertionError("secondary instance must not bootstrap a window")
        ),
    )

    assert run_desktop_app(tmp_path / "secondary.db") == 0
