from __future__ import annotations

from PySide6.QtCore import Qt
from PySide6.QtTest import QTest
from PySide6.QtWidgets import QApplication

from dropsort.ui.scan.import_review_row import ImportReviewRow


def test_movie_review_row_exposes_edit_search_for_no_match(qapp: QApplication, proposal_factory) -> None:
    from dropsort.application.dto.movie_import import ImportProposalStatus
    row = ImportReviewRow(proposal_factory(status=ImportProposalStatus.NO_MATCH))
    assert row.manual_search_button.isHidden() is False
    assert row.manual_search_button.text() == "Search"
    assert row.manual_search_button.toolTip() == "Edit Search"


def test_manual_search_button_does_not_emit_import(qapp: QApplication, proposal_factory) -> None:
    from dropsort.application.dto.movie_import import ImportProposalStatus

    row = ImportReviewRow(proposal_factory(status=ImportProposalStatus.NO_MATCH))
    imported = []
    searched = []
    row.confirm_requested.connect(lambda *args: imported.append(args))
    row.manual_search_requested.connect(lambda proposal, active: searched.append((proposal, active)))
    QTest.mouseClick(row.manual_search_button, Qt.MouseButton.LeftButton)
    assert imported == []
    assert len(searched) == 1


def test_successful_automatic_proposals_hide_edit_search(qapp: QApplication, proposal_factory) -> None:
    from dropsort.application.dto.movie_import import ImportProposalStatus

    for status in (ImportProposalStatus.MATCH_PROPOSED, ImportProposalStatus.REVIEW_REQUIRED):
        row = ImportReviewRow(proposal_factory(status=status))
        assert row.manual_search_button.isHidden()


def test_single_automatic_candidate_is_preselected_but_not_imported(
    qapp: QApplication, proposal_factory
) -> None:
    row = ImportReviewRow(proposal_factory())

    assert row.selected_candidate == row.proposal.proposed_candidate
    assert row.can_import is True


def test_add_movies_row_shows_title_without_creating_path_widgets(qapp: QApplication, proposal_factory) -> None:
    from PySide6.QtWidgets import QLabel

    row = ImportReviewRow(proposal_factory())
    title_label = row.findChild(QLabel, "importTitleLabel")
    assert title_label is not None
    assert title_label.text() == row.proposal.discovery.parsed_media.title
    assert row.findChild(QLabel, "importFilenameLabel") is None
    assert row.findChild(QLabel, "importPathLabel") is None


def test_manual_selection_hides_edit_search_after_usable_candidate_is_selected(
    qapp: QApplication, proposal_factory, candidate_factory
) -> None:
    from dropsort.application.dto.movie_import import ImportProposalStatus
    from dropsort.ui.scan.import_view import ImportView

    original = proposal_factory(status=ImportProposalStatus.NO_MATCH)
    row = ImportReviewRow(original)
    candidate = candidate_factory(external_id="579", title="The Wind Rises", year=2013)
    ImportView._manual_candidate_selected(ImportView.__new__(ImportView), row, original, candidate)

    assert row.manual_search_button.isHidden()


def test_manual_candidate_selection_replaces_no_match_with_explicit_importable_proposal(
    qapp: QApplication, proposal_factory, candidate_factory
) -> None:
    from dropsort.application.dto.movie_import import ImportProposalReason, ImportProposalStatus
    from dropsort.ui.scan.import_view import ImportView

    original = proposal_factory(status=ImportProposalStatus.NO_MATCH)
    row = ImportReviewRow(original)
    view = ImportView.__new__(ImportView)
    candidate = candidate_factory(external_id="579", title="The Wind Rises", year=2013)
    ImportView._manual_candidate_selected(view, row, original, candidate)

    assert row.proposal.status is ImportProposalStatus.MANUAL_SELECTION
    assert row.selected_candidate == candidate
    assert row.proposal.reasons == (ImportProposalReason.MANUAL_SELECTION,)
    assert row.can_import is True
