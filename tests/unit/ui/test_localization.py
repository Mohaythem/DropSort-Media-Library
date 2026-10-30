from __future__ import annotations

from PySide6.QtCore import QCoreApplication, QEvent, Qt
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


def test_destroyed_bound_widget_is_unregistered_before_language_refresh(qapp: QApplication) -> None:
    localizer = UiLocalizer()
    label = QLabel()
    localizer.bind_text(label, TextId.NAV_LIBRARY)
    binding_count = len(localizer._bindings)

    label.deleteLater()
    QCoreApplication.sendPostedEvents(None, QEvent.Type.DeferredDelete)
    qapp.processEvents()

    assert len(localizer._bindings) < binding_count
    localizer.set_language(UiLanguage.ARABIC)


def test_rebinding_live_widget_reuses_one_destroyed_lifecycle_hook(qapp: QApplication) -> None:
    localizer = UiLocalizer()
    label = QLabel()
    localizer.bind_text(label, TextId.NAV_LIBRARY)
    first_count = len(localizer._bindings)
    localizer.bind_text(label, TextId.NAV_SETTINGS)

    assert len(localizer._bindings) == first_count == 1
    assert label.text() == localizer.text(TextId.NAV_SETTINGS)


def test_destroyed_retranslator_owner_is_unregistered_before_refresh(
    qapp: QApplication,
) -> None:
    localizer = UiLocalizer()

    class RetranslatableLabel(QLabel):
        def refresh(self, _language) -> None:
            self.setToolTip(localizer.text(TextId.NAV_LIBRARY))

    label = RetranslatableLabel()
    localizer.bind_retranslator(label, label.refresh)
    assert len(localizer._retranslators) == 1

    label.deleteLater()
    QCoreApplication.sendPostedEvents(None, QEvent.Type.DeferredDelete)
    qapp.processEvents()
    localizer.set_language(UiLanguage.ARABIC)

    assert localizer._retranslators == {}


def test_repeated_navigation_language_switches_do_not_accumulate_dead_bindings(
    qapp, movie_details_factory
) -> None:
    from dropsort.ui.main_window.window import MainWindow

    class LibraryActions:
        def list_movies(self):
            return ()

        def get_movie_details(self, _movie_id):
            return movie_details_factory()

    class PersonalActions:
        def list_personal_movies(self, _section):
            return ()

        def get_personal_snapshot(self, _movie_id):
            return None

    class SettingsActions:
        def metadata_credential_status(self):
            from dropsort.application.configuration.metadata_credentials import (
                MetadataCredentialOrigin,
                MetadataCredentialStatus,
            )

            return MetadataCredentialStatus(False, MetadataCredentialOrigin.NOT_CONFIGURED)

        def current_ui_language(self):
            return UiLanguage.ENGLISH

    window = MainWindow(
        LibraryActions(),
        personal_actions=PersonalActions(),
        settings_actions=SettingsActions(),
        import_actions=object(),
        load_on_show=False,
    )
    baseline = len(window._localizer._bindings)
    for language in (
        UiLanguage.ARABIC,
        UiLanguage.ENGLISH,
        UiLanguage.ARABIC,
        UiLanguage.ENGLISH,
    ):
        for show in (
            window.show_settings,
            window.show_personal_library,
            window.show_check_library,
            window.show_import,
            window.show_library,
            lambda: window.show_movie_details(1),
            window.show_library,
        ):
            show()
            window._localizer.set_language(language)
            qapp.processEvents()
    QCoreApplication.sendPostedEvents(None, QEvent.Type.DeferredDelete)
    qapp.processEvents()

    import shiboken6

    dead = [
        binding_id
        for binding_id, (reference, _key, _values) in window._localizer._bindings.items()
        if reference() is None or not shiboken6.isValid(reference())
    ]
    assert dead == []
    assert len(window._localizer._bindings) >= baseline
    window.close()
