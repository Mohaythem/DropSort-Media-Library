from __future__ import annotations

from dataclasses import dataclass
import logging
from pathlib import Path
import sys
import threading

from PySide6.QtWidgets import QApplication, QMessageBox

from dropsort.application.configuration import (
    MetadataSettings,
    SessionTmdbCredentials,
    UiLanguage,
    UiLanguageSettings,
    UiSidebarSettings,
    UiTheme,
    UiThemeSettings,
)
from dropsort.application.dto.library import MovieDetails, MovieListItem
from dropsort.application.dto.personal_library import PersonalMovieSnapshot
from dropsort.application.dto.catalog import MovieFileIngestionResult
from dropsort.application.dto.import_review import ImportReviewProgress, ImportReviewSession
from dropsort.application.dto.movie_import import ConfirmMovieImportCommand
from dropsort.application.dto.organization import OrganizationPreview, OrganizationResult
from dropsort.application.dto.operation_history import (
    OperationDetails,
    OperationHistoryItem,
    OperationHistoryQuery,
    RecoveryAssessment,
    RecoveryResult,
    UndoPreview,
    UndoResult,
)
from dropsort.application.dto.reconciliation import (
    LibraryReconciliationProgress,
    RelinkPreview,
    RelinkResult,
)
from dropsort.application.dto.library_health import LibraryHealthProgress
from dropsort.application.dto.catalog_maintenance import ClearLibraryDataResult
from dropsort.application.use_cases import (
    ConfirmMovieImport,
    DiscoverMedia,
    GetMovieDetails,
    GetMovieListItem,
    ListMovies,
    PrepareFolderImportReview,
    ProposeMovieImport,
    RegisterLocalMovieFile,
    EnrichMovieMetadata,
    OrganizeMediaFile,
    GetOperationDetails,
    ListOperationHistory,
    RecoverFileOperation,
    SaveOperationHistory,
    UndoFileOperation,
    ImportReviewCancellation,
    ReconcileLibraryFiles,
    ReconciliationCancellation,
    RelinkMediaFile,
    ClearLibraryData,
    CheckLibrary,
    ManualMovieSearch,
    AddToWatchlist,
    ClearPersonalPreference,
    GetPersonalMovieState,
    GetWatchHistory,
    ListPersonalMovies,
    RecordWatch,
    RemoveFromWatchlist,
    RemoveWatchEvent,
    SetPersonalPreference,
)
from dropsort.database import Database, MigrationRunner
from dropsort.database.repositories import (
    MediaFileRepository,
    MetadataCacheRepository,
    SqliteCatalogUnitOfWork,
    SqliteMovieLibraryReadRepository,
    SqliteMovieRepository,
    SqliteOperationStore,
    SqliteOperationJournalReadRepository,
    SqliteLibraryMaintenanceRepository,
    SqliteUiLanguageRepository,
    SqliteUiSidebarRepository,
    SqliteUiThemeRepository,
    SqlitePersonalLibraryRepository,
)
from dropsort.media.discovery import ReadOnlyMediaScanner
from dropsort.media.matcher import MovieMatcher
from dropsort.library.playback import WindowsLocalMediaActions
from dropsort.library.availability import NoFollowMediaFileInspector
from dropsort.library.personal import PersonalLibrarySection, PersonalPreference
from dropsort.application.use_cases._library_mapping import to_list_item
from dropsort.library.movies import MovieSummary
from dropsort.metadata.cache import CachedMetadataProvider
from dropsort.metadata.contracts import (
    MetadataProvider,
)
from dropsort.metadata.providers import SessionConfiguredTmdbProvider
from dropsort.posters import PosterAssetCache, PosterAssetService
from dropsort.posters.providers import TmdbPosterSource
from dropsort.ui.common.theme import apply_theme
from dropsort.ui.common.icon import application_icon
from dropsort.ui.main_window.window import MainWindow
from dropsort.application.runtime import (
    SingleInstanceCoordinator,
    configure_runtime_logging,
    resolve_runtime_paths,
)


