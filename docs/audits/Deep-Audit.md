# DropSort Python Baseline — Deep Audit

Audit date: 2026-08-23  
Authoritative product document: `docs/source/The Idea-v3.md`  
Audited baseline: `D:\DropSort_ chat\DropSort`, before feature changes

## 1. Executive Summary

DropSort is a substantial Python/PySide6/SQLite Windows desktop application, not a skeleton or abandoned rewrite. Its strongest area is the filesystem mutation core: approved-root checks, collision prevention, durable operation journaling, content verification, database commit ordering, and ambiguity-preserving recovery are implemented and tested. Stable `Movie`, `MediaFile`, and `WatchEvent` identities exist, and missing files remain registered rather than being deleted.

At this audit's original baseline, startup automatically reconciled every registered file and progress
could cause repeated full Library queries. Python Stabilization Pass 1 remediated those two findings.
Python Stabilization Pass 2 now also remediates the Add Movies/TMDB coupling: explicit local
registration commits stable Movie and MediaFile identities before optional enrichment, including
offline, missing-credential, no-match, and ambiguous cases. Poster cache misses can still call TMDB
when Library cards are constructed; Play/Open Folder does not persist newly discovered missing
state; and Clear Library Data retains active personal state and watch history.

No product behavior was changed during the original audit task. Sections 27 and 28 record the later
Pass 1 and Pass 2 implementation and verification deltas without rewriting the original findings.

## 2. Selected Project Root and Why

Selected root: `D:\DropSort_ chat\DropSort`.

Evidence:

- `pyproject.toml` defines the `dropsort` package, Python requirement, dependencies, pytest configuration, and scripts.
- `src/dropsort/__main__.py` is the active module entry point.
- `src/dropsort/application/bootstrap/desktop.py` composes the database, migrations, application services, and PySide6 window.
- `tests/` imports and exercises the same `src/dropsort` package.
- `DropSort.spec`, `packaging/build_release.ps1`, migrations, assets, and current V2 reports all reference this tree.
- `docs/source/The Idea-v3.md` is present in the selected root and explicitly governs the renewed Python direction.
- No pre-existing `.git` repository was present. Numerous `.build-*`, `.pytest-*`, virtual-environment, coverage, release, and ZIP artifacts are generated outputs, not competing source roots.

Observed fact: this is the only internally coherent source/test/package/documentation root found.  
Inference: historical reports and generated directories are evidence about prior work, not alternative authoritative applications.  
Recommended future change: keep this root canonical and move historical reports into a clearly labeled documentation archive in a separate documentation-only task.

## 3. Current Stack and Entry Points

- Python `>=3.11`; verified interpreter: Python 3.12.10.
- PySide6 `>=6.11.1,<7`; verified: 6.11.1.
- SQLite through the standard library, with foreign keys, WAL, and `synchronous=FULL` in `src/dropsort/database/connection/sqlite.py:15-22`.
- pytest; verified: 9.1.1.
- PyInstaller one-folder Windows packaging through `DropSort.spec`.
- Process entry: `python -m dropsort` → `src/dropsort/__main__.py:1-5` → `run_desktop_app()`.
- Console script: `dropsort = dropsort.__main__:main` in `pyproject.toml`.

## 4. Repository / Module Map

~~~text
src/dropsort/
├─ domain/                 stable entities, personal state, policies
├─ application/            use cases, ports, DTOs, configuration
├─ database/               SQLite connection, migrations, repositories
├─ file_engine/            path policy, transfer, operations, recovery
├─ media/                  discovery, parsing, matching, import
├─ metadata/               TMDB provider and poster source
├─ posters/                cache and asset service
├─ ui/                     PySide6 views, grids, cards, task runner
├─ desktop.py              composition root and main window
└─ __main__.py             process entry
tests/                     unit and integration tests
packaging/                 release builder and verifier
docs/                      architecture, ADRs, specs, reviews, source copies
.codex/skills/             DropSort-specific safety skills
~~~

