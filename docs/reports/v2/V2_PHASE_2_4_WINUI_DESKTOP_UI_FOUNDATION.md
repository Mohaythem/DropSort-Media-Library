# DropSort Media Library — V2 Phase 2.4

## 1. Scope

Implemented the shared WinUI-inspired desktop UI foundation and local Library search requested for Phase 2.4. The work covers shared geometry, spacing, buttons, header, sidebar semantics, language/theme controls, read-only provider rating presentation, scroll treatment, TMDB setup help, localization/RTL, accessibility, and local-only search. Check Library behavior and its Phase 2.3 implementation were not redesigned or restyled directly.

DropSort remains Python/PySide6/Qt with SQLite. WinUI, XAML, and C# were used only as visual and interaction references; there is no WinUI/XAML/C# runtime dependency.

## 2. WinUI-to-Qt Mapping

WinUI concepts map to existing Qt primitives: `QFrame` and QSS for surfaces and InfoBar-style regions, `QPushButton` for Button/ToggleButton states, `QComboBox` for the theme ComboBox, a checkable two-button `LanguageToggle` for the ToggleSwitch-inspired language control, `QSplitter` and existing navigation buttons for NavigationView behavior, `QLineEdit` plus bounded `QCompleter` suggestions for search, and the native `QMainWindow` caption behavior for the desktop window.

## 3. Geometry Tokens

Shared tokens are defined in `src/dropsort/ui/common/theme.py`: control radius 4 px, overlay/top-level radius 8 px, control height 36 px, and navigation item height 42 px. Existing compatibility aliases remain available.

## 4. Spacing System

The shared spacing scale is 4, 8, 12, 16, 24, 36, and 48 px. Existing small/medium/large aliases map to the shared scale so the current application layout remains compatible.

## 5. Button System

The shared QSS defines rest, hover, pressed, disabled, focused, and checked behavior while preserving DropSort's existing semantic action hierarchy. Blacklist remains a non-destructive state/action and was not treated as a destructive control.

## 6. Language Toggle

Settings now presents an accessible English/Arabic two-state `LanguageToggle` with semantic labels rather than On/Off wording. It uses the existing settings persistence and localization binding, updates live, and preserves RTL behavior. A hidden compatibility ComboBox remains available to existing callers and tests.

## 7. Theme ComboBox

The existing theme ComboBox remains the source of truth and is styled as a WinUI-inspired control. It exposes exactly Main, Dark, Slate, and Light with IDs `main`, `dark`, `slate`, and `light`, and retains existing persistence behavior.

## 8. TMDB Rating Presentation

Provider metadata is displayed read-only in Movie Card and Movie Details views. A TMDB value from 0–10 is shown authoritatively as `x.x / 10` with a visual five-position star treatment derived from `rating / 2`. Missing values use localized unavailable text. No personal rating, `rating_snapshot`, rating editor, or rating persistence was added.

## 9. Sidebar / Navigation

The existing sidebar and resize/collapse architecture remain in place. Navigation items retain their current scope and behavior, while accessible names are localized even when compact mode hides visible labels. No Discover, Trending, Popular, Upcoming, Recommendations, Analytics, Diary, or other deferred navigation was added.

## 10. Title / App Header

A refined app header now sits below the native title bar. It keeps the existing DropSort identity and native caption/window behavior, shows the active section, and provides the Library search field only on Library and Personal Library pages.

## 11. Local Library Search

Search is local-only and operates on already loaded Library or Personal Library items. It matches title, original title, and year where available, provides bounded local suggestions, supports Escape dismissal/clear behavior, and displays the localized “No movies found” state. The UI does not issue SQL or network requests, and no TMDB search was added.

## 12. Scroll Behavior

Existing Qt scroll areas and their behavior were preserved. Shared styling gives scrollbars an 8 px track width and 4 px radius with clean add/sub-line treatment. No fake scroll annotations or decorative affordances were introduced.

## 13. TMDB InfoBar / Setup Help

Settings includes a localized TMDB InfoBar-style help region with an offline setup guide and an official TMDB action. The guide is local text shown through Qt, and the official action opens `https://www.themoviedb.org/` through the system browser. No credentials, API keys, or secrets are requested, displayed, or persisted.

## 14. Localization / RTL

All new/refined controls and states have English and Arabic text IDs, including language accessibility text, rating states, search placeholder/suggestions/no-results text, and TMDB setup help. Technical values such as `x.x / 10` use LTR-safe presentation. Existing RTL switching remains live and localized.

## 15. Accessibility

The language toggle, theme ComboBox, search field, search suggestions, rating visual/value, sidebar items, and TMDB InfoBar actions have accessible names/descriptions or semantic labels. Keyboard Escape behavior closes the search popup before clearing text, preserving predictable focus interaction.

## 16. Themes

