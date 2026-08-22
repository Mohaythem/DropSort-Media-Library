from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]


def source(relative: str) -> str:
    return (ROOT / relative).read_text(encoding="utf-8")


def test_movie_grid_prepares_hidden_page_width_and_ignores_viewport_scrollbar_churn() -> None:
    text = source("src/dropsort/ui/library/movie_grid.py")
    assert "def prepare_for_width" in text
    assert "outer_width = self.width() if available_width is None else available_width" in text
    assert "self.verticalScrollBar().sizeHint().width()" in text
    assert "width = max(self.viewport().width(), CARD_WIDTH)" not in text


def test_navigation_prepares_movie_grids_before_revealing_destination() -> None:
    text = source("src/dropsort/ui/main_window/window.py")

    library = text[text.index("    def show_library"):text.index("    def show_personal_library")]
    assert library.index("self.library_view.activate()") < library.index(
        "self.library_view.prepare_for_width"
    ) < library.index("self._set_current_page(self.library_view)")

    personal = text[text.index("    def show_personal_library"):text.index("    def show_check_library")]
    assert personal.index("self.personal_view.activate()") < personal.index(
        "self.personal_view.prepare_for_width"
    ) < personal.index("self._set_current_page(self.personal_view)")


def test_hidden_child_geometry_is_not_used_as_the_only_navigation_width() -> None:
    text = source("src/dropsort/ui/main_window/window.py")
    helper = text[text.index("    def _content_page_width"):text.index("    def _set_current_page")]
    assert "self.width() - self.sidebar.width()" in helper
    assert "return max(shell_width, self._stack.width())" in helper


def test_fixed_movie_card_title_does_not_mutate_on_first_visible_resize() -> None:
    text = source("src/dropsort/ui/library/movie_card.py")
    assert "def resizeEvent" not in text
    assert "_two_line_elide(self.item.title, self._title.font(), CARD_WIDTH)" in text


def test_reconciliation_progress_text_cannot_change_library_grid_height() -> None:
    text = source("src/dropsort/ui/library/library_view.py")
    assert "self._reconciliation.setWordWrap(False)" in text
    assert "QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Fixed" in text


def test_focus_ring_changes_color_not_border_geometry() -> None:
    text = source("src/dropsort/ui/common/theme.py")
    assert "border: 2px solid {colors.focus};" not in text
    assert text.count("border: 1px solid {colors.focus};") >= 3


def test_primary_navigation_pages_have_no_show_event_correction_or_deferred_timer() -> None:
    paths = (
        "src/dropsort/ui/main_window/window.py",
        "src/dropsort/ui/library/library_view.py",
        "src/dropsort/ui/library/movie_grid.py",
        "src/dropsort/ui/library/movie_card.py",
        "src/dropsort/ui/personal_library/personal_library_view.py",
        "src/dropsort/ui/movie_details/details_view.py",
        "src/dropsort/ui/settings/settings_view.py",
        "src/dropsort/ui/history/view.py",
        "src/dropsort/ui/reconciliation/page.py",
    )
    combined = "\n".join(source(path) for path in paths)
    assert "def showEvent" not in combined
    assert "singleShot" not in combined
    assert "processEvents" not in combined


def test_details_responsive_direction_is_owned_by_outer_page_not_scrollbar_sized_children() -> None:
    text = source("src/dropsort/ui/movie_details/details_view.py")
    columns = text[text.index("class ResponsiveDetailsColumns"):text.index("class ResponsiveDetailsHero")]
    hero = text[text.index("class ResponsiveDetailsHero"):text.index("class ElidedDetailsLabel")]
    assert "def resizeEvent" not in columns
    assert "def resizeEvent" not in hero
    view = text[text.index("class MovieDetailsView"): ]
    assert "self.prepare_for_width(event.size().width())" in view


def test_primary_navigation_cards_do_not_repolish_live_styles() -> None:
    text = source("src/dropsort/ui/library/movie_card.py")
    assert ".unpolish(" not in text
    assert ".polish(" not in text