Architectural boundary observed: UI code receives application-facing action objects; SQLite, HTTP, and filesystem mutation are composed outside widgets. The failing SQL-string architecture test is triggered by localized prose in `ui/localization.py`, not an embedded SQL call.

## 5. Startup Flow

~~~text
python -m dropsort
  → __main__.main
  → desktop.run_desktop_app
  → QApplication + single-instance guard
  → runtime AppData paths + logging
  → create_main_window
      → Database + MigrationRunner.run
      → repositories/providers/poster cache/use cases
      → MainWindow(..., load_on_show=True)
          → construct pages and connect signals
          → show_library()
              → LibraryView.activate() → list_movies() → grid.set_items()
          → _start_automatic_reconciliation()
              → QtTaskRunner → ReconcileLibraryUseCase.execute()
~~~

Concrete chain: `src/dropsort/application/bootstrap/desktop.py:536-563`, `467-525`, `src/dropsort/ui/main_window/window.py:322-442`, `552-565`, and `864-900`.

Observed fact: the first Library entry starts automatic filesystem reconciliation. `ReconcileLibraryUseCase.execute()` lists the catalog in batches and performs `lstat` checks for each registered file (`src/dropsort/application/use_cases/reconcile_library_files.py:58-120`). Status-changing progress invokes `_refresh_library_snapshot()`, which calls `LibraryView.show_library()`.

Inference: startup is “load, then automatic Check-like work,” not the local-state-only startup required by `The Idea-v3.md`. A changing library can be queried and diffed multiple times during that worker.

Recommended future change: remove the automatic reconciliation trigger from initial navigation. Keep reconciliation behind explicit Check Library and emit item-specific updates or one final invalidation.

## 6. Library Load and Refresh Flow

~~~text
MainWindow.show_library
  → LibraryView.activate
      → first visit: show_library
          → LocalLibraryActions.list_movies
          → ListMovies.execute
          → SQLite LibraryQueryRepository.list_movies
          → map all rows to MovieListItem DTOs
          → MovieGrid.set_items
              → retain unchanged cards by movie_id
              → replace only changed/new card widgets
              → relayout only when required
~~~

Evidence: `src/dropsort/ui/library/library_view.py:170-202`; `src/dropsort/application/use_cases/list_movies.py:13-22`; `src/dropsort/database/repositories/library_queries.py:17-40`; `src/dropsort/ui/library/movie_grid.py:58-128,163-219`.

Observed fact: navigation is cached, and the grid preserves card identity by `movie_id`; it does not blindly destroy the entire grid. Each actual refresh still runs the full summary query and recreates all DTO values. There is an item-level details query, but no item-level summary/update contract for the Library grid.

Inference: the historic destructive grid rebuild has been substantially fixed, but broad refresh cost and repaint pressure remain upstream.

Recommended future change: add a query for one Library summary and an item-specific signal keyed by `MovieId`; retain the current stable grid.

## 7. UI Flicker / Full-Refresh Findings

Observed facts:

1. `_on_automatic_reconciliation_progress()` refreshes the Library whenever `status_changes` increases (`src/dropsort/ui/main_window/window.py:888-900`).
2. `_refresh_library_snapshot()` reloads the full visible Library and invalidates/refreshes dependent projections (`src/dropsort/ui/main_window/window.py:773-806`).
3. `MovieGrid.set_items()` diffs by stable ID, so only DTO changes replace cards.
4. A replaced `MovieCard` starts poster loading if it has a poster reference (`src/dropsort/ui/library/movie_card.py:63-68`).
5. Ordinary cached navigation does not reload unchanged data.

Inference: visible flicker is most plausibly caused by repeated broad query/diff/layout/poster activity during automatic reconciliation, not by unconditional whole-grid destruction.

Recommended future change: first remove startup reconciliation and progress-triggered full refresh. Measure before changing the grid implementation.

