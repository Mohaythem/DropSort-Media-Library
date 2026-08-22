from __future__ import annotations

import os
import re
from typing import TypeAlias

from dropsort.media.parser.models import MediaType


FilenameInput: TypeAlias = str | os.PathLike[str]

SUPPORTED_VIDEO_EXTENSIONS = frozenset(
    {
        ".avi",
        ".m2ts",
        ".m4v",
        ".mkv",
        ".mov",
        ".mp4",
        ".mpeg",
        ".mpg",
        ".ts",
        ".webm",
        ".wmv",
    }
)

_TV_EPISODE_RE = re.compile(
    r"(?<![A-Za-z0-9])(?:"
    r"s\d{1,2}[\s._-]*e\d{1,3}(?:[\s._-]*e\d{1,3})*"
    r"|\d{1,2}x\d{1,3}"
    r"|season[\s._-]*\d{1,2}[\s._-]*episode[\s._-]*\d{1,3}"
    r")(?![A-Za-z0-9])",
    re.IGNORECASE,
)
_TOKEN_RE = re.compile(r"[^\W_]+", re.UNICODE)
_SINGLE_TECHNICAL_TOKENS = frozenset(
    {
        "4k",
        "720p",
        "1080p",
        "2160p",
        "av1",
        "avc",
        "bluray",
        "dvdrip",
        "h264",
        "h265",
        "hdrip",
        "hdtv",
        "hevc",
        "remux",
        "webdl",
        "webrip",
        "x264",
        "x265",
    }
)
_PAIRED_TECHNICAL_TOKENS = frozenset(
    {
        ("blu", "ray"),
        ("dvd", "rip"),
        ("h", "264"),
        ("h", "265"),
        ("hd", "rip"),
        ("web", "dl"),
        ("web", "rip"),
    }
)


def filename_parts(value: FilenameInput) -> tuple[str, str, str]:
    """Return the untouched input, basename stem, and normalized extension."""
    original = os.fspath(value)
    basename = original.replace("\\", "/").rsplit("/", 1)[-1]
    dot_index = basename.rfind(".")
    if dot_index < 0:
        return original, basename, ""
    return original, basename[:dot_index], basename[dot_index:].casefold()


def is_supported_video_filename(value: FilenameInput) -> bool:
    _, _, extension = filename_parts(value)
    return extension in SUPPORTED_VIDEO_EXTENSIONS


def detect_media_type(value: FilenameInput) -> MediaType:
    _, stem, extension = filename_parts(value)
    if extension not in SUPPORTED_VIDEO_EXTENSIONS:
        return MediaType.UNKNOWN
    if _TV_EPISODE_RE.search(stem):
        return MediaType.TV_EPISODE
    if not _contains_meaningful_title_token(stem):
        return MediaType.UNKNOWN
    return MediaType.MOVIE


def _contains_meaningful_title_token(stem: str) -> bool:
    tokens = [token.casefold() for token in _TOKEN_RE.findall(stem)]
    index = 0
    while index < len(tokens):
        token = tokens[index]
        if token in _SINGLE_TECHNICAL_TOKENS:
            index += 1
            continue
        if index + 1 < len(tokens) and (token, tokens[index + 1]) in _PAIRED_TECHNICAL_TOKENS:
            index += 2
            continue
        return True
    return False
