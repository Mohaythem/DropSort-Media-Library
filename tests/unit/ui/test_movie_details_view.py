from __future__ import annotations

from pathlib import Path

from PySide6.QtCore import Signal
from PySide6.QtWidgets import QApplication, QDialog, QLabel, QPushButton

from dropsort.ui.movie_details.details_view import MovieDetailsView
from dropsort.posters import PosterAsset
from dropsort.library.playback import LocalMediaLaunchError, MissingMediaFileError
from dropsort.application.dto.organization import (
    OrganizationOperation,
    OrganizationPreview,
    OrganizationResult,
)


class DeferredPosterLoader:
    def __init__(self) -> None:
        self.requests = []

    def request(self, receiver, request, token) -> None:
        self.requests.append((receiver, request, token))


def _text(view: MovieDetailsView, object_name: str) -> str:
    label = view.findChild(QLabel, object_name)
    assert label is not None
    return label.text()


def test_details_view_renders_metadata_and_multiple_physical_files(
    qapp: QApplication,
    movie_details_factory,
) -> None:
    view = MovieDetailsView()

    view.set_movie(movie_details_factory())

    assert _text(view, "detailsTitleLabel") == "The Dark Knight"
    assert _text(view, "detailsMetaLabel") == "2008  •  2h 32m  •  8.5 / 10"
    assert _text(view, "detailsGenresLabel") == "Action  •  Crime"
    assert _text(view, "detailsOverviewLabel") == "Batman faces a criminal mastermind."
    assert view.media_file_count == 2
    file_text = "\n".join(
        label.text() for label in view.findChildren(QLabel) if label.objectName() == "mediaPathLabel"
    )
    assert r"D:\Movies\The Dark Knight.mkv" in file_text
    assert r"E:\Archive\The Dark Knight 4K.mkv" in file_text
    assert "Missing" in " ".join(
        label.text() for label in view.findChildren(QLabel) if label.objectName() == "mediaStatusLabel"
    )


def test_details_view_preserves_unknown_optional_metadata(
    qapp: QApplication,
    movie_details_factory,
) -> None:
    view = MovieDetailsView()

    view.set_movie(
        movie_details_factory(
            year=None,
            runtime_minutes=None,
            rating=None,
            overview=None,
            genres=(),
            media_files=(),
        )
    )

    assert _text(view, "detailsMetaLabel") == "Year unavailable  •  Runtime unavailable  •  Not rated"
    assert _text(view, "detailsGenresLabel") == "Genres unavailable"
    assert _text(view, "detailsOverviewLabel") == "Overview unavailable."
    assert view.media_file_count == 0
    assert _text(view, "mediaEmptyLabel") == "No physical media files are linked."


def test_details_view_can_show_controlled_error(qapp: QApplication) -> None:
    view = MovieDetailsView()

    view.show_error("Movie details are unavailable.")

    assert _text(view, "detailsStateLabel") == "Movie details are unavailable."


def test_details_view_displays_poster_and_ignores_stale_result(
    qapp: QApplication,
    movie_details_factory,
    png_bytes: bytes,
) -> None:
    loader = DeferredPosterLoader()
    view = MovieDetailsView(poster_loader=loader)
    view.set_movie(movie_details_factory(movie_id=1))
    first_receiver, _request, first_token = loader.requests[0]
    view.set_movie(movie_details_factory(movie_id=2, title="Arrival"))
    second_receiver, _request, second_token = loader.requests[1]

    first_receiver.apply_poster(first_token, PosterAsset("png", png_bytes))
    assert view.poster_loaded is False
    second_receiver.apply_poster(second_token, PosterAsset("png", png_bytes))

    assert view.poster_loaded is True


class RecordingLocalActions:
    def __init__(self) -> None:
        self.calls: list[tuple[str, Path]] = []
        self.error: Exception | None = None

    def play(self, media_path: Path) -> None:
        if self.error is not None:
            raise self.error
        self.calls.append(("play", media_path))

    def open_folder(self, media_path: Path) -> None:
        if self.error is not None:
            raise self.error
        self.calls.append(("open_folder", media_path))


