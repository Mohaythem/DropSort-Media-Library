from __future__ import annotations

from dataclasses import dataclass, field
from pathlib import Path

from PySide6.QtWidgets import QApplication

from dropsort.application.dto.import_review import (
    ImportReviewProgress,
    ImportReviewSession,
    ImportReviewStage,
)
from dropsort.application.errors import ImportReviewCancelled
from dropsort.application.use_cases.prepare_folder_import_review import ImportReviewCancellation
from dropsort.ui.scan.import_view import ImportView


@dataclass
class ProgressiveTask:
    token: int
    task: object
    on_progress: object
    on_success: object
    on_failure: object


class ProgressiveRunner:
    def __init__(self) -> None:
        self.tasks: list[ProgressiveTask] = []

    def submit_progressive(self, token, task, on_progress, on_success, on_failure) -> None:
        self.tasks.append(ProgressiveTask(token, task, on_progress, on_success, on_failure))

    def submit(self, token, task, on_success, on_failure) -> None:
        raise AssertionError("scan must use progressive delivery")


@dataclass
class Actions:
    session: ImportReviewSession
    cancellations: list[ImportReviewCancellation] = field(default_factory=list)

    def prepare_import_review(
        self,
        root,
        recursive,
        *,
        progress=None,
        cancellation=None,
    ):
        assert cancellation is not None
        self.cancellations.append(cancellation)
        return self.session

    def confirm_movie_import(self, command):
        return object()


def test_scan_progress_controls_and_cancellation_are_visible(
    qapp: QApplication,
    proposal_factory,
) -> None:
    root = Path.cwd() / "movies"
    runner = ProgressiveRunner()
    view = ImportView(
        Actions(ImportReviewSession(root, True, (proposal_factory(),))),
        runner=runner,
    )

    view.start_scan(root)

    assert view.is_busy is True
    assert view.cancel_button.isHidden() is False
    assert view.recursive_checkbox.isEnabled() is False
    task = runner.tasks[0]
    task.on_progress(
        task.token,
        ImportReviewProgress(
            ImportReviewStage.DISCOVERING,
            directories_seen=3,
            entries_seen=40,
            supported_media_found=5,
            movie_candidates=4,
            tv_episodes_skipped=1,
        ),
    )
    assert "Folders: 3" in view.progress_message
    assert "Files inspected: 40" in view.progress_message
    assert view.progress_bar.maximum() == 0

    view.cancel_scan()

    assert "Cancelling" in view.state_message
    assert view.cancel_button.isEnabled() is False


def test_cancelled_session_discards_partial_rows_and_restores_inputs(
    qapp: QApplication,
    proposal_factory,
) -> None:
    root = Path.cwd() / "movies"
    runner = ProgressiveRunner()
    view = ImportView(
        Actions(ImportReviewSession(root, True, (proposal_factory(),))),
        runner=runner,
    )
    view.start_scan(root)
    task = runner.tasks[0]
    view.cancel_scan()
    task.on_failure(
        task.token,
        ImportReviewCancelled(
            ImportReviewProgress(ImportReviewStage.DISCOVERING, entries_seen=12)
        ),
    )

    assert view.row_count == 0
    assert view.is_busy is False
    assert view.recursive_checkbox.isEnabled() is True
    assert "Scan cancelled" in view.state_message
    assert "12" in view.state_message
    assert "No files were changed" in view.state_message


def test_old_progress_and_result_cannot_mutate_new_session(
    qapp: QApplication,
    proposal_factory,
) -> None:
    first = Path.cwd() / "first"
    second = Path.cwd() / "second"
    runner = ProgressiveRunner()
    actions = Actions(ImportReviewSession(second, True, (proposal_factory(),)))
    view = ImportView(actions, runner=runner)
    view.start_scan(first)
    old = runner.tasks[0]
    view.cancel_scan()
    old.on_failure(
        old.token,
        ImportReviewCancelled(ImportReviewProgress(ImportReviewStage.DISCOVERING)),
    )
    view.start_scan(second)
    current = runner.tasks[1]

    old.on_progress(
        old.token,
        ImportReviewProgress(ImportReviewStage.DISCOVERING, entries_seen=999),
    )
    old.on_success(old.token, ImportReviewSession(first, True, (proposal_factory(),)))

    assert "999" not in view.progress_message
    assert view.row_count == 0
    current.on_success(current.token, actions.session)
    assert view.row_count == 1


