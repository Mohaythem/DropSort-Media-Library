from __future__ import annotations

from dataclasses import dataclass, field

from PySide6.QtCore import Qt, Signal
from PySide6.QtTest import QTest
from PySide6.QtWidgets import QApplication, QComboBox, QDialog, QPushButton

from dropsort.application.dto.library import MovieDetails, MovieListItem
from dropsort.application.errors import (
    CatalogClearBlockedError,
    CatalogClearError,
    LibraryReconciliationCancelled,
    MovieNotFoundError,
)
from dropsort.application.dto.reconciliation import LibraryReconciliationProgress
from dropsort.application.dto.catalog_maintenance import ClearLibraryDataResult
from dropsort.application.configuration.metadata_credentials import (
    MetadataCredentialOrigin,
    MetadataCredentialStatus,
)
from dropsort.application.configuration.localization import UiLanguage
from dropsort.ui.main_window.window import MainWindow
from dropsort.posters import PosterAsset
from dropsort.library.personal import PersonalLibrarySection
import base64


@dataclass
class FakeActions:
    movies: tuple[MovieListItem, ...]
    details: MovieDetails
    calls: list[str] = field(default_factory=list)
    details_error: bool = False

    def list_movies(self) -> tuple[MovieListItem, ...]:
        self.calls.append("library")
        return self.movies

    def get_movie_details(self, movie_id: int) -> MovieDetails:
        self.calls.append(f"details:{movie_id}")
        if self.details_error:
            raise MovieNotFoundError("technical detail")
        return self.details


def _button(window: MainWindow, name: str) -> QPushButton:
    button = window.findChild(QPushButton, name)
    assert button is not None
    return button


def test_main_window_navigation_and_card_selection(
    qapp: QApplication,
    movie_item_factory,
    movie_details_factory,
) -> None:
    item = movie_item_factory(movie_id=7)
    actions = FakeActions((item,), movie_details_factory(movie_id=7))
    window = MainWindow(actions, load_on_show=False)

    window.show_library()
    assert window.current_section == "library"

    window.library_view.cards[0].selected.emit(7)
    assert window.current_section == "details"
    assert actions.calls == ["library", "details:7"]


def test_default_startup_prepares_library_before_first_event_loop_turn(
    qapp: QApplication,
    movie_item_factory,
    movie_details_factory,
) -> None:
    item = movie_item_factory(movie_id=3)
    actions = FakeActions((item,), movie_details_factory(movie_id=3))

    window = MainWindow(actions)

    # No processEvents() is needed: initial navigation/data preparation is
    # synchronous and complete before the first visible paint can happen.
    assert actions.calls == ["library"]
    assert window.current_section == "library"
    assert window._stack.currentWidget() is window.library_view
    assert window._library_button.isChecked()

    # Entering the event loop must not schedule a second initial navigation or
    # refresh after the window has become paintable.
    qapp.processEvents()
    assert actions.calls == ["library"]
    assert window._stack.currentWidget() is window.library_view


def test_primary_navigation_switches_stack_once_per_destination(
    qapp: QApplication,
    movie_item_factory,
    movie_details_factory,
) -> None:
    actions = FakeActions((movie_item_factory(),), movie_details_factory())
    window = MainWindow(actions, load_on_show=False)
    window.show_library()
    changes: list[int] = []
    window._stack.currentChanged.connect(changes.append)

    window.show_check_library()
    assert len(changes) == 1
    assert window._stack.currentWidget() is window.check_library_page

    window.show_library()
    assert len(changes) == 2
    assert window._stack.currentWidget() is window.library_view

    # Reselecting the active destination is a no-op: no reload and no second
    # setCurrentWidget/currentChanged cycle.
    window.show_library()
    assert len(changes) == 2
    assert actions.calls == ["library"]


def test_single_instance_activation_restores_minimized_and_hidden_window(
    qapp: QApplication,
    movie_details_factory,
) -> None:
    window = MainWindow(FakeActions((), movie_details_factory()), load_on_show=False)
    window.show()
    qapp.processEvents()

    window.showMinimized()
    qapp.processEvents()
    window.activate_from_single_instance()
    qapp.processEvents()
    assert window.isVisible()
    assert not window.isMinimized()

    window.hide()
    window.activate_from_single_instance()
    qapp.processEvents()
    assert window.isVisible()
    window.close()


