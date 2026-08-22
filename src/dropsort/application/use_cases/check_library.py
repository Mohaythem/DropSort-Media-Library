from __future__ import annotations

from collections.abc import Callable
from datetime import datetime, timezone

from dropsort.application.dto.library_health import (
    LibraryHealthProgress,
    MetadataHealthIssue,
    MetadataHealthItem,
    MetadataHealthStatus,
    MetadataProviderError,
)
from dropsort.application.dto.reconciliation import LibraryReconciliationProgress
from dropsort.application.errors import LibraryReconciliationCancelled
from dropsort.application.use_cases.reconcile_library_files import (
    ReconcileLibraryFiles,
    ReconciliationCancellation,
)
from dropsort.library.movies import Movie, MovieCatalogData, MovieRepository
from dropsort.metadata.contracts import (
    MetadataAuthenticationError,
    MetadataError,
    MetadataProvider,
    MetadataRateLimitError,
    MetadataResponseError,
    MetadataUnavailableError,
    MovieMetadata,
)
from dropsort.posters import PosterActions, PosterRequest


ProgressCallback = Callable[[LibraryHealthProgress], None]
MAX_ISSUE_ITEMS = 100


class CheckLibrary:
    """Run explicit file reconciliation plus bounded, safe metadata repair."""

    def __init__(
        self,
        reconcile_files: ReconcileLibraryFiles,
        movies: MovieRepository,
        provider: MetadataProvider,
        *,
        poster_actions: PosterActions | None = None,
        now: Callable[[], datetime] | None = None,
        batch_size: int = 50,
    ) -> None:
        if isinstance(batch_size, bool) or not isinstance(batch_size, int) or batch_size <= 0:
            raise ValueError("batch_size must be a positive integer")
        self._reconcile_files = reconcile_files
        self._movies = movies
        self._provider = provider
        self._poster_actions = poster_actions
        self._now = now or (lambda: datetime.now(timezone.utc))
        self._batch_size = batch_size

    def execute(
        self,
        *,
        progress: ProgressCallback | None = None,
        cancellation: ReconciliationCancellation | None = None,
    ) -> LibraryHealthProgress:
        file_progress = self._reconcile_files.execute(
            progress=lambda value: _emit(
                progress,
                LibraryHealthProgress(value, 0, 0, 0, 0, 0, 0, 0),
            ),
            cancellation=cancellation,
        )
        total = self._movies.count_all()
        latest = LibraryHealthProgress(file_progress, total, 0, 0, 0, 0, 0, 0)
        _emit(progress, latest)
        checked = complete = issues = repaired = needs_review = provider_unavailable = 0
        issue_items: list[MetadataHealthItem] = []
        after_id = 0
        while checked < total:
            _raise_if_cancelled(cancellation, latest)
            page = self._movies.list_page(after_id=after_id, limit=self._batch_size)
            if not page:
                break
            for movie in page:
                _raise_if_cancelled(cancellation, latest)
                item = self._inspect_movie(movie)
                checked += 1
                after_id = movie.id
                if item.status is MetadataHealthStatus.COMPLETE:
                    complete += 1
                else:
                    issues += 1
                if item.repaired_fields:
                    repaired += 1
                if item.status in {
                    MetadataHealthStatus.NEEDS_MATCH,
                    MetadataHealthStatus.PROVIDER_VALUE_UNAVAILABLE,
                }:
                    needs_review += 1
                if item.status is MetadataHealthStatus.PROVIDER_UNAVAILABLE:
                    provider_unavailable += 1
                if item.status is not MetadataHealthStatus.COMPLETE or item.repaired_fields:
                    if len(issue_items) < MAX_ISSUE_ITEMS:
                        issue_items.append(item)
                latest = LibraryHealthProgress(
                    file_progress,
                    total,
                    checked,
                    complete,
                    issues,
                    repaired,
                    needs_review,
                    provider_unavailable,
                    tuple(issue_items),
                )
                _emit(progress, latest)
        return latest

    def _inspect_movie(self, movie: Movie) -> MetadataHealthItem:
        missing = _missing_fields(movie)
        if not _has_provider_identity(movie, self._provider.provider_name):
            return MetadataHealthItem(
                movie.id,
                movie.title,
                MetadataHealthStatus.NEEDS_MATCH,
                (MetadataHealthIssue.NEEDS_MATCH,),
            )

        poster_cache_missing = False
        if movie.poster_reference and self._poster_actions is not None:
            try:
                poster_cache_missing = (
                    self._poster_actions.load_poster(
                        PosterRequest(movie.provider, movie.poster_reference)
                    )
                    is None
                )
            except Exception:
                # PosterAssetService intentionally collapses provider and cache
                # failures to a safe miss. The library check must not turn that
                # into a metadata identity failure or mutate the media file.
                poster_cache_missing = True
            if poster_cache_missing and MetadataHealthIssue.POSTER not in missing:
                missing.append(MetadataHealthIssue.POSTER)

        metadata_missing = tuple(
            issue
            for issue in missing
            if issue is not MetadataHealthIssue.POSTER or not movie.poster_reference
        )
        repaired: list[MetadataHealthIssue] = []
        remaining: list[MetadataHealthIssue] = []
        provider_error: MetadataProviderError | None = None
        if metadata_missing:
            try:
                remote = self._provider.get_movie(movie.external_id)
            except MetadataAuthenticationError:
                provider_error = MetadataProviderError.AUTHENTICATION
            except MetadataRateLimitError:
                provider_error = MetadataProviderError.RATE_LIMIT
            except MetadataUnavailableError:
                provider_error = MetadataProviderError.UNAVAILABLE
            except MetadataResponseError:
                provider_error = MetadataProviderError.INVALID_RESPONSE
            except MetadataError:
                provider_error = MetadataProviderError.UNAVAILABLE
            else:
                if not isinstance(remote, MovieMetadata) or not _same_identity(movie, remote):
                    provider_error = MetadataProviderError.INVALID_RESPONSE
                else:
                    merged, repaired, remaining = _merge_missing(
                        movie, remote, metadata_missing
                    )
                    if repaired:
                        self._movies.update_metadata(
                            movie.id,
                            merged,
                            now=self._require_aware_now(),
                        )

        if provider_error is not None:
            status = MetadataHealthStatus.PROVIDER_UNAVAILABLE
        elif remaining:
            status = MetadataHealthStatus.PROVIDER_VALUE_UNAVAILABLE
        elif MetadataHealthIssue.POSTER in missing and MetadataHealthIssue.POSTER not in repaired:
            status = MetadataHealthStatus.MISSING_POSTER
        elif repaired:
            status = MetadataHealthStatus.COMPLETE
        elif missing:
            status = MetadataHealthStatus.INCOMPLETE
        else:
            status = MetadataHealthStatus.COMPLETE
        return MetadataHealthItem(
            movie.id,
            movie.title,
            status,
            tuple(missing),
            tuple(repaired),
            provider_error,
        )

    def _require_aware_now(self) -> datetime:
        value = self._now()
        if not isinstance(value, datetime) or value.tzinfo is None:
            raise ValueError("now must return a timezone-aware datetime")
        return value


