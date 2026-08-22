from __future__ import annotations

from datetime import datetime, timezone
from pathlib import Path

from PySide6.QtCore import Qt
from PySide6.QtTest import QTest
from PySide6.QtWidgets import QDialog, QFrame, QLabel, QProgressBar, QPushButton

from dropsort.application.dto.reconciliation import (
    LibraryReconciliationProgress,
    RelinkPreview,
    RelinkResult,
)
from dropsort.application.dto.library_health import (
    LibraryHealthProgress,
    MetadataHealthIssue,
    MetadataHealthItem,
    MetadataProviderError,
    MetadataHealthStatus,
)
from dropsort.application.errors import (
    LibraryReconciliationCancelled,
    RelinkPreviewStaleError,
    RelinkValidationCode,
    RelinkValidationError,
)
from dropsort.application.use_cases import ReconciliationCancellation
from dropsort.library.movies import MediaFile, MediaFileStatus
from dropsort.ui.reconciliation import LibraryFileCheckDialog, RelinkMediaFileDialog
from dropsort.ui.localization import UiLocalizer
from dropsort.ui.common.theme import ThemeId, apply_theme
from dropsort.application.configuration.localization import UiLanguage


NOW = datetime(2026, 8, 12, tzinfo=timezone.utc)


class ImmediateRunner:
    def submit(self, token, task, on_success, on_failure):
        try:
            value = task()
        except BaseException as error:
            on_failure(token, error)
        else:
            on_success(token, value)

    def submit_progressive(self, token, task, on_progress, on_success, on_failure):
        try:
            value = task(lambda progress: on_progress(token, progress))
        except BaseException as error:
            on_failure(token, error)
        else:
            on_success(token, value)


class DeferredRunner:
    def __init__(self) -> None:
        self.submission = None

    def submit(self, *args):
        self.submission = args

    def submit_progressive(self, *args):
        self.submission = args


class Actions:
    def __init__(self) -> None:
        self.prepared: list[tuple[int, Path]] = []
        self.confirmed: list[str] = []
        self.discarded: list[str] = []

    def reconcile_library_files(self, *, progress=None, cancellation=None):
        value = LibraryReconciliationProgress(3, 3, 2, 1, 0, 1)
        if progress:
            progress(value)
        return value

    def prepare_media_relink(self, media_file_id, candidate_path):
        self.prepared.append((media_file_id, candidate_path))
        return RelinkPreview(
            "token",
            media_file_id,
            7,
            r"D:\Old.mkv",
            str(candidate_path),
            5,
            ("SIZE_EXACT",),
        )

    def confirm_media_relink(self, preview_id):
        self.confirmed.append(preview_id)
        return RelinkResult(
            MediaFile(
                1,
                7,
                Path(r"D:\New.mkv"),
                5,
                ".mkv",
                None,
                None,
                None,
                MediaFileStatus.PRESENT,
                NOW,
                NOW,
            )
        )

    def discard_media_relink_preview(self, preview_id):
        self.discarded.append(preview_id)


class HealthActions(Actions):
    def check_library(self, *, progress=None, cancellation=None):
        value = LibraryHealthProgress(
            LibraryReconciliationProgress(3, 3, 2, 1, 0, 1),
            2,
            2,
            1,
            1,
            1,
            0,
            0,
            (
                MetadataHealthItem(
                    7,
                    "Repairable Movie",
                    MetadataHealthStatus.COMPLETE,
                    (MetadataHealthIssue.OVERVIEW,),
                    (MetadataHealthIssue.OVERVIEW,),
                ),
            ),
        )
        if progress:
            progress(value)
        return value


def test_library_check_shows_exact_progress_and_summary(qapp) -> None:
    dialog = LibraryFileCheckDialog(Actions(), ImmediateRunner())

    dialog.start_check()

    assert dialog.state is LibraryFileCheckDialog.State.COMPLETED_WITH_ISSUES
    assert "Library file check complete" in dialog.status_text
    summary = dialog.findChild(QLabel, "libraryCheckSummaryLabel").text()
    assert "3 files checked" in summary
    assert "1 missing files" in summary
    assert dialog.findChild(QPushButton, "cancelLibraryCheckButton").isEnabled() is False
    assert dialog.findChild(QPushButton, "closeLibraryCheckButton").isEnabled() is True
    assert dialog.findChild(QProgressBar, "libraryCheckProgressBar").isHidden() is True


