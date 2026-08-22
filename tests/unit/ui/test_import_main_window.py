from __future__ import annotations

from dataclasses import dataclass, field
from pathlib import Path

from PySide6.QtCore import Qt
from PySide6.QtTest import QTest
from PySide6.QtWidgets import QApplication, QLineEdit, QPushButton

from dropsort.application.dto.import_review import ImportReviewSession
from dropsort.application.dto.library import MovieDetails, MovieListItem
from dropsort.application.dto.movie_import import (
    ConfirmMovieImportCommand,
    ImportProposalReason,
    ImportProposalStatus,
)
from dropsort.application.configuration.metadata_credentials import (
    MetadataCredentialOrigin,
    MetadataCredentialStatus,
)
from dropsort.ui.main_window.window import MainWindow


class ImmediateRunner:
    def submit(self, token, task, on_success, on_failure) -> None:
        try:
            on_success(token, task())
        except BaseException as error:
            on_failure(token, error)


@dataclass
class CombinedActions:
    item: MovieListItem
    details: MovieDetails
    session: ImportReviewSession
    library_calls: list[str] = field(default_factory=list)

    def list_movies(self) -> tuple[MovieListItem, ...]:
        self.library_calls.append("library")
        return (self.item,)

    def get_movie_details(self, movie_id: int) -> MovieDetails:
        return self.details

    def prepare_import_review(
        self,
        root: Path,
        recursive: bool,
        *,
        progress=None,
        cancellation=None,
    ) -> ImportReviewSession:
        return self.session

    def confirm_movie_import(self, command: ConfirmMovieImportCommand) -> object:
        return object()


@dataclass
class FakeSettingsActions:
    origin: MetadataCredentialOrigin = MetadataCredentialOrigin.NOT_CONFIGURED

    def metadata_credential_status(self) -> MetadataCredentialStatus:
        return MetadataCredentialStatus(
            self.origin is not MetadataCredentialOrigin.NOT_CONFIGURED,
            self.origin,
        )

    def apply_tmdb_session_token(self, token: str) -> MetadataCredentialStatus:
        self.origin = MetadataCredentialOrigin.SESSION
        return self.metadata_credential_status()

    def clear_tmdb_session_token(self) -> MetadataCredentialStatus:
        self.origin = MetadataCredentialOrigin.NOT_CONFIGURED
        return self.metadata_credential_status()


def test_main_window_exposes_import_workflow_only_when_composed(
    qapp: QApplication,
    movie_item_factory,
    movie_details_factory,
    proposal_factory,
) -> None:
    root = Path.cwd() / "movies"
    actions = CombinedActions(
        movie_item_factory(),
        movie_details_factory(),
        ImportReviewSession(root, True, (proposal_factory(),)),
    )
    window = MainWindow(
        actions,
        import_actions=actions,
        task_runner=ImmediateRunner(),
        load_on_show=False,
    )
    import_button = window.findChild(QPushButton, "importNavButton")
    assert import_button is not None

    QTest.mouseClick(import_button, Qt.MouseButton.LeftButton)

    assert window.current_section == "import"
    assert window.import_view is not None


def test_leaving_filtered_library_for_add_movies_clears_transient_search(
    qapp: QApplication,
    movie_item_factory,
    movie_details_factory,
    proposal_factory,
) -> None:
    root = Path.cwd() / "movies"
    actions = CombinedActions(
        movie_item_factory(title="The Wind Rises"),
        movie_details_factory(),
        ImportReviewSession(root, True, (proposal_factory(),)),
    )
    window = MainWindow(
        actions,
        import_actions=actions,
        task_runner=ImmediateRunner(),
        load_on_show=False,
    )
    window.show_library()
    search = window.findChild(QLineEdit, "librarySearchInput")
    assert search is not None
    search.setText("Wind")
    assert window.library_view._search_query == "Wind"

    window.show_import()

    assert window.current_section == "import"
    assert search.text() == ""
    assert window.library_view._search_query == ""


