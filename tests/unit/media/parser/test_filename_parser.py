from __future__ import annotations

from itertools import product
from pathlib import PureWindowsPath
import re

import pytest

from dropsort.media.parser import MediaType, ParsedMedia, parse_media_filename


@pytest.mark.parametrize(
    ("filename", "title", "year", "resolution", "source", "codec"),
    [
        (
            "The.Dark.Knight.2008.1080p.BluRay.x264.mkv",
            "The Dark Knight",
            2008,
            "1080p",
            "BluRay",
            "x264",
        ),
        (
            "Interstellar.2014.2160p.WEB-DL.x265.mkv",
            "Interstellar",
            2014,
            "2160p",
            "WEB-DL",
            "x265",
        ),
        (
            "The.Matrix.1999.1080p.BluRay.REMUX.mkv",
            "The Matrix",
            1999,
            "1080p",
            "REMUX",
            None,
        ),
    ],
)
def test_common_release_names_are_parsed(
    filename: str,
    title: str,
    year: int,
    resolution: str,
    source: str,
    codec: str | None,
) -> None:
    parsed = parse_media_filename(filename)

    assert parsed == ParsedMedia(
        original_name=filename,
        media_type=MediaType.MOVIE,
        title=title,
        year=year,
        resolution=resolution,
        source=source,
        codec=codec,
        extension=".mkv",
    )


@pytest.mark.parametrize(
    "filename",
    [
        "The.Dark.Knight.2008.1080p.BluRay.x264.mkv",
        "The_Dark_Knight_2008_1080p_BluRay_x264.mkv",
        "The Dark Knight 2008 1080p BluRay x264.mkv",
        "The-Dark-Knight-2008-1080p-BluRay-x264.mkv",
    ],
)
def test_common_separators_produce_the_same_title(filename: str) -> None:
    parsed = parse_media_filename(filename)

    assert parsed.title == "The Dark Knight"
    assert parsed.year == 2008


@pytest.mark.parametrize(
    ("filename", "title", "year"),
    [
        ("movie.mkv", "movie", None),
        ("The.Movie.1080p.mkv", "The Movie", None),
        ("1917.2019.1080p.mkv", "1917", 2019),
        ("Blade.Runner.2049.2017.1080p.mkv", "Blade Runner 2049", 2017),
        ("Se7en.1995.mkv", "Se7en", 1995),
        ("2001.A.Space.Odyssey.1968.1080p.mkv", "2001 A Space Odyssey", 1968),
        ("1984.mkv", "1984", None),
    ],
)
def test_title_numbers_are_preserved(filename: str, title: str, year: int | None) -> None:
    parsed = parse_media_filename(filename)

    assert parsed.title == title
    assert parsed.year == year


@pytest.mark.parametrize(
    ("filename", "year", "title"),
    [
        ("Movie.Title.1888.mkv", 1888, "Movie Title"),
        ("Movie.Title.2100.mkv", 2100, "Movie Title"),
        ("Movie.Title.1887.1080p.mkv", None, "Movie Title 1887"),
        ("Movie.Title.2101.1080p.mkv", None, "Movie Title 2101"),
        ("Movie.1999.2001.mkv", None, "Movie 1999 2001"),
        ("Movie.1234.2024.2160p.mkv", 2024, "Movie 1234"),
    ],
)
def test_year_parsing_is_bounded_and_avoids_ambiguous_numbers(
    filename: str, year: int | None, title: str
) -> None:
    parsed = parse_media_filename(filename)

    assert parsed.year == year
    assert parsed.title == title


@pytest.mark.parametrize(
    ("filename", "resolution", "source", "codec"),
    [
        ("Movie.Title.2024.720p.HDRip.AVC.mkv", "720p", "HDRip", "H.264"),
        ("Movie.Title.2024.1080p.WEBRip.H264.mkv", "1080p", "WEBRip", "H.264"),
        ("Movie.Title.2024.4K.DVDRip.H.265.mkv", "4K", "DVDRip", "H.265"),
        ("Movie.Title.2024.2160p.HDTV.HEVC.mkv", "2160p", "HDTV", "H.265"),
        ("Movie.Title.2024.AV1.mkv", None, None, "AV1"),
        ("Movie.Title.2024.BluRay.h265.mkv", None, "BluRay", "H.265"),
    ],
)
def test_technical_tokens_are_normalized(
    filename: str,
    resolution: str | None,
    source: str | None,
    codec: str | None,
) -> None:
    parsed = parse_media_filename(filename)

    assert parsed.resolution == resolution
    assert parsed.source == source
    assert parsed.codec == codec


@pytest.mark.parametrize(
    "filename",
    [
        "Better.Call.Saul.S03E05.1080p.mkv",
        "Better.Call.Saul.S03.E05.1080p.mkv",
        "Show.Name.1x05.mkv",
        "Show.Name.Season.03.Episode.05.mkv",
    ],
)
def test_tv_episode_names_are_classified_but_not_parsed_as_movies(filename: str) -> None:
    parsed = parse_media_filename(filename)

    assert parsed.media_type is MediaType.TV_EPISODE
    assert parsed.title is None
    assert parsed.year is None


