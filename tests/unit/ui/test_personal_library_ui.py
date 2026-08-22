from __future__ import annotations

from datetime import UTC, datetime

from PySide6.QtCore import QDate, Qt
from PySide6.QtWidgets import QDateEdit, QFrame, QLabel, QPushButton

from dropsort.application.dto.personal_library import PersonalMovieSnapshot
from dropsort.application.dto.library import MovieDetails
from dropsort.library.personal import (
    PersonalLibrarySection,
    PersonalMovieState,
    PersonalPreference,
    WatchEvent,
)
from dropsort.ui.movie_details.details_view import (
    MovieDetailsView,
    _format_personal_date,
)
from dropsort.ui.personal_library.personal_library_view import PersonalLibraryView
from dropsort.ui.main_window.window import MainWindow
from dropsort.application.bootstrap.desktop import LocalPersonalLibraryActions
from dropsort.ui.localization import TextId, UiLocalizer
from dropsort.application.configuration.localization import UiLanguage


NOW = datetime(2026, 8, 16, 12, tzinfo=UTC)


class ImmediateRunner:
    def submit(self, token, task, on_success, on_failure) -> None:
        try:
            on_success(token, task())
        except BaseException as error:
            on_failure(token, error)

    def wait_for_done(self) -> None:
        return None


class DeferredRunner:
    def __init__(self) -> None:
        self.pending = []
        self.waited = False

    def submit(self, token, task, on_success, on_failure) -> None:
        self.pending.append((token, task, on_success, on_failure))

    def succeed(self, value) -> None:
        token, _task, on_success, _on_failure = self.pending.pop(0)
        on_success(token, value)

    def fail(self, error: BaseException) -> None:
        token, _task, _on_success, on_failure = self.pending.pop(0)
        on_failure(token, error)

    def wait_for_done(self) -> None:
        self.waited = True


class FakePersonalActions:
    def __init__(self, movie_id: int = 1) -> None:
        self.movie_id = movie_id
        self.preference = PersonalPreference.NO_OPINION
        self.watchlisted = False
        self.history = [WatchEvent(4, movie_id, NOW, False, NOW)]
        self.calls: list[str] = []

    def _snapshot(self) -> PersonalMovieSnapshot:
        return PersonalMovieSnapshot(
            PersonalMovieState(
                self.movie_id,
                self.preference,
                NOW if self.watchlisted else None,
                len(self.history),
                self.history[-1].watched_at if self.history else None,
                NOW,
                NOW,
            ),
            tuple(self.history),
        )

    def get_personal_snapshot(self, movie_id: int) -> PersonalMovieSnapshot:
        self.calls.append("load")
        return self._snapshot()

    def set_personal_preference(self, movie_id, preference):
        self.calls.append("preference")
        self.preference = preference
        return self._snapshot()

    def clear_personal_preference(self, movie_id):
        self.calls.append("clear")
        self.preference = PersonalPreference.NO_OPINION
        return self._snapshot()

    def add_to_watchlist(self, movie_id):
        self.calls.append("add_watchlist")
        self.watchlisted = True
        return self._snapshot()

    def remove_from_watchlist(self, movie_id):
        self.calls.append("remove_watchlist")
        self.watchlisted = False
        return self._snapshot()

    def record_watch(self, movie_id, watched_at=None):
        self.calls.append("record")
        event_time = watched_at or NOW
        self.history.append(WatchEvent(len(self.history) + 5, movie_id, event_time, True, NOW))
        return self._snapshot()

    def remove_watch_event(self, event_id):
        self.calls.append("remove")
        self.history = [event for event in self.history if event.id != event_id]
        return self._snapshot()

    def list_personal_movies(self, section):
        self.calls.append(f"list:{section.value}")
        return ()


class FakeLibraryActions:
    def __init__(self, details: MovieDetails) -> None:
        self.details = details
        self.details_calls = 0

    def list_movies(self):
        return ()

    def get_movie_details(self, movie_id: int):
        self.details_calls += 1
        return self.details


