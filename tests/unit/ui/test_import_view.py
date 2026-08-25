from __future__ import annotations

from dataclasses import dataclass, field
from pathlib import Path
from typing import Callable
from types import SimpleNamespace

import pytest
from PySide6.QtCore import QEvent, QObject, Qt
from PySide6.QtGui import QWindow
from PySide6.QtTest import QTest
from PySide6.QtWidgets import (
    QApplication,
    QComboBox,
    QFileDialog,
    QLabel,
    QMainWindow,
    QScrollArea,
    QWidget,
)

from dropsort.application.dto.catalog import MovieFileIngestionResult
from dropsort.application.dto.import_review import (
    ImportReviewProgress,
    ImportReviewSession,
    ImportReviewStage,
    ImportReviewSummary,
)
from dropsort.application.dto.movie_import import ConfirmMovieImportCommand, ImportProposalStatus
from dropsort.application.dto.movie_import import ImportProposalReason, MovieImportProposal
from dropsort.application.errors import (
    ImportReviewCancelled,
    MovieImportCatalogError,
    MovieImportMetadataError,
)
from dropsort.media.discovery.errors import DiscoveryRootError
from dropsort.media.discovery.models import (
    DiscoveryClassification,
    DiscoveryErrorCode,
    DiscoveryIssue,
    DiscoveredMedia,
)
from dropsort.media.parser import MediaType, ParsedMedia
from dropsort.media.matcher.models import (
    CandidateScore,
    MatchDecision,
    MatchReason,
    MatchStatus,
)
from dropsort.ui.scan.import_view import ImportView


_TRANSIENT_CONTROL_NAMES = {
    "candidateSelector",
    "confirmImportButton",
    "dismissProposalButton",
    "editSearchButton",
    "openMetadataSettingsButton",
}


class _TransientImportControlProbe(QObject):
    def __init__(self, main_window: QMainWindow) -> None:
        super().__init__()
        self._main_window = main_window
        self.logical_top_level_shows: list[str] = []
        self.native_window_shows: list[str] = []
        self.main_activation_events: list[QEvent.Type] = []

    def eventFilter(self, watched: QObject, event: QEvent) -> bool:
        event_type = event.type()
        if event_type is QEvent.Type.Show:
            name = watched.objectName()
            if (
                isinstance(watched, QWidget)
                and name in _TRANSIENT_CONTROL_NAMES
                and watched.isWindow()
            ):
                self.logical_top_level_shows.append(name)
            if (
                isinstance(watched, QWindow)
                and watched.parent() is None
            ):
                self.native_window_shows.append(name or type(watched).__name__)
        if watched is self._main_window and event_type in {
            QEvent.Type.WindowActivate,
            QEvent.Type.WindowDeactivate,
        }:
            self.main_activation_events.append(event_type)
        return False


class ImmediateRunner:
    def submit(self, token: int, task, on_success, on_failure) -> None:
        try:
            on_success(token, task())
        except BaseException as error:
            on_failure(token, error)


@dataclass
class PendingTask:
    token: int
    task: Callable[[], object]
    on_success: Callable[[int, object], None]
    on_failure: Callable[[int, BaseException], None]


class DeferredRunner:
    def __init__(self) -> None:
        self.tasks: list[PendingTask] = []

    def submit(self, token: int, task, on_success, on_failure) -> None:
        self.tasks.append(PendingTask(token, task, on_success, on_failure))


@dataclass
class FakeImportActions:
    session: ImportReviewSession
    prepare_error: BaseException | None = None
    confirm_error: BaseException | None = None
    prepare_calls: list[tuple[Path, bool]] = field(default_factory=list)
    confirmations: list[ConfirmMovieImportCommand] = field(default_factory=list)
    enrichments: list[
        tuple[ConfirmMovieImportCommand, MovieFileIngestionResult]
    ] = field(default_factory=list)

    def prepare_import_review(
        self,
        root: Path,
        recursive: bool,
        *,
        progress=None,
        cancellation=None,
    ) -> ImportReviewSession:
        self.prepare_calls.append((root, recursive))
        if self.prepare_error:
            raise self.prepare_error
        return self.session

    def register_movie_import(self, command: ConfirmMovieImportCommand) -> object:
        self.confirmations.append(command)
        if self.confirm_error:
            raise self.confirm_error
        return MovieFileIngestionResult(
            movie=SimpleNamespace(id=99), media_file=SimpleNamespace(id=100)
        )

    def enrich_movie_import(
        self,
        command: ConfirmMovieImportCommand,
        registration: MovieFileIngestionResult,
    ) -> MovieFileIngestionResult:
        self.enrichments.append((command, registration))
        return registration

    def confirm_movie_import(self, command: ConfirmMovieImportCommand) -> object:
        registration = self.register_movie_import(command)
        assert isinstance(registration, MovieFileIngestionResult)
        return self.enrich_movie_import(command, registration)


