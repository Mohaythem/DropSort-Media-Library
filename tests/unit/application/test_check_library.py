from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
import base64

import pytest

from dropsort.application.dto.library_health import (
    MetadataHealthIssue,
    MetadataHealthStatus,
)
from dropsort.application.dto.reconciliation import LibraryReconciliationProgress
from dropsort.application.errors import LibraryReconciliationCancelled
from dropsort.application.use_cases import CheckLibrary, ReconciliationCancellation
from dropsort.library.movies import Movie, MovieCatalogData
from dropsort.metadata.contracts import (
    MetadataAuthenticationError,
    MetadataError,
    MetadataRateLimitError,
    MetadataResponseError,
    MetadataUnavailableError,
    MovieMetadata,
)
from dropsort.posters import PosterAsset, PosterAssetCache, PosterAssetService, PosterRequest


NOW = datetime(2026, 8, 16, 12, 0, tzinfo=timezone.utc)
PNG_BYTES = base64.b64decode(
    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="
)


def _movie(movie_id: int = 1, **overrides) -> Movie:
    values = {
        "provider": "tmdb",
        "external_id": str(movie_id),
        "title": f"Movie {movie_id}",
        "original_title": "Original title",
        "year": 2024,
        "overview": "A complete overview.",
        "genres": ("Drama",),
        "runtime_minutes": 120,
        "rating": 8.2,
        "poster_reference": "/posters/movie.jpg",
    }
    values.update(overrides)
    data = MovieCatalogData(**values)
    return Movie(movie_id, data, NOW, NOW, NOW)


def _metadata(movie: Movie, **overrides) -> MovieMetadata:
    values = {
        "provider": movie.provider,
        "external_id": movie.external_id,
        "title": "Provider title that must not replace the catalog title",
        "original_title": "Provider original title",
        "year": 2025,
        "overview": "Provider overview.",
        "genres": ("Science Fiction",),
        "runtime_minutes": 131,
        "rating": 7.4,
        "director": None,
        "cast": (),
        "poster_reference": "/posters/provider.jpg",
    }
    values.update(overrides)
    return MovieMetadata(**values)


@dataclass
class FakeFileCheck:
    value: LibraryReconciliationProgress = LibraryReconciliationProgress(0, 0, 0, 0, 0, 0)

    def execute(self, *, progress=None, cancellation=None):
        if progress:
            progress(self.value)
        return self.value


class FakeMovies:
    def __init__(self, movies: tuple[Movie, ...]) -> None:
        self.movies = movies
        self.page_calls: list[tuple[int, int]] = []
        self.updated: list[tuple[int, MovieCatalogData]] = []

    def count_all(self) -> int:
        return len(self.movies)

    def list_page(self, *, after_id: int, limit: int) -> tuple[Movie, ...]:
        self.page_calls.append((after_id, limit))
        return tuple(movie for movie in self.movies if movie.id > after_id)[:limit]

    def update_metadata(self, movie_id: int, data: MovieCatalogData, *, now: datetime):
        self.updated.append((movie_id, data))
        return next(movie for movie in self.movies if movie.id == movie_id)


class CountMismatchMovies(FakeMovies):
    def count_all(self) -> int:
        return 1

    def list_page(self, *, after_id: int, limit: int) -> tuple[Movie, ...]:
        self.page_calls.append((after_id, limit))
        return ()


class FakeProvider:
    provider_name = "tmdb"

    def __init__(self, metadata: MovieMetadata | None = None, error: Exception | None = None):
        self.metadata = metadata
        self.error = error
        self.calls: list[str] = []

    def get_movie(self, external_id: str) -> MovieMetadata:
        self.calls.append(external_id)
        if self.error:
            raise self.error
        assert self.metadata is not None
        return self.metadata


class FakePosterActions:
    def __init__(self, value) -> None:
        self.value = value
        self.calls = []

    def load_poster(self, request):
        self.calls.append(request)
        return self.value