@dataclass(frozen=True, slots=True)
class LocalLibraryActions:
    """Concrete composition adapter; widgets consume only its protocol."""

    _list_movies: ListMovies
    _get_movie_item: GetMovieListItem
    _get_movie_details: GetMovieDetails

    def list_movies(self) -> tuple[MovieListItem, ...]:
        return tuple(item for item in self._list_movies.execute() if item.media_file_count > 0)

    def get_movie_item(self, movie_id: int) -> MovieListItem:
        return self._get_movie_item.execute(movie_id)

    def get_movie_details(self, movie_id: int) -> MovieDetails:
        return self._get_movie_details.execute(movie_id)


@dataclass(frozen=True, slots=True)
class LocalPersonalLibraryActions:
    """Composition adapter for personal state; no widget reaches SQLite directly."""

    _state: GetPersonalMovieState
    _history: GetWatchHistory
    _set_preference: SetPersonalPreference
    _clear_preference: ClearPersonalPreference
    _add_watchlist: AddToWatchlist
    _remove_watchlist: RemoveFromWatchlist
    _record_watch: RecordWatch
    _remove_watch: RemoveWatchEvent
    _list_movies: ListPersonalMovies

    def get_personal_snapshot(self, movie_id: int) -> PersonalMovieSnapshot:
        return PersonalMovieSnapshot(self._state.execute(movie_id), self._history.execute(movie_id))

    def set_personal_preference(
        self, movie_id: int, preference: PersonalPreference
    ) -> PersonalMovieSnapshot:
        self._set_preference.execute(movie_id, preference)
        return self.get_personal_snapshot(movie_id)

    def clear_personal_preference(self, movie_id: int) -> PersonalMovieSnapshot:
        self._clear_preference.execute(movie_id)
        return self.get_personal_snapshot(movie_id)

    def add_to_watchlist(self, movie_id: int) -> PersonalMovieSnapshot:
        self._add_watchlist.execute(movie_id)
        return self.get_personal_snapshot(movie_id)

    def remove_from_watchlist(self, movie_id: int) -> PersonalMovieSnapshot:
        self._remove_watchlist.execute(movie_id)
        return self.get_personal_snapshot(movie_id)

    def record_watch(self, movie_id: int, watched_at=None) -> PersonalMovieSnapshot:
        self._record_watch.execute(movie_id, watched_at=watched_at)
        return self.get_personal_snapshot(movie_id)

    def remove_watch_event(self, event_id: int) -> PersonalMovieSnapshot:
        event = self._remove_watch.execute(event_id)
        return self.get_personal_snapshot(event.movie_id)

    def list_personal_movies(
        self, section: PersonalLibrarySection
    ) -> tuple[MovieListItem, ...]:
        return tuple(
            to_list_item(MovieSummary(item.movie, item.media_file_count, item.missing_file_count))
            for item in self._list_movies.execute(section)
        )


@dataclass(frozen=True, slots=True)
class LocalImportActions:
    """Composition adapter for proposal generation and explicit catalog import."""

    _prepare_review: PrepareFolderImportReview
    _confirm_import: ConfirmMovieImport
    _manual_search: ManualMovieSearch

    def prepare_import_review(
        self,
        root: Path,
        recursive: bool,
        *,
        progress=None,
        cancellation: ImportReviewCancellation | None = None,
    ) -> ImportReviewSession:
        return self._prepare_review.execute(
            root,
            recursive=recursive,
            progress=progress,
            cancellation=cancellation,
        )

    def confirm_movie_import(
        self,
        command: ConfirmMovieImportCommand,
    ) -> MovieFileIngestionResult:
        return self._confirm_import.execute(command)

    def register_movie_import(
        self,
        command: ConfirmMovieImportCommand,
    ) -> MovieFileIngestionResult:
        return self._confirm_import.register(command)

    def enrich_movie_import(
        self,
        command: ConfirmMovieImportCommand,
        registration: MovieFileIngestionResult,
    ) -> MovieFileIngestionResult:
        return self._confirm_import.enrich(command, registration)

    def manual_movie_search(self, title: str, year: str | None = None):
        return self._manual_search.execute(title, year)