def _session(root: Path, *proposals) -> ImportReviewSession:
    statuses = [proposal.status for proposal in proposals]
    return ImportReviewSession(
        root=root,
        recursive=True,
        items=tuple(proposals),
        summary=ImportReviewSummary(
            entries_seen=len(proposals),
            supported_media_found=len(proposals),
            movie_candidates=len(proposals),
            already_in_library=statuses.count(ImportProposalStatus.ALREADY_IN_LIBRARY),
            ready_for_review=sum(
                status in {
                    ImportProposalStatus.MATCH_PROPOSED,
                    ImportProposalStatus.REVIEW_REQUIRED,
                }
                for status in statuses
            ),
            no_match=statuses.count(ImportProposalStatus.NO_MATCH),
            metadata_unavailable=statuses.count(
                ImportProposalStatus.METADATA_UNAVAILABLE
            ),
        ),
    )


def test_folder_picker_cancel_performs_no_work(qapp: QApplication, proposal_factory) -> None:
    root = Path.cwd() / "movies"
    actions = FakeImportActions(_session(root, proposal_factory()))
    view = ImportView(actions, runner=ImmediateRunner(), folder_picker=lambda _parent: "")

    view.choose_folder()

    assert actions.prepare_calls == []
    assert view.row_count == 0


def test_folder_picker_selection_starts_scan(qapp: QApplication, proposal_factory) -> None:
    root = Path.cwd() / "movies"
    actions = FakeImportActions(_session(root, proposal_factory()))
    view = ImportView(
        actions,
        runner=ImmediateRunner(),
        folder_picker=lambda _parent: str(root),
    )

    view.choose_folder()

    assert actions.prepare_calls == [(root, True)]


def test_scan_shows_match_review_and_non_importable_states(
    qapp: QApplication,
    proposal_factory,
    discovery_factory,
) -> None:
    root = Path.cwd() / "movies"
    proposals = (
        proposal_factory(discovery=discovery_factory(path=root / "one.mkv")),
        proposal_factory(
            status=ImportProposalStatus.REVIEW_REQUIRED,
            discovery=discovery_factory(path=root / "two.mkv"),
        ),
        proposal_factory(
            status=ImportProposalStatus.NO_MATCH,
            discovery=discovery_factory(path=root / "three.mkv"),
        ),
        proposal_factory(
            status=ImportProposalStatus.METADATA_UNAVAILABLE,
            discovery=discovery_factory(path=root / "four.mkv"),
        ),
        proposal_factory(
            status=ImportProposalStatus.ALREADY_IN_LIBRARY,
            discovery=discovery_factory(path=root / "five.mkv"),
        ),
    )
    actions = FakeImportActions(_session(root, *proposals))
    view = ImportView(actions, runner=ImmediateRunner())

    view.start_scan(root)

    assert actions.prepare_calls == [(root, True)]
    assert view.row_count == 4
    assert [row.proposal.status for row in view.rows] == [proposal.status for proposal in proposals if proposal.status is not ImportProposalStatus.ALREADY_IN_LIBRARY]
    assert "Scan complete" in view.state_message
    assert "Ready to add/review: 2" in view.state_message


def test_empty_scan_has_clear_empty_state(qapp: QApplication) -> None:
    root = Path.cwd() / "empty"
    view = ImportView(FakeImportActions(_session(root)), runner=ImmediateRunner())

    view.start_scan(root)

    assert view.row_count == 0
    assert view.state_message == "Scan complete. No movie candidates found in this folder."


