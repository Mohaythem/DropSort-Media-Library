from __future__ import annotations

from datetime import UTC, datetime
from pathlib import Path

import pytest
from PySide6.QtCore import QDate, QLocale, Qt
from PySide6.QtWidgets import QAbstractButton, QCheckBox, QDateEdit, QFileDialog, QFrame, QLabel

from dropsort.application.configuration.localization import UiLanguage
from dropsort.application.configuration.theme import UiTheme
from dropsort.application.dto.operation_history import OperationHistoryQuery
from dropsort.application.use_cases.operation_history import SaveOperationHistory
from dropsort.ui.common.formatting import (
    format_date,
    format_datetime,
    format_file_size,
    format_rating,
    format_runtime,
    to_western_numerals,
)
from dropsort.ui.common.theme import THEMES, application_stylesheet
from dropsort.ui.history.view import ElidedPathLabel, OperationHistoryView, _operation_text, _status_text
from dropsort.ui.localization import TextId, UiLocalizer
from dropsort.ui.movie_details.details_view import MovieDetailsView
from dropsort.ui.settings.settings_view import SettingsView
from tests.unit.ui.test_operation_history_view import (
    FakeHistoryActions,
    ImmediateRunner,
    _item,
)
from tests.unit.ui.test_personal_library_ui import FakePersonalActions
from tests.unit.ui.test_settings_view import FakeSettingsActions


def _contains_eastern_digits(value: str) -> bool:
    return any(character in value for character in "٠١٢٣٤٥٦٧٨٩۰۱۲۳۴۵۶۷۸۹")


def test_numeric_formatting_normalizes_to_western_digits() -> None:
    value = datetime(2026, 8, 16, 23, 31, 0, tzinfo=UTC)
    outputs = (
        to_western_numerals("٢٠٢٦-٠٨-١٦ ٧.٨"),
        format_date(value),
        format_datetime(value),
        format_rating(7.8),
        format_runtime(126),
        format_file_size(1_400_000_000),
    )

    assert all(not _contains_eastern_digits(output) for output in outputs)
    assert outputs[0] == "2026-08-16 7.8"
    assert outputs[1] == "Aug 16, 2026"
    assert outputs[2].startswith("2026-08-17")


def test_numeric_formatting_has_explicit_unavailable_states() -> None:
    assert format_date(None) == "Date unavailable"
    assert format_datetime(None) == "Timestamp unavailable"


def test_operation_export_is_human_readable_and_does_not_prune_journal(
    tmp_path: Path,
) -> None:
    destination = tmp_path / "operations.txt"
    item = _item(movie_title=None)
    SaveOperationHistory().execute((item,), destination)

    text = destination.read_text(encoding="utf-8")
    assert "DropSort Operations Log" in text
    assert "Move — Completed" in text
    assert "Unlinked media operation" in text
    assert "D:\\Incoming\\Movie.mkv" in text
    with pytest.raises(ValueError):
        SaveOperationHistory().execute((), "")


def test_slate_uses_coherent_semantic_surface_roles(qapp) -> None:
    slate = THEMES[UiTheme.SLATE]
    assert slate.background != "#000000"
    assert len({slate.background, slate.surface, slate.surface_raised, slate.card}) >= 3
    stylesheet = application_stylesheet(UiTheme.SLATE)
    for role in (slate.background, slate.surface, slate.surface_raised, slate.card, slate.sidebar):
        assert role in stylesheet


def test_date_picker_is_single_field_native_calendar_control(
    qapp, movie_details_factory
) -> None:
    view = MovieDetailsView(
        personal_actions=FakePersonalActions(),
        personal_runner=ImmediateRunner(),
    )
    view.set_movie(movie_details_factory(media_files=()))

    picker = view.findChild(QDateEdit, "personalWatchDateEdit")
    assert picker is not None
    assert picker.calendarPopup()
    assert picker.displayFormat() == "MMM d, yyyy"
    assert picker.layoutDirection() is Qt.LayoutDirection.LeftToRight
    assert picker.locale().language() == QLocale.Language.English
    assert picker.accessibleName() == "Watched date"
    assert view.findChild(QAbstractButton, "personalWatchDateCalendarButton") is not None

    picker.setDate(QDate(2026, 8, 16))
    assert picker.date().toString("yyyy-MM-dd") == "2026-08-16"


def test_date_picker_retranslates_accessible_label_without_changing_watch_semantics(
    qapp, movie_details_factory
) -> None:
    localizer = UiLocalizer()
    actions = FakePersonalActions()
    view = MovieDetailsView(
        personal_actions=actions,
        personal_runner=ImmediateRunner(),
        localizer=localizer,
    )
    view.set_movie(movie_details_factory(media_files=()))
    picker = view.findChild(QDateEdit, "personalWatchDateEdit")
    assert picker is not None
    picker.setDate(QDate(2026, 8, 16))

    localizer.set_language(UiLanguage.ARABIC)
    assert picker.accessibleName() == localizer.text(TextId.DETAILS_WATCH_DATE)
    assert picker.layoutDirection() is Qt.LayoutDirection.LeftToRight
    assert not _contains_eastern_digits(picker.text())

    view._mark_watched_date_button.click()
    assert actions.calls.count("record") == 1
    assert actions.history[-1].watched_at.date().isoformat() == "2026-08-16"
    localizer.set_language(UiLanguage.ENGLISH)