def _check(movies, provider, *, poster_actions=None, batch_size=50) -> CheckLibrary:
    return CheckLibrary(
        FakeFileCheck(),
        movies,
        provider,
        poster_actions=poster_actions,
        now=lambda: NOW,
        batch_size=batch_size,
    )


def test_complete_movie_never_requests_provider_and_metadata_only_movie_is_checked() -> None:
    movie = _movie()
    movies = FakeMovies((movie,))
    provider = FakeProvider()

    result = _check(movies, provider).execute()

    assert result.metadata_total == 1
    assert result.metadata_complete == 1
    assert result.metadata_issues == 0
    assert result.items == ()
    assert provider.calls == []


def test_empty_page_ends_a_count_mismatch_without_fabricating_metadata_results() -> None:
    movies = CountMismatchMovies(())

    result = _check(movies, FakeProvider()).execute()

    assert result.metadata_total == 1
    assert result.metadata_checked == 0
    assert result.items == ()


def test_health_progress_callback_receives_file_and_metadata_phases() -> None:
    values = []
    movie = _movie(overview=None, year=None)
    movies = FakeMovies((movie,))

    result = _check(movies, FakeProvider(_metadata(movie))).execute(progress=values.append)

    assert values[0].metadata_checked == 0
    assert values[-1] == result


@pytest.mark.parametrize("batch_size", [0, -1, True])
def test_batch_size_is_positive_and_bounded(batch_size) -> None:
    with pytest.raises(ValueError, match="batch_size"):
        CheckLibrary(FakeFileCheck(), FakeMovies(()), FakeProvider(), batch_size=batch_size)


def test_missing_fields_are_repaired_without_replacing_populated_identity_fields() -> None:
    movie = _movie(
        overview=None,
        genres=(),
        runtime_minutes=None,
        poster_reference=None,
        year=None,
    )
    movies = FakeMovies((movie,))
    provider = FakeProvider(_metadata(movie))

    result = _check(movies, provider).execute()

    assert result.metadata_complete == 1
    assert result.metadata_repaired == 1
    assert provider.calls == [movie.external_id]
    updated = movies.updated[0][1]
    assert updated.title == movie.title
    assert updated.original_title == movie.original_title
    assert updated.year == 2025
    assert updated.rating == movie.rating
    assert updated.overview == "Provider overview."
    assert updated.genres == ("Science Fiction",)
    assert updated.runtime_minutes == 131
    assert updated.poster_reference == "/posters/provider.jpg"
    assert set(result.items[0].repaired_fields) == {
        MetadataHealthIssue.OVERVIEW,
        MetadataHealthIssue.RUNTIME,
        MetadataHealthIssue.GENRES,
        MetadataHealthIssue.POSTER,
        MetadataHealthIssue.YEAR,
    }


def test_missing_provider_identity_is_needs_match_and_never_guessed() -> None:
    movie = _movie(provider="manual")
    movies = FakeMovies((movie,))
    provider = FakeProvider()

    result = _check(movies, provider).execute()

    assert result.items[0].status is MetadataHealthStatus.NEEDS_MATCH
    assert result.items[0].issues == (MetadataHealthIssue.NEEDS_MATCH,)
    assert provider.calls == []
    assert movies.updated == []


@pytest.mark.parametrize(
    "error",
    [
        MetadataAuthenticationError("missing token"),
        MetadataUnavailableError("network failure"),
        MetadataRateLimitError("slow down"),
        MetadataResponseError("invalid response"),
        MetadataError("provider failure"),
    ],
)
def test_provider_failures_are_reported_without_partial_metadata_mutation(error: Exception) -> None:
    movie = _movie(overview=None)
    movies = FakeMovies((movie,))
    provider = FakeProvider(error=error)

    result = _check(movies, provider).execute()

    assert result.items[0].status is MetadataHealthStatus.PROVIDER_UNAVAILABLE
    assert result.items[0].provider_error is not None
    assert movies.updated == []


