from __future__ import annotations

from PySide6.QtCore import Qt
from PySide6.QtTest import QTest
from PySide6.QtWidgets import QApplication, QLabel

from dropsort.ui.library.movie_card import MovieCard
from dropsort.posters import PosterAsset
from dropsort.ui.common.theme import CARD_HEIGHT


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
