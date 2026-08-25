from __future__ import annotations

import logging

from PySide6.QtCore import Signal, Qt
from PySide6.QtWidgets import (
    QFrame,
    QHBoxLayout,
    QLabel,
    QPushButton,
    QSizePolicy,
    QToolButton,
    QVBoxLayout,
    QWidget,
)

from dropsort.application.dto.library import MovieListItem
from dropsort.application.dto.reconciliation import LibraryReconciliationProgress
from dropsort.application.errors import LibraryQueryError
from dropsort.ui.common.theme import SPACE_4, SPACE_36, SPACE_LARGE
from dropsort.ui.common.icon import FluentIconName, set_fluent_icon
from dropsort.ui.contracts import LibraryUiActions
from dropsort.ui.library.movie_card import MovieCard
from dropsort.ui.library.movie_grid import MovieGrid
from dropsort.ui.library.search import filter_movie_items, movie_search_suggestions
from dropsort.ui.posters import PosterRequestDispatcher
from dropsort.ui.localization import TextId, UiLocalizer


LOGGER = logging.getLogger(__name__)


class LibraryView(QWidget):
    movie_selected = Signal(int)
    check_files_requested = Signal()
    clear_search_requested = Signal()
    search_candidates_changed = Signal(object)

    def __init__(
        self,
        actions: LibraryUiActions,
        *,
        poster_loader: PosterRequestDispatcher | None = None,
        localizer: UiLocalizer | None = None,
        parent=None,
    ) -> None:
        super().__init__(parent)
        self._actions = actions
        self._localizer = localizer or UiLocalizer()
        self._all_items: tuple[MovieListItem, ...] = ()
        self._search_query = ""
        self._has_snapshot = False
        layout = QVBoxLayout(self)
        layout.setContentsMargins(SPACE_36, SPACE_36, SPACE_36, SPACE_36)
        layout.setSpacing(SPACE_LARGE)

        heading_row = QHBoxLayout()
        heading_block = QWidget()
        heading_block_layout = QVBoxLayout(heading_block)
        heading_block_layout.setContentsMargins(0, 0, 0, 0)
        heading_block_layout.setSpacing(SPACE_4)
        self._heading = QLabel()
        self._heading.setObjectName("libraryHeadingLabel")
        self._heading.setProperty("role", "screenHeading")
        self._localizer.bind_text(self._heading, TextId.LIBRARY_HEADING)
        heading_block_layout.addWidget(self._heading)
        self._count = QLabel()
        self._count.setObjectName("libraryCountLabel")
        self._count.setProperty("role", "muted")
        heading_block_layout.addWidget(self._count)
        heading_row.addWidget(heading_block)
        heading_row.addStretch(1)
        self._check_files = QPushButton()
        self._check_files.setObjectName("checkLibraryFilesButton")
        self._check_files.setProperty("role", "secondaryAction")
        set_fluent_icon(self._check_files, FluentIconName.CHECK_LIBRARY)
        self._check_files.clicked.connect(self.check_files_requested)
        heading_row.addWidget(self._check_files)
        self._localizer.bind_text(self._check_files, TextId.CHECK_LIBRARY_FILES)
        self._check_files.hide()
        layout.addLayout(heading_row)

        self._state_host = QFrame()
        self._state_host.setObjectName("libraryStateHost")
        self._state_host.setMaximumWidth(520)
        state_layout = QHBoxLayout(self._state_host)
        state_layout.setContentsMargins(16, 16, 16, 16)
        state_layout.setSpacing(12)
        state_layout.setAlignment(Qt.AlignmentFlag.AlignTop | Qt.AlignmentFlag.AlignLeft)

        state_icon = QToolButton()
        state_icon.setObjectName("libraryStateIcon")
        state_icon.setAutoRaise(True)
        state_icon.setEnabled(False)
        state_icon.setFixedSize(36, 36)
        set_fluent_icon(state_icon, FluentIconName.SEARCH)
        state_layout.addWidget(state_icon, 0, Qt.AlignmentFlag.AlignTop)

        state_copy = QVBoxLayout()
        state_copy.setContentsMargins(0, 0, 0, 0)
        state_copy.setSpacing(SPACE_4)
        self._state = QLabel()
        self._state.setObjectName("libraryStateLabel")
        self._state.setProperty("role", "h4")
        self._state.setWordWrap(True)
        state_copy.addWidget(self._state)
        self._state_helper = QLabel()
        self._state_helper.setObjectName("libraryStateHelperLabel")
        self._state_helper.setProperty("role", "muted")
        self._state_helper.setWordWrap(True)
        state_copy.addWidget(self._state_helper)
        self._clear_search = QPushButton()
        self._clear_search.setObjectName("libraryEmptyClearSearchButton")
        self._clear_search.setProperty("role", "ghostAction")
        self._clear_search.clicked.connect(self.clear_search_requested)
        self._localizer.bind_text(self._clear_search, TextId.LIBRARY_SEARCH_CLEAR)
        state_copy.addWidget(self._clear_search, 0, Qt.AlignmentFlag.AlignLeft)
        state_layout.addLayout(state_copy, 1)
        self._state_host.hide()
        layout.addWidget(self._state_host, 0, Qt.AlignmentFlag.AlignTop | Qt.AlignmentFlag.AlignLeft)

        self._reconciliation = QLabel()
        self._reconciliation.setObjectName("libraryReconciliationStatusLabel")
        self._reconciliation.setProperty("role", "muted")
        # Keep background reconciliation text on one stable line. Progress text
        # changes after startup must repaint only this label, not change its
        # height and push the live movie grid down/up.
        self._reconciliation.setWordWrap(False)
        self._reconciliation.setSizePolicy(
            QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Fixed
        )
        self._reconciliation.hide()
        layout.addWidget(self._reconciliation)

        self._grid = MovieGrid(poster_loader=poster_loader)
        self._grid.movie_selected.connect(self.movie_selected.emit)
        layout.addWidget(self._grid, 1)
        self._localizer.language_changed.connect(lambda _language: self._apply_search())

    def prepare_for_width(self, page_width: int) -> None:
        """Prepare grid columns before this page becomes the stack current page."""

        self._grid.prepare_for_width(max(1, page_width - (SPACE_36 * 2)))

    @property
    def card_count(self) -> int:
        return len(self._grid.cards)

    @property
    def cards(self) -> tuple[MovieCard, ...]:
        return self._grid.cards

    @property
    def reconciliation_message(self) -> str:
        return self._reconciliation.text()

    def show_reconciliation_progress(
        self,
        value: LibraryReconciliationProgress,
    ) -> None:
        self.show_reconciliation_message(
            f"Checking library files: {value.checked} / {value.total} | "
            f"Present: {value.present} | Missing: {value.missing} | Errors: {value.errors}"
        )

    def show_reconciliation_message(self, message: str) -> None:
        self._reconciliation.setText(message)
        self._reconciliation.setVisible(bool(message) and not self._search_query)

    def activate(self) -> None:
        """Show the cached snapshot when returning to Library.

        Navigation used to synchronously query SQLite and recreate every movie
        card on each entry, which produced the visible refresh hitch reported in
        the desktop build.  Explicit refresh paths still call ``show_library``.
        """

        if self._has_snapshot:
            self._apply_search()
            return
        self.show_library()

    def clear_snapshot(self) -> None:
        """Drop every catalog-derived UI snapshot after Clear Library."""

        self._all_items = ()
        self._has_snapshot = False
        self._grid.set_items(())
        self.search_candidates_changed.emit(())
        self._apply_search(render_grid=False)

    def invalidate_snapshot(self) -> None:
        """Mark cached library data stale without repainting the current UI."""

        self._has_snapshot = False

    def show_library(self) -> None:
        try:
            items = self._actions.list_movies()
        except LibraryQueryError:
            LOGGER.warning("Local library query failed", exc_info=True)
            self._has_snapshot = False
            self._all_items = ()
            self._grid.set_items(())
            self._show_state(self._localizer.text(TextId.LIBRARY_LOAD_ERROR))
            return
        self._all_items = tuple(items)
        self._has_snapshot = True
        filtered = filter_movie_items(self._all_items, self._search_query)
        self._grid.set_items(self._all_items, visible_items=filtered)
        self.search_candidates_changed.emit(movie_search_suggestions(self._all_items))
        self._apply_search(render_grid=False)

    def refresh_movies(self, movie_ids: tuple[int, ...]) -> None:
        """Refresh or insert Library items identified by stable MovieId."""

        if not self._has_snapshot:
            return
        items = list(self._all_items)
        indexes = {item.movie_id: index for index, item in enumerate(items)}
        changed = False
        for movie_id in dict.fromkeys(movie_ids):
            index = indexes.get(movie_id)
            try:
                item = self._actions.get_movie_item(movie_id)
            except LibraryQueryError:
                LOGGER.warning(
                    "Local movie summary refresh failed for movie %s",
                    movie_id,
                    exc_info=True,
                )
                self.invalidate_snapshot()
                continue
            if index is None:
                if item.media_file_count > 0:
                    indexes[item.movie_id] = len(items)
                    items.append(item)
                    changed = True
                continue
            if item != items[index]:
                items[index] = item
                changed = True
        if not changed:
            return
        items.sort(key=lambda value: (value.date_added, value.movie_id), reverse=True)
        self._all_items = tuple(items)
        filtered = filter_movie_items(self._all_items, self._search_query)
        self._grid.set_items(self._all_items, visible_items=filtered)
        self.search_candidates_changed.emit(movie_search_suggestions(self._all_items))
        self._apply_search(render_grid=False)

    def set_search_query(self, query: str) -> None:
        normalized = query.strip()
        if normalized == self._search_query:
            return
        self._search_query = normalized
        self._apply_search()

    def clear_search_query(self, *, render: bool = True) -> None:
        """Reset the transient filter, optionally deferring the repaint.

        Navigation away from Library clears the search text, but repainting the
        full poster grid immediately before hiding the page is wasted work and
        was one source of the visible navigation hitch.
        """

        if not self._search_query:
            return
        self._search_query = ""
        if render:
            self._apply_search()

    def search_suggestions(self) -> tuple[str, ...]:
        return movie_search_suggestions(self._all_items)

    def _apply_search(self, *, render_grid: bool = True) -> None:
        items = filter_movie_items(self._all_items, self._search_query)
        self._reconciliation.setVisible(
            bool(self._reconciliation.text()) and not self._search_query
        )
        self._count.setText(
            self._localizer.text(
                TextId.LIBRARY_COUNT_FILTERED,
                count=len(items),
                query=self._search_query,
            )
            if self._search_query
            else self._localizer.text(TextId.LIBRARY_COUNT, count=len(items))
        )
        if render_grid:
            self._grid.show_items(items)
        if items:
            self._state_host.hide()
            self._grid.show()
        elif self._all_items and self._search_query:
            self._show_state(
                self._localizer.text(TextId.LIBRARY_SEARCH_NO_RESULTS),
                helper=self._localizer.text(TextId.LIBRARY_SEARCH_NO_RESULTS_HELPER),
                can_clear_search=True,
            )
        else:
            self._show_state(
                self._localizer.text(TextId.LIBRARY_EMPTY),
                helper=self._localizer.text(TextId.LIBRARY_EMPTY_HELPER),
            )

    def _show_state(
        self,
        message: str,
        *,
        helper: str = "",
        can_clear_search: bool = False,
    ) -> None:
        self._state.setText(message)
        self._state_helper.setText(helper)
        self._state_helper.setVisible(bool(helper))
        self._clear_search.setVisible(can_clear_search)
        self._state_host.show()
        self._grid.hide()
