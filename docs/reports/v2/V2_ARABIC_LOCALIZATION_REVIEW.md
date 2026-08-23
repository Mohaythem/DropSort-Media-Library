# DropSort V2 Arabic Localization Review

Date: 2026-08-16

## Scope

This pass applies the approved Arabic UI copy to the current DropSort catalog. It is a localization-only change. English behavior, application architecture, safety behavior, themes, database schema, personal-library semantics, WatchEvent semantics, file-operation semantics, Undo, recovery, and Clear Library behavior were preserved.

No Check Library layout, navigation, state, counter, or behavior redesign was introduced.

## Arabic copy source

Source of truth: the approved Arabic UI copy prompt supplied at:

`<local request attachment>`

The approved wording was applied as product copy rather than literal machine translation. The intentional terminology includes `مكتبتي` for Personal Library, `إجراءات حساسة` for Danger Zone, `رأيك` for Preference, `استبعاد` for Blacklist, `سجل العمليات` for Operations Log, `فحص المكتبة` for Check Library, and `سليت` for Slate.

## Keys changed

Approved Arabic values were updated across:

- Main window, branding, navigation, Library, Settings, themes, language, and Danger Zone.
- TMDB setup/help/status strings and provider rating states.
- Library search, empty, missing-file, and load-error states.
- Movie Details, personal preference actions, Watchlist, Watch History, and watched-date guidance.
- Personal Library tabs and approved empty states.
- Add Movies, read-only scan controls/progress/results, manual search, and year validation.
- Operations Log commands, empty/save states, operation/status labels, details, and recovery controls.
- Approved Check Library copy only.
- Localized accessibility labels for TMDB rating visuals, operation paths, and the watched-date calendar.

The existing English catalog values were not rewritten. Three pre-existing hardcoded English accessibility labels were represented as new localization keys with the same English wording so the approved Arabic accessibility wording can be applied without hardcoded language exceptions.

## Strings requiring later review

The source contains additional Arabic catalog entries outside the approved copy list. They were intentionally left unchanged and are recorded here for a later copy review rather than silently rewritten:

