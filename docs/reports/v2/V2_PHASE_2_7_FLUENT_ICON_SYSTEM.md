# V2 Phase 2.7 — Fluent UI Icon System Migration

## 1. Scope

Phase 2.7 migrated DropSort's existing UI icon usage to one centralized Microsoft
Fluent UI System Icons registry. The existing navigation architecture, labels,
business behavior, database, file operations, and Check Library page behavior
were preserved.

## 2. Existing Icon Audit

The audit found one existing application identity icon, Qt standard icons in the
main sidebar, a small number of Unicode fallback symbols, and many text-only
buttons. The migrated locations now request semantic icons through the central
registry. DropSort identity and TMDB attribution SVGs remain separate brand or
attribution assets, not UI system icons.

## 3. Icon Loading Strategy

`src/dropsort/ui/common/icon.py` now owns `FluentIconName`, the semantic asset
map, SVG loading, palette-aware rendering, 16px sizing, disabled-state
rendering, and theme refresh. Widgets do not contain raw SVG paths or asset
filenames. Registered widgets retain `dropsortIconName` so MainWindow can
re-render them after a theme change.

## 4. Fluent Asset Strategy

The minimum approved 16px Filled SVG assets were obtained from the official
Microsoft Fluent UI System Icons repository. Official paths and 16x16 viewBoxes
were preserved; the monochrome source color token was adapted to `currentColor`
for Qt palette rendering. No rasterization or generated artwork was used.

## 5. Semantic Icon Registry / Mapping

The registry covers Library, Personal Library/Grid, Add Movies/Folder Add,
Check Library/Folder Search, Settings, Sidebar Toggle/Panel Left, Back/Arrow
Left, Search, Like/Heart, Blacklist/Prohibited, Watchlist/Bookmark, Mark
Watched/Checkmark Circle, Date Picker/Calendar LTR, Play/Play Circle, Open
Folder, Organize File/Folder Arrow Right, Delete, Refresh, Copy, Save,
Operation Details/Document Bullet List, Warning, Failed/Error Circle, Info,
External Link/Open, and Clear Library/Broom.

## 6. Sidebar / Navigation Geometry

Navigation controls retain the existing 42px row contract, 4px control radius,
theme-driven selected/hover states, and compact 44px icon-only mode. Sidebar
icons use 16x16 assets and remain vertically centered by the existing Qt
control layout. Check Library remains a permanent sidebar/page-stack
destination and uses Folder Search 16 Filled.

## 7. Button / Toolbar Geometry

Ordinary controls use the existing 36px control system and 16px icons. Existing
theme padding and Qt style-managed icon/text spacing remain intact; no arbitrary
per-widget offsets or layout redesign were introduced. Icon-only controls retain
accessible names or tooltips where applicable.

## 8. RTL / Directional Icon Handling

Icons are palette-rendered without automatic mirroring. Back uses the approved
Arrow Left asset and remains a semantic fixed-direction asset in the current
navigation model. Calendar uses Calendar LTR and the date control remains
explicitly LTR for technical/date presentation. Arabic localization continues
to control text/layout direction without mirroring non-directional icons.

## 9. Theme Compatibility

Main, Dark, Slate, and Light remain supported. Normal and disabled icon pixmaps
are rendered from the active widget palette rather than hardcoded black or
white. MainWindow refreshes registered icons after applying a theme.

## 10. Accessibility

Existing accessible names and localized labels were preserved. Icon-only
refresh, copy, save, calendar, dismiss, back, open-folder, and related controls
retain semantic names/tooltips. Status icons are supplemental to localized
status text and are not the sole communication channel.

## 11. Icon Inventory — Old → New

- Qt standard sidebar icon → Library, Grid, Folder Add, Folder Search, Settings.
- No icon on the permanent Check Library navigation item → Folder Search.
- Unicode `←` Back label → Arrow Left icon plus localized `Back` text.
- Unicode calendar `▦` → Calendar LTR icon.
- Unicode dismiss `×` → Delete icon.
- Library Check button → Folder Search icon.
- Check Library page/dialog start actions → Folder Search icon.
- Personal Like / Blacklist → Heart / Prohibited.
- Personal clear preference → Delete.
- Watchlist / Mark Watched → Bookmark / Checkmark Circle.
- Play / Open Folder / Organize / Locate → Play Circle / Folder Open / Folder Arrow Right / Search.
- Remove watched event/history item → Delete.
- History Refresh / Copy / Save / Details → Arrow Clockwise / Copy / Save / Document Bullet List.
- History Completed / Needs Attention / Failed status → Checkmark Circle / Warning / Error Circle.
- Import add/search/settings/dismiss → Folder Add / Search / Settings / Delete.
- Organization choose/refresh/confirm → Folder Open / Arrow Clockwise / Folder Arrow Right.
- TMDB info/open/apply/clear → Info / Open / Save / Delete.
- Clear Library → Broom.

## 12. Official Assets Added

Added 26 official semantic SVG assets under
`src/dropsort/ui/assets/fluent/`: `library`, `personal_library`, `add_movies`,
`check_library`, `settings`, `panel_left`, `back`, `search`, `like`,
`blacklist`, `watchlist`, `mark_watched`, `date_picker`, `play`, `open_folder`,
`organize`, `delete`, `refresh`, `copy`, `save`, `operation_details`, `warning`,
`failed`, `info`, `external_link`, and `clear_library`.

## 13. Exceptions / Substitutions

The current MainWindow source has no existing Sidebar Toggle widget, so
`panel_left.svg` is registered and packaged but no new toggle architecture was
invented. Close/cancel controls have no approved mapping and remain text-only.
The manual-search Select action uses the approved Checkmark Circle asset as the
closest semantic confirmation action. No official icon was replaced by a
custom path.

