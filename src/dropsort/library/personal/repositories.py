from __future__ import annotations

from datetime import datetime
from typing import Protocol

from dropsort.library.personal.models import (
    PersonalMovieState,
    PersonalMovieSummary,
    PersonalPreference,
    PersonalLibrarySection,
    ReadyToWatchMovie,
    WatchEvent,
)


class PersonalLibraryRepository(Protocol):
    def get_state(self, movie_id: int) -> PersonalMovieState: ...

    def set_preference(
        self,
        movie_id: int,
        preference: PersonalPreference,
        *,
        now: datetime,
    ) -> PersonalMovieState: ...

    def clear_preference(self, movie_id: int, *, now: datetime) -> PersonalMovieState: ...

    def record_watch(
        self,
        movie_id: int,
        *,
        watched_at: datetime,
        created_at: datetime,
    ) -> WatchEvent: ...

    def remove_watch_event(self, event_id: int) -> WatchEvent: ...

    def list_watch_history(self, movie_id: int) -> tuple[WatchEvent, ...]: ...

    def add_to_watchlist(self, movie_id: int, *, now: datetime) -> PersonalMovieState: ...

    def remove_from_watchlist(self, movie_id: int, *, now: datetime) -> PersonalMovieState: ...

    def list_ready_to_watch(
        self,
        *,
        limit: int,
        offset: int,
    ) -> tuple[ReadyToWatchMovie, ...]: ...

    def list_movies(
        self,
        section: PersonalLibrarySection,
        *,
        limit: int,
        offset: int,
    ) -> tuple[PersonalMovieSummary, ...]: ...