def test_explicit_library_check_renders_file_and_metadata_summary_and_issues(qapp) -> None:
    dialog = LibraryFileCheckDialog(HealthActions(), ImmediateRunner())

    dialog.start_check()

    assert dialog.state is LibraryFileCheckDialog.State.COMPLETED_WITH_ISSUES
    assert dialog.status_text == "Library check complete"
    assert dialog.findChild(QFrame, "checkLibrarySummaryPanel").isHidden() is False
    summary = dialog.findChild(QLabel, "libraryCheckSummaryLabel").text()
    assert "3 files checked" in summary
    assert "1 metadata issues found" in summary
    assert "1 repaired" in summary
    assert dialog.findChild(QLabel, "libraryCheckIssuesLabel").text() == "Issues to review"
    assert any(
        "Repaired: Missing overview" in label.text()
        for label in dialog.findChildren(QLabel, "checkLibraryIssueOutcome")
    )


def test_health_dialog_handles_waiting_no_issues_provider_failure_and_health_cancellation(qapp) -> None:
    dialog = LibraryFileCheckDialog(HealthActions(), ImmediateRunner())
    dialog.wait_for_automatic_file_check()
    assert "already running" in dialog.status_text.casefold()
    assert dialog.findChild(QPushButton, "startLibraryCheckButton").isEnabled() is False

    file_progress = LibraryReconciliationProgress(1, 1, 1, 0, 0, 0)
    no_issues = LibraryHealthProgress(file_progress, 0, 0, 0, 0, 0, 0, 0)
    dialog._token = 4
    dialog._on_success(4, no_issues)
    assert dialog.status_text == "Library looks good"
    assert dialog.findChild(QLabel, "libraryCheckIssuesLabel").isHidden()
    assert "All cataloged files are present." in dialog.findChild(
        QLabel, "libraryCheckSummaryLabel"
    ).text()

    failure = LibraryHealthProgress(
        file_progress,
        1,
        1,
        0,
        1,
        0,
        1,
        1,
        (
            MetadataHealthItem(
                8,
                "Unavailable Movie",
                MetadataHealthStatus.PROVIDER_UNAVAILABLE,
                (MetadataHealthIssue.OVERVIEW,),
                provider_error=MetadataProviderError.AUTHENTICATION,
            ),
        ),
    )
    dialog._token = 5
    dialog._on_progress(5, failure)
    assert dialog.status_text == "Checking library files and movie metadata..."
    dialog._render_health_details(failure)
    assert dialog.findChild(QLabel, "libraryCheckIssuesLabel").text() == "Issues to review"
    assert dialog.findChild(QLabel, "checkLibraryIssueOutcome").text() == "Provider unavailable"
    assert "authentication failed" not in dialog.findChild(QLabel, "checkLibraryIssueOutcome").text()

    dialog._on_failure(5, LibraryReconciliationCancelled(failure))
    assert dialog.state is LibraryFileCheckDialog.State.CANCELLED


def test_health_issue_statuses_use_consistent_structured_outcomes(qapp) -> None:
    dialog = LibraryFileCheckDialog(HealthActions(), ImmediateRunner())

    for status in (
        MetadataHealthStatus.NEEDS_MATCH,
        MetadataHealthStatus.PROVIDER_VALUE_UNAVAILABLE,
        MetadataHealthStatus.INCOMPLETE,
    ):
        text = dialog._format_health_item(
            MetadataHealthItem(
                9,
                "Review Movie",
                status,
                (MetadataHealthIssue.POSTER,),
            )
        )
        assert "Needs attention" in text
    missing_poster = dialog._format_health_item(
        MetadataHealthItem(
            9,
            "Poster Movie",
            MetadataHealthStatus.MISSING_POSTER,
            (MetadataHealthIssue.POSTER,),
        )
    )
    assert "Not repaired" in missing_poster
    assert "Needs attention" not in missing_poster


