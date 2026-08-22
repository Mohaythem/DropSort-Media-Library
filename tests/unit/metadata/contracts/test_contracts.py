from dataclasses import FrozenInstanceError

import pytest

from dropsort.metadata.contracts import (
    MetadataProvider,
    MovieCandidate,
    MovieMetadata,
    MovieSearchQuery,
)


class ExampleProvider:
    provider_name = "example"

    def search_movies(self, query: MovieSearchQuery) -> tuple[MovieCandidate, ...]:
        return ()

    def get_movie(self, external_id: str) -> MovieMetadata:
        return MovieMetadata(
            provider=self.provider_name,
            external_id=external_id,
            title="Example",
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


def test_provider_contract_is_structural_and_provider_neutral() -> None:
    assert isinstance(ExampleProvider(), MetadataProvider)


def test_contract_models_are_immutable() -> None:
    candidate = MovieCandidate(
        provider="example",
        external_id="1",
        title="Movie",
        original_title=None,
        year=2024,
        overview=None,
        rating=None,
        poster_reference=None,
    )

    with pytest.raises(FrozenInstanceError):
        candidate.title = "Changed"  # type: ignore[misc]


@pytest.mark.parametrize("title", ["", " ", "\t\n"])
def test_search_query_rejects_empty_titles(title: str) -> None:
    with pytest.raises(ValueError, match="title"):
        MovieSearchQuery(title=title)


@pytest.mark.parametrize("year", [True, 0, -1, 10000])
def test_search_query_rejects_invalid_years(year: int) -> None:
    with pytest.raises(ValueError, match="year"):
        MovieSearchQuery(title="Movie", year=year)


def test_search_query_normalizes_outer_and_repeated_whitespace() -> None:
    assert MovieSearchQuery(title="  The   Movie\tTitle  ").title == "The Movie Title"


@pytest.mark.parametrize(
    "candidate",
    [
        {"year": 10000},
        {"rating": float("nan")},
        {"overview": ""},
    ],
)
def test_candidate_rejects_malformed_normalized_optional_fields(
    candidate: dict[str, object],
) -> None:
    values = {
        "provider": "example",
        "external_id": "1",
        "title": "Movie",
        "original_title": None,
        "year": 2024,
        "overview": None,
        "rating": None,
        "poster_reference": None,
    }
    values.update(candidate)

    with pytest.raises(ValueError):
        MovieCandidate(**values)  # type: ignore[arg-type]
