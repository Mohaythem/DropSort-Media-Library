from __future__ import annotations

from pathlib import Path

import pytest

from dropsort.media.discovery import (
    DiscoveryClassification,
    DiscoveryErrorCode,
    DiscoveryIssue,
    DiscoveredMedia,
)
from dropsort.media.parser import MediaType, ParsedMedia


def _parsed(media_type: MediaType = MediaType.MOVIE) -> ParsedMedia:
    return ParsedMedia(
        original_name="Movie.2024.mkv",
        media_type=media_type,
        title="Movie" if media_type is MediaType.MOVIE else None,
        year=2024 if media_type is MediaType.MOVIE else None,
        resolution=None,
        source=None,
        codec=None,
        extension=".mkv",
    )


def test_movie_discovery_item_is_immutable_and_coherent(tmp_path: Path) -> None:
    item = DiscoveredMedia(
        path=(tmp_path / "Movie.mkv").absolute(),
        file_size=100,
        parsed_media=_parsed(),
        classification=DiscoveryClassification.MOVIE_CANDIDATE,
        issue=None,
    )

    assert item.file_size == 100
    assert item.parsed_media.media_type is MediaType.MOVIE  # type: ignore[union-attr]


def test_error_item_requires_issue_and_has_no_file_facts(tmp_path: Path) -> None:
    issue = DiscoveryIssue(
        code=DiscoveryErrorCode.PERMISSION_DENIED,
        message="not readable",
    )
    item = DiscoveredMedia.error((tmp_path / "blocked").absolute(), issue)

    assert item.classification is DiscoveryClassification.ERROR
    assert item.file_size is None
    assert item.parsed_media is None


def test_incoherent_discovery_models_are_rejected(tmp_path: Path) -> None:
    path = (tmp_path / "Movie.mkv").absolute()
    with pytest.raises(ValueError, match="absolute"):
        DiscoveredMedia(Path("Movie.mkv"), 1, _parsed(), DiscoveryClassification.MOVIE_CANDIDATE, None)
    with pytest.raises(ValueError, match="classification"):
        DiscoveredMedia(path, 1, _parsed(), "MOVIE_CANDIDATE", None)  # type: ignore[arg-type]
    with pytest.raises(ValueError, match="issue"):
        DiscoveredMedia(path, None, None, DiscoveryClassification.ERROR, None)
    with pytest.raises(ValueError, match="cannot contain"):
        DiscoveredMedia(
            path,
            1,
            _parsed(),
            DiscoveryClassification.ERROR,
            DiscoveryIssue(DiscoveryErrorCode.STAT_FAILED, "failed"),
        )
    with pytest.raises(ValueError, match="cannot contain an issue"):
        DiscoveredMedia(
            path,
            1,
            _parsed(),
            DiscoveryClassification.MOVIE_CANDIDATE,
            DiscoveryIssue(DiscoveryErrorCode.STAT_FAILED, "failed"),
        )
    with pytest.raises(ValueError, match="file_size"):
        DiscoveredMedia(path, None, _parsed(), DiscoveryClassification.MOVIE_CANDIDATE, None)
    with pytest.raises(ValueError, match="parsed_media"):
        DiscoveredMedia(path, 1, None, DiscoveryClassification.MOVIE_CANDIDATE, None)
    with pytest.raises(ValueError, match="media type"):
        DiscoveredMedia(path, 10, _parsed(MediaType.TV_EPISODE), DiscoveryClassification.MOVIE_CANDIDATE, None)


def test_discovery_issue_requires_controlled_code_and_message() -> None:
    with pytest.raises(ValueError, match="code"):
        DiscoveryIssue("BAD", "message")  # type: ignore[arg-type]
    with pytest.raises(ValueError, match="message"):
        DiscoveryIssue(DiscoveryErrorCode.STAT_FAILED, "")
