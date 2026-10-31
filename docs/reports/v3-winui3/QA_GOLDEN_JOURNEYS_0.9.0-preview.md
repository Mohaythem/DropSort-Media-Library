# TBD Movies — Golden Journeys + Scale QA (0.9.0-preview)

Date: 2026-09-25 · Branch: `codex` · Installed build tested (not Debug).

## Correction (2026-09-26): empty Library root cause + fix
An earlier version of this report presented startup timing and raw DB integrity as scale evidence. That
was **not** proof of a populated, Library-visible dataset. When the installed app was pointed at the
seeded 10K root, the Library was **empty**.

- **Root cause (Case B — data did not satisfy the read contract):** the QA seeder inserted `overview = ''`
  (empty string) for ~1/5 of movies. The production read path maps every `movies` row through the `Movie`
  domain type, which requires `overview` to be **null or non-empty**. `Movies.ListAll()` therefore threw
  `ArgumentException: overview must be null or a non-empty string`; `LibraryService.ListMovies()` propagated
  it and `LibraryPage` fell into its catch, so the grid showed nothing. Raw SQL counts were non-zero the
  whole time — the rows existed but were not read-path-visible. Normal TBD Movies import stores NULL, never ''.
- **Fix:** the seeder now inserts `NULL` (never `''`) for absent overview, matching real application data.
  No production code changed. Schema stays v13.