def test_single_instance_activation_is_ignored_during_shutdown(
    qapp: QApplication,
    movie_details_factory,
) -> None:
    window = MainWindow(FakeActions((), movie_details_factory()), load_on_show=False)
    window._single_instance_closing = True
    window.hide()

    window.activate_from_single_instance()

    assert not window.isVisible()
    window.close()


def test_recently_added_navigation_is_removed(
    qapp: QApplication,
    movie_item_factory,
    movie_details_factory,
) -> None:
    item = movie_item_factory(movie_id=7)
    window = MainWindow(
        FakeActions((item,), movie_details_factory(movie_id=7)),
        load_on_show=False,
    )

    assert window.findChild(QPushButton, "recentNavButton") is None
    assert not hasattr(window.library_view, "show_recent")


def test_check_library_is_permanent_navigation_and_library_shortcut_only_navigates(
    qapp,
    movie_details_factory,
) -> None:
    window = MainWindow(FakeActions((), movie_details_factory()), load_on_show=False)
    nav = _button(window, "checkLibraryNavButton")
    shortcut = window.library_view.findChild(QPushButton, "checkLibraryFilesButton")
    assert shortcut is not None and shortcut.isHidden()
    assert window._stack.indexOf(window.check_library_page) >= 0

    window.show_library()
    QTest.mouseClick(nav, Qt.MouseButton.LeftButton)
    assert window.current_section == "check_library"
    assert window._stack.currentWidget() is window.check_library_page
    assert not window.check_library_page.is_running
    assert not window._library_check_dialogs

    QTest.keyClick(window, Qt.Key.Key_Escape)
    assert window.current_section == "library"
    QTest.mouseClick(nav, Qt.MouseButton.LeftButton)
    assert window.current_section == "check_library"
    # A second navigation click only reveals the existing page; it does not
    # create a dialog or start a second worker.
    QTest.mouseClick(nav, Qt.MouseButton.LeftButton)
    assert not window.check_library_page.is_running
    window.close()


def test_runtime_language_switch_updates_navigation_and_closes_safely(
    qapp,
    movie_item_factory,
    movie_details_factory,
) -> None:
    class SettingsActions:
        language = UiLanguage.ENGLISH

        def metadata_credential_status(self):
            return MetadataCredentialStatus(
                False, MetadataCredentialOrigin.NOT_CONFIGURED
            )

        def apply_tmdb_session_token(self, _token):
            raise AssertionError

        def clear_tmdb_session_token(self):
            raise AssertionError

        def current_ui_language(self):
            return self.language

        def set_ui_language(self, language):
            self.language = language
            return language

    settings = SettingsActions()
    window = MainWindow(
        FakeActions((movie_item_factory(),), movie_details_factory()),
        settings_actions=settings,
        load_on_show=False,
    )
    window.show_settings()
    selector = window.findChild(QComboBox, "languageSelector")
    assert selector is not None

    selector.setCurrentIndex(1)

    assert _button(window, "libraryNavButton").text() == "المكتبة"
    assert qapp.layoutDirection() is Qt.LayoutDirection.RightToLeft

    selector.setCurrentIndex(0)
    assert _button(window, "libraryNavButton").text() == "Library"
    assert qapp.layoutDirection() is Qt.LayoutDirection.LeftToRight

    window.close()
    window.wait_for_pending_tasks()


def test_main_window_shows_friendly_details_failure(
    qapp: QApplication,
    movie_item_factory,
    movie_details_factory,
) -> None:
    item = movie_item_factory()
    actions = FakeActions((item,), movie_details_factory(), details_error=True)
    window = MainWindow(actions, load_on_show=False)

    window.show_movie_details(1)

    assert window.current_section == "details"
    assert "could not load" in window.details_view.state_message.casefold()


def test_back_from_details_returns_to_library(
    qapp: QApplication,
    movie_item_factory,
    movie_details_factory,
) -> None:
    item = movie_item_factory()
    window = MainWindow(FakeActions((item,), movie_details_factory()), load_on_show=False)
    window.show_library()
    window.show_movie_details(1)

    window.details_view.back_requested.emit()

    assert window.current_section == "library"


