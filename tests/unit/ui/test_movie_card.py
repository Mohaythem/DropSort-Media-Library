from __future__ import annotations

from dataclasses import replace

import pytest

from PySide6.QtCore import QBuffer, QIODevice, Qt, QTimer
from PySide6.QtGui import QColor, QImage
from PySide6.QtTest import QTest
from PySide6.QtWidgets import QApplication, QLabel

from dropsort.ui.library.movie_card import MovieCard
from dropsort.ui.library.movie_grid import MovieGrid
from dropsort.posters import (
    PosterAsset,
    PosterAssetCache,
    PosterAssetService,
    PosterRequest,
)
from dropsort.application.dto.library import MovieMetadataStatus
from dropsort.ui.common.theme import CARD_HEIGHT
from dropsort.library.personal import PersonalLibrarySection
from dropsort.ui.personal_library.personal_library_view import PersonalLibraryView
from dropsort.ui.posters.loader import PosterLoader


class DeferredPosterLoader:
    def __init__(self) -> None:
        self.requests = []

    def request(self, receiver, request, token) -> None:
        self.requests.append((receiver, request, token))


def _label(card: MovieCard, name: str) -> QLabel:
    label = card.findChild(QLabel, name)
    assert label is not None
    return label


def test_movie_card_renders_summary_and_uses_local_placeholder(
    qapp: QApplication,
    movie_item_factory,
) -> None:
    card = MovieCard(movie_item_factory())

    assert _label(card, "movieTitleLabel").text() == "The Dark Knight"
    assert _label(card, "movieYearLabel").text() == "2008"
    assert _label(card, "movieRatingLabel").text() == "8.5 / 10"
    assert _label(card, "movieFileCountLabel").text() == "2 files"
    assert _label(card, "posterPlaceholder").text() == "TDK"


def test_card_shows_subtle_all_missing_indicator(qapp, movie_item_factory) -> None:
    card = MovieCard(movie_item_factory(media_file_count=2, missing_file_count=2))

    label = _label(card, "movieAvailabilityLabel")
    assert label.text() == "Missing file"
    assert label.property("availability") == "MISSING"


def test_movie_card_handles_missing_optional_values(
    qapp: QApplication,
    movie_item_factory,
) -> None:
    card = MovieCard(
        movie_item_factory(year=None, rating=None, poster_reference=None, media_file_count=1)
    )

    assert _label(card, "movieYearLabel").text() == "Year unavailable"
    assert _label(card, "movieRatingLabel").text() == "Not rated"
    assert _label(card, "movieFileCountLabel").text() == "1 file"


def test_movie_card_emits_selected_movie_id(
    qapp: QApplication,
    movie_item_factory,
) -> None:
    card = MovieCard(movie_item_factory(movie_id=42))
    selected: list[int] = []
    card.selected.connect(selected.append)
    card.show()

    QTest.mouseClick(card, Qt.MouseButton.LeftButton)
    qapp.processEvents()

    assert selected == [42]


def test_movie_card_displays_loaded_poster_without_distortion(
    qapp: QApplication,
    movie_item_factory,
    png_bytes: bytes,
) -> None:
    loader = DeferredPosterLoader()
    card = MovieCard(movie_item_factory(), poster_loader=loader)
    receiver, request, token = loader.requests[0]

    receiver.apply_poster(token, PosterAsset("png", png_bytes))

    assert card.poster_loaded is True
    assert _label(card, "posterPlaceholder").pixmap().isNull() is False


def test_movie_card_missing_failed_or_stale_poster_keeps_placeholder(
    qapp: QApplication,
    movie_item_factory,
) -> None:
    loader = DeferredPosterLoader()
    card = MovieCard(movie_item_factory(), poster_loader=loader)
    receiver, _request, token = loader.requests[0]

    receiver.apply_poster(token + 1, None)
    receiver.apply_poster(token, None)

    assert card.poster_loaded is False
    assert _label(card, "posterPlaceholder").text() == "TDK"


def test_movie_card_long_titles_are_elided_into_a_stable_accessible_two_line_area(
    qapp,
    movie_item_factory,
) -> None:
    title = "The Extremely Long Movie Title That Must Remain Readable Across Every Grid Width"
    card = MovieCard(movie_item_factory(title=title))
    card.show()
    qapp.processEvents()

    rendered = _label(card, "movieTitleLabel")
    assert rendered.text().count("\n") <= 1
    assert rendered.text() != title
    assert "…" in rendered.text()
    assert rendered.toolTip() == title
    assert rendered.accessibleName() == title
    assert card.accessibleDescription() == title
    assert card.height() == CARD_HEIGHT