def test_metadata_progress_is_determinate_and_completion_summary_is_clear(
    qapp: QApplication,
    proposal_factory,
) -> None:
    root = Path.cwd() / "movies"
    runner = ProgressiveRunner()
    session = ImportReviewSession(root, True, (proposal_factory(),))
    view = ImportView(Actions(session), runner=runner)
    view.start_scan(root)
    task = runner.tasks[0]
    task.on_progress(
        task.token,
        ImportReviewProgress(
            ImportReviewStage.PREPARING_METADATA,
            proposal_completed=2,
            proposal_total=5,
        ),
    )

    assert view.progress_bar.maximum() == 5
    assert view.progress_bar.value() == 2
    assert "2 / 5" in view.progress_message

    task.on_success(task.token, session)

    assert view.row_count == 1
    assert "Scan complete" in view.state_message


def test_double_scan_is_ignored_until_active_session_finishes(
    qapp: QApplication,
    proposal_factory,
) -> None:
    root = Path.cwd() / "movies"
    runner = ProgressiveRunner()
    view = ImportView(
        Actions(ImportReviewSession(root, True, (proposal_factory(),))),
        runner=runner,
    )

    view.start_scan(root)
    view.start_scan(root / "other")

    assert len(runner.tasks) == 1


def test_late_progress_after_cancel_cannot_replace_cancelling_state(
    qapp: QApplication,
    proposal_factory,
) -> None:
    root = Path.cwd() / "movies"
    runner = ProgressiveRunner()
    view = ImportView(
        Actions(ImportReviewSession(root, True, (proposal_factory(),))),
        runner=runner,
    )
    view.start_scan(root)
    task = runner.tasks[0]

    view.cancel_scan()
    task.on_progress(
        task.token,
        ImportReviewProgress(
            ImportReviewStage.PREPARING_METADATA,
            proposal_completed=5,
            proposal_total=10,
        ),
    )

    assert view.state_message == "Cancelling scan..."


def test_view_close_requests_cooperative_cancellation_and_ignores_late_progress(
    qapp: QApplication,
    proposal_factory,
) -> None:
    root = Path.cwd() / "movies"
    runner = ProgressiveRunner()
    actions = Actions(ImportReviewSession(root, True, (proposal_factory(),)))
    view = ImportView(actions, runner=runner)
    view.start_scan(root)
    task = runner.tasks[0]
    cancellation = view._cancellation

    view.show()
    qapp.processEvents()
    view.close()
    task.on_progress(
        task.token,
        ImportReviewProgress(ImportReviewStage.DISCOVERING, entries_seen=100),
    )

    assert cancellation is not None and cancellation.is_cancelled()
    assert "100" not in view.progress_message


def test_batched_review_rendering_deduplicates_paths(
    qapp: QApplication,
    proposal_factory,
    discovery_factory,
) -> None:
    root = Path.cwd() / "movies"
    proposals = tuple(
        proposal_factory(discovery=discovery_factory(path=root / f"Movie.{index}.mkv"))
        for index in range(30)
    )
    duplicate = proposal_factory(discovery=discovery_factory(path=root / "Movie.0.mkv"))
    session = ImportReviewSession(root, True, proposals + (duplicate,))
    runner = ProgressiveRunner()
    view = ImportView(Actions(session), runner=runner)
    view.start_scan(root)
    task = runner.tasks[0]

    task.on_success(task.token, session)
    qapp.processEvents()

    assert view.row_count == 30
    assert len({str(row.proposal.discovery.path).casefold() for row in view.rows}) == 30