def _action_button(view: MovieDetailsView, prefix: str, media_file_id: int) -> QPushButton:
    button = view.findChild(QPushButton, f"{prefix}_{media_file_id}")
    assert button is not None
    return button


def test_each_physical_file_has_explicit_actions_bound_to_that_file(
    qapp: QApplication,
    movie_details_factory,
) -> None:
    actions = RecordingLocalActions()
    view = MovieDetailsView(local_media_actions=actions)
    details = movie_details_factory()
    view.set_movie(details)

    _action_button(view, "playMovieButton", 10).click()
    _action_button(view, "openFolderButton", 11).click()

    assert actions.calls == [
        ("play", Path(details.media_files[0].current_path)),
        ("open_folder", Path(details.media_files[1].current_path)),
    ]
    assert len(view.findChildren(QPushButton)) >= 4


def test_missing_file_and_launch_failure_show_controlled_per_file_feedback(
    qapp: QApplication,
    movie_details_factory,
) -> None:
    actions = RecordingLocalActions()
    view = MovieDetailsView(local_media_actions=actions)
    view.set_movie(movie_details_factory())

    actions.error = MissingMediaFileError("technical path")
    _action_button(view, "playMovieButton", 10).click()
    assert "no longer available" in _text(view, "mediaActionErrorLabel_10").casefold()

    actions.error = LocalMediaLaunchError("technical launcher detail")
    _action_button(view, "openFolderButton", 11).click()
    assert "could not open" in _text(view, "mediaActionErrorLabel_11").casefold()

    actions.error = None
    _action_button(view, "openFolderButton", 11).click()
    assert _text(view, "mediaActionErrorLabel_11") == ""
    assert _action_button(view, "playMovieButton", 10).isEnabled()


def test_single_file_renders_one_action_pair_and_unwired_actions_are_disabled(
    qapp: QApplication,
    movie_details_factory,
) -> None:
    details = movie_details_factory()
    view = MovieDetailsView()

    view.set_movie(movie_details_factory(media_files=(details.media_files[0],)))

    assert len([button for button in view.findChildren(QPushButton) if button.objectName().startswith("playMovieButton_")]) == 1
    assert _action_button(view, "playMovieButton", 10).isEnabled() is False
    assert _action_button(view, "openFolderButton", 10).isEnabled() is False


class RecordingOrganizationActions:
    def prepare_organization(self, media_file_id, destination_root, destination_filename):
        return OrganizationPreview(
            "preview", media_file_id, "source", "destination",
            OrganizationOperation.MOVE, True, 1, "D:\\", "D:\\", (),
        )

    def confirm_organization(self, preview_id):
        return OrganizationResult("operation", 10, "source", "destination", "strategy")

    def discard_organization_preview(self, preview_id):
        pass


def test_each_physical_file_has_its_own_organize_entry_point(
    qapp: QApplication,
    movie_details_factory,
) -> None:
    view = MovieDetailsView(organization_actions=RecordingOrganizationActions())
    view.set_movie(movie_details_factory())

    first = _action_button(view, "organizeFileButton", 10)
    second = _action_button(view, "organizeFileButton", 11)

    assert first.text() == "Organize File"
    assert second.property("mediaFileId") == 11
    assert first.isEnabled() is True


def test_unwired_organize_action_is_disabled(
    qapp: QApplication,
    movie_details_factory,
) -> None:
    view = MovieDetailsView()
    view.set_movie(movie_details_factory())

    assert _action_button(view, "organizeFileButton", 10).isEnabled() is False


