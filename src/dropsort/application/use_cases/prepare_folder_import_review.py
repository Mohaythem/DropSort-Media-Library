from __future__ import annotations

from pathlib import Path
from collections.abc import Callable
from threading import Event
from typing import Protocol

from dropsort.application.dto.import_review import (
    ImportReviewProgress,
    ImportReviewSession,
    ImportReviewStage,
    ImportReviewSummary,
)
from dropsort.application.dto.movie_import import (
    ImportProposalReason,
    ImportProposalStatus,
    MovieImportProposal,
)
from dropsort.application.errors import ImportReviewCancelled
from dropsort.media.discovery import DiscoveryCancelled
from dropsort.media.discovery.contracts import DiscoveryCancellation
from dropsort.media.discovery.models import DiscoveredMedia, DiscoveryProgress


ProgressCallback = Callable[[ImportReviewProgress], None]


class ImportReviewCancellation:
    """Thread-safe cancellation context owned by exactly one UI scan session."""

    def __init__(self) -> None:
        self._event = Event()

    def cancel(self) -> None:
        self._event.set()

    def is_cancelled(self) -> bool:
        return self._event.is_set()


class DiscoveryUseCase(Protocol):
    def execute(
        self,
        root: Path,
        *,
        recursive: bool = True,
        progress: Callable[[DiscoveryProgress], None] | None = None,
        cancellation: DiscoveryCancellation | None = None,
    ) -> tuple[DiscoveredMedia, ...]: ...


class ProposalUseCase(Protocol):
    def execute(self, discovery: DiscoveredMedia) -> MovieImportProposal: ...

    def after_provider_failure(
        self,
        discovery: DiscoveredMedia,
        reason: ImportProposalReason,
    ) -> MovieImportProposal: ...


class PrepareFolderImportReview:
    """Compose discovery and matching proposals without importing anything."""

    def __init__(
        self,
        discovery: DiscoveryUseCase,
        proposals: ProposalUseCase,
        *,
        proposal_progress_interval: int = 8,
    ) -> None:
        if (
            isinstance(proposal_progress_interval, bool)
            or not isinstance(proposal_progress_interval, int)
            or proposal_progress_interval <= 0
        ):
            raise ValueError("proposal_progress_interval must be a positive integer")
        self._discovery = discovery
        self._proposals = proposals
        self._proposal_progress_interval = proposal_progress_interval

    def execute(
        self,
        root: Path,
        *,
        recursive: bool = True,
        progress: ProgressCallback | None = None,
        cancellation: DiscoveryCancellation | None = None,
    ) -> ImportReviewSession:
        if not isinstance(root, Path):
            raise ValueError("root must be a Path")
        if not isinstance(recursive, bool):
            raise ValueError("recursive must be a boolean")
        latest_discovery = DiscoveryProgress()

        def discovery_progress(value: DiscoveryProgress) -> None:
            nonlocal latest_discovery
            latest_discovery = value
            _emit(progress, _review_progress(ImportReviewStage.DISCOVERING, value))

        try:
            discoveries = self._discovery.execute(
                root,
                recursive=recursive,
                progress=discovery_progress,
                cancellation=cancellation,
            )
        except DiscoveryCancelled as error:
            raise ImportReviewCancelled(
                _review_progress(ImportReviewStage.DISCOVERING, error.progress)
            ) from error
        _raise_if_cancelled(cancellation, ImportReviewStage.DISCOVERING, latest_discovery)

        total = len(discoveries)
        completed = 0
        _emit(
            progress,
            _review_progress(
                ImportReviewStage.PREPARING_METADATA,
                latest_discovery,
                proposal_completed=0,
                proposal_total=total,
            ),
        )
        proposals: list[MovieImportProposal] = []
        provider_failure: ImportProposalReason | None = None
        for discovery in discoveries:
            _raise_if_cancelled(
                cancellation,
                ImportReviewStage.PREPARING_METADATA,
                latest_discovery,
                proposal_completed=completed,
                proposal_total=total,
            )
            proposal = (
                self._proposals.execute(discovery)
                if provider_failure is None
                else self._proposals.after_provider_failure(discovery, provider_failure)
            )
            _raise_if_cancelled(
                cancellation,
                ImportReviewStage.PREPARING_METADATA,
                latest_discovery,
                proposal_completed=completed,
                proposal_total=total,
            )
            proposals.append(proposal)
            failure_reason = next(
                (
                    reason
                    for reason in proposal.reasons
                    if reason
                    in {
                        ImportProposalReason.METADATA_AUTHENTICATION,
                        ImportProposalReason.METADATA_RATE_LIMIT,
                        ImportProposalReason.METADATA_UNAVAILABLE,
                    }
                ),
                None,
            )
            if failure_reason is not None:
                provider_failure = failure_reason
            completed += 1
            if completed % self._proposal_progress_interval == 0 or completed == total:
                _emit(
                    progress,
                    _review_progress(
                        ImportReviewStage.PREPARING_METADATA,
                        latest_discovery,
                        proposal_completed=completed,
                        proposal_total=total,
                    ),
                )
        _emit(
            progress,
            _review_progress(
                ImportReviewStage.BUILDING_REVIEW,
                latest_discovery,
                proposal_completed=completed,
                proposal_total=total,
            ),
        )
        return ImportReviewSession(
            root=root,
            recursive=recursive,
            items=tuple(proposals),
            summary=_summary(latest_discovery, proposals),
        )