def test_skipped_and_error_discoveries_are_explained_without_import_controls(
    qapp: QApplication,
) -> None:
    root = Path.cwd() / "mixed"
    tv_discovery = DiscoveredMedia(
        path=root / "Show.S01E02.mkv",
        file_size=10,
        parsed_media=ParsedMedia(
            "Show.S01E02.mkv", MediaType.TV_EPISODE, "Show", None, None, None, None, ".mkv"
        ),
        classification=DiscoveryClassification.TV_EPISODE_SKIPPED,
        issue=None,
    )
    error_discovery = DiscoveredMedia.error(
        root / "broken.mkv",
        DiscoveryIssue(DiscoveryErrorCode.STAT_FAILED, "technical stat detail"),
    )
    proposals = (
        MovieImportProposal(
            ImportProposalStatus.NO_MATCH,
            tv_discovery,
            (),
            None,
            None,
            (ImportProposalReason.TV_EPISODE_NOT_SUPPORTED,),
            None,
        ),
        MovieImportProposal(
            ImportProposalStatus.NO_MATCH,
            error_discovery,
            (),
            None,
            None,
            (ImportProposalReason.DISCOVERY_ERROR,),
            None,
        ),
    )
    view = ImportView(FakeImportActions(_session(root, *proposals)), runner=ImmediateRunner())

    view.start_scan(root)

    assert [row.status_text for row in view.rows] == ["TV episode skipped", "Scan error"]
    assert all(row.can_import is False for row in view.rows)
    assert "technical stat detail" not in view.rows[1].explanation_text


def test_metadata_authentication_failure_is_actionable(
    qapp: QApplication,
    proposal_factory,
) -> None:
    root = Path.cwd() / "movies"
    proposal = proposal_factory(
        status=ImportProposalStatus.METADATA_UNAVAILABLE,
        reasons=(ImportProposalReason.METADATA_AUTHENTICATION,),
    )
    view = ImportView(FakeImportActions(_session(root, proposal)), runner=ImmediateRunner())

    view.start_scan(root)

    requests: list[bool] = []
    view.settings_requested.connect(lambda: requests.append(True))

    assert view.rows[0].explanation_text == ""
    assert view.rows[0].findChild(QLabel, "importExplanationLabel") is None
    view.rows[0].settings_button.click()

    assert requests == [True]


def test_matched_proposal_performs_zero_catalog_writes_until_explicit_click(
    qapp: QApplication,
    proposal_factory,
) -> None:
    root = Path.cwd() / "movies"
    actions = FakeImportActions(_session(root, proposal_factory()))
    view = ImportView(actions, runner=ImmediateRunner())
    refreshed: list[int] = []
    view.catalog_changed.connect(refreshed.append)

    view.start_scan(root)
    assert actions.confirmations == []

    QTest.mouseClick(view.rows[0].import_button, Qt.MouseButton.LeftButton)

    assert len(actions.confirmations) == 1
    assert refreshed == [99, 99]
    assert len(actions.enrichments) == 1
    assert view.row_count == 0
    assert "All done" in view.findChild(QLabel, "importQueueEmptyLabel").text()


def test_local_registration_is_published_before_deferred_enrichment(
    qapp: QApplication,
    proposal_factory,
) -> None:
    root = Path.cwd() / "movies"
    actions = FakeImportActions(_session(root, proposal_factory()))
    runner = DeferredRunner()
    view = ImportView(actions, runner=runner)
    refreshed: list[int] = []
    view.catalog_changed.connect(refreshed.append)

    view.start_scan(root)
    scan_task = runner.tasks[0]
    scan_task.on_success(scan_task.token, scan_task.task())
    QTest.mouseClick(view.rows[0].import_button, Qt.MouseButton.LeftButton)

    registration_task = runner.tasks[1]
    registration = registration_task.task()
    registration_task.on_success(registration_task.token, registration)

    assert view.row_count == 0
    assert refreshed == [99]
    assert actions.enrichments == []
    assert len(runner.tasks) == 3

    enrichment_task = runner.tasks[2]
    enrichment = enrichment_task.task()
    enrichment_task.on_success(enrichment_task.token, enrichment)

    assert refreshed == [99, 99]
    assert len(actions.enrichments) == 1