def test_organize_button_opens_exact_file_dialog_and_success_requests_refresh(
    qapp: QApplication,
    movie_details_factory,
    monkeypatch,
) -> None:
    created: list[object] = []

    class FakeDialog(QDialog):
        organization_succeeded = Signal(object)

        def __init__(self, actions, **kwargs):
            super().__init__(kwargs["parent"])
            self.actions = actions
            self.kwargs = kwargs
            self.invalidated = False
            created.append(self)

        @property
        def is_executing(self):
            return False

        def invalidate_pending_delivery(self):
            self.invalidated = True

    monkeypatch.setattr(
        "dropsort.ui.movie_details.details_view.OrganizeFileDialog",
        FakeDialog,
    )
    organization = RecordingOrganizationActions()
    view = MovieDetailsView(organization_actions=organization)
    view.set_movie(movie_details_factory(movie_id=7))
    refreshed: list[int] = []
    view.organization_completed.connect(refreshed.append)

    _action_button(view, "organizeFileButton", 10).click()

    dialog = created[0]
    assert dialog.kwargs["media_file_id"] == 10
    assert dialog.kwargs["current_path"] == Path(r"D:\Movies\The Dark Knight.mkv")
    assert dialog.kwargs["file_size"] == 1_500_000_000
    dialog.organization_succeeded.emit(object())
    assert refreshed == [7]
    view.invalidate_pending_organization_delivery()
    assert dialog.invalidated is True
    dialog.done(0)


def test_missing_row_keeps_last_known_path_disables_organize_and_offers_locate(
    qapp: QApplication,
    movie_details_factory,
) -> None:
    class ReconciliationActions:
        pass

    class Runner:
        def submit(self, *args):
            raise AssertionError("opening the dialog does not start validation")

    view = MovieDetailsView(
        organization_actions=RecordingOrganizationActions(),
        reconciliation_actions=ReconciliationActions(),
        reconciliation_runner=Runner(),
    )
    view.set_movie(movie_details_factory())

    assert _action_button(view, "organizeFileButton", 11).isEnabled() is False
    assert _action_button(view, "locateFileButton", 11).isHidden() is False
    assert _action_button(view, "locateFileButton", 10).isHidden() is True
    paths = [
        label.text()
        for label in view.findChildren(QLabel)
        if label.objectName() == "mediaPathLabel"
    ]
    assert any(value.startswith("Last known location: ") for value in paths)


def test_locate_button_opens_exact_relink_dialog_and_success_requests_refresh(
    qapp: QApplication,
    movie_details_factory,
    monkeypatch,
) -> None:
    created = []

    class FakeDialog(QDialog):
        relinked = Signal(object)

        def __init__(
            self,
            actions,
            media_file_id,
            old_path,
            runner,
            parent=None,
            **_kwargs,
        ):
            super().__init__(parent)
            self.media_file_id = media_file_id
            self.old_path = old_path
            self.invalidated = False
            created.append(self)

        def invalidate_pending(self):
            self.invalidated = True

    monkeypatch.setattr(
        "dropsort.ui.movie_details.details_view.RelinkMediaFileDialog",
        FakeDialog,
    )
    actions = object()
    runner = object()
    view = MovieDetailsView(
        reconciliation_actions=actions,
        reconciliation_runner=runner,
    )
    view.set_movie(movie_details_factory(movie_id=9))
    refreshed = []
    view.relink_completed.connect(refreshed.append)

    _action_button(view, "locateFileButton", 11).click()

    dialog = created[0]
    assert dialog.media_file_id == 11
    assert dialog.old_path == Path(r"E:\Archive\The Dark Knight 4K.mkv")
    dialog.relinked.emit(object())
    assert refreshed == [9]
    view.invalidate_pending_organization_delivery()
    assert dialog.invalidated is True
    dialog.done(0)


def test_organization_wait_and_missing_movie_id_success_are_safe(
    qapp: QApplication,
) -> None:
    class WaitRunner:
        def __init__(self):
            self.waited = False

        def submit(self, *args):
            raise AssertionError("no task expected")

        def wait_for_done(self):
            self.waited = True

    runner = WaitRunner()
    view = MovieDetailsView(
        organization_actions=RecordingOrganizationActions(),
        organization_runner=runner,
    )
    delivered: list[int] = []
    view.organization_completed.connect(delivered.append)

    view._organization_finished(None)
    view.wait_for_pending_tasks()

    assert delivered == []
    assert runner.waited is True