def test_movie_card_extreme_and_arabic_titles_do_not_change_card_geometry(
    qapp,
    movie_item_factory,
) -> None:
    extreme = "X" * 240
    card = MovieCard(movie_item_factory(title=extreme))
    card.show()
    qapp.processEvents()
    assert card.height() == CARD_HEIGHT
    assert card.findChild(QLabel, "movieTitleLabel").text().count("\n") <= 1

    arabic = "فيلم عربي طويل للغاية يحتاج إلى سطرين واضحين في بطاقة الفيلم"
    arabic_card = MovieCard(movie_item_factory(title=arabic))
    arabic_card.show()
    qapp.processEvents()
    arabic_title = _label(arabic_card, "movieTitleLabel")
    assert arabic_card.height() == CARD_HEIGHT
    assert arabic_title.toolTip() == arabic
    assert arabic_title.text().count("\n") <= 1


def test_stable_movie_id_updates_card_in_place_without_repeating_same_poster(
    qapp, movie_item_factory
) -> None:
    loader = DeferredPosterLoader()
    item = movie_item_factory(movie_id=7)
    grid = MovieGrid(poster_loader=loader)
    grid.set_items((item,))
    card = grid.cards[0]
    assert len(loader.requests) == 1

    updated = replace(
        item,
        title="Updated title",
        year=2025,
        rating=7.2,
        missing_file_count=1,
    )
    grid.set_items((updated,))

    assert grid.cards[0] is card
    assert card.item.movie_id == 7
    assert _label(card, "movieTitleLabel").toolTip() == "Updated title"
    assert _label(card, "movieYearLabel").text() == "2025"
    assert _label(card, "movieCompactRatingValue").text() == "7.2"
    assert _label(card, "movieAvailabilityLabel").property("availability") == "PARTIAL"
    assert len(loader.requests) == 1

    poster_changed = replace(updated, poster_reference="/new-poster.jpg")
    grid.set_items((poster_changed,))
    assert grid.cards[0] is card
    assert len(loader.requests) == 2


def test_movie_grid_creates_only_new_ids_and_removes_only_missing_ids(
    qapp, movie_item_factory
) -> None:
    first = movie_item_factory(movie_id=1)
    second = movie_item_factory(movie_id=2, title="Second")
    grid = MovieGrid()
    grid.set_items((first,))
    first_card = grid.cards[0]

    grid.set_items((first, second))
    assert grid.cards[0] is first_card
    assert len(grid.cards) == 2
    second_card = grid.cards[1]

    grid.set_items((second,))
    assert grid.cards == (second_card,)
    assert 1 not in grid._cards_by_id


def test_registration_then_enrichment_updates_same_movie_card(
    qapp, movie_item_factory
) -> None:
    local = movie_item_factory(
        movie_id=42,
        provider=None,
        poster_reference=None,
        rating=None,
        title="Local title",
        metadata_status=MovieMetadataStatus.PENDING,
    )
    enriched = replace(
        local,
        provider="tmdb",
        poster_reference="/enriched.jpg",
        rating=8.1,
        title="Enriched title",
        metadata_status=MovieMetadataStatus.READY,
    )
    loader = DeferredPosterLoader()
    grid = MovieGrid(poster_loader=loader)

    grid.set_items((local,))
    card = grid.cards[0]
    grid.set_items((enriched,))

    assert grid.cards[0] is card
    assert grid.cards[0].item.movie_id == 42
    assert _label(card, "movieTitleLabel").toolTip() == "Enriched title"
    assert len(loader.requests) == 1


class CountingMovieGrid(MovieGrid):
    def __init__(self, **kwargs) -> None:
        self.set_items_calls = 0
        super().__init__(**kwargs)

    def set_items(self, *args, **kwargs) -> None:
        self.set_items_calls += 1
        super().set_items(*args, **kwargs)


def _solid_poster(color: str) -> PosterAsset:
    image = QImage(8, 12, QImage.Format.Format_RGB32)
    image.fill(QColor(color))
    buffer = QBuffer()
    assert buffer.open(QIODevice.OpenModeFlag.WriteOnly)
    assert image.save(buffer, "PNG")
    return PosterAsset("png", bytes(buffer.data()))


