# DropSort V2 Phase 2.9 — Windows Navigation Shell + Visual Repair

## Scope

Implemented the Phase 2.9 UI-only pass in the current workspace. Python, PySide6, SQLite, local-first behavior, file safety, metadata semantics, and the existing schema were preserved.

## Reference Screenshots Used

Used the two user-supplied Windows-style navigation references:

- `<local screenshot reference 1>`
- `<local screenshot reference 2>`

The implementation follows the shell relationships and density, without copying the sample branding or palette.

## Sidebar Architecture

The left shell now owns the Back action, text-only DropSort identity, pane toggle, sidebar search, primary navigation, Check Library, and bottom Settings placement. The content header is intentionally reduced to a stable section heading.

## Back Placement

Added `sidebarBackButton` using the existing `navigate_back()` path. It is disabled when there is no meaningful destination and enabled for Movie Details and Check Library contexts with a valid previous library context. The visible Movie Details Back control is retained only for compatibility and hidden.

## Pane Toggle

Added `sidebarPaneToggleButton` with the existing Fluent `PANEL_LEFT` icon. It toggles the existing splitter between the persisted expanded width and the compact width. Expanded mode shows labels and search; compact mode centers 42px navigation rows and hides labels.

## Sidebar Search

Moved `librarySearchInput` into the sidebar. It retains local title/original-title/year search and suggestion behavior. Compact mode exposes `sidebarSearchButton`, which opens a temporary `compactLibrarySearchInput` popup. The former full-width header search was removed.

## Header Stability

The top content header now contains only the current section heading and stable spacing. Duplicate app icon/brand and full search controls were removed from the content header.

## Sidebar Geometry

Shared metrics remain 4/8/12/16/24/36/48px, with 36px ordinary controls, 42px navigation rows, 16px icons, and 8px icon/text spacing. Compact shell actions use the shared 36px control height.

## Check Library Visual Redesign

Check Library remains a permanent stacked page and sidebar navigation item. Its idle/running/terminal layouts now keep the upper content compact, cap issue content at 220px, and leave remaining space below the action row. Existing progress, cancellation, summary, issue, and reconciliation semantics are unchanged.

## Movie Surface Fix

Added explicit semantic background roles for the Movie Details scroll surface/content and compact search popup. Slate continues to use distinct background, surface, raised-surface, card, and sidebar roles; no palette or theme identity was replaced.

## Movie Details Spacing

Movie Details now has explicit content/header/metadata spacing and 8px date-row spacing. Shared button/control metrics provide 36px breathing room while preserving the existing actions and signals.

## CalendarDatePicker UX

The watched-date field is now placeholder-first: it starts at the internal empty sentinel `1900-01-01` and displays localized `Pick a date` / `اختر تاريخًا`. A real date is required before recording a dated watch event. Western numerals and technical LTR layout remain enforced.

## Calendar Popup Styling

The existing native Qt calendar popup remains in use and receives the existing semantic surface, border, selected-state, and focus styling. No custom date widget or date semantics were introduced.

## TMDB Settings Simplification

The informational/setup controls and token session controls now sit in one `tmdbSettingsCard`. Token masking, save/clear behavior, setup guide, official-link action, and credential semantics are unchanged.

## Fluent Icon Compatibility

Phase 2.7 icon names and the centralized icon helper remain the source of truth. This phase added only existing semantic icons for Back, pane toggle, and compact search.

## Themes

The shell and surface selectors use the existing semantic theme roles across Main, Dark, Slate, and Light. No new theme or decorative bitmap asset was added.

## Arabic/RTL

Arabic localization was preserved, including the new date placeholder. Technical search/date values remain LTR/Western-digit controls. The shell responds to the existing application RTL direction and localization refresh signals.

## Accessibility

Shell controls have object names, accessible names/descriptions, localized nav names/tooltips in compact mode, disabled-state semantics, focus-visible styling, and keyboard-safe behavior. Escape/modal precedence remains unchanged.

## Files Created