Main, Dark, Slate, and Light remain supported. The shared UI foundation uses semantic theme roles and does not copy Microsoft sample colors. Existing DropSort semantic colors remain authoritative.

## 17. Files Created

- `src/dropsort/ui/common/rating.py`
- `src/dropsort/ui/library/search.py`
- `tests/unit/ui/test_phase24_ui_foundation.py`
- `V2_PHASE_2_4_WINUI_DESKTOP_UI_FOUNDATION.md`

Disposable verification outputs also include `.coverage-v2-phase24-final.json`, `.build-phase24-dist`, `.build-phase24-work`, and `.build-phase24-smoke-local` in the workspace.

## 18. Files Modified

- `src/dropsort/ui/common/theme.py`
- `src/dropsort/ui/localization.py`
- `src/dropsort/ui/library/movie_card.py`
- `src/dropsort/ui/movie_details/details_view.py`
- `src/dropsort/ui/library/library_view.py`
- `src/dropsort/ui/personal_library/personal_library_view.py`
- `src/dropsort/ui/settings/settings_view.py`
- `src/dropsort/ui/main_window/window.py`

No database schema, migration, File Engine, journal, recovery, or real-media operation code was modified.

## 19. Tests

Focused Phase 2.4 and related UI verification: `89 passed`.

The focused run covered rating rendering, local title/original-title/year search, bounded suggestions, search Escape behavior, header visibility, language persistence/RTL, TMDB InfoBar localization, existing settings/theme behavior, navigation, Movie Card, Movie Details, localization parity, and UI architecture rules. Compile-time verification with `compileall` also passed.

## 20. Full Regression

Full suite command completed successfully with `1093 passed, 5 skipped` in 127.58 seconds. The five skips are host-specific symlink tests because symlink privilege is unavailable on this Windows host; no test failed.

## 21. Coverage

Branch coverage is `95%` for the full suite (`10,598` statements, `2,212` branches). Coverage JSON was written to `.coverage-v2-phase24-final.json`.

## 22. Packaging

PyInstaller `6.22.0` built the disposable distribution successfully. `DropSort.exe` is 2,426,146 bytes with SHA-256:

`DB52522C8BB263886A0A520B100A93BFEB73A53D9ACF777DFB756212B824D997`

An isolated offscreen launch using disposable `LOCALAPPDATA` created the disposable SQLite database and exited cleanly after the smoke check. Read-only SQLite inspection confirmed migrations 1–4, schema version 4, and zero movies/media/settings rows.

## 23. File-Safety Review

No real media library was scanned or mutated. Search is read-only over already loaded DTOs. No File Engine transfer, journal, recovery, path-policy, or database migration behavior was changed. Packaging and smoke testing used disposable workspace directories and an isolated local application-data root. The SQLite inspection used read-only mode.

## 24. Asset Audit

No new images, illustrations, icons, posters, fonts, or other visual assets were created, generated, downloaded, or added. The package contains only the pre-existing DropSort icon/SVG, TMDB SVG, and bundled fonts already declared by `DropSort.spec`.

## 25. Normal Review

The implementation stays within the requested Python/PySide6/Qt/SQLite architecture, reuses existing localizer/settings/theme/navigation mechanisms, keeps search out of the UI's SQL/network boundary, preserves native window behavior, and keeps provider rating read-only. Existing tests and the full regression suite pass.

## 26. Adversarial Review

Reviewed for accidental scope expansion and unsafe behavior: no network search path, credentials, personal rating storage, schema change, real-library mutation, destructive action, global Escape regression, new media asset, or deferred feature surface was introduced. No Discover, Letterboxd, Analytics, TV, Subtitles, Folder Watcher, Storage Dashboard, Duplicates, Reviews, Tags, Favorites, cloud profile, or recommendations scope was added.

## 27. User Visual Retest

Required: YES. Automated tests validate structure, semantics, localization, and behavior, but do not constitute visual approval. Please visually retest Main/Dark/Slate/Light, English and Arabic/RTL, expanded and compact sidebar, header/search/suggestions, language toggle, theme ComboBox, TMDB star/value presentation, InfoBar actions, scrollbars, and the existing Check Library screen at normal and narrow window sizes.

## 28. Deferred Features

Discover, Trending, Popular, Upcoming, Recommendations, Letterboxd, Analytics, Diary, Reviews, Tags, Favorites, personal ratings, Storage Dashboard, Duplicates, Folder Watcher, TV, Subtitles, cloud profile, new artwork/assets, TMDB search, and schema changes remain explicitly deferred.

## 29. Phase Decision

**PASS for implementation and automated verification; user visual retest remains required.**

DropSort remains Python/PySide6/Qt. WinUI/XAML/C# are visual/interaction references only. No new images/assets were added, no personal numeric rating or `rating_snapshot` was introduced, TMDB rating remains read-only provider metadata, Library search is local-only with no TMDB/network search, and no database migration/schema change was made.