def test_relink_preview_shows_exact_old_new_paths_and_requires_confirmation(qapp) -> None:
    actions = Actions()
    dialog = RelinkMediaFileDialog(actions, 1, Path(r"D:\Old.mkv"), ImmediateRunner())
    new_path = Path(r"D:\New.mkv")

    dialog.prepare_selected_path(new_path)

    paths = dialog.findChild(QLabel, "relinkPathsLabel")
    confirm = dialog.findChild(QPushButton, "confirmRelinkButton")
    assert paths is not None and r"D:\Old.mkv" in paths.text() and r"D:\New.mkv" in paths.text()
    assert confirm is not None and confirm.isEnabled()
    assert actions.confirmed == []

    dialog.confirm_relink()

    assert actions.confirmed == ["token"]


def test_successful_relink_closes_before_refresh_signal(qapp) -> None:
    actions = Actions()
    dialog = RelinkMediaFileDialog(actions, 1, Path(r"D:\Old.mkv"), ImmediateRunner())
    delivered: list[str] = []
    dialog.finished.connect(lambda _result: delivered.append("closed"))
    dialog.relinked.connect(lambda _result: delivered.append("relinked"))
    dialog.prepare_selected_path(Path(r"D:\New.mkv"))

    dialog.confirm_relink()

    assert delivered == ["closed", "relinked"]


def test_closing_relink_dialog_discards_preview_and_invalidates_delivery(qapp) -> None:
    actions = Actions()
    dialog = RelinkMediaFileDialog(actions, 1, Path(r"D:\Old.mkv"), ImmediateRunner())
    dialog.prepare_selected_path(Path(r"D:\New.mkv"))

    dialog.close()

    assert actions.discarded == ["token"]


def test_library_check_cancel_failure_and_stale_delivery_are_controlled(qapp) -> None:
    runner = DeferredRunner()
    dialog = LibraryFileCheckDialog(Actions(), runner)
    dialog.start_check()
    token, _task, on_progress, on_success, on_failure = runner.submission

    dialog.cancel_check()
    assert "Cancelling" in dialog.status_text
    cancelled = LibraryReconciliationProgress(3, 1, 1, 0, 0, 0)
    on_failure(token, LibraryReconciliationCancelled(cancelled))
    assert dialog.state is LibraryFileCheckDialog.State.CANCELLED
    assert "Check cancelled" in dialog.status_text

    dialog.start_check()
    new_token, _task, on_progress, on_success, on_failure = runner.submission
    on_progress(token, LibraryReconciliationProgress(3, 2, 2, 0, 0, 0))
    assert "Checking library files" in dialog.status_text
    on_progress(new_token, object())
    on_success(new_token, object())
    on_failure(new_token, RuntimeError("backend"))
    assert dialog.state is LibraryFileCheckDialog.State.FAILED
    assert "could not finish" in dialog.status_text


def test_progress_reaching_total_does_not_complete_before_terminal_result(qapp) -> None:
    runner = DeferredRunner()
    dialog = LibraryFileCheckDialog(Actions(), runner)
    dialog.start_check()
    token, _task, on_progress, on_success, _on_failure = runner.submission

    on_progress(token, LibraryReconciliationProgress(3, 3, 2, 1, 0, 1))

    assert dialog.state is LibraryFileCheckDialog.State.RUNNING
    assert dialog.findChild(QPushButton, "cancelLibraryCheckButton").isEnabled()

    on_success(token, LibraryReconciliationProgress(3, 3, 2, 1, 0, 1))

    assert dialog.state is LibraryFileCheckDialog.State.COMPLETED_WITH_ISSUES
    assert dialog.findChild(QPushButton, "closeLibraryCheckButton").isEnabled()