def test_failed_import_is_friendly_and_can_be_retried(
    qapp: QApplication,
    proposal_factory,
) -> None:
    root = Path.cwd() / "movies"
    actions = FakeImportActions(
        _session(root, proposal_factory()),
        confirm_error=MovieImportCatalogError("technical database text"),
    )
    view = ImportView(actions, runner=ImmediateRunner())
    view.start_scan(root)

    QTest.mouseClick(view.rows[0].import_button, Qt.MouseButton.LeftButton)

    assert view.rows[0].can_import is True
    assert "could not add" in view.rows[0].status_text.casefold()
    assert "technical" not in view.rows[0].status_text.casefold()


def test_dismiss_removes_only_the_review_row_and_shows_all_done(
    qapp: QApplication,
    proposal_factory,
) -> None:
    root = Path.cwd() / "movies"
    actions = FakeImportActions(_session(root, proposal_factory()))
    view = ImportView(actions, runner=ImmediateRunner())
    view.start_scan(root)
    row = view.rows[0]

    row.dismiss_button.click()

    assert view.row_count == 0
    assert "All done" in view.findChild(QLabel, "importQueueEmptyLabel").text()
    assert actions.confirmations == []


def test_invalid_dismiss_payload_and_duplicate_removal_are_ignored(
    qapp: QApplication,
    proposal_factory,
) -> None:
    root = Path.cwd() / "movies"
    view = ImportView(FakeImportActions(_session(root, proposal_factory())), runner=ImmediateRunner())
    view.start_scan(root)
    row = view.rows[0]

    view._dismiss_row(object())
    view._remove_row(row)
    view._remove_row(row)

    assert view.row_count == 0


def test_metadata_and_unexpected_import_failures_are_friendly(
    qapp: QApplication,
    proposal_factory,
) -> None:
    root = Path.cwd() / "movies"
    for error, expected in (
        (MovieImportMetadataError("raw provider detail"), "details are unavailable"),
        (RuntimeError("raw unexpected detail"), "could not complete"),
    ):
        actions = FakeImportActions(_session(root, proposal_factory()), confirm_error=error)
        view = ImportView(actions, runner=ImmediateRunner())
        view.start_scan(root)

        view.rows[0].import_button.click()

        assert expected in view.rows[0].status_text.casefold()
        assert "raw" not in view.rows[0].status_text.casefold()


def test_root_failure_is_translated_without_raw_details(qapp: QApplication) -> None:
    root = Path.cwd() / "missing"
    error = DiscoveryRootError(root, DiscoveryErrorCode.ROOT_MISSING, "raw path failure")
    actions = FakeImportActions(_session(root), prepare_error=error)
    view = ImportView(actions, runner=ImmediateRunner())

    view.start_scan(root)

    assert view.row_count == 0
    assert "selected folder" in view.state_message.casefold()
    assert "raw path failure" not in view.state_message


def test_stale_scan_result_cannot_replace_newer_session(
    qapp: QApplication,
    proposal_factory,
) -> None:
    first_root = Path.cwd() / "first"
    second_root = Path.cwd() / "second"
    actions = FakeImportActions(_session(second_root, proposal_factory()))
    runner = DeferredRunner()
    view = ImportView(actions, runner=runner)

    view.start_scan(first_root)
    first_task = runner.tasks[0]
    view.cancel_scan()
    first_task.on_failure(
        first_task.token,
        ImportReviewCancelled(
            ImportReviewProgress(ImportReviewStage.DISCOVERING)
        ),
    )
    view.start_scan(second_root)
    assert len(runner.tasks) == 2

    first_task.on_success(first_task.token, _session(first_root, proposal_factory()))
    assert view.row_count == 0

    runner.tasks[1].on_success(runner.tasks[1].token, _session(second_root, proposal_factory()))
    assert view.row_count == 1


def test_invalid_scan_result_is_controlled_and_scan_busy_state_is_visible(
    qapp: QApplication,
    proposal_factory,
) -> None:
    root = Path.cwd() / "movies"
    runner = DeferredRunner()
    view = ImportView(FakeImportActions(_session(root, proposal_factory())), runner=runner)

    view.start_scan(root)
    assert view.is_busy is True
    runner.tasks[0].on_success(runner.tasks[0].token, object())

    assert view.is_busy is False
    assert "invalid scan result" in view.state_message.casefold()


