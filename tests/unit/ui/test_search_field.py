from __future__ import annotations

import pytest
from PySide6.QtCore import QStringListModel, Qt
from PySide6.QtWidgets import QCompleter, QToolButton

from dropsort.application.configuration.theme import SELECTABLE_THEMES, UiTheme
from dropsort.ui.common.search_field import PageSearchEdit
from dropsort.ui.common.theme import THEMES, apply_theme, application_stylesheet


@pytest.mark.parametrize("theme_id", SELECTABLE_THEMES)
@pytest.mark.parametrize("rtl", (False, True))
def test_clear_button_is_centered_and_mirrors_without_overlapping_text(
    qapp,
    theme_id: UiTheme,
    rtl: bool,
) -> None:
    apply_theme(qapp, theme_id)
    search = PageSearchEdit()
    search.resize(400, 36)
    search.apply_language_direction(rtl=rtl)
    search.setText("Interstellar")
    search.show()
    qapp.processEvents()

    clear_button = next(
        button for button in search.findChildren(QToolButton) if button.isVisible()
    )
    assert clear_button.height() == 18
    assert clear_button.geometry().center().y() == search.rect().center().y()
    if rtl:
        assert clear_button.geometry().center().x() < search.rect().center().x()
        assert clear_button.geometry().right() < search.cursorRect().left()
    else:
        assert clear_button.geometry().center().x() > search.rect().center().x()
        assert search.cursorRect().right() < clear_button.geometry().left()

    search.close()


@pytest.mark.parametrize("theme_id", SELECTABLE_THEMES)
@pytest.mark.parametrize("prefix", ("i", "in"))
def test_autocomplete_popup_uses_semantic_theme_and_comfortable_rows(
    qapp,
    theme_id: UiTheme,
    prefix: str,
) -> None:
    apply_theme(qapp, theme_id)
    search = PageSearchEdit()
    search.resize(400, 36)
    model = QStringListModel(["Inception", "Inside Out", "Interstellar"], search)
    completer = QCompleter(model, search)
    completer.setCaseSensitivity(Qt.CaseSensitivity.CaseInsensitive)
    completer.setFilterMode(Qt.MatchFlag.MatchContains)
    search.setCompleter(completer)
    search.setText(prefix)
    search.show()
    completer.complete()
    qapp.processEvents()

    popup = completer.popup()
    stylesheet = application_stylesheet(theme_id)
    assert popup.property("role") == "pageSearchSuggestions"
    assert popup.layoutDirection() == search.layoutDirection()
    assert 28 <= popup.sizeHintForRow(0) <= 32
    popup_block = stylesheet.split(
        'QAbstractItemView[role="pageSearchSuggestions"]', 1
    )[1].split("}", 1)[0]
    assert f"background: {THEMES[theme_id].popup};" in popup_block
    assert f"border: 1px solid {THEMES[theme_id].subtle_border};" in popup_block
    assert f"selection-background-color: {THEMES[theme_id].selected};" in popup_block
    assert "#000000" not in popup_block.lower()

    popup.hide()
    search.close()


def test_autocomplete_popup_follows_language_direction(qapp) -> None:
    search = PageSearchEdit()
    model = QStringListModel(["Interstellar"], search)
    search.setCompleter(QCompleter(model, search))

    search.apply_language_direction(rtl=True)
    assert search.completer().popup().layoutDirection() is Qt.LayoutDirection.RightToLeft
    search.apply_language_direction(rtl=False)
    assert search.completer().popup().layoutDirection() is Qt.LayoutDirection.LeftToRight