def test_movie_details_renders_personal_controls_history_and_metadata_only(
    qapp, movie_details_factory
) -> None:
    actions = FakePersonalActions()
    view = MovieDetailsView(
        personal_actions=actions,
        personal_runner=ImmediateRunner(),
    )

    view.set_movie(movie_details_factory(media_files=()))

    assert view.findChild(QPushButton, "personalLikeButton") is not None
    assert view.findChild(QPushButton, "personalMarkWatchedButton") is not None
    assert view._like_button.property("role") == "preferenceAction"
    assert view._blacklist_button.property("role") == "preferenceAction"
    assert view._watchlist_button.property("role") == "secondaryAction"
    assert view._mark_watched_button.property("role") == "watchAction"
    assert view.findChild(QFrame, "personalPreferenceGroup") is not None
    assert view.findChild(QFrame, "personalWatchlistGroup") is not None
    assert view.findChild(QFrame, "personalWatchingGroup") is not None
    date_edit = view.findChild(QDateEdit, "personalWatchDateEdit")
    assert date_edit is not None
    assert date_edit.date() == QDate.currentDate()
    assert date_edit.calendarWidget().selectedDate() == QDate.currentDate()
    assert date_edit.specialValueText() == ""
    assert date_edit.isEnabled()
    assert view.media_file_count == 0
    assert not any(
        button.objectName().startswith(("playMovieButton_", "openFolderButton_"))
        for button in view.findChildren(QPushButton)
    )

    view._like_button.click()
    assert actions.preference is PersonalPreference.LIKED
    view._watchlist_button.click()
    assert actions.watchlisted is True
    view._mark_watched_button.click()
    assert actions.calls.count("record") == 1


def test_movie_details_uses_rtl_copy_without_mirroring_technical_values(
    qapp, movie_details_factory
) -> None:
    localizer = UiLocalizer(UiLanguage.ARABIC)
    try:
        view = MovieDetailsView(localizer=localizer)
        view.set_movie(movie_details_factory())
        title = view.findChild(QLabel, "detailsTitleLabel")
        path = view.findChild(QLabel, "mediaPathLabel")
        assert title is not None and title.layoutDirection() == Qt.LayoutDirection.RightToLeft
        assert path is not None and path.layoutDirection() == Qt.LayoutDirection.LeftToRight
        assert "فحص" not in title.text()
        assert view.findChild(QPushButton, "detailsBackButton").text() == "رجوع"
    finally:
        localizer.set_language(UiLanguage.ENGLISH)


def test_personal_library_view_uses_section_queries_and_stale_tokens(qapp) -> None:
    actions = FakePersonalActions()
    view = PersonalLibraryView(actions, runner=ImmediateRunner())

    view.refresh(PersonalLibrarySection.LIKED)

    assert view.current_section is PersonalLibrarySection.LIKED
    assert "list:LIKED" in actions.calls
    assert view.card_count == 0



def test_personal_library_activation_reuses_loaded_section(qapp) -> None:
    actions = FakePersonalActions()
    view = PersonalLibraryView(actions, runner=ImmediateRunner())

    view.refresh(PersonalLibrarySection.WATCHLIST)
    calls = list(actions.calls)
    view.activate()

    assert actions.calls == calls


def test_personal_library_stale_refresh_keeps_last_snapshot_painted(
    qapp, movie_item_factory
) -> None:
    actions = FakePersonalActions()
    runner = DeferredRunner()
    view = PersonalLibraryView(actions, runner=runner)
    item = movie_item_factory(media_file_count=0)

    view.refresh(PersonalLibrarySection.WATCHLIST)
    runner.succeed((item,))
    assert view._has_snapshot is True
    assert view._grid.isHidden() is False

    view.invalidate_snapshot()
    assert view._has_snapshot is True
    assert view._snapshot_stale is True
    view.activate()

    assert runner.pending
    assert view._grid.isHidden() is False
    assert view.card_count == 1
    runner.succeed((item,))
    assert view._snapshot_stale is False

def test_personal_library_revisited_tab_reuses_cache_before_background_refresh(
    qapp, movie_item_factory
) -> None:
    actions = FakePersonalActions()
    runner = DeferredRunner()
    view = PersonalLibraryView(actions, runner=runner)
    watch_item = movie_item_factory(movie_id=1, title="Watch item")
    liked_item = movie_item_factory(movie_id=2, title="Liked item")

    view.refresh(PersonalLibrarySection.WATCHLIST)
    runner.succeed((watch_item,))
    watch_card = view._grid.cards[0]
    view.refresh(PersonalLibrarySection.LIKED)
    # NO SNAPSHOT for the target tab is not the same as an empty screen: keep
    # the last painted cards until the first result for the new tab arrives.
    assert not view._grid.isHidden()
    assert view._grid.cards[0].item.movie_id == watch_item.movie_id
    assert view._snapshot_stale is False
    runner.succeed((liked_item,))
    liked_card = view._grid.cards[0]

    view._tabs.setCurrentIndex(0)

    assert not runner.pending
    assert not view._grid.isHidden()
    assert view._grid.cards[0].item.movie_id == watch_item.movie_id
    assert view._grid.cards[0] is watch_card

    view._tabs.setCurrentIndex(2)
    assert view._grid.cards[0].item.movie_id == liked_item.movie_id
    assert view._grid.cards[0] is liked_card