## 8. Movie and MediaFile Identity

Observed facts:

- `movies.id INTEGER PRIMARY KEY` and unique `(provider, external_id)` are created in migration 0001.
- `media_files.id INTEGER PRIMARY KEY`, nullable `movie_id`, unique normalized `path_key`, and `PRESENT|MISSING` status are persisted.
- `watch_events.id` and `movie_id` are stable in migration 0004.
- Domain entities expose stable IDs in `src/dropsort/library/movies/models.py:77-90,163-198` and `library/personal/models.py:46-54`.
- Relink/path updates mutate the same media-file row rather than replacing identity (`database/repositories/media_files.py:74-86,251-285`).
- Reconciliation marks `MISSING`; it does not delete the movie or media-file record.

Inference: stable local identity and “registered != available” are correctly represented. Path is a unique location key, not the logical identity.

Recommended future change: add an explicit local metadata lifecycle independent of the required TMDB provider/external identity.

## 9. SQLite / Persistence Findings

Observed facts:

- Versioned migrations are atomic and recorded by `MigrationRunner` (`database/connection/migrations.py:18-56`).
- Writes use explicit transactions; operation commits atomically update the media path plus journal state (`database/repositories/operation_store.py:47-60`).
- Library summaries use a database-wide aggregate query; details use item-specific queries (`library_queries.py:17-78`).
- Filesystem identifiers `st_dev` and `st_ino` are stored as decimal SQLite TEXT by migration 0002, avoiding Windows integer overflow.
- The operation journal preserves nonterminal and recovery states.

Inference: persistence is safety-oriented and supports item writes, but Library projection APIs encourage broad reads.

Recommended future change: introduce item-level projection reads and explicit metadata-enrichment state; do not weaken journal durability.

## 10. Missing File Behavior

~~~text
External move/delete
  → no watcher-driven deletion
  → startup auto reconciliation OR manual Check Library
  → lstat failure / identity mismatch
  → MediaFile.status = MISSING
  → Movie remains registered
~~~

Observed fact: reconciliation retains the row and movie (`application/use_cases/reconcile_library_files.py:80-99`). Library actions include movies with registered missing rows.

Play/Open Folder flow:

~~~text
button → playback adapter → validate current path
  ├─ exists: Windows open/default player
  └─ absent: MissingMediaFileError → UI message only
~~~

Evidence: `src/dropsort/library/playback/windows.py:21-71`.

Inference: reconciliation follows the contract, but click-time discovery does not persist MISSING state, so UI/SQLite truth can remain stale.

Recommended future change: route Play/Open Folder missing discovery through an application use case that updates the selected `MediaFileId` and its movie projection.

## 11. TMDB / Network Coupling

Observed facts:

- Folder scanning and filename parsing are local.
- `ProposeMovieImport` calls provider search and returns `METADATA_UNAVAILABLE` when metadata cannot be reached (`application/media/propose_movie_import.py:47-124,177-193`).
- `ConfirmMovieImport` requires a provider candidate and calls `provider.get_movie()` before registration (`application/media/confirm_movie_import.py:35-69`).
- The schema requires provider/external ID; no pending local-only metadata identity exists.
- Card construction requests a poster; `PosterAssetService` is cache-first but `TmdbPosterSource` performs HTTP on a miss (`src/dropsort/ui/library/movie_card.py:63-68`, `src/dropsort/posters/service.py:23-39`, `posters/providers/tmdb.py:40-64`).
- Library and Personal Library text search are in-memory and make no network requests (`src/dropsort/ui/library/search.py:8-38`).

Inference: local registration is incorrectly coupled to TMDB enrichment, and opening Library can cause bounded background TMDB traffic on poster-cache misses.

Recommended future change: register a local movie/media file first with a durable pending/failed/needs-match metadata state. Make enrichment and poster fetching explicit or scheduled outside Library open.

## 12. Check Library Flow

