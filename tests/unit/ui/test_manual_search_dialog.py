from __future__ import annotations

from dataclasses import dataclass

from PySide6.QtCore import Qt
from PySide6.QtWidgets import QApplication

from dropsort.application.dto.manual_search import ManualMovieSearchResult, ManualMovieSearchRequest
from dropsort.application.configuration.localization import UiLanguage
from dropsort.metadata.contracts.errors import MetadataUnavailableError
from dropsort.ui.common.theme import application_stylesheet
from dropsort.ui.localization import UiLocalizer
from dropsort.ui.scan.manual_search_dialog import ManualSearchDialog
from dropsort.ui.scan.manual_search_result_card import ManualSearchResultCard


class ImmediateRunner:
    def submit(self, token, task, on_success, on_failure):
        try:
            on_success(token, task())
        except BaseException as error:
            on_failure(token, error)


@dataclass
class Actions:
    result: object
    calls: list[tuple[str, str | None]]

    def manual_movie_search(self, title, year=None):
        self.calls.append((title, year))
        if isinstance(self.result, BaseException):
            raise self.result
        return self.result


def test_manual_dialog_searches_and_allows_explicit_candidate_selection(
    qapp: QApplication, proposal_factory
) -> None:
    candidate = proposal_factory().candidates[0]
    result = ManualMovieSearchResult(ManualMovieSearchRequest("Kaze Tachinu", 2013), (candidate,))
    actions = Actions(result, [])
    dialog = ManualSearchDialog(proposal_factory().discovery, actions, runner=ImmediateRunner())

    dialog.search_title.setText("Kaze Tachinu")
    dialog.search_year.setText("2013")
    dialog.search()
    assert actions.calls == [("Kaze Tachinu", "2013")]
    assert dialog.results.count() == 1
    dialog.results.setCurrentRow(0)
    selected = []
    dialog.candidate_selected.connect(selected.append)
    dialog.select_current()
    assert selected == [candidate]


def test_manual_dialog_blank_year_and_zero_results_are_distinct(
    qapp: QApplication, proposal_factory
) -> None:
    result = ManualMovieSearchResult(ManualMovieSearchRequest("Kaze Tachinu"), ())
    actions = Actions(result, [])
    dialog = ManualSearchDialog(proposal_factory().discovery, actions, runner=ImmediateRunner())
    dialog.search_year.setText("   ")
    dialog.search()
    assert actions.calls == [("The Dark Knight", "   ")]
    assert dialog.results.count() == 0
    assert "No results" in dialog.error_label.text()


def test_manual_dialog_retranslates_dynamic_state_messages(
    qapp: QApplication, proposal_factory
) -> None:
    localizer = UiLocalizer()
    result = ManualMovieSearchResult(ManualMovieSearchRequest("Kaze Tachinu"), ())
    dialog = ManualSearchDialog(
        proposal_factory().discovery,
        Actions(result, []),
        runner=ImmediateRunner(),
        localizer=localizer,
    )
    dialog.search()
    localizer.set_language(UiLanguage.ARABIC)

    assert "لم يتم" in dialog.error_label.text()


def test_manual_dialog_invalid_year_never_calls_provider(qapp: QApplication, proposal_factory) -> None:
    result = ManualMovieSearchResult(ManualMovieSearchRequest("Kaze Tachinu"), ())
    actions = Actions(result, [])
    dialog = ManualSearchDialog(proposal_factory().discovery, actions, runner=ImmediateRunner())
    dialog.search_year.setText("20x3")
    dialog.search()
    assert actions.calls == []
    assert "valid four-digit" in dialog.error_label.text()


def test_manual_dialog_provider_failure_is_not_reported_as_zero_results(
    qapp: QApplication, proposal_factory
) -> None:
    actions = Actions(MetadataUnavailableError("offline"), [])
    dialog = ManualSearchDialog(proposal_factory().discovery, actions, runner=ImmediateRunner())
    dialog.search()
    assert "unavailable" in dialog.error_label.text().casefold()
    assert "No results" not in dialog.error_label.text()


def test_manual_dialog_keeps_empty_state_compact_and_limits_visible_results(
    qapp: QApplication, proposal_factory, candidate_factory
) -> None:
    candidates = tuple(
        candidate_factory(external_id=str(index), title=f"Movie {index}")
        for index in range(1, 8)
    )
    result = ManualMovieSearchResult(ManualMovieSearchRequest("Movies"), candidates)
    dialog = ManualSearchDialog(
        proposal_factory().discovery,
        Actions(result, []),
        runner=ImmediateRunner(),
    )

    assert dialog.results.isHidden()
    assert dialog.results.minimumHeight() == 0

    dialog.search()

    assert dialog.results.count() == 5
    assert not dialog.results.isHidden()
    assert dialog.results.height() < 500


