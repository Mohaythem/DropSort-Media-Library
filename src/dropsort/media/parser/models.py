from __future__ import annotations

from dataclasses import dataclass
from enum import StrEnum


class MediaType(StrEnum):
    MOVIE = "MOVIE"
    TV_EPISODE = "TV_EPISODE"
    UNKNOWN = "UNKNOWN"


@dataclass(frozen=True, slots=True)
class ParsedMedia:
    original_name: str
    media_type: MediaType
    title: str | None
    year: int | None
    resolution: str | None
    source: str | None
    codec: str | None
    extension: str