~~~text
Check Library navigation/button
  → CheckLibraryPage starts Qt task
  → CheckLibraryUseCase
      → ReconcileLibraryUseCase (filesystem/status)
      → inspect metadata health
      → TMDB only for missing metadata
      → identity-preserving metadata update
  → progress shown on Check page
  → completed signal
  → MainWindow performs one dependent refresh
~~~

Evidence: `src/dropsort/ui/reconciliation/page.py:203-230,286-300`; `application/use_cases/check_library.py:36-198`; `ui/src/dropsort/ui/main_window/window.py:425-428,604-615`.

Observed fact: the visible Check Library workflow is explicit/manual, can mark missing, does not delete catalog rows, and refreshes after completion. The lower-level reconciliation is also invoked automatically at startup.

Recommended future change: make Check Library the sole trigger, then progressively emit item results without full projection reloads.

## 13. Filesystem Safety

~~~text
UI command
  → application authorization
  → PathPolicy.validate (approved roots, no reparse traversal, no collision)
  → journal CREATED/VALIDATED
  → EXECUTING
  → same-volume hard-link OR cross-volume copy
  → flush/fsync + size/hash/identity verification
  → journal FS_VERIFIED
  → remove source
  → atomic SQLite path update + COMMITTED
  → reversible plan / History recovery
~~~

Evidence: `core/safety/path_policy.py:29-112,153-166`; `core/file_engine/transfer.py:13-64,128-133`; `core/operations/service.py:69-144`; `core/operations/recovery.py:20-192`.

Implemented safeguards:

- approved-root and normalized/case-insensitive collision checks;
- same-file/physical-identity checks;
- rejection of symlink/junction/reparse traversal;
- no-overwrite finalization;
- verified copy/hash before source removal;
- durable state transitions before and after mutation;
- ambiguous recovery preserves both files;
- committed path and journal update share a transaction;
- Clear Library does not delete physical movie media.

Needs runtime evidence: real locked files, permission failure, reparse insertion race, real cross-volume interruption, disk-full behavior, and packaged recovery after process termination. A TOCTOU interval remains between validation and path operations.

Recommended future change: keep this engine unchanged until a dedicated adversarial task can add runtime evidence and, if justified, stronger handle-based Windows operations.

## 14. Background Workers / Signals / Threading

Observed facts:

- `QtTaskRunner` owns one retained `QThread` per task, queues progress, rejects stale delivery, and waits during shutdown (`src/dropsort/ui/common/tasks.py:97-193`).
- Poster work uses a separate `QThreadPool` capped at four (`ui/posters/loader.py:45-99`).
- Add Movies batches row creation with zero-delay timers.
- A shared runner permits multiple application tasks; mutation use cases use locks.
- No filesystem watcher is part of the active startup path.
- The problematic startup worker is automatic reconciliation, not an unbounded thread leak.

Inference: lifecycle management is deliberate, but broad signals from a safe worker still produce unnecessary UI work.

Recommended future change: remove the startup task and narrow refresh signals by identity; retain stale-callback protection and shutdown waits.

## 15. Search

Observed facts:

- Library search filters loaded `MovieListItem` values by title/original title/year (`src/dropsort/ui/library/search.py:8-38`).
- Personal Library search filters its cached section.
- Search does not query SQLite, call TMDB, or start workers.
- Grid filtering hides/shows retained cards rather than creating duplicates.

Inference: core search is local and inexpensive. One current test reports sidebar input being cleared unexpectedly on show.

Recommended future change: fix that regression separately without replacing the local filtering model.

## 16. Personal State

Observed facts:

- Preference, watchlist membership, and watch events are normalized SQLite records keyed by stable `MovieId`.
- Like/Blacklist/Watchlist/History update through application actions.
- Personal Library caches section projections and invalidates hidden views.
- Movie Details can update personal controls without a full Library rebuild.
- One test reports the mark-watched-date button enabled when its expected initial state is disabled.

