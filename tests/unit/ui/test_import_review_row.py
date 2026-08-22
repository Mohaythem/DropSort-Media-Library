from __future__ import annotations

from PySide6.QtCore import Qt
from PySide6.QtTest import QTest
from PySide6.QtWidgets import QApplication

from dropsort.application.dto.movie_import import ImportProposalReason, ImportProposalStatus
from dropsort.media.matcher.models import MatchStatus
from dropsort.ui.scan.import_review_row import ImportReviewRow


def test_matched_row_explains_confidence_but_does_not_auto_import(
    qapp: QApplication,
    proposal_factory,
) -> None:
    row = ImportReviewRow(proposal_factory())
    confirmations: list[object] = []
    row.confirm_requested.connect(lambda *values: confirmations.append(values))

    assert row.status_text == "Match proposed"
    assert row.can_import is True
    assert "98%" in row.explanation_text
    assert confirmations == []


def test_review_row_allows_explicit_candidate_selection(
    qapp: QApplication,
    proposal_factory,
    candidate_factory,
) -> None:
    first = candidate_factory()
    second = candidate_factory(external_id="999", title="The Dark Knight Returns", year=2012)
    proposal = proposal_factory(candidate=first)
    score_type = type(proposal.match_decision.ranked_candidates[0])
    decision_type = type(proposal.match_decision)
    second_score = score_type(second, 0.90, proposal.match_decision.reasons, ())
    decision = decision_type(
        status=MatchStatus.REVIEW_REQUIRED,
        candidate=first,
        confidence=proposal.match_decision.confidence,
        reasons=proposal.match_decision.reasons,
        ranked_candidates=proposal.match_decision.ranked_candidates + (second_score,),
    )
    proposal = proposal_factory(
        status=ImportProposalStatus.REVIEW_REQUIRED,
        candidate=first,
        candidates=(first, second),
        match_decision=decision,
    )
    row = ImportReviewRow(proposal)

    row.candidate_selector.setCurrentIndex(1)

    assert row.selected_candidate == second


def test_non_confirmable_statuses_never_offer_import(
    qapp: QApplication,
    proposal_factory,
) -> None:
    for status in (
        ImportProposalStatus.NO_MATCH,
        ImportProposalStatus.METADATA_UNAVAILABLE,
        ImportProposalStatus.ALREADY_IN_LIBRARY,
    ):
        row = ImportReviewRow(proposal_factory(status=status))
        assert row.can_import is False
        assert row.import_button.isHidden()


def test_import_requires_button_click_and_disables_duplicate_clicks(
    qapp: QApplication,
    proposal_factory,
) -> None:
    row = ImportReviewRow(proposal_factory())
    confirmations: list[object] = []
    row.confirm_requested.connect(lambda *values: confirmations.append(values))
    row.show()

    QTest.mouseClick(row.import_button, Qt.MouseButton.LeftButton)
    QTest.mouseClick(row.import_button, Qt.MouseButton.LeftButton)

    assert len(confirmations) == 1
    assert row.import_button.isEnabled() is False


def test_dismiss_button_is_a_session_only_action(qapp: QApplication, proposal_factory) -> None:
    row = ImportReviewRow(proposal_factory())
    dismissed: list[object] = []
    row.dismiss_requested.connect(dismissed.append)

    row.dismiss_button.click()

    assert dismissed == [row]


def test_missing_candidate_cannot_emit_confirmation(
    qapp: QApplication,
    proposal_factory,
) -> None:
    row = ImportReviewRow(proposal_factory())
    confirmations: list[object] = []
    row.confirm_requested.connect(lambda *values: confirmations.append(values))
    row.candidate_selector.clear()

    row.import_button.click()

    assert confirmations == []


def test_missing_tmdb_credential_offers_settings_without_import(
    qapp: QApplication,
    proposal_factory,
) -> None:
    proposal = proposal_factory(
        status=ImportProposalStatus.METADATA_UNAVAILABLE,
        reasons=(ImportProposalReason.METADATA_AUTHENTICATION,),
    )
    row = ImportReviewRow(proposal)
    requests: list[bool] = []
    row.settings_requested.connect(lambda: requests.append(True))

    assert "TMDB is not configured" in row.explanation_text
    assert "Settings" in row.explanation_text
    assert row.settings_button.isHidden() is False
    assert row.import_button.isHidden() is True

    row.settings_button.click()

    assert requests == [True]


def test_non_authentication_metadata_error_does_not_offer_settings(
    qapp: QApplication,
    proposal_factory,
) -> None:
    row = ImportReviewRow(
        proposal_factory(
            status=ImportProposalStatus.METADATA_UNAVAILABLE,
            reasons=(ImportProposalReason.METADATA_RESPONSE_ERROR,),
        )
    )

    assert row.settings_button.isHidden() is True