def _assert_poster_color(card: MovieCard, color: str) -> None:
    pixmap = _label(card, "posterPlaceholder").pixmap()
    assert pixmap is not None and not pixmap.isNull()
    actual = pixmap.toImage().pixelColor(pixmap.width() // 2, pixmap.height() // 2)
    assert actual.name() == QColor(color).name()


@pytest.mark.parametrize("card_count", (1, 3, 5))
def test_visible_poster_results_are_presented_in_one_bounded_batch(
    qapp,
    movie_item_factory,
    card_count: int,
) -> None:
    colors = ("red", "green", "blue", "yellow", "magenta")
    items = tuple(
        movie_item_factory(
            movie_id=index + 1,
            title=f"Movie {index + 1}",
            poster_reference=f"/poster-{index + 1}.png",
        )
        for index in range(card_count)
    )
    loader = DeferredPosterLoader()
    grid = CountingMovieGrid(poster_loader=loader)
    grid.set_items(items)
    original_cards = grid.cards

    deliveries = [
        (receiver, token, _solid_poster(colors[index]))
        for index, (receiver, _request, token) in enumerate(loader.requests)
    ]
    receiver, token, asset = deliveries[0]
    receiver.apply_poster(token, asset)
    for index, (receiver, token, asset) in enumerate(deliveries[1:], start=1):
        QTimer.singleShot(
            index * 10,
            lambda receiver=receiver, token=token, asset=asset: receiver.apply_poster(
                token, asset
            ),
        )
    QTest.qWait(80)
    qapp.processEvents()

    assert grid.cards == original_cards
    assert grid.set_items_calls == 1
    assert grid.poster_presentation_count == 1
    assert all(card.poster_loaded for card in grid.cards)
    for card, color in zip(grid.cards, colors[:card_count], strict=True):
        _assert_poster_color(card, color)


def test_cached_posters_use_the_same_coalesced_presentation_path(
    qapp,
    movie_item_factory,
    tmp_path,
) -> None:
    colors = ("red", "green", "blue")
    items = tuple(
        movie_item_factory(
            movie_id=index + 1,
            title=f"Cached {index + 1}",
            poster_reference=f"/cached-{index + 1}.png",
        )
        for index in range(3)
    )
    cache = PosterAssetCache(tmp_path / "poster-cache")
    for item, color in zip(items, colors, strict=True):
        cache.put(
            PosterRequest(item.provider or "", item.poster_reference or ""),
            _solid_poster(color),
        )
    service = PosterAssetService(cache, {})
    loader = PosterLoader(service)
    grid = MovieGrid(poster_loader=loader)
    grid.set_items(items)
    original_cards = grid.cards

    for _ in range(200):
        qapp.processEvents()
        if loader.active_request_count == 0:
            break
        QTest.qWait(10)

    assert loader.active_request_count == 0
    assert grid.cards == original_cards
    assert 1 <= grid.poster_presentation_count < len(grid.cards)
    # Native cache workers may straddle the 100 ms bound, but never regress
    # to one presentation cycle per visible card.
    assert all(card.poster_loaded for card in grid.cards)
    for card, color in zip(grid.cards, colors, strict=True):
        _assert_poster_color(card, color)
    loader.shutdown()


class _ImmediatePersonalRunner:
    def submit(self, token, task, on_success, on_failure) -> None:
        try:
            on_success(token, task())
        except BaseException as error:
            on_failure(token, error)

    def wait_for_done(self) -> None:
        return None


class _PersonalPosterActions:
    def __init__(self, items) -> None:
        self._items = items

    def list_personal_movies(self, section: PersonalLibrarySection):
        return self._items


def test_personal_library_coalesces_multiple_poster_results_without_rebuild(
    qapp,
    movie_item_factory,
    monkeypatch,
) -> None:
    colors = ("red", "green", "blue")
    items = tuple(
        movie_item_factory(
            movie_id=index + 1,
            title=f"Personal {index + 1}",
            poster_reference=f"/personal-{index + 1}.png",
            media_file_count=0,
        )
        for index in range(3)
    )
    loader = DeferredPosterLoader()
    view = PersonalLibraryView(
        _PersonalPosterActions(items),
        poster_loader=loader,
        runner=_ImmediatePersonalRunner(),
    )
    view.activate()
    original_cards = view._grid.cards
    set_items_calls = 0
    original_set_items = view._grid.set_items

    def counted_set_items(*args, **kwargs):
        nonlocal set_items_calls
        set_items_calls += 1
        return original_set_items(*args, **kwargs)

    monkeypatch.setattr(view._grid, "set_items", counted_set_items)
    deliveries = [
        (receiver, token, _solid_poster(colors[index]))
        for index, (receiver, _request, token) in enumerate(loader.requests)
    ]
    for receiver, token, asset in reversed(deliveries):
        receiver.apply_poster(token, asset)

    assert view._grid.cards == original_cards
    assert set_items_calls == 0
    assert view._grid.poster_presentation_count == 1
    assert all(card.poster_loaded for card in view._grid.cards)
    for card, color in zip(view._grid.cards, colors, strict=True):
        _assert_poster_color(card, color)