Inference: identity and persistence are sound; UI-state and clear semantics require correction.

Recommended future change: repair the focused UI-state regression, then define which history remains visible after a full active-state clear.

## 17. Clear Library Data

~~~text
Settings confirmation
  → ClearLibraryData.execute
  → acquire operation lock
  → reject nonterminal journal state
  → SQLite transaction:
      delete media/catalog metadata
      delete movies only when no personal/watch references remain
  → delete app-owned poster cache
  → preserve user movie files and operation journal
~~~

Evidence: `src/dropsort/application/use_cases/clear_library_data.py:19-60`; `src/dropsort/database/repositories/library_maintenance.py:22-65`.

Observed fact: physical movie files are untouched, and recovery-relevant operations are protected. Active liked/blacklisted/watchlisted state and visible watch history keep referenced movies alive.

Inference: filesystem safety is correct, but active application state is not empty as required by `The Idea-v3.md`; history remains active rather than inert.

Recommended future change: define an atomic “clear active state” projection that clears personal/watch activity while preserving only explicitly inert audit/recovery records.

## 18. Packaging / Runtime

Observed facts:

- Runtime DB, poster cache, config, and logs resolve under per-user AppData (`application/runtime/paths.py:22-40`).
- Logging rotates and redacts secret patterns (`application/runtime/logging.py:9-47`).
- `DropSort.spec` bundles migrations, Fluent icons, fonts, TMDB assets, icons, and license notices.
- `packaging/build_release.ps1` assumes `.venv\Scripts\pyinstaller.exe` and writes `release/DropSort`.
- `pyproject.toml` package-data does not enumerate SQL migrations, Fluent SVGs, or application icons.

Inference: the PyInstaller path is likely complete, but a wheel/non-editable installation may omit runtime resources. Packaged visual behavior, live TMDB, normal-user launch, and DPI variants were not rerun.

Recommended future change: add an installation-resource test or declare PyInstaller the only supported distribution path; reconcile package data accordingly.

## 19. Repository Hygiene / Security

Observed workspace artifacts included virtual environments, bytecode, pytest temp/cache trees, coverage outputs, build trees, release output, runtime SQLite files, and ZIP backups. They remain on disk but are excluded from Git.

`.gitignore` now covers environments, bytecode, test/coverage artifacts, build/release trees, ZIPs, SQLite/WAL/SHM, logs, environment files, poster cache, editor/OS files, and audit scratch directories.

No secret value was printed. The filename scan found no credential artifact beyond the legitimate `metadata_credentials.py` module. Three workstation-username path literals were replaced with generic/redacted equivalents before staging. The decisive staged-content scan is recorded in section 26. There is no first-party license file; `THIRD_PARTY_NOTICES.md` is not a project license grant.

Recommended future change: add an explicit project license before treating this public repository as a distributable open-source release.

## 20. Test and Verification Results

Command:

~~~powershell
.\.venv\Scripts\python.exe -m pytest -q -W error `
  --basetemp=.audit-pytest-temp -o cache_dir=.audit-pytest-cache
~~~

Result on 2026-08-23:

- Python: 3.12.10
- PySide6: 6.11.1
- pytest: 9.1.1
- Collected: 1,188
- Passed: 1,172
- Failed: 11
- Skipped: 5 (host symlink privilege unavailable)
- xfailed/xpassed: 0/0
- Duration: 438.21 seconds; `python -m compileall -q src` also passed.

Failing baseline tests:

1. Arabic approved-copy source mismatch.
2. Bootstrap initial section expected `library` but was empty with `load_on_show=False`.
3. Import fallback expected Library but current section was empty.
4. History fallback expected Library but current section was empty.
5. Mark-watched-date button initial state mismatch.
6. Expanded sidebar margin mismatch.
7. Official theme token not emitted.
8. All semantic theme tokens not emitted.
9. UI architecture regex flags localized prose as SQL.
10. Sidebar search text clears unexpectedly on show.
11. Source-inspection test finds `unpolish` in a comment.

Observed fact: this is not a green baseline. Historical reports claiming fewer tests and full success are stale.  
Inference: most failures are visual/localization/test-contract drift, not file-safety failures, but they must be triaged.  
Recommended future change: address these in a focused baseline-stabilization task after the startup product contract is specified.

## 21. Comparison Against The Idea-v3.md

| Product rule | Current result | Evidence summary |
|---|---|---|
| Local-first / SQLite source | Mostly aligned | Local DB and cached projections are authoritative |
| Stable MovieId/MediaFileId | Aligned | Stable PKs and in-place relink |
| Registered may be missing | Aligned | `MISSING` persists without deletion |
| TMDB optional enrichment | Not aligned | registration requires candidate/details |
| Startup loads local state only | Aligned in Pass 1 | one local Library snapshot; no startup reconciliation |
| Check Library explicit/manual | Aligned in Pass 1 | reconciliation starts only from explicit user action |
| Incremental UI updates | Aligned for reconciliation in Pass 1 | changed MovieIds use one-item summary queries; unaffected cards persist |
| Opening Library avoids network | Not aligned | poster cache miss can call TMDB |
| Safe filesystem operations | Strongly aligned in code | policy, journal, verification, recovery |
| Explicit recovery state | Aligned | durable nonterminal/recovery states |
| Clear never deletes movie files | Aligned | only DB/app cache are cleared |
| Clear leaves empty active state | Not aligned | personal state/history retain active movies |
| Historical state inert | Not aligned/undefined | watch history remains visible/active |
| Play missing updates truth | Not aligned | error only; no persistence update |

## 22. Classification

### Already correct

- Python/PySide6/SQLite modular-monolith direction.
- Stable movie/media/watch-event identities.
- Missing rows retained and relinked in place.
- Local Library/Personal search.
- Stable-ID MovieGrid diffing and cached navigation.
- Explicit Check Library page behavior.
- Durable no-overwrite filesystem engine and recovery journal.
- AppData paths, redacted logs, migration discipline.
- Clear Library protection of physical user media.

### Needs modification

- Startup composition and reconciliation signals.
- Item-level Library projection/update APIs.
- Play/Open Folder missing-state persistence.
- Clear Library active personal/history semantics.
- Package-data completeness or distribution contract.
- Stale baseline/status documentation.
- Eleven failing tests/contracts.

### Must be removed/replaced

- Automatic startup reconciliation trigger.
- Progress-triggered full Library snapshot refresh.
- Assumption that TMDB identity must exist before local registration.

### Missing entirely

- Local-only registration and pending/failed/needs-match metadata state.
- Item-level Library summary query/signal keyed by `MovieId`.
- Persisted metadata/poster enrichment lifecycle.
- First-party project license.

### Needs further runtime evidence

- Real cross-volume interruption and disk-full recovery.
- Locked/permission-denied media on native Windows.
- Reparse/TOCTOU mutation during authorization.
- Packaged launch, resources, live TMDB, offline behavior.
- 100/125/150% DPI and clean-profile checks.
- Wheel/non-editable installation.

## 23. Highest-Priority Risks

1. **Product-contract risk (remediated in Pass 1):** startup no longer reconciles files or repeats broad refreshes.
2. **Local-first risk:** TMDB failure prevents valid local registration.
3. **Privacy/network risk:** opening Library can fetch posters on cache miss.
4. **State-semantics risk:** Clear Library does not produce empty active state.
5. **Truth-consistency risk:** Play/Open Folder missing discovery is not persisted.
6. **Release-confidence risk:** 11 tests fail and runtime/package gates remain unverified.
7. **Distribution/legal risk:** resources may be incomplete outside PyInstaller and no first-party license exists.

No critical user-file-loss path was found in the inspected mutation core. This statement is limited to static inspection plus the current suite; adversarial runtime evidence remains necessary.

## 24. Recommended Next Implementation Step

Write a narrowly scoped specification and tests for **local-only startup plus explicit incremental Check Library**:

1. remove the initial automatic reconciliation call;
2. prove startup performs one local Library query and no filesystem/TMDB/poster network work;
3. make Check Library the only reconciliation trigger;
4. add item-level status projection/update signals and one final fallback refresh;
5. instrument query/card/update counts to prove the flicker mechanism is gone.

Do not combine offline registration, Clear Library semantics, or UI redesign into that first implementation change.

## 25. Files Changed During This Task

Application source changed: none.  
Tests changed: one privacy-only Windows user-path fixture was generalized; assertion behavior is unchanged and its focused file rerun passed 4/4.

Baseline-only changes:

- added `docs/audits/Deep-Audit.md`;
- expanded `.gitignore`;
- preserved remote `Skills.md`;
- staged the existing Python source, tests, specifications, documentation, assets, and packaging metadata on top of remote history;
- reconciled the useful local `README.md` with the remote seed README, whose only content was the repository title.

Generated artifacts, runtime DBs, caches, environments, release output, and ZIP backups were not deleted; they were excluded.

## 26. GitHub Upload Result

Target: `https://github.com/Mohaythem/DropSort-Media-Library`  
Branch: `codex`  
Remote base inspected: `d5d7fb2f0887904ee2f882eae7cb6421568f5522`  
Integration: the dedicated audited-baseline branch descends from the fetched remote history; remote `Skills.md` is preserved. The accidental `main` upload is removed with ordinary revert commits, never force-push or history rewrite.