def test_starting_new_scan_clears_previous_review_rows(
    qapp: QApplication,
    proposal_factory,
) -> None:
    root = Path.cwd() / "movies"
    actions = FakeImportActions(_session(root, proposal_factory()))
    view = ImportView(actions, runner=ImmediateRunner())
    view.start_scan(root)
    assert view.row_count == 1

    actions.session = _session(root)
    view.start_scan(root)

    assert view.row_count == 0


def test_stale_scan_failure_and_stale_import_result_are_ignored(
    qapp: QApplication,
    proposal_factory,
) -> None:
    root = Path.cwd() / "movies"
    runner = DeferredRunner()
    view = ImportView(FakeImportActions(_session(root, proposal_factory())), runner=runner)
    view.start_scan(root)
    old_scan = runner.tasks[0]
    view.cancel_scan()
    old_scan.on_failure(
        old_scan.token,
        ImportReviewCancelled(
            ImportReviewProgress(ImportReviewStage.DISCOVERING)
        ),
    )
    view.start_scan(root)

    old_scan.on_failure(old_scan.token, RuntimeError("old"))
    assert view.is_busy is True

    runner.tasks[1].on_success(runner.tasks[1].token, _session(root, proposal_factory()))
    view.rows[0].import_button.click()
    import_task = runner.tasks[2]
    view.start_scan(root)
    import_task.on_success(import_task.token, object())

    assert view.row_count == 0


def test_invalid_confirmation_payload_is_rejected_in_ui(
    qapp: QApplication,
    proposal_factory,
) -> None:
    root = Path.cwd() / "movies"
    view = ImportView(
        FakeImportActions(_session(root, proposal_factory())),
        runner=ImmediateRunner(),
    )
    view.start_scan(root)
    row = view.rows[0]

    row.confirm_requested.emit(object(), object())

    assert row.status_text == "The selected candidate is invalid. Please rescan."


def test_result_after_view_close_is_ignored_safely(
    qapp: QApplication,
    proposal_factory,
) -> None:
    root = Path.cwd() / "movies"
    runner = DeferredRunner()
    view = ImportView(
        FakeImportActions(_session(root, proposal_factory())),
        runner=runner,
    )
    view.start_scan(root)

    view.show()
    qapp.processEvents()
    view.close()
    runner.tasks[0].on_success(runner.tasks[0].token, _session(root, proposal_factory()))

    assert view.row_count == 0


def test_non_recursive_choice_is_passed_to_application(
    qapp: QApplication,
    proposal_factory,
) -> None:
    root = Path.cwd() / "movies"
    actions = FakeImportActions(_session(root, proposal_factory()))
    view = ImportView(actions, runner=ImmediateRunner())
    view.recursive_checkbox.setChecked(False)

    view.start_scan(root)

    assert actions.prepare_calls == [(root, False)]


def test_import_row_uses_compact_placeholders_for_missing_year_and_resolution(
    qapp: QApplication,
    tmp_path: Path,
    proposal_factory,
    discovery_factory,
) -> None:
    root = tmp_path / "movies"
    parsed = ParsedMedia(
        "Unknown.Movie.mkv",
        MediaType.MOVIE,
        "Unknown Movie",
        None,
        None,
        None,
        None,
        ".mkv",
    )
    proposal = proposal_factory(
        discovery=discovery_factory(
            path=root / "Unknown.Movie.mkv",
            parsed_media=parsed,
        )
    )
    view = ImportView(
        FakeImportActions(_session(root, proposal)), runner=ImmediateRunner()
    )

    view.start_scan(root)

    row = view.rows[0]
    assert row.year_label.text() == "--"
    assert row.resolution_label.text() == "--"


def _review_proposal(proposal_factory, candidates) -> MovieImportProposal:
    decision = MatchDecision(
        status=MatchStatus.REVIEW_REQUIRED,
        candidate=candidates[0],
        confidence=0.86,
        reasons=(MatchReason.TITLE_STRONG, MatchReason.YEAR_EXACT),
        ranked_candidates=tuple(
            CandidateScore(
                candidate=candidate,
                score=0.86 - index * 0.05,
                reasons=(MatchReason.TITLE_STRONG,),
                penalties=(),
            )
            for index, candidate in enumerate(candidates)
        ),
    )
    return proposal_factory(
        status=ImportProposalStatus.REVIEW_REQUIRED,
        candidate=candidates[0],
        candidates=tuple(candidates),
        proposed_candidate=candidates[0],
        match_decision=decision,
    )