- Settings/TMDB extras: `VIEW_OPERATION_HISTORY`, `TMDB_ENTER_TOKEN`, `TMDB_INVALID_TOKEN`, `TMDB_READY`, `TMDB_CLEARED`, `ABOUT_CREDITS`, `TMDB_NOTICE`, `TMDB_SOURCE_NOTICE`, `TMDB_SETUP_GUIDE_TITLE`, `TMDB_SETUP_GUIDE_BODY`, `LIBRARY_DATA_NOTICE`.
- Existing Library/Details/Personal and media actions: `FILE_SINGULAR`, `FILE_PLURAL`, `DETAILS_GENRES_UNAVAILABLE`, `DETAILS_OVERVIEW_UNAVAILABLE`, `DETAILS_NO_FILES`, `DETAILS_REMOVED`, `DETAILS_PERSONAL_LOAD_ERROR`, `DETAILS_PERSONAL_SAVE_ERROR`, `DETAILS_WATCH_SAVED`, `PERSONAL_EMPTY_LIKED`, `PERSONAL_EMPTY_LIKED_DESCRIPTION`, `PERSONAL_EMPTY_BLACKLISTED`, `PERSONAL_EMPTY_BLACKLISTED_DESCRIPTION`, `PERSONAL_LOAD_ERROR`, `PERSONAL_NO_LOCAL_COPY`, `STATUS_PRESENT`, `STATUS_MISSING`, `LAST_KNOWN_PATH`, `CURRENT_PATH`, `PLAY_MOVIE`, `OPEN_FOLDER`, `ORGANIZE_FILE`, `LOCATE_FILE`.
- Additional scan/manual-search/import states: `SCANNING_MOVIES`, `CANCELLING_SCAN`, `PREPARING_MATCHES`, `BUILDING_RESULTS`, `INVALID_SCAN_RESULT`, `ADD_TO_LIBRARY`, `OPEN_SETTINGS`, `EDIT_SEARCH`, `DETECTED_TITLE`, `SEARCH_AS`, `NO_RESULTS`, `MANUAL_SEARCH_RESULTS`, `MANUAL_SEARCH_SELECT`, `MANUAL_SEARCH_RATING`, `MANUAL_SEARCH_NO_OVERVIEW`, `MANUAL_SEARCH_SEARCHING`, `MANUAL_SEARCH_PROVIDER_FAILED`, `IMPORT_MANUAL_SELECTED`, `IMPORT_MANUAL_EXPLANATION`, `COPY`, `ADDED_TO_LIBRARY`, `DISMISS_PROPOSAL`, `ALL_DONE`, `NO_MOVIES_WAITING`, `ADDING_TO_LIBRARY`, `SCAN_PROGRESS_METADATA`, `SCAN_PROGRESS_ROWS`, `SCAN_COMPLETE_EMPTY`, all remaining `SCAN_*` states not listed in the approved section, and all remaining `IMPORT_*` states not listed in the approved section.
- Additional Operations Log and recovery states: `HISTORY_LOADING`, `HISTORY_READ_ERROR`, `HISTORY_UNLINKED`, `HISTORY_REVERSE`, `UNDO_PREVIEW`, `UNDO_WARNING`, `UNDO_NO_CHANGE`, `CANCEL`, `CONFIRM_UNDO`, `UNDO_RUNNING`, `HISTORY_LOADING_DETAILS`, `HISTORY_INVALID`, `HISTORY_INVALID_DETAILS`, all `UNDO_*` states not listed in the approved section, `RECOVERY_INVALID`, `RECOVERY_INSPECT_FAILED`, `RECOVERY_COMPLETE`, `HISTORY_FIELD_FILE_SIZE`, and `HISTORY_FIELD_TRANSFER`.
- Additional Check Library states: `CHECK_FILES_CANCELLING`, `CHECK_FILES_FAILED`, `CHECK_FILES_PROGRESS`, `CHECK_FILES_BACKGROUND`, `CHECK_FILES_ALREADY_RUNNING`, `CHECK_FILES_COMPLETE`, `CHECK_FILES_DONE`, `CHECK_FILES_BACKGROUND_CANCELLED`, `CHECK_FILES_BACKGROUND_FAILED`, all `CHECK_LIBRARY_*` states not listed in the approved section, including detailed progress, summary, issue, provider, repair, and failure copy.
- Relink and organize states: all `RELINK_*`, `VIDEO_FILES`, and all `ORGANIZE_*` keys not listed in the approved section.
- Remaining local-media failure states: `MEDIA_MISSING_ACTION`, `PLAY_FAILED`, `OPEN_FOLDER_FAILED`, `BUSY_CLEAR`, `CLEAR_UNAVAILABLE`, `CLEAR_BLOCKED`, `CLEAR_DATABASE`, `CLEAR_FAILED`, and `DETAILS_LOAD_ERROR`.

## Duplicate `scan.folder_dialog` resolution

The current source confirms that `scan.folder_dialog` is the folder-picker title used by `TextId.CHOOSE_MOVIE_FOLDER`, so it remains `اختر مجلد الأفلام`.

The Add Movies button uses the distinct existing `TextId.CHOOSE_FOLDER_SCAN` key with `scan.choose_folder`, so its approved Arabic is `اختيار مجلد وبدء الفحص`. No duplicate catalog key was added.

## Placeholder verification

The focused localization regression compares placeholder sets byte-for-byte between every English and Arabic catalog entry. Approved placeholders such as `{count}`, `{date}`, `{title}`, `{path}`, `{files}`, `{movies}`, `{folders}`, `{ready}`, `{existing}`, and `{errors}` remain unchanged.

## Western numeral verification

The localization tests and existing formatting tests confirm Western digits only. The tested presentation outputs include `123`, `2024`, and `8.5 / 10`; no Eastern Arabic numeral characters `٠١٢٣٤٥٦٧٨٩` are introduced.

Technical values remain direction-safe: movie titles, file names, paths, dates, timestamps, years, IDs, provider values, extensions, sizes, and resolutions are not translated. Existing LTR marking remains in use for technical widgets.

## RTL verification

Arabic localization continues to set application RTL direction. Focused tests verify Arabic RTL and explicit LTR technical widgets. The packaged Arabic startup smoke also launched successfully with the persisted isolated `ui.language=ar` setting.