Baseline commit SHA: `ce4e87ff44a57dd0513969dada0e2cc8065d092f`.  
Documentation-organization commit: `1fe58272ad7b7970e89b52fb4dd65296b0afc0ed`.  
Dedicated-branch rename: `origin/codex` was created and directly verified at `ffc8a6883b8e0a4c97f575bc589ae6273e85c037`; the old `origin/codex/audited-python-baseline` ref was then removed.  
`main` correction: ordinary fast-forward to revert commit `02ba6ba4d42a316357530c661993277e0723f9ee`; its tree exactly matches pre-baseline commit `d5d7fb2f0887904ee2f882eae7cb6421568f5522` and contains only the original `README.md` and `Skills.md`.  
No force push or history rewrite was used. The final dedicated-branch SHA containing this record is reported in the final task handoff.

## 27. Python Stabilization Pass 1 Remediation

Implemented on 2026-08-23 on branch `codex`:

- removed automatic startup reconciliation and progress-triggered full Library reloads;
- retained Check Library as an explicit manual action;
- added committed `media_file_id`, `movie_id`, and availability-status progress identity;
- added a one-movie SQLite summary query and `GetMovieListItem`;
- updated only affected cached cards while retaining missing movies;
- added eight focused startup, manual-check, identity, query, and UI-stability tests.

Verification: `29 passed` in the focused stabilization/MainWindow gate. The full suite reported
`1,175 passed, 11 failed, 5 skipped`. The 11 failures exactly match section 20's audited baseline;
compileall and an offscreen startup smoke guarded against any reconciliation invocation also passed.

Deferred by scope: poster cache-miss network behavior, offline registration, Play/Open Folder
missing-state persistence, Clear Library semantics, packaging rebuild, and UI redesign.

## 28. Python Stabilization Pass 2 Remediation

Implemented on 2026-08-25 on branch `codex` from accepted SHA
`92fdfb537e164d936e84071047ca1bbc173d0cd9`.

Pass 2 replaces the external-identity-first import assumption with two explicit boundaries.
Transaction A registers a provisional `PENDING` Movie and its MediaFile atomically from local
parser/discovery facts, with no TMDB, poster, filesystem mutation, or operation-journal call.
Transaction B performs metadata I/O before its write transaction, reloads by stable `MovieId`, and
updates the same Movie to `READY`, `PENDING`, `FAILED`, or `NEEDS_MATCH`.