def test_personal_library_empty_states_are_distinct_and_descriptive(qapp) -> None:
    view = PersonalLibraryView(FakePersonalActions(), runner=ImmediateRunner())

    titles = []
    descriptions = []
    for section in (
        PersonalLibrarySection.WATCHLIST,
        PersonalLibrarySection.READY_TO_WATCH,
        PersonalLibrarySection.LIKED,
        PersonalLibrarySection.BLACKLISTED,
    ):
        view.refresh(section)
        title = view.findChild(QLabel, "personalEmptyStateTitle")
        description = view.findChild(QLabel, "personalEmptyStateDescription")
        assert title is not None and description is not None
        titles.append(title.text())
        descriptions.append(description.text())

    assert len(set(titles)) == 4
    assert len(set(descriptions)) == 4
    assert all(descriptions)


def test_personal_empty_state_retranslates_and_follows_rtl_direction(qapp) -> None:
    localizer = UiLocalizer()
    view = PersonalLibraryView(
        FakePersonalActions(),
        runner=ImmediateRunner(),
        localizer=localizer,
    )
    view.refresh(PersonalLibrarySection.WATCHLIST)
    description = view.findChild(QLabel, "personalEmptyStateDescription")
    assert description is not None
    assert description.text() == localizer.text(TextId.PERSONAL_EMPTY_WATCHLIST_DESCRIPTION)
    assert view._empty_state.accessibleName() == "Personal Library empty state"

    localizer.set_language(UiLanguage.ARABIC)
    assert qapp.layoutDirection() is Qt.LayoutDirection.RightToLeft
    assert description.text() == localizer.text(TextId.PERSONAL_EMPTY_WATCHLIST_DESCRIPTION)
    localizer.set_language(UiLanguage.ENGLISH)


def test_personal_library_view_ignores_stale_results_and_renders_failures(
    qapp, movie_item_factory
) -> None:
    actions = FakePersonalActions()
    runner = DeferredRunner()
    view = PersonalLibraryView(actions, runner=runner)

    view.refresh()
    view.invalidate_pending()
    runner.succeed((movie_item_factory(media_file_count=0),))
    assert view.card_count == 0

    view.refresh(PersonalLibrarySection.BLACKLISTED)
    runner.fail(RuntimeError("database unavailable"))
    assert "could not be loaded" in view._state.text()
    view.wait_for_pending_tasks()
    assert runner.waited is True


def test_personal_library_view_renders_cards_and_retranslates(qapp, movie_item_factory) -> None:
    actions = FakePersonalActions()
    view = PersonalLibraryView(actions, runner=ImmediateRunner())

    view._loaded(0, (movie_item_factory(media_file_count=0),))
    assert view.card_count == 1
    view.refresh()
    view._retranslate(None)
    assert view._tabs.count() == 4


def test_movie_details_personal_actions_update_authoritative_state_and_remove_history(
    qapp, movie_details_factory
) -> None:
    actions = FakePersonalActions()
    view = MovieDetailsView(personal_actions=actions, personal_runner=ImmediateRunner())
    view.set_movie(movie_details_factory(media_files=()))

    view._blacklist_button.click()
    assert actions.preference is PersonalPreference.BLACKLISTED
    view._clear_preference_button.click()
    assert actions.preference is PersonalPreference.NO_OPINION
    view._watchlist_button.click()
    assert actions.watchlisted is True
    view._watchlist_button.click()
    assert actions.watchlisted is False
    assert view._mark_watched_date_button.isEnabled() is False
    view._watch_date.setDate(QDate(2026, 8, 16))
    view._mark_watched_date_button.click()
    assert "record" in actions.calls
    remove = view.findChild(QPushButton, "removeWatchEventButton_4")
    assert remove is not None
    remove.click()
    assert "remove" in actions.calls