def test_library_check_active_progress_and_completed_controls_have_distinct_states(qapp) -> None:
    runner = DeferredRunner()
    dialog = LibraryFileCheckDialog(Actions(), runner)
    dialog.start_check()
    token, _task, on_progress, on_success, _on_failure = runner.submission

    on_progress(token, LibraryReconciliationProgress(4, 2, 1, 1, 0, 0))
    progress = dialog.findChild(QProgressBar, "libraryCheckProgressBar")
    percentage = dialog.findChild(QLabel, "libraryCheckPercentageLabel")
    assert progress is not None and progress.isHidden() is False
    assert percentage is not None and percentage.text() == "50%"

    on_success(token, LibraryReconciliationProgress(4, 4, 3, 1, 0, 0))
    assert progress.isHidden() is True
    assert percentage.isHidden() is True
    assert dialog.findChild(QPushButton, "startLibraryCheckButton").text() == "Check Again"
    assert dialog.findChild(QPushButton, "closeLibraryCheckButton").property("role") == "dialogCloseAction"


def test_library_check_idle_state_is_compact_and_explicit(qapp) -> None:
    dialog = LibraryFileCheckDialog(Actions(), DeferredRunner())

    assert dialog.state is LibraryFileCheckDialog.State.IDLE
    assert dialog.status_text == "Ready to check cataloged media paths."
    assert dialog.findChild(QLabel, "libraryCheckIdleDescription").isHidden() is False
    assert dialog.findChild(QLabel, "libraryCheckFailureLabel").isHidden() is True
    assert dialog.findChild(QProgressBar, "libraryCheckProgressBar").isHidden() is True
    assert dialog.findChild(QLabel, "libraryCheckPercentageLabel").isHidden() is True
    assert dialog.findChild(QPushButton, "startLibraryCheckButton").isHidden() is False
    assert dialog.findChild(QPushButton, "startLibraryCheckButton").isEnabled() is True
    assert dialog.findChild(QPushButton, "cancelLibraryCheckButton").isHidden() is True
    assert dialog.findChild(QPushButton, "closeLibraryCheckButton").isHidden() is False


def test_library_check_dialog_constructs_under_all_supported_themes(qapp) -> None:
    try:
        for theme in ThemeId:
            apply_theme(qapp, theme)
            dialog = LibraryFileCheckDialog(Actions(), DeferredRunner())
            assert dialog.findChild(QLabel, "libraryCheckIdleDescription").text()
            assert dialog.findChild(QPushButton, "startLibraryCheckButton").property(
                "role"
            ) == "primaryAction"
    finally:
        apply_theme(qapp, ThemeId.MAIN)


def test_escape_running_check_requests_cancellation_without_closing_dialog(qapp) -> None:
    runner = DeferredRunner()
    dialog = LibraryFileCheckDialog(Actions(), runner)
    dialog.show()
    qapp.processEvents()
    dialog.start_check()
    cancellation = dialog._cancellation

    QTest.keyClick(dialog, Qt.Key.Key_Escape)

    assert cancellation is not None and cancellation.is_cancelled()
    assert dialog.state is LibraryFileCheckDialog.State.RUNNING
    assert dialog.isVisible()
    assert dialog.findChild(QPushButton, "cancelLibraryCheckButton").isEnabled() is False
    dialog.close()


def test_escape_terminal_check_closes_only_the_dialog(qapp) -> None:
    dialog = LibraryFileCheckDialog(Actions(), ImmediateRunner())
    dialog.show()
    qapp.processEvents()
    dialog.start_check()

    QTest.keyClick(dialog, Qt.Key.Key_Escape)

    assert not dialog.isVisible()
    assert dialog.result() == QDialog.DialogCode.Rejected


def test_library_check_summary_and_percentage_are_direction_safe_in_arabic(qapp) -> None:
    localizer = UiLocalizer(UiLanguage.ARABIC)
    try:
        dialog = LibraryFileCheckDialog(
            HealthActions(), ImmediateRunner(), localizer=localizer
        )
        dialog.start_check()
        percentage = dialog.findChild(QLabel, "libraryCheckPercentageLabel")
        assert percentage is not None
        assert percentage.layoutDirection() == Qt.LayoutDirection.LeftToRight
        assert percentage.text() == "100%"
        assert "تم فحص" in dialog.findChild(QLabel, "libraryCheckSummaryLabel").text()
        assert dialog.findChild(QLabel, "checkLibraryIssueOutcome").layoutDirection() == Qt.LayoutDirection.RightToLeft
    finally:
        localizer.set_language(UiLanguage.ENGLISH)