def test_metadata_unavailable_actions_have_usable_unclipped_geometry(
    qapp: QApplication,
    tmp_path: Path,
    proposal_factory,
) -> None:
    root = tmp_path / "movies"
    proposal = proposal_factory(
        status=ImportProposalStatus.METADATA_UNAVAILABLE,
        reasons=(ImportProposalReason.METADATA_AUTHENTICATION,),
    )
    view = ImportView(
        FakeImportActions(_session(root, proposal)), runner=ImmediateRunner()
    )
    view.resize(1400, 850)
    view.show()
    view.start_scan(root)
    qapp.processEvents()

    row = view.rows[0]
    buttons = (
        row.import_button,
        row.manual_search_button,
        row.settings_button,
        row.dismiss_button,
    )
    assert all(button.isVisible() for button in buttons)
    button_heights = [button.height() for button in buttons]
    assert len(set(button_heights)) == 1, button_heights
    assert button_heights[0] >= 38
    assert all(
        button.width() >= button.sizeHint().width()
        for button in (row.import_button, row.manual_search_button)
    )
    assert row.import_button.text() == "Add"
    assert row.manual_search_button.text() == "Search"
    assert row.settings_button.text() == ""
    assert row.settings_button.toolTip() == "Open Settings"
    assert row.dismiss_button.text() == ""
    assert row.settings_button.width() == row.settings_button.height() == 38
    assert row.dismiss_button.width() == row.dismiss_button.height() == 38
    assert not row.settings_button.icon().isNull()
    assert not row.dismiss_button.icon().isNull()
    assert (
        row.settings_button.contentsRect().center()
        == row.settings_button.rect().center()
    )
    assert (
        row.dismiss_button.contentsRect().center()
        == row.dismiss_button.rect().center()
    )
    action_host = row.findChild(QWidget, "importActionHost")
    assert action_host.width() == 272
    assert (
        row.dismiss_button.geometry().right()
        <= action_host.contentsRect().right()
    )
    assert row.findChild(QLabel, "importStatusLabel").width() == 156
    assert row.findChild(QLabel, "importStatusLabel").wordWrap() is True


def test_multiple_candidates_stay_inside_the_information_columns(
    qapp: QApplication,
    tmp_path: Path,
    proposal_factory,
    candidate_factory,
) -> None:
    root = tmp_path / "movies"
    candidates = tuple(
        candidate_factory(
            external_id=str(index),
            title=(
                "A Very Long Candidate Movie Title That Must Remain Bounded "
                f"Version {index}"
            ),
            year=None if index == 2 else 2000 + index,
        )
        for index in range(1, 4)
    )
    proposal = _review_proposal(proposal_factory, candidates)
    view = ImportView(
        FakeImportActions(_session(root, proposal)), runner=ImmediateRunner()
    )
    view.resize(1400, 850)
    view.show()
    view.start_scan(root)
    qapp.processEvents()

    row = view.rows[0]
    selector = row.findChild(QComboBox, "candidateSelector")
    action_host = row.findChild(QWidget, "importActionHost")
    assert all(widget is not None for widget in (selector, action_host))
    assert row.findChild(QWidget, "importCandidateHost") is None
    assert row.findChild(QLabel, "candidateDetailsLabel") is None
    assert selector.count() == 3
    assert selector.isVisible()
    assert "(--)" in selector.itemText(1)
    assert selector.itemText(1).endswith("8.5/10")
    assert selector.itemData(0, Qt.ItemDataRole.ToolTipRole) == selector.itemText(0)
    visible_candidate_text = " ".join(
        selector.itemText(index) for index in range(selector.count())
    )
    assert "TMDB" not in visible_candidate_text
    assert "Confidence" not in visible_candidate_text
    assert "Title Strong" not in visible_candidate_text
    assert row.findChild(QLabel, "importExplanationLabel") is None
    assert row.findChild(QLabel, "importPathLabel") is None
    assert row.findChild(QLabel, "importFilenameLabel") is None
    candidate_right = selector.mapTo(row, selector.rect().topLeft()).x() + selector.width()
    action_left = action_host.mapTo(row, action_host.rect().topLeft()).x()
    assert candidate_right <= action_left
    assert selector.parentWidget() is row