def test_movie_details_personal_mutation_only_marks_affected_control_busy(
    qapp, movie_details_factory
) -> None:
    actions = FakePersonalActions()
    runner = DeferredRunner()
    view = MovieDetailsView(personal_actions=actions, personal_runner=runner)
    view.set_movie(movie_details_factory(media_files=()))
    runner.succeed(actions._snapshot())

    view._like_button.click()

    assert view._personal_busy is True
    assert view._personal_busy_action == "like"
    assert not view._like_button.isEnabled()
    assert view._like_button.property("busy") is True
    assert view._blacklist_button.isEnabled()
    assert view._watchlist_button.isEnabled()
    assert view._mark_watched_button.isEnabled()
    assert view._like_button.isChecked() is False
    actions.preference = PersonalPreference.LIKED
    runner.succeed(actions._snapshot())
    assert view._personal_busy is False
    assert view._like_button.isEnabled()
    assert view._like_button.property("busy") is False
    assert view._like_button.isChecked()


def test_movie_details_personal_failure_is_user_facing_and_authoritative_controls_disable(
    qapp, movie_details_factory
) -> None:
    class FailingActions(FakePersonalActions):
        def get_personal_snapshot(self, movie_id):
            raise RuntimeError("unavailable")

    actions = FailingActions()
    view = MovieDetailsView(personal_actions=actions, personal_runner=ImmediateRunner())
    view.set_movie(movie_details_factory(media_files=()))

    assert "could not be loaded" in view._personal_error.text()
    assert not view._like_button.isEnabled()
    view.clear_movie()
    assert view._movie_id is None


def test_main_window_personal_navigation_returns_to_personal_context(
    qapp, movie_details_factory
) -> None:
    personal = FakePersonalActions()
    library = FakeLibraryActions(movie_details_factory(media_files=()))
    window = MainWindow(
        library,
        personal_actions=personal,
        task_runner=ImmediateRunner(),
        load_on_show=False,
    )

    window.show_personal_library()
    assert window.current_section == "personal"
    assert window.personal_view is not None
    window.show_movie_details(1)
    assert library.details_calls == 1
    assert window.current_section == "details"
    window._return_from_details()
    assert window.current_section == "personal"
    window.show_movie_details(1)
    assert library.details_calls == 2
    assert window.personal_view._has_snapshot is True
    window._personal_changed(1)
    assert window.current_section == "details"
    assert library.details_calls == 2
    assert window.personal_view._has_snapshot is True
    assert window.personal_view._snapshot_stale is True
    window.close()



def test_personal_library_duplicate_refresh_is_coalesced_while_query_is_pending(qapp) -> None:
    runner = DeferredRunner()
    view = PersonalLibraryView(FakePersonalActions(), runner=runner)

    view.refresh(PersonalLibrarySection.WATCHLIST)
    view.refresh(PersonalLibrarySection.WATCHLIST)
    view.activate()

    assert len(runner.pending) == 1


def test_movie_details_preference_update_keeps_history_widget_snapshot_in_place(
    qapp, movie_details_factory
) -> None:
    actions = FakePersonalActions()
    runner = DeferredRunner()
    view = MovieDetailsView(personal_actions=actions, personal_runner=runner)
    view.set_movie(movie_details_factory(media_files=()))
    runner.succeed(actions._snapshot())
    original_row = view._history_row_widgets[4][0]

    view._like_button.click()
    actions.preference = PersonalPreference.LIKED
    runner.succeed(actions._snapshot())

    assert view._history_row_widgets[4][0] is original_row
    assert view._like_button.isChecked()


def test_movie_details_remove_watch_history_deletes_only_affected_row(
    qapp, movie_details_factory
) -> None:
    actions = FakePersonalActions()
    actions.history.append(WatchEvent(5, 1, NOW.replace(hour=13), True, NOW))
    view = MovieDetailsView(personal_actions=actions, personal_runner=ImmediateRunner())
    view.set_movie(movie_details_factory(media_files=()))
    untouched_row = view._history_row_widgets[5][0]

    view._remove_watch_event(4)

    assert 4 not in view._history_row_widgets
    assert view._history_row_widgets[5][0] is untouched_row