def test_provider_identity_mismatch_is_invalid_response_and_not_an_identity_update() -> None:
    movie = _movie(overview=None)
    movies = FakeMovies((movie,))
    provider = FakeProvider(_metadata(movie, external_id="different"))

    result = _check(movies, provider).execute()

    assert result.items[0].provider_error is not None
    assert result.items[0].status is MetadataHealthStatus.PROVIDER_UNAVAILABLE
    assert movies.updated == []


def test_provider_legitimately_missing_value_is_needs_review_not_provider_failure() -> None:
    movie = _movie(overview=None)
    movies = FakeMovies((movie,))
    provider = FakeProvider(_metadata(movie, overview=None))

    result = _check(movies, provider).execute()

    assert result.items[0].status is MetadataHealthStatus.PROVIDER_VALUE_UNAVAILABLE
    assert result.items[0].provider_error is None
    assert result.items[0].issues == (MetadataHealthIssue.OVERVIEW,)
    assert movies.updated == []


def test_missing_poster_cache_uses_only_poster_service_and_can_recover_safely(
    tmp_path: Path,
) -> None:
    movie = _movie()

    class Source:
        calls = 0

        def fetch(self, request):
            self.calls += 1
            return PosterAsset("png", PNG_BYTES)

    source = Source()
    cache = PosterAssetCache((tmp_path / "poster-cache").absolute())
    poster_service = PosterAssetService(cache, {"tmdb": source})
    movies = FakeMovies((movie,))

    result = _check(movies, FakeProvider(), poster_actions=poster_service).execute()

    assert result.metadata_complete == 1
    assert result.items == ()
    assert source.calls == 1
    assert poster_service.load_poster(
        PosterRequest("tmdb", movie.poster_reference or "/posters/movie.jpg")
    ) is not None
    assert movies.updated == []


def test_poster_cache_failure_is_reported_as_a_safe_missing_poster() -> None:
    movie = _movie()

    class BrokenPosterActions:
        def load_poster(self, request):
            raise OSError("cache unavailable")

    result = _check(
        FakeMovies((movie,)),
        FakeProvider(),
        poster_actions=BrokenPosterActions(),
    ).execute()

    assert result.items[0].status is MetadataHealthStatus.MISSING_POSTER
    assert result.items[0].provider_error is None
    assert result.items[0].issues == (MetadataHealthIssue.POSTER,)


def test_metadata_repair_is_bounded_and_processes_multiple_pages() -> None:
    movies = FakeMovies(tuple(_movie(index) for index in range(1, 8)))
    provider = FakeProvider()

    result = _check(movies, provider, batch_size=2).execute()

    assert result.metadata_checked == 7
    assert len(movies.page_calls) == 4
    assert all(limit == 2 for _after_id, limit in movies.page_calls)
    assert provider.calls == []


def test_issue_list_is_bounded_even_when_library_is_larger() -> None:
    movies = FakeMovies(tuple(_movie(index, provider="manual") for index in range(1, 105)))

    result = _check(movies, FakeProvider()).execute()

    assert result.metadata_checked == 104
    assert len(result.items) == 100


def test_invalid_now_is_rejected_before_metadata_update() -> None:
    movie = _movie(overview=None)
    provider = FakeProvider(_metadata(movie))
    checker = CheckLibrary(
        FakeFileCheck(),
        FakeMovies((movie,)),
        provider,
        now=lambda: datetime(2026, 8, 16),
    )

    with pytest.raises(ValueError, match="timezone-aware"):
        checker.execute()


def test_cancellation_stops_before_metadata_mutation() -> None:
    movies = FakeMovies((_movie(1, overview=None), _movie(2, overview=None)))
    cancellation = ReconciliationCancellation()
    cancellation.cancel()

    with pytest.raises(LibraryReconciliationCancelled) as raised:
        _check(movies, FakeProvider(_metadata(movies.movies[0]))).execute(
            cancellation=cancellation
        )

    assert raised.value.progress.metadata_checked == 0
    assert movies.updated == []
