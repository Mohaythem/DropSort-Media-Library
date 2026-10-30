from __future__ import annotations

from datetime import datetime, timezone

from dropsort.application.dto.library import MovieDetails, MovieListItem
from dropsort.ui.library.movie_card import MovieCard
from dropsort.ui.movie_details.details_view import MovieDetailsView
from dropsort.application.dto.library import MovieMetadataStatus


NOW = datetime(2026, 8, 24, 12, 0, tzinfo=timezone.utc)


class PosterLoader:
    def __init__(self) -> None:
        self.requests = []

    def request(self, receiver, request, token) -> None:
        self.requests.append((receiver, request, token))


def test_identityless_library_card_uses_placeholder_without_poster_request(qapp) -> None:
    loader = PosterLoader()
    item = MovieListItem(
        movie_id=1,
        provider=None,
        title="Offline Movie",
        original_title=None,
        year=2024,
        rating=None,
        poster_reference=None,
        media_file_count=1,
        date_added=NOW,
        metadata_status=MovieMetadataStatus.PENDING,
    )

    card = MovieCard(item, poster_loader=loader)

    assert card.poster_loaded is False
    assert loader.requests == []


def test_identityless_movie_details_use_placeholder_without_poster_request(qapp) -> None:
    loader = PosterLoader()
    details = MovieDetails(
        movie_id=1,
        provider=None,
        external_id=None,
        title="Offline Movie",
        original_title=None,
        year=2024,
        overview=None,
        genres=(),
        runtime_minutes=None,
        rating=None,
        poster_reference=None,
        date_added=NOW,
        media_files=(),
        metadata_status=MovieMetadataStatus.PENDING,
    )
    view = MovieDetailsView(poster_loader=loader)

    view.set_movie(details)

    assert loader.requests == []