- **New verification:** `TBD Movies.QaSeed libcheck <db>` runs the real catalog read path
  (`CatalogUnitOfWorkFactory` → `Movies.ListAll()` / `TvShows.ListAll()` — the same repositories/queries the
  app's `LibraryService`/`TvLibraryService` use) and **fails (exit 1)** on a throw or zero rows.

Read-path results after the fix (real production query + domain mapping):
| Scale | Movies.ListAll | TvShows.ListAll | libcheck |
|---|---|---|---|
| 1K | 1000 | 120 | PASS |
| 5K | 5000 | 600 | PASS |
| 10K | 10000 | 1200 | PASS |

Installed app relaunched at 1K and 10K after the fix: window shown, process alive (WorkingSet ~214 / ~246 MB).
The three "Executed results" tables below (startup, memory, raw integrity) remain accurate for what they
measure — **process startup against an isolated DB** and **raw DB integrity** — but only the read-path table
above and the human GUI checklist constitute populated-Library evidence.


## Scope note (honest)
This agent environment cannot drive or observe the WinUI GUI (scroll smoothness, felt search latency,
artwork rendering, memory-under-scroll, click-through journeys). Per the agreed approach ("env-var data
root + I seed, you click"), this pass **executed** all automatable backend, integrity and installed-app
startup/memory QA against isolated synthetic data, and **defers** the interactive GUI journeys to a human
checklist (below). Interactive journeys, removable-drive, and clean-machine verification are marked
`NOT EXECUTED` where a human or hardware is required — they are not claimed.

## Environment
- OS: Windows 11 Pro build 26200
- CPU: Intel Core i7-13620H (16 logical) · RAM: 15.7 GB · Disk: SSD
- App data path (default): `%LOCALAPPDATA%\TBD Movies`
- Schema version: 13

## Artifact under test
- Installed: `%LOCALAPPDATA%\Programs\TBD Movies\TBD Movies.UI.exe`, version `0.9.0-preview`, self-contained win-x64.
- Setup: `artifacts/release/0.9.0-preview/TBD MoviesSetup.exe` (rebuilt with the data-root override).
  SHA-256 `4E87498A949A3C91E1AF12F4615CE661E93ED608768B93D272E0FC11FEE2FD9B`, size 34,715,016 bytes.
- Automated suite baseline: **381 / 381** passing.

## QA data isolation
- The installed app resolves its data folder from `TBD Movies_TEST_DATA_ROOT` (absolute path) when set,
  else the default `%LOCALAPPDATA%\TBD Movies`. This override was moved from Debug-only to all
  configurations so the installed Release build can run against a disposable profile. It is inert unless
  the variable is set and never changes the default location.
- QA profiles live under `%LOCALAPPDATA%\Temp\TBD Movies-qa\{1k,5k,10k}` (disposable). The real user
  library at `%LOCALAPPDATA%\TBD Movies` was never pointed at, moved, or mutated.

## Synthetic dataset generator (reproducible)
- Tool: `tools/TBD Movies.QaSeed` (reuses the production `DatabaseMigrator`, so seeded DBs are the real v13
  schema, then bulk-inserts). Run: `dotnet run --project tools/TBD Movies.QaSeed -c Release -- <root> <movies> <shows> [seed]`.
- Variety: non-ASCII and long titles; distinct identity for duplicate-looking titles (unique provider/
  external_id); movies with present / missing / registered-only files; ~1/7 missing posters; metadata
  status mix; TV shows with 1–3 seasons and present/missing episodes; 5 custom lists incl. an Arabic name;
  movies in multiple lists; mixed movie+TV membership; movie and episode watch events.
- Seeded (deterministic seed 20260925):
  - 1K: movies=1000 shows=120 episodes=1086 lists=5 members=2120 watch_events=700
  - 5K: movies=5000 shows=600 episodes=5512 lists=5 members=2800 watch_events=700
  - 10K: movies=10000 shows=1200 episodes=10694 lists=5 members=2800 watch_events=700

<!-- APPEND_QA -->
## Executed results (automatable)

### Installed-app startup to main window (isolated data, 2 runs each)
| Scale | Run 1 | Run 2 | Window shown | Process survived |
|---|---|---|---|---|
| 1K | 987 ms | 738 ms | yes | yes |
| 5K | 923 ms | 987 ms | yes | yes |
| 10K | 1186 ms | 1205 ms | yes | yes |

Methodology: installed EXE launched with `TBD Movies_TEST_DATA_ROOT` set; wall-clock from process start
until `MainWindowHandle != 0` (proxy for time-to-usable window); process fully stopped between runs.
No established SLO exists, so no pass/fail threshold is invented. Observation: startup is ~0.7–1.2 s and
grows only modestly from 1K to 10K (roughly flat, no nonlinear blow-up, no hang, no immediate exit).

### Installed-app memory (WorkingSet)
| Scale | ~3 s after window | ~11 s idle |
|---|---|---|
| 1K | 211 MB | 211 MB |
| 10K | 223 MB | 224 MB |

Startup/idle only. +12 MB from 1K to 10K; stable at idle (no growth at rest). Scroll-driven memory is a
GUI metric (deferred to the human checklist).

### DB integrity + invariants at scale (after the installed app opened each DB)
All three profiles, read-only verify:
`integrity_check=ok · user_version=13 · fk_violations=0 · dup_movie_members=0 · dup_show_members=0 ·
bad_member_rows=0 · orphan_members=0`.
Evidence: the installed Release build opened 1K/5K/10K DBs, kept schema v13 (no migration replay), and
left no FK violation, no duplicate custom-list membership (per-kind partial unique holds), no membership
row violating the exactly-one-target CHECK, and no orphaned membership.

### Automated suite
`dotnet test` → **381 / 381** (baseline and unchanged; production change was the data-root override only).

### Backend trust guarantees (covered by the suite, re-confirmed)
Duplicate custom-list names (incl. Unicode), mixed movie+TV membership, per-kind dedup, list/media delete
cascades, migration backup fail-closed, StorageUnavailable vs Missing — all green in the 381 suite.

## NOT EXECUTED (need a human at the GUI or specific hardware)
1. Interactive Golden Journeys A–I clicked in the running app (empty state, add movies/shows, custom-list
   UI flows, watch-state UI, organize/relink UI, restart-via-GUI).
2. Scroll smoothness / virtualization feel, felt search latency, sort/filter freeze, My Lists rendering.
3. Artwork loading behavior in the GUI (placeholder geometry, no download loop, UI-thread stalls).
4. Journey H physical removable-drive (`StorageUnavailable`) — no removable drive attached.
5. UNC / slow-network path behavior — no safe network target.
6. Clean-machine / VM install verification.
7. Abnormal-termination startup recovery observed in the GUI.

## Manual GUI checklist (run against the installed app; set the QA data root first)
Set once per shell, then launch the installed app:
`setx-free:  $env:TBD Movies_TEST_DATA_ROOT="$env:LOCALAPPDATA\Temp\TBD Movies-qa\10k"` then start
`%LOCALAPPDATA%\Programs\TBD Movies\TBD Movies.UI.exe`. Then verify (5 groups):
1. Library: cards appear quickly; scrolling stays smooth; posters fall back to placeholder with correct
   2:3 geometry (QA poster refs are synthetic, so all show placeholders — no layout collapse, no runaway).
2. Search/Sort: type a common prefix, a rare title, and no-result; switch Movies/TV; Date added/Title/Year
   both directions — no multi-second freeze.
3. Custom lists: open the 5 seeded lists (incl. the Arabic-named one and mixed movie+TV); Add media search;
   remove from one list; rename; confirm counts and restart persistence.
4. Navigation: Home from a detail page; `Esc` Back; dialog `Esc` closes without navigating.
5. Restart: close and reopen; confirm library, lists, watch history persist and the sidebar matches the page.

## Findings
No `BLOCKER`/`HIGH`/`MEDIUM` defect was found in the executed (backend + startup + integrity) scope.
- `OBSERVATION` — "Date added" sort orders by catalog id (documented monotonic proxy; `MovieListItem`
  carries no date). Not a defect at these scales; precise date sort would need the DTO to carry the date.
- `OBSERVATION` — custom-list read caps at 1000 members (`ListCustomListItems` default). Seeded lists stay
  under this; a >1000-member list would truncate in My Lists. Reproduce/confirm during the GUI checklist.
- Deferred risks below remain open.

## Deferred release risks (unchanged; not closed by this pass)
Crash-safe journaled restore + user-facing restore workflow; backup ACL/privacy; UNC/network timeout;
real removable-drive verification; Home/Recently-Watched stale availability hints; large-library GUI
scroll/latency/memory verification; clean-machine/installed-app verification; code signing (unsigned).

## Large-library performance blocker + fix (2026-09-26)
Manual test at 10K exposed a severe blocker: the Library took ~1 hour to become usable and opening a
card then hung. (The earlier "~0.7–1.2 s to MainWindowHandle" numbers are **process/window-start timing
only** — a window appeared; the populated Library had not finished loading and was not interactive.)

**Two independent root causes, both measured/proved from source:**
1. **N+1 write-gated transactions in `LibraryService.ListMovies()`.** `MapToListItem` called
   `IMediaFileRepository.GetByMovieId(id)` per movie, and each `factory.Begin()` opens a connection and a
   write-gated IMMEDIATE transaction. Measured on the UI thread: **1K ≈ 2.0 s, 5K ≈ 16.7 s, 10K ≈ 34 s**
   (`Movies.ListAll` + N+1). Fix: one bulk `ListMovieIdsWithPresentFile()` query — **10K: 34,079 ms → 8 ms**.
   `ListPersonalMovies` now maps from the summary's own file counts (no per-item query).
2. **Broken GridView virtualization.** `LibraryPage`/`MyListsPage` nested the virtualizing `GridView`
   inside the page `ScrollViewer`, which gives it unbounded height and realizes **every** card (all 10K)
   plus fires 10K `MovieRecord.PosterSource` getters. Fix: the grid now owns its scroll (fixed header
   `Auto` row + grid `*` row); `ItemsWrapGrid` virtualizes, so only visible cards realize.

Both were separate causes. Card/Details hang was a consequence of the same load: the UI thread and thread
pool were saturated (34 s query block + 10K container builds + 10K poster tasks), so any subsequent
navigation waited behind it. No exception/crash in the mapper — pure saturation.

**Installed Release after the fix, 10K QA profile:** window ~2.0 s; WorkingSet **227 MB flat**; total CPU
**2.1 s and unchanged from t=3s to t=23s** (work finishes in ~3 s, then idle). Before: ~1 hour + hang.
A flat ~227 MB working set (same as a tiny library) is strong evidence only visible cards are realized.

**Regression cover:** `LibraryScaleContractTests` asserts `ListMovies()` performs one bulk presence query
and zero per-movie `GetByMovieId` calls; `UiSourceContractTests` asserts the grid pages own their scroll
(no page `ScrollViewer` wrapping the grid). Full suite **382 / 382**.

## Library interaction cleanup (2026-09-26)
The first human 10K GUI pass (after the performance fix) found four Library issues; all addressed here.

1. **Could not scroll vertically (blocker).** The shared `MediaGridViewStyle` sets the GridView's
   `ScrollViewer.VerticalScrollMode=Disabled` — correct when the grid lived inside the page ScrollViewer,
   wrong once the grid became the scroll owner. The grid had a bounded viewport (so it still virtualized)
   but scrolling was off, clipping everything past the first screen. Fix: the Library/My Lists GridViews
   override `VerticalScrollMode=Enabled` + `VerticalScrollBarVisibility=Auto` (wheel, touchpad, scrollbar,
   keyboard, and focus-scrolls-into-view all work; virtualization unchanged).
2. **Grid did not use full width.** Same disabled-scroll/measurement state left the grid narrow. Fix: the
   GridViews set `HorizontalAlignment=Stretch`; `ItemsWrapGrid` (fixed 200-epx cells, 2:3 posters) now
   reflows columns across the page width. No fixed Width/MaxWidth; no horizontal scroll.
3. **Visible "Esc" badge.** The shell-wide Escape `KeyboardAccelerator` surfaced its accelerator
   tooltip/badge during focus. Fix: `RootGrid.KeyboardAcceleratorPlacementMode="Hidden"` — the shortcut
   still works, no badge.
4. **Tab focused off-screen / decorative elements.** The card templates already contain only decorative
   non-focusable elements (Border/Image/FontIcon/TextBlock), so each GridView item is one focus unit; the
   symptom was focus moving to items that could not scroll into view (a consequence of #1). Enabling grid
   scroll makes the focused item scroll into view. Contract test locks the "one focus unit per card" rule.

**Re-verified, installed Release, 10K:** window ~1.6 s; WorkingSet **241 MB flat**; CPU **2.2 s, unchanged
t=3s→t=15s** — same performance class as the perf-fix build, so virtualization is intact. Scroll ownership
is the GridView's own ScrollViewer (not a page ScrollViewer). Full suite **385 / 385**. Visual confirmation
of scroll feel, column reflow, no-Esc-badge and one-stop-per-card focus is the user's pending GUI check.


## Grid right-side dead region (2026-09-26)
Follow-up manual finding: the Library grid left a right-side blank region about one card wide.

**Measured (instrumented the real visual tree, then removed the instrumentation):** at a 1611 epx grid
viewport the ItemsWrapGrid was 1547 epx wide (viewport − 64 epx page padding) with `ItemWidth=200`, giving
**7 columns** (1400 epx) and a 147 epx remainder. The card cell (`MediaCardItemWidth=200`) equalled the
full card footprint (poster 180 + 2 epx border ×2 + 4 epx padding ×2 + 8 epx item margin), so the leftover
was a genuine sub-cell remainder — dumped on the right and, with the 32 epx right page padding, reading as
~one missing card.

**Fix (poster geometry unchanged):** drop the 8 epx horizontal item margin (`Margin 0,0,8,16 → 0,0,0,16`)
and set the cell to the resulting footprint (`MediaCardItemWidth 200 → 192`). Poster stays 180×270 (2:3);
inter-card gap becomes padding+border (12 epx, was 20 epx). No `MaxWidth`, no `MaximumRowsOrColumns`,
GridView still `HorizontalAlignment=Stretch` and still the scroll owner.

**Re-measured after the fix, same 1611 viewport:** `ItemWidth=192` → **8 columns** (1536 epx), remainder
11 epx — the extra card fills the region. Columns still follow width (5 at the ~1071 default viewport,
unchanged). Installed Release 10K: window ~1.5 s, WorkingSet **239 MB flat**, CPU idle after ~3 s — same
performance class; virtualization intact. Card size did **not** change; only the inter-card gap tightened
20→12 epx. Full suite **385 / 385**. Final visual width confirmation is the user's pending GUI check.

## Release Hardening + RC gate (2026-09-26)
Executed against the accepted `0.9.0-preview` installed build (SHA-256 `3166F077…`, 34,722,663 bytes),
schema v13, suite 385/385.

- **Restore crash-safety (Phase 4/5): not a shipping risk — restore is unreachable.** `RestoreFromBackup`
  has no production/UI/startup caller (only `DatabaseSafetyBackupTests`), so an interrupted restore cannot
  occur in the field. The primitive itself preserves the current DB with a WAL-safe `.pre-restore` snapshot,
  stages `.restore-incoming`, swaps with an atomic same-volume `File.Replace`, validates, and rolls back on
  exception — so the target is always atomically old-or-new. It is **not** a fully journaled restore with
  startup reconciliation; that plus the user-facing restore workflow remain a documented deferred pre-1.0
  item. No implementation change made (implementing a state machine for unreachable code would be
  gold-plating).
- **Backup privacy/ACL (Phase 6): OK.** Backups live in `%LOCALAPPDATA%\TBD Movies\backups`. The code sets no
  ACLs, so the folder inherits the per-user `%LOCALAPPDATA%` permissions — same isolation as the database
  itself; not made more permissive. No unsafe permission change; no elevation. A dedicated owner-only ACL
  remains an optional future hardening.
- **UNC/network (Phase 3): code-audited, physical NOT EXECUTED.** The Library load path performs no
  filesystem probing (fixed with the bulk presence query); availability is probed off the UI thread, once
  per distinct storage root (`AppServices.UnavailableStoragePaths`/`IsStorageAvailable`). Play/Open Folder
  do a single `File.Exists` on click (bounded). No safe UNC share available → real slow/dead-share behaviour
  `NOT EXECUTED`.
- **Removable drive (Phase 2): NOT EXECUTED** — no physical removable drive attached. Present/Missing/
  StorageUnavailable semantics are covered by the automated suite and the earlier Drive-Aware pass; the
  physical connect/eject/reconnect sequence needs the user's hardware.
- **Clean-machine (Phase 8): NOT EXECUTED** — no VM/Windows Sandbox/second machine available (Sandbox not
  installed). Installed-artifact independence is otherwise evidenced: the installed app runs from
  `%LOCALAPPDATA%\Programs\TBD Movies` self-contained (Phase 10), 10K smoke path confirms it.
- **Code signing (Phase 9): UNSIGNED — production code signing pending.** No certificate available. The
  Setup EXE and app EXE would receive Authenticode signatures (with timestamping) as a post-build step;
  until then Windows SmartScreen will warn on first run. External/distribution item, not a code defect.
- **Installer lifecycle (Phase 7):** fresh install, existing-data preservation, uninstall and reinstall were
  verified earlier this session on this machine; true cross-version upgrade `NOT YET AVAILABLE TO TEST` (no
  prior released version); the fixed Inno `AppId` structurally supports future in-place upgrade.
- **DB integrity (Phase 11) + 10K smoke (Phase 12):** 10K QA DB after all tests: `integrity_check=ok`,
  v13, 0 FK violations, 0 duplicate/bad/orphan members. Installed 10K smoke: window ~1.5 s, ~239 MB flat,
  CPU idle after ~3 s, running from the installed path — no regression.

**RC gate: NOT READY FOR RELEASE CANDIDATE.** No reachable technical BLOCKER or HIGH product defect was
found. The gate is held only by verifications that require resources not available here, kept explicitly
separate from product defects:
- removable-drive physical test — `NOT EXECUTED` (needs hardware);
- clean-machine install — `NOT EXECUTED` (needs a VM/Sandbox/second PC);
- UNC/slow-share behaviour — `NOT EXECUTED` (needs a network share);
- code signing — `UNSIGNED, pending certificate` (distribution item);
- human visual confirmation of the accepted Library GUI at 10K.

No RC artifact was built and no version was bumped (still `0.9.0-preview`), per the gate-first rule.



