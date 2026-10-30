from __future__ import annotations

from functools import partial
from pathlib import Path

from datetime import datetime, timezone

from PySide6.QtCore import QDate, QLocale, Qt, Signal
from PySide6.QtGui import QFontMetrics, QImage, QPixmap
from PySide6.QtWidgets import (
    QBoxLayout,
    QFrame,
    QHBoxLayout,
    QLabel,
    QLayout,
    QPushButton,
    QDateEdit,
    QToolButton,
    QScrollArea,
    QSizePolicy,
    QVBoxLayout,
    QWidget,
)

from dropsort.application.dto.library import MediaFileDetails, MovieDetails
from dropsort.application.dto.library import MediaFileAvailability
from dropsort.application.dto.personal_library import PersonalMovieSnapshot
from dropsort.library.personal import (
    PersonalLibrarySection,
    PersonalPreference,
    WatchEvent,
)
from dropsort.library.playback import (
    LocalMediaActionError,
    LocalMediaActions,
    MissingMediaFileError,
)
from dropsort.posters import PosterAsset, PosterRequest
from dropsort.ui.common.formatting import (
    format_date,
    format_file_size,
    format_rating,
    format_runtime,
    format_year,
    title_initials,
)
from dropsort.ui.common.rating import provider_rating_stars, provider_rating_text
from dropsort.ui.common.icon import FluentIconName, set_fluent_icon
from dropsort.ui.common.theme import (
    PREFERENCE_ACTION_WIDTH,
    PREFERENCE_CLEAR_WIDTH,
    SPACE_36,
    SPACE_LARGE,
    SPACE_MEDIUM,
    SPACE_SMALL,
)
from dropsort.ui.common.tasks import QtTaskRunner, TaskRunner
from dropsort.ui.contracts import (
    OrganizationUiActions,
    PersonalLibraryUiActions,
    ReconciliationUiActions,
)
from dropsort.ui.organization import OrganizeFileDialog
from dropsort.ui.reconciliation import RelinkMediaFileDialog
from dropsort.ui.posters import PosterRequestDispatcher
from dropsort.ui.localization import TextId, UiLocalizer


_PERSONAL_PREFERENCE_SECTIONS = (
    PersonalLibrarySection.LIKED,
    PersonalLibrarySection.BLACKLISTED,
)
_PERSONAL_WATCHLIST_SECTIONS = (
    PersonalLibrarySection.WATCHLIST,
    PersonalLibrarySection.READY_TO_WATCH,
)
_PERSONAL_WATCH_EVENT_SECTIONS = (PersonalLibrarySection.READY_TO_WATCH,)


def _personal_sections_for_action(
    action: str | None,
) -> tuple[PersonalLibrarySection, ...]:
    if action in {"like", "blacklist", "clear_preference"}:
        return _PERSONAL_PREFERENCE_SECTIONS
    if action in {"add_watchlist", "remove_watchlist"}:
        return _PERSONAL_WATCHLIST_SECTIONS
    if action in {"record_watch", "record_watch_date", "remove_watch_event"}:
        return _PERSONAL_WATCH_EVENT_SECTIONS
    return tuple(PersonalLibrarySection)


MIN_VALID_WATCH_YEAR = 1901
DETAILS_MAX_WIDTH = 1152
DETAILS_HERO_BREAKPOINT = 720


def _format_personal_date(value: datetime | None) -> str:
    """Format a real watch date without treating sentinel/legacy 1900 values as valid."""

    if value is None or value.year < MIN_VALID_WATCH_YEAR:
        return format_date(None)
    return format_date(value)


def _section_divider(object_name: str) -> QFrame:
    divider = QFrame()
    divider.setObjectName(object_name)
    divider.setProperty("role", "personalDivider")
    divider.setFrameShape(QFrame.Shape.HLine)
    return divider


class ResponsiveDetailsColumns(QWidget):
    """Keep detail cards stable while switching between columns and a stack.

    Changing a QBoxLayout direction avoids removing/re-adding live widgets at
    the breakpoint.  The previous grid reflow could expose a one-frame
    collapse/expand when a hidden page received its final geometry.
    """

    def __init__(self, left: QWidget, right: QWidget, parent=None) -> None:
        super().__init__(parent)
        self._left = left
        self._right = right
        self._left.setMinimumWidth(0)
        self._right.setMinimumWidth(0)
        self._left.setSizePolicy(
            QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Maximum
        )
        self._right.setSizePolicy(
            QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Maximum
        )
        self._columns = 0
        self.setMinimumWidth(0)
        self.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Maximum)
        self._layout = QBoxLayout(QBoxLayout.Direction.LeftToRight, self)
        self._layout.setContentsMargins(0, 0, 0, 0)
        self._layout.setSpacing(SPACE_LARGE)
        self._layout.addWidget(self._left, 1, Qt.AlignmentFlag.AlignTop)
        self._layout.addWidget(self._right, 1, Qt.AlignmentFlag.AlignTop)
        self._reflow()

    def _reflow(self, available_width: int | None = None) -> None:
        width = self.width() if available_width is None else available_width
        columns = 2 if width >= 820 else 1
        direction = (
            QBoxLayout.Direction.LeftToRight
            if columns == 2
            else QBoxLayout.Direction.TopToBottom
        )
        if columns == self._columns and self._layout.direction() == direction:
            return
        self._columns = columns
        self._layout.setDirection(direction)
        self._layout.setStretch(0, 1 if columns == 2 else 0)
        self._layout.setStretch(1, 1 if columns == 2 else 0)

    def prepare_for_width(self, available_width: int) -> None:
        """Resolve responsive direction before the containing page is shown."""

        self._reflow(max(0, available_width))


class ResponsiveDetailsHero(QFrame):
    """Match the Make hero without transient remove/re-add layout flashes."""

    def __init__(self, poster: QWidget, metadata: QWidget, parent=None) -> None:
        super().__init__(parent)
        self._poster = poster
        self._metadata = metadata
        self._columns = 0
        self.setMinimumWidth(0)
        self.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Maximum)
        self._layout = QBoxLayout(QBoxLayout.Direction.LeftToRight, self)
        self._layout.setContentsMargins(0, 0, 0, 0)
        self._layout.setSpacing(SPACE_LARGE)
        self._layout.addWidget(
            self._poster, 0, Qt.AlignmentFlag.AlignTop | Qt.AlignmentFlag.AlignLeft
        )
        self._layout.addWidget(self._metadata, 1, Qt.AlignmentFlag.AlignTop)
        self._reflow()

    def _reflow(self, available_width: int | None = None) -> None:
        width = self.width() if available_width is None else available_width
        columns = 2 if width >= DETAILS_HERO_BREAKPOINT else 1
        direction = (
            QBoxLayout.Direction.LeftToRight
            if columns == 2
            else QBoxLayout.Direction.TopToBottom
        )
        if columns == self._columns and self._layout.direction() == direction:
            return
        self._columns = columns
        self._layout.setDirection(direction)
        self._layout.setStretch(0, 0)
        self._layout.setStretch(1, 1 if columns == 2 else 0)

    def prepare_for_width(self, available_width: int) -> None:
        """Resolve responsive direction before the containing page is shown."""

        self._reflow(max(0, available_width))