## 14. Files Created

- `src/dropsort/ui/assets/fluent/*.svg` — 26 official Fluent SVG assets.
- `licenses/Microsoft-Fluent-UI-System-Icons-LICENSE.txt`.
- `licenses/Microsoft-Fluent-UI-System-Icons-NOTICE.txt`.
- `tests/unit/ui/test_phase27_fluent_icons.py`.
- `V2_PHASE_2_7_FLUENT_ICON_SYSTEM.md`.

## 15. Files Modified

- `src/dropsort/ui/common/icon.py`.
- `src/dropsort/ui/main_window/window.py`.
- `src/dropsort/ui/library/library_view.py`.
- `src/dropsort/ui/reconciliation/page.py`.
- `src/dropsort/ui/reconciliation/dialogs.py`.
- `src/dropsort/ui/movie_details/details_view.py`.
- `src/dropsort/ui/history/view.py`.
- `src/dropsort/ui/organization/dialog.py`.
- `src/dropsort/ui/scan/import_review_row.py`.
- `src/dropsort/ui/scan/import_view.py`.
- `src/dropsort/ui/scan/manual_search_dialog.py`.
- `src/dropsort/ui/scan/manual_search_result_card.py`.
- `src/dropsort/ui/settings/settings_view.py`.
- `src/dropsort/ui/localization.py`.
- `DropSort.spec`.
- `THIRD_PARTY_NOTICES.md`.

## 16. Focused Tests

Focused Phase 2.7, Phase 2.6, sidebar, and icon architecture checks passed:
`16 passed` for the focused UI run and `9 passed` for the icon/architecture
verification run. Tests cover registry completeness, native 16px SVG structure,
palette rendering, semantic attachment, permanent Check Library navigation,
and architecture restrictions against unsafe UI dependencies.

## 17. Full Regression

Repository-local full run:

`1124 passed, 5 skipped, 0 failed`.

The five skips are existing Windows symlink-privilege skips. No new skip was
introduced.

## 18. Branch Coverage

Branch coverage is `95%` for the full repository run. `icon.py` reached 95%
branch-aware file coverage in that run.

## 19. Packaging

PyInstaller built successfully with the existing `DropSort.spec` into the
isolated `.build-phase27-dist` directory. The package contains all 26 Fluent
SVG assets under `_internal/dropsort/ui/assets/fluent`.

The packaged executable SHA-256 is:

`F5446EC31E730763B76253B9C0F3BEB206C651B8878F0F2DCE67970932B1667C`

Smoke startup remained alive for the eight-second check and was then stopped
by the test harness. It created only an isolated database and empty log under
`.build-phase27-smoke-local`.

## 20. Schema

No schema or migration file changed. The logical schema remains v4: the isolated
smoke database contains migration rows 1 through 4, `PRAGMA user_version = 0`,
zero movies, and zero media files.

## 21. File-Safety Review

No real media-library path was accessed or mutated. This phase added only UI
icon rendering, asset packaging, tests, and documentation. PathGuard,
SafeTransferEngine, operation journaling, undo, recovery, reconciliation,
matcher, TMDB, and database behavior were not changed.

## 22. Asset / License Audit

All added UI assets are official Microsoft Fluent UI System Icons sourced from
the [Microsoft Fluent UI System Icons repository](https://github.com/microsoft/fluentui-system-icons).
They are vector SVGs only, use the native 16px viewBox, and have no generated
or raster companion files. Microsoft license and notice files are included and
referenced from `THIRD_PARTY_NOTICES.md`.

## 23. Normal Review

DropSort remains Python/PySide6/Qt. No WinUI/XAML/C# runtime dependency was
introduced. Only authorized official Fluent SVG icon assets were added. No
generated images, decorative illustrations, PNG/JPG icon exports, emoji, or
unrelated visual assets were added. The icon abstraction is small and shared;
widgets request semantic names rather than raw paths.

## 24. Adversarial Review

The implementation was checked for hardcoded icon colors, mixed Qt standard
icons, Unicode action fallbacks, missing bundled assets, disabled rendering,
theme refresh behavior, unsafe UI imports, and schema/file-operation changes.
The architecture guard tests and full regression passed. No destructive Git
operation, credential access, real media mutation, or database migration was
used.

## 25. User Visual Retest

`USER VISUAL RETEST REQUIRED: YES`

Please visually retest Main, Dark, Slate, and Light; English/LTR and
Arabic/RTL; expanded and compact sidebar; Library, Personal Library, Add
Movies, Check Library, Settings; movie actions; history controls/statuses; and
destructive actions. Confirm there are no clipping, baseline, or perceived
icon/text-gap issues on the actual display.

## 26. Phase Decision

`PASS`

The Fluent 16 Filled icon system is implemented and verified without changing
DropSort's navigation architecture or behavior. The 4px control radius, 8px
overlay radius, existing 42px navigation geometry, four themes, Arabic/RTL,
schema v4, and file-safety boundaries remain intact. Check Library behavior was
not redesigned by this icon phase.

Required confirmations:

- DropSort remains Python/PySide6/Qt.
- No WinUI/XAML/C# runtime dependency was introduced.
- Only authorized official Fluent SVG icon assets were added.
- No generated images, decorative illustrations, PNG/JPG icon exports, emoji, or unrelated visual assets were added.
- Default ordinary icon size is 16x16px.
- Sidebar/navigation rows follow the existing 42px WinUI-inspired geometry.
- The 4px control radius / 8px overlay radius design system remains intact.
- Main, Dark, Slate, and Light remain supported.
- Arabic/RTL remains supported.
- No database schema or migration change was introduced.
- No file-operation, recovery, Undo, matcher, TMDB, or business logic was changed.
- Check Library behavior was not redesigned by this icon phase.
