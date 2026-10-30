# V2 Phase 2.8 — Typography, Spacing & Navigation Polish

## 1. Scope

This phase applies a compact desktop typography, spacing, alignment, control
density, and Windows-style navigation polish pass after Phases 2.6 and 2.7.
Navigation destinations, Check Library semantics, business logic, database
behavior, file safety, and Fluent icon choices remain unchanged.

## 2. UI Inventory

Audited the shared theme, font bootstrap, MainWindow header/sidebar/search,
Library, Personal Library, MovieCard, Movie Details, Add Movies/manual search,
Check Library page, Operations Log, Settings/Danger Zone, TMDB InfoBar,
DatePicker, dialogs, empty/status states, localization/RTL, and the Phase 2.7
Fluent icon helper.

## 3. Typography Audit

The Phase 2.7 baseline used an oversized hierarchy: body 16px, page title 32px,
screen heading 28px, section heading 21px, movie title 17px, and heading weight
700. The H1-H5 scale ranged from 21.28px to 67.36px, which was too large for
the compact desktop application.

## 4. Final Typography Roles

- Body: 14px Regular.
- Page/screen title: 24px Semibold.
- Dialog title role: 20px Semibold.
- Section heading: 16px Semibold.
- Item/card title: 14px Semibold.
- Compact/control text: 13px where navigation requires it.
- Metadata/small text: 12px Regular.
- Existing Inter and Noto Sans Arabic family strategy is preserved.
- Existing font files remain bundled; no new font dependency was added.

## 5. Spacing Audit

The shared scale was already present and remains authoritative. The main issues
were excessive page/section gaps, a 16px sidebar rhythm, a 36/30px Check
Library page margin, and un-tokenized manual-search card/dialog spacing.

## 6. Final Spacing Tokens

The final scale remains `4 / 8 / 12 / 16 / 24 / 36 / 48`. Added explicit
`ICON_SIZE = 16` and `ICON_TEXT_GAP = 8` geometry tokens. Repeated page and
component spacing now uses these tokens rather than parallel 2/10/14/18px
values. Header and sidebar internal rhythm uses 8px; structural page spacing
uses 16px or 24px.

## 7. Final Control Metrics

- Ordinary buttons: 36px minimum height through the shared stylesheet.
- Search input: explicit 36px minimum height.
- ComboBox, LineEdit, and DateEdit: 36px stylesheet metric.
- Navigation rows: 42px.
- Ordinary Fluent icons: 16x16px.
- Icon-only QToolButton hit areas: 28px minimum, preserving a usable target.
- Control/navigation radius: 4px.
- Overlay/top-level radius: 8px.

## 8. Windows Sidebar / Navigation Polish

MainWindow keeps the Phase 2.6 destinations and Phase 2.7 icons. Expanded
navigation uses 42px rows, 13px labels, 16px icons, compact 8px row rhythm,
and existing semantic selected/hover states. Compact mode still uses 44px
hit areas, centered 16px icons, no label padding, and localized tooltips.
No destination was moved and no new Sidebar Toggle architecture was invented.

## 9. Header / Search

The header now uses shared 16px/8px margins and an 8px internal rhythm. The
local search remains semantically unchanged, retains the Phase 2.7 Search icon,
and now has an explicit 36px minimum control height. Query scope, suggestions,
Escape behavior, and local-only search behavior were not changed.

## 10. Library / Movie Cards

Library page section rhythm was tightened from 24px repeated gaps to 16px while
retaining 24px page padding. MovieCard fixed geometry remains unchanged to
preserve long-title stability. Titles are now 14px Semibold; year, file count,
availability, and rating metadata use the 12px secondary treatment. Poster to
text spacing remains on the shared 8px relation.

## 11. Movie Details

The established details grouping remains intact. The normalized global roles
now provide a 24px details/page title, 16px section headings, 14px body text,
12px metadata, and 36px equivalent controls. DatePicker controls retain their
LTR technical behavior, 16px Fluent calendar icon, 36px control metric, and
existing watch-event semantics.

## 12. Check Library Page

The persistent Phase 2.6 page remains the only normal Check Library page path;
the old dialog was not redesigned or restored. Its page margins now use
24px horizontal / 16px vertical padding, 16px structural rhythm, 12px issue
row padding, and shared 8px issue/button spacing. Idle, running, Passed, Needs
attention, failure, cancellation, and retry semantics remain unchanged.

## 13. Operations Log

Operations Log remains information-dense and readable. The shared typography
reduces title/status scale to the compact hierarchy while preserving full-path
details, Copy, Save, Refresh, Details, latest-500 policy, journal retention,
undo, and recovery behavior. Status text and Phase 2.7 status icons remain
semantically distinct and are not color-only.