def test_add_movies_uses_one_page_scroll_without_nested_results_scroll(
    qapp: QApplication,
    tmp_path: Path,
    proposal_factory,
    discovery_factory,
) -> None:
    root = tmp_path / "movies"
    proposals = tuple(
        proposal_factory(
            status=ImportProposalStatus.NO_MATCH,
            discovery=discovery_factory(path=root / f"movie-{index}.mkv"),
        )
        for index in range(8)
    )
    view = ImportView(
        FakeImportActions(_session(root, *proposals)), runner=ImmediateRunner()
    )
    view.resize(1200, 500)
    view.show()
    view.start_scan(root)
    qapp.processEvents()

    scroll_areas = view.findChildren(QScrollArea)
    assert [scroll.objectName() for scroll in scroll_areas] == ["importPageScroll"]
    assert view.findChild(QScrollArea, "importReviewScroll") is None
    assert view.page_scroll.verticalScrollBar().maximum() > 0
    assert all(
        row.parentWidget().objectName() == "importReviewContainer"
        for row in view.rows
    )


def test_mixed_review_rows_keep_minimum_height_without_visual_overlap(
    qapp: QApplication,
    tmp_path: Path,
    proposal_factory,
    candidate_factory,
) -> None:
    root = tmp_path / "movies"
    candidates = tuple(
        candidate_factory(external_id=str(index), title=f"Candidate {index}")
        for index in range(3)
    )
    proposals = (
        _review_proposal(proposal_factory, candidates),
        proposal_factory(status=ImportProposalStatus.NO_MATCH),
        proposal_factory(
            status=ImportProposalStatus.METADATA_UNAVAILABLE,
            reasons=(ImportProposalReason.METADATA_AUTHENTICATION,),
        ),
        _review_proposal(proposal_factory, candidates[:2]),
    )
    view = ImportView(
        FakeImportActions(_session(root, *proposals)), runner=ImmediateRunner()
    )
    view.resize(1400, 850)
    view.show()
    view.start_scan(root)
    qapp.processEvents()

    for row in view.rows:
        assert row.height() >= 54
        action_host = row.findChild(QWidget, "importActionHost")
        assert action_host.height() == 38
        assert action_host.geometry().bottom() <= row.contentsRect().bottom()
        if row.candidate_selector.isVisible():
            assert row.candidate_selector.height() >= 38
            assert (
                row.candidate_selector.geometry().bottom()
                <= row.contentsRect().bottom()
            )
    for current, following in zip(view.rows, view.rows[1:]):
        assert current.geometry().bottom() < following.geometry().top()


def test_no_match_and_tv_skipped_keep_the_same_primary_column_geometry(
    qapp: QApplication,
    tmp_path: Path,
    proposal_factory,
    discovery_factory,
) -> None:
    root = tmp_path / "movies"
    no_match = proposal_factory(
        status=ImportProposalStatus.NO_MATCH,
        discovery=discovery_factory(path=root / "no-match.mkv"),
    )
    tv_discovery = DiscoveredMedia(
        path=root / "Show.S01E02.mkv",
        file_size=10,
        parsed_media=ParsedMedia(
            "Show.S01E02.mkv",
            MediaType.TV_EPISODE,
            "Show",
            None,
            None,
            None,
            None,
            ".mkv",
        ),
        classification=DiscoveryClassification.TV_EPISODE_SKIPPED,
        issue=None,
    )
    tv_skipped = MovieImportProposal(
        ImportProposalStatus.NO_MATCH,
        tv_discovery,
        (),
        None,
        None,
        (ImportProposalReason.TV_EPISODE_NOT_SUPPORTED,),
        None,
    )
    view = ImportView(
        FakeImportActions(_session(root, no_match, tv_skipped)),
        runner=ImmediateRunner(),
    )
    view.resize(1400, 850)
    view.show()
    view.start_scan(root)
    qapp.processEvents()

    first, second = view.rows
    for row in (first, second):
        assert row.findChild(QLabel, "importYearLabel").width() == 72
        assert row.findChild(QLabel, "importResolutionLabel").width() == 88
        assert row.findChild(QLabel, "importStatusLabel").width() == 156
        assert row.findChild(QWidget, "importActionHost").width() == 272
    assert first.status_text == "No match"
    assert second.status_text == "TV episode skipped"
    assert second.import_button.isHidden()
    assert second.manual_search_button.isHidden()