@dataclass(frozen=True, slots=True)
class LocalOrganizationActions:
    """Explicit one-file preview/confirmation adapter for presentation."""

    _organize: OrganizeMediaFile

    def prepare_organization(
        self,
        media_file_id: int,
        destination_root: Path,
        destination_filename: str,
    ) -> OrganizationPreview:
        return self._organize.prepare_preview(
            media_file_id,
            destination_root,
            destination_filename,
        )

    def confirm_organization(self, preview_id: str) -> OrganizationResult:
        return self._organize.confirm(preview_id)

    def discard_organization_preview(self, preview_id: str) -> None:
        self._organize.discard_preview(preview_id)


@dataclass(frozen=True, slots=True)
class LocalOperationHistoryActions:
    """Composition adapter for read history and separately authorized reverse/recovery work."""

    _list_history: ListOperationHistory
    _get_details: GetOperationDetails
    _undo: UndoFileOperation
    _recovery: RecoverFileOperation

    def list_operation_history(
        self,
        query: OperationHistoryQuery | None = None,
    ) -> tuple[OperationHistoryItem, ...]:
        return self._list_history.execute(query)

    def save_operation_history(
        self,
        items: tuple[OperationHistoryItem, ...],
        path: str,
    ) -> None:
        SaveOperationHistory().execute(items, path)

    def get_operation_details(self, operation_id: str) -> OperationDetails:
        return self._get_details.execute(operation_id)

    def prepare_undo(self, operation_id: str) -> UndoPreview:
        return self._undo.prepare_preview(operation_id)

    def confirm_undo(self, preview_id: str) -> UndoResult:
        return self._undo.confirm(preview_id)

    def discard_undo_preview(self, preview_id: str) -> None:
        self._undo.discard_preview(preview_id)

    def inspect_recovery(self, operation_id: str) -> RecoveryAssessment:
        return self._recovery.inspect(operation_id)

    def attempt_recovery(self, operation_id: str) -> RecoveryResult:
        return self._recovery.attempt(operation_id)


@dataclass(frozen=True, slots=True)
class LocalReconciliationActions:
    _reconcile: ReconcileLibraryFiles
    _relink: RelinkMediaFile
    _check_library: CheckLibrary | None = None

    def reconcile_library_files(
        self,
        *,
        progress=None,
        cancellation: ReconciliationCancellation | None = None,
    ) -> LibraryReconciliationProgress:
        return self._reconcile.execute(progress=progress, cancellation=cancellation)

    def prepare_media_relink(self, media_file_id: int, candidate_path: Path) -> RelinkPreview:
        return self._relink.prepare_preview(media_file_id, candidate_path)

    def confirm_media_relink(self, preview_id: str) -> RelinkResult:
        return self._relink.confirm(preview_id)

    def discard_media_relink_preview(self, preview_id: str) -> None:
        self._relink.discard_preview(preview_id)

    def check_library(
        self,
        *,
        progress=None,
        cancellation: ReconciliationCancellation | None = None,
    ) -> LibraryHealthProgress:
        if self._check_library is None:
            raise RuntimeError("full library health check is not configured")
        return self._check_library.execute(progress=progress, cancellation=cancellation)


@dataclass(frozen=True, slots=True)
class LocalSettingsActions:
    _metadata: MetadataSettings
    _clear_library: ClearLibraryData
    _language: UiLanguageSettings
    _theme: UiThemeSettings
    _sidebar: UiSidebarSettings

    def metadata_credential_status(self):
        return self._metadata.metadata_credential_status()

    def apply_tmdb_session_token(self, token: str):
        return self._metadata.apply_tmdb_session_token(token)

    def clear_tmdb_session_token(self):
        return self._metadata.clear_tmdb_session_token()

    def clear_library_data(self) -> ClearLibraryDataResult:
        return self._clear_library.execute()

    def current_ui_language(self) -> UiLanguage:
        return self._language.current_language()

    def set_ui_language(self, language: UiLanguage) -> UiLanguage:
        return self._language.set_language(language)

    def current_ui_theme(self) -> UiTheme:
        return self._theme.current_theme()

    def set_ui_theme(self, theme: UiTheme) -> UiTheme:
        return self._theme.set_theme(theme)

    def current_sidebar_width(self) -> int:
        return self._sidebar.current_width()

    def set_sidebar_width(self, width: int) -> int:
        return self._sidebar.set_width(width)