def test_operations_log_queries_and_displays_at_most_latest_500(qapp) -> None:
    actions = FakeHistoryActions(
        items=tuple(_item(operation_id=f"operation-{index}") for index in range(501))
    )
    captured: list[OperationHistoryQuery] = []
    original = actions.list_operation_history

    def list_history(query=None):
        captured.append(query)
        return original(query)

    actions.list_operation_history = list_history
    view = OperationHistoryView(actions, runner=ImmediateRunner())
    view.refresh()

    assert captured[0].limit == 500
    assert view.row_count == 500
    state = view.findChild(QLabel, "operationHistoryState_operation-1")
    assert state is not None and "Completed" in state.text()
    view.refresh()
    assert view.row_count == 500


def test_operations_log_bounds_long_paths_but_keeps_full_tooltip(qapp) -> None:
    source = "D:\\incoming\\" + ("very-long-folder-name\\" * 15) + "movie.mkv"
    destination = "D:\\library\\" + ("another-long-folder-name\\" * 15) + "movie.mkv"
    item = _item(source_path=source, destination_path=destination)
    actions = FakeHistoryActions(items=(item,))
    view = OperationHistoryView(actions, runner=ImmediateRunner())
    view.resize(520, 420)
    view.refresh()

    path = view.findChild(QLabel, "operationHistoryPath_operation-1")
    assert path is not None
    assert path.toolTip().startswith("From:")
    assert source in path.toolTip()
    assert destination in path.toolTip()


def test_operations_log_copy_matches_save_export_for_complete_log(qapp, tmp_path) -> None:
    actions = FakeHistoryActions()
    view = OperationHistoryView(actions, runner=ImmediateRunner())
    view.refresh()
    view.copy_selected()

    clipboard = qapp.clipboard().text()
    destination = tmp_path / "operations.txt"
    SaveOperationHistory().execute(actions.items, destination)
    assert clipboard == destination.read_text(encoding="utf-8")
    assert clipboard.startswith("DropSort Operations Log\n")
    assert "D:\\Incoming\\Movie.mkv" in clipboard
    assert "D:\\Movies\\Movie.mkv" in clipboard
    assert view._state.text() == "Operations log copied to the clipboard."


def test_operations_log_copy_and_save_empty_or_cancelled_states(
    monkeypatch, qapp
) -> None:
    empty = OperationHistoryView(
        FakeHistoryActions(items=()), runner=ImmediateRunner()
    )
    empty.refresh()
    empty.copy_selected()
    assert "No file operations yet" in empty._state.text()
    empty.save_log()
    assert "No file operations yet" in empty._state.text()

    actions = FakeHistoryActions()
    view = OperationHistoryView(actions, runner=ImmediateRunner())
    view.refresh()
    view.copy_selected()
    assert view._state.text() == "Operations log copied to the clipboard."
    monkeypatch.setattr(QFileDialog, "getSaveFileName", lambda *_a, **_k: ("", ""))
    view.save_log()
    assert view._state.text() == "Operations log copied to the clipboard."


def test_operations_log_save_reports_missing_or_failing_action(monkeypatch, qapp) -> None:
    destination = "C:/does-not-matter/operations.txt"
    monkeypatch.setattr(
        QFileDialog, "getSaveFileName", lambda *_a, **_k: (destination, "")
    )

    view = OperationHistoryView(FakeHistoryActions(), runner=ImmediateRunner())
    view.refresh()
    view.save_log()
    assert view._state.text() == "The operations log could not be saved."

    actions = FakeHistoryActions()
    actions.save_operation_history = lambda *_a, **_k: (_ for _ in ()).throw(
        OSError("blocked")
    )
    failing = OperationHistoryView(actions, runner=ImmediateRunner())
    failing.refresh()
    failing.save_log()
    assert failing._state.text() == "The operations log could not be saved."


def test_operations_log_helpers_and_elision_are_direction_safe(qapp) -> None:
    localizer = UiLocalizer()
    assert _operation_text(localizer, "RENAME") == "Rename"
    assert _operation_text(localizer, "OTHER") == "Other"
    assert _status_text(localizer, "RECOVERY_REQUIRED") == "Recovery required"
    assert _status_text(localizer, "OTHER") == "Other"
    label = ElidedPathLabel("From: " + ("long-path\\" * 20) + "movie.mkv")
    label.resize(120, 24)
    label.show()
    qapp.processEvents()
    assert label.toolTip().startswith("From:")
    assert "…" in label.text() or label.text() == label.toolTip()


def test_operations_log_save_uses_action_and_standard_dialog(
    monkeypatch, qapp, tmp_path: Path
) -> None:
    actions = FakeHistoryActions()
    saved: list[tuple[tuple[object, ...], str]] = []
    actions.save_operation_history = lambda items, path: saved.append((items, path))
    destination = str(tmp_path / "operations.txt")
    monkeypatch.setattr(
        QFileDialog,
        "getSaveFileName",
        lambda *_args, **_kwargs: (destination, "Text files (*.txt)"),
    )
    view = OperationHistoryView(actions, runner=ImmediateRunner())
    view.refresh()
    view.save_log()

    assert saved == [((actions.items[0],), destination)]
    assert view._state.text() == "Operations log saved."


def test_clear_library_uses_danger_zone_without_changing_confirmation(
    qapp,
) -> None:
    view = SettingsView(FakeSettingsActions())
    danger = view.findChild(QFrame, "clearLibraryDangerZone")
    assert danger is not None
    assert danger.property("role") == "dangerZone"
    heading = view.findChild(QLabel, "dangerZoneHeading")
    assert heading is not None
    assert heading.text() == "Danger Zone"