def _missing_fields(movie: Movie) -> list[MetadataHealthIssue]:
    missing: list[MetadataHealthIssue] = []
    if not movie.overview or not movie.overview.strip():
        missing.append(MetadataHealthIssue.OVERVIEW)
    if movie.runtime_minutes is None or movie.runtime_minutes <= 0:
        missing.append(MetadataHealthIssue.RUNTIME)
    if not movie.genres:
        missing.append(MetadataHealthIssue.GENRES)
    if movie.year is None:
        missing.append(MetadataHealthIssue.YEAR)
    if not movie.poster_reference:
        missing.append(MetadataHealthIssue.POSTER)
    return missing


def _has_provider_identity(movie: Movie, provider_name: str) -> bool:
    return (
        isinstance(movie.provider, str)
        and movie.provider.casefold() == provider_name.casefold()
        and isinstance(movie.external_id, str)
        and bool(movie.external_id.strip())
    )


def _same_identity(movie: Movie, remote: MovieMetadata) -> bool:
    return (
        remote.provider.casefold() == movie.provider.casefold()
        and remote.external_id == movie.external_id
    )


def _merge_missing(
    movie: Movie,
    remote: MovieMetadata,
    missing: tuple[MetadataHealthIssue, ...],
) -> tuple[MovieCatalogData, list[MetadataHealthIssue], list[MetadataHealthIssue]]:
    repaired: list[MetadataHealthIssue] = []
    remaining: list[MetadataHealthIssue] = []

    def value_or_current(issue: MetadataHealthIssue, current, incoming):
        if issue not in missing:
            return current
        if incoming is None or incoming == () or (isinstance(incoming, str) and not incoming.strip()):
            remaining.append(issue)
            return current
        repaired.append(issue)
        return incoming

    data = MovieCatalogData(
        provider=movie.provider,
        external_id=movie.external_id,
        title=movie.title,
        original_title=movie.original_title,
        year=value_or_current(MetadataHealthIssue.YEAR, movie.year, remote.year),
        overview=value_or_current(MetadataHealthIssue.OVERVIEW, movie.overview, remote.overview),
        genres=value_or_current(MetadataHealthIssue.GENRES, movie.genres, remote.genres),
        runtime_minutes=value_or_current(
            MetadataHealthIssue.RUNTIME, movie.runtime_minutes, remote.runtime_minutes
        ),
        rating=movie.rating,
        poster_reference=value_or_current(
            MetadataHealthIssue.POSTER, movie.poster_reference, remote.poster_reference
        ),
    )
    return data, repaired, remaining


def _raise_if_cancelled(cancellation, latest: LibraryHealthProgress) -> None:
    if cancellation is not None and cancellation.is_cancelled():
        raise LibraryReconciliationCancelled(latest)


def _emit(callback: ProgressCallback | None, value: LibraryHealthProgress) -> None:
    if callback is not None:
        callback(value)