def test_successful_explicit_import_refreshes_local_library_snapshot(
    qapp: QApplication,
    movie_item_factory,
    movie_details_factory,
    proposal_factory,
) -> None:
    root = Path.cwd() / "movies"
    actions = CombinedActions(
        movie_item_factory(),
        movie_details_factory(),
        ImportReviewSession(root, True, (proposal_factory(),)),
    )
    window = MainWindow(
        actions,
        import_actions=actions,
        task_runner=ImmediateRunner(),
        load_on_show=False,
    )
    window.show_library()
    window.show_import()
    assert window.import_view is not None

    window.import_view.catalog_changed.emit()

    # Hidden Library UI is invalidated, not synchronously rebuilt behind the
    # Add Movies page.  The fresh query runs only when Library is revisited.
    assert actions.library_calls == ["library"]
    assert window.library_view._has_snapshot is False
    assert window.current_section == "import"
    window.show_library()
    assert actions.library_calls == ["library", "library"]


def test_window_without_import_composition_has_no_scan_or_import_controls(
    qapp: QApplication,
    movie_item_factory,
    movie_details_factory,
) -> None:
    class LibraryOnly:
        def list_movies(self):
            return (movie_item_factory(),)

        def get_movie_details(self, movie_id: int):
            return movie_details_factory()

    window = MainWindow(LibraryOnly(), load_on_show=False)

    assert window.findChild(QPushButton, "importNavButton") is None
    assert window.import_view is None
    window.show_import()
    assert window.current_section == "library"


def test_close_invalidates_import_worker_results_and_library_refreshes_library_section(
    qapp: QApplication,
    movie_item_factory,
    movie_details_factory,
    proposal_factory,
) -> None:
    root = Path.cwd() / "movies"
    actions = CombinedActions(
        movie_item_factory(),
        movie_details_factory(),
        ImportReviewSession(root, True, (proposal_factory(),)),
    )
    window = MainWindow(
        actions,
        import_actions=actions,
        task_runner=ImmediateRunner(),
        load_on_show=False,
    )
    window.show_library()
    window.show_import()
    assert window.import_view is not None
    token_before = window.import_view._session_token

    window.import_view.catalog_changed.emit()
    window.close()

    assert actions.library_calls == ["library"]
    assert window.import_view._session_token > token_before


def test_settings_navigation_and_missing_credential_route_are_composed(
    qapp: QApplication,
    movie_item_factory,
    movie_details_factory,
    proposal_factory,
) -> None:
    root = Path.cwd() / "movies"
    proposal = proposal_factory(
        status=ImportProposalStatus.METADATA_UNAVAILABLE,
        reasons=(ImportProposalReason.METADATA_AUTHENTICATION,),
    )
    actions = CombinedActions(
        movie_item_factory(),
        movie_details_factory(),
        ImportReviewSession(root, True, (proposal,)),
    )
    window = MainWindow(
        actions,
        import_actions=actions,
        settings_actions=FakeSettingsActions(),
        task_runner=ImmediateRunner(),
        load_on_show=False,
    )
    settings_button = window.findChild(QPushButton, "settingsNavButton")

    assert settings_button is not None
    window.show_import()
    assert window.import_view is not None
    window.import_view.start_scan(root)
    window.import_view.rows[0].settings_button.click()

    assert window.current_section == "settings"
    assert window.settings_view is not None


def test_settings_apply_returns_to_add_movies(
    qapp: QApplication,
    movie_item_factory,
    movie_details_factory,
    proposal_factory,
) -> None:
    root = Path.cwd() / "movies"
    actions = CombinedActions(
        movie_item_factory(),
        movie_details_factory(),
        ImportReviewSession(root, True, (proposal_factory(),)),
    )
    window = MainWindow(
        actions,
        import_actions=actions,
        settings_actions=FakeSettingsActions(),
        task_runner=ImmediateRunner(),
        load_on_show=False,
    )
    assert window.settings_view is not None
    window.show_settings()
    window.settings_view.token_input.setText("session-token-value-123456789012345")

    window.settings_view.apply_session_token()

    assert window.current_section == "import"