## 14. Settings / Danger Zone

Settings keeps its established panel/card structure. Global rows now follow
14px setting text, 12px secondary descriptions, and 36px controls. Danger Zone
remains a restrained isolated surface with its existing border/accent treatment;
the whole Settings page was not recolored red. Clear Library confirmation and
backend behavior are unchanged.

## 15. Add Movies

Add Movies retains its heading, guidance, folder selection, scan controls,
progress, review rows, error/help text, and matching/import behavior. Manual
search cards now use shared 12px padding, 8px internal gaps, and 16px metadata
separation rather than unshared 14/10/18px values. The search dialog uses 24px
outer padding, 16px layout rhythm, and 4/8px result-host spacing.

## 16. Common Dialogs

Common dialogs inherit the normalized 14px body and 20px dialog-heading role.
Existing tokenized 24px outer padding, 16px layout rhythm, 12px form spacing,
and 8px action spacing are preserved where already established. The old Check
Library dialog remains explicitly excluded from visual optimization.

## 17. Empty / Status States

The Personal Library empty state no longer uses the Unicode `✦` fallback. It
uses the existing Phase 2.7 Personal Library Fluent icon through the centralized
helper, with theme refresh support and a 16px vector rendering. Empty-state
spacing remains compact and no illustration was added. Status text continues
to accompany success, warning, error, and disabled states.

## 18. Arabic / RTL

Arabic remains RTL and existing localization/technical LTR handling is
preserved. Sidebar, Check Library, Personal Library, search, DatePicker,
Settings, Operations Log, and Movie Details construction were covered by the
existing Arabic tests plus a Phase 2.8 Arabic sidebar metric test. No Arabic
wording was changed.

## 19. Western Numerals

No numeric localization code was changed. Dates, years, times, ratings, counts,
percentages, runtime, file sizes, IDs, progress, and paths retain Western
digits and existing LTR-safe handling.

## 20. Themes

Main, Dark, Slate, and Light remain the same user-facing themes and palettes.
Typography/spacing metrics are theme-independent. New primary, secondary, and
tertiary text roles use existing semantic foreground tokens; important text was
not made dependent on low-opacity gray or theme-specific hardcoded colors.

## 21. DPI / Scaling

Automated construction and metric tests were run with the repository's Qt
offscreen test configuration. A separate rendered 100%/125%/150% Windows DPI
visual inspection was not claimed; actual DPI and display rendering remain part
of the required user visual retest.

## 22. Accessibility

Accessible names, localized labels, focus indicators, compact-sidebar tooltips,
keyboard navigation, disabled states, and status text were preserved. The
Personal Library empty icon is decorative/supporting content and does not
replace its accessible empty-state name or text.

## 23. Magic-Number Cleanup

Reusable values now include typography, spacing, control height, navigation
height, icon size, icon/text gap, and radius tokens. Representative repeated
raw metrics in MainWindow, Check Library, manual search, and card layouts were
replaced with shared tokens. One-off stable dimensions such as card geometry,
poster size, window sizing, and 44px compact hit areas were intentionally kept.

## 24. Files Created

- `tests/unit/ui/test_phase28_typography_spacing.py`.
- `V2_PHASE_2_8_TYPOGRAPHY_SPACING_AND_NAVIGATION_POLISH.md`.

## 25. Files Modified

- `src/dropsort/ui/common/theme.py`.
- `src/dropsort/ui/common/icon.py`.
- `src/dropsort/ui/main_window/window.py`.
- `src/dropsort/ui/library/library_view.py`.
- `src/dropsort/ui/personal_library/personal_library_view.py`.
- `src/dropsort/ui/reconciliation/page.py`.
- `src/dropsort/ui/scan/manual_search_dialog.py`.
- `src/dropsort/ui/scan/manual_search_result_card.py`.
- `tests/unit/ui/test_theme.py`.

## 26. Before / After Evidence

Measured source values before this phase and final values:

| Component | Before | After |
|---|---:|---:|
| Body text | 16px | 14px |
| Heading weight | 700 | 600 |
| Page title | 32px | 24px |
| Screen heading | 28px | 24px |
| Section heading | 21px | 16px |
| Movie/card title | 17px | 14px |
| H1/H2/H3/H4/H5 | 67.36/50.56/37.92/28.48/21.28px | 28/20/20/16/14px |
| Sidebar layout spacing | 16px | 8px |
| Search minimum height | stylesheet only / 0 explicit | 36px explicit |
| Check Library page margins | 36px horizontal / 30px vertical | 24px / 16px |
| Check Library page spacing | 12px | 16px |
| Manual result-card spacing | 14/12px margins, 6px gap | 12px margins, 8px gap |

