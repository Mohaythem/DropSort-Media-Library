# DropSort Final V1 Remaining-Issues Report

## Result

**READY FOR USER RETEST.** This pass addressed the five required remaining V1 UX areas without
changing matcher policy, catalog authorization, filesystem safety, journal/recovery behavior, or
V1 scope.

## Check Library completion

`LibraryFileCheckDialog` now has explicit `IDLE`, `RUNNING`, `COMPLETED`, `FAILED`, and `CANCELLED`
states. Reaching the total progress count does not complete the dialog; only the authoritative
terminal callback does. Success renders a `Present / Missing / Errors` summary, disables Cancel,
and enables Close. Failure and cancellation remain visibly distinct. Manual dialogs opened while
automatic reconciliation is active subscribe to that same job and receive its terminal result;
closing a coalesced dialog does not cancel the shared job.

## Add Movies queue

Add Movies now renders only remaining review work; already-cataloged paths are omitted. Successful
explicit imports remove their row immediately and refresh the local library. Failed imports remain
visible and retryable. Every remaining row has a session-only dismiss X. Dismissal only removes the
row from the current review session and performs no media/catalog/journal operation. An exhausted
queue shows `All done` and `No movies are waiting for review.`. Filename-only presentation and
status-aware Edit Search behavior remain intact. Exactly one valid automatic candidate remains
preselected but never auto-imported.

## Manual TMDB search presentation correction

The manual/edit search dialog now renders each of up to five deduplicated candidates as a semantic
card. Title/year, TMDB ID, rating, and a bounded word-wrapped overview are separate controls, and
each card owns its compact localized **Select** action. Results use a resizable vertical-only scroll
area; the legacy dense raw-text rows, horizontal overflow, and detached full-width selection button
were removed. Search remains background/non-blocking, selection remains candidate-only, and explicit
**Add to DropSort Library** is still required. The card styling derives from the four centralized
themes and technical identifiers remain LTR-safe in Arabic mode.

Focused source coverage for this correction is **17 passed, 0 failed**; the affected UI,
localization, theme, import, and architecture gate is **57 passed, 0 failed**. The complete source
suite after the change is **1023 passed, 5 skipped, 0 failed** with **95% total branch coverage**.
The exact one-directory release was rebuilt with PyInstaller 6.22.0 at
`release\\DropSort\\DropSort.exe`; its final audit is 190 files and 120,116,153 bytes (114.55 MiB),
with required runtime resources present and zero secret/developer-path matches.

The packaged executable was rebuilt and artifact-audited, but this controlled process did not perform
a live TMDB credential search or a direct visual drag-through of the packaged Manual Search dialog.
Those remain honest user-side retests; no packaged visual success is claimed here.

## Application icon

Added the branded local assets:

- `src/dropsort/ui/assets/dropsort.svg`
- `src/dropsort/ui/assets/dropsort.ico` (7 embedded sizes: 16, 24, 32, 48, 64, 128, 256)
- `src/dropsort/ui/common/icon.py`

`QApplication`, `MainWindow`, and packaged PyInstaller EXE use the icon. The SVG/ICO are bundled
under the application asset directory; child dialogs inherit the application identity.

## Themes and sidebar

The user-facing theme selector now exposes exactly **Main**, **Dark**, **Slate**, and **Light**.
Main preserves the original DropSort palette, Dark is the neutral charcoal direction, Slate maps
the supplied dark scale, and Light maps the supplied light scale with themed Settings root,
viewport, card-host, and scroll-exposed surfaces. Legacy persisted IDs migrate safely to the new
stable IDs without resetting the saved preference.

The sidebar remains a bounded persisted splitter. At widths below the explicit 200 px threshold it
switches immediately to a compact icon-only layout: all branding/subtitle/navigation text is hidden,
the DropSort mark is shown, icons use normalized 24 px sizing and 44 px button geometry, selected
state is proportionate, and localized tooltips remain available in English and Arabic. Wider widths
restore fully readable branding and labels.

## Files created

- `src/dropsort/ui/assets/dropsort.svg`
- `src/dropsort/ui/assets/dropsort.ico`
- `src/dropsort/ui/common/icon.py`
- `src/dropsort/application/runtime/single_instance.py`
- `src/dropsort/ui/scan/manual_search_result_card.py`
- `PHASE_6B_FINAL_REMAINING_ISSUES_REPORT.md`

## Files modified

- `src/dropsort/application/configuration/theme.py`
- `src/dropsort/application/bootstrap/desktop.py`
- `src/dropsort/ui/common/__init__.py`
- `src/dropsort/ui/common/theme.py`
- `src/dropsort/ui/localization.py`
- `src/dropsort/ui/main_window/window.py`
- `src/dropsort/ui/reconciliation/dialogs.py`
- `src/dropsort/ui/scan/import_review_row.py`
- `src/dropsort/ui/scan/import_view.py`
- `src/dropsort/ui/scan/manual_search_dialog.py`
- `src/dropsort/ui/settings/settings_view.py`
- `tests/unit/ui/test_manual_search_dialog.py`
- `tests/unit/ui/test_ui_architecture.py` (existing gate remains green)
- `src/dropsort/application/runtime/__init__.py`
- `DropSort.spec`
- `README.md`
- `docs/status/PROJECT_STATUS.md`
- `docs/status/RELEASE_CHECKLIST.md`
- focused UI/application/release regression tests under `tests/unit/`