def test_opening_movie_details_clears_global_search_without_extra_navigation(
    qapp: QApplication,
    movie_item_factory,
    movie_details_factory,
) -> None:
    item = movie_item_factory(title="The Wind Rises")
    window = MainWindow(FakeActions((item,), movie_details_factory()), load_on_show=False)
    window.show_library()
    window._search_field.setText("Wind")

    window.show_movie_details(item.movie_id)

    assert window.current_section == "details"
    assert window._search_field.text() == ""
    window.navigate_back()
    assert window.current_section == "library"
    assert window.library_view._search_query == ""


def test_escape_back_navigation_matches_visible_back_and_is_noop_at_library_root(
    qapp: QApplication,
    movie_details_factory,
) -> None:
    window = MainWindow(FakeActions((), movie_details_factory()), load_on_show=False)
    window.show_library()
    window.show()
    qapp.processEvents()

    QTest.keyClick(window, Qt.Key.Key_Escape)
    assert window.current_section == "library"
    assert window.isVisible()

    window.show_movie_details(1)
    QTest.keyClick(window, Qt.Key.Key_Escape)
    assert window.current_section == "library"
    window.close()


def test_escape_preserves_personal_library_tab_context(
    qapp: QApplication,
    movie_details_factory,
) -> None:
    window = MainWindow(
        FakeActions((), movie_details_factory()),
        personal_actions=type("PersonalActions", (), {
            "list_personal_movies": lambda self, _section: (),
            "get_personal_snapshot": lambda self, _movie_id: None,
        })(),
        load_on_show=False,
    )
    window.show()
    qapp.processEvents()
    for section in PersonalLibrarySection:
        window.show_personal_library()
        assert window.personal_view is not None
        window.personal_view.refresh(section)
        assert window.personal_view.current_section is section
        window.show_movie_details(1)
        QTest.keyClick(window, Qt.Key.Key_Escape)
        assert window.current_section == "personal"
        assert window.personal_view.current_section is section
    window.close()


def test_escape_does_not_navigate_under_a_modal_dialog(
    qapp: QApplication,
    movie_details_factory,
) -> None:
    window = MainWindow(FakeActions((), movie_details_factory()), load_on_show=False)
    window.show_movie_details(1)
    window.show()
    modal = QDialog(window)
    modal.setModal(True)
    modal.show()
    qapp.processEvents()

    QTest.keyClick(window, Qt.Key.Key_Escape)

    assert window.current_section == "details"
    modal.close()
    window.close()


def test_composed_window_loads_card_poster_in_shared_background_pool(
    qapp: QApplication,
    movie_item_factory,
    movie_details_factory,
) -> None:
    content = base64.b64decode(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="
    )

    class PosterActions:
        def load_poster(self, request):
            return PosterAsset("png", content)

    item = movie_item_factory()
    window = MainWindow(
        FakeActions((item,), movie_details_factory()),
        poster_actions=PosterActions(),
        load_on_show=False,
    )
    window.show_library()
    assert window.poster_loader is not None

    from PySide6.QtCore import QEventLoop, QTimer

    loop = QEventLoop()
    window.poster_loader.idle.connect(loop.quit)
    QTimer.singleShot(2000, loop.quit)
    loop.exec()

    assert window.library_view.cards[0].poster_loaded is True
    window.wait_for_pending_tasks()


def test_window_close_invalidates_poster_delivery_before_shutdown(
    qapp: QApplication,
    movie_item_factory,
    movie_details_factory,
) -> None:
    class PosterActions:
        def load_poster(self, request):
            return None

    window = MainWindow(
        FakeActions((movie_item_factory(),), movie_details_factory()),
        poster_actions=PosterActions(),
        load_on_show=False,
    )
    assert window.poster_loader is not None

    window.close()

    assert window.poster_loader.accepting_requests is False
    window.wait_for_pending_tasks()


