from __future__ import annotations

from dataclasses import dataclass, field

from PySide6.QtCore import Qt
from PySide6.QtWidgets import QApplication, QLabel, QPushButton, QWidget

from dropsort.application.dto.library import MovieDetails, MovieListItem
from dropsort.application.errors import LibraryQueryError
from dropsort.ui.library.library_view import LibraryView
from dropsort.ui.common.theme import CARD_HEIGHT, CARD_WIDTH
from dropsort.application.configuration.localization import UiLanguage
from dropsort.ui.localization import TextId, UiLocalizer


@dataclass
class FakeLibraryActions:
    movies: tuple[MovieListItem, ...] = ()
    details: MovieDetails | None = None
    error: Exception | None = None
    calls: list[str] = field(default_factory=list)

    def list_movies(self) -> tuple[MovieListItem, ...]:
        self.calls.append("library")
        if self.error:
            raise self.error
        return self.movies

    def get_movie_details(self, movie_id: int) -> MovieDetails:
        self.calls.append(f"details:{movie_id}")
        if self.error:
            raise self.error
        assert self.details is not None
        return self.details


def _text(view: LibraryView, object_name: str) -> str:
    label = view.findChild(QLabel, object_name)
    assert label is not None
    return label.text()


def test_library_view_has_clear_empty_state(qapp: QApplication) -> None:
    actions = FakeLibraryActions()
    view = LibraryView(actions)

    view.show_library()

    assert actions.calls == ["library"]
    assert view.card_count == 0
    assert _text(view, "libraryHeadingLabel") == "Library"
    assert _text(view, "libraryCountLabel") == "0 movies"
    assert _text(view, "libraryStateLabel") == "Your movie library is empty."
    host = view.findChild(QWidget, "libraryStateHost")
    cta = view.findChild(QPushButton, "libraryEmptyAddMoviesButton")
    assert host is not None and host.maximumWidth() == 16777215
    assert cta is not None and not cta.isHidden()


def test_library_empty_state_cta_and_copy_retranslate(qapp: QApplication) -> None:
    localizer = UiLocalizer()
    view = LibraryView(FakeLibraryActions(), localizer=localizer)
    requested: list[bool] = []
    view.add_movies_requested.connect(lambda: requested.append(True))
    view.show_library()

    cta = view.findChild(QPushButton, "libraryEmptyAddMoviesButton")
    helper = view.findChild(QLabel, "libraryStateHelperLabel")
    assert cta is not None and helper is not None
    cta.click()
    assert requested == [True]

    localizer.set_language(UiLanguage.ARABIC)
    assert view.layoutDirection() is Qt.LayoutDirection.RightToLeft
    assert cta.text() == localizer.text(TextId.NAV_ADD_MOVIES)
    assert helper.text() == localizer.text(TextId.LIBRARY_EMPTY_HELPER)
    assert _text(view, "libraryCountLabel") == localizer.text(
        TextId.LIBRARY_COUNT, count=0
    )
    localizer.set_language(UiLanguage.ENGLISH)


def test_library_view_renders_multiple_cards(
    qapp: QApplication,
    movie_item_factory,
) -> None:
    actions = FakeLibraryActions(
        movies=(movie_item_factory(), movie_item_factory(movie_id=2, title="Arrival")),
    )
    view = LibraryView(actions)

    view.show_library()
    assert view.card_count == 2

    assert actions.calls == ["library"]


def test_library_view_translates_controlled_query_failure_to_friendly_state(
    qapp: QApplication,
) -> None:
    view = LibraryView(FakeLibraryActions(error=LibraryQueryError("database details")))

    view.show_library()

    assert view.card_count == 0
    assert _text(view, "libraryStateLabel") == (
        "DropSort could not load the local library. Please try again."
    )


def test_library_cards_request_posters_through_shared_loader(
    qapp: QApplication,
    movie_item_factory,
) -> None:
    class Loader:
        def __init__(self) -> None:
            self.requests = []

        def request(self, receiver, request, token) -> None:
            self.requests.append((receiver, request, token))

    loader = Loader()
    item = movie_item_factory(movie_id=7, poster_reference="/poster.jpg")
    view = LibraryView(FakeLibraryActions(movies=(item,)), poster_loader=loader)

    view.show_library()

    assert len(loader.requests) == 1
    assert loader.requests[0][1].provider == "tmdb"
    assert loader.requests[0][1].reference == "/poster.jpg"



def test_library_activation_reuses_snapshot_cards_and_poster_requests(
    qapp: QApplication,
    movie_item_factory,
) -> None:
    class Loader:
        def __init__(self) -> None:
            self.requests = []

        def request(self, receiver, request, token) -> None:
            self.requests.append((receiver, request, token))

    loader = Loader()
    item = movie_item_factory(movie_id=9, poster_reference="/poster.jpg")
    actions = FakeLibraryActions(movies=(item,))
    view = LibraryView(actions, poster_loader=loader)

    view.show_library()
    first_card = view.cards[0]
    view.activate()

    assert actions.calls == ["library"]
    assert view.cards[0] is first_card
    assert len(loader.requests) == 1


def test_library_search_reuses_existing_movie_cards(
    qapp: QApplication,
    movie_item_factory,
) -> None:
    wind = movie_item_factory(movie_id=1, title="The Wind Rises")
    arrival = movie_item_factory(movie_id=2, title="Arrival")
    view = LibraryView(FakeLibraryActions(movies=(wind, arrival)))
    view.show_library()
    cards = {card.item.movie_id: card for card in view.cards}

    view.set_search_query("Wind")
    assert view.card_count == 1
    assert view.cards[0] is cards[1]

    view.set_search_query("")
    assert view.card_count == 2
    assert {card.item.movie_id: card for card in view.cards} == cards


def test_library_search_no_results_is_compact_actionable_and_hides_background_status(
    qapp: QApplication,
    movie_item_factory,
) -> None:
    view = LibraryView(
        FakeLibraryActions(movies=(movie_item_factory(title="Arrival"),))
    )
    view.show_library()
    view.show_reconciliation_message(
        "Library file check complete: Present: 1 | Missing: 0 | Errors: 0"
    )

    view.set_search_query("does-not-exist")

    state_host = view.findChild(QWidget, "libraryStateHost")
    helper = view.findChild(QLabel, "libraryStateHelperLabel")
    clear = view.findChild(QPushButton, "libraryEmptyClearSearchButton")
    assert state_host is not None and not state_host.isHidden()
    assert state_host.maximumWidth() == 16777215
    assert _text(view, "libraryStateLabel") == "No movies found"
    assert helper is not None and "clear" in helper.text().casefold()
    assert clear is not None and not clear.isHidden()
    assert view._reconciliation.isHidden()

    view.clear_search_query()
    assert state_host.isHidden()
    assert not view._reconciliation.isHidden()

def test_library_grid_keeps_movie_cards_stable_at_practical_viewport_widths(
    qapp: QApplication,
    movie_item_factory,
) -> None:
    view = LibraryView(
        FakeLibraryActions(
            movies=tuple(
                movie_item_factory(movie_id=index, title=f"Long title {index} " + "word " * 12)
                for index in range(1, 5)
            )
        )
    )
    view.show_library()
    view.show()
    for width in (360, 640, 1024):
        view.resize(width, 720)
        qapp.processEvents()
        assert all(card.width() == CARD_WIDTH for card in view.cards)
        assert all(card.height() == CARD_HEIGHT for card in view.cards)
        assert view._grid.horizontalScrollBar().maximum() == 0
