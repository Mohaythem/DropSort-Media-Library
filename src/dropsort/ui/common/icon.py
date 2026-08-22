from __future__ import annotations

from pathlib import Path
from enum import StrEnum
import re

from PySide6.QtCore import QByteArray, QFileInfo, QSize, Qt
from PySide6.QtGui import QColor, QIcon, QPainter, QPalette, QPixmap
from PySide6.QtSvg import QSvgRenderer
from PySide6.QtWidgets import QWidget


APPLICATION_ICON_PATH = Path(__file__).parent.parent / "assets" / "dropsort.ico"
APPLICATION_ICON_SVG_PATH = Path(__file__).parent.parent / "assets" / "dropsort.svg"
FLUENT_ICON_ROOT = Path(__file__).parent.parent / "assets" / "fluent"
FLUENT_ICON_SIZE = QSize(16, 16)


class FluentIconName(StrEnum):
    LIBRARY = "library"
    PERSONAL_LIBRARY = "personal_library"
    ADD_MOVIES = "add_movies"
    CHECK_LIBRARY = "check_library"
    SETTINGS = "settings"
    PANEL_LEFT = "panel_left"
    BACK = "back"
    SEARCH = "search"
    LIKE = "like"
    BLACKLIST = "blacklist"
    WATCHLIST = "watchlist"
    MARK_WATCHED = "mark_watched"
    DATE_PICKER = "date_picker"
    PLAY = "play"
    OPEN_FOLDER = "open_folder"
    ORGANIZE = "organize"
    DELETE = "delete"
    REFRESH = "refresh"
    COPY = "copy"
    SAVE = "save"
    OPERATION_DETAILS = "operation_details"
    WARNING = "warning"
    FAILED = "failed"
    INFO = "info"
    EXTERNAL_LINK = "external_link"
    CLEAR_LIBRARY = "clear_library"


FLUENT_ICON_ASSETS: dict[FluentIconName, Path] = {
    name: FLUENT_ICON_ROOT / f"{name.value}.svg" for name in FluentIconName
}


def application_icon() -> QIcon:
    """Return the bundled DropSort identity icon for all Qt windows."""

    path = (
        APPLICATION_ICON_PATH
        if QFileInfo(str(APPLICATION_ICON_PATH)).isFile()
        else APPLICATION_ICON_SVG_PATH
    )
    return QIcon(str(path))


def fluent_icon_path(name: FluentIconName | str) -> Path:
    """Return the vendored official Fluent 16px Filled SVG path."""

    try:
        icon_name = FluentIconName(name)
    except ValueError as error:
        raise KeyError(f"Unknown DropSort Fluent icon: {name!r}") from error
    path = FLUENT_ICON_ASSETS[icon_name]
    if not QFileInfo(str(path)).isFile():
        raise FileNotFoundError(path)
    return path


def _render_fluent_pixmap(
    path: Path,
    color: QColor,
    *,
    size: QSize = FLUENT_ICON_SIZE,
) -> QPixmap:
    """Render an official currentColor SVG with a palette-resolved color."""

    source = re.sub(
        rb"currentColor",
        color.name().encode("ascii"),
        path.read_bytes(),
    )
    renderer = QSvgRenderer(QByteArray(source))
    pixmap = QPixmap(size)
    pixmap.fill(Qt.GlobalColor.transparent)
    painter = QPainter(pixmap)
    renderer.render(painter)
    painter.end()
    return pixmap


def fluent_icon(
    name: FluentIconName | str,
    *,
    palette: QPalette | None = None,
    size: QSize = FLUENT_ICON_SIZE,
) -> QIcon:
    """Create a theme-colored Fluent icon for a Qt control."""

    path = fluent_icon_path(name)
    palette = palette or QPalette()
    normal_color = palette.color(QPalette.ColorGroup.Active, QPalette.ColorRole.WindowText)
    disabled_color = palette.color(
        QPalette.ColorGroup.Disabled, QPalette.ColorRole.WindowText
    )
    icon = QIcon()
    icon.addPixmap(_render_fluent_pixmap(path, normal_color, size=size), QIcon.Mode.Normal)
    icon.addPixmap(
        _render_fluent_pixmap(path, disabled_color, size=size), QIcon.Mode.Disabled
    )
    return icon


def set_fluent_icon(
    widget: QWidget,
    name: FluentIconName | str,
    *,
    size: QSize = FLUENT_ICON_SIZE,
) -> None:
    """Attach a semantic Fluent icon and remember it for theme refreshes."""

    icon_name = FluentIconName(name)
    widget.setProperty("dropsortIconName", icon_name.value)
    icon = fluent_icon(icon_name, palette=widget.palette(), size=size)
    set_icon = getattr(widget, "setIcon", None)
    if callable(set_icon):
        set_icon(icon)
        set_icon_size = getattr(widget, "setIconSize", None)
        if callable(set_icon_size):
            set_icon_size(size)
        return
    set_pixmap = getattr(widget, "setPixmap", None)
    if callable(set_pixmap):
        set_pixmap(icon.pixmap(size))


def refresh_fluent_icons(root: QWidget) -> None:
    """Re-render all registered child icons after a theme/palette change."""

    for widget in (root, *root.findChildren(QWidget)):
        name = widget.property("dropsortIconName")
        if name:
            set_fluent_icon(widget, name)
