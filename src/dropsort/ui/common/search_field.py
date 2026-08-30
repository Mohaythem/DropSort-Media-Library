from __future__ import annotations

from PySide6.QtCore import Qt
from PySide6.QtWidgets import QAbstractItemView, QCompleter, QLineEdit, QListView

from dropsort.ui.common.theme import CONTROL_HEIGHT


class PageSearchEdit(QLineEdit):
    """Page-owned search field with stable logical-direction behavior."""

    def __init__(self, parent=None) -> None:
        super().__init__(parent)
        self.setProperty("role", "pageSearch")
        self.setClearButtonEnabled(True)
        self.setFixedHeight(CONTROL_HEIGHT)

    def setCompleter(self, completer: QCompleter | None) -> None:
        """Attach a completer whose popup uses the shared search semantics."""

        super().setCompleter(completer)
        if completer is None:
            return
        popup = completer.popup()
        popup.setObjectName("pageSearchSuggestions")
        popup.setProperty("role", "pageSearchSuggestions")
        popup.setHorizontalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAlwaysOff)
        popup.setVerticalScrollMode(QAbstractItemView.ScrollMode.ScrollPerPixel)
        popup.setTextElideMode(Qt.TextElideMode.ElideRight)
        popup.setLayoutDirection(self.layoutDirection())
        if isinstance(popup, QListView):
            popup.setUniformItemSizes(True)
        popup.style().unpolish(popup)
        popup.style().polish(popup)

    def apply_language_direction(self, *, rtl: bool) -> None:
        self.setLayoutDirection(
            Qt.LayoutDirection.RightToLeft
            if rtl
            else Qt.LayoutDirection.LeftToRight
        )
        # Qt mirrors logical left/right alignment in an RTL line edit and also
        # moves its built-in trailing clear action to the opposite edge.
        self.setAlignment(Qt.AlignmentFlag.AlignLeft)
        completer = self.completer()
        if completer is not None:
            completer.popup().setLayoutDirection(self.layoutDirection())
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