def test_main_window_opens_library_check_refreshes_and_invalidates_on_close(
    qapp,
    movie_item_factory,
    movie_details_factory,
    monkeypatch,
) -> None:
    created = []

    class FakeCheckDialog(QDialog):
        completed = Signal(object)

        def __init__(self, actions, runner, parent=None, **_kwargs):
            super().__init__(parent)
            self.invalidated = False
            created.append(self)

        def invalidate_pending(self):
            self.invalidated = True

    monkeypatch.setattr(
        "dropsort.ui.main_window.window.LibraryFileCheckDialog",
        FakeCheckDialog,
    )
    item = movie_item_factory()
    actions = FakeActions((item,), movie_details_factory())
    window = MainWindow(
        actions,
        reconciliation_actions=object(),
        load_on_show=False,
    )
    window.show_library()

    window.show_library_file_check()

    dialog = created[0]
    window.show_library_file_check()
    assert len(created) == 1
    dialog.completed.emit(object())
    assert actions.calls == ["library", "library"]
    window.close()
    assert dialog.invalidated is True
    dialog.done(0)
    window.wait_for_pending_tasks()


class DeferredProgressRunner:
    def __init__(self) -> None:
        self.progressive: list[tuple] = []

    def submit(self, *args) -> None:
        raise AssertionError("this scenario expects only reconciliation work")

    def submit_progressive(self, *args) -> None:
        self.progressive.append(args)

    def wait_for_done(self) -> None:
        return None


class ReconciliationActions:
    def __init__(self) -> None:
        self.cancellations = []

    def reconcile_library_files(self, *, progress=None, cancellation=None):
        self.cancellations.append(cancellation)
        value = LibraryReconciliationProgress(2, 2, 1, 1, 0, 1)
        if progress is not None:
            progress(value)
        return value


class ImmediateRunner:
    def submit(self, token, task, on_success, on_failure) -> None:
        try:
            on_success(token, task())
        except BaseException as error:
            on_failure(token, error)

    def submit_progressive(self, token, task, on_progress, on_success, on_failure) -> None:
        try:
            on_success(token, task(lambda value: on_progress(token, value)))
        except BaseException as error:
            on_failure(token, error)

    def wait_for_done(self) -> None:
        return None


def test_real_clear_button_reaches_application_action(
    monkeypatch,
    qapp,
    movie_item_factory,
    movie_details_factory,
) -> None:
    item = movie_item_factory()
    library = FakeActions((item,), movie_details_factory())

    class SettingsActions:
        clear_calls = 0

        def metadata_credential_status(self):
            return MetadataCredentialStatus(False, MetadataCredentialOrigin.NOT_CONFIGURED)

        def apply_tmdb_session_token(self, token):
            raise AssertionError

        def clear_tmdb_session_token(self):
            raise AssertionError

        def clear_library_data(self):
            self.clear_calls += 1
            library.movies = ()
            return ClearLibraryDataResult(1, 1, 0, 0)

    monkeypatch.setattr(
        "dropsort.ui.settings.settings_view._confirm_clear_library",
        lambda *_args: True,
    )
    settings = SettingsActions()
    window = MainWindow(
        library,
        settings_actions=settings,
        task_runner=ImmediateRunner(),
        load_on_show=False,
    )
    window.show_settings()

    QTest.mouseClick(
        _button(window, "clearLibraryDataButton"),
        Qt.MouseButton.LeftButton,
    )

    assert settings.clear_calls == 1
    assert window.current_section == "library"
    assert window.library_view.card_count == 0


def test_first_library_entry_starts_one_background_reconciliation_after_render(
    qapp,
    movie_item_factory,
    movie_details_factory,
) -> None:
    item = movie_item_factory()
    actions = FakeActions((item,), movie_details_factory())
    runner = DeferredProgressRunner()
    window = MainWindow(
        actions,
        reconciliation_actions=ReconciliationActions(),
        task_runner=runner,
        load_on_show=False,
    )

    window.show_library()
    window.show_library()

    # Re-entering Library reuses the rendered snapshot instead of querying and
    # rebuilding every card again. Explicit catalog-change paths still force a
    # refresh through _refresh_library_snapshot().
    assert actions.calls == ["library"]
    assert len(runner.progressive) == 1
    assert "checking" in window.library_view.reconciliation_message.casefold()


