from __future__ import annotations

from datetime import UTC, datetime
import base64
import os
from pathlib import Path

import pytest


os.environ.setdefault("QT_QPA_PLATFORM", "offscreen")

from PySide6.QtCore import QCoreApplication, QEvent  # noqa: E402
from PySide6.QtWidgets import QApplication  # noqa: E402

from dropsort.application.dto.library import (  # noqa: E402
    MediaFileAvailability,
    MediaFileDetails,
    MovieDetails,
    MovieListItem,
)
from dropsort.application.dto.movie_import import (  # noqa: E402
    ImportProposalReason,
    ImportProposalStatus,
    MovieImportProposal,
)
from dropsort.media.discovery.models import (  # noqa: E402
    DiscoveryClassification,
    DiscoveredMedia,
)
from dropsort.media.matcher.models import (  # noqa: E402
    CandidateScore,
    MatchDecision,
    MatchReason,
    MatchStatus,
)
from dropsort.media.parser import MediaType, ParsedMedia  # noqa: E402
from dropsort.metadata.contracts import MovieCandidate  # noqa: E402


@pytest.fixture
def png_bytes() -> bytes:
    return base64.b64decode(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="
    )


@pytest.fixture(scope="session")
def qapp() -> QApplication:
    application = QApplication.instance() or QApplication([])
    return application


@pytest.fixture(autouse=True)
def flush_qt_deferred_deletes(qapp: QApplication):
    """Keep widget lifetimes isolated before later tests start nested event loops."""
    yield
    QCoreApplication.sendPostedEvents(None, QEvent.Type.DeferredDelete)
    qapp.processEvents()


@pytest.fixture
def movie_item_factory():
    def build(**overrides: object) -> MovieListItem:
        values: dict[str, object] = {
            "movie_id": 1,
            "provider": "tmdb",
            "title": "The Dark Knight",
            "original_title": None,
            "year": 2008,
            "rating": 8.5,
            "poster_reference": "/poster.jpg",
            "media_file_count": 2,
            "missing_file_count": 0,
            "date_added": datetime(2026, 1, 2, tzinfo=UTC),
        }
        values.update(overrides)
        return MovieListItem(**values)  # type: ignore[arg-type]

    return build


@pytest.fixture
def movie_details_factory():
    def build(**overrides: object) -> MovieDetails:
        values: dict[str, object] = {
            "movie_id": 1,
            "provider": "tmdb",
            "external_id": "155",
            "title": "The Dark Knight",
            "original_title": None,
            "year": 2008,
            "overview": "Batman faces a criminal mastermind.",
            "genres": ("Action", "Crime"),
            "runtime_minutes": 152,
            "rating": 8.5,
            "poster_reference": "/poster.jpg",
            "date_added": datetime(2026, 1, 2, tzinfo=UTC),
            "media_files": (
                MediaFileDetails(
                    media_file_id=10,
                    current_path=r"D:\Movies\The Dark Knight.mkv",
                    file_size=1_500_000_000,
                    extension=".mkv",
                    resolution="1080p",
                    codec="x264",
                    source="BluRay",
                    status=MediaFileAvailability.PRESENT,
                ),
                MediaFileDetails(
                    media_file_id=11,
                    current_path=r"E:\Archive\The Dark Knight 4K.mkv",
                    file_size=4_000_000_000,
                    extension=".mkv",
                    resolution="2160p",
                    codec="x265",
                    source="REMUX",
                    status=MediaFileAvailability.MISSING,
                ),
            ),
        }
        values.update(overrides)
        return MovieDetails(**values)  # type: ignore[arg-type]

    return build


@pytest.fixture
def candidate_factory():
    def build(**overrides: object) -> MovieCandidate:
        values: dict[str, object] = {
            "provider": "tmdb",
            "external_id": "155",
            "title": "The Dark Knight",
            "original_title": None,
            "year": 2008,
            "overview": "A masked vigilante faces chaos.",
            "rating": 8.5,
            "poster_reference": "/poster.jpg",
        }
        values.update(overrides)
        return MovieCandidate(**values)  # type: ignore[arg-type]

    return build


@pytest.fixture
def discovery_factory():
    def build(**overrides: object) -> DiscoveredMedia:
        values: dict[str, object] = {
            "path": Path.cwd() / "The.Dark.Knight.2008.mkv",
            "file_size": 1_000,
            "parsed_media": ParsedMedia(
                original_name="The.Dark.Knight.2008.mkv",
                media_type=MediaType.MOVIE,
                title="The Dark Knight",
                year=2008,
                resolution=None,
                source=None,
                codec=None,
                extension=".mkv",
            ),
            "classification": DiscoveryClassification.MOVIE_CANDIDATE,
            "issue": None,
        }
        values.update(overrides)
        return DiscoveredMedia(**values)  # type: ignore[arg-type]

    return build


@pytest.fixture
def proposal_factory(candidate_factory, discovery_factory):
    def build(**overrides: object) -> MovieImportProposal:
        candidate = overrides.pop("candidate", candidate_factory())
        discovery = overrides.pop("discovery", discovery_factory())
        status = overrides.pop("status", ImportProposalStatus.MATCH_PROPOSED)
        if status in {
            ImportProposalStatus.MATCH_PROPOSED,
            ImportProposalStatus.REVIEW_REQUIRED,
        }:
            match_status = (
                MatchStatus.MATCHED
                if status is ImportProposalStatus.MATCH_PROPOSED
                else MatchStatus.REVIEW_REQUIRED
            )
            score = CandidateScore(
                candidate=candidate,
                score=0.98,
                reasons=(MatchReason.TITLE_EXACT, MatchReason.YEAR_EXACT),
                penalties=(),
            )
            decision = MatchDecision(
                status=match_status,
                candidate=candidate,
                confidence=0.98,
                reasons=(MatchReason.TITLE_EXACT, MatchReason.YEAR_EXACT),
                ranked_candidates=(score,),
            )
            values: dict[str, object] = {
                "status": status,
                "discovery": discovery,
                "candidates": (candidate,),
                "match_decision": decision,
                "proposed_candidate": candidate,
                "reasons": (
                    ImportProposalReason.MATCHED_CANDIDATE
                    if status is ImportProposalStatus.MATCH_PROPOSED
                    else ImportProposalReason.REVIEW_REQUIRED,
                ),
                "existing_media_file_id": None,
            }
        else:
            reason = {
                ImportProposalStatus.NO_MATCH: ImportProposalReason.NO_MATCH,
                ImportProposalStatus.METADATA_UNAVAILABLE: ImportProposalReason.METADATA_UNAVAILABLE,
                ImportProposalStatus.ALREADY_IN_LIBRARY: ImportProposalReason.ALREADY_IN_LIBRARY,
            }[status]
            values = {
                "status": status,
                "discovery": discovery,
                "candidates": (),
                "match_decision": None,
                "proposed_candidate": None,
                "reasons": (reason,),
                "existing_media_file_id": 9
                if status is ImportProposalStatus.ALREADY_IN_LIBRARY
                else None,
            }
        values.update(overrides)
        return MovieImportProposal(**values)  # type: ignore[arg-type]

    return build
