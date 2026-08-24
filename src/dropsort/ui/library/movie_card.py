from __future__ import annotations

from PySide6.QtCore import Qt, Signal
from PySide6.QtGui import QFontMetrics, QImage, QMouseEvent, QPixmap
from PySide6.QtWidgets import QFrame, QHBoxLayout, QLabel, QVBoxLayout, QWidget

from dropsort.application.dto.library import MovieListItem
from dropsort.posters import PosterAsset, PosterRequest
from dropsort.ui.common.formatting import format_rating, format_year, title_initials
from dropsort.ui.common.rating import provider_rating_stars
from dropsort.ui.common.theme import (
    CARD_HEIGHT,
    CARD_WIDTH,
    POSTER_HEIGHT,
    SPACE_4,
    SPACE_SMALL,
)
from dropsort.ui.localization import TextId, UiLocalizer
from dropsort.ui.posters import PosterRequestDispatcher


class MovieCard(QFrame):
    """Dense poster-first movie tile matching the approved Make composition.

    The legacy summary labels remain in the object tree (hidden) so existing UI
    contracts and accessibility/tests can continue to query their values.  The
    visible card intentionally shows only poster, title, year and compact TMDB
    rating; local-file state appears only when it needs attention.
    """

    selected = Signal(int)

    def __init__(
        self,
        item: MovieListItem,
        *,
        poster_loader: PosterRequestDispatcher | None = None,
        localizer: UiLocalizer | None = None,
        show_local_state: bool = False,
        parent=None,
    ) -> None:
        super().__init__(parent)
        self.item = item
        self._localizer = localizer or UiLocalizer()
        self._show_local_state = show_local_state
        self._poster_loader = poster_loader
        self.setObjectName("movieCard")
        self.setCursor(Qt.CursorShape.PointingHandCursor)
        self.setFixedSize(CARD_WIDTH, CARD_HEIGHT)
        self.setAccessibleName(f"Open details for {item.title}")
        self.setAccessibleDescription(item.title)

        layout = QVBoxLayout(self)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(SPACE_SMALL)

        self._poster = QLabel(title_initials(item.title))
        self._poster.setObjectName("posterPlaceholder")
        self._poster.setFixedSize(CARD_WIDTH, POSTER_HEIGHT)
        self._poster.setAlignment(Qt.AlignmentFlag.AlignCenter)
        layout.addWidget(self._poster)
        self._poster_token = 1
        self._poster_loaded = False
        if (
            poster_loader is not None
            and item.provider is not None
            and item.poster_reference is not None
        ):
            poster_loader.request(
                self,
                PosterRequest(item.provider, item.poster_reference),
                self._poster_token,
            )

        metadata_host = QWidget()
        metadata_host.setObjectName("movieCardMetadata")
        metadata_host.setFixedWidth(CARD_WIDTH)
        metadata_layout = QVBoxLayout(metadata_host)
        metadata_layout.setContentsMargins(0, 0, 0, 0)
        metadata_layout.setSpacing(SPACE_4)
        layout.addWidget(metadata_host)

        self._title = QLabel()
        self._title.setObjectName("movieTitleLabel")
        self._title.setFixedHeight(36)
        self._title.setToolTip(item.title)
        self._title.setAccessibleName(item.title)
        self._title.setAccessibleDescription("Full movie title")
        self._title.setMaximumWidth(CARD_WIDTH)
        metadata_layout.addWidget(self._title)
        self._update_title_text()

        compact_meta = QHBoxLayout()
        compact_meta.setContentsMargins(0, 0, 0, 0)
        compact_meta.setSpacing(SPACE_4)

        self._year = QLabel(format_year(item.year))
        self._year.setObjectName("movieYearLabel")
        self._year.setProperty("role", "muted")
        compact_meta.addWidget(self._year)

        self._compact_separator = QLabel("·")
        self._compact_separator.setProperty("role", "muted")
        self._compact_separator.setVisible(item.rating is not None)
        compact_meta.addWidget(self._compact_separator)

        self._compact_star = QLabel("★")
        self._compact_star.setObjectName("movieCompactRatingStar")
        self._compact_star.setVisible(item.rating is not None)
        self._compact_star.setAccessibleName(
            self._localizer.text(TextId.ACCESSIBILITY_TMDB_RATING_VISUAL)
        )
        compact_meta.addWidget(self._compact_star)

        self._compact_rating = QLabel(_compact_rating(item.rating))
        self._compact_rating.setObjectName("movieCompactRatingValue")
        self._compact_rating.setVisible(item.rating is not None)
        self._compact_rating.setAccessibleName("TMDB rating")
        self._localizer.mark_ltr(self._compact_rating)
        compact_meta.addWidget(self._compact_rating)
        compact_meta.addStretch(1)
        metadata_layout.addLayout(compact_meta)

        # Compatibility/accessibility values retained but intentionally hidden
        # from the poster-first card composition.
        self._rating_stars = QLabel(provider_rating_stars(item.rating))
        self._rating_stars.setObjectName("movieRatingStars")
        self._rating_stars.setAccessibleName(
            self._localizer.text(TextId.ACCESSIBILITY_TMDB_RATING_VISUAL)
        )
        self._localizer.mark_ltr(self._rating_stars)
        self._rating_stars.hide()
        metadata_layout.addWidget(self._rating_stars)

        self._rating = QLabel(format_rating(item.rating))
        self._rating.setObjectName("movieRatingLabel")
        self._rating.setProperty("role", "muted")
        self._rating.setAccessibleName("TMDB rating")
        self._localizer.mark_ltr(self._rating)
        self._rating.hide()
        metadata_layout.addWidget(self._rating)

        self._file_count = QLabel()
        self._file_count.setObjectName("movieFileCountLabel")
        self._file_count.setProperty("role", "muted")
        self._file_count.hide()
        metadata_layout.addWidget(self._file_count)

        self._availability = QLabel()
        self._availability.setObjectName("movieAvailabilityLabel")
        metadata_layout.addWidget(self._availability)
        self.retranslate()
        layout.addStretch(1)

    def update_item(self, item: MovieListItem) -> None:
        """Update a stable MovieId card in place from a newer DTO."""

        if item.movie_id != self.item.movie_id:
            raise ValueError("MovieCard identity cannot change")

        previous_poster = (self.item.provider, self.item.poster_reference)
        next_poster = (item.provider, item.poster_reference)
        self.item = item
        self.setAccessibleName(f"Open details for {item.title}")
        self.setAccessibleDescription(item.title)
        self._title.setToolTip(item.title)
        self._title.setAccessibleName(item.title)
        self._update_title_text()
        self._year.setText(format_year(item.year))
        has_rating = item.rating is not None
        self._compact_separator.setVisible(has_rating)
        self._compact_star.setVisible(has_rating)
        self._compact_rating.setText(_compact_rating(item.rating))
        self._compact_rating.setVisible(has_rating)
        self._rating_stars.setText(provider_rating_stars(item.rating))
        self._rating.setText(format_rating(item.rating))
        self.retranslate()

        if next_poster == previous_poster:
            if not self._poster_loaded:
                self._poster.setText(title_initials(item.title))
            return
        self._poster_token += 1
        self._poster_loaded = False
        self._poster.clear()
        self._poster.setText(title_initials(item.title))
        if (
            self._poster_loader is not None
            and item.provider is not None
            and item.poster_reference is not None
        ):
            self._poster_loader.request(
                self,
                PosterRequest(item.provider, item.poster_reference),
                self._poster_token,
            )

    def retranslate(self) -> None:
        """Refresh only locale-dependent card text without recreating the widget."""

        item = self.item
        noun = self._localizer.text(
            TextId.FILE_SINGULAR if item.media_file_count == 1 else TextId.FILE_PLURAL
        )
        self._file_count.setText(f"{item.media_file_count} {noun}")

        availability: str | None = None
        availability_role: str | None = None
        if item.all_files_missing:
            availability = self._localizer.text(TextId.MISSING_FILE)
            availability_role = "MISSING"
        elif item.missing_file_count:
            availability = self._localizer.text(
                TextId.MISSING_FILES, count=item.missing_file_count
            )
            availability_role = "PARTIAL"
        elif self._show_local_state and item.media_file_count == 0:
            availability = self._localizer.text(TextId.PERSONAL_NO_LOCAL_COPY)
            availability_role = "PERSONAL"

        self._availability.setText(availability or "")
        next_role = availability_role or ""
        self._availability.setProperty("availability", next_role)
        self._availability.setVisible(availability is not None)
    def _update_title_text(self) -> None:
        # Movie cards have a fixed CARD_WIDTH, so title wrapping must not depend
        # on a construction-time QLabel width and then mutate again on the first
        # visible resize.  Render against the final fixed width from the start.
        self._title.setText(
            _two_line_elide(self.item.title, self._title.font(), CARD_WIDTH)
        )

    @property
    def poster_loaded(self) -> bool:
        return self._poster_loaded

    def apply_poster(self, token: int, asset: PosterAsset | None) -> None:
        if token != self._poster_token or asset is None:
            return
        image = QImage.fromData(asset.content)
        if image.isNull():
            return
        self._poster.setPixmap(_cover_pixmap(image, self._poster.width(), self._poster.height()))
        self._poster.setText("")
        self._poster_loaded = True

    def mouseReleaseEvent(self, event: QMouseEvent) -> None:
        if (
            event.button() == Qt.MouseButton.LeftButton
            and self.rect().contains(event.position().toPoint())
        ):
            self.selected.emit(self.item.movie_id)
        super().mouseReleaseEvent(event)