Single-instance implementation and focused tests:

- `src/dropsort/application/runtime/single_instance.py`
- `tests/unit/ui/test_single_instance.py`
- `tests/unit/ui/test_main_window.py`
- `tests/unit/ui/test_desktop_bootstrap.py`

## Tests and coverage

- Remaining-issues focused gate: **80 passed, 0 failed**; single-instance focused gate: **29 passed, 0 failed**.
- Complete source suite: **1023 passed, 5 skipped, 0 failed**.
- Branch coverage: **95% total**.
- The five skips are legitimate Windows symlink-creation permission limitations.

## Packaging and verification

The existing `packaging/build_release.ps1` rebuilt the one-directory portable release with
PyInstaller **6.22.0**. Final artifact:

```text
release\DropSort\DropSort.exe
```

Artifact audit: 190 files, 120,110,630 bytes (114.55 MiB). Required fonts, Arabic fonts, OFL
licenses, migrations, TMDB attribution resources, README, notices, and icon assets are present.
Forbidden runtime/test artifacts: 0. Developer-path matches: 0. DropSort credential/bearer-token
matches: 0. The executable associated icon loads successfully.

A disposable packaged first-run launch from unrelated `C:\Windows\Temp` with a project-contained
`LOCALAPPDATA` override created the isolated database, poster-cache, and logs directories and
closed cleanly with exit code 0. No log output was produced. No separate clean machine or VM was
available; this remains a **CLEAN-PROFILE APPROXIMATION**.

The rebuilt exact executable also passed the disposable single-instance smoke: normal second launch,
five repeated launches, minimized activation, normal exit/relaunch, rapid double launch, and abnormal
primary termination followed by stale recovery. Each secondary exited with code 0; one functional
primary window/process remained; the disposable database was created; no catalog, media, or
`file_operations` side effects were introduced.

## Review findings and fixes

- Terminal progress was incorrectly treated as completion; fixed by making terminal callbacks
  authoritative and adding explicit dialog states.
- Manual reconciliation requests were previously rejected while automatic work was active; fixed
  by coalescing them onto the active job without duplicate reconciliation.
- Successful imports remained in the review list; fixed by removing only the successful row.
- The review surface had no safe session dismissal; added a row-local X with no catalog/media side
  effects.
- The UI architecture gate caught a `Path.is_file()` call in icon presentation code; replaced it
  with Qt `QFileInfo.isFile()` so filesystem probing remains outside widget logic.
- Legacy theme IDs and old user-facing labels were inconsistent with the final four-theme contract;
  added safe migration and exact Main/Dark/Slate/Light labels.

No unresolved BLOCKER, CRITICAL, or release-relevant HIGH finding remains in this pass.

## Single-instance application fix

The duplicate-window behavior is fixed at the application boundary. `SingleInstanceCoordinator`
claims an atomic Qt `QLockFile` scoped to the current Windows user/session, then owns a
`QLocalServer` endpoint for the deliberately small `ACTIVATE` protocol. The SQLite catalog is never
used as the process lock, and a secondary process exits before runtime paths, database migrations,
reconciliation, workers, metadata, or UI composition are initialized.

The primary process receives `ACTIVATE` and calls the MainWindow lifecycle boundary to show, restore,
raise, and activate the existing window. Hidden and minimized windows are handled; callbacks are inert
after shutdown begins. Unknown or oversized local messages are ignored safely. Normal shutdown closes
the endpoint and unlocks immediately. Qt stale-lock detection is used once after an unreachable owner,
with no infinite retry and no process killing. A stale local server is removed only after ownership has
been acquired.

Focused source coverage includes primary acquisition, secondary activation, unknown/oversized messages,
cleanup and immediate relaunch, stale state, user/session identity, startup short-circuiting, and
normal/minimized/hidden/shutdown window activation.

## Remaining issues / user retest

- Live TMDB verification of the noisy `AnimeSanka.com Kaze Tachinu ...` case remains unverified
  because no credential was available to this process. User-side packaged retest is still required.
- Manual visual drag-through of the packaged sidebar, all four themes, Arabic/RTL, taskbar/Alt+Tab,
  and the full disposable destructive-action smoke should be completed by the user on the rebuilt
  executable. Automated source and artifact gates are green.
- The executable is unsigned; SmartScreen may warn. No separate clean machine/VM was available.
- Manual visual inspection of taskbar/Alt+Tab activation remains optional user-side confirmation; the
  exact executable's process/window activation behavior is covered by the packaged smoke above.

Do not begin Release Freeze or V2 until the remaining user retest is accepted.
