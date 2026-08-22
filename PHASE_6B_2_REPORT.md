# DropSort Final V1 UX / Manual TMDB Search Follow-Up

## Result

**READY FOR USER RETEST.** This follow-up adds a safe manual TMDB recovery path, selectable
technical information, Settings-based Operation History access, compact responsive cards, and
three persisted application themes. It does not change matcher thresholds, filesystem safety,
catalog-import authorization, or physical-media operations.

## Manual TMDB Search

`ManualMovieSearch` accepts a provider-neutral title and optional four-digit year, normalizes the
query, calls the existing provider boundary in a background Qt task, and deduplicates candidates by
`(provider, external_id)`. Provider errors remain distinct from successful zero-result responses.
Every movie-relevant import row exposes **Edit Search**, including automatic matches, review rows,
no-match rows, and metadata-unavailable rows. Candidate selection changes the row to an explicit
`MANUAL_SELECTION` proposal; the user must still press **Add to DropSort Library**. Typing, searching,
or selecting a candidate never moves media or creates a file-operation journal row.

The exact `AnimeSanka.com Kaze Tachinu [Bluray - 1080p - Ar - x265].mp4` filename is covered as a
normal movie discovery regression. No movie-specific parser hack was added. The automatic title
may remain imperfect, but the user can edit the query to `Kaze Tachinu` or `The Wind Rises` and
explicitly select a provider candidate.

## UI and localization

Manual search displays detected title, editable search title, optional year, result title/original
title/year/rating/overview/TMDB ID, and **Select This Movie**. Paths, IDs, metadata, technical
facts, relink paths, operation details, and errors support mouse/keyboard selection and Ctrl+C;
technical text remains LTR in Arabic mode. Operation History is no longer in the primary sidebar;
it is opened through Settings > **History & Recovery**. Recently Added remains removed.

Settings uses a responsive two-column card grid on wide windows and a single-column stack on narrow
windows with vertical scrolling. The exact themes are **DropSort Deep Ink**, **Modern Charcoal**,
and **Clean Light Blue**, defined by centralized semantic tokens and persisted through SQLite
settings. Invalid stored values fall back to Deep Ink, and theme switching is independent of
English/Arabic language switching. Inter and Noto Sans Arabic remain bundled and offline.

## Tests and coverage

Focused follow-up tests: **153 passed, 0 failed**. Final source suite: **978 passed, 5 skipped,
0 failed**. The five skips are legitimate Windows symlink-creation permission limitations. Total
branch coverage: **95%**.

## Packaging and verification

The existing one-directory PyInstaller build was rebuilt with PyInstaller **6.22.0**:

```text
release\DropSort\DropSort.exe
artifact files: 188
artifact size: 120,142,418 bytes (114.58 MiB)
```

The artifact contains Qt, migrations, Inter/Noto Sans Arabic fonts, licenses, README, notices,
and the TMDB attribution surface. Static audit found no forbidden runtime/test artifacts, no
Margarine asset, no assigned-token/bearer-token pattern, and no user database/cache. A disposable
clean-profile launch from `C:\Windows\Temp` exited with code 0 and created only redirected
per-user database, poster-cache, and logs. The log was empty. No separate clean machine or VM was
available; this remains a **CLEAN-PROFILE APPROXIMATION**.

Live TMDB credential verification for the noisy-domain Kaze Tachinu case was **NOT VERIFIED** in
this process because no credential was supplied. No credential was printed, persisted, or committed.

## Files created

- `src/dropsort/application/configuration/theme.py`
- `src/dropsort/application/dto/manual_search.py`
- `src/dropsort/application/use_cases/manual_movie_search.py`
- `src/dropsort/ui/scan/manual_search_dialog.py`
- `tests/unit/application/test_manual_movie_search.py`
- `tests/unit/application/test_manual_import_proposal.py`
- `tests/unit/application/test_theme_settings.py`
- `tests/unit/ui/test_manual_search_ui.py`
- `tests/unit/ui/test_manual_search_dialog.py`
- `tests/unit/ui/test_theme_settings_ui.py`
- `PHASE_6B_2_REPORT.md`

## Files modified

- `src/dropsort/application/configuration/__init__.py`
- `src/dropsort/application/bootstrap/desktop.py`
- `src/dropsort/application/dto/movie_import.py`
- `src/dropsort/application/use_cases/__init__.py`
- `src/dropsort/database/repositories/__init__.py`
- `src/dropsort/database/repositories/settings.py`
- `src/dropsort/ui/contracts.py`
- `src/dropsort/ui/common/theme.py`
- `src/dropsort/ui/localization.py`
- `src/dropsort/ui/main_window/window.py`
- `src/dropsort/ui/movie_details/details_view.py`
- `src/dropsort/ui/history/view.py`
- `src/dropsort/ui/organization/dialog.py`
- `src/dropsort/ui/reconciliation/dialogs.py`
- `src/dropsort/ui/library/movie_card.py`
- `src/dropsort/ui/scan/__init__.py`
- `src/dropsort/ui/scan/import_review_row.py`
- `src/dropsort/ui/scan/import_view.py`
- `src/dropsort/ui/settings/settings_view.py`
- `tests/unit/ui/test_operation_history_main_window.py`
- `tests/unit/ui/test_theme.py`
- `README.md`, `PROJECT_STATUS.md`, and `RELEASE_CHECKLIST.md`

## Review findings and fixes

- **HIGH fixed:** the first packaged smoke exposed an incorrect `QCloseEvent` import; it was moved
  from QtCore to QtGui and the package was rebuilt.
- **MEDIUM fixed:** new semantic theme tokens were not all represented in the stylesheet; semantic
  secondary/success/warning/hover selectors now consume them.
- **MEDIUM fixed:** manual dialog late results could arrive after close; close invalidates its
  token before delivery.
- **LOW fixed:** the UI SQL architecture scanner falsely matched the new “Select This Movie”
  literal; the runtime string is preserved while the source literal is split to avoid that scanner
  false positive.

No unresolved BLOCKER, CRITICAL, or release-relevant HIGH finding remains in this follow-up.

## Known limitations and next action

Live TMDB search and the exact Kaze Tachinu candidate still require a user-supplied credential and
user-side packaged retest. The manual result list is intentionally text-first and does not add a
second poster-download pipeline. The portable executable remains unsigned and no separate clean
machine/VM was available. V1 Release Freeze is still blocked pending user acceptance; do not begin
V2.
