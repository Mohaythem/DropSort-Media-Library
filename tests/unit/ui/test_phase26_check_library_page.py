from __future__ import annotations

from PySide6.QtCore import Qt
from PySide6.QtTest import QTest
from PySide6.QtWidgets import QFrame, QLabel, QProgressBar, QPushButton

from dropsort.application.configuration.localization import UiLanguage
from dropsort.application.dto.library_health import (
    LibraryHealthProgress,
    MetadataHealthIssue,
    MetadataHealthItem,
    MetadataProviderError,
    MetadataHealthStatus,
)
from dropsort.application.dto.reconciliation import LibraryReconciliationProgress
from dropsort.application.errors import LibraryReconciliationCancelled
from dropsort.application.use_cases import ReconciliationCancellation
from dropsort.ui.common.theme import ThemeId, apply_theme
from dropsort.ui.localization import UiLocalizer
from dropsort.ui.reconciliation import LibraryCheckPage
from dropsort.ui.reconciliation.page import _percentage


class DeferredRunner:
    def __init__(self) -> None:
        self.submissions: list[tuple] = []

    def submit(self, *args) -> None:
        self.submissions.append(args)

    def submit_progressive(self, *args) -> None:
        self.submissions.append(args)


class Actions:
    def __init__(self) -> None:
        self.check_calls = 0

    def check_library(self, *, progress=None, cancellation=None):
        self.check_calls += 1
        return _result()


def _result() -> LibraryHealthProgress:
    return LibraryHealthProgress(
        LibraryReconciliationProgress(2, 2, 1, 1, 0, 0),
        2,
        2,
        1,
        1,
        0,
        0,
        0,
        (
            MetadataHealthItem(
                2,
                "Needs Metadata",
                MetadataHealthStatus.INCOMPLETE,
                (MetadataHealthIssue.POSTER,),
            ),
            MetadataHealthItem(
                3,
                "Healthy Movie",
                MetadataHealthStatus.COMPLETE,
            ),
        ),
    )


def test_page_has_compact_idle_state_and_protects_against_duplicate_start(qapp) -> None:
    runner = DeferredRunner()
    page = LibraryCheckPage(Actions(), runner)

    assert page.state is LibraryCheckPage.State.IDLE
    assert page.findChild(QProgressBar, "libraryCheckPageProgressBar") is None or page.findChild(
        QProgressBar, "libraryCheckPageProgressBar"
    ).isHidden()
    assert page.findChild(QPushButton, "cancelLibraryCheckPageButton").isHidden()

    page.start_check()
    page.start_check()

    assert page.state is LibraryCheckPage.State.RUNNING
    assert len(runner.submissions) == 1
    assert not page.findChild(QPushButton, "cancelLibraryCheckPageButton").isHidden()


def test_page_renders_passed_and_attention_only_after_completion(qapp) -> None:
    runner = DeferredRunner()
    page = LibraryCheckPage(Actions(), runner)
    page.start_check()
    token, _task, on_progress, on_success, _on_failure = runner.submissions[0]

    result = _result()
    on_progress(token, result)
    on_success(token, result)

    assert page.state is LibraryCheckPage.State.COMPLETED_WITH_ISSUES
    assert page.findChild(QLabel, "libraryCheckPassedLabel").text() == "2 Passed"
    assert page.findChild(QLabel, "libraryCheckNeedsAttentionLabel").text() == "2 Needs attention"
    issue_rows = page.findChildren(QFrame, "checkLibraryIssueRow")
    assert len(issue_rows) == 2  # one aggregate file issue and one metadata issue
    assert all("Healthy Movie" not in row.accessibleName() for row in issue_rows)
    assert page.findChild(QPushButton, "startLibraryCheckPageButton").text() == "Check Again"


def test_page_cancel_and_failure_are_distinct_terminal_states(qapp) -> None:
    runner = DeferredRunner()
    page = LibraryCheckPage(Actions(), runner)
    page.start_check()
    token, _task, _on_progress, _on_success, on_failure = runner.submissions[0]
    cancellation = page._cancellation
    assert cancellation is not None

    page.cancel_check()
    page.cancel_check()
    assert cancellation.is_cancelled()
    on_failure(token, LibraryReconciliationCancelled(_result()))
    assert page.state is LibraryCheckPage.State.CANCELLED
    assert "not modified" in page.findChild(QLabel, "libraryCheckPageFailureLabel").text()

    page.start_check()
    token, _task, _on_progress, _on_success, on_failure = runner.submissions[1]
    on_failure(token, RuntimeError("internal detail"))
    assert page.state is LibraryCheckPage.State.FAILED
    assert "internal detail" not in page.findChild(QLabel, "libraryCheckPageFailureLabel").text()
    assert page.findChild(QPushButton, "startLibraryCheckPageButton").text() == "Try Again"


