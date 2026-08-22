from __future__ import annotations

from collections.abc import Callable
from typing import Protocol

from pathlib import Path

from dropsort.application.dto.catalog import MovieFileIngestionResult
from dropsort.application.dto.import_review import ImportReviewProgress, ImportReviewSession
from dropsort.application.dto.library import MovieDetails, MovieListItem
from dropsort.application.dto.personal_library import PersonalMovieSnapshot
from dropsort.library.personal import PersonalLibrarySection, PersonalPreference, WatchEvent
from dropsort.application.dto.movie_import import ConfirmMovieImportCommand
from dropsort.application.configuration.metadata_credentials import MetadataCredentialStatus
from dropsort.application.configuration.localization import UiLanguage
from dropsort.application.configuration.theme import UiTheme
from dropsort.application.dto.manual_search import ManualMovieSearchResult
from dropsort.application.dto.catalog_maintenance import ClearLibraryDataResult
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
from dropsort.application.use_cases.prepare_folder_import_review import ImportReviewCancellation
from dropsort.application.dto.reconciliation import (
    LibraryReconciliationProgress,
    RelinkPreview,
    RelinkResult,
)
from dropsort.application.dto.library_health import LibraryHealthProgress
from dropsort.application.use_cases.reconcile_library_files import ReconciliationCancellation


class LibraryUiActions(Protocol):
    """Read-only application actions consumed by library widgets."""

    def list_movies(self) -> tuple[MovieListItem, ...]: ...

    def get_movie_details(self, movie_id: int) -> MovieDetails: ...


class PersonalLibraryUiActions(Protocol):
    """Personal-library application boundary consumed by Qt widgets."""

    def get_personal_snapshot(self, movie_id: int) -> PersonalMovieSnapshot: ...

    def set_personal_preference(
        self, movie_id: int, preference: PersonalPreference
    ) -> PersonalMovieSnapshot: ...

    def clear_personal_preference(self, movie_id: int) -> PersonalMovieSnapshot: ...

    def add_to_watchlist(self, movie_id: int) -> PersonalMovieSnapshot: ...

    def remove_from_watchlist(self, movie_id: int) -> PersonalMovieSnapshot: ...

    def record_watch(
        self, movie_id: int, watched_at=None
    ) -> PersonalMovieSnapshot: ...

    def remove_watch_event(self, event_id: int) -> PersonalMovieSnapshot: ...

    def list_personal_movies(
        self, section: PersonalLibrarySection
    ) -> tuple[MovieListItem, ...]: ...


class ImportUiActions(Protocol):
    """Application orchestration consumed by the explicit import UI."""

    def prepare_import_review(
        self,
        root: Path,
        recursive: bool,
        *,
        progress: Callable[[ImportReviewProgress], None] | None = None,
        cancellation: ImportReviewCancellation | None = None,
    ) -> ImportReviewSession: ...

    def confirm_movie_import(
        self,
        command: ConfirmMovieImportCommand,
    ) -> MovieFileIngestionResult: ...

    def manual_movie_search(self, title: str, year: str | None = None) -> ManualMovieSearchResult: ...


class SettingsUiActions(Protocol):
    """Secret-free metadata configuration actions consumed by Settings."""

    def metadata_credential_status(self) -> MetadataCredentialStatus: ...

    def apply_tmdb_session_token(self, token: str) -> MetadataCredentialStatus: ...

    def clear_tmdb_session_token(self) -> MetadataCredentialStatus: ...

    def clear_library_data(self) -> ClearLibraryDataResult: ...

    def current_ui_language(self) -> UiLanguage: ...

    def set_ui_language(self, language: UiLanguage) -> UiLanguage: ...

    def current_ui_theme(self) -> UiTheme: ...

    def set_ui_theme(self, theme: UiTheme) -> UiTheme: ...

    def current_sidebar_width(self) -> int: ...

    def set_sidebar_width(self, width: int) -> int: ...


class OrganizationUiActions(Protocol):
    """Explicit preview and confirmation boundary consumed by organization widgets."""

    def prepare_organization(
        self,
        media_file_id: int,
        destination_root: Path,
        destination_filename: str,
    ) -> OrganizationPreview: ...

    def confirm_organization(self, preview_id: str) -> OrganizationResult: ...

    def discard_organization_preview(self, preview_id: str) -> None: ...


class OperationHistoryUiActions(Protocol):
    """History, explicit undo, and explicit recovery actions consumed by widgets."""

    def list_operation_history(
        self,
        query: OperationHistoryQuery | None = None,
    ) -> tuple[OperationHistoryItem, ...]: ...

    def save_operation_history(
        self,
        items: tuple[OperationHistoryItem, ...],
        path: str,
    ) -> None: ...

    def get_operation_details(self, operation_id: str) -> OperationDetails: ...

    def prepare_undo(self, operation_id: str) -> UndoPreview: ...

    def confirm_undo(self, preview_id: str) -> UndoResult: ...

    def discard_undo_preview(self, preview_id: str) -> None: ...

    def inspect_recovery(self, operation_id: str) -> RecoveryAssessment: ...

    def attempt_recovery(self, operation_id: str) -> RecoveryResult: ...


class ReconciliationUiActions(Protocol):
    def reconcile_library_files(
        self,
        *,
        progress: Callable[[LibraryReconciliationProgress], None] | None = None,
        cancellation: ReconciliationCancellation | None = None,
    ) -> LibraryReconciliationProgress: ...

    def prepare_media_relink(self, media_file_id: int, candidate_path: Path) -> RelinkPreview: ...

    def confirm_media_relink(self, preview_id: str) -> RelinkResult: ...

    def discard_media_relink_preview(self, preview_id: str) -> None: ...

    def check_library(
        self,
        *,
        progress: Callable[[LibraryHealthProgress], None] | None = None,
        cancellation: ReconciliationCancellation | None = None,
    ) -> LibraryHealthProgress: ...
