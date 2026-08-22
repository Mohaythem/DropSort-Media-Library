from __future__ import annotations

from collections.abc import Callable
from datetime import datetime, timezone

from dropsort.library.movies import (
    Movie,
    MovieCatalogData,
    MovieIdentityConflictError,
    MovieRepository,
)
from dropsort.library.personal import (
    PersonalMovieState,
    PersonalMovieSummary,
    PersonalPreference,
    PersonalLibraryRepository,
    PersonalLibrarySection,
    ReadyToWatchMovie,
    WatchEvent,
)


class EnsureLogicalMovie:
    """Create or reuse a logical Movie without creating a MediaFile."""

    def __init__(self, movies: MovieRepository) -> None:
        self._movies = movies

    def execute(self, data: MovieCatalogData) -> Movie:
        existing = self._movies.get_by_external_id(data.provider, data.external_id)
        if existing is not None:
            return existing
        try:
            return self._movies.create(data, now=datetime.now(timezone.utc))
        except MovieIdentityConflictError as error:
            # A concurrent creator may win the unique provider/external-id race.
            existing = self._movies.get_by_external_id(data.provider, data.external_id)
            if existing is not None:
                return existing
            raise error


EnsureMovie = EnsureLogicalMovie


class SetPersonalPreference:
    def __init__(
        self,
        repository: PersonalLibraryRepository,
        *,
        now: Callable[[], datetime] | None = None,
    ) -> None:
        self._repository = repository
        self._now = now or (lambda: datetime.now(timezone.utc))

    def execute(self, movie_id: int, preference: PersonalPreference) -> PersonalMovieState:
        return self._repository.set_preference(movie_id, preference, now=self._clock())

    def _clock(self) -> datetime:
        return _aware_now(self._now())


class ClearPersonalPreference:
    def __init__(
        self,
        repository: PersonalLibraryRepository,
        *,
        now: Callable[[], datetime] | None = None,
    ) -> None:
        self._repository = repository
        self._now = now or (lambda: datetime.now(timezone.utc))

    def execute(self, movie_id: int) -> PersonalMovieState:
        return self._repository.clear_preference(movie_id, now=_aware_now(self._now()))


class RecordWatch:
    def __init__(
        self,
        repository: PersonalLibraryRepository,
        *,
        now: Callable[[], datetime] | None = None,
    ) -> None:
        self._repository = repository
        self._now = now or (lambda: datetime.now(timezone.utc))

    def execute(self, movie_id: int, watched_at: datetime | None = None) -> WatchEvent:
        created_at = _aware_now(self._now())
        occurred_at = created_at if watched_at is None else _aware_now(watched_at)
        return self._repository.record_watch(
            movie_id,
            watched_at=occurred_at,
            created_at=created_at,
        )


class RemoveWatchEvent:
    def __init__(self, repository: PersonalLibraryRepository) -> None:
        self._repository = repository

    def execute(self, event_id: int) -> WatchEvent:
        return self._repository.remove_watch_event(event_id)


class GetWatchHistory:
    def __init__(self, repository: PersonalLibraryRepository) -> None:
        self._repository = repository

    def execute(self, movie_id: int) -> tuple[WatchEvent, ...]:
        return self._repository.list_watch_history(movie_id)


class AddToWatchlist:
    def __init__(
        self,
        repository: PersonalLibraryRepository,
        *,
        now: Callable[[], datetime] | None = None,
    ) -> None:
        self._repository = repository
        self._now = now or (lambda: datetime.now(timezone.utc))

    def execute(self, movie_id: int) -> PersonalMovieState:
        return self._repository.add_to_watchlist(movie_id, now=_aware_now(self._now()))


class RemoveFromWatchlist:
    def __init__(
        self,
        repository: PersonalLibraryRepository,
        *,
        now: Callable[[], datetime] | None = None,
    ) -> None:
        self._repository = repository
        self._now = now or (lambda: datetime.now(timezone.utc))

    def execute(self, movie_id: int) -> PersonalMovieState:
        return self._repository.remove_from_watchlist(movie_id, now=_aware_now(self._now()))


class GetPersonalMovieState:
    def __init__(self, repository: PersonalLibraryRepository) -> None:
        self._repository = repository

    def execute(self, movie_id: int) -> PersonalMovieState:
        return self._repository.get_state(movie_id)


class QueryReadyToWatch:
    def __init__(self, repository: PersonalLibraryRepository) -> None:
        self._repository = repository

    def execute(self, *, limit: int = 100, offset: int = 0) -> tuple[ReadyToWatchMovie, ...]:
        return self._repository.list_ready_to_watch(limit=limit, offset=offset)


class ListPersonalMovies:
    def __init__(self, repository: PersonalLibraryRepository) -> None:
        self._repository = repository

    def execute(
        self,
        section: PersonalLibrarySection,
        *,
        limit: int = 100,
        offset: int = 0,
    ) -> tuple[PersonalMovieSummary, ...]:
        return self._repository.list_movies(section, limit=limit, offset=offset)


def _aware_now(value: datetime) -> datetime:
    if not isinstance(value, datetime) or value.tzinfo is None:
        raise ValueError("clock must return a timezone-aware datetime")
    return value
