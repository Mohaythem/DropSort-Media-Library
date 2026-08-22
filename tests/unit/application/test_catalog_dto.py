from __future__ import annotations

from datetime import datetime, timezone
from pathlib import Path

import pytest

from dropsort.application.dto import RegisterMovieFileCommand
from dropsort.media.parser.models import MediaType, ParsedMedia
from dropsort.metadata.contracts import MovieMetadata


NOW = datetime(2026, 8, 11, 12, 0, tzinfo=timezone.utc)


def _metadata() -> MovieMetadata:
    return MovieMetadata(
        provider="tmdb",
        external_id="1",
        title="Movie",
        original_title=None,
        year=None,
        overview=None,
        genres=(),
        runtime_minutes=None,
        rating=None,
        director=None,
        cast=(),
        poster_reference=None,
    )


def _parsed() -> ParsedMedia:
    return ParsedMedia("Movie.mkv", MediaType.MOVIE, "Movie", None, None, None, None, ".mkv")


@pytest.mark.parametrize(
    ("field", "value", "message"),
    [
        ("metadata", None, "metadata"),
        ("parsed_media", None, "parsed_media"),
        ("file_size", -1, "file_size"),
        ("file_size", True, "file_size"),
        ("observed_at", datetime(2026, 8, 11), "timezone-aware"),
    ],
)
def test_registration_command_rejects_invalid_boundary_values(
    tmp_path: Path,
    field: str,
    value: object,
    message: str,
) -> None:
    values: dict[str, object] = {
        "metadata": _metadata(),
        "parsed_media": _parsed(),
        "file_path": (tmp_path / "Movie.mkv").absolute(),
        "file_size": 1,
        "observed_at": NOW,
    }
    values[field] = value

    with pytest.raises(ValueError, match=message):
        RegisterMovieFileCommand(**values)  # type: ignore[arg-type]

