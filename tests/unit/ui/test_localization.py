from __future__ import annotations

from PySide6.QtCore import Qt
from PySide6.QtWidgets import QApplication, QLabel

from dropsort.application.configuration.localization import UiLanguage
from dropsort.ui.localization import TextId, UiLocalizer


def test_english_is_default_and_catalog_has_complete_arabic_entries(qapp: QApplication) -> None:
    localizer = UiLocalizer()

    assert localizer.language is UiLanguage.ENGLISH
    assert localizer.text(TextId.NAV_LIBRARY) == "Library"
    assert localizer.missing_translations() == ()


def test_runtime_language_switch_updates_bound_text_and_direction(qapp: QApplication) -> None:
    localizer = UiLocalizer()
    label = QLabel()
    localizer.bind_text(label, TextId.NAV_LIBRARY)

    localizer.set_language(UiLanguage.ARABIC)

    assert label.text() == "المكتبة"
    assert qapp.layoutDirection() is Qt.LayoutDirection.RightToLeft

    localizer.set_language(UiLanguage.ENGLISH)

    assert label.text() == "Library"
    assert qapp.layoutDirection() is Qt.LayoutDirection.LeftToRight


def test_paths_are_explicitly_ltr_in_arabic(qapp: QApplication) -> None:
    localizer = UiLocalizer(UiLanguage.ARABIC)
    path = QLabel(r"D:\Movies\فيلم.mkv")

    localizer.mark_ltr(path)

    assert path.layoutDirection() is Qt.LayoutDirection.LeftToRight
