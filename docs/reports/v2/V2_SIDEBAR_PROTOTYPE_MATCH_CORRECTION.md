# DropSort Sidebar Prototype Match Correction

## Task

Sidebar Prototype Match Correction only. The supplied approved HTML prototype was treated as the visual source of truth; unrelated page content and backend behavior remained out of scope.

## Status

BLOCKED_PENDING_VISUAL_APPROVAL

Implementation, automated verification, accepted coverage, packaging, official artifact promotion, normal exact-release launch, and file-safety review pass. Real pixel inspection is blocked because the Windows computer-control kernel failed before list_apps on both its initial attempt and the one permitted reset/retry.

## Expanded sidebar

PASS by source and automated geometry tests. The default pane remains 272 px. Its outer horizontal inset is 8 px; top, pane-toggle, search, navigation, and footer regions are explicit rows rather than one loose vertical stack. The top row is 42 px, Back is 36 px, DropSort remains text-only, and expanded navigation uses 42 px rows with 4 px vertical gaps and 12 px row padding.

## Navigation alignment

PASS by source and automated tests. Primary destinations are rendered inside sidebarPrimaryNavigation with 4 px expanded horizontal insets and 4 px row spacing. Arabic/RTL navigation text aligns right while LTR text aligns left.

## Selected state

PASS by source and automated tests. Navigation rows override the generic checked-button border. Selection uses the theme backplate plus an independent 3 x 24 px accent marker, mirrored to the trailing edge in RTL. Compact selection has no accent outline.

## Back + DropSort

PASS by source and automated tests. Back and the DropSort text label share one 42 px top row with an 8 px gap. No sidebar application logo was added. Existing meaningful Back behavior is unchanged.

## Pane toggle

PASS by source, asset hash, and automated icon tests. The existing panel_left.svg asset now contains the approved clear three-line navigation/pane glyph instead of the malformed square-like panel outline. No new image asset was introduced.

## Search

PASS by regression tests. Expanded search remains structurally present on every page inside an 8 px vertical / 4 px horizontal wrapper. Compact search remains a centered 36 px shell action. Existing local Library filtering, cross-page routing, completion, and Escape behavior are unchanged; no network search was added.

## Settings

PASS by source and automated hierarchy tests. Settings is rendered by the same data-driven navigation builder inside a dedicated footer after the flexible spacer. The footer has a subtle top divider and 8 px top padding.

## Compact rail

PASS by source and automated geometry tests. The rail remains 56 px with 6 px outer side insets, leaving exactly 44 px for each 44 x 42 navigation target. Nested primary/footer horizontal margins become zero in compact mode, preventing the previous 44 px target from being squeezed into a 36 px inner slot. Shell actions remain centered at 36 px.

## Themes

PASS by stylesheet construction and full regression. All selected, hover, border, text, and accent colors remain semantic values from Main, Dark, Slate, and Light themes. Real per-theme pixel traversal remains blocked.

## RTL

PASS by automated LTR/RTL tests. A layout-direction change immediately repositions the accent marker, and RTL navigation text receives mirrored alignment. Existing Arabic copy, Western numerals, and technical LTR handling are unchanged. Real Arabic pixel traversal remains blocked.

## Files changed

- src/dropsort/ui/main_window/window.py
- src/dropsort/ui/common/theme.py
- src/dropsort/ui/assets/fluent/panel_left.svg
- tests/unit/ui/test_sidebar.py
- tests/unit/ui/test_phase29_windows_shell.py
- tests/unit/ui/test_visual_consolidation_master.py
- tests/unit/ui/test_sidebar_prototype_match.py (new)
- coverage-sidebar-correction-final.json (generated evidence)
- V2_SIDEBAR_PROTOTYPE_MATCH_CORRECTION.md (this report)

## Tests

- Focused final sidebar, Fluent-icon, typography, Phase 2.9 shell, and visual-consolidation slice: 29 passed, 0 failed.
- Final complete regression: 1,145 passed, 5 skipped, 0 failed in 413.70 seconds.
- The five skips are the existing Windows host symlink-privilege skips.
- Final source compilation check passed.

## Coverage

PASS. Final combined statement/branch metric is 94.57308767653595 percent, displayed as 95 percent. Coverage configuration and exclusions were not changed.

## Official release

PASS. PyInstaller 6.22.0 under Python 3.12.10 rebuilt DropSort.spec from the exact final source. The official one-directory release contains 221 files, including migrations 1-4, Fluent assets, Inter and Noto Sans Arabic fonts, licenses, README, and third-party notices.

- Executable: D:\DropSort_ chat\DropSort\release\DropSort\DropSort.exe
- Size: 2,462,886 bytes
- Timestamp: 2026-08-17 00:22:32 +03:00
- Previous and intermediate release directories were preserved in project-local build backups instead of being deleted.

## SHA-256

DD194DE0AA7D98E94EE36A2A7EEBBB8079CEABC408215AB3DFFA507436A30CAC

## Real GUI inspection

BLOCKED. The exact final official executable was launched normally and its PyInstaller GUI child is running as PID 26072 with title DropSort Media Library from the exact official path. It uses the project-local isolated profile under .build-sidebar-correction-visual-final-local.

The Windows computer-control kernel failed before list_apps with windows sandbox failed: helper_unknown_error: apply deny-read ACLs. The fresh-session retry failed identically. Therefore no screenshot, rendered traversal, or pixel-level PASS is claimed.

One diagnostic direct launch briefly inherited the default profile while diagnosing PyInstaller parent/child behavior. It was immediately closed, no UI action or media operation was issued, and the final running process was relaunched with the isolated project-local profile.

## File-safety review

PASS for the implemented correction. No production filesystem engine, path policy, database repository, migration, metadata provider, network provider, or media operation code changed. The final isolated database was inspected read-only and contains four migrations, zero movies, and zero media files. The startup smoke used only project-local isolated profiles. No real media file was selected, moved, renamed, deleted, or organized.

## Remaining differences

No known source-level or automated geometry difference remains against the approved sidebar specification. The only unresolved requirement is real rendered inspection across expanded/compact states, all four themes, Arabic RTL, navigation destinations, and relevant DPI/resize states. Those pixels require user/device visual approval because the capture helper could not initialize.

## Decision

BLOCKED_PENDING_VISUAL_APPROVAL. Do not start another phase until the exact official release is visually approved or the Windows capture helper becomes available for the required traversal.
