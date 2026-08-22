from __future__ import annotations

from pathlib import Path

import pytest

from dropsort.application.dto.import_review import ImportReviewSession, ImportReviewSummary
from dropsort.application.dto.movie_import import (
    ImportProposalReason,
    ImportProposalStatus,
    MovieImportProposal,
)
from dropsort.application.use_cases.prepare_folder_import_review import (
    PrepareFolderImportReview,
)
from dropsort.media.discovery.models import DiscoveryClassification, DiscoveredMedia
from dropsort.media.parser import MediaType, ParsedMedia


def _discovery(name: str) -> DiscoveredMedia:
    return DiscoveredMedia(
        path=Path.cwd() / name,
        file_size=100,
        parsed_media=ParsedMedia(name, MediaType.MOVIE, "Movie", 2024, None, None, None, ".mkv"),
        classification=DiscoveryClassification.MOVIE_CANDIDATE,
        issue=None,
    )


def _proposal(discovery: DiscoveredMedia) -> MovieImportProposal:
    return MovieImportProposal(
        status=ImportProposalStatus.NO_MATCH,
        discovery=discovery,
        candidates=(),
        match_decision=None,
        proposed_candidate=None,
        reasons=(ImportProposalReason.NO_MATCH,),
        existing_media_file_id=None,
    )


class FakeDiscovery:
    def __init__(self, items: tuple[DiscoveredMedia, ...]) -> None:
        self.items = items
        self.calls: list[tuple[Path, bool]] = []

    def execute(
        self,
        root: Path,
        *,
        recursive: bool = True,
        progress=None,
        cancellation=None,
    ) -> tuple[DiscoveredMedia, ...]:
        self.calls.append((root, recursive))
        if progress is not None:
            from dropsort.media.discovery import DiscoveryProgress

            progress(
                DiscoveryProgress(
                    entries_seen=len(self.items),
                    supported_media_found=len(self.items),
                    movie_candidates=len(self.items),
                )
            )
        return self.items


class FakeProposals:
    def __init__(self) -> None:
        self.calls: list[DiscoveredMedia] = []

    def execute(self, discovery: DiscoveredMedia) -> MovieImportProposal:
        self.calls.append(discovery)
        return _proposal(discovery)

    def after_provider_failure(self, discovery, reason):
        self.calls.append(discovery)
        return _proposal(discovery)


def test_prepare_review_composes_discovery_and_proposals_without_importing() -> None:
    discoveries = (_discovery("A.mkv"), _discovery("B.mkv"))
    scanner = FakeDiscovery(discoveries)
    proposer = FakeProposals()
    use_case = PrepareFolderImportReview(scanner, proposer)
    root = Path.cwd() / "selected"

    session = use_case.execute(root, recursive=False)

    assert session == ImportReviewSession(
        root=root,
        recursive=False,
        items=tuple(_proposal(item) for item in discoveries),
        summary=ImportReviewSummary(
            entries_seen=2,
            supported_media_found=2,
            movie_candidates=2,
            no_match=2,
        ),
    )
    assert scanner.calls == [(root, False)]
    assert proposer.calls == list(discoveries)


@pytest.mark.parametrize("root", ["folder", None, 1])
def test_prepare_review_rejects_non_path_roots(root: object) -> None:
    with pytest.raises(ValueError, match="root"):
        PrepareFolderImportReview(FakeDiscovery(()), FakeProposals()).execute(root)  # type: ignore[arg-type]


def test_prepare_review_rejects_non_boolean_recursive_flag() -> None:
    with pytest.raises(ValueError, match="recursive"):
        PrepareFolderImportReview(FakeDiscovery(()), FakeProposals()).execute(
            Path.cwd(), recursive=1  # type: ignore[arg-type]
        )
