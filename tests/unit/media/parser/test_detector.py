from pathlib import PureWindowsPath

import pytest

from dropsort.media.parser import MediaType, detect_media_type, is_supported_video_filename


@pytest.mark.parametrize(
    "filename",
    [
        "Movie.mkv",
        "Movie.MP4",
        "Movie.avi",
        "Movie.mov",
        "Movie.wmv",
        "Movie.m4v",
        "Movie.webm",
        "Movie.mpg",
        "Movie.mpeg",
    ],
)
def test_supported_video_extensions_are_recognized(filename: str) -> None:
    assert is_supported_video_filename(filename)
    assert detect_media_type(filename) is MediaType.MOVIE


@pytest.mark.parametrize(
    "filename",
    [
        "Better.Call.Saul.S03E05.1080p.mkv",
        "better-call-saul-s3e5.mp4",
        "Show.Name.S01E01E02.avi",
        "Show.Name.S03.E05.1080p.mkv",
        "Show.Name.S03-E05.1080p.mkv",
        "Show.Name.1x05.mkv",
        "Show_Name_12x123_WEB-DL.mkv",
        "Show.Name.Season.03.Episode.05.mkv",
    ],
)
def test_obvious_tv_episode_patterns_are_not_movies(filename: str) -> None:
    assert detect_media_type(filename) is MediaType.TV_EPISODE


@pytest.mark.parametrize(
    "filename",
    [
        "random_file.txt",
        "poster.jpg",
        "subtitle.srt",
        "movie",
        "movie.mkv.part",
    ],
)
def test_non_video_files_are_unknown(filename: str) -> None:
    assert not is_supported_video_filename(filename)
    assert detect_media_type(filename) is MediaType.UNKNOWN


@pytest.mark.parametrize(
    "filename",
    [
        "",
        ".mkv",
        "...mkv",
        "1080p.mkv",
        "WEB-DL.mkv",
        "x264.mkv",
        "1080p.BluRay.x264.mkv",
    ],
)
def test_empty_or_technical_only_video_names_are_unknown(filename: str) -> None:
    assert detect_media_type(filename) is MediaType.UNKNOWN


def test_windows_path_input_is_detected_without_filesystem_access() -> None:
    path = PureWindowsPath(r"D:\Movies\Movie.Title.2024.1080p.mkv")

    assert is_supported_video_filename(path)
    assert detect_media_type(path) is MediaType.MOVIE


def test_tv_syntax_with_unsupported_extension_remains_unknown() -> None:
    assert detect_media_type("Show.Name.S01E02.txt") is MediaType.UNKNOWN
