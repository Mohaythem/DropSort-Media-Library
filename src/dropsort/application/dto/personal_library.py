from __future__ import annotations

from dataclasses import dataclass

from dropsort.library.personal import PersonalMovieState, WatchEvent


@dataclass(frozen=True, slots=True)
class PersonalMovieSnapshot:
    """Authoritative personal state and history for one logical movie."""

    state: PersonalMovieState
    history: tuple[WatchEvent, ...]