def create_library_actions(database: Database) -> LocalLibraryActions:
    repository = SqliteMovieLibraryReadRepository(database)
    return LocalLibraryActions(
        _list_movies=ListMovies(repository),
        _get_movie_item=GetMovieListItem(repository),
        _get_movie_details=GetMovieDetails(repository),
    )


def create_personal_library_actions(database: Database) -> LocalPersonalLibraryActions:
    repository = SqlitePersonalLibraryRepository(database)
    return LocalPersonalLibraryActions(
        _state=GetPersonalMovieState(repository),
        _history=GetWatchHistory(repository),
        _set_preference=SetPersonalPreference(repository),
        _clear_preference=ClearPersonalPreference(repository),
        _add_watchlist=AddToWatchlist(repository),
        _remove_watchlist=RemoveFromWatchlist(repository),
        _record_watch=RecordWatch(repository),
        _remove_watch=RemoveWatchEvent(repository),
        _list_movies=ListPersonalMovies(repository),
    )


def create_import_actions(
    database: Database,
    *,
    provider: MetadataProvider | None = None,
    credentials: SessionTmdbCredentials | None = None,
) -> LocalImportActions:
    raw_provider = provider or SessionConfiguredTmdbProvider(
        credentials or SessionTmdbCredentials()
    )
    cached_provider = CachedMetadataProvider(
        raw_provider,
        MetadataCacheRepository(database),
    )
    discover = DiscoverMedia(ReadOnlyMediaScanner())
    propose = ProposeMovieImport(
        cached_provider,
        MovieMatcher(),
        MediaFileRepository(database),
    )
    unit_of_work_factory = lambda: SqliteCatalogUnitOfWork(database)
    registrar = RegisterLocalMovieFile(unit_of_work_factory)
    enricher = EnrichMovieMetadata(cached_provider, unit_of_work_factory)
    return LocalImportActions(
        _prepare_review=PrepareFolderImportReview(discover, propose),
        _confirm_import=ConfirmMovieImport(registrar, enricher),
        _manual_search=ManualMovieSearch(cached_provider),
    )


def create_organization_actions(
    database: Database,
    *,
    execution_lock: threading.Lock | None = None,
) -> LocalOrganizationActions:
    media_files = MediaFileRepository(database)
    operation_store = SqliteOperationStore(database, media_files=media_files)
    return LocalOrganizationActions(
        OrganizeMediaFile(media_files, operation_store, execution_lock=execution_lock)
    )


def create_operation_history_actions(
    database: Database,
    *,
    execution_lock: threading.Lock | None = None,
) -> LocalOperationHistoryActions:
    media_files = MediaFileRepository(database)
    operation_store = SqliteOperationStore(database, media_files=media_files)
    journal = SqliteOperationJournalReadRepository(database)
    return LocalOperationHistoryActions(
        _list_history=ListOperationHistory(journal),
        _get_details=GetOperationDetails(journal),
        _undo=UndoFileOperation(
            journal,
            media_files,
            operation_store,
            execution_lock=execution_lock,
        ),
        _recovery=RecoverFileOperation(
            journal,
            operation_store,
            execution_lock=execution_lock,
        ),
    )


