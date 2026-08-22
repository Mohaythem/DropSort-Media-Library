from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime
from enum import StrEnum
from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from dropsort.library.movies import Movie


class PersonalPreference(StrEnum):
    NO_OPINION = "NO_OPINION"
    LIKED = "LIKED"
    BLACKLISTED = "BLACKLISTED"


class PersonalLibrarySection(StrEnum):
    WATCHLIST = "WATCHLIST"
    READY_TO_WATCH = "READY_TO_WATCH"
    LIKED = "LIKED"
    BLACKLISTED = "BLACKLISTED"


@dataclass(frozen=True, slots=True)
class PersonalMovieState:
    """Personal state with watch facts derived from normalized watch events."""

    movie_id: int
    preference: PersonalPreference
    watchlist_added_at: datetime | None
    watch_count: int
    last_watched: datetime | None
    created_at: datetime | None
    updated_at: datetime | None

    @property
    def is_watchlisted(self) -> bool:
        return self.watchlist_added_at is not None

    @property
    def watched(self) -> bool:
        return self.watch_count > 0


@dataclass(frozen=True, slots=True)
class WatchEvent:
    """One viewing occurrence; rewatch is derived by chronological ordering."""

    id: int
    movie_id: int
    watched_at: datetime
    rewatch: bool
    created_at: datetime | None = None


@dataclass(frozen=True, slots=True)
class ReadyToWatchMovie:
    movie_id: int
    movie: Movie
    present_media_file_count: int
    watchlist_added_at: datetime


@dataclass(frozen=True, slots=True)
class PersonalMovieSummary:
    movie: Movie
    media_file_count: int
    missing_file_count: int
    preference: PersonalPreference
    watchlisted: bool
    watch_count: int
    last_watched: datetime | None