- `tests\unit\ui\test_phase29_windows_shell.py`
- `V2_PHASE_2_9_WINDOWS_NAVIGATION_AND_VISUAL_REPAIR.md`
- `coverage-phase29.json`
- `.build-phase29-dist\DropSort\DropSort.exe` and its packaged distribution

## Files Modified

- `src\dropsort\ui\main_window\window.py`
- `src\dropsort\ui\common\theme.py`
- `src\dropsort\ui\movie_details\details_view.py`
- `src\dropsort\ui\reconciliation\page.py`
- `src\dropsort\ui\settings\settings_view.py`
- `src\dropsort\ui\localization.py`
- `tests\unit\ui\test_sidebar.py`
- `tests\unit\ui\test_personal_library_ui.py`

## Focused Tests

`57 passed` before the final compact-popup metric correction; the final Phase 2.9 test file passed `5 passed`, and the final combined focused set was rerun during implementation.

## Full Regression

`1134 passed, 5 skipped, 0 failed`.

The five skips are expected host symlink-privilege skips documented by the test suite.

## Branch Coverage

Coverage.py final combined metric: `95%` (`11492` statements, `2370` branches, `289` partial branches). Raw branch-only coverage reported by the same run is `86%`; the project’s prior phase reports used the combined displayed coverage metric. This distinction is recorded explicitly.

## Packaging

PyInstaller `6.22.0` built successfully from the current source using `DropSort.spec`, with the bundled migrations, Fluent SVGs, fonts, TMDB asset, DropSort identity assets, licenses, README, and notices.

## Official Release Artifact

Updated the official project artifact at:

`D:\DropSort_ chat\DropSort\release\DropSort\DropSort.exe`

The final official one-directory artifact contains 221 files.

## Real Device Visual Inspection

`BLOCKED / approval still required.` The exact final official executable was launched normally with an isolated repository-local `LOCALAPPDATA` root. The process remained alive, exposed the window title `DropSort Media Library`, created only an isolated empty database/log root, and closed cleanly. The available Windows Computer Use screenshot helper failed to initialize in this device context, so no pixel-level screenshot inspection of the packaged release was completed. This is not reported as a visual PASS.

## What Was Visually Inspected

The supplied reference screenshots were inspected for shell structure and density. The packaged executable’s process/window-title launch was verified, but its rendered pixels were not captured. User visual approval is still required for the final release.

## Schema

No schema or migration files were changed. Read-only smoke inspection of the isolated packaged database found migrations `1, 2, 3, 4`, zero movies, and zero media files. Schema remains v4 by the project migration set.

## File-Safety Review

No real media-library path was opened or mutated. Search remains local catalog search. The packaged smoke used only `.build-phase29-smoke-local` under the workspace. The official release directory was intentionally replaced as the explicitly requested release-artifact update.

## Asset Audit

No new decorative assets were added. The package retains the existing 26 Fluent SVG assets, bundled Inter and Noto Sans Arabic fonts, TMDB attribution asset, DropSort SVG/ICO, migrations, notices, and licenses.

## Normal Review

The implementation is scoped to the Phase 2.9 UI shell, spacing, surfaces, date-picker presentation, and settings grouping. Existing use-case contracts, repository access, filesystem safety, and metadata/provider behavior were not changed.

## Adversarial Review

Checked compact/expanded search synchronization, repeated popup opening, disabled Back with no previous destination, Check Library back context, hidden duplicate Details Back, placeholder date guarding, Arabic technical LTR behavior, theme refresh icon retention, and isolated packaged startup. No direct UI filesystem/SQL/network access was introduced.

## Remaining Visual Issues

- Pixel-level packaged-release inspection remains outstanding because the Windows screenshot helper was unavailable.
- Raw branch-only coverage is 86%, despite the project combined coverage metric reaching 95%.
- Final English/Arabic/theme visual approval on the exact official executable remains required.

## Phase Decision

`BLOCKED_PENDING_VISUAL_APPROVAL_AND_STRICT_BRANCH_GATE`.

Implementation, focused tests, full regression, packaging, isolated startup, schema preservation, and file-safety checks are complete. Phase 2.9 must not be called PASS until the exact official executable is visually inspected and the project accepts the documented strict branch-coverage interpretation.