def test_auto_reconciliation_progress_refreshes_once_and_manual_does_not_duplicate(
    qapp,
    movie_item_factory,
    movie_details_factory,
    monkeypatch,
) -> None:
    created = []

    class FakeCheckDialog(QDialog):
        completed = Signal(object)

        def __init__(self, *args, **kwargs):
            super().__init__(kwargs.get("parent"))
            self.attached = None
            self.progress = []
            self.successes = []
            created.append(self)

        def attach_to_existing(self, token, cancellation):
            self.attached = (token, cancellation)

        def _on_progress(self, token, value):
            self.progress.append((token, value))

        def _on_success(self, token, value):
            self.successes.append((token, value))

        def _on_failure(self, token, error):
            return None

        def invalidate_pending(self):
            return None

    monkeypatch.setattr(
        "dropsort.ui.main_window.window.LibraryFileCheckDialog",
        FakeCheckDialog,
    )
    item = movie_item_factory()
    actions = FakeActions((item,), movie_details_factory())
    runner = DeferredProgressRunner()
    window = MainWindow(
        actions,
        reconciliation_actions=ReconciliationActions(),
        task_runner=runner,
        load_on_show=False,
    )
    window.show_library()
    window.show_library_file_check()
    token, task, on_progress, on_success, _on_failure = runner.progressive[0]

    assert len(created) == 1
    assert created[0].attached[0] == window._reconciliation_token
    progress = LibraryReconciliationProgress(2, 1, 0, 1, 0, 1)
    on_progress(token, progress)
    assert "1 / 2" in window.library_view.reconciliation_message
    result = task(lambda value: on_progress(token, value))
    on_success(token, result)

    assert actions.calls == ["library", "library"]
    assert "complete" in window.library_view.reconciliation_message.casefold()
    assert len(created[0].successes) == 1
    window.show_library_file_check()
    assert len(created) == 1


def test_explicit_full_library_check_waits_behind_startup_file_reconciliation(
    qapp,
    movie_item_factory,
    movie_details_factory,
    monkeypatch,
) -> None:
    created = []

    class FullReconciliationActions(ReconciliationActions):
        def check_library(self, *, progress=None, cancellation=None):
            raise AssertionError("queued full check should start through the dialog runner")

    class FakeCheckDialog(QDialog):
        completed = Signal(object)

        def __init__(self, *args, **kwargs):
            super().__init__(kwargs.get("parent"))
            self.waited = False
            self.started = 0
            created.append(self)

        @property
        def is_running(self):
            return self.started > 0

        def wait_for_automatic_file_check(self):
            self.waited = True

        def start_check(self):
            self.started += 1

        def invalidate_pending(self):
            return None

    monkeypatch.setattr(
        "dropsort.ui.main_window.window.LibraryFileCheckDialog",
        FakeCheckDialog,
    )
    window = MainWindow(
        FakeActions((movie_item_factory(),), movie_details_factory()),
        reconciliation_actions=FullReconciliationActions(),
        task_runner=DeferredProgressRunner(),
        load_on_show=False,
    )
    window.show_library()
    window.show_library_file_check()

    assert created[0].waited is True
    assert created[0].started == 0
    token, _task, _on_progress, on_success, _on_failure = window._task_runner.progressive[0]
    on_success(token, LibraryReconciliationProgress(1, 1, 1, 0, 0, 0))
    assert created[0].started == 1


def test_window_close_cancels_auto_reconciliation_and_ignores_late_result(
    qapp,
    movie_item_factory,
    movie_details_factory,
) -> None:
    actions = FakeActions((movie_item_factory(),), movie_details_factory())
    runner = DeferredProgressRunner()
    window = MainWindow(
        actions,
        reconciliation_actions=ReconciliationActions(),
        task_runner=runner,
        load_on_show=False,
    )
    window.show_library()
    token, _task, _on_progress, on_success, _on_failure = runner.progressive[0]

    window.close()
    on_success(token, LibraryReconciliationProgress(1, 1, 1, 0, 0, 0))

    assert actions.calls == ["library"]