def test_coalesced_dialog_receives_terminal_result_without_starting_second_job(qapp) -> None:
    dialog = LibraryFileCheckDialog(Actions(), DeferredRunner())
    cancellation = ReconciliationCancellation()

    dialog.attach_to_existing(9, cancellation)
    dialog._on_progress(9, LibraryReconciliationProgress(2, 1, 1, 0, 0, 1))
    dialog._on_success(9, LibraryReconciliationProgress(2, 2, 2, 0, 0, 1))

    assert dialog.state is LibraryFileCheckDialog.State.COMPLETED
    assert dialog.is_running is False
    assert dialog.findChild(QPushButton, "cancelLibraryCheckButton").isEnabled() is False


def test_closing_coalesced_dialog_does_not_cancel_shared_reconciliation(qapp) -> None:
    dialog = LibraryFileCheckDialog(Actions(), DeferredRunner())
    cancellation = ReconciliationCancellation()
    dialog.attach_to_existing(4, cancellation)

    dialog.close()

    assert cancellation.is_cancelled() is False
    assert dialog.state is LibraryFileCheckDialog.State.IDLE


def test_library_check_close_invalidates_active_result(qapp) -> None:
    runner = DeferredRunner()
    dialog = LibraryFileCheckDialog(Actions(), runner)
    dialog.start_check()
    token, _task, _on_progress, on_success, _on_failure = runner.submission

    dialog.close()
    on_success(token, LibraryReconciliationProgress(1, 1, 1, 0, 0, 0))

    assert "Cancelling" in dialog.status_text


def test_relink_picker_cancel_selection_no_preview_confirm_and_error_states(
    qapp,
    monkeypatch,
) -> None:
    actions = Actions()
    dialog = RelinkMediaFileDialog(actions, 1, Path(r"D:\Old.mkv"), ImmediateRunner())
    dialog.confirm_relink()
    assert actions.confirmed == []

    monkeypatch.setattr(
        "dropsort.ui.reconciliation.dialogs.QFileDialog.getOpenFileName",
        lambda *args: ("", ""),
    )
    dialog.choose_file()
    assert actions.prepared == []

    monkeypatch.setattr(
        "dropsort.ui.reconciliation.dialogs.QFileDialog.getOpenFileName",
        lambda *args: (r"D:\New.mkv", "Video files"),
    )
    dialog.choose_file()
    assert actions.prepared == [(1, Path(r"D:\New.mkv"))]

    token = dialog._token
    dialog._on_preview(token - 1, object())
    dialog._on_relinked(token - 1, object())
    dialog._on_error(token - 1, RuntimeError())
    dialog._on_error(
        token,
        RelinkValidationError("wrong", RelinkValidationCode.SIZE_MISMATCH),
    )
    assert "Size Mismatch" in dialog.status_text
    dialog._on_error(token, RelinkPreviewStaleError("stale"))
    assert "no longer valid" in dialog.status_text
    dialog._on_error(token, RuntimeError("unexpected"))
    assert "could not complete" in dialog.status_text


def test_relink_dialog_ignores_late_or_wrong_typed_success(qapp) -> None:
    actions = Actions()
    runner = DeferredRunner()
    dialog = RelinkMediaFileDialog(actions, 1, Path(r"D:\Old.mkv"), runner)
    dialog.prepare_selected_path(Path(r"D:\New.mkv"))
    token, _task, on_success, _on_failure = runner.submission

    dialog.close()
    on_success(token, RelinkPreview("late", 1, 7, r"D:\Old.mkv", r"D:\New.mkv", 5, ()))
    dialog._on_preview(dialog._token, object())
    dialog._on_relinked(dialog._token, object())

    assert actions.confirmed == []
