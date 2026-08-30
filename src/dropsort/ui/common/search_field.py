from __future__ import annotations

from PySide6.QtCore import Qt
from PySide6.QtWidgets import QLineEdit

from dropsort.ui.common.theme import CONTROL_HEIGHT


class PageSearchEdit(QLineEdit):
    """Page-owned search field with stable logical-direction behavior."""

    def __init__(self, parent=None) -> None:
        super().__init__(parent)
        self.setProperty("role", "pageSearch")
        self.setClearButtonEnabled(True)
        self.setFixedHeight(CONTROL_HEIGHT)

    def apply_language_direction(self, *, rtl: bool) -> None:
        self.setLayoutDirection(
            Qt.LayoutDirection.RightToLeft
            if rtl
            else Qt.LayoutDirection.LeftToRight
        )
        # Qt mirrors logical left/right alignment in an RTL line edit and also
        # moves its built-in trailing clear action to the opposite edge.
        self.setAlignment(Qt.AlignmentFlag.AlignLeft)
        self.updateGeometry()
        self.update()

    def keyPressEvent(self, event) -> None:
        if event.key() == Qt.Key.Key_Escape:
            completer = self.completer()
            if (
                completer is not None
                and completer.popup() is not None
                and completer.popup().isVisible()
            ):
                completer.popup().hide()
                event.accept()
                return
            if self.text():
                self.clear()
                event.accept()
                return
        super().keyPressEvent(event)
