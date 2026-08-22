# DropSort Final V1 UX Polish Follow-Up

## Result

**READY FOR USER RETEST.** This follow-up is limited to the final manual-search and desktop UX
polish requested for the V1 release candidate. It does not change matching thresholds, catalog
authorization, filesystem safety, or any physical-media operation.

## Manual TMDB Search

- The dialog no longer reserves a large fixed empty results panel; results are hidden until a
  successful response and the dialog grows only for displayed rows.
- Provider candidates are deduplicated by `(provider, external_id)` and capped at five after
  deduplication. Zero-result and provider-error states remain distinct and compact.
- Automatic proposals with one valid candidate retain that candidate as the selected proposal, but
  the user must still click **Add to DropSort Library**. No catalog or filesystem action is triggered
  by selection or matching.
- **Edit Search** is shown only for movie proposals with no useful automatic candidate or unavailable
  metadata; successful match/review rows do not carry the extra button.
- Add Movies rows show the filename, not the full physical path. Full path data remains in the
  discovery/catalog models and technical/detail surfaces.
- Live TMDB search remains **NOT VERIFIED** here because no credential was supplied; user-side
  packaged retest remains required for the noisy `Kaze Tachinu` case.

## Settings, Sidebar, and Themes

- Settings root, scroll viewport, content host, and card host now consume centralized background
  tokens, removing exposed dark bands in Light.
- The primary navigation uses a native `QSplitter`, bounded to 72-360 px, with a deterministic
  icon-only mode at narrow widths. Native Qt icons are used temporarily and localized tooltips are
  supplied in English and Arabic.
- Sidebar width is persisted through the existing SQLite settings boundary; invalid values fall
  back to the 220 px default. Compact state restores through the saved width.
- The user-facing theme set is exactly **Main**, **Dark**, **Slate**, and **Light**. Main preserves
  DropSort's original identity, Dark is the neutral charcoal palette, Slate uses the supplied dark
  palette, and Light uses the supplied light palette. Legacy `deep_ink`, `charcoal`, and `light_blue`
  IDs migrate to the new stable IDs without resetting the saved preference.
- Language and theme/sidebar settings remain independent, with existing RTL/LTR and bundled
  Inter/Noto Sans Arabic behavior preserved.

## Files created

- `tests/unit/ui/test_sidebar.py`
- `PHASE_6B_3_REPORT.md`
- `src/dropsort/ui/assets/dropsort.svg`
- `src/dropsort/ui/assets/dropsort.ico`
- `src/dropsort/ui/common/icon.py`

## Files modified

- `src/dropsort/application/configuration/theme.py`
- `src/dropsort/application/configuration/__init__.py`
- `src/dropsort/application/bootstrap/desktop.py`
- `src/dropsort/application/use_cases/manual_movie_search.py`
- `src/dropsort/database/repositories/settings.py`
- `src/dropsort/database/repositories/__init__.py`
- `src/dropsort/ui/contracts.py`
- `src/dropsort/ui/common/theme.py`
- `src/dropsort/ui/localization.py`
- `src/dropsort/ui/main_window/window.py`
- `src/dropsort/ui/settings/settings_view.py`
- `src/dropsort/ui/scan/manual_search_dialog.py`
- `src/dropsort/ui/scan/import_review_row.py`
- `tests/unit/application/test_manual_movie_search.py`
- `tests/unit/application/test_theme_settings.py`
- `tests/unit/ui/test_manual_search_dialog.py`
- `tests/unit/ui/test_manual_search_ui.py`
- `tests/unit/ui/test_settings_view.py`
- `tests/unit/ui/test_theme.py`
- `README.md`
- `PROJECT_STATUS.md`
- `RELEASE_CHECKLIST.md`

## Tests and coverage

The focused polish suite passes: **129 passed, 0 failed**. The final complete source gate passes
with **988 passed, 5 skipped, 0 failed** and **95% total branch coverage**, using warnings-as-errors.
The five known Windows symlink-permission skips are legitimate and no failures are waived.

## Packaging

The existing one-directory PyInstaller process was rebuilt with PyInstaller **6.22.0**. The exact
artifact is:

```text
release\DropSort\DropSort.exe
```

The rebuilt directory contains 188 files and 120,147,276 bytes (114.58 MiB total). Required Qt
runtime, migrations, Inter/Noto Sans Arabic fonts and OFL texts, TMDB attribution assets, README,
and third-party notices are present. Static audit found no forbidden test/runtime artifacts, no
Margarine asset, no developer-path matches, and no credential/bearer-token pattern.

A disposable clean-profile launch from `C:\Windows\Temp` exited with code 0, created only redirected
per-user database, poster-cache, and logs, and produced an empty log. No separate clean machine or
VM was available; this is a **CLEAN-PROFILE APPROXIMATION**.

## Review findings and fixes

- Fixed the oversized manual-search layout and duplicate signal registration while preserving
  stale-result invalidation.
- Fixed result-limit ordering so duplicates cannot consume the five visible slots.
- Fixed Add Movies path noise without deleting path data from domain models.
- Fixed successful-row Edit Search clutter while retaining recovery for no-match/unavailable states.
- Fixed Light Settings background layers.
- Added bounded native splitter behavior, localized compact tooltips, and validated persisted width.
- Added the branded DropSort application icon to QApplication/MainWindow and the PyInstaller EXE.
- Replaced the legacy three-theme surface with the four-theme semantic palette and safe ID migration.

No unresolved BLOCKER, CRITICAL, or release-relevant HIGH finding remains.

## Known limitations and next action

Live TMDB credential verification for `AnimeSanka.com Kaze Tachinu ...` remains user-side. The
portable executable is unsigned and no separate clean machine/VM was available. The result list is
intentionally text-first; no new poster-download pipeline was added to the manual dialog.

Return the rebuilt executable to the user for the final V1 UX retest. Do not begin V1 Release
Freeze or V2 until the user accepts the compact search, Add Movies cleanup, sidebar behavior, theme,
and Arabic/English checks.