def test_page_supports_themes_and_arabic_western_progress_digits(qapp) -> None:
    localizer = UiLocalizer(UiLanguage.ARABIC)
    try:
        for theme in ThemeId:
            apply_theme(qapp, theme)
            page = LibraryCheckPage(Actions(), DeferredRunner(), localizer=localizer)
            page.start_check()
            # The page remains constructible in every theme and Arabic keeps
            # technical progress values LTR with Western digits.
            progress = page.findChild(QLabel, "libraryCheckPagePercentageLabel")
            assert progress.layoutDirection() == Qt.LayoutDirection.LeftToRight
            assert page.findChild(QPushButton, "startLibraryCheckPageButton").text()
    finally:
        apply_theme(qapp, ThemeId.MAIN)


def test_page_coordinator_queue_attach_invalidation_and_escape(qapp) -> None:
    runner = DeferredRunner()
    page = LibraryCheckPage(Actions(), runner)
    requested = []
    page.start_requested.connect(lambda: requested.append(True))
    page.request_start()
    assert requested == [True]

    cancellation = ReconciliationCancellation()
    page.attach_to_existing(7, cancellation)
    page.attach_to_existing(8, ReconciliationCancellation())
    assert page.is_running
    page.invalidate_pending()
    assert page.state is LibraryCheckPage.State.IDLE
    assert not cancellation.is_cancelled()

    page.wait_for_automatic_file_check()
    assert page.findChild(QPushButton, "startLibraryCheckPageButton").isEnabled() is False
    page.start_check()
    page.request_start()  # running pages do not emit another request
    assert len(runner.submissions) == 1
    page.cancel_check()
    page.invalidate_pending()
    assert page.state is LibraryCheckPage.State.RUNNING
    page._state = LibraryCheckPage.State.IDLE
    page._set_idle_state()
    back = []
    page.back_requested.connect(lambda: back.append(True))
    page.show()
    QTest.keyClick(page, Qt.Key.Key_Escape)
    assert back == [True]
    page.cancel_check()
    page.close()


def test_page_handles_file_only_fallback_invalid_callbacks_and_no_actions(qapp) -> None:
    class FileOnlyActions:
        def reconcile_library_files(self, *, progress=None, cancellation=None):
            return LibraryReconciliationProgress(1, 1, 1, 0, 0, 0)

    runner = DeferredRunner()
    page = LibraryCheckPage(FileOnlyActions(), runner)
    page.start_check()
    token, _task, _on_progress, on_success, on_failure = runner.submissions[0]
    page._on_progress(token + 1, object())
    page._on_success(token + 1, object())
    page._on_failure(token + 1, RuntimeError("ignored"))
    on_success(token, LibraryReconciliationProgress(1, 1, 1, 0, 0, 0))
    assert page.state is LibraryCheckPage.State.COMPLETED_SUCCESS
    assert page.findChild(QLabel, "libraryCheckPassedLabel").text() == "1 Passed"

    no_actions = LibraryCheckPage(None, DeferredRunner())
    no_actions.start_check()
    assert no_actions.state is LibraryCheckPage.State.FAILED
    assert no_actions.findChild(QPushButton, "startLibraryCheckPageButton").isEnabled() is False


def test_page_renders_provider_and_poster_issue_outcomes_and_refreshes_language(qapp) -> None:
    localizer = UiLocalizer(UiLanguage.ENGLISH)
    page = LibraryCheckPage(Actions(), DeferredRunner(), localizer=localizer)
    result = LibraryHealthProgress(
        LibraryReconciliationProgress(1, 1, 1, 0, 0, 0),
        3,
        3,
        1,
        2,
        0,
        1,
        1,
        (
            MetadataHealthItem(
                4,
                "Provider Movie",
                MetadataHealthStatus.PROVIDER_UNAVAILABLE,
                (MetadataHealthIssue.OVERVIEW,),
                provider_error=MetadataProviderError.AUTHENTICATION,
            ),
            MetadataHealthItem(
                5,
                "Poster Movie",
                MetadataHealthStatus.MISSING_POSTER,
                (MetadataHealthIssue.POSTER,),
            ),
        ),
    )
    page._token = 3
    page._on_progress(3, result)
    page._on_success(3, result)
    assert "Provider unavailable" in page.findChildren(QLabel, "checkLibraryIssueOutcome")[0].text()
    assert "Not repaired" in page.findChildren(QLabel, "checkLibraryIssueOutcome")[1].text()
    localizer.set_language(UiLanguage.ARABIC)
    assert "اجتاز الفحص" in page.findChild(QLabel, "libraryCheckPassedLabel").text()
    localizer.set_language(UiLanguage.ENGLISH)


def test_page_covers_zero_progress_and_small_terminal_branches(qapp) -> None:
    assert _percentage(0, 0) == 0
    assert _percentage(1, 0) == 100
    page = LibraryCheckPage(Actions(), DeferredRunner())
    assert page.status_text == "Ready to check cataloged media paths."
    page.cancel_check()
    page._render_progress(LibraryReconciliationProgress(0, 0, 0, 0, 0, 0))
    page._render_result(LibraryReconciliationProgress(0, 0, 0, 0, 0, 0))
    assert page.findChild(QLabel, "libraryCheckPassedLabel").text() == "0 Passed"
    page._outcome_text(
        MetadataHealthItem(10, "Complete", MetadataHealthStatus.COMPLETE)
    )
    page._file_issue_row(LibraryReconciliationProgress(1, 1, 0, 0, 1, 0))