Migration `0005_offline_movie_registration` permits only null/null or populated/populated external
identity pairs, rejects blanks and invalid states, keeps populated identity uniqueness, and migrates
existing Movies to `READY`. The foreign-key rebuild is atomic, runs `foreign_key_check` before
acceptance, restores enforcement on success/failure, preserves all referenced identities/state/
operation evidence, and has a fail-closed down migration.

External identity collisions use Strategy B: both Movies, their MediaFiles, personal/watch state, and
operation attribution remain separate; the provisional Movie becomes `NEEDS_MATCH`; the result
exposes both IDs; no automatic merge or title/year deduplication occurs.

The UI now permits explicit local Add for no-match and metadata-unavailable rows, publishes the
Transaction A result immediately, inserts/updates only the affected Library card, then schedules
Transaction B separately. Identity-less Movies use safe poster placeholders. Check Library retains
them as valid local members and remains explicit/manual.

Verification: migration `5 passed`; focused Add/enrichment/import `72 passed`; final two-stage
callback gate `46 passed`; persistence/personal/Check Library/Pass 1 gate `95 passed`; full suite
`1,217 collected, 1,202 passed, 10 failed, 5 skipped` in 333.77 seconds. The accepted baseline had
11 failures; the remaining ten are the isolated pre-existing UI/localization/theme/source-inspection
contracts, so Pass 2 adds no failure. Compileall, diff whitespace, native disposable offline launch,
restart persistence, and deterministic same-ID `PENDING -> READY` smoke passed. Live TMDB remains
unverified because no credential was configured.

Deferred by scope: the full poster-network redesign, Clear Library semantics, missing-state
persistence from Play/Open Folder, changed-file physical identity, packaging/release work, code
signing, licensing, and release-verifier redesign. See
`docs/reports/python-stabilization/02-offline-registration-tmdb.md`.

## 29. Python Stabilization Pass 3 Remediation

Implemented on 2026-08-25 on branch `codex` from accepted SHA
`83d0d8e4ff9ab3975581bb5f2ec0da83cc162d58`.

Personal Library requests now carry section plus generation ownership. Late results may warm only
their own still-current cache and can paint only the section/request they own. Uncached targets use
a localized target loading/error state; same-section stale content is permitted, while cross-section
content is impossible.

Clear Library now atomically removes every active Movie, MediaFile, metadata-cache row, personal
preference/watchlist row, and Watch Event. It preserves physical media and the immutable
filesystem-operation/recovery journal. UI success handling explicitly drops Library cards/source,
all Personal caches, details state, and old search suggestions, then performs one authoritative local
Library reload.

MovieCard, MediaFile panel, and Operation History row presentation now follow stable MovieId,
MediaFileId, and operation-id identity. Card DTO updates mutate visible fields in place and restart a
poster request only when poster identity changes. Personal invalidation is projection-specific, and
manual Check Library coalesces repeated identical change identities within one run.

The isolated runtime A/B trace proved startup performs one Library source load before show and no
post-show full reload. Poster suppression did not remove the four observed geometry passes; cached
and uncached posters both use per-card delivery, while only uncached assets fetch. Poster networking
was therefore not redesigned.

Verification: 13/13 new acceptance tests; Clear gate 7/7; startup/refresh/check gate 65/65; full
suite 1,230 collected, 1,215 passed, 10 failed, 5 skipped in 331.82 seconds. The same ten accepted
baseline failures remain and no new failure was introduced. Full evidence is in
`docs/reports/python-stabilization/03-refresh-state-flicker-remediation.md`.

Deferred deliberately: Poster Phase 3, V7 UI port, packaging/release, and unsafe derivation of a
MovieId from History results that currently expose only MediaFileId.