## Accessibility strings

The following approved labels are now localized through the catalog:

- `accessibility.tmdb_rating_visual`: `عرض تقييم TMDB`
- `accessibility.operation_paths`: `مسارات المصدر والوجهة للعملية`
- `accessibility.watched_date_calendar`: `تقويم تاريخ المشاهدة`

The existing semantic language-toggle accessibility wording remains `اللغة: الإنجليزية أو العربية`.

## Files modified

- `src/dropsort/ui/localization.py`
- `src/dropsort/ui/library/movie_card.py`
- `src/dropsort/ui/movie_details/details_view.py`
- `src/dropsort/ui/history/view.py`
- `tests/unit/ui/test_arabic_localization.py`
- `tests/unit/ui/test_settings_view.py`
- `tests/unit/ui/test_personal_library_ui.py`

No database migration, schema file, production file-operation code, image, or other visual asset was modified.

## Tests

Focused localization/UI regression:

`74 passed`

The focused tests cover approved copy, catalog parity, placeholder parity, RTL, LTR technical presentation, Western numerals, settings language switching, Movie Details, Operations Log, and Check Library UI behavior.

## Full regression

Command used with repository-local basetemp:

`QT_QPA_PLATFORM=offscreen .venv\Scripts\python.exe -m pytest --basetemp .pytest-basetemp-arabic-final --cov=src/dropsort --cov-branch --cov-report=term-missing -q`

Result: `1111 passed, 5 skipped, 0 failed`.

Branch coverage: `95%`.

The five skips are the existing Windows symlink-privilege skips; no new skip was introduced.

## Packaging

Existing `DropSort.spec` workflow completed with PyInstaller `6.22.0`.

Executable:

`D:\DropSort_ chat\DropSort\.build-arabic-dist\DropSort\DropSort.exe`

Size: `2,434,947` bytes

SHA-256: `9B90DF1864FE675BD7FA36DE2A9B41CCD4F315E2C74C19546F0D979F6E7540AC`

An isolated offscreen English launch and an isolated offscreen Arabic launch both remained alive during the bounded startup check. The smoke database was under `.build-arabic-smoke-local-final\isolated-localappdata`; no real LocalAppData, credentials, or media library was touched.

## Schema

The packaged smoke database reports four rows in `schema_migrations`, maximum logical migration version `4`, and zero rows in movies, media files, watch events, and file operations. `PRAGMA user_version` remains `0`, matching the existing project baseline; the logical schema is migration v4. No schema change or migration was introduced.

## File-safety review

The pass only changes localized presentation and accessibility lookup. It does not mutate physical media, invoke real media operations, change PathGuard/SafeTransferEngine behavior, alter journal lifecycle, or change recovery/Undo semantics. Package smoke used a disposable LocalAppData path only.

## Asset audit

No new images/assets were added, downloaded, or generated. The rebuilt package contains the existing DropSort icon/SVG, TMDB SVG, existing Inter fonts, and existing Noto Sans Arabic fonts declared by `DropSort.spec`.

## Check Library confirmation

Check Library was not redesigned. Only the approved Arabic strings for its existing title, readiness, running, cancellation, issue, result, outcome, and retry labels were applied. Layout, navigation, state handling, counters, and behavior remain unchanged.

## User visual retest

Automated construction, RTL, accessibility, Western-numeral, and packaged startup checks passed. A user visual retest remains required for final visual approval, especially for Arabic text fit in navigation, Settings/Danger Zone, Movie Details, Personal Library, Add Movies, Operations Log, and Check Library.

## Decision

PASS — approved Arabic copy was applied as a bounded localization pass and the full automated regression remains green at the required `95%` branch coverage threshold. Stop after this localization pass; no additional roadmap phase was started.

## Required confirmations

- English UI source copy was not rewritten.
- Approved Arabic wording from the supplied source was applied.
- Arabic support remains enabled and RTL.
- Western numerals remain mandatory in Arabic UI.
- Placeholders were preserved exactly.
- No personal numeric rating was introduced.
- No schema change or migration was introduced.
- No physical media mutation behavior was changed.
- Check Library was not redesigned.
- No new images/assets were added.
- DropSort remains Python/PySide6/Qt.