## 27. Focused Tests

Phase 2.8 and relevant regression-focused tests passed:

`58 passed, 0 failed`.

Coverage includes typography tokens, semantic text roles, 36px/42px/16px/8px
geometry, expanded/compact navigation construction, English/Arabic fit,
Unicode fallback removal, Phase 2.7 icon integration, Personal Library,
MovieCard, and MainWindow behavior.

## 28. Full Regression

Repository-local full run:

`1129 passed, 5 skipped, 0 failed`.

The five skips are existing Windows symlink-privilege skips. No new skip was
introduced.

## 29. Coverage

Full branch coverage is `95%`. The updated theme file reached 98% branch-aware
coverage; the updated icon helper reached 94% file coverage with the new QLabel
fallback path and exceptional missing-asset branches remaining only in the
reported coverage detail.

## 30. Packaging

PyInstaller completed successfully using the existing specification into
`.build-phase28-dist`. The package contains all 26 Phase 2.7 Fluent SVG assets,
with zero PNG/JPG assets in the Fluent directory.

Executable SHA-256:

`7EEA4ADE5536F0887377F64FC71129888734797BEAE93D8B5B8D1569D5F1EB27`

An isolated package smoke start stayed alive for eight seconds and created only
`.build-phase28-smoke-local/DropSort/dropsort.db` and an empty log.

## 31. Schema / File Safety

No schema or migration changed. Read-only smoke inspection found migration rows
1 through 4, `PRAGMA user_version = 0`, zero movies, and zero media files,
confirming logical schema v4. No real media library, credentials, or production
paths were used. No filesystem mutation, file-operation, recovery, journal,
Undo, matcher, TMDB, search, or database behavior was changed.

## 32. Exceptions

- No new font or font-family replacement was justified.
- No new Sidebar Toggle control exists to receive the already-registered Phase
  2.7 Panel Left icon.
- Qt controls retain their native icon/text rendering while the shared 8px
  icon/text token and equivalent row spacing are enforced; no risky custom
  button framework was introduced.
- 100%/125%/150% display rendering was not claimed without an actual visual
  display inspection.

## 33. Remaining Visual Issues

No automated structural issue remains. Actual display-specific concerns such as
font rasterization, Arabic expansion at the user's DPI, perceived icon/text
gap, and long-title appearance still require the user visual retest.

## 34. Normal Review

The phase preserves the existing Inter/Noto Sans Arabic font-family strategy,
adds no dependency or image asset, keeps the authoritative 4/8/12/16/24/36/48
scale, uses 36px ordinary controls, 42px navigation rows, 16px Fluent icons,
8px equivalent icon/text spacing, 4px control radius, 8px overlay radius,
Python/PySide6/Qt, four themes, Arabic/RTL, Western numerals, and the permanent
Check Library page.

## 35. Adversarial Review

No credible data-loss path was introduced. No UI direct SQL/HTTP/filesystem
operation was added. Existing architecture tests, full regression, packaging,
isolated startup, and read-only SQLite inspection passed. Destructive actions
retain their existing danger styling and confirmation flow.

## 36. User Visual Retest

`USER VISUAL RETEST REQUIRED: YES`

Please inspect the three completed parts together across expanded/compact
sidebar, Library, Personal Library, long-title MovieCards, Movie Details, Add
Movies, Check Library idle/running/results, Operations Log, Settings/Danger
Zone, DatePicker, header/search, Main/Dark/Slate/Light, English, Arabic RTL,
and representative 100%/125%/150% Windows scaling.

## 37. Phase Decision

`PASS`

Phase 2.8 is complete. The result is a compact, shared typography and spacing
system layered on top of Phases 2.6 and 2.7, with no roadmap expansion and no
changes to navigation destinations, Check Library semantics, business logic,
database schema, file safety, or Fluent icon semantics.

Required confirmations:

- The existing DropSort font-family strategy was preserved.
- No new font dependency was introduced.
- The authoritative spacing scale remains 4/8/12/16/24/36/48.
- Ordinary controls use the shared 36px metric where applicable.
- Sidebar/navigation rows use the shared 42px metric.
- Ordinary Fluent icons remain 16x16px.
- Equivalent icon/text controls use an 8px gap by default.
- Control/navigation radius remains 4px and overlay/top-level radius remains 8px.
- The Fluent icon family and semantic choices were not replaced.
- Check Library remains a permanent page and was not reverted to a dialog.
- Arabic/RTL remains supported.
- Western digits remain used in numeric presentation.
- Main/Dark/Slate/Light remain supported.
- No database schema or migration change was introduced.
- No business/file-safety behavior was changed.
- No new images or illustrations were added.
- DropSort remains Python/PySide6/Qt.
