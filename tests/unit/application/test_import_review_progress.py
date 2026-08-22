from __future__ import annotations

from pathlib import Path

import pytest

from dropsort.application.dto.import_review import (
    ImportReviewProgress,
    ImportReviewStage,
)
from dropsort.application.dto.movie_import import (
    ImportProposalReason,
    ImportProposalStatus,
    MovieImportProposal,
)
from dropsort.application.errors import ImportReviewCancelled
from dropsort.application.use_cases.prepare_folder_import_review import (
    ImportReviewCancellation,
    PrepareFolderImportReview,
)
from dropsort.media.discovery import DiscoveryProgress

from tests.unit.application.test_prepare_import_review import (
    FakeDiscovery,
    FakeProposals,
    _discovery,
)


class ProgressDiscovery(FakeDiscovery):
    def execute(
        self,
        root: Path,
        *,
        recursive: bool = True,
        progress=None,
        cancellation=None,
    ):
        self.calls.append((root, recursive))
        if progress is not None:
            progress(DiscoveryProgress())
            progress(
                DiscoveryProgress(
                    directories_seen=1,
                    entries_seen=len(self.items),
                    supported_media_found=len(self.items),
                    movie_candidates=len(self.items),
                )
            )
        return self.items


def test_review_progress_has_real_stage_totals_and_summary() -> None:
    discoveries = tuple(_discovery(f"Movie.{index}.mkv") for index in range(3))
    progress: list[ImportReviewProgress] = []

    session = PrepareFolderImportReview(
        ProgressDiscovery(discoveries),
        FakeProposals(),
        proposal_progress_interval=2,
    ).execute(Path.cwd() / "movies", progress=progress.append)

    assert [item.stage for item in progress] == [
        ImportReviewStage.DISCOVERING,
        ImportReviewStage.DISCOVERING,
        ImportReviewStage.PREPARING_METADATA,
        ImportReviewStage.PREPARING_METADATA,
        ImportReviewStage.PREPARING_METADATA,
        ImportReviewStage.BUILDING_REVIEW,
    ]
    assert progress[-2].proposal_completed == 3
    assert progress[-2].proposal_total == 3
    assert session.summary.entries_seen == 3
    assert session.summary.movie_candidates == 3
    assert session.summary.ready_for_review == 0
    assert session.summary.no_match == 3


def test_cancellation_after_in_flight_proposal_discards_its_result() -> None:
    cancellation = ImportReviewCancellation()
    discoveries = (_discovery("A.mkv"), _discovery("B.mkv"))

    class CancellingProposals(FakeProposals):
        def execute(self, discovery):
            result = super().execute(discovery)
            cancellation.cancel()
            return result

    proposer = CancellingProposals()

    with pytest.raises(ImportReviewCancelled) as caught:
        PrepareFolderImportReview(
            ProgressDiscovery(discoveries),
            proposer,
        ).execute(
            Path.cwd() / "movies",
            cancellation=cancellation,
        )

    assert proposer.calls == [discoveries[0]]
    assert caught.value.progress.proposal_completed == 0


def test_each_import_review_cancellation_context_is_independent() -> None:
    first = ImportReviewCancellation()
    second = ImportReviewCancellation()

    first.cancel()

    assert first.is_cancelled() is True
    assert second.is_cancelled() is False


def test_cancel_after_completion_is_harmless_for_completed_session() -> None:
    cancellation = ImportReviewCancellation()
    session = PrepareFolderImportReview(
        ProgressDiscovery((_discovery("A.mkv"),)),
        FakeProposals(),
    ).execute(Path.cwd() / "movies", cancellation=cancellation)

    cancellation.cancel()

    assert len(session.items) == 1


def test_import_progress_validation_rejects_fake_percentages_and_bad_totals() -> None:
    with pytest.raises(ValueError, match="stage"):
        ImportReviewProgress("DISCOVERING")  # type: ignore[arg-type]
    with pytest.raises(ValueError, match="proposal"):
        ImportReviewProgress(
            ImportReviewStage.PREPARING_METADATA,
            proposal_completed=2,
            proposal_total=1,
        )


def test_session_wide_provider_failure_stops_new_provider_work() -> None:
    discoveries = tuple(_discovery(f"Movie.{index}.mkv") for index in range(20))

    class ProviderFailureProposals(FakeProposals):
        def __init__(self) -> None:
            super().__init__()
            self.short_circuited: list[object] = []

        def execute(self, discovery):
            self.calls.append(discovery)
            return MovieImportProposal(
                ImportProposalStatus.METADATA_UNAVAILABLE,
                discovery,
                (),
                None,
                None,
                (ImportProposalReason.METADATA_RATE_LIMIT,),
                None,
            )

        def after_provider_failure(self, discovery, reason):
            self.short_circuited.append(discovery)
            return MovieImportProposal(
                ImportProposalStatus.METADATA_UNAVAILABLE,
                discovery,
                (),
                None,
                None,
                (reason,),
                None,
            )

    proposer = ProviderFailureProposals()

    session = PrepareFolderImportReview(
        ProgressDiscovery(discoveries),
        proposer,
    ).execute(Path.cwd() / "movies")

    assert proposer.calls == [discoveries[0]]
    assert proposer.short_circuited == list(discoveries[1:])
    assert all(
        item.reasons == (ImportProposalReason.METADATA_RATE_LIMIT,)
        for item in session.items
    )
