# DropSort Approved Figma Make Implementation

## IMPLEMENTATION SUMMARY

The approved `DropSort_Redesign_Stitch_Metrics_Pass_FIXED.make` package was
inspected as the primary visual source. Its embedded source snapshot resolves to
commit `0152354554b9f4272c00c0fbf33d3a0447765cce`; the recovered Git pack has
SHA-1 `5b3e0876e03b4455f162661b6ee123fbfeae3204`.

Implemented changes:

- Replaced the resizable/collapsible shell with one structurally fixed 272 px
  sidebar. Compact rail state, pane toggle, compact search popup, sidebar Back,
  draggable width handling, and the duplicate global content header are removed.
- Preserved one always-visible local Library search and Settings at the bottom.
- Matched the Make navigation geometry: 8 px outer inset, 42 px rows, 4 px gaps,
  16 px icons, 12 px row padding, 4 px radius, and a mirrored 3 x 24 px accent.
- Updated Slate semantic colors to the approved Cool Midnight / Blue Slate family.
- Applied 36 px major page edges and compact content-height composition across
  Library, Personal Library, Add Movies, Check Library, Settings, and Operations Log.
- Reworked Personal Library empty states into small left-aligned content without a
  giant centered panel.
- Consolidated Add Movies folder selection into one card while preserving the real
  choose-folder-and-scan workflow and all import semantics.
- Constrained Check Library content near the top and kept it as a permanent page.
- Reworked Movie Details around a persistent 42 px Back bar, 200 x 300 poster hero,
  inline overview, one Your Library card, one Media Files card, and responsive
  two-column-to-stacked behavior.
- Kept Settings as one calm, single-column, content-height sequence with exactly one
  TMDB card, one Setup Guide action, and no Open TMDB action.
- Removed browser-like tab borders and the heavy Danger Zone side stripe.
- Preserved Main, Dark, Slate, and Light themes; English/Arabic behavior; Western
  digit formatting; official Fluent icons; and all existing local-first behavior.

Production files changed:

- `src/dropsort/ui/main_window/window.py`
- `src/dropsort/ui/common/theme.py`
- `src/dropsort/ui/library/library_view.py`
- `src/dropsort/ui/personal_library/personal_library_view.py`
- `src/dropsort/ui/scan/import_view.py`
- `src/dropsort/ui/reconciliation/page.py`
- `src/dropsort/ui/movie_details/details_view.py`
- `src/dropsort/ui/settings/settings_view.py`
- `src/dropsort/ui/history/view.py`

Focused contract tests were updated in `test_sidebar.py`,
`test_sidebar_prototype_match.py`, `test_phase29_windows_shell.py`,
`test_visual_consolidation_master.py`, and `test_theme.py`.

No database migration, filesystem engine, path policy, journal/recovery behavior,
Undo/Relink behavior, metadata provider, personal-state model, or movie identity rule
was changed. No images were generated or added.

## DESIGN / PRODUCT FEEDBACK

- [KEEP] The fixed expanded navigation is substantially clearer for DropSort's five
  destinations and removes a low-value desktop state.
- [KEEP] The Make Slate palette maps cleanly to the existing semantic theme system;
  all themes continue to share one geometry system.
- [KEEP] The existing official Fluent icon registry and real poster pipeline are more
  appropriate than copying web symbols or mock artwork.
- [CHANGE RECOMMENDED] The Library still exposes its earlier Check Library header
  shortcut in addition to permanent sidebar navigation. It is behaviorally safe, but
  could be removed in a later explicitly approved product cleanup if exact Make
  minimalism is preferred.
- [MISSING / UNCLEAR] The web reference separates Browse from Scan Folder, while the
  real application intentionally starts review after folder selection. The real
  workflow was preserved and placed inside one coherent card.
- [MISSING / UNCLEAR] The clean isolated profile has no real movies, media files, or
  operation history, so populated-state visual acceptance still needs representative
  user-owned test data or approved disposable fixtures.
- [PYSide6 CONSTRAINT] Native QTabBar, QDateEdit calendar popup, scrollbars, and text
  measurement do not reproduce browser CSS pixel-for-pixel; their spacing and semantic
  hierarchy are matched while retaining native keyboard/accessibility behavior.
