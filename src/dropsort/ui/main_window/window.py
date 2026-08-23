from __future__ import annotations

from dataclasses import dataclass
import logging

from PySide6.QtCore import (
    QEvent,
    QSignalBlocker,
    QSize,
    Qt,
    QStringListModel,
)
from PySide6.QtGui import QCloseEvent
from PySide6.QtWidgets import (
    QFrame,
    QCompleter,
    QHBoxLayout,
    QLabel,
    QLineEdit,
    QMainWindow,
    QPushButton,
    QStackedWidget,
    QSplitter,
    QVBoxLayout,
    QWidget,
    QApplication,
)

from dropsort.application.dto.reconciliation import LibraryReconciliationProgress
from dropsort.application.dto.library_health import LibraryHealthProgress
from dropsort.application.dto.catalog_maintenance import ClearLibraryDataResult
from dropsort.application.errors import (
    CatalogClearBlockedError,
    CatalogClearError,
    LibraryQueryError,
)
from dropsort.library.playback import LocalMediaActions
from dropsort.posters import PosterActions
from dropsort.application.configuration.theme import SIDEBAR_DEFAULT_WIDTH
from dropsort.ui.common.theme import (
    CONTROL_HEIGHT,
    ICON_SIZE,
    NAVIGATION_ITEM_HEIGHT,
    SPACE_4,
    SPACE_12,
    SPACE_SMALL,
    apply_theme,
)
from dropsort.ui.common.icon import (
    FluentIconName,
    application_icon,
    refresh_fluent_icons,
    set_fluent_icon,
)
from dropsort.ui.common.tasks import TaskRunner
from dropsort.ui.contracts import (
    ImportUiActions,
    LibraryUiActions,
    PersonalLibraryUiActions,
    OrganizationUiActions,
    OperationHistoryUiActions,
    ReconciliationUiActions,
    SettingsUiActions,
)
from dropsort.ui.history import OperationHistoryView
from dropsort.ui.library.library_view import LibraryView
from dropsort.ui.movie_details.details_view import MovieDetailsView
from dropsort.ui.posters import PosterLoader
from dropsort.ui.scan.import_view import ImportView
from dropsort.ui.settings import SettingsView
from dropsort.ui.personal_library import PersonalLibraryView
from dropsort.ui.reconciliation import LibraryCheckPage, LibraryFileCheckDialog
from dropsort.ui.common.tasks import QtTaskRunner
from dropsort.application.configuration.localization import UiLanguage
from dropsort.ui.localization import TextId, UiLocalizer


LOGGER = logging.getLogger(__name__)


class LibrarySearchEdit(QLineEdit):
    """Search input whose Escape first belongs to its local interaction."""

    def keyPressEvent(self, event) -> None:
        if event.key() == Qt.Key.Key_Escape:
            completer = self.completer()
            if completer is not None and completer.popup() is not None and completer.popup().isVisible():
                completer.popup().hide()
                event.accept()
                return
            if self.text():
                self.clear()
                event.accept()
                return
        super().keyPressEvent(event)


@dataclass(frozen=True, slots=True)
class NavigationItem:
    """One shell destination, independent from how the pane is rendered."""

    item_id: str
    text_id: TextId
    tooltip_id: TextId
    icon: FluentIconName
    destination: str
    object_name: str
    placement: str = "primary"