def create_reconciliation_actions(
    database: Database,
    *,
    confirmation_lock: threading.Lock | None = None,
    metadata_provider: MetadataProvider | None = None,
    poster_actions: PosterAssetService | None = None,
) -> LocalReconciliationActions:
    media_files = MediaFileRepository(database)
    inspector = NoFollowMediaFileInspector()
    movie_repository = SqliteMovieRepository(database)
    full_check = None
    if metadata_provider is not None:
        full_check = CheckLibrary(
            ReconcileLibraryFiles(media_files, inspector),
            movie_repository,
            metadata_provider,
            poster_actions=poster_actions,
        )
    return LocalReconciliationActions(
        ReconcileLibraryFiles(media_files, inspector),
        RelinkMediaFile(
            media_files,
            SqliteMovieLibraryReadRepository(database),
            inspector,
            confirmation_lock=confirmation_lock,
        ),
        full_check,
    )


def create_main_window(
    database_path: Path,
    *,
    metadata_provider: MetadataProvider | None = None,
    poster_cache_path: Path | None = None,
    load_on_show: bool = True,
) -> MainWindow:
    database = Database(database_path)
    MigrationRunner(database).migrate()
    credentials = SessionTmdbCredentials()
    provider = metadata_provider or SessionConfiguredTmdbProvider(credentials)
    operation_execution_lock = threading.Lock()
    poster_cache = PosterAssetCache(poster_cache_path or default_poster_cache_path())
    poster_service = PosterAssetService(
        poster_cache,
        {"tmdb": TmdbPosterSource(credentials)},
    )
    health_provider = CachedMetadataProvider(provider, MetadataCacheRepository(database))
    settings_actions = LocalSettingsActions(
        MetadataSettings(credentials),
        ClearLibraryData(
            SqliteLibraryMaintenanceRepository(database),
            poster_cache,
            execution_lock=operation_execution_lock,
        ),
        UiLanguageSettings(SqliteUiLanguageRepository(database)),
        UiThemeSettings(SqliteUiThemeRepository(database)),
        UiSidebarSettings(SqliteUiSidebarRepository(database)),
    )
    application = QApplication.instance()
    if application is not None:
        # Prepare the persisted theme before MainWindow constructs any widget.
        # This avoids a full application repolish after shell/page geometry has
        # already been calculated.
        apply_theme(application, settings_actions.current_ui_theme())

    return MainWindow(
        create_library_actions(database),
        personal_actions=create_personal_library_actions(database),
        import_actions=create_import_actions(database, provider=provider),
        settings_actions=settings_actions,
        local_media_actions=WindowsLocalMediaActions(),
        organization_actions=create_organization_actions(
            database,
            execution_lock=operation_execution_lock,
        ),
        operation_history_actions=create_operation_history_actions(
            database,
            execution_lock=operation_execution_lock,
        ),
        reconciliation_actions=create_reconciliation_actions(
            database,
            confirmation_lock=operation_execution_lock,
            metadata_provider=health_provider,
            poster_actions=poster_service,
        ),
        poster_actions=poster_service,
        load_on_show=load_on_show,
    )


def default_database_path() -> Path:
    return resolve_runtime_paths().database_path


def default_poster_cache_path() -> Path:
    return resolve_runtime_paths().poster_cache_path


def run_desktop_app(database_path: Path | None = None) -> int:
    application = QApplication.instance() or QApplication(sys.argv)
    single_instance = SingleInstanceCoordinator(parent=application)
    if not single_instance.acquire():
        return 0
    application.setWindowIcon(application_icon())
    runtime_paths = resolve_runtime_paths()
    try:
        runtime_paths.ensure_directories()
        configure_runtime_logging(runtime_paths.log_directory)
        window = create_main_window(
            database_path or runtime_paths.database_path,
            poster_cache_path=runtime_paths.poster_cache_path,
        )
    except Exception:
        logging.getLogger(__name__).exception("DropSort startup failed")
        single_instance.close()
        QMessageBox.critical(
            None,
            "DropSort could not start",
            "DropSort could not open the local library database. The existing database was preserved.",
        )
        return 1
    single_instance.activation_requested.connect(window.activate_from_single_instance)
    application.aboutToQuit.connect(single_instance.close)
    application.aboutToQuit.connect(window.wait_for_pending_tasks)
    window.show()
    return application.exec()
