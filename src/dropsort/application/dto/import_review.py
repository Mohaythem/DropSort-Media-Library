from __future__ import annotations

from dataclasses import dataclass
from enum import StrEnum
from pathlib import Path

from dropsort.application.dto.movie_import import MovieImportProposal


class ImportReviewStage(StrEnum):
    DISCOVERING = "DISCOVERING"
    PREPARING_METADATA = "PREPARING_METADATA"
    BUILDING_REVIEW = "BUILDING_REVIEW"


@dataclass(frozen=True, slots=True)
class ImportReviewProgress:
    stage: ImportReviewStage
    directories_seen: int = 0
    entries_seen: int = 0
    supported_media_found: int = 0
    movie_candidates: int = 0
    tv_episodes_skipped: int = 0
    unknown_media: int = 0
    discovery_errors: int = 0
    proposal_completed: int = 0
    proposal_total: int = 0

    def __post_init__(self) -> None:
        if not isinstance(self.stage, ImportReviewStage):
            raise ValueError("stage must be ImportReviewStage")
        values = (
            self.directories_seen,
            self.entries_seen,
            self.supported_media_found,
            self.movie_candidates,
            self.tv_episodes_skipped,
            self.unknown_media,
            self.discovery_errors,
            self.proposal_completed,
            self.proposal_total,
        )
        if any(isinstance(value, bool) or not isinstance(value, int) or value < 0 for value in values):
            raise ValueError("progress counters must be non-negative integers")
        if self.proposal_completed > self.proposal_total:
            raise ValueError("proposal completed count cannot exceed proposal total")


@dataclass(frozen=True, slots=True)
class ImportReviewSummary:
    directories_seen: int = 0
    entries_seen: int = 0
    supported_media_found: int = 0
    movie_candidates: int = 0
    tv_episodes_skipped: int = 0
    unknown_media: int = 0
    discovery_errors: int = 0
    already_in_library: int = 0
    ready_for_review: int = 0
    no_match: int = 0
    metadata_unavailable: int = 0

    def __post_init__(self) -> None:
        values = tuple(getattr(self, field) for field in self.__dataclass_fields__)
        if any(isinstance(value, bool) or not isinstance(value, int) or value < 0 for value in values):
            raise ValueError("summary counters must be non-negative integers")


@dataclass(frozen=True, slots=True)
class ImportReviewSession:
    """Read-only scan/proposal snapshot prepared for an explicit review UI."""

    root: Path
    recursive: bool
    items: tuple[MovieImportProposal, ...]
    summary: ImportReviewSummary = ImportReviewSummary()

    def __post_init__(self) -> None:
        if not isinstance(self.root, Path) or not self.root.is_absolute():
            raise ValueError("root must be an absolute Path")
        if not isinstance(self.recursive, bool):
            raise ValueError("recursive must be a boolean")
        if not isinstance(self.items, tuple) or any(
            not isinstance(item, MovieImportProposal) for item in self.items
        ):
            raise ValueError("items must contain MovieImportProposal values")
        if not isinstance(self.summary, ImportReviewSummary):
            raise ValueError("summary must be ImportReviewSummary")
