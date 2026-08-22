from __future__ import annotations

from PySide6.QtCore import QEvent, Qt, Signal
from PySide6.QtWidgets import QGridLayout, QScrollArea, QWidget

from dropsort.application.dto.library import MovieListItem
from dropsort.ui.common.theme import CARD_WIDTH, SPACE_LARGE, SPACE_MEDIUM
from dropsort.ui.library.movie_card import MovieCard
from dropsort.ui.localization import UiLocalizer
from dropsort.ui.posters import PosterRequestDispatcher


class MovieGrid(QScrollArea):
    """Responsive poster grid that preserves card widgets across navigation/filtering.

    Rebuilding every card on each navigation or search keystroke caused a visible
    refresh flash and repeated poster work.  The grid now keeps one card per
    movie id, only replaces a card when its DTO actually changes, and only
    re-lays out when the visible ids or column count changes.
    """

    movie_selected = Signal(int)

    def __init__(
        self,
        *,
        poster_loader: PosterRequestDispatcher | None = None,
        localizer: UiLocalizer | None = None,
        show_local_state: bool = False,
        parent=None,
    ) -> None:
        super().__init__(parent)
        self.setWidgetResizable(True)
        self._container = QWidget()
        self._container.setObjectName("movieGridContainer")
        self.viewport().setObjectName("movieGridViewport")
        self._layout = QGridLayout(self._container)
        self._layout.setContentsMargins(0, 0, 0, SPACE_LARGE)
        self._layout.setHorizontalSpacing(SPACE_MEDIUM)
        self._layout.setVerticalSpacing(SPACE_MEDIUM)
        self._layout.setAlignment(Qt.AlignmentFlag.AlignTop | Qt.AlignmentFlag.AlignLeft)
        self.setWidget(self._container)

        self._cards: list[MovieCard] = []
        self._cards_by_id: dict[int, MovieCard] = {}
        self._source_ids: tuple[int, ...] = ()
        self._visible_ids: tuple[int, ...] = ()
        self._columns = 0
        self._poster_loader = poster_loader
        self._localizer = localizer or UiLocalizer()
        self._show_local_state = show_local_state
        self._localizer.language_changed.connect(self._retranslate_cards)

    @property
    def cards(self) -> tuple[MovieCard, ...]:
        return tuple(self._cards)

    def set_items(
        self,
        items: tuple[MovieListItem, ...],
        *,
        retain_unlisted: bool = False,
    ) -> None:
        """Replace the visible source snapshot while reusing stable cards.

        ``retain_unlisted`` keeps currently-unlisted card widgets cached and
        hidden. Personal Library uses this across its cached tabs so returning
        to a tab does not recreate poster/card widgets. The main Library keeps
        the default authoritative behavior and discards movies no longer in
        its source snapshot.
        """

        items = tuple(items)
        incoming_ids = tuple(item.movie_id for item in items)
        incoming_id_set = set(incoming_ids)
        source_changed = incoming_ids != self._source_ids

        if not retain_unlisted:
            for movie_id in tuple(self._cards_by_id):
                if movie_id not in incoming_id_set:
                    self._discard_card(movie_id)
                    source_changed = True

        for item in items:
            existing = self._cards_by_id.get(item.movie_id)
            if existing is None or existing.item != item:
                if existing is not None:
                    self._discard_card(item.movie_id)
                self._cards_by_id[item.movie_id] = self._create_card(item)
                source_changed = True

        self._source_ids = incoming_ids
        self.show_items(items, force=source_changed)

    def show_items(
        self,
        items: tuple[MovieListItem, ...],
        *,
        force: bool = False,
    ) -> None:
        """Show a filtered/order-specific subset without destroying cached cards."""

        items = tuple(items)
        visible_ids = tuple(item.movie_id for item in items)
        cards: list[MovieCard] = []
        replaced = False

        for item in items:
            card = self._cards_by_id.get(item.movie_id)
            if card is None or card.item != item:
                if card is not None:
                    self._discard_card(item.movie_id)
                card = self._create_card(item)
                self._cards_by_id[item.movie_id] = card
                replaced = True
            cards.append(card)

        if not force and not replaced and visible_ids == self._visible_ids:
            return

        visible_set = set(visible_ids)
        for movie_id, card in self._cards_by_id.items():
            if movie_id not in visible_set:
                card.hide()

        self._cards = cards
        self._visible_ids = visible_ids
        self._relayout(force=True)

    def event(self, event: QEvent) -> bool:
        handled = super().event(event)
        if event.type() == QEvent.Type.Resize:
            self._relayout()
        return handled

    def prepare_for_width(self, available_width: int) -> None:
        """Resolve the grid column count before a hidden page is revealed.

        Hidden QScrollArea viewports can still report their construction-time
        width.  Laying cards out from that transient value and then correcting
        them on the first visible Resize event produces a real one-frame grid
        reflow.  Navigation supplies the already-known content width so the
        first painted frame uses the final column count.
        """

        self._relayout(available_width=max(1, available_width))

    def _retranslate_cards(self, _language) -> None:
        for card in self._cards_by_id.values():
            card.retranslate()

    def _create_card(self, item: MovieListItem) -> MovieCard:
        card = MovieCard(
            item,
            poster_loader=self._poster_loader,
            localizer=self._localizer,
            show_local_state=self._show_local_state,
            parent=self._container,
        )
        card.selected.connect(self.movie_selected.emit)
        return card

    def _discard_card(self, movie_id: int) -> None:
        card = self._cards_by_id.pop(movie_id, None)
        if card is None:
            return
        self._layout.removeWidget(card)
        card.hide()
        card.setParent(None)
        card.deleteLater()

    def _relayout(
        self,
        *,
        available_width: int | None = None,
        force: bool = False,
    ) -> None:
        if not self._cards:
            self._columns = 0
            return

        # Base responsive columns on the scroll area's stable outer width, not
        # the viewport width.  The viewport changes when the vertical scrollbar
        # appears/disappears, which can otherwise toggle a column and visibly
        # shake the grid even though the page/data did not change.  Reserve one
        # scrollbar width consistently so both hidden-page preparation and live
        # resize events use the same geometry model.
        outer_width = self.width() if available_width is None else available_width
        scrollbar_reserve = max(0, self.verticalScrollBar().sizeHint().width())
        width = max(outer_width - scrollbar_reserve, CARD_WIDTH)
        columns = max(1, (width + SPACE_MEDIUM) // (CARD_WIDTH + SPACE_MEDIUM))
        if not force and columns == self._columns:
            return

        # Never empty the live layout as a relayout strategy. Cached/hidden
        # cards keep their last cell, and a visible card is moved only when its
        # target cell actually changed. Returning to an already-laid-out
        # Personal Library tab therefore needs no remove/reinsert pass at all.
        for index, card in enumerate(self._cards):
            target_row = index // columns
            target_column = index % columns
            layout_index = self._layout.indexOf(card)
            positioned = False
            if layout_index >= 0:
                row, column, row_span, column_span = self._layout.getItemPosition(
                    layout_index
                )
                positioned = (
                    row == target_row
                    and column == target_column
                    and row_span == 1
                    and column_span == 1
                )
            if not positioned:
                if layout_index >= 0:
                    self._layout.removeWidget(card)
                self._layout.addWidget(card, target_row, target_column)
            card.show()
        self._columns = columns