class ElidedDetailsLabel(QLabel):
    """A long technical value that never dictates the page's minimum width."""

    def __init__(
        self,
        text: str = "",
        *,
        mode: Qt.TextElideMode = Qt.TextElideMode.ElideRight,
        parent=None,
    ) -> None:
        super().__init__(parent)
        self._source_text = text
        self._elide_mode = mode
        self.setMinimumWidth(0)
        self.setSizePolicy(QSizePolicy.Policy.Ignored, QSizePolicy.Policy.Preferred)
        self._render_elided()

    def source_text(self) -> str:
        return self._source_text

    def set_source_text(self, text: str) -> None:
        self._source_text = text
        self._render_elided()

    def resizeEvent(self, event) -> None:
        super().resizeEvent(event)
        self._render_elided()

    def _render_elided(self) -> None:
        available = max(0, self.contentsRect().width())
        rendered = (
            QFontMetrics(self.font()).elidedText(
                self._source_text, self._elide_mode, available
            )
            if available > 0
            else self._source_text
        )
        QLabel.setText(self, rendered)


class MovieDetailsView(QWidget):
    back_requested = Signal()
    organization_completed = Signal(int)
    relink_completed = Signal(int)
    personal_changed = Signal(int, object)

    def __init__(
        self,
        *,
        poster_loader: PosterRequestDispatcher | None = None,
        local_media_actions: LocalMediaActions | None = None,
        organization_actions: OrganizationUiActions | None = None,
        organization_runner: TaskRunner | None = None,
        reconciliation_actions: ReconciliationUiActions | None = None,
        reconciliation_runner: TaskRunner | None = None,
        personal_actions: PersonalLibraryUiActions | None = None,
        personal_runner: TaskRunner | None = None,
        localizer: UiLocalizer | None = None,
        parent=None,
    ) -> None:
        super().__init__(parent)
        self._poster_loader = poster_loader
        self._localizer = localizer or UiLocalizer()
        self._local_media_actions = local_media_actions
        self._organization_actions = organization_actions
        self._organization_runner = (
            organization_runner or QtTaskRunner()
            if organization_actions is not None
            else None
        )
        self._organization_dialogs: set[OrganizeFileDialog] = set()
        self._reconciliation_actions = reconciliation_actions
        self._reconciliation_runner = reconciliation_runner
        self._relink_dialogs: set[RelinkMediaFileDialog] = set()
        self._personal_actions = personal_actions
        self._personal_runner = personal_runner or QtTaskRunner(self)
        self._personal_token = 0
        self._personal_busy = False
        self._personal_busy_action: str | None = None
        self._personal_busy_event_id: int | None = None
        self._personal_snapshot: PersonalMovieSnapshot | None = None
        self._history_row_widgets: dict[int, tuple[QFrame, QLabel, QPushButton]] = {}
        self._movie_id: int | None = None
        self._poster_token = 0
        self._poster_loaded = False
        self._current_rating: float | None = None
        self._restore_focus_after_personal_action = False
        self.setFocusPolicy(Qt.FocusPolicy.StrongFocus)
        outer = QVBoxLayout(self)
        outer.setContentsMargins(0, 0, 0, 0)
        outer.setSpacing(0)

        back_bar = QFrame()
        back_bar.setObjectName("detailsBackBar")
        back_bar.setFixedHeight(42)
        back_layout = QHBoxLayout(back_bar)
        back_layout.setContentsMargins(SPACE_36, 0, SPACE_36, 0)
        back_layout.setSpacing(0)
        self._back_button = QPushButton()
        self._back_button.setObjectName("detailsBackButton")
        self._back_button.setProperty("role", "ghostAction")
        self._back_button.setMaximumWidth(110)
        set_fluent_icon(self._back_button, FluentIconName.BACK)
        self._back_button.clicked.connect(self.back_requested)
        self._localizer.bind_text(self._back_button, TextId.DETAILS_BACK)
        back_layout.addWidget(self._back_button)
        back_layout.addStretch(1)
        outer.addWidget(back_bar)

        self._state = QLabel()
        self._state.setObjectName("detailsStateLabel")
        self._state.setWordWrap(True)
        self._state.hide()
        outer.addWidget(self._state)

        self._scroll = QScrollArea()
        self._scroll.setObjectName("detailsScrollArea")
        self._scroll.setWidgetResizable(True)
        self._scroll.setHorizontalScrollBarPolicy(
            Qt.ScrollBarPolicy.ScrollBarAlwaysOff
        )
        self._content = QWidget()
        self._content.setObjectName("detailsContent")
        self._content.setMinimumWidth(0)
        content_host_layout = QHBoxLayout(self._content)
        content_host_layout.setContentsMargins(0, 0, 0, 0)
        content_host_layout.setSpacing(0)
        # Only constrain vertical alignment. Horizontal AlignLeft here used to
        # make the max-width body settle at its sizeHint instead of consuming
        # the real desktop width, so responsive children saw a false narrow
        # breakpoint during initial layout.
        content_host_layout.setAlignment(Qt.AlignmentFlag.AlignTop)
        self._body = QWidget()
        self._body.setObjectName("detailsBody")
        self._body.setMinimumWidth(0)
        self._body.setMaximumWidth(DETAILS_MAX_WIDTH)
        self._body.setSizePolicy(
            QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Preferred
        )
        self._content_layout = QVBoxLayout(self._body)
        self._content_layout.setContentsMargins(SPACE_36, SPACE_36, SPACE_36, SPACE_36)
        self._content_layout.setSpacing(SPACE_LARGE)
        self._content_layout.setAlignment(Qt.AlignmentFlag.AlignTop)
        # Let the body expand to the available content width (up to its sane
        # desktop cap). The old aligned, zero-stretch insertion was the source
        # of the narrow/mobile-like Details regression on wide windows.
        content_host_layout.addWidget(self._body, 1)
        self._scroll.setWidget(self._content)
        outer.addWidget(self._scroll, 1)

        self._poster = QLabel()
        self._poster.setObjectName("posterPlaceholder")
        self._poster.setFixedSize(200, 300)
        self._poster.setAlignment(Qt.AlignmentFlag.AlignCenter)

        metadata_host = QWidget()
        metadata_host.setObjectName("detailsMetadataHost")
        metadata_host.setMinimumWidth(0)
        metadata_host.setSizePolicy(
            QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Preferred
        )
        metadata = QVBoxLayout(metadata_host)
        metadata.setContentsMargins(0, 0, 0, 0)
        metadata.setSpacing(SPACE_SMALL)
        metadata.setAlignment(Qt.AlignmentFlag.AlignTop)
        self._title = QLabel()
        self._title.setObjectName("detailsTitleLabel")
        self._title.setProperty("role", "detailsHeading")
        self._title.setWordWrap(True)
        self._title.setTextInteractionFlags(Qt.TextInteractionFlag.TextSelectableByMouse | Qt.TextInteractionFlag.TextSelectableByKeyboard)
        metadata.addWidget(self._title)
        # Compatibility metadata remains queryable for existing UI contracts,
        # while the visible hero uses the tighter Make-style inline metadata row.
        self._meta = QLabel()
        self._meta.setObjectName("detailsMetaLabel")
        self._meta.setProperty("role", "muted")
        self._meta.setWordWrap(True)
        self._meta.setTextInteractionFlags(
            Qt.TextInteractionFlag.TextSelectableByMouse
            | Qt.TextInteractionFlag.TextSelectableByKeyboard
        )
        self._meta.hide()
        metadata.addWidget(self._meta)

        rating_row = QHBoxLayout()
        rating_row.setSpacing(SPACE_SMALL)
        self._hero_meta = QLabel()
        self._hero_meta.setObjectName("detailsHeroMetaLabel")
        self._hero_meta.setProperty("role", "muted")
        self._localizer.mark_ltr(self._hero_meta)
        rating_row.addWidget(self._hero_meta)
        self._hero_rating_separator = QLabel("•")
        self._hero_rating_separator.setProperty("role", "muted")
        rating_row.addWidget(self._hero_rating_separator)
        self._hero_rating_star = QLabel("★")
        self._hero_rating_star.setObjectName("detailsHeroRatingStar")
        rating_row.addWidget(self._hero_rating_star)
        self._hero_rating_value = QLabel()
        self._hero_rating_value.setObjectName("detailsHeroRatingValue")
        self._localizer.mark_ltr(self._hero_rating_value)
        rating_row.addWidget(self._hero_rating_value)
        rating_row.addStretch(1)
        metadata.addLayout(rating_row)

        self._rating_stars = QLabel()
        self._rating_stars.setObjectName("detailsRatingStars")
        self._rating_stars.setAccessibleName(
            self._localizer.text(TextId.ACCESSIBILITY_TMDB_RATING_VISUAL)
        )
        self._rating_stars.hide()
        metadata.addWidget(self._rating_stars)
        self._rating_value = QLabel()
        self._rating_value.setObjectName("detailsRatingValue")
        self._rating_value.setAccessibleName(
            self._localizer.text(TextId.ACCESSIBILITY_TMDB_RATING)
        )
        self._localizer.mark_ltr(self._rating_value)
        self._rating_value.hide()
        metadata.addWidget(self._rating_value)
        self._genres = QLabel()
        self._genres.setObjectName("detailsGenresLabel")
        self._genres.setProperty("role", "muted")
        self._genres.setWordWrap(True)
        self._genres.setTextInteractionFlags(Qt.TextInteractionFlag.TextSelectableByMouse | Qt.TextInteractionFlag.TextSelectableByKeyboard)
        metadata.addWidget(self._genres)
        self._current_genres: tuple[str, ...] = ()
        self._original_title = QLabel()
        self._original_title.setObjectName("detailsOriginalTitleLabel")
        self._original_title.setProperty("role", "muted")
        self._original_title.setWordWrap(True)
        self._original_title.setTextInteractionFlags(Qt.TextInteractionFlag.TextSelectableByMouse | Qt.TextInteractionFlag.TextSelectableByKeyboard)
        metadata.addWidget(self._original_title)
        overview_divider = QFrame()
        overview_divider.setObjectName("detailsOverviewDivider")
        overview_divider.setFrameShape(QFrame.Shape.HLine)
        metadata.addWidget(overview_divider)
        overview_heading = QLabel()
        overview_heading.setProperty("role", "sectionHeading")
        self._localizer.bind_text(overview_heading, TextId.DETAILS_OVERVIEW)
        metadata.addWidget(overview_heading)
        self._overview = QLabel()
        self._overview.setObjectName("detailsOverviewLabel")
        self._overview.setWordWrap(True)
        self._overview.setTextInteractionFlags(Qt.TextInteractionFlag.TextSelectableByMouse | Qt.TextInteractionFlag.TextSelectableByKeyboard)
        metadata.addWidget(self._overview)
        metadata.addStretch(1)
        self._hero = ResponsiveDetailsHero(self._poster, metadata_host)
        self._hero.setObjectName("detailsHero")
        self._content_layout.addWidget(self._hero)

        self._personal_panel = self._build_personal_panel()
        files_panel = QFrame()
        files_panel.setObjectName("mediaFilesPanel")
        files_panel.setProperty("role", "panel")
        files_layout = QVBoxLayout(files_panel)
        files_layout.setContentsMargins(
            SPACE_MEDIUM, SPACE_MEDIUM, SPACE_MEDIUM, SPACE_MEDIUM
        )
        files_heading = QLabel()
        files_heading.setProperty("role", "sectionHeading")
        self._localizer.bind_text(files_heading, TextId.DETAILS_MEDIA_FILES)
        files_layout.addWidget(files_heading)
        self._files = QVBoxLayout()
        self._files.setSpacing(SPACE_LARGE)
        files_layout.addLayout(self._files)
        self._details_columns = ResponsiveDetailsColumns(
            self._personal_panel, files_panel
        )
        self._content_layout.addWidget(self._details_columns)
        self._media_file_count = 0
        self._media_file_panels: dict[int, QFrame] = {}
        self._localizer.language_changed.connect(self._refresh_rating_text)
        self._localizer.language_changed.connect(self._refresh_genres_text)
        self._localizer.language_changed.connect(self._refresh_watch_date_accessibility)

    @property
    def media_file_count(self) -> int:
        return self._media_file_count

    @property
    def state_message(self) -> str:
        return self._state.text()

    @property
    def poster_loaded(self) -> bool:
        return self._poster_loaded

    def prepare_for_width(self, viewport_width: int) -> None:
        """Prepare responsive layout while Details is still hidden.

        Navigation must not depend on showEvent to correct an initially narrow
        size hint.  The shell's stack width is already stable before the page
        switch, so resolve both responsive containers from that width first.
        """

        body_width = min(DETAILS_MAX_WIDTH, max(0, viewport_width))
        content_width = max(0, body_width - (SPACE_36 * 2))
        self._hero.prepare_for_width(content_width)
        self._details_columns.prepare_for_width(content_width)

    def resizeEvent(self, event) -> None:
        # Drive responsive direction from the stable outer Details page width,
        # not from child widths that can change when a vertical scrollbar
        # appears. MainWindow already prepares this exact width before the page
        # switch, so the first visible resize is a no-op; genuine window resizes
        # still re-evaluate the breakpoint.
        self.prepare_for_width(event.size().width())
        super().resizeEvent(event)

    def take_stable_focus(self) -> None:
        """Keep keyboard focus inside Details instead of falling back to Search."""

        self.setFocus(Qt.FocusReason.OtherFocusReason)

    def _build_personal_panel(self) -> QFrame:
        panel = QFrame()
        panel.setObjectName("personalStatePanel")
        panel.setProperty("role", "panel")
        layout = QVBoxLayout(panel)
        layout.setContentsMargins(SPACE_MEDIUM, SPACE_MEDIUM, SPACE_MEDIUM, SPACE_MEDIUM)
        layout.setSpacing(SPACE_MEDIUM)

        heading = QLabel()
        heading.setProperty("role", "sectionHeading")
        self._localizer.bind_text(heading, TextId.DETAILS_YOUR_LIBRARY)
        layout.addWidget(heading)

        preference_group = QFrame()
        preference_group.setObjectName("personalPreferenceGroup")
        preference_group.setProperty("role", "personalSection")
        preference_layout = QVBoxLayout(preference_group)
        preference_layout.setContentsMargins(0, 0, 0, 0)
        preference_layout.setSpacing(SPACE_SMALL)
        preference_heading = QLabel()
        preference_heading.setObjectName("personalGroupHeading")
        self._localizer.bind_text(preference_heading, TextId.DETAILS_PREFERENCE_GROUP)
        preference_layout.addWidget(preference_heading)
        preference_row = QHBoxLayout()
        preference_row.setSpacing(SPACE_SMALL)
        self._like_button = QPushButton()
        self._like_button.setObjectName("personalLikeButton")
        self._like_button.setProperty("role", "preferenceAction")
        set_fluent_icon(self._like_button, FluentIconName.LIKE)
        self._like_button.setCheckable(True)
        self._like_button.setFixedWidth(PREFERENCE_ACTION_WIDTH)
        self._localizer.bind_text(self._like_button, TextId.DETAILS_LIKE)
        self._like_button.clicked.connect(
            lambda: self._run_personal_action("like")
        )
        preference_row.addWidget(self._like_button)
        self._blacklist_button = QPushButton()
        self._blacklist_button.setObjectName("personalBlacklistButton")
        self._blacklist_button.setProperty("role", "preferenceAction")
        set_fluent_icon(self._blacklist_button, FluentIconName.BLACKLIST)
        self._blacklist_button.setCheckable(True)
        self._blacklist_button.setFixedWidth(PREFERENCE_ACTION_WIDTH)
        self._localizer.bind_text(self._blacklist_button, TextId.DETAILS_BLACKLIST)
        self._blacklist_button.clicked.connect(
            lambda: self._run_personal_action("blacklist")
        )
        preference_row.addWidget(self._blacklist_button)
        self._clear_preference_button = QPushButton()
        self._clear_preference_button.setObjectName("personalClearPreferenceButton")
        self._clear_preference_button.setProperty("role", "ghostAction")
        self._clear_preference_button.setFixedWidth(PREFERENCE_CLEAR_WIDTH)
        self._localizer.bind_text(
            self._clear_preference_button, TextId.DETAILS_CLEAR_PREFERENCE
        )
        self._clear_preference_button.clicked.connect(
            lambda: self._run_personal_action("clear_preference")
        )
        preference_row.addWidget(self._clear_preference_button)
        preference_row.addStretch(1)
        preference_layout.addLayout(preference_row)
        layout.addWidget(preference_group)
        layout.addWidget(_section_divider("preferenceDivider"))

        watchlist_group = QFrame()
        watchlist_group.setObjectName("personalWatchlistGroup")
        watchlist_group.setProperty("role", "personalSection")
        watchlist_layout = QVBoxLayout(watchlist_group)
        watchlist_layout.setContentsMargins(0, 0, 0, 0)
        watchlist_layout.setSpacing(SPACE_SMALL)
        watchlist_heading = QLabel()
        watchlist_heading.setObjectName("personalGroupHeading")
        self._localizer.bind_text(watchlist_heading, TextId.DETAILS_WATCHLIST_GROUP)
        watchlist_layout.addWidget(watchlist_heading)
        watchlist_row = QHBoxLayout()
        watchlist_row.setSpacing(SPACE_SMALL)
        self._watchlist_button = QPushButton()
        self._watchlist_button.setObjectName("personalWatchlistButton")
        self._watchlist_button.setProperty("role", "secondaryAction")
        set_fluent_icon(self._watchlist_button, FluentIconName.WATCHLIST)
        self._watchlist_button.clicked.connect(self._watchlist_clicked)
        watchlist_row.addWidget(self._watchlist_button)
        watchlist_row.addStretch(1)
        watchlist_layout.addLayout(watchlist_row)
        layout.addWidget(watchlist_group)
        layout.addWidget(_section_divider("watchlistDivider"))

        watching_group = QFrame()
        watching_group.setObjectName("personalWatchingGroup")
        watching_group.setProperty("role", "personalSection")
        watching_layout = QVBoxLayout(watching_group)
        watching_layout.setContentsMargins(0, 0, 0, 0)
        watching_layout.setSpacing(SPACE_SMALL)
        watching_heading = QLabel()
        watching_heading.setObjectName("personalGroupHeading")
        self._localizer.bind_text(watching_heading, TextId.DETAILS_WATCHING_GROUP)
        watching_layout.addWidget(watching_heading)
        self._mark_watched_button = QPushButton()
        self._mark_watched_button.setObjectName("personalMarkWatchedButton")
        self._mark_watched_button.setProperty("role", "watchAction")
        set_fluent_icon(self._mark_watched_button, FluentIconName.MARK_WATCHED)
        self._localizer.bind_text(self._mark_watched_button, TextId.DETAILS_MARK_WATCHED)
        self._mark_watched_button.clicked.connect(
            lambda: self._run_personal_action("record_watch")
        )
        watched_row = QHBoxLayout()
        watched_row.setSpacing(SPACE_SMALL)
        watched_row.addWidget(self._mark_watched_button)
        watched_row.addStretch(1)
        watching_layout.addLayout(watched_row)

        date_section = QVBoxLayout()
        date_section.setSpacing(SPACE_SMALL)
        date_label = QLabel()
        self._watch_date_label = date_label
        self._localizer.bind_text(date_label, TextId.DETAILS_WATCH_DATE)
        date_section.addWidget(date_label)
        date_row = QHBoxLayout()
        date_row.setSpacing(SPACE_SMALL)
        self._watch_date = QDateEdit()
        self._watch_date.setObjectName("personalWatchDateEdit")
        self._watch_date.setCalendarPopup(True)
        self._watch_date.setDisplayFormat("MMM d, yyyy")
        self._watch_date.setLocale(
            QLocale(QLocale.Language.English, QLocale.Country.UnitedStates)
        )
        self._watch_date.setLayoutDirection(Qt.LayoutDirection.LeftToRight)
        self._watch_date.setProperty("dropsortTechnicalLtr", True)
        self._watch_date.setAccessibleName(self._localizer.text(TextId.DETAILS_WATCH_DATE))
        self._watch_date.setMinimumDate(QDate(MIN_VALID_WATCH_YEAR, 1, 1))
        self._watch_date.setMaximumDate(QDate.currentDate())
        self._watch_date.setDate(QDate.currentDate())
        self._watch_date.dateChanged.connect(self._watch_date_changed)
        self._watch_date_calendar_button = QToolButton()
        self._watch_date_calendar_button.setObjectName(
            "personalWatchDateCalendarButton"
        )
        set_fluent_icon(
            self._watch_date_calendar_button, FluentIconName.DATE_PICKER
        )
        self._watch_date_calendar_button.setAutoRaise(True)
        self._watch_date_calendar_button.setAccessibleName(
            self._localizer.text(TextId.ACCESSIBILITY_WATCHED_DATE_CALENDAR)
        )
        self._watch_date_calendar_button.clicked.connect(self._show_watch_calendar)
        date_row.addWidget(self._watch_date)
        date_row.addWidget(self._watch_date_calendar_button)
        self._mark_watched_date_button = QPushButton()
        self._mark_watched_date_button.setObjectName("personalMarkWatchedDateButton")
        self._mark_watched_date_button.setProperty("role", "secondaryAction")
        set_fluent_icon(self._mark_watched_date_button, FluentIconName.MARK_WATCHED)
        self._localizer.bind_text(
            self._mark_watched_date_button, TextId.DETAILS_MARK_WATCHED_DATE
        )
        self._mark_watched_date_button.clicked.connect(
            lambda: self._run_personal_action("record_watch_date")
        )
        date_row.addWidget(self._mark_watched_date_button)
        date_row.addStretch(1)
        date_section.addLayout(date_row)
        watching_layout.addLayout(date_section)

        self._personal_summary = QLabel()
        self._personal_summary.setObjectName("personalSummaryLabel")
        self._personal_summary.setProperty("role", "muted")
        self._personal_summary.setWordWrap(True)
        watching_layout.addWidget(self._personal_summary)
        self._personal_error = QLabel()
        self._personal_error.setObjectName("personalActionErrorLabel")
        self._personal_error.setProperty("role", "error")
        self._personal_error.setWordWrap(True)
        watching_layout.addWidget(self._personal_error)
        layout.addWidget(watching_group)
        layout.addWidget(_section_divider("watchingDivider"))

        history_group = QFrame()
        history_group.setObjectName("personalHistoryGroup")
        history_group.setProperty("role", "personalSection")
        history_layout = QVBoxLayout(history_group)
        history_layout.setContentsMargins(0, 0, 0, 0)
        history_layout.setSpacing(SPACE_SMALL)
        history_heading = QLabel()
        history_heading.setObjectName("personalGroupHeading")
        self._localizer.bind_text(history_heading, TextId.DETAILS_WATCH_HISTORY)
        history_layout.addWidget(history_heading)
        self._history_rows = QVBoxLayout()
        self._history_rows.setSpacing(0)
        history_layout.addLayout(self._history_rows)
        layout.addWidget(history_group)
        if self._personal_actions is None:
            panel.hide()
        else:
            self._set_personal_controls_enabled(False)
        return panel

    def _refresh_watch_date_accessibility(self, _language: object = None) -> None:
        if not hasattr(self, "_watch_date"):
            return
        label = self._localizer.text(TextId.DETAILS_WATCH_DATE)
        self._watch_date.setAccessibleName(label)
        self._watch_date_calendar_button.setAccessibleName(
            self._localizer.text(TextId.ACCESSIBILITY_WATCHED_DATE_CALENDAR)
        )
        self._rating_stars.setAccessibleName(
            self._localizer.text(TextId.ACCESSIBILITY_TMDB_RATING_VISUAL)
        )
        self._rating_value.setAccessibleName(
            self._localizer.text(TextId.ACCESSIBILITY_TMDB_RATING)
        )

    def _show_watch_calendar(self) -> None:
        selected = self._watch_date.date()
        if not selected.isValid():
            selected = QDate.currentDate()
            self._watch_date.setDate(selected)
        calendar = self._watch_date.calendarWidget()
        calendar.setSelectedDate(selected)
        calendar.setCurrentPage(selected.year(), selected.month())
        self._watch_date.setFocus()
        self._watch_date.showPopup()

    def _set_personal_controls_enabled(self, enabled: bool) -> None:
        active = enabled and self._movie_id is not None
        snapshot = self._personal_snapshot
        has_preference = (
            snapshot is not None
            and snapshot.state.preference is not PersonalPreference.NO_OPINION
        )
        self._like_button.setEnabled(active)
        self._blacklist_button.setEnabled(active)
        self._clear_preference_button.setEnabled(active and has_preference)
        self._watchlist_button.setEnabled(active)
        self._mark_watched_button.setEnabled(active)
        self._watch_date.setEnabled(active)
        self._watch_date_calendar_button.setEnabled(active)
        self._mark_watched_date_button.setEnabled(
            active and self._selected_watch_date() is not None
        )
        for _event_id, (_row, _label, remove) in self._history_row_widgets.items():
            remove.setEnabled(active)

    def _selected_watch_date(self) -> QDate | None:
        date = self._watch_date.date()
        if not date.isValid() or date.year() < MIN_VALID_WATCH_YEAR:
            return None
        return date

    def _watch_date_changed(self, _date: QDate) -> None:
        self._mark_watched_date_button.setEnabled(
            self._movie_id is not None
            and self._personal_busy_action != "record_watch_date"
            and self._selected_watch_date() is not None
        )

    def _load_personal(self, movie_id: int) -> None:
        if self._personal_actions is None:
            return
        self._personal_token += 1
        token = self._personal_token
        self._personal_busy = True
        self._personal_busy_action = "initial_load"
        self._personal_busy_event_id = None
        self._personal_error.clear()
        self._set_personal_controls_enabled(False)
        self._personal_runner.submit(
            token,
            lambda: self._personal_actions.get_personal_snapshot(movie_id),
            self._personal_loaded,
            self._personal_failed,
        )

    def _personal_loaded(self, token: int, value: object) -> None:
        if (
            token != self._personal_token
            or self._movie_id is None
            or not isinstance(value, PersonalMovieSnapshot)
        ):
            return
        self._personal_busy = False
        self._personal_busy_action = None
        self._personal_busy_event_id = None
        self._personal_snapshot = value
        self._render_personal()
        self._set_personal_controls_enabled(True)

    def _personal_failed(self, token: int, error: BaseException) -> None:
        if token != self._personal_token:
            return
        action = self._personal_busy_action
        event_id = self._personal_busy_event_id
        self._personal_busy = False
        self._personal_busy_action = None
        self._personal_busy_event_id = None
        if action is not None:
            self._set_personal_action_busy(action, False, event_id=event_id)
        if self._personal_snapshot is not None:
            # Mutation failure: repaint only the authoritative subsection that
            # may have been optimistically toggled and keep unrelated controls
            # stable/interactive.
            self._render_personal(action=action)
            self._set_personal_controls_enabled(True)
            action_button = self._button_for_personal_action(action or "")
            if action_button is not None and action in {
                "like",
                "blacklist",
                "clear_preference",
            }:
                action_button.setFocus(Qt.FocusReason.OtherFocusReason)
        else:
            self._set_personal_controls_enabled(False)
        if event_id is not None:
            row = self._history_row_widgets.get(event_id)
            if row is not None:
                row[2].setEnabled(self._personal_snapshot is not None)
        self._personal_error.setText(
            self._localizer.text(TextId.DETAILS_PERSONAL_LOAD_ERROR)
        )
        if self._restore_focus_after_personal_action:
            self._restore_focus_after_personal_action = False
            self.take_stable_focus()

    def _button_for_personal_action(self, action: str) -> QPushButton | None:
        if action == "like":
            return self._like_button
        if action == "blacklist":
            return self._blacklist_button
        if action == "clear_preference":
            return self._clear_preference_button
        if action in {"add_watchlist", "remove_watchlist"}:
            return self._watchlist_button
        if action == "record_watch":
            return self._mark_watched_button
        if action == "record_watch_date":
            return self._mark_watched_date_button
        return None

    def _set_personal_action_busy(
        self, action: str, busy: bool, *, event_id: int | None = None
    ) -> None:
        """Expose temporary operation state only on the affected control."""

        button = self._button_for_personal_action(action)
        if button is None and event_id is not None:
            row = self._history_row_widgets.get(event_id)
            button = row[2] if row is not None else None
        if button is None:
            return
        button.setProperty("busy", busy)
        if busy and action in {"like", "blacklist", "clear_preference"}:
            self.take_stable_focus()
        button.setEnabled(not busy)
        # setEnabled() already schedules the correct native/QSS state repaint.
        # Do not force a synchronous style reset for one transient operation;
        # that can synchronously invalidate geometry while Details is visible.

    def _run_personal_action(self, action: str) -> None:
        if self._personal_actions is None or self._movie_id is None:
            return
        if self._personal_busy:
            # Checkable controls toggle before clicked is emitted. Restore only
            # the relevant authoritative state if a second mutation is blocked.
            self._render_personal(action=action)
            return
        movie_id = self._movie_id
        if action == "record_watch_date" and self._selected_watch_date() is None:
            return
        self._personal_busy = True
        self._personal_busy_action = action
        self._personal_busy_event_id = None
        self._personal_error.clear()
        action_button = self._button_for_personal_action(action)
        if action_button is not None and action in {
            "like",
            "blacklist",
            "clear_preference",
        }:
            action_button.setFocus(Qt.FocusReason.MouseFocusReason)
        # Revert a checkable button's immediate Qt toggle to the authoritative
        # snapshot, then show busy state on that control only.
        self._render_personal(action=action)
        self._set_personal_action_busy(action, True)
        self._personal_token += 1
        token = self._personal_token
        if action == "like":
            task = lambda: self._personal_actions.set_personal_preference(
                movie_id, PersonalPreference.LIKED
            )
        elif action == "blacklist":
            task = lambda: self._personal_actions.set_personal_preference(
                movie_id, PersonalPreference.BLACKLISTED
            )
        elif action == "clear_preference":
            task = lambda: self._personal_actions.clear_personal_preference(movie_id)
        elif action == "add_watchlist":
            task = lambda: self._personal_actions.add_to_watchlist(movie_id)
        elif action == "remove_watchlist":
            task = lambda: self._personal_actions.remove_from_watchlist(movie_id)
        elif action == "record_watch_date":
            date = self._selected_watch_date()
            if date is None:  # defensive; validated above
                self._personal_busy = False
                self._personal_busy_action = None
                self._set_personal_action_busy(action, False)
                return
            task = lambda: self._personal_actions.record_watch(
                movie_id,
                datetime(date.year(), date.month(), date.day(), tzinfo=timezone.utc),
            )
        else:
            task = lambda: self._personal_actions.record_watch(movie_id)
        self._personal_runner.submit(
            token, task, self._personal_saved, self._personal_failed
        )

    def _watchlist_clicked(self) -> None:
        snapshot = self._personal_snapshot
        if snapshot is None:
            return
        self._run_personal_action(
            "remove_watchlist" if snapshot.state.is_watchlisted else "add_watchlist"
        )

    def _personal_saved(self, token: int, value: object) -> None:
        if (
            token != self._personal_token
            or self._movie_id is None
            or not isinstance(value, PersonalMovieSnapshot)
        ):
            return
        action = self._personal_busy_action
        event_id = self._personal_busy_event_id
        self._personal_busy = False
        self._personal_busy_action = None
        self._personal_busy_event_id = None
        if action is not None:
            self._set_personal_action_busy(action, False, event_id=event_id)
        self._personal_snapshot = value
        self._render_personal(action=action)
        self._set_personal_controls_enabled(True)
        if action == "remove_watchlist":
            self._mark_watched_date_button.setEnabled(False)
        action_button = self._button_for_personal_action(action or "")
        if action_button is not None and action in {
            "like",
            "blacklist",
            "clear_preference",
        }:
            action_button.setFocus(Qt.FocusReason.OtherFocusReason)
        self.personal_changed.emit(
            value.state.movie_id,
            _personal_sections_for_action(action),
        )
        if self._restore_focus_after_personal_action:
            self._restore_focus_after_personal_action = False
            self.take_stable_focus()

    def _render_preference_state(self) -> None:
        snapshot = self._personal_snapshot
        if snapshot is None:
            return
        state = snapshot.state
        self._like_button.setChecked(state.preference is PersonalPreference.LIKED)
        self._blacklist_button.setChecked(
            state.preference is PersonalPreference.BLACKLISTED
        )
        self._clear_preference_button.setEnabled(
            state.preference is not PersonalPreference.NO_OPINION
        )

    def _render_watchlist_state(self) -> None:
        snapshot = self._personal_snapshot
        if snapshot is None:
            return
        self._watchlist_button.setText(
            self._localizer.text(
                TextId.DETAILS_IN_WATCHLIST
                if snapshot.state.is_watchlisted
                else TextId.DETAILS_ADD_WATCHLIST
            )
        )

    def _render_watch_summary(self) -> None:
        snapshot = self._personal_snapshot
        if snapshot is None:
            return
        state = snapshot.state
        if state.watch_count:
            summary = self._localizer.text(
                TextId.DETAILS_WATCHED_COUNT, count=state.watch_count
            )
            if state.last_watched is not None:
                summary += "  •  " + self._localizer.text(
                    TextId.DETAILS_LAST_WATCHED,
                    date=_format_personal_date(state.last_watched),
                )
        else:
            summary = self._localizer.text(TextId.DETAILS_NOT_WATCHED)
        self._personal_summary.setText(summary)

    def _history_event_text(self, event: WatchEvent) -> str:
        return (
            f"{_format_personal_date(event.watched_at)}  •  "
            + self._localizer.text(
                TextId.DETAILS_REWATCH
                if event.rewatch
                else TextId.DETAILS_FIRST_WATCH
            )
        )

    def _create_history_row(
        self, event: WatchEvent
    ) -> tuple[QFrame, QLabel, QPushButton]:
        row = QFrame()
        row.setObjectName("historyRow")
        row_layout = QHBoxLayout(row)
        row_layout.setContentsMargins(0, SPACE_SMALL, 0, SPACE_SMALL)
        text = QLabel(self._history_event_text(event))
        text.setTextInteractionFlags(Qt.TextInteractionFlag.TextSelectableByMouse)
        row_layout.addWidget(text)
        remove = QPushButton()
        remove.setObjectName(f"removeWatchEventButton_{event.id}")
        remove.setProperty("role", "historyRemoveAction")
        remove.setSizePolicy(QSizePolicy.Policy.Maximum, QSizePolicy.Policy.Fixed)
        set_fluent_icon(remove, FluentIconName.DELETE)
        self._localizer.bind_text(remove, TextId.DETAILS_REMOVE_WATCH_EVENT)
        remove.clicked.connect(
            lambda _checked=False, event_id=event.id: self._remove_watch_event(event_id)
        )
        row_layout.addWidget(remove)
        return row, text, remove

    def _sync_history_rows(self) -> None:
        snapshot = self._personal_snapshot
        if snapshot is None:
            return
        desired = list(reversed(snapshot.history))
        desired_ids = {event.id for event in desired}

        # Delete only rows that no longer exist. Existing rows remain the same
        # QWidget instances, preserving paint/focus stability.
        for event_id in tuple(self._history_row_widgets):
            if event_id in desired_ids:
                continue
            row, _label, _remove = self._history_row_widgets.pop(event_id)
            self._history_rows.removeWidget(row)
            row.hide()
            row.deleteLater()

        # Create just the new rows and update text in place for existing ones.
        for event in desired:
            entry = self._history_row_widgets.get(event.id)
            if entry is None:
                entry = self._create_history_row(event)
                self._history_row_widgets[event.id] = entry
            row, label, remove = entry
            label.setText(self._history_event_text(event))
            remove.setEnabled(
                not (
                    self._personal_busy_action == "remove_watch_event"
                    and self._personal_busy_event_id == event.id
                )
            )
            self._history_rows.removeWidget(row)
            self._history_rows.addWidget(row)
            row.show()

    def _render_personal(self, *, action: str | None = None) -> None:
        if self._personal_snapshot is None:
            return
        if action is None or action in {"like", "blacklist", "clear_preference"}:
            self._render_preference_state()
        if action is None or action in {"add_watchlist", "remove_watchlist"}:
            self._render_watchlist_state()
        if action is None or action in {
            "record_watch",
            "record_watch_date",
            "remove_watch_event",
        }:
            self._render_watch_summary()
            self._sync_history_rows()

    def _remove_watch_event(self, event_id: int) -> None:
        if self._personal_actions is None or self._movie_id is None or self._personal_busy:
            return
        # Move focus before the affected row can disappear. The diff update
        # below deletes only that row, so focus cannot cascade through a full
        # history teardown into the sidebar Search field.
        self.take_stable_focus()
        self._personal_busy = True
        self._personal_busy_action = "remove_watch_event"
        self._personal_busy_event_id = event_id
        self._restore_focus_after_personal_action = True
        self._personal_error.clear()
        self._set_personal_action_busy("remove_watch_event", True, event_id=event_id)
        self._personal_token += 1
        token = self._personal_token
        self._personal_runner.submit(
            token,
            lambda: self._personal_actions.remove_watch_event(event_id),
            self._personal_saved,
            self._personal_failed,
        )

    def _clear_history_rows(self) -> None:
        for row, _label, _remove in self._history_row_widgets.values():
            self._history_rows.removeWidget(row)
            row.hide()
            row.deleteLater()
        self._history_row_widgets.clear()

    def set_movie(self, details: MovieDetails) -> None:
        self._movie_id = details.movie_id
        # A date selection belongs to the current watch action. Every newly
        # opened movie starts from today; no fake sentinel date is involved.
        self._watch_date.setMaximumDate(QDate.currentDate())
        self._watch_date.setDate(QDate.currentDate())
        self._current_rating = details.rating
        self._poster_token += 1
        self._poster_loaded = False
        self._state.hide()
        self._content.show()
        self._scroll.horizontalScrollBar().setValue(0)
        self._scroll.verticalScrollBar().setValue(0)
        self._poster.clear()
        self._poster.setText(title_initials(details.title))
        if (
            self._poster_loader is not None
            and details.provider is not None
            and details.poster_reference is not None
        ):
            self._poster_loader.request(
                self,
                PosterRequest(details.provider, details.poster_reference),
                self._poster_token,
            )
        self._title.setText(details.title)
        self._meta.setText(
            "  •  ".join(
                (
                    format_year(details.year),
                    format_runtime(details.runtime_minutes),
                    format_rating(details.rating),
                )
            )
        )
        self._hero_meta.setText(
            "  •  ".join(
                (
                    format_year(details.year),
                    format_runtime(details.runtime_minutes),
                )
            )
        )
        self._refresh_rating_text()
        self._current_genres = tuple(details.genres)
        self._refresh_genres_text()
        if details.original_title and details.original_title != details.title:
            self._original_title.setText(
                self._localizer.text(
                    TextId.DETAILS_ORIGINAL_TITLE,
                    title=details.original_title,
                )
            )
            self._original_title.show()
        else:
            self._original_title.hide()
        self._overview.setText(
            details.overview
            or self._localizer.text(TextId.DETAILS_OVERVIEW_UNAVAILABLE)
        )
        if self._personal_actions is not None:
            self._personal_panel.show()
            self._personal_snapshot = None
            self._personal_summary.clear()
            self._clear_history_rows()
            self._load_personal(details.movie_id)
        self.update_media_files(details.media_files)

    def update_media_files(self, media_files: tuple[MediaFileDetails, ...]) -> None:
        """Diff Media Files by stable MediaFileId and update rows in place."""

        self._media_file_count = len(media_files)
        desired_ids = {item.media_file_id for item in media_files}
        for media_file_id in tuple(self._media_file_panels):
            if media_file_id in desired_ids:
                continue
            panel = self._media_file_panels.pop(media_file_id)
            self._files.removeWidget(panel)
            panel.hide()
            panel.setParent(None)
            panel.deleteLater()

        # Remove only the transient empty label, never surviving file panels.
        for index in reversed(range(self._files.count())):
            widget = self._files.itemAt(index).widget()
            if widget is not None and widget.objectName() == "mediaEmptyLabel":
                self._files.removeWidget(widget)
                widget.deleteLater()

        if not media_files:
            empty = QLabel(self._localizer.text(TextId.DETAILS_NO_FILES))
            empty.setObjectName("mediaEmptyLabel")
            empty.setProperty("role", "muted")
            self._files.addWidget(empty)
            return

        for media_file in media_files:
            panel = self._media_file_panels.get(media_file.media_file_id)
            panel = self._media_file_panel(media_file, panel=panel)
            self._media_file_panels[media_file.media_file_id] = panel
            self._files.removeWidget(panel)
            self._files.addWidget(panel)
            panel.show()

    def _refresh_rating_text(self) -> None:
        self._rating_stars.setText(provider_rating_stars(self._current_rating))
        self._rating_stars.setVisible(False)
        self._rating_value.setText(
            f"{self._localizer.text(TextId.TMDB_RATING_LABEL)} "
            f"{provider_rating_text(self._current_rating)}"
            if self._current_rating is not None
            else self._localizer.text(TextId.TMDB_RATING_UNAVAILABLE)
        )
        has_rating = self._current_rating is not None
        self._hero_rating_separator.setVisible(has_rating)
        self._hero_rating_star.setVisible(has_rating)
        self._hero_rating_value.setVisible(has_rating)
        self._hero_rating_value.setText(
            provider_rating_text(self._current_rating) if has_rating else ""
        )

    def _refresh_genres_text(self, _language=None) -> None:
        self._genres.setText(
            self._localizer.genres(list(self._current_genres))
            if self._current_genres
            else self._localizer.text(TextId.DETAILS_GENRES_UNAVAILABLE)
        )

    def apply_poster(self, token: int, asset: PosterAsset | None) -> None:
        if token != self._poster_token or asset is None:
            return
        image = QImage.fromData(asset.content)
        if image.isNull():
            return
        pixmap = QPixmap.fromImage(image).scaled(
            self._poster.width(),
            self._poster.height(),
            Qt.AspectRatioMode.KeepAspectRatioByExpanding,
            Qt.TransformationMode.SmoothTransformation,
        )
        left = max(0, (pixmap.width() - self._poster.width()) // 2)
        top = max(0, (pixmap.height() - self._poster.height()) // 2)
        self._poster.setPixmap(
            pixmap.copy(left, top, self._poster.width(), self._poster.height())
        )
        self._poster.setText("")
        self._poster_loaded = True

    def show_error(self, message: str) -> None:
        self._state.setText(message)
        self._state.show()
        self._content.hide()

    def clear_movie(self) -> None:
        self._movie_id = None
        self._current_rating = None
        self._refresh_rating_text()
        self._current_genres = ()
        self._refresh_genres_text()
        self._poster_token += 1
        self._personal_token += 1
        self._personal_busy = False
        self._personal_busy_action = None
        self._personal_busy_event_id = None
        self._personal_snapshot = None
        self._personal_panel.hide()
        self._clear_history_rows()
        self._clear_files()
        self._media_file_count = 0
        self.show_error(self._localizer.text(TextId.DETAILS_REMOVED))

    def _clear_files(self) -> None:
        while self._files.count():
            item = self._files.takeAt(0)
            widget = item.widget()
            if widget is not None:
                widget.hide()
                widget.setParent(None)
                widget.deleteLater()
        self._media_file_panels.clear()

    def _media_file_panel(
        self,
        media_file: MediaFileDetails,
        *,
        panel: QFrame | None = None,
    ) -> QFrame:
        if panel is None:
            panel = QFrame()
            layout = QVBoxLayout(panel)
        else:
            layout = panel.layout()
            assert isinstance(layout, QVBoxLayout)
            self._clear_layout_widgets(layout)
        panel.setObjectName("mediaFileEntry")
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(SPACE_SMALL)

        status_text = self._localizer.text(
            TextId.STATUS_MISSING
            if media_file.status is MediaFileAvailability.MISSING
            else TextId.STATUS_PRESENT
        )
        status = QLabel(status_text)
        status.setObjectName("mediaStatusLabel")
        status.setProperty("availability", media_file.status.value)
        status.setSizePolicy(QSizePolicy.Policy.Maximum, QSizePolicy.Policy.Fixed)
        layout.addWidget(status, 0, Qt.AlignmentFlag.AlignRight)

        info = QFrame()
        info.setObjectName("mediaFileInfoSurface")
        info.setMinimumWidth(0)
        info.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Maximum)
        info_layout = QVBoxLayout(info)
        info_layout.setContentsMargins(SPACE_MEDIUM, SPACE_SMALL, SPACE_MEDIUM, SPACE_SMALL)
        info_layout.setSpacing(SPACE_SMALL // 2)

        filename_text = Path(media_file.current_path).name or media_file.current_path
        filename = ElidedDetailsLabel(filename_text)
        filename.setObjectName("mediaFilenameLabel")
        filename.setProperty("role", "rowTitle")
        filename.setToolTip(media_file.current_path)
        filename.setTextInteractionFlags(
            Qt.TextInteractionFlag.TextSelectableByMouse
            | Qt.TextInteractionFlag.TextSelectableByKeyboard
        )
        self._localizer.mark_ltr(filename)
        info_layout.addWidget(filename)

        path_text = (
            self._localizer.text(TextId.LAST_KNOWN_PATH) + media_file.current_path
            if media_file.status is MediaFileAvailability.MISSING
            else media_file.current_path
        )
        path = ElidedDetailsLabel(
            path_text,
            mode=Qt.TextElideMode.ElideMiddle,
        )
        path.setObjectName("mediaPathLabel")
        path.setProperty("role", "muted")
        path.setToolTip(media_file.current_path)
        path.setTextInteractionFlags(
            Qt.TextInteractionFlag.TextSelectableByMouse
            | Qt.TextInteractionFlag.TextSelectableByKeyboard
        )
        self._localizer.mark_ltr(path)
        info_layout.addWidget(path)

        technical = tuple(
            value
            for value in (
                media_file.resolution,
                media_file.source,
                media_file.codec,
                media_file.extension,
                format_file_size(media_file.file_size),
            )
            if value
        )
        facts = QLabel("  •  ".join(technical))
        facts.setObjectName("mediaFactsLabel")
        facts.setProperty("role", "muted")
        facts.setWordWrap(True)
        facts.setTextInteractionFlags(
            Qt.TextInteractionFlag.TextSelectableByMouse
            | Qt.TextInteractionFlag.TextSelectableByKeyboard
        )
        self._localizer.mark_ltr(facts)
        info_layout.addWidget(facts)
        layout.addWidget(info)

        actions = QHBoxLayout()
        actions.setSpacing(SPACE_SMALL)
        play = QPushButton()
        play.setObjectName(f"playMovieButton_{media_file.media_file_id}")
        play.setProperty("role", "primaryAction")
        set_fluent_icon(play, FluentIconName.PLAY)
        play.setProperty("mediaFileId", media_file.media_file_id)
        play.setEnabled(self._local_media_actions is not None)
        self._localizer.bind_text(play, TextId.PLAY_MOVIE)
        actions.addWidget(play)
        open_folder = QPushButton()
        open_folder.setObjectName(f"openFolderButton_{media_file.media_file_id}")
        open_folder.setProperty("role", "secondaryAction")
        set_fluent_icon(open_folder, FluentIconName.OPEN_FOLDER)
        open_folder.setProperty("mediaFileId", media_file.media_file_id)
        open_folder.setEnabled(self._local_media_actions is not None)
        self._localizer.bind_text(open_folder, TextId.OPEN_FOLDER)
        actions.addWidget(open_folder)
        organize = QPushButton()
        organize.setObjectName(f"organizeFileButton_{media_file.media_file_id}")
        organize.setProperty("role", "secondaryAction")
        set_fluent_icon(organize, FluentIconName.ORGANIZE)
        organize.setProperty("mediaFileId", media_file.media_file_id)
        organize.setEnabled(
            self._organization_actions is not None
            and media_file.status is MediaFileAvailability.PRESENT
        )
        self._localizer.bind_text(organize, TextId.ORGANIZE_FILE)
        actions.addWidget(organize)
        locate = QPushButton()
        locate.setObjectName(f"locateFileButton_{media_file.media_file_id}")
        locate.setProperty("role", "secondaryAction")
        set_fluent_icon(locate, FluentIconName.SEARCH)
        locate.setVisible(
            self._reconciliation_actions is not None
            and media_file.status is MediaFileAvailability.MISSING
        )
        locate.setEnabled(locate.isVisible())
        self._localizer.bind_text(locate, TextId.LOCATE_FILE)
        actions.addWidget(locate)
        actions.addStretch(1)
        layout.addLayout(actions)

        feedback = QLabel()
        feedback.setObjectName(f"mediaActionErrorLabel_{media_file.media_file_id}")
        feedback.setProperty("role", "error")
        feedback.setWordWrap(True)
        layout.addWidget(feedback)

        play.clicked.connect(
            partial(self._perform_media_action, "play", media_file, feedback)
        )
        open_folder.clicked.connect(
            partial(self._perform_media_action, "open_folder", media_file, feedback)
        )
        organize.clicked.connect(partial(self._open_organization_dialog, media_file))
        locate.clicked.connect(partial(self._open_relink_dialog, media_file))
        return panel

    def _clear_layout_widgets(self, layout: QLayout) -> None:
        while layout.count():
            item = layout.takeAt(0)
            child_layout = item.layout()
            if child_layout is not None:
                self._clear_layout_widgets(child_layout)
                child_layout.deleteLater()
            widget = item.widget()
            if widget is not None:
                widget.hide()
                widget.setParent(None)
                widget.deleteLater()

    def _open_relink_dialog(self, media_file: MediaFileDetails) -> None:
        if self._reconciliation_actions is None or self._reconciliation_runner is None:
            return
        movie_id = self._movie_id
        dialog = RelinkMediaFileDialog(
            self._reconciliation_actions,
            media_file.media_file_id,
            Path(media_file.current_path),
            self._reconciliation_runner,
            localizer=self._localizer,
            parent=self,
        )
        dialog.relinked.connect(
            lambda _result, selected_movie_id=movie_id: self._relink_finished(selected_movie_id)
        )
        dialog.finished.connect(lambda _result, active=dialog: self._relink_dialogs.discard(active))
        self._relink_dialogs.add(dialog)
        dialog.show()

    def _relink_finished(self, movie_id: int | None) -> None:
        if movie_id is not None:
            self.relink_completed.emit(movie_id)

    def _open_organization_dialog(self, media_file: MediaFileDetails) -> None:
        if self._organization_actions is None or self._organization_runner is None:
            return
        movie_id = self._movie_id
        dialog = OrganizeFileDialog(
            self._organization_actions,
            media_file_id=media_file.media_file_id,
            current_path=Path(media_file.current_path),
            file_size=media_file.file_size,
            runner=self._organization_runner,
            localizer=self._localizer,
            parent=self,
        )
        dialog.organization_succeeded.connect(
            lambda _result, selected_movie_id=movie_id: self._organization_finished(
                selected_movie_id
            )
        )
        dialog.finished.connect(lambda _result, active=dialog: self._organization_dialogs.discard(active))
        self._organization_dialogs.add(dialog)
        dialog.show()

    def _organization_finished(self, movie_id: int | None) -> None:
        if movie_id is not None:
            self.organization_completed.emit(movie_id)

    def invalidate_pending_organization_delivery(self) -> None:
        for dialog in tuple(self._organization_dialogs):
            if not dialog.is_executing:
                dialog.invalidate_pending_delivery()
        for dialog in tuple(self._relink_dialogs):
            dialog.invalidate_pending()

    def wait_for_pending_tasks(self) -> None:
        if self._organization_runner is None:
            return
        waiter = getattr(self._organization_runner, "wait_for_done", None)
        if callable(waiter):
            waiter()
        for dialog in tuple(self._organization_dialogs):
            dialog.invalidate_pending_delivery()
        if self._reconciliation_runner is not None:
            waiter = getattr(self._reconciliation_runner, "wait_for_done", None)
            if callable(waiter):
                waiter()
        for dialog in tuple(self._relink_dialogs):
            dialog.invalidate_pending()

    def _perform_media_action(
        self,
        operation: str,
        media_file: MediaFileDetails,
        feedback: QLabel,
    ) -> None:
        if self._local_media_actions is None:
            return
        feedback.clear()
        media_path = Path(media_file.current_path)
        try:
            if operation == "play":
                self._local_media_actions.play(media_path)
            else:
                self._local_media_actions.open_folder(media_path)
        except MissingMediaFileError:
            feedback.setText(self._localizer.text(TextId.MEDIA_MISSING_ACTION))
        except LocalMediaActionError:
            feedback.setText(self._localizer.text(
                TextId.PLAY_FAILED
                if operation == "play"
                else TextId.OPEN_FOLDER_FAILED
            ))