def test_clear_library_result_immediately_invalidates_details_and_shows_empty_library(
    qapp,
    movie_item_factory,
    movie_details_factory,
) -> None:
    item = movie_item_factory()
    library = FakeActions((item,), movie_details_factory())

    class SettingsActions:
        clear_calls = 0

        def metadata_credential_status(self):
            return MetadataCredentialStatus(False, MetadataCredentialOrigin.NOT_CONFIGURED)

        def apply_tmdb_session_token(self, token):
            raise AssertionError

        def clear_tmdb_session_token(self):
            raise AssertionError

        def clear_library_data(self):
            self.clear_calls += 1
            library.movies = ()
            return ClearLibraryDataResult(1, 1, 1, 2)

    settings = SettingsActions()
    window = MainWindow(
        library,
        settings_actions=settings,
        task_runner=ImmediateRunner(),
        load_on_show=False,
    )
    window.show_movie_details(item.movie_id)
    window.show_settings()
    assert window.settings_view is not None

    window.settings_view.clear_library_requested.emit()

    assert settings.clear_calls == 1
    assert window.current_section == "library"
    assert window.library_view.card_count == 0
    assert "no longer" in window.details_view.state_message.casefold()
    assert "library cleared" in window.settings_view._clear_feedback.text().casefold()


def test_automatic_reconciliation_controlled_and_unexpected_failures_are_nonfatal(
    qapp,
    movie_item_factory,
    movie_details_factory,
) -> None:
    actions = FakeActions((movie_item_factory(),), movie_details_factory())
    runner = DeferredProgressRunner()
    window = MainWindow(
        actions,
        reconciliation_actions=ReconciliationActions(),
        task_runner=runner,
        load_on_show=False,
    )
    window.show_library()
    token = window._reconciliation_token
    cancelled = LibraryReconciliationCancelled(
        LibraryReconciliationProgress(2, 1, 1, 0, 0, 0)
    )

    window._automatic_reconciliation_failed(token, cancelled)
    assert "cancelled" in window.library_view.reconciliation_message.casefold()

    window._reconciliation_active = True
    window._automatic_reconciliation_failed(token, RuntimeError("inspection failed"))
    assert "could not complete" in window.library_view.reconciliation_message.casefold()
    window._automatic_reconciliation_progress(token - 1, object())
    window._automatic_reconciliation_succeeded(token - 1, object())


def test_clear_library_busy_unavailable_invalid_and_controlled_failures_have_feedback(
    qapp,
    movie_item_factory,
    movie_details_factory,
) -> None:
    class SettingsWithoutClear:
        def metadata_credential_status(self):
            return MetadataCredentialStatus(False, MetadataCredentialOrigin.NOT_CONFIGURED)

        def apply_tmdb_session_token(self, token):
            raise AssertionError

        def clear_tmdb_session_token(self):
            raise AssertionError

    window = MainWindow(
        FakeActions((movie_item_factory(),), movie_details_factory()),
        settings_actions=SettingsWithoutClear(),
        task_runner=ImmediateRunner(),
        load_on_show=False,
    )
    assert window.settings_view is not None

    window._reconciliation_active = True
    window._clear_library_data_requested()
    assert "busy" in window.settings_view._clear_feedback.text().casefold()
    window._reconciliation_active = False
    window._clear_library_data_requested()
    assert "unavailable" in window.settings_view._clear_feedback.text().casefold()

    window._maintenance_token = 5
    window._maintenance_active = True
    window._clear_library_failed(5, CatalogClearBlockedError("blocked"))
    assert "blocked" in window.settings_view._clear_feedback.text().casefold()
    window._maintenance_active = True
    window._clear_library_failed(5, CatalogClearError("database"))
    assert "preserved" in window.settings_view._clear_feedback.text().casefold()
    window._maintenance_active = True
    window._clear_library_succeeded(5, object())
    assert "could not clear" in window.settings_view._clear_feedback.text().casefold()


def test_settings_clear_result_warns_when_only_poster_cleanup_failed(
    qapp,
) -> None:
    class SettingsActions:
        def metadata_credential_status(self):
            return MetadataCredentialStatus(False, MetadataCredentialOrigin.NOT_CONFIGURED)

        def apply_tmdb_session_token(self, token):
            raise AssertionError

        def clear_tmdb_session_token(self):
            raise AssertionError

    from dropsort.ui.settings import SettingsView

    view = SettingsView(SettingsActions())
    view.show_clear_result(
        ClearLibraryDataResult(1, 2, 3, 0, "POSTER_CACHE_CLEANUP_FAILED")
    )

    assert "poster cache cleanup" in view._clear_feedback.text().casefold()
    assert "media files were unaffected" in view._clear_feedback.text().casefold()
