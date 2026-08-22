from __future__ import annotations

from dataclasses import dataclass
from enum import StrEnum
from pathlib import Path

from dropsort.media.parser import MediaType, ParsedMedia


class DiscoveryClassification(StrEnum):
    MOVIE_CANDIDATE = "MOVIE_CANDIDATE"
    TV_EPISODE_SKIPPED = "TV_EPISODE_SKIPPED"
    UNKNOWN_MEDIA = "UNKNOWN_MEDIA"
    ERROR = "ERROR"


class DiscoveryErrorCode(StrEnum):
    ROOT_MISSING = "ROOT_MISSING"
    ROOT_NOT_DIRECTORY = "ROOT_NOT_DIRECTORY"
    ROOT_LINK_NOT_ALLOWED = "ROOT_LINK_NOT_ALLOWED"
    PERMISSION_DENIED = "PERMISSION_DENIED"
    DISAPPEARED = "DISAPPEARED"
    DIRECTORY_READ_FAILED = "DIRECTORY_READ_FAILED"
    STAT_FAILED = "STAT_FAILED"
    LINK_SKIPPED = "LINK_SKIPPED"
    LOOP_SKIPPED = "LOOP_SKIPPED"
    PARSE_FAILED = "PARSE_FAILED"


@dataclass(frozen=True, slots=True)
class DiscoveryProgress:
    """Lightweight monotonic counters for one read-only discovery session."""

    directories_seen: int = 0
    entries_seen: int = 0
    supported_media_found: int = 0
    movie_candidates: int = 0
    tv_episodes_skipped: int = 0
    unknown_media: int = 0
    errors: int = 0

    def __post_init__(self) -> None:
        values = (
            self.directories_seen,
            self.entries_seen,
            self.supported_media_found,
            self.movie_candidates,
            self.tv_episodes_skipped,
            self.unknown_media,
            self.errors,
        )
        if any(isinstance(value, bool) or not isinstance(value, int) or value < 0 for value in values):
            raise ValueError("progress counters must be non-negative integers")
        if self.supported_media_found > self.entries_seen:
            raise ValueError("supported media cannot exceed entries seen")
        classified = self.movie_candidates + self.tv_episodes_skipped + self.unknown_media
        if classified > self.supported_media_found:
            raise ValueError("classified media cannot exceed supported media")


@dataclass(frozen=True, slots=True)
class DiscoveryIssue:
    code: DiscoveryErrorCode
    message: str

    def __post_init__(self) -> None:
        if not isinstance(self.code, DiscoveryErrorCode):
            raise ValueError("code must be DiscoveryErrorCode")
        if not isinstance(self.message, str) or not self.message.strip():
            raise ValueError("message must be a non-empty string")


@dataclass(frozen=True, slots=True)
class DiscoveredMedia:
    path: Path
    file_size: int | None
    parsed_media: ParsedMedia | None
    classification: DiscoveryClassification
    issue: DiscoveryIssue | None

    def __post_init__(self) -> None:
        if not isinstance(self.path, Path) or not self.path.is_absolute():
            raise ValueError("path must be an absolute Path")
        if not isinstance(self.classification, DiscoveryClassification):
            raise ValueError("classification must be DiscoveryClassification")
        if self.classification is DiscoveryClassification.ERROR:
            if self.issue is None:
                raise ValueError("ERROR discovery requires an issue")
            if self.file_size is not None or self.parsed_media is not None:
                raise ValueError("ERROR discovery cannot contain file facts")
            return
        if self.issue is not None:
            raise ValueError("successful discovery cannot contain an issue")
        if (
            isinstance(self.file_size, bool)
            or not isinstance(self.file_size, int)
            or self.file_size < 0
        ):
            raise ValueError("file_size must be a non-negative integer")
        if not isinstance(self.parsed_media, ParsedMedia):
            raise ValueError("parsed_media must be ParsedMedia")
        expected_type = {
            DiscoveryClassification.MOVIE_CANDIDATE: MediaType.MOVIE,
            DiscoveryClassification.TV_EPISODE_SKIPPED: MediaType.TV_EPISODE,
            DiscoveryClassification.UNKNOWN_MEDIA: MediaType.UNKNOWN,
        }[self.classification]
        if self.parsed_media.media_type is not expected_type:
            raise ValueError("classification does not match parsed media type")

    @classmethod
    def error(cls, path: Path, issue: DiscoveryIssue) -> DiscoveredMedia:
        return cls(
            path=path,
            file_size=None,
            parsed_media=None,
            classification=DiscoveryClassification.ERROR,
            issue=issue,
        )
