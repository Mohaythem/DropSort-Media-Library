from __future__ import annotations

from PySide6.QtCore import QSize
from PySide6.QtWidgets import QApplication, QPushButton

from dropsort.application.configuration.theme import UiTheme
from dropsort.ui.common.icon import (
    FLUENT_ICON_ASSETS,
    FLUENT_ICON_ROOT,
    FluentIconName,
    fluent_icon,
    set_fluent_icon,
)
from dropsort.ui.common.theme import apply_theme
from dropsort.ui.main_window.window import MainWindow


def test_fluent_registry_contains_only_native_16px_filled_svg_assets() -> None:
    assert len(FLUENT_ICON_ASSETS) == 26
    assert set(FLUENT_ICON_ASSETS) == set(FluentIconName)
    assert not list(FLUENT_ICON_ROOT.glob("*.png"))
    assert not list(FLUENT_ICON_ROOT.glob("*.jpg"))

    for path in FLUENT_ICON_ASSETS.values():
        source = path.read_text(encoding="utf-8")
        assert path.suffix == ".svg"
        assert 'viewBox="0 0 16 16"' in source
        assert "currentColor" in source
        assert "#212121" not in source


def test_fluent_icons_render_at_16px_for_all_supported_themes(qapp: QApplication) -> None:
    for theme in UiTheme:
        apply_theme(qapp, theme)
        for name in FluentIconName:
            icon = fluent_icon(name, palette=qapp.palette())
            assert not icon.isNull(), name
            assert not icon.pixmap(QSize(16, 16)).isNull(), name

    apply_theme(qapp, UiTheme.MAIN)


def test_icon_attachment_is_semantic_and_theme_refreshable(qapp: QApplication) -> None:
    button = QPushButton("Library")
    set_fluent_icon(button, FluentIconName.LIBRARY)

    assert button.property("dropsortIconName") == FluentIconName.LIBRARY.value
    assert button.iconSize() == QSize(16, 16)
    assert not button.icon().isNull()


def test_permanent_sidebar_check_library_uses_fluent_icon(qapp, movie_item_factory, movie_details_factory) -> None:
    library = type(
        "Library",
        (),
        {
            "list_movies": lambda self: (movie_item_factory(),),
            "get_movie_details": lambda self, _movie_id: movie_details_factory(),
        },
    )()
    window = MainWindow(library, load_on_show=False)
    button = window.findChild(QPushButton, "checkLibraryNavButton")

    assert button is not None
    assert button.property("dropsortIconName") == FluentIconName.CHECK_LIBRARY.value
    assert button.iconSize() == QSize(16, 16)
    assert button.minimumHeight() == 42
    assert not button.icon().isNull()
    window.close()
