from __future__ import annotations

import pytest

from dropsort.application.use_cases.manual_movie_search import ManualMovieSearch
from dropsort.application.dto.manual_search import ManualMovieSearchRequest
from dropsort.metadata.contracts import MovieCandidate
from dropsort.metadata.contracts.errors import MetadataUnavailableError


def _candidate(external_id: str, title: str = "The Wind Rises") -> MovieCandidate:
    return MovieCandidate("tmdb", external_id, title, "Kaze Tachinu", 2013, "A story.", 8.0, "/poster.jpg")


class FakeProvider:
    def __init__(self, results=(), error: BaseException | None = None) -> None:
        self.results = tuple(results)
        self.error = error
        self.queries = []

    def search_movies(self, query):
        self.queries.append(query)
        if self.error is not None:
            raise self.error
        return self.results


def test_manual_search_normalizes_query_and_deduplicates_candidates() -> None:
    first = _candidate("579")
    provider = FakeProvider((first, _candidate("579"), _candidate("580", "Kaze Tachinu")))

    result = ManualMovieSearch(provider).execute("  Kaze   Tachinu ", "2013")

    assert result.query.title == "Kaze Tachinu"
    assert result.query.year == 2013
    assert [candidate.external_id for candidate in result.candidates] == ["579", "580"]
    assert provider.queries[0].title == "Kaze Tachinu"
    assert provider.queries[0].year == 2013


def test_manual_search_deduplicates_before_limiting_to_five() -> None:
    candidates = tuple(_candidate(str(index), f"Movie {index}") for index in range(1, 8))
    provider = FakeProvider((candidates[0], *candidates))

    result = ManualMovieSearch(provider).execute("Movies")

    assert [candidate.external_id for candidate in result.candidates] == ["1", "2", "3", "4", "5"]


def test_manual_search_omits_blank_year() -> None:
    provider = FakeProvider((_candidate("579"),))
    result = ManualMovieSearch(provider).execute("Kaze Tachinu", "   ")
    assert result.query.year is None
    assert provider.queries[0].year is None


@pytest.mark.parametrize("year", ("13", "201x", "10000"))
def test_manual_search_rejects_invalid_year_locally(year: str) -> None:
    provider = FakeProvider()
    with pytest.raises(ValueError):
        ManualMovieSearch(provider).execute("Kaze Tachinu", year)
    assert provider.queries == []


def test_manual_search_preserves_provider_failure() -> None:
    provider = FakeProvider(error=MetadataUnavailableError("offline"))
    with pytest.raises(MetadataUnavailableError):
        ManualMovieSearch(provider).execute("Kaze Tachinu")


def test_kaze_tachinu_filename_remains_a_normal_movie_discovery(tmp_path) -> None:
    from dropsort.media.discovery import DiscoveryClassification, DiscoveredMedia
    from dropsort.media.parser import MediaType, ParsedMedia

    parsed = ParsedMedia(
        "AnimeSanka.com Kaze Tachinu [Bluray - 1080p - Ar - x265].mp4",
        MediaType.MOVIE,
        "AnimeSanka com Kaze Tachinu",
        None,
        "1080p",
        "BluRay",
        "x265",
        ".mp4",
    )
    discovery = DiscoveredMedia((tmp_path / parsed.original_name).absolute(), 100, parsed, DiscoveryClassification.MOVIE_CANDIDATE, None)
    assert discovery.parsed_media.title != "Kaze Tachinu"
    assert discovery.classification is DiscoveryClassification.MOVIE_CANDIDATE


def test_manual_request_rejects_blank_title_and_invalid_year_type() -> None:
    with pytest.raises(ValueError):
        ManualMovieSearchRequest("   ")
    with pytest.raises(ValueError):
        ManualMovieSearchRequest("Movie", 99)
    with pytest.raises(ValueError):
        ManualMovieSearchRequest("Movie", True)