def test_manual_dialog_deduplicates_before_visible_result_limit(
    qapp: QApplication, proposal_factory, candidate_factory
) -> None:
    first = candidate_factory(external_id="1", title="Movie 1")
    candidates = (first, first) + tuple(
        candidate_factory(external_id=str(index), title=f"Movie {index}")
        for index in range(2, 8)
    )
    result = ManualMovieSearchResult(ManualMovieSearchRequest("Movies"), candidates)
    dialog = ManualSearchDialog(
        proposal_factory().discovery,
        Actions(result, []),
        runner=ImmediateRunner(),
    )

    dialog.search()

    assert dialog.results.count() == 5
    assert [dialog.results.item(index).data(Qt.ItemDataRole.UserRole).external_id for index in range(5)] == [
        "1", "2", "3", "4", "5"
    ]


def test_result_card_exposes_structured_fields_and_wrapped_overview(
    qapp: QApplication, candidate_factory
) -> None:
    candidate = candidate_factory(
        title="A deliberately long movie title that should wrap instead of being truncated",
        external_id="149870",
        rating=7.8,
        overview="A long overview that remains readable in a bounded wrapped label rather than becoming one horizontal raw text row.",
    )
    card = ManualSearchResultCard(candidate, UiLocalizer())

    assert card.title_label.text() == candidate.title
    assert card.year_label.text() == "2008"
    assert card.id_label.text() == "TMDB 149870"
    assert card.rating_label.text() == "Rating 7.8 / 10"
    assert card.overview_label.text() == candidate.overview
    assert card.title_label.wordWrap()
    assert card.overview_label.wordWrap()
    assert card.select_button.text() == "Select"


def test_manual_dialog_uses_vertical_cards_without_horizontal_scroll(
    qapp: QApplication, proposal_factory, candidate_factory
) -> None:
    candidates = tuple(candidate_factory(external_id=str(index), title=f"Movie {index}") for index in range(1, 6))
    dialog = ManualSearchDialog(
        proposal_factory().discovery,
        Actions(ManualMovieSearchResult(ManualMovieSearchRequest("Movies"), candidates), []),
        runner=ImmediateRunner(),
    )
    dialog.search()

    assert dialog.results.horizontalScrollBarPolicy() == Qt.ScrollBarPolicy.ScrollBarAlwaysOff
    assert dialog.results.widgetResizable()
    assert dialog.results.findChildren(ManualSearchResultCard)
    assert not dialog.results_heading.isHidden()


def test_each_card_selects_its_exact_candidate_without_bottom_action(
    qapp: QApplication, proposal_factory, candidate_factory
) -> None:
    first = candidate_factory(external_id="1", title="First")
    second = candidate_factory(external_id="2", title="Second")
    dialog = ManualSearchDialog(
        proposal_factory().discovery,
        Actions(ManualMovieSearchResult(ManualMovieSearchRequest("Movies"), (first, second)), []),
        runner=ImmediateRunner(),
    )
    selected = []
    dialog.candidate_selected.connect(selected.append)
    dialog.search()
    dialog.results.item(1).select_button.click()

    assert selected == [second]
    assert dialog.findChild(type(dialog.search_button), "selectThisMovieButton") is None


def test_search_refresh_replaces_cards_and_clears_old_selection(
    qapp: QApplication, proposal_factory, candidate_factory
) -> None:
    first = candidate_factory(external_id="1", title="First")
    second = candidate_factory(external_id="2", title="Second")

    class RefreshActions:
        def __init__(self):
            self.calls = 0

        def manual_movie_search(self, title, year=None):
            self.calls += 1
            candidate = first if self.calls == 1 else second
            return ManualMovieSearchResult(ManualMovieSearchRequest(title), (candidate,))

    actions = RefreshActions()
    dialog = ManualSearchDialog(proposal_factory().discovery, actions, runner=ImmediateRunner())
    dialog.search()
    old_card = dialog.results.item(0)
    dialog.search()

    assert dialog.results.count() == 1
    assert dialog.results.item(0).candidate is second
    assert old_card.parent() is None
    selected = []
    dialog.candidate_selected.connect(selected.append)
    old_card.select_button.click()
    assert selected == []


def test_manual_search_localizes_card_and_keeps_technical_metadata_ltr(
    qapp: QApplication, proposal_factory, candidate_factory
) -> None:
    localizer = UiLocalizer()
    dialog = ManualSearchDialog(
        proposal_factory().discovery,
        Actions(ManualMovieSearchResult(ManualMovieSearchRequest("Movies"), (candidate_factory(),)), []),
        runner=ImmediateRunner(),
        localizer=localizer,
    )
    dialog.search()
    localizer.set_language(UiLanguage.ARABIC)
    card = dialog.results.item(0)

    assert card.select_button.text() == "اختيار"
    assert card.rating_label.text().startswith("التقييم")
    assert card.id_label.layoutDirection() == Qt.LayoutDirection.LeftToRight
    assert "manualSearchResultCard" in application_stylesheet("Main")
    assert "manualSearchResultCard" in application_stylesheet("Light")
