from __future__ import annotations

from collections.abc import Iterable
import re

from dropsort.media.parser.detector import (
    FilenameInput,
    SUPPORTED_VIDEO_EXTENSIONS,
    detect_media_type,
    filename_parts,
)
from dropsort.media.parser.models import MediaType, ParsedMedia


MIN_FILM_YEAR = 1888
MAX_FILM_YEAR = 2100

_YEAR_RE = re.compile(r"(?<![A-Za-z0-9])(\d{4})(?![A-Za-z0-9])")
_TITLE_SEPARATOR_RE = re.compile(r"[\s._\-()\[\]{}]+")
_ONLY_SEPARATORS_RE = re.compile(r"[\s._\-()\[\]{}]*")
_LEADING_RELEASE_SITE_RE = re.compile(
    r"^\s*(?:\[\s*)?(?:www\.)?[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?"
    r"\.(?:com|net|org)\b(?:\s*\])?[\s._-]*",
    re.IGNORECASE,
)


def _release_token(pattern: str) -> re.Pattern[str]:
    return re.compile(
        rf"(?<![A-Za-z0-9])(?:{pattern})(?![A-Za-z0-9])",
        re.IGNORECASE,
    )


_RESOLUTION_PATTERNS = (
    ("720p", _release_token(r"720p")),
    ("1080p", _release_token(r"1080p")),
    ("2160p", _release_token(r"2160p")),
    ("4K", _release_token(r"4k")),
)
_SOURCE_PATTERNS = (
    ("BluRay", _release_token(r"blu[\s._-]*ray")),
    ("WEB-DL", _release_token(r"web[\s._-]*dl")),
    ("WEBRip", _release_token(r"web[\s._-]*rip")),
    ("HDRip", _release_token(r"hd[\s._-]*rip")),
    ("DVDRip", _release_token(r"dvd[\s._-]*rip")),
    ("REMUX", _release_token(r"remux")),
    ("HDTV", _release_token(r"hdtv")),
)
_CODEC_PATTERNS = (
    ("x264", _release_token(r"x264")),
    ("H.264", _release_token(r"h[\s._-]*264|avc")),
    ("x265", _release_token(r"x265")),
    ("H.265", _release_token(r"h[\s._-]*265|hevc")),
    ("AV1", _release_token(r"av1")),
)


def parse_media_filename(value: FilenameInput) -> ParsedMedia:
    original, stem, extension = filename_parts(value)
    stem = _LEADING_RELEASE_SITE_RE.sub("", stem, count=1)
    media_type = detect_media_type(value)
    if media_type is MediaType.UNKNOWN and extension not in SUPPORTED_VIDEO_EXTENSIONS:
        return _empty_result(original, extension)

    resolutions = _find_values(stem, _RESOLUTION_PATTERNS)
    sources = _find_values(stem, _SOURCE_PATTERNS)
    codecs = _find_values(stem, _CODEC_PATTERNS)
    technical_starts = [start for _, start in (*resolutions, *sources, *codecs)]

    resolution = _unique_value(resolutions)
    source = _source_value(sources)
    codec = _unique_value(codecs)

    if media_type is MediaType.TV_EPISODE:
        return ParsedMedia(
            original_name=original,
            media_type=media_type,
            title=None,
            year=None,
            resolution=resolution,
            source=source,
            codec=codec,
            extension=extension,
        )

    year_match = _select_year(stem, technical_starts)
    year = int(year_match.group(1)) if year_match is not None else None
    title_end = (
        year_match.start()
        if year_match is not None
        else min(technical_starts, default=len(stem))
    )
    title = _clean_title(stem[:title_end])
    if media_type is MediaType.UNKNOWN or title is None:
        media_type = MediaType.UNKNOWN

    return ParsedMedia(
        original_name=original,
        media_type=media_type,
        title=title,
        year=year,
        resolution=resolution,
        source=source,
        codec=codec,
        extension=extension,
    )


def _empty_result(original: str, extension: str) -> ParsedMedia:
    return ParsedMedia(
        original_name=original,
        media_type=MediaType.UNKNOWN,
        title=None,
        year=None,
        resolution=None,
        source=None,
        codec=None,
        extension=extension,
    )


def _find_values(
    stem: str,
    patterns: Iterable[tuple[str, re.Pattern[str]]],
) -> list[tuple[str, int]]:
    found: list[tuple[str, int]] = []
    for normalized, pattern in patterns:
        found.extend((normalized, match.start()) for match in pattern.finditer(stem))
    return found


def _unique_value(found: list[tuple[str, int]]) -> str | None:
    values = {value for value, _ in found}
    return next(iter(values)) if len(values) == 1 else None


def _source_value(found: list[tuple[str, int]]) -> str | None:
    values = {value for value, _ in found}
    if values == {"BluRay", "REMUX"}:
        return "REMUX"
    return next(iter(values)) if len(values) == 1 else None


def _select_year(stem: str, technical_starts: list[int]) -> re.Match[str] | None:
    candidates = [
        match
        for match in _YEAR_RE.finditer(stem)
        if MIN_FILM_YEAR <= int(match.group(1)) <= MAX_FILM_YEAR
        and _clean_title(stem[: match.start()]) is not None
        and not any(start < match.start() for start in technical_starts)
    ]
    if len(candidates) == 1:
        return candidates[0]
    if len(candidates) < 2:
        return None

    immediately_before_technical: list[re.Match[str]] = []
    for candidate in candidates:
        following_starts = [start for start in technical_starts if start >= candidate.end()]
        if not following_starts:
            continue
        between = stem[candidate.end() : min(following_starts)]
        if _ONLY_SEPARATORS_RE.fullmatch(between):
            immediately_before_technical.append(candidate)
    return immediately_before_technical[0] if len(immediately_before_technical) == 1 else None


def _clean_title(fragment: str) -> str | None:
    title = _TITLE_SEPARATOR_RE.sub(" ", fragment).strip()
    return title or None