@pytest.mark.parametrize(
    "filename",
    ["random_file.txt", "poster.jpg", "subtitle.srt", "movie.mkv.part"],
)
def test_unsupported_extensions_return_unknown_without_release_data(filename: str) -> None:
    parsed = parse_media_filename(filename)

    assert parsed.media_type is MediaType.UNKNOWN
    assert parsed.title is None
    assert parsed.year is None
    assert parsed.resolution is None
    assert parsed.source is None
    assert parsed.codec is None


@pytest.mark.parametrize(
    "filename",
    [
        "",
        ".",
        "..",
        ".mkv",
        "...mkv",
        "   .mkv",
        "\x00.mkv",
        "[].mkv",
        "---.mkv",
        "Movie..__--Title.2024...1080p.mkv",
        "Movie.999999999999999999999999.mkv",
        "🎬.2024.mkv",
    ],
)
def test_malformed_and_unusual_names_fail_safely(filename: str) -> None:
    parsed = parse_media_filename(filename)

    assert isinstance(parsed, ParsedMedia)
    assert parsed.original_name == filename
    assert parsed.year is None or 1888 <= parsed.year <= 2100
    assert parsed.media_type in MediaType
    if parsed.title is not None:
        assert not re.search(r"\s{2,}", parsed.title)


def test_conflicting_technical_values_preserve_uncertainty() -> None:
    parsed = parse_media_filename(
        "Movie.Title.2024.720p.1080p.WEB-DL.BluRay.x264.x265.mkv"
    )

    assert parsed.media_type is MediaType.MOVIE
    assert parsed.resolution is None
    assert parsed.source is None
    assert parsed.codec is None


def test_remux_only_supersedes_its_normal_bluray_medium() -> None:
    bluray_remux = parse_media_filename("Movie.Title.2024.BluRay.REMUX.mkv")
    contradictory = parse_media_filename("Movie.Title.2024.WEB-DL.REMUX.mkv")

    assert bluray_remux.source == "REMUX"
    assert contradictory.source is None


def test_technical_tokens_before_a_year_are_not_treated_as_title_or_release_year() -> None:
    parsed = parse_media_filename("Movie.1080p.2024.mkv")

    assert parsed.title == "Movie"
    assert parsed.year is None
    assert parsed.resolution == "1080p"


def test_path_input_preserves_original_value_and_uses_only_its_filename() -> None:
    value = PureWindowsPath(r"D:\Incoming\Movie.Title.2024.1080p.WEB-DL.x264.MKV")

    parsed = parse_media_filename(value)

    assert parsed.original_name == str(value)
    assert parsed.title == "Movie Title"
    assert parsed.extension == ".mkv"


def test_parsing_does_not_modify_mutable_input_holder() -> None:
    values = ["Movie.Title.2024.1080p.mkv"]
    before = values.copy()

    parse_media_filename(values[0])

    assert values == before


def test_small_deterministic_filename_fuzz_corpus_never_throws() -> None:
    alphabet = (".", "_", "-", " ", "[", "]", "A", "1", "\x00", "🎬")
    samples = [""]
    for length in range(1, 4):
        samples.extend("".join(parts) for parts in product(alphabet, repeat=length))

    for sample in samples:
        parsed = parse_media_filename(f"{sample}.mkv")
        assert parsed.original_name == f"{sample}.mkv"
        assert parsed.year is None or 1888 <= parsed.year <= 2100
        if parsed.title is not None:
            assert not re.search(r"\s{2,}", parsed.title)


@pytest.mark.parametrize(
    "filename",
    (
        "example.com Movie Title 2024 1080p.mkv",
        "example.net Movie Title 2024 1080p.mkv",
        "example.org Movie Title 2024 1080p.mkv",
        "www.example.com Movie Title 2024 1080p.mkv",
        "[example.com] Movie Title 2024 1080p.mkv",
        "AnimeSanka.com Kaze Tachinu [Bluray - 1080p - Ar - x265].mp4",
    ),
)
def test_leading_release_site_is_removed_before_separator_normalization(
    filename: str,
) -> None:
    parsed = parse_media_filename(filename)

    expected = "Kaze Tachinu" if "AnimeSanka" in filename else "Movie Title"
    assert parsed.title == expected


@pytest.mark.parametrize(
    "filename",
    (
        "Coma.2020.mkv",
        "Dot.2022.mkv",
        "Net.2021.mkv",
        "Org.2019.mkv",
        "1917.2019.mkv",
        "1984.mkv",
        "2001.A.Space.Odyssey.1968.mkv",
        "Blade.Runner.2049.2017.mkv",
        "Se7en.1995.mkv",
    ),
)
def test_domain_cleanup_does_not_remove_legitimate_title_words_or_numbers(
    filename: str,
) -> None:
    parsed = parse_media_filename(filename)

    assert parsed.title is not None
    assert parsed.media_type is MediaType.MOVIE
