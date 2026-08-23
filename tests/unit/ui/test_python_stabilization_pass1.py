from __future__ import annotations

from dataclasses import dataclass, field, replace

from dropsort.application.dto.library import (
    MediaFileAvailability,
    MovieDetails,
    MovieListItem,
)
from dropsort.application.dto.reconciliation import (
    LibraryReconciliationProgress,
    MediaFileStatusChange,
)
from dropsort.application.errors import MovieNotFoundError
from dropsort.ui.library.library_view import LibraryView
from dropsort.ui.main_window.window import MainWindow


@dataclass
class LibraryActions:
    movies: tuple[MovieListItem, ...]
    details: MovieDetails
    calls: list[str] = field(default_factory=list)

    def list_movies(self) -> tuple[MovieListItem, ...]:
        self.calls.append("library")
        return self.movies

    def get_movie_item(self, movie_id: int) -> MovieListItem:
        self.calls.append(f"item:{movie_id}")
        for item in self.movies:
            if item.movie_id == movie_id:
                return item
        raise MovieNotFoundError(f"movie {movie_id} was not found")

    def get_movie_details(self, movie_id: int) -> MovieDetails:
        self.calls.append(f"details:{movie_id}")
        return self.details


class DeferredRunner:
    def __init__(self) -> None:
        self.progressive: list[tuple] = []

    def submit(self, *_args) -> None:
        raise AssertionError("this pass expects only manual Check Library work")

    def submit_progressive(self, *args) -> None:
        self.progressive.append(args)

    def wait_for_done(self) -> None:
        return None


class ManualCheckActions:
    def __init__(self) -> None:
        self.reconcile_calls = 0
        self.check_calls = 0

    def reconcile_library_files(self, **_kwargs):
        self.reconcile_calls += 1
        raise AssertionError("startup must not inspect cataloged files")

    def check_library(self, *, progress=None, cancellation=None):
        del cancellation
        self.check_calls += 1
        value = LibraryReconciliationProgress(1, 1, 1, 0, 0, 0)
        if progress is not None:
            progress(value)
        return value


def _window(actions, checks, runner) -> MainWindow:
    return MainWindow(
        actions,
        reconciliation_actions=checks,
        task_runner=runner,
    )


def _start_manual_check(window: MainWindow, runner: DeferredRunner):
    window.show_check_library()
    window.check_library_page.request_start()
    assert len(runner.progressive) == 1
    return runner.progressive[0]


def test_startup_loads_local_library_once_without_file_reconciliation(
    qapp,
    movie_item_factory,
    movie_details_factory,
) -> None:
    actions = LibraryActions((movie_item_factory(),), movie_details_factory())
    checks = ManualCheckActions()
    runner = DeferredRunner()

    window = _window(actions, checks, runner)
    qapp.processEvents()

    assert actions.calls == ["library"]
    assert checks.reconcile_calls == 0
    assert checks.check_calls == 0
    assert runner.progressive == []


def test_check_library_starts_only_after_explicit_user_request(
    qapp,
    movie_item_factory,
    movie_details_factory,
) -> None:
    actions = LibraryActions((movie_item_factory(),), movie_details_factory())
    checks = ManualCheckActions()
    runner = DeferredRunner()
    window = _window(actions, checks, runner)

    window.show_check_library()
    assert checks.check_calls == 0
    assert runner.progressive == []

    _token, task, _on_progress, _on_success, _on_failure = _start_manual_check(
        window, runner
    )
    task(lambda _value: None)

    assert checks.check_calls == 1
    assert checks.reconcile_calls == 0


def test_check_progress_without_item_changes_never_reloads_library(
    qapp,
    movie_item_factory,
    movie_details_factory,
) -> None:
    actions = LibraryActions((movie_item_factory(),), movie_details_factory())
    runner = DeferredRunner()
    window = _window(actions, ManualCheckActions(), runner)
    token, _task, on_progress, _on_success, _on_failure = _start_manual_check(
        window, runner
    )

    on_progress(token, LibraryReconciliationProgress(1, 1, 1, 0, 0, 0))

    assert actions.calls == ["library"]


def test_one_availability_change_queries_and_replaces_only_affected_card(
    qapp,
    movie_item_factory,
    movie_details_factory,
) -> None:
    first = movie_item_factory(movie_id=1, title="Arrival")
    second = movie_item_factory(movie_id=2, title="The Wind Rises")
    actions = LibraryActions((first, second), movie_details_factory(movie_id=1))
    runner = DeferredRunner()
    window = _window(actions, ManualCheckActions(), runner)
    cards_before = {card.item.movie_id: card for card in window.library_view.cards}
    token, _task, on_progress, _on_success, _on_failure = _start_manual_check(
        window, runner
    )
    actions.movies = (replace(first, missing_file_count=1), second)

    on_progress(
        token,
        LibraryReconciliationProgress(
            2,
            1,
            0,
            1,
            0,
            1,
            (MediaFileStatusChange(11, 1, MediaFileAvailability.MISSING),),
        ),
    )

    cards_after = {card.item.movie_id: card for card in window.library_view.cards}
    assert actions.calls == ["library", "item:1"]
    assert cards_after[1] is not cards_before[1]
    assert cards_after[2] is cards_before[2]
    assert cards_after[1].item.missing_file_count == 1


def test_incremental_missing_update_keeps_movie_registered(
    qapp,
    movie_item_factory,
    movie_details_factory,
) -> None:
    item = movie_item_factory(movie_id=7, media_file_count=1)
    actions = LibraryActions((item,), movie_details_factory(movie_id=7))
    view = LibraryView(actions)
    view.show_library()
    actions.movies = (replace(item, missing_file_count=1),)

    view.refresh_movies((7,))

    assert view.card_count == 1
    assert view.cards[0].item.movie_id == 7
    assert view.cards[0].item.media_file_count == 1
    assert view.cards[0].item.all_files_missing is True


def test_manual_check_completion_and_navigation_do_not_reload_library(
    qapp,
    movie_item_factory,
    movie_details_factory,
) -> None:
    item = movie_item_factory(movie_id=5)
    actions = LibraryActions((item,), movie_details_factory(movie_id=5))
    runner = DeferredRunner()
    window = _window(actions, ManualCheckActions(), runner)
    token, _task, on_progress, on_success, _on_failure = _start_manual_check(
        window, runner
    )
    actions.movies = (replace(item, missing_file_count=1),)
    value = LibraryReconciliationProgress(
        1,
        1,
        0,
        1,
        0,
        1,
        (MediaFileStatusChange(51, 5, MediaFileAvailability.MISSING),),
    )

    on_progress(token, value)
    on_success(token, value)
    window.show_library()

    assert actions.calls == ["library", "item:5"]
    assert window.library_view.card_count == 1
    assert window.library_view.cards[0].item.missing_file_count == 1