- [PYSide6 CONSTRAINT] The legacy persisted sidebar-width setting remains as inactive
  compatibility data because persistence semantics were out of scope. MainWindow no
  longer reads, writes, or responds to it.

## TEST RESULTS

- Final focused shell/search/RTL/resize contract slice: 25 passed, 0 failed.
- Final complete regression: 1,150 passed, 5 skipped, 0 failed in 346.57 seconds.
- Skips: the five established Windows symlink-privilege cases.
- Branch coverage run: 1,145 passed, 5 skipped, 0 failed; combined statement/branch
  coverage displayed as 95 percent. Evidence: `coverage-figma-make-final.json`.
- Final `compileall` check: passed.
- Desktop size contracts passed at 1280 x 800, 1440 x 900, and 1600 x 1000.
- Movie Details two-column and narrow stacked topology passed.
- One intermediate regression had one expected environmental failure because the
  intentionally running official GUI owned the single-instance lock. After the exact
  process ended, the affected test passed in isolation and the full uncontaminated
  regression passed as reported above.

## VISUAL QA

**VISUAL STATUS: BLOCKED_PENDING_MANUAL_APPROVAL**

The exact official executable was launched with the normal Windows GUI and an isolated
project-local profile. The process path was
`D:\DropSort_ chat\DropSort\release\DropSort\DropSort.exe`; Windows reported title
`DropSort Media Library` and `Responding = True`.

The required Computer Use helper failed before `list_apps` on the initial call, the
permitted lightweight retry, and the fresh-kernel retry with:

`windows sandbox failed: helper_unknown_error: apply deny-read ACLs`

Therefore no screenshot or pixel-level page traversal is claimed. English/Arabic,
LTR/RTL, target-size, geometry, search-routing, and accent-mirroring contracts passed
automated tests, but real rendered inspection of Library, Personal Library, Add Movies,
Check Library, Movie Details, Settings, Operations Log, and 100/125/150 percent Windows
scaling remains pending manual approval.

Manual screenshot checklist:

1. Confirm the 272 px fixed sidebar, always-visible search, selected accent, and bottom
   Settings row on every destination.
2. Check Library, Personal Library empty states, Add Movies folder card, Check Library
   idle/running/result states, and compact Settings cards at 1280 x 800 and 1600 x 1000.
3. Open a populated Movie Details page and confirm the 200 x 300 hero and balanced
   Your Library / Media Files columns, then narrow until they stack.
4. Switch to Arabic and inspect Library, Settings, and Movie Details at 100, 125, and
   150 percent Windows scaling; confirm mirrored navigation accent and LTR paths/dates.
5. Open Operations Log with representative rows and confirm dense natural-width actions.

## RELEASE

- Build status: PASS.
- Builder: PyInstaller 6.22.0, Python 3.12.10, Windows 11.
- Official executable:
  `D:\DropSort_ chat\DropSort\release\DropSort\DropSort.exe`
- Executable size: 2,457,558 bytes.
- Official release inventory: 221 files.
- SHA-256:
  `BD20CDCF5A5505AF4E319B89B8B2846BE469E547A0DC07CBDD8684890EBFCE32`
- Previous official release preserved at
  `.build-figma-make-previous-release-20260817`.
- Fresh isolated packaged startup remained alive for eight seconds from unrelated
  `C:\Windows\Temp` and created migration versions 1-4 with zero movies, media files,
  and file operations.

## REMAINING DIFFERENCES FROM FIGMA MAKE

- Add Movies retains the real combined folder-picker/start-review interaction instead
  of inventing separate Browse and Scan state.
- The existing Library Check shortcut remains alongside permanent sidebar navigation.
- Existing Library cards retain DropSort's established 168 px card width and poster
  crop behavior rather than forcing browser CSS aspect-ratio rendering.
- Native Qt calendar, scrollbar, focus, font rasterization, and text wrapping can differ
  slightly from Chromium/Figma Make rendering.
- The full populated Movie Details, detected-movie list, issue list, and Operations Log
  could not be visually compared because the isolated profile was intentionally empty
  and the Windows screenshot helper could not initialize.
- Pixel-level theme, RTL, and DPI acceptance remains pending the manual checklist above.
