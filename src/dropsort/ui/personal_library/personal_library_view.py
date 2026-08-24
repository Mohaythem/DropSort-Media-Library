from __future__ import annotations

import logging

from PySide6.QtCore import QSize, Qt, Signal
from PySide6.QtWidgets import QFrame, QHBoxLayout, QLabel, QTabBar, QVBoxLayout, QWidget

from dropsort.application.dto.library import MovieListItem
from dropsort.library.personal import PersonalLibrarySection
from dropsort.ui.common.tasks import QtTaskRunner, TaskRunner
from dropsort.ui.common.icon import FluentIconName, set_fluent_icon
from dropsort.ui.common.theme import SPACE_36, SPACE_MEDIUM, SPACE_SMALL
from dropsort.ui.contracts import PersonalLibraryUiActions
from dropsort.ui.library.movie_card import MovieCard
from dropsort.ui.library.movie_grid import MovieGrid
from dropsort.ui.library.search import filter_movie_items, movie_search_suggestions
from dropsort.ui.localization import TextId, UiLocalizer
from dropsort.ui.posters import PosterRequestDispatcher


LOGGER = logging.getLogger(__name__)


class PersonalLibraryView(QWidget):
    movie_selected = Signal(int)
    search_candidates_changed = Signal(object)

    def __init__(
        self,
        actions: PersonalLibraryUiActions,
        *,
        poster_loader: PosterRequestDispatcher | None = None,
        runner: TaskRunner | None = None,
        localizer: UiLocalizer | None = None,
        parent=None,
    ) -> None:
        super().__init__(parent)
        self._actions = actions
        self._runner = runner or QtTaskRunner(self)
        self._localizer = localizer or UiLocalizer()
        self._poster_loader = poster_loader
        self._all_items: tuple[MovieListItem, ...] = ()
        self._search_query = ""
        self._token = 0
        self._section = PersonalLibrarySection.WATCHLIST
        self._state_error = False
        self._has_snapshot = False
        self._snapshot_stale = False
        self._visible_section: PersonalLibrarySection | None = None
        self._snapshots: dict[
            PersonalLibrarySection, tuple[MovieListItem, ...]
        ] = {}
        self._stale_sections: set[PersonalLibrarySection] = set()
        self._active_request: tuple[int, PersonalLibrarySection] | None = None
        self._latest_generation_by_section: dict[PersonalLibrarySection, int] = {}
        layout = QVBoxLayout(self)
        layout.setContentsMargins(SPACE_36, SPACE_36, SPACE_36, SPACE_36)
        layout.setSpacing(SPACE_MEDIUM)

        heading = QLabel()
        heading.setObjectName("personalLibraryHeadingLabel")
        heading.setProperty("role", "screenHeading")
        self._localizer.bind_text(heading, TextId.PERSONAL_LIBRARY_HEADING)
        layout.addWidget(heading)

        tab_row = QHBoxLayout()
        self._tabs = QTabBar()
        self._tabs.setObjectName("personalLibraryTabs")
        self._tabs.setExpanding(False)
        for text_id in (
            TextId.PERSONAL_TAB_WATCHLIST,
            TextId.PERSONAL_TAB_READY,
            TextId.PERSONAL_TAB_LIKED,
            TextId.PERSONAL_TAB_BLACKLISTED,
        ):
            self._tabs.addTab(self._localizer.text(text_id))
        self._tabs.currentChanged.connect(self._tab_changed)
        tab_row.addWidget(self._tabs)
        tab_row.addStretch(1)
        layout.addLayout(tab_row)

        self._state = QLabel()
        self._state.setObjectName("personalLibraryStateLabel")
        self._state.setWordWrap(True)
        self._state.hide()
        layout.addWidget(self._state)

        self._empty_host = QWidget()
        self._empty_host.setObjectName("personalEmptyStateHost")
        empty_host_layout = QVBoxLayout(self._empty_host)
        empty_host_layout.setContentsMargins(0, SPACE_MEDIUM, 0, 0)
        empty_host_layout.setSpacing(0)
        self._empty_state = QFrame()
        self._empty_state.setObjectName("personalEmptyState")
        self._empty_state.setAccessibleName("Personal Library empty state")
        empty_layout = QVBoxLayout(self._empty_state)
        empty_layout.setContentsMargins(0, SPACE_MEDIUM, 0, 0)
        empty_layout.setSpacing(SPACE_SMALL)
        empty_layout.setAlignment(Qt.AlignmentFlag.AlignTop | Qt.AlignmentFlag.AlignLeft)
        icon = QLabel()
        icon.setObjectName("personalEmptyStateIcon")
        icon.setAlignment(Qt.AlignmentFlag.AlignLeft)
        icon.setAccessibleName("Personal Library")
        set_fluent_icon(icon, FluentIconName.PERSONAL_LIBRARY, size=QSize(24, 24))
        empty_layout.addWidget(icon)
        self._empty_title = QLabel()
        self._empty_title.setObjectName("personalEmptyStateTitle")
        self._empty_title.setProperty("role", "h4")
        self._empty_title.setAlignment(Qt.AlignmentFlag.AlignLeft)
        empty_layout.addWidget(self._empty_title)
        self._empty_description = QLabel()
        self._empty_description.setObjectName("personalEmptyStateDescription")
        self._empty_description.setProperty("role", "muted")
        self._empty_description.setWordWrap(True)
        self._empty_description.setMaximumWidth(520)
        self._empty_description.setAlignment(Qt.AlignmentFlag.AlignLeft)
        empty_layout.addWidget(self._empty_description)
        empty_host_layout.addWidget(
            self._empty_state, 0, Qt.AlignmentFlag.AlignTop | Qt.AlignmentFlag.AlignLeft
        )
        empty_host_layout.addStretch(1)
        self._empty_host.hide()
        layout.addWidget(self._empty_host, 1)

        self._grid = MovieGrid(
            poster_loader=poster_loader,
            localizer=self._localizer,
            show_local_state=True,
        )
        self._grid.movie_selected.connect(self.movie_selected.emit)
        layout.addWidget(self._grid, 1)

        self._localizer.language_changed.connect(self._retranslate)

    def prepare_for_width(self, page_width: int) -> None:
        """Prepare cached card geometry before revealing Personal Library."""

        self._grid.prepare_for_width(max(1, page_width - (SPACE_36 * 2)))

    @property
    def card_count(self) -> int:
        return len(self._grid.cards)

    @property
    def current_section(self) -> PersonalLibrarySection:
        return self._section

    def activate(self) -> None:
        """Return to cached content immediately, then refresh stale data quietly."""

        if self._adopt_cached_section(self._section):
            if self._snapshot_stale:
                self.refresh()
            return
        self.refresh()

    def invalidate_snapshot(self) -> None:
        """Mark every cached personal projection stale without repainting it."""

        self.invalidate_sections(tuple(_SECTIONS))

    def invalidate_sections(
        self, sections: tuple[PersonalLibrarySection, ...]
    ) -> None:
        """Invalidate only projections affected by a personal mutation."""

        self._stale_sections.update(
            section for section in sections if section in self._snapshots
        )
        self._snapshot_stale = self._section in self._stale_sections

    def clear_snapshots_after_catalog_clear(self) -> None:
        """Discard all catalog-derived Personal state after an authoritative clear."""

        self.invalidate_pending()
        self._snapshots.clear()
        self._stale_sections.clear()
        self._all_items = ()
        self._has_snapshot = False
        self._snapshot_stale = False
        self._visible_section = None
        self._grid.set_items((), retain_unlisted=False)
        self._grid.hide()
        self._empty_host.hide()
        self._state.hide()
        self.search_candidates_changed.emit(())

    def _adopt_cached_section(self, section: PersonalLibrarySection) -> bool:
        cached = self._snapshots.get(section)
        if cached is None:
            self._has_snapshot = False
            self._snapshot_stale = False
            return False
        self._all_items = cached
        self._has_snapshot = True
        self._visible_section = section
        self._snapshot_stale = section in self._stale_sections
        filtered = filter_movie_items(cached, self._search_query)
        self._grid.set_items(
            cached,
            retain_unlisted=True,
            visible_items=filtered,
        )
        self._render_filtered_items(filtered)
        return True

    def refresh(self, section: PersonalLibrarySection | None = None) -> None:
        target = section or self._section
        if target is not self._section:
            self._section = target
            self._tabs.blockSignals(True)
            self._tabs.setCurrentIndex(_section_index(target))
            self._tabs.blockSignals(False)

        cached = self._snapshots.get(target)
        if cached is not None:
            self._adopt_cached_section(target)
        elif self._visible_section is not target:
            # Never paint cards owned by one Personal tab underneath another.
            self._all_items = ()
            self._has_snapshot = False
            self._snapshot_stale = False
            self._visible_section = None
            self._grid.set_items((), retain_unlisted=True)
            self._grid.hide()
            self._empty_host.hide()
            self._state_error = False
            self._state.setText(self._localizer.text(TextId.PERSONAL_LOADING))
            self._state.show()

        if self._active_request is not None and self._active_request[1] is target:
            return

        self._token += 1
        generation = self._token
        self._active_request = (generation, target)
        self._latest_generation_by_section[target] = generation
        self._state_error = False
        if cached is not None:
            self._state.hide()
        self._runner.submit(
            generation,
            lambda target=target: self._actions.list_personal_movies(target),
            lambda delivered, value, target=target: self._loaded(
                delivered, value, target
            ),
            lambda delivered, error, target=target: self._failed(
                delivered, error, target
            ),
        )

    def set_search_query(self, query: str) -> None:
        self._search_query = query.strip()
        self._apply_search()

    def search_suggestions(self) -> tuple[str, ...]:
        return movie_search_suggestions(self._all_items)

    def invalidate_pending(self) -> None:
        self._token += 1
        for section in _SECTIONS:
            self._latest_generation_by_section[section] = self._token
        self._active_request = None

    def wait_for_pending_tasks(self) -> None:
        waiter = getattr(self._runner, "wait_for_done", None)
        if callable(waiter):
            waiter()

    def _tab_changed(self, index: int) -> None:
        if not 0 <= index < len(_SECTIONS):
            return
        target = _SECTIONS[index]
        if target is self._section:
            return
        self._section = target
        # Changing tabs transfers visible ownership immediately. A late result
        # may warm its own section cache but cannot paint this target tab.
        self._active_request = None
        if self._adopt_cached_section(target):
            if self._snapshot_stale:
                self.refresh()
            return
        self.refresh()

    def _loaded(
        self,
        token: int,
        value: object,
        section: PersonalLibrarySection | None = None,
    ) -> None:
        section = section or self._section
        if (
            self._latest_generation_by_section.get(section, token) != token
            or not isinstance(value, tuple)
        ):
            return
        items = tuple(item for item in value if isinstance(item, MovieListItem))
        self._snapshots[section] = items
        self._stale_sections.discard(section)
        direct_delivery = (
            section not in self._latest_generation_by_section
            and self._active_request is None
        )
        if (
            not direct_delivery
            and self._active_request != (token, section)
        ) or self._section is not section:
            return
        self._active_request = None
        self._all_items = items
        self._has_snapshot = True
        self._visible_section = section
        self._snapshot_stale = False
        filtered = filter_movie_items(items, self._search_query)
        self._grid.set_items(
            items,
            retain_unlisted=True,
            visible_items=filtered,
        )
        self.search_candidates_changed.emit(movie_search_suggestions(items))
        self._render_filtered_items(filtered)

    def _apply_search(self) -> None:
        items = filter_movie_items(self._all_items, self._search_query)
        self._grid.show_items(items)
        self._render_filtered_items(items)

    def _render_filtered_items(self, items: tuple[MovieListItem, ...]) -> None:
        if items:
            self._empty_host.hide()
            self._grid.show()
            self._state.hide()
        elif self._all_items and self._search_query:
            self._empty_host.hide()
            self._state_error = False
            self._state.setText(self._localizer.text(TextId.LIBRARY_SEARCH_NO_RESULTS))
            self._state.show()
        else:
            self._grid.hide()
            self._state_error = False
            self._state.hide()
            self._render_empty_state()
            self._empty_host.show()

    def _failed(
        self,
        token: int,
        error: BaseException,
        section: PersonalLibrarySection | None = None,
    ) -> None:
        section = section or self._section
        if self._latest_generation_by_section.get(section, token) != token:
            return
        LOGGER.warning(
            "Personal library query failed",
            exc_info=(type(error), error, error.__traceback__),
        )
        if section in self._snapshots:
            self._stale_sections.add(section)
        direct_delivery = (
            section not in self._latest_generation_by_section
            and self._active_request is None
        )
        if (
            not direct_delivery
            and self._active_request != (token, section)
        ) or self._section is not section:
            return
        self._active_request = None
        if section in self._snapshots:
            self._adopt_cached_section(section)
        else:
            self._has_snapshot = False
            self._snapshot_stale = False
            self._all_items = ()
            self._visible_section = None
            self._grid.set_items((), retain_unlisted=True)
            self._grid.hide()
            self._empty_host.hide()
        self._state_error = True
        self._state.setText(self._localizer.text(TextId.PERSONAL_LOAD_ERROR))
        self._state.show()

    def _empty_text(self, section: PersonalLibrarySection) -> str:
        title_id, _description_id = _EMPTY_TEXT[section]
        return self._localizer.text(title_id)

    def _render_empty_state(self) -> None:
        title_id, description_id = _EMPTY_TEXT[self._section]
        self._empty_title.setText(self._localizer.text(title_id))
        self._empty_description.setText(self._localizer.text(description_id))

    def _retranslate(self, _language) -> None:
        for index, text_id in enumerate(
            (
                TextId.PERSONAL_TAB_WATCHLIST,
                TextId.PERSONAL_TAB_READY,
                TextId.PERSONAL_TAB_LIKED,
                TextId.PERSONAL_TAB_BLACKLISTED,
            )
        ):
            self._tabs.setTabText(index, self._localizer.text(text_id))
        self._render_empty_state()
        if self._state.isVisible() and not self._grid.isVisible():
            self._state.setText(
                self._localizer.text(TextId.PERSONAL_LOAD_ERROR)
                if self._state_error
                else self._empty_text(self._section)
            )


_SECTIONS = (
    PersonalLibrarySection.WATCHLIST,
    PersonalLibrarySection.READY_TO_WATCH,
    PersonalLibrarySection.LIKED,
    PersonalLibrarySection.BLACKLISTED,
)
_EMPTY_TEXT = {
    PersonalLibrarySection.WATCHLIST: (
        TextId.PERSONAL_EMPTY_WATCHLIST,
        TextId.PERSONAL_EMPTY_WATCHLIST_DESCRIPTION,
    ),
    PersonalLibrarySection.READY_TO_WATCH: (
        TextId.PERSONAL_EMPTY_READY,
        TextId.PERSONAL_EMPTY_READY_DESCRIPTION,
    ),
    PersonalLibrarySection.LIKED: (
        TextId.PERSONAL_EMPTY_LIKED,
        TextId.PERSONAL_EMPTY_LIKED_DESCRIPTION,
    ),
    PersonalLibrarySection.BLACKLISTED: (
        TextId.PERSONAL_EMPTY_BLACKLISTED,
        TextId.PERSONAL_EMPTY_BLACKLISTED_DESCRIPTION,
    ),
}


def _section_index(section: PersonalLibrarySection) -> int:
    return _SECTIONS.index(section)