@pytest.mark.parametrize("item_count", (1, 3, 5))
def test_no_match_rows_never_show_controls_as_top_level_windows(
    qapp: QApplication,
    tmp_path: Path,
    proposal_factory,
    discovery_factory,
    item_count: int,
) -> None:
    root = tmp_path / "movies"
    proposals = tuple(
        proposal_factory(
            status=ImportProposalStatus.NO_MATCH,
            discovery=discovery_factory(path=root / f"movie-{index}.mkv"),
        )
        for index in range(item_count)
    )
    view = ImportView(
        FakeImportActions(_session(root, *proposals)),
        runner=ImmediateRunner(),
    )
    main_window = QMainWindow()
    main_window.setCentralWidget(view)
    main_window.show()
    qapp.processEvents()

    probe = _TransientImportControlProbe(main_window)
    qapp.installEventFilter(probe)
    try:
        view.start_scan(root)
        qapp.processEvents()
    finally:
        qapp.removeEventFilter(probe)

    try:
        assert view.row_count == item_count
        assert probe.logical_top_level_shows == []
        assert probe.native_window_shows == []
        assert probe.main_activation_events == []
        for row in view.rows:
            controls = (
                row.import_button,
                row.manual_search_button,
            )
            assert all(control is not None for control in controls)
            assert all(control.parentWidget() is not None for control in controls)
            assert all(not control.isWindow() for control in controls)
            visibility = [
                (control.objectName(), control.isVisible(), control.isHidden())
                for control in controls
            ]
            assert all(control.isVisible() for control in controls), visibility
            assert row.findChild(QLabel, "importExplanationLabel") is None
    finally:
        main_window.close()
        qapp.processEvents()


def test_candidate_and_metadata_actions_never_show_as_top_level_windows(
    qapp: QApplication,
    tmp_path: Path,
    proposal_factory,
    candidate_factory,
) -> None:
    root = tmp_path / "movies"
    candidates = (
        candidate_factory(external_id="1", title="First candidate"),
        candidate_factory(external_id="2", title="Second candidate"),
    )
    proposals = (
        _review_proposal(proposal_factory, candidates),
        proposal_factory(
            status=ImportProposalStatus.METADATA_UNAVAILABLE,
            reasons=(ImportProposalReason.METADATA_AUTHENTICATION,),
        ),
    )
    view = ImportView(
        FakeImportActions(_session(root, *proposals)), runner=ImmediateRunner()
    )
    main_window = QMainWindow()
    main_window.setCentralWidget(view)
    main_window.show()
    qapp.processEvents()

    probe = _TransientImportControlProbe(main_window)
    qapp.installEventFilter(probe)
    try:
        view.start_scan(root)
        qapp.processEvents()
    finally:
        qapp.removeEventFilter(probe)

    try:
        assert probe.logical_top_level_shows == []
        assert probe.native_window_shows == []
        assert probe.main_activation_events == []
        for row in view.rows:
            for name in _TRANSIENT_CONTROL_NAMES:
                control = row.findChild(QWidget, name)
                assert control is not None
                assert control.parentWidget() is not None
                assert not control.isWindow()
    finally:
        main_window.close()
        qapp.processEvents()


def test_default_folder_picker_opens_once_and_starts_scan(
    qapp: QApplication,
    tmp_path: Path,
    proposal_factory,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    root = tmp_path / "movies"
    calls: list[tuple[QWidget, str]] = []

    def get_existing_directory(parent: QWidget, title: str) -> str:
        calls.append((parent, title))
        return str(root)

    monkeypatch.setattr(
        QFileDialog,
        "getExistingDirectory",
        get_existing_directory,
    )
    actions = FakeImportActions(_session(root, proposal_factory()))
    view = ImportView(actions, runner=ImmediateRunner())

    view.choose_folder()

    assert calls == [(view, "Choose a movie folder")]
    assert actions.prepare_calls == [(root, True)]
