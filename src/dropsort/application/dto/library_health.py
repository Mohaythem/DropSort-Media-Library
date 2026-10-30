from __future__ import annotations

from dataclasses import dataclass
from enum import StrEnum

from dropsort.application.dto.reconciliation import LibraryReconciliationProgress


class MetadataHealthStatus(StrEnum):
    COMPLETE = "COMPLETE"
    INCOMPLETE = "INCOMPLETE"
    MISSING_POSTER = "MISSING_POSTER"
    NEEDS_MATCH = "NEEDS_MATCH"
    PROVIDER_UNAVAILABLE = "PROVIDER_UNAVAILABLE"
    PROVIDER_VALUE_UNAVAILABLE = "PROVIDER_VALUE_UNAVAILABLE"


class MetadataHealthIssue(StrEnum):
    OVERVIEW = "OVERVIEW"
    RUNTIME = "RUNTIME"
    GENRES = "GENRES"
    YEAR = "YEAR"
    POSTER = "POSTER"
    NEEDS_MATCH = "NEEDS_MATCH"


class MetadataProviderError(StrEnum):
    AUTHENTICATION = "AUTHENTICATION"
    UNAVAILABLE = "UNAVAILABLE"
    RATE_LIMIT = "RATE_LIMIT"
    INVALID_RESPONSE = "INVALID_RESPONSE"


@dataclass(frozen=True, slots=True)
class MetadataHealthItem:
    movie_id: int
    title: str
    status: MetadataHealthStatus
    issues: tuple[MetadataHealthIssue, ...] = ()
    repaired_fields: tuple[MetadataHealthIssue, ...] = ()
    provider_error: MetadataProviderError | None = None


@dataclass(frozen=True, slots=True)
class LibraryHealthProgress:
    file_progress: LibraryReconciliationProgress
    metadata_total: int
    metadata_checked: int
    metadata_complete: int
    metadata_issues: int
    metadata_repaired: int
    metadata_needs_review: int
    metadata_provider_unavailable: int
    items: tuple[MetadataHealthItem, ...] = ()
    changed_movie_ids: tuple[int, ...] = ()

    @property
    def total(self) -> int:
        return self.file_progress.total + self.metadata_total

    @property
    def checked(self) -> int:
        return self.file_progress.checked + self.metadata_checked

    @property
    def present(self) -> int:
        return self.file_progress.present

    @property
    def missing(self) -> int:
        return self.file_progress.missing

    @property
    def errors(self) -> int:
        return self.file_progress.errors

    @property
    def status_changes(self) -> int:
        return self.file_progress.status_changes