def _compact_rating(value: float | None) -> str:
    if value is None:
        return ""
    return f"{float(value):.1f}"


def _cover_pixmap(image: QImage, width: int, height: int) -> QPixmap:
    pixmap = QPixmap.fromImage(image).scaled(
        width,
        height,
        Qt.AspectRatioMode.KeepAspectRatioByExpanding,
        Qt.TransformationMode.SmoothTransformation,
    )
    left = max(0, (pixmap.width() - width) // 2)
    top = max(0, (pixmap.height() - height) // 2)
    return pixmap.copy(left, top, width, height)


def _two_line_elide(title: str, font, width: int) -> str:
    """Fit a title into two metric-measured lines without changing its full value."""

    metrics = QFontMetrics(font)
    words = title.split()
    if not words:
        return ""
    lines: list[str] = []
    current = ""
    for word in words:
        candidate = f"{current} {word}".strip()
        if current and metrics.horizontalAdvance(candidate) > width:
            lines.append(current)
            current = word
        else:
            current = candidate
    if current:
        lines.append(current)
    if len(lines) <= 2:
        return "\n".join(
            metrics.elidedText(line, Qt.TextElideMode.ElideRight, width)
            for line in lines
        )
    return "\n".join(
        (
            metrics.elidedText(lines[0], Qt.TextElideMode.ElideRight, width),
            metrics.elidedText(
                " ".join(lines[1:]), Qt.TextElideMode.ElideRight, width
            ),
        )
    )