def _review_progress(
    stage: ImportReviewStage,
    discovery: DiscoveryProgress,
    *,
    proposal_completed: int = 0,
    proposal_total: int = 0,
) -> ImportReviewProgress:
    return ImportReviewProgress(
        stage=stage,
        directories_seen=discovery.directories_seen,
        entries_seen=discovery.entries_seen,
        supported_media_found=discovery.supported_media_found,
        movie_candidates=discovery.movie_candidates,
        tv_episodes_skipped=discovery.tv_episodes_skipped,
        unknown_media=discovery.unknown_media,
        discovery_errors=discovery.errors,
        proposal_completed=proposal_completed,
        proposal_total=proposal_total,
    )


def _emit(callback: ProgressCallback | None, value: ImportReviewProgress) -> None:
    if callback is not None:
        callback(value)


def _raise_if_cancelled(
    cancellation: DiscoveryCancellation | None,
    stage: ImportReviewStage,
    discovery: DiscoveryProgress,
    *,
    proposal_completed: int = 0,
    proposal_total: int = 0,
) -> None:
    if cancellation is not None and cancellation.is_cancelled():
        raise ImportReviewCancelled(
            _review_progress(
                stage,
                discovery,
                proposal_completed=proposal_completed,
                proposal_total=proposal_total,
            )
        )


def _summary(
    discovery: DiscoveryProgress,
    proposals: list[MovieImportProposal],
) -> ImportReviewSummary:
    statuses = [proposal.status for proposal in proposals]
    return ImportReviewSummary(
        directories_seen=discovery.directories_seen,
        entries_seen=discovery.entries_seen,
        supported_media_found=discovery.supported_media_found,
        movie_candidates=discovery.movie_candidates,
        tv_episodes_skipped=discovery.tv_episodes_skipped,
        unknown_media=discovery.unknown_media,
        discovery_errors=discovery.errors,
        already_in_library=statuses.count(ImportProposalStatus.ALREADY_IN_LIBRARY),
        ready_for_review=sum(
            status in {
                ImportProposalStatus.MATCH_PROPOSED,
                ImportProposalStatus.REVIEW_REQUIRED,
            }
            for status in statuses
        ),
        no_match=statuses.count(ImportProposalStatus.NO_MATCH),
        metadata_unavailable=statuses.count(ImportProposalStatus.METADATA_UNAVAILABLE),
    )