class NavigationButton(QPushButton):
    """Native button with the prototype's independent selection marker."""

    def __init__(self, text: str = "", parent: QWidget | None = None) -> None:
        super().__init__(text, parent)
        self.setProperty("role", "navigationItem")
        self._accent = QFrame(self)
        self._accent.setObjectName("navigationAccent")
        self._accent.setFixedSize(3, 24)
        self._accent.setAttribute(Qt.WidgetAttribute.WA_TransparentForMouseEvents)
        self._accent.hide()
        self.toggled.connect(self._selection_changed)

    def _selection_changed(self, selected: bool) -> None:
        # Qt already repaints :checked pseudo-state automatically.  A manual
        # unpolish/polish here forced two extra style/layout passes on every
        # navigation transition (old item off, new item on).
        self._accent.setVisible(selected)
        self._position_accent()

    def _position_accent(self) -> None:
        x = self.width() - self._accent.width() if self.isRightToLeft() else 0
        self._accent.move(x, max(0, (self.height() - self._accent.height()) // 2))
        self._accent.raise_()

    def resizeEvent(self, event) -> None:
        super().resizeEvent(event)
        self._position_accent()

    def changeEvent(self, event) -> None:
        super().changeEvent(event)
        if event.type() == QEvent.Type.LayoutDirectionChange:
            self._position_accent()


class MainWindow(QMainWindow):
    NAVIGATION_ITEMS = (
        NavigationItem(
            "library",
            TextId.NAV_LIBRARY,
            TextId.NAV_LIBRARY,
            FluentIconName.LIBRARY,
            "library",
            "libraryNavButton",
        ),
        NavigationItem(
            "personal",
            TextId.NAV_PERSONAL_LIBRARY,
            TextId.NAV_PERSONAL_LIBRARY,
            FluentIconName.PERSONAL_LIBRARY,
            "personal_library",
            "personalLibraryNavButton",
        ),
        NavigationItem(
            "import",
            TextId.NAV_ADD_MOVIES,
            TextId.NAV_ADD_MOVIES,
            FluentIconName.ADD_MOVIES,
            "import",
            "importNavButton",
        ),
        NavigationItem(
            "check_library",
            TextId.CHECK_LIBRARY_FILES,
            TextId.CHECK_LIBRARY_FILES,
            FluentIconName.CHECK_LIBRARY,
            "check_library",
            "checkLibraryNavButton",
        ),
        NavigationItem(
            "settings",
            TextId.NAV_SETTINGS,
            TextId.NAV_SETTINGS,
            FluentIconName.SETTINGS,
            "settings",
            "settingsNavButton",
            "footer",
        ),
    )

    def __init__(
        self,
        actions: LibraryUiActions,
        *,
        import_actions: ImportUiActions | None = None,
        personal_actions: PersonalLibraryUiActions | None = None,
        settings_actions: SettingsUiActions | None = None,
        local_media_actions: LocalMediaActions | None = None,
        organization_actions: OrganizationUiActions | None = None,
        operation_history_actions: OperationHistoryUiActions | None = None,
        reconciliation_actions: ReconciliationUiActions | None = None,
        poster_actions: PosterActions | None = None,
        task_runner: TaskRunner | None = None,
        localizer: UiLocalizer | None = None,
        load_on_show: bool = True,
        parent=None,
    ) -> None:
        super().__init__(parent)
        self.setWindowIcon(application_icon())
        self._actions = actions
        self._settings_actions = settings_actions
        language = UiLanguage.ENGLISH
        if settings_actions is not None:
            current_language = getattr(settings_actions, "current_ui_language", None)
            if callable(current_language):
                language = current_language()
        self._localizer = localizer or UiLocalizer(language, self)
        self._current_section = ""
        self._previous_library_section = "library"
        self._previous_check_library_section: str | None = None
        self._task_runner = task_runner or QtTaskRunner(self)
        self._reconciliation_actions = reconciliation_actions
        self._library_check_dialogs: set[LibraryFileCheckDialog] = set()
        self._maintenance_token = 0
        self._maintenance_active = False
        self._single_instance_closing = False
        self._search_query = ""
        self.setWindowTitle(self._localizer.text(TextId.WINDOW_TITLE))
        self._localizer.language_changed.connect(
            lambda _language: self.setWindowTitle(
                self._localizer.text(TextId.WINDOW_TITLE)
            )
        )
        self.resize(1440, 900)

        root = QWidget()
        root.setObjectName("appRoot")
        root_layout = QVBoxLayout(root)
        root_layout.setContentsMargins(0, 0, 0, 0)
        root_layout.setSpacing(0)
        self.setCentralWidget(root)

        sidebar = QFrame()
        self.sidebar = sidebar
        sidebar.setObjectName("sidebar")
        sidebar.setFixedWidth(SIDEBAR_DEFAULT_WIDTH)
        sidebar_layout = QVBoxLayout(sidebar)
        sidebar_layout.setContentsMargins(SPACE_SMALL, SPACE_SMALL, SPACE_SMALL, SPACE_SMALL)
        sidebar_layout.setSpacing(0)

        self._sidebar_top_row = QFrame()
        self._sidebar_top_row.setObjectName("sidebarTopRow")
        self._sidebar_top_row.setFixedHeight(NAVIGATION_ITEM_HEIGHT)
        brand_row = QHBoxLayout(self._sidebar_top_row)
        brand_row.setContentsMargins(SPACE_12, 0, SPACE_12, 0)
        brand_row.setSpacing(0)

        brand = QLabel("DropSort")
        brand.setObjectName("brandLabel")
        brand_row.addWidget(brand)
        sidebar_layout.addWidget(self._sidebar_top_row)
        sidebar_layout.addSpacing(16)

        self._sidebar_search_wrap = QFrame()
        self._sidebar_search_wrap.setObjectName("sidebarSearchWrap")
        search_wrap_layout = QHBoxLayout(self._sidebar_search_wrap)
        search_wrap_layout.setContentsMargins(SPACE_12, 0, SPACE_12, 0)
        search_wrap_layout.setSpacing(0)
        self._search_field = LibrarySearchEdit()
        self._search_field.setObjectName("librarySearchInput")
        self._configure_search_field(self._search_field)
        search_wrap_layout.addWidget(self._search_field)
        sidebar_layout.addWidget(self._sidebar_search_wrap)
        sidebar_layout.addSpacing(16)

        self._sidebar_primary_navigation = QFrame()
        self._sidebar_primary_navigation.setObjectName("sidebarPrimaryNavigation")
        primary_navigation_layout = QVBoxLayout(self._sidebar_primary_navigation)
        primary_navigation_layout.setContentsMargins(0, 0, 0, 0)
        primary_navigation_layout.setSpacing(SPACE_4)
        self._navigation_buttons = self._build_navigation(
            primary_navigation_layout,
            personal_available=personal_actions is not None,
            import_available=import_actions is not None,
            settings_available=settings_actions is not None,
            placement="primary",
        )
        sidebar_layout.addWidget(self._sidebar_primary_navigation)
        sidebar_layout.addStretch(1)
        self._sidebar_footer = QFrame()
        self._sidebar_footer.setObjectName("sidebarFooter")
        footer_layout = QVBoxLayout(self._sidebar_footer)
        footer_layout.setContentsMargins(0, 0, 0, 0)
        footer_layout.setSpacing(SPACE_4)
        self._navigation_buttons.update(
            self._build_navigation(
                footer_layout,
                personal_available=personal_actions is not None,
                import_available=import_actions is not None,
                settings_available=settings_actions is not None,
                placement="footer",
            )
        )
        sidebar_layout.addWidget(self._sidebar_footer)
        self._library_button = self._navigation_buttons["library"]
        self._personal_button = self._navigation_buttons.get("personal")
        self._import_button = self._navigation_buttons.get("import")
        self._check_library_button = self._navigation_buttons["check_library"]
        self._settings_button = self._navigation_buttons.get("settings")

        self.poster_loader = PosterLoader(poster_actions) if poster_actions is not None else None
        self._stack = QStackedWidget()
        self.library_view = LibraryView(
            actions,
            poster_loader=self.poster_loader,
            localizer=self._localizer,
        )
        self.check_library_page = LibraryCheckPage(
            reconciliation_actions,
            self._task_runner,
            parent=self,
            localizer=self._localizer,
        )
        self.personal_view = (
            PersonalLibraryView(
                personal_actions,
                poster_loader=self.poster_loader if hasattr(self, "poster_loader") else None,
                runner=self._task_runner,
                localizer=self._localizer,
            )
            if personal_actions is not None
            else None
        )
        self.details_view = MovieDetailsView(
            poster_loader=self.poster_loader,
            local_media_actions=local_media_actions,
            organization_actions=organization_actions,
            organization_runner=self._task_runner,
            reconciliation_actions=reconciliation_actions,
            reconciliation_runner=self._task_runner,
            personal_actions=personal_actions,
            personal_runner=self._task_runner,
            localizer=self._localizer,
        )
        self.import_view = (
            ImportView(import_actions, runner=self._task_runner, localizer=self._localizer)
            if import_actions is not None
            else None
        )
        self.settings_view = (
            SettingsView(settings_actions, localizer=self._localizer)
            if settings_actions is not None
            else None
        )
        self.history_view = (
            OperationHistoryView(
                operation_history_actions,
                runner=self._task_runner,
                localizer=self._localizer,
            )
            if operation_history_actions is not None
            else None
        )
        self._stack.addWidget(self.library_view)
        self._stack.addWidget(self.check_library_page)
        if self.personal_view is not None:
            self._stack.addWidget(self.personal_view)
            self.personal_view.movie_selected.connect(self.show_movie_details)
        self._stack.addWidget(self.details_view)
        if self.import_view is not None:
            self._stack.addWidget(self.import_view)
            self.import_view.catalog_changed.connect(self._refresh_library_snapshot)
            self.import_view.settings_requested.connect(self.show_settings)
        if self.settings_view is not None:
            self._stack.addWidget(self.settings_view)
            self.settings_view.session_token_applied.connect(self._return_from_settings)
            self.settings_view.clear_library_requested.connect(
                self._clear_library_data_requested
            )
            self.settings_view.history_requested.connect(self.show_history)
            self.settings_view.theme_changed.connect(self._theme_changed)
        if self.history_view is not None:
            self._stack.addWidget(self.history_view)
            self.history_view.catalog_changed.connect(self._refresh_library_snapshot)
        self._splitter = QSplitter(Qt.Orientation.Horizontal)
        self._splitter.setObjectName("mainSplitter")
        self._splitter.setChildrenCollapsible(False)
        self._splitter.setHandleWidth(0)
        self._splitter.addWidget(sidebar)
        content_shell = QWidget()
        content_shell.setObjectName("contentShell")
        content_layout = QVBoxLayout(content_shell)
        content_layout.setContentsMargins(0, 0, 0, 0)
        content_layout.setSpacing(0)
        content_layout.addWidget(self._stack, 1)
        self._splitter.addWidget(content_shell)
        self._splitter.setStretchFactor(1, 1)
        body = QWidget()
        body_layout = QHBoxLayout(body)
        body_layout.setContentsMargins(0, 0, 0, 0)
        body_layout.setSpacing(0)
        body_layout.addWidget(self._splitter)
        root_layout.addWidget(body, 1)
        self._splitter.setSizes(
            [SIDEBAR_DEFAULT_WIDTH, max(1, self.width() - SIDEBAR_DEFAULT_WIDTH)]
        )
        self._localizer.language_changed.connect(
            lambda _language: self._refresh_shell_text()
        )

        self.library_view.movie_selected.connect(self.show_movie_details)
        self.library_view.check_files_requested.connect(self.show_check_library_from_library)
        self.library_view.clear_search_requested.connect(self._clear_search_state)
        self.check_library_page.start_requested.connect(self._start_check_library_page)
        self.check_library_page.progress_changed.connect(self._library_check_progress)
        self.check_library_page.back_requested.connect(self._return_from_check_library)
        self.library_view.search_candidates_changed.connect(self._set_search_suggestions)
        self.details_view.back_requested.connect(self.navigate_back)
        self.details_view.organization_completed.connect(self._organization_completed)
        self.details_view.relink_completed.connect(self._organization_completed)
        self.details_view.personal_changed.connect(self._personal_changed)
        # Theme is applied by desktop composition before any widget is built.
        # Finish shell geometry/icons, then synchronously prepare the initial
        # Library page before the window can receive its first visible paint.
        refresh_fluent_icons(self)
        self._apply_shell_metrics()
        if load_on_show:
            self.show_library()

    def _configure_search_field(self, field: LibrarySearchEdit) -> None:
        field.setClearButtonEnabled(True)
        field.setFixedHeight(CONTROL_HEIGHT)
        field.setMaximumWidth(430)
        field.setMinimumWidth(0)
        field.setAccessibleName("Library search")
        field.setPlaceholderText(
            self._localizer.text(TextId.LIBRARY_SEARCH_PLACEHOLDER)
        )
        field.textChanged.connect(self._search_changed)
        if field is self._search_field:
            self._search_completer = QCompleter(self)
            self._search_completer.setCaseSensitivity(Qt.CaseSensitivity.CaseInsensitive)
            self._search_completer.setFilterMode(Qt.MatchFlag.MatchContains)
            self._search_completer.setMaxVisibleItems(7)
            self._search_completer.setModel(QStringListModel([], self._search_completer))
            self._search_completer.activated.connect(self._search_suggestion_activated)
            field.setCompleter(self._search_completer)

    def _apply_shell_metrics(self) -> None:
        self._search_field.setMinimumHeight(CONTROL_HEIGHT)
        self._search_field.setMaximumHeight(CONTROL_HEIGHT)

    def _refresh_shell_text(self) -> None:
        self._search_field.setPlaceholderText(
            self._localizer.text(TextId.LIBRARY_SEARCH_PLACEHOLDER)
        )
        self._search_field.setAccessibleName("Library search")

    def _set_header_section(self, text_id: TextId, *, search_visible: bool = True) -> None:
        # Keep the fixed sidebar geometry stable on every destination, but make
        # its search control interactive only while Library owns the query.
        # Hiding/removing the wrapper would shift the navigation rows and look
        # like the shell itself had refreshed.
        del text_id
        # The search row is a permanent part of the fixed sidebar geometry.
        # Never hide/show it as a navigation side effect; only change whether
        # Library owns the interaction.
        if self._search_field.isEnabled() != search_visible:
            self._search_field.setEnabled(search_visible)

    def _set_search_suggestions(self, values: object) -> None:
        if not isinstance(values, tuple):
            return
        model = self._search_completer.model()
        if isinstance(model, QStringListModel):
            strings = [value for value in values if isinstance(value, str)]
            model.setStringList(strings)

    def _search_changed(self, query: str) -> None:
        # Sidebar search is strictly Library-local. A textChanged signal that
        # happens while another destination owns focus must never navigate back
        # to Library or cause an unrelated page refresh.
        if self._current_section != "library":
            if query:
                blocker = QSignalBlocker(self._search_field)
                self._search_field.clear()
                del blocker
            self._search_query = ""
            return
        self._search_query = query
        self.library_view.set_search_query(query)

    def _clear_search_state(self, *, render_library: bool = True) -> None:
        """Clear the shell search without triggering navigation side effects.

        Search is a transient Library interaction, not persistent application
        state.  Clearing it under a signal blocker prevents the old behavior
        where leaving a filtered Library briefly routed through another search
        update (or where focus returned to a stale query after a detail action).
        """

        completer = self._search_field.completer()
        if completer is not None and completer.popup() is not None:
            completer.popup().hide()
        if self._search_field.text():
            blocker = QSignalBlocker(self._search_field)
            self._search_field.clear()
            del blocker
        self._search_query = ""
        self.library_view.clear_search_query(render=render_library)

    def _search_suggestion_activated(
        self, value: str, field: LibrarySearchEdit | None = None
    ) -> None:
        target = field or self._search_field
        target.setText(value)
        target.setFocus()
        target.end(False)

    @property
    def current_section(self) -> str:
        return self._current_section

    def keyPressEvent(self, event) -> None:
        """Provide one safe back action without competing with modal UI."""

        if event.key() == Qt.Key.Key_Escape:
            if QApplication.activeModalWidget() is not None or QApplication.activePopupWidget() is not None:
                event.accept()
                return
            if self._current_section == "details":
                self.navigate_back()
            elif self._current_section == "check_library":
                self._return_from_check_library()
            event.accept()
            return
        super().keyPressEvent(event)

    def show_library(self) -> None:
        if self._current_section == "library":
            return
        self._previous_library_section = "library"
        # Prepare the destination while it is still hidden.  This avoids a
        # one-frame empty/old-state flash when returning to a stale Library.
        self.library_view.set_search_query(self._search_field.text())
        self.library_view.activate()
        self.library_view.prepare_for_width(self._content_page_width())
        self._current_section = "library"
        self._set_navigation_checked("library")
        self._set_header_section(TextId.LIBRARY_HEADING, search_visible=True)
        self._set_current_page(self.library_view)

    def show_personal_library(self) -> None:
        if self.personal_view is None or self._current_section == "personal":
            return
        self._clear_search_state(render_library=False)
        # Keep any cached Personal Library snapshot painted while a stale
        # refresh happens in the background, then reveal the page. Resolve the
        # card columns first so the hidden page cannot reflow on its first paint.
        self.personal_view.activate()
        self.personal_view.prepare_for_width(self._content_page_width())
        self._previous_library_section = "personal"
        self._current_section = "personal"
        self._set_navigation_checked("personal")
        self._set_header_section(TextId.PERSONAL_LIBRARY_HEADING, search_visible=False)
        self._set_current_page(self.personal_view)

    def show_check_library(self) -> None:
        """Show the persistent Check Library page without starting a run."""

        if self._current_section == "check_library":
            return
        self._clear_search_state(render_library=False)
        if self._current_section != "check_library":
            self._previous_check_library_section = (
                self._current_section
                if self._current_section in {"library", "personal"}
                else None
            )
        self._current_section = "check_library"
        self._set_navigation_checked("check_library")
        self._set_header_section(TextId.CHECK_FILES_TITLE, search_visible=False)
        self._set_current_page(self.check_library_page)
        self.check_library_page.setFocus(Qt.FocusReason.OtherFocusReason)

    def show_check_library_from_library(self) -> None:
        self._previous_check_library_section = "library"
        self.show_check_library()

    def _start_check_library_page(self) -> None:
        if self.check_library_page.is_running:
            return
        self.check_library_page.start_check()

    def _return_from_check_library(self) -> None:
        previous = self._previous_check_library_section
        self._previous_check_library_section = None
        if previous == "personal" and self.personal_view is not None:
            self.show_personal_library()
        elif previous == "library":
            self.show_library()

    def activate_from_single_instance(self) -> None:
        """Restore and foreground the existing window for a second launch."""

        if self._single_instance_closing:
            return
        self.show()
        self.showNormal()
        self.raise_()
        self.activateWindow()

    def show_import(self) -> None:
        if self.import_view is None or self._current_section == "import":
            return
        self._clear_search_state(render_library=False)
        self._current_section = "import"
        self._set_navigation_checked("import")
        self._set_header_section(TextId.ADD_MOVIES_TITLE, search_visible=False)
        self._set_current_page(self.import_view)

    def show_settings(self) -> None:
        if self.settings_view is None or self._current_section == "settings":
            return
        self._clear_search_state(render_library=False)
        self.settings_view.refresh_status()
        self._current_section = "settings"
        self._set_navigation_checked("settings")
        self._set_header_section(TextId.SETTINGS_TITLE, search_visible=False)
        self._set_current_page(self.settings_view)

    def show_history(self) -> None:
        if self.history_view is None or self._current_section == "history":
            return
        self._clear_search_state(render_library=False)
        # Reuse an already-rendered snapshot on ordinary navigation.
        # Explicit Refresh and operation mutations still request fresh data.
        self.history_view.activate()
        self._current_section = "history"
        self._set_navigation_checked("settings")
        self._set_header_section(TextId.HISTORY_TITLE, search_visible=False)
        self._set_current_page(self.history_view)

    def _theme_changed(self, theme) -> None:
        apply_theme(QApplication.instance(), theme)
        refresh_fluent_icons(self)

    def show_movie_details(self, movie_id: int) -> None:
        if self._current_section in {"library", "personal"}:
            self._previous_library_section = self._current_section
        # A search is only a navigation aid.  Clear it without emitting
        # textChanged before any detail work starts, and move keyboard focus
        # away from the sidebar so a deleted/destroyed detail control cannot
        # unexpectedly return focus to Search.
        self._clear_search_state(render_library=False)
        try:
            details = self._actions.get_movie_details(movie_id)
        except LibraryQueryError:
            LOGGER.warning("Movie details query failed", exc_info=True)
            self.details_view.show_error(
                self._localizer.text(TextId.DETAILS_LOAD_ERROR)
            )
        else:
            # Populate while hidden.  Previously the stack switched first,
            # briefly exposing the previous movie/empty controls before this
            # synchronous local query completed.
            self.details_view.set_movie(details)
        # Resolve responsive direction from the already-stable stack width while
        # Details is still hidden. showEvent must not perform a second reflow.
        self.details_view.prepare_for_width(self._content_page_width())
        self._current_section = "details"
        self._set_navigation_checked(None)
        self._set_header_section(TextId.DETAILS, search_visible=False)
        self._set_current_page(self.details_view)
        self.details_view.take_stable_focus()

    def navigate_back(self) -> None:
        """Return from a detail/check page to its meaningful previous page."""

        if self._current_section == "details":
            if self._previous_library_section == "personal":
                self.show_personal_library()
            else:
                self.show_library()
        elif self._current_section == "check_library":
            self._return_from_check_library()

    def _return_from_details(self) -> None:
        """Compatibility wrapper for existing callers and integrations."""

        self.navigate_back()

    def _content_page_width(self) -> int:
        """Return the shell content width without relying on hidden child geometry."""

        # Before the first show, QStackedWidget can still report its designer/
        # construction-time width. The shell width is already deterministic:
        # fixed sidebar + zero-width splitter handle + content. Use the larger
        # of the live stack width and that known shell width so hidden pages are
        # prepared for the same width they will receive on first paint.
        shell_width = max(1, self.width() - self.sidebar.width())
        return max(shell_width, self._stack.width())

    def _set_current_page(self, widget: QWidget) -> None:
        """Switch the existing page exactly once and never reselect it."""

        if self._stack.currentWidget() is widget:
            return
        self._stack.setCurrentWidget(widget)

    def _set_navigation_checked(self, section: str | None) -> None:
        for item_id, button in self._navigation_buttons.items():
            selected = item_id == section
            if button.isChecked() != selected:
                button.setChecked(selected)

    def _build_navigation(
        self,
        layout: QVBoxLayout,
        *,
        personal_available: bool,
        import_available: bool,
        settings_available: bool,
        placement: str,
    ) -> dict[str, QPushButton]:
        availability = {
            "library": True,
            "personal": personal_available,
            "import": import_available,
            "check_library": True,
            "settings": settings_available,
        }
        buttons: dict[str, QPushButton] = {}
        for item in self.NAVIGATION_ITEMS:
            if item.placement != placement or not availability[item.item_id]:
                continue
            button = self._nav_button("", item.object_name)
            set_fluent_icon(button, item.icon)
            self._localizer.bind_text(button, item.text_id)
            button.clicked.connect(getattr(self, f"show_{item.destination}"))
            layout.addWidget(button)
            buttons[item.item_id] = button
        return buttons

    def _return_from_settings(self) -> None:
        if self.import_view is not None:
            self.show_import()
        else:
            self.show_library()

    def _refresh_library_snapshot(self) -> None:
        """Refresh visible catalog UI, invalidate hidden projections.

        Import/check/reconciliation events can arrive while another page is
        active.  Rebuilding the hidden poster grid in that moment creates a
        very real foreground hitch even though the user cannot see the work.
        Keep the visible page responsive and refresh the cached projection only
        when the user actually returns to it.
        """

        if self._current_section == "library":
            self.library_view.show_library()
        else:
            self.library_view.invalidate_snapshot()

        if self.history_view is not None:
            self.history_view.invalidate_snapshot()

        if self.personal_view is None:
            return
        if self._current_section == "personal":
            self.personal_view.refresh()
        else:
            self.personal_view.invalidate_snapshot()

    def _library_check_progress(self, value: object) -> None:
        if isinstance(value, LibraryHealthProgress):
            movie_ids = [
                change.movie_id for change in value.file_progress.changes
            ]
            movie_ids.extend(value.changed_movie_ids)
        elif isinstance(value, LibraryReconciliationProgress):
            movie_ids = [change.movie_id for change in value.changes]
        else:
            return
        changed_movie_ids = tuple(dict.fromkeys(movie_ids))
        if not changed_movie_ids:
            return
        self.library_view.refresh_movies(changed_movie_ids)
        if self.personal_view is not None:
            self.personal_view.invalidate_snapshot()
        if self.history_view is not None:
            self.history_view.invalidate_snapshot()

    def _personal_changed(self, movie_id: int) -> None:
        # MovieDetails already receives the authoritative PersonalMovieSnapshot
        # returned by the mutation and updates its controls in-place. Reloading
        # the whole details DTO here used to repaint the hero/poster/media cards
        # after every Like / Blacklist / Watch action, producing a visible flash.
        # Defer Personal Library refresh until it is visited again instead.
        del movie_id
        if self.personal_view is not None:
            self.personal_view.invalidate_snapshot()

    def _organization_completed(self, movie_id: int) -> None:
        # File organization/relinking can change paths and availability, but it
        # should not rebuild the entire details surface. Invalidate the Library
        # snapshot for the next visit and refresh only the Media Files region of
        # the currently open movie.
        self.library_view.invalidate_snapshot()
        if self.personal_view is not None:
            self.personal_view.invalidate_snapshot()
        if self.history_view is not None:
            self.history_view.invalidate_snapshot()
        if self._current_section != "details":
            return
        try:
            details = self._actions.get_movie_details(movie_id)
        except LibraryQueryError:
            LOGGER.warning("Movie details refresh after file action failed", exc_info=True)
            return
        self.details_view.update_media_files(details.media_files)

    def show_library_file_check(self) -> None:
        if self._reconciliation_actions is None:
            return
        if self._library_check_dialogs:
            dialog = next(iter(self._library_check_dialogs))
            dialog.show()
            dialog.raise_()
            dialog.activateWindow()
            return
        dialog = LibraryFileCheckDialog(
            self._reconciliation_actions,
            self._task_runner,
            parent=self,
            localizer=self._localizer,
        )
        dialog.progress_changed.connect(self._library_check_progress)
        dialog.finished.connect(
            lambda _result, active=dialog: self._discard_check_dialog(active)
        )
        self._library_check_dialogs.add(dialog)
        dialog.show()

    def _discard_check_dialog(self, dialog: LibraryFileCheckDialog) -> None:
        self._library_check_dialogs.discard(dialog)

    def _clear_library_data_requested(self) -> None:
        if self.settings_view is None or self._settings_actions is None:
            return
        if self._catalog_maintenance_is_busy():
            self.settings_view.show_clear_error(self._localizer.text(TextId.BUSY_CLEAR))
            return
        action = getattr(self._settings_actions, "clear_library_data", None)
        if not callable(action):
            self.settings_view.show_clear_error(
                self._localizer.text(TextId.CLEAR_UNAVAILABLE)
            )
            return
        self._maintenance_active = True
        self._maintenance_token += 1
        token = self._maintenance_token
        self.settings_view.show_clear_started()
        self._task_runner.submit(
            token,
            action,
            self._clear_library_succeeded,
            self._clear_library_failed,
        )

    def _catalog_maintenance_is_busy(self) -> bool:
        if self._maintenance_active:
            return True
        if any(dialog.is_running for dialog in self._library_check_dialogs):
            return True
        if self.check_library_page.is_running:
            return True
        if self.import_view is not None and (
            self.import_view.is_busy or self.import_view.has_pending_catalog_work
        ):
            return True
        return self.poster_loader is not None and self.poster_loader.active_request_count > 0

    def _clear_library_succeeded(self, token: int, value: object) -> None:
        if token != self._maintenance_token or not self._maintenance_active:
            return
        self._maintenance_active = False
        if not isinstance(value, ClearLibraryDataResult):
            self._clear_library_failed(token, RuntimeError("invalid clear result"))
            return
        assert self.settings_view is not None
        self.settings_view.show_clear_result(value)
        self.details_view.clear_movie()
        self.show_library()

    def _clear_library_failed(self, token: int, error: BaseException) -> None:
        if token != self._maintenance_token:
            return
        self._maintenance_active = False
        if self.settings_view is None:
            return
        if isinstance(error, CatalogClearBlockedError):
            message = self._localizer.text(TextId.CLEAR_BLOCKED)
        elif isinstance(error, CatalogClearError):
            message = self._localizer.text(TextId.CLEAR_DATABASE)
        else:
            LOGGER.error(
                "Unexpected library clear failure",
                exc_info=(type(error), error, error.__traceback__),
            )
            message = self._localizer.text(TextId.CLEAR_FAILED)
        self.settings_view.show_clear_error(message)

    def closeEvent(self, event: QCloseEvent) -> None:
        self._single_instance_closing = True
        self._maintenance_token += 1
        self._maintenance_active = False
        if self.import_view is not None:
            self.import_view.invalidate_pending_tasks()
        if self.poster_loader is not None:
            self.poster_loader.invalidate_pending()
        self.details_view.invalidate_pending_organization_delivery()
        if self.history_view is not None:
            self.history_view.invalidate_pending_tasks()
        if self.personal_view is not None:
            self.personal_view.invalidate_pending()
        for dialog in tuple(self._library_check_dialogs):
            dialog.invalidate_pending()
        self.check_library_page.invalidate_pending()
        super().closeEvent(event)

    def wait_for_pending_tasks(self) -> None:
        if self.import_view is not None:
            self.import_view.wait_for_pending_tasks()
        if self.poster_loader is not None:
            self.poster_loader.shutdown()
        self.details_view.wait_for_pending_tasks()
        if self.history_view is not None:
            self.history_view.wait_for_pending_tasks()
        if self.personal_view is not None:
            self.personal_view.wait_for_pending_tasks()
        self._task_runner.wait_for_done()

    @staticmethod
    def _nav_button(text: str, object_name: str) -> QPushButton:
        button = NavigationButton(text)
        button.setObjectName(object_name)
        button.setCheckable(True)
        button.setAutoExclusive(True)
        button.setFixedHeight(NAVIGATION_ITEM_HEIGHT)
        button.setIconSize(QSize(ICON_SIZE, ICON_SIZE))
        return button
