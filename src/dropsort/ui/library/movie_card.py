from __future__ import annotations

from typing import Protocol

from PySide6.QtCore import Qt, Signal
from PySide6.QtGui import QFontMetrics, QImage, QMouseEvent, QPixmap
from PySide6.QtWidgets import QFrame, QLabel, QVBoxLayout

from dropsort.application.dto.library import MovieListItem
from dropsort.posters import PosterAsset, PosterRequest
from dropsort.ui.common.formatting import title_initials
from dropsort.ui.common.theme import (
    CARD_HEIGHT,
    CARD_WIDTH,
    POSTER_HEIGHT,
    SPACE_SMALL,
)
from dropsort.ui.posters import PosterRequestDispatcher
from dropsort.ui.localization import TextId, UiLocalizer


class PosterPresentationDispatcher(Protocol):
    def poster_request_started(self, card: "MovieCard", token: int) -> None: ...

    def poster_request_invalidated(self, card: "MovieCard") -> None: ...

    def poster_result_ready(
        self, card: "MovieCard", token: int, pixmap: QPixmap | None
    ) -> None: ...


class MovieCard(QFrame):
    """Poster-first movie tile containing only the poster and movie title."""

    selected = Signal(int)

    def __init__(
        self,
        item: MovieListItem,
        *,
        poster_loader: PosterRequestDispatcher | None = None,
        poster_presenter: PosterPresentationDispatcher | None = None,
        localizer: UiLocalizer | None = None,
        parent=None,
    ) -> None:
        super().__init__(parent)
        self.item = item
        self._localizer = localizer or UiLocalizer()
        self._poster_loader = poster_loader
        self._poster_presenter = poster_presenter
        self.setObjectName("movieCard")
        self.setCursor(Qt.CursorShape.PointingHandCursor)
        self.setFixedSize(CARD_WIDTH, CARD_HEIGHT)
        self.setAccessibleName(
            self._localizer.text(
                TextId.ACCESSIBILITY_MOVIE_OPEN_DETAILS,
                title=item.title,
            )
        )
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
            if self._poster_presenter is not None:
                self._poster_presenter.poster_request_started(
                    self, self._poster_token
                )
            poster_loader.request(
                self,
                PosterRequest(item.provider, item.poster_reference),
                self._poster_token,
            )

        self._title = QLabel()
        self._title.setObjectName("movieTitleLabel")
        self._title.setFixedHeight(36)
        self._title.setToolTip(item.title)
        self._title.setAccessibleName(item.title)
        self._title.setAccessibleDescription(
            self._localizer.text(TextId.ACCESSIBILITY_MOVIE_FULL_TITLE)
        )
        self._title.setMaximumWidth(CARD_WIDTH)
        layout.addWidget(self._title)
        self._update_title_text()
        self._localizer.bind_retranslator(self, self._language_changed)

    def update_item(self, item: MovieListItem) -> None:
        """Update a stable MovieId card in place from a newer DTO."""

        if item.movie_id != self.item.movie_id:
            raise ValueError("MovieCard identity cannot change")

        previous_poster = (self.item.provider, self.item.poster_reference)
        next_poster = (item.provider, item.poster_reference)
        self.item = item
        self._retranslate()
        self._title.setToolTip(item.title)
        self._title.setAccessibleName(item.title)
        self._update_title_text()

        if next_poster == previous_poster:
            if not self._poster_loaded:
                self._poster.setText(title_initials(item.title))
            return
        if self._poster_presenter is not None:
            self._poster_presenter.poster_request_invalidated(self)
        self._poster_token += 1
        self._poster_loaded = False
        self._poster.clear()
        self._poster.setText(title_initials(item.title))
        if (
            self._poster_loader is not None
            and item.provider is not None
            and item.poster_reference is not None
        ):
            if self._poster_presenter is not None:
                self._poster_presenter.poster_request_started(
                    self, self._poster_token
                )
            self._poster_loader.request(
                self,
                PosterRequest(item.provider, item.poster_reference),
                self._poster_token,
            )

    def _update_title_text(self) -> None:
        # Movie cards have a fixed CARD_WIDTH, so title wrapping must not depend
        # on a construction-time QLabel width and then mutate again on the first
        # visible resize.  Render against the final fixed width from the start.
        self._title.setText(
            _two_line_elide(self.item.title, self._title.font(), CARD_WIDTH)
        )

    def _language_changed(self, _language) -> None:
        self._retranslate()

    def _retranslate(self) -> None:
        self.setAccessibleName(
            self._localizer.text(
                TextId.ACCESSIBILITY_MOVIE_OPEN_DETAILS,
                title=self.item.title,
            )
        )
        self.setAccessibleDescription(self.item.title)
        self._title.setAccessibleDescription(
            self._localizer.text(TextId.ACCESSIBILITY_MOVIE_FULL_TITLE)
        )

    @property
    def poster_loaded(self) -> bool:
        return self._poster_loaded

    def apply_poster(self, token: int, asset: PosterAsset | None) -> None:
        if token != self._poster_token:
            return

        pixmap: QPixmap | None = None
        if asset is not None:
            image = QImage.fromData(asset.content)
            if not image.isNull():
                pixmap = _cover_pixmap(
                    image, self._poster.width(), self._poster.height()
                )

        if self._poster_presenter is not None:
            self._poster_presenter.poster_result_ready(self, token, pixmap)
        elif pixmap is not None:
            self._present_poster(token, pixmap)

    def _present_poster(self, token: int, pixmap: QPixmap) -> None:
        if token != self._poster_token:
            return
        self._poster.setPixmap(pixmap)
        self._poster.setText("")
        self._poster_loaded = True

    def mouseReleaseEvent(self, event: QMouseEvent) -> None:
        if (
            event.button() == Qt.MouseButton.LeftButton
            and self.rect().contains(event.position().toPoint())
        ):
            self.selected.emit(self.item.movie_id)
        super().mouseReleaseEvent(event)


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