def test_watch_date_defaults_to_today_and_legacy_1900_values_format_safely(
    qapp, movie_details_factory
) -> None:
    view = MovieDetailsView(
        personal_actions=FakePersonalActions(), personal_runner=ImmediateRunner()
    )
    view.set_movie(movie_details_factory(media_files=()))

    today = QDate.currentDate()
    assert view._watch_date.date() == today
    assert view._watch_date.calendarWidget().selectedDate() == today
    assert view._watch_date.minimumDate() == QDate(1901, 1, 1)
    assert view._selected_watch_date() == today
    assert _format_personal_date(datetime(1900, 1, 14, tzinfo=UTC)) == "Date unavailable"
    assert _format_personal_date(NOW) == "Aug 16, 2026"

    view._watch_date.setDate(QDate(2026, 8, 16))
    assert view._selected_watch_date() == QDate(2026, 8, 16)
    assert view._mark_watched_date_button.isEnabled()


def test_watch_date_resets_to_today_when_opening_another_movie(
    qapp, movie_details_factory
) -> None:
    view = MovieDetailsView(
        personal_actions=FakePersonalActions(), personal_runner=ImmediateRunner()
    )
    view.set_movie(movie_details_factory(movie_id=1, media_files=()))
    view._watch_date.setDate(QDate(2026, 8, 16))
    assert view._selected_watch_date() is not None

    view.set_movie(movie_details_factory(movie_id=2, media_files=()))

    assert view._watch_date.date() == QDate.currentDate()
    assert view._selected_watch_date() == QDate.currentDate()
    assert view._mark_watched_date_button.isEnabled()


def test_leaving_library_for_personal_clears_search_without_navigation_loop(
    qapp, movie_item_factory, movie_details_factory
) -> None:
    personal = FakePersonalActions()
    library = FakeLibraryActions(movie_details_factory(media_files=()))
    window = MainWindow(
        library,
        personal_actions=personal,
        task_runner=ImmediateRunner(),
        load_on_show=False,
    )
    window.show_library()
    window._search_field.setText("Wind")
    assert window.library_view._search_query == "Wind"

    window.show_personal_library()

    assert window.current_section == "personal"
    assert window._search_field.text() == ""
    assert window.library_view._search_query == ""
    assert not window._search_field.isEnabled()
    window.close()


def test_remove_watch_history_keeps_sidebar_search_inactive_and_details_focused(
    qapp, movie_details_factory
) -> None:
    personal = FakePersonalActions()
    library = FakeLibraryActions(movie_details_factory(media_files=()))
    window = MainWindow(
        library,
        personal_actions=personal,
        task_runner=ImmediateRunner(),
        load_on_show=False,
    )
    window.show()
    window.show_movie_details(1)
    qapp.processEvents()
    remove = window.details_view.findChild(QPushButton, "removeWatchEventButton_4")
    assert remove is not None
    assert window._search_field.text() == ""
    assert not window._search_field.isEnabled()

    remove.click()
    qapp.processEvents()

    assert window.current_section == "details"
    assert window._search_field.text() == ""
    assert not window._search_field.hasFocus()
    assert window.details_view.hasFocus()
    window.close()

def test_personal_composition_adapter_routes_every_personal_action() -> None:
    state = FakePersonalActions()._snapshot()
    event = state.history[0]

    class UseCase:
        def __init__(self, value) -> None:
            self.value = value
            self.calls = []

        def execute(self, *args, **kwargs):
            self.calls.append((args, kwargs))
            return self.value

    usecases = [UseCase(state.state), UseCase(state.history), UseCase(state.state), UseCase(state.state)]
    adapter = LocalPersonalLibraryActions(
        _state=usecases[0],
        _history=usecases[1],
        _set_preference=usecases[2],
        _clear_preference=usecases[3],
        _add_watchlist=UseCase(state.state),
        _remove_watchlist=UseCase(state.state),
        _record_watch=UseCase(event),
        _remove_watch=UseCase(event),
        _list_movies=UseCase(()),
    )

    assert adapter.get_personal_snapshot(1).state == state.state
    assert adapter.set_personal_preference(1, PersonalPreference.LIKED).state == state.state
    assert adapter.clear_personal_preference(1).state == state.state
    assert adapter.add_to_watchlist(1).state == state.state
    assert adapter.remove_from_watchlist(1).state == state.state
    assert adapter.record_watch(1).state == state.state
    assert adapter.remove_watch_event(event.id).state == state.state
    assert adapter.list_personal_movies(PersonalLibrarySection.WATCHLIST) == ()
