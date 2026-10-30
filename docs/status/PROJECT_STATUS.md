# DropSort Project Status

Last verified: 2026-08-25 on native Windows / Python 3.12 / NTFS.

## Add Movies / RTL / theme follow-up status

**IMPLEMENTED AND SOURCE-VERIFIED ON `codex`.** Add Movies uses one page-level vertical scroll,
stable title/year/resolution/status/action columns, compact Add/Search/icon actions, bounded candidate
selectors, and no normal-row filename/path or diagnostic prose. Selection remains proposal-only and
every catalog import still requires an explicit Add action; the UI performs no direct filesystem,
SQL, or HTTP work.

Arabic presentation now retranslates centralized genre display names and explicitly applies RTL
alignment to Personal Library and Check Library content while paths, years, resolutions, progress
values, and other technical text remain LTR with Western numerals. Main is retained only as a
persisted compatibility identifier and migrates to Slate; Settings exposes exactly Slate, Dark, and
Light, with Slate as the default and a higher-contrast Dark palette.

Verification: changed/affected UI gate `149 passed, 2 accepted baseline failures`; architecture,
localization, RTL, and visual-contract gate `72 passed, 2 accepted baseline failures`; full suite
`1,245 passed, 6 failed, 5 skipped` in 324.79 seconds. All six full-suite failures are established
baseline contracts outside this diff. Compileall and diff whitespace checks passed. The synthetic
Add Movies runtime probe rendered eight mixed-state rows with one scroll area, zero transient
top-level controls, and zero activation churn. Ruff was unavailable in the active environment.



## Python Stabilization Pass 3 status

**IMPLEMENTED AND SOURCE-VERIFIED ON `codex`.** Personal Library async delivery is owned by
request generation and section, so late or failed loads cannot paint another tab. Same-section stale
snapshots remain available; uncached targets use a localized loading/error state.

Clear Library now removes all active Movies, MediaFiles, metadata cache, Likes, Blacklist,
Watchlist, and Watch Events in one transaction while preserving physical media and immutable
filesystem operation/recovery evidence. Successful UI delivery discards all Library/Personal card
snapshots, details state, and old search suggestions before exactly one authoritative local reload.

Stable MovieId DTO changes update the same MovieCard and restart poster work only for a changed
poster identity. Media panels and History rows retain stable MediaFileId/operation-id ownership.
Personal invalidation is projection-specific, layered grid presentation is reduced to one source
presentation call, and repeated identical Check Library change identities are coalesced per run.

Runtime tracing across empty, one-card, multi-card uncached, multi-card cached, and poster-suppressed
startup proved one pre-show Library load and no post-show full reload. The confirmed remaining chain
was per-card poster completion through `QLabel.setPixmap()` and a Window UpdateRequest. A grid-owned
coordinator now stages visible pixmaps and presents each ready wave together, with a 100 ms maximum
wait and no card/grid rebuild. Cached results that finish before visibility classification are staged
safely. Poster loader, cache, TMDB/network, placeholders, and offline behavior are unchanged.

Native production-view measurement changed Library poster-correlated presentation/Window-update
counts from approximately `1/3/5` to `1/1/1` for 1/3/5 cards; Personal Library with five cards
measured `1`. All posters loaded and card identity was preserved. Verification: compileall passed;
the five new poster tests passed; full suite 1,235 collected, 1,220 passed, 10 failed, 5 skipped in
224.59 seconds. The ten failures are the accepted baseline set; new failures are zero. V7 was not
ported, Poster Phase 3 was not started, and no release was packaged. See
`docs/reports/python-stabilization/03-refresh-state-flicker-remediation.md`.
## Python Stabilization Pass 1 status

**IMPLEMENTED AND SOURCE-VERIFIED ON `codex`.** Desktop startup now performs one local SQLite
Library query only. It does not start reconciliation, catalog-wide file inspection, metadata repair,
global poster refresh, or a progress-driven full Library reload. Check Library remains explicit and
manual. Committed status changes carry `media_file_id`, `movie_id`, and new status; the UI uses a
one-movie projection to update only affected cards while missing items remain registered.

Verification: eight focused stabilization tests plus affected MainWindow tests passed `29/29`;
the broader affected gate passed `109` with the one audited bootstrap contract failure; the full
suite is `1,175 passed, 11 failed, 5 skipped`, matching the same 11 pre-existing failures recorded
by the Deep Audit. Compileall and an offscreen guarded startup smoke passed. No migration or runtime
dependency changed.

## Python Stabilization Pass 2 status

**IMPLEMENTED AND SOURCE-VERIFIED ON `codex`.** Add Movies now commits a provisional local Movie
plus MediaFile before optional TMDB enrichment. Migration `0005_offline_movie_registration`
preserves all stable IDs/references/evidence, supports nullable paired external identity, persists
`PENDING / READY / FAILED / NEEDS_MATCH`, validates foreign keys before commit, restores
foreign-key enforcement after success/failure, and fails closed on unsafe downgrade.

The UI exposes no-match and metadata-unavailable local Add, publishes Transaction A success before
Transaction B runs, and updates only the affected Library card. Identity collision never merges:
both Movies and all file/personal/watch/history attribution remain separate, the provisional Movie
becomes `NEEDS_MATCH`, and the typed result exposes both IDs. Check Library remains manual and
retains identity-less local Movies. Nullable identity uses a safe poster placeholder.

Verification: migration `5/5`; focused Add/enrichment/import `72/72`; final callback-order gate
`46/46`; persistence/personal/Check Library/Pass 1 gate `95/95`; full suite `1,202 passed,
10 failed, 5 skipped` from 1,217 collected in 333.77 seconds. This is zero new failures versus the
accepted 11-failure baseline. Compileall, diff check, disposable native offline launch, restart, and
same-ID deterministic enrichment smoke passed. Live TMDB is unverified because no credential was
configured. Full evidence is in
`docs/reports/python-stabilization/02-offline-registration-tmdb.md`.

## DropSort V1 status

**RELEASE FREEZE BLOCKED PENDING ONE USER RETEST.** The final manual-acceptance follow-up fixes the
packaged Clear Library confirmation path, strips safe leading release-site domains before filename
punctuation normalization, removes the redundant Recently Added surface, adds persisted English /
Arabic localization with RTL/LTR handling, and replaces Margarine with bundled Inter plus a bundled
Arabic companion. The refreshed portable release candidate passed all source gates, artifact audits,
and packaged English/Arabic startup and shutdown checks. The user separately confirmed that packaged
Clear Library Data now opens its confirmation and completes successfully. Only the exact live noisy-
domain TMDB lookup remains for user-side retest because no credential was available to this process.

## Completed phases

- Phase 0 - Modular-monolith architecture and explicit I/O boundaries.
- Phase 1 - Journaled file safety, SQLite migrations, crash recovery, and Windows portability.
- Phase 2A - Movie filename parsing and movie/TV/unknown detection.
- Phase 2B - Provider-neutral metadata contracts, TMDB adapter, and SQLite metadata cache.
- Phase 2C - Explainable candidate ranking, confidence scoring, and match decisions.
- Phase 3A - Transactional, idempotent movie catalog ingestion.
- Phase 3B - Read-only local-library queries and immutable presentation DTOs.
- Phase 3C - Safe folder scan and read-only media discovery.
- Phase 3D - Metadata/matching proposals and separate explicit catalog confirmation.
- Phase 4A - PySide6 read-only Library and Movie Details UI (the former Recently Added surface was
  removed during final acceptance follow-up).
- Phase 4B - Folder scan, match review, candidate selection, and explicit catalog-import UI.
- Phase 4B.1 - Windows scanner hardening and session-only TMDB credential setup UX.
- Phase 4C - Local poster asset cache and asynchronous poster presentation.
- Phase 4D - Safe local Play Movie/Open Folder actions and finalized visual identity.
- Phase 5A - Safe organization preview and explicit journaled Move/Rename authorization.
- Phase 5B - Read-only operation history, dynamic safe Undo, and explicit recovery UI.
- Phase 5C - Scan progress, cooperative cancellation, and large-library UX hardening.
- Phase 6A - V1 hardening, missing-file reconciliation, explicit Relink, and runtime safety.
- Phase 6B - Windows portable packaging, attribution/licensing, artifact audits, and clean-profile
  final verification.
- Phase 6B.1 - Noisy-filename metadata fallbacks, automatic Library reconciliation, Relink refresh/
  race hardening, Clear Library Data, and final packaged acceptance fixes.
- Final V1 UX follow-up - provider-neutral manual TMDB search recovery, selectable/copyable
  technical text, Settings-based Operation History, compact responsive cards, and English/Arabic
  coverage (the later remaining-issues pass expanded the selector to four final themes).
- Final V1 UX polish follow-up - compact five-result manual search, automatic single-candidate
  preselection, filename-only Add Movies queue rows with dismiss controls, terminal Check Library
  states, branded application icon, exactly four persisted themes (Main, Dark, Slate, Light),
  theme-consistent Settings, and a resizable/persisted icon-only sidebar.
- Manual TMDB search UI correction - provider results are now localized structured cards with
  separate title/year, TMDB ID, rating, bounded wrapped overview, and per-card Select actions;
  the legacy dense raw-row list and detached bottom selection button are gone.
- V1 single-instance fix - atomic per-user/session Qt ownership, local `ACTIVATE` IPC, guarded window
  restoration, stale-lock recovery, and secondary startup exit before database/UI bootstrap.
- Manual TMDB Search UI redesign fix - structured localized result cards replace dense raw rows;
  title/year, TMDB ID, rating, and bounded wrapped overview are separate, with vertical-only
  scrolling and a compact per-card Select action.

## What works end to end

Running `python -m dropsort` opens the desktop application. It can display the local SQLite movie
catalog as a responsive card grid and show movie metadata plus every linked physical media file.
Poster areas retain intentional local placeholders; the UI performs no networking directly. Library
cards and Movie Details request poster
artwork through a dedicated asset boundary. Valid cached artwork is shown offline; missing,
unavailable, unauthenticated, or invalid artwork retains the intentional placeholder.

The runtime cache root is centralized at `%LOCALAPPDATA%\DropSort\poster-cache`; tests inject
temporary roots and never use the real profile. Poster binaries are not stored in SQLite, and cache
files are disposable without affecting catalog records or user media.

From **Add Movies**, the user can choose a folder, select recursive or non-recursive scanning, and
run read-only discovery plus optional metadata/matching proposal work in a background thread. The
review screen distinguishes proposed matches, review-required items, no-match results, TV episodes,
discovery errors, metadata failures, and paths already in the Library. Missing credentials, offline
providers, zero results, and ambiguous results no longer disable explicit local Add for a valid movie.

Large scans still show monotonic directory/file/media counters. Cancel remains cooperative, discards
incomplete results, restores inputs, and creates no catalog or journal state. Nothing is added merely
because scanning or matching runs. Only **Add to DropSort Library** authorizes Transaction A.

Transaction A atomically registers the local Movie and MediaFile with stable IDs and `PENDING`
metadata before any provider/detail request. The completed row is removed and the one affected
Library card is inserted/refreshed immediately. Optional Transaction B enrichment then runs
separately by stable `MovieId` and may produce `READY`, `PENDING`, `FAILED`, or
`NEEDS_MATCH`. TMDB failure never undoes local registration. Registration remains catalog-only:
the existing file path/bytes and filesystem-operation journal are unchanged.

Movie Details now exposes **Play Movie** and **Open Folder** on every physical-media row. Each
button is bound to that exact file, so multiple versions are never silently ranked or selected.
Play delegates to the user's Windows file association; Open Folder launches Explorer with the exact
file selected. Both actions revalidate that the cataloged path is an existing regular, non-reparse
file at click time. Missing, invalid, or launch-failure cases remain controlled UI messages and do
not reconcile, relink, or alter the catalog.

Movie Details also exposes **Organize File** on each exact physical-media row. The user selects one
destination folder, may keep or edit the filename, and receives a read-only preview containing exact
`FROM`/`TO` paths, operation type, size, volumes, transfer class, and validation state. Preview
creates no journal and performs no mutation. A single explicit confirmation consumes a one-time
preview, revalidates source identity/catalog ownership/destination availability, and invokes the
existing Phase 1 journaled Move/Rename pipeline. Successful completion refreshes the displayed
catalog path; the movie association and technical metadata remain unchanged.

The desktop now includes **Operation History**. It reads the durable journal newest first and shows
read-only details plus reverse-operation context. `COMMITTED` is not Undo authorization: DropSort
verifies current catalog ownership, latest-operation ordering, identity/content evidence, exact
historical paths, and destination availability before creating an in-memory preview. One explicit
confirmation creates a new journal row linked through `reverses_operation_id` and executes it through
the Phase 1 safe pipeline. The original journal record is never edited or deleted.

Out-of-order Undo, duplicate reverse journals, changed/absent sources, occupied destinations,
casefold collisions, unavailable roots, and catalog-path changes are blocked before journaling.
Organize, Undo, and recovery share one operation coordinator. Recovery inspection is read-only; only
source-only EXECUTING and identity-verified destination-only states offer explicit actions.
Ambiguous both/neither/tampered states preserve files and have no automatic action.

The desktop now has a **Settings** entry for TMDB metadata. A user can enter a masked Read Access
Token for the current process, clear it, and return directly to Add Movies. A session token takes
priority over `DROPSORT_TMDB_READ_ACCESS_TOKEN`; when neither is available, Add Movies displays an
actionable route to Settings. Applying a token updates subsequent metadata requests without an app
restart. The credential is deliberately never written to SQLite or a settings file.

Recursive scans now traverse multiple ordinary sibling movie folders correctly on native Windows.
The manual failure came from `DirEntry.stat(follow_symlinks=False)` returning `(st_dev, st_ino) ==
(0, 0)` for ordinary directories, causing unrelated siblings to look like the same visited
directory. DropSort now obtains directory identity with `os.lstat()` and revalidates a scheduled
directory immediately before enumeration. Symlinks, junctions/reparse points, changed identities,
inaccessible entries, and loop identities remain controlled skip/error results; normal directories
are not review rows.

The active visual identity is centralized in `ui/common/theme.py`:

```css
--text:       #FFF1A6;
--background: #0B1E1B;
--primary:    #013C35;
--secondary:  #6B352A;
--accent:     #E87454;
```

The bundled Latin UI typeface is Inter Regular/Bold (SIL Open Font License 1.1), registered through
`QFontDatabase.addApplicationFont`. Body text uses the real 400 asset and headings use the real 700
asset. Inter does not contain Arabic glyphs, so DropSort bundles Noto Sans Arabic Regular/Bold as a
deterministic offline companion instead of relying on an uncontrolled system fallback. The
centralized scale is Body 16 px, H1 67.36 px, H2 50.56 px, H3 37.92 px, H4 28.48 px, H5 21.28 px,
and Small 12 px. No font is downloaded at runtime.

The Library exposes explicit **Check Library Files** reconciliation.
Only cataloged paths are inspected with no-follow semantics in bounded background batches; progress
reports exact present/missing/error counts and cancellation leaves unchecked rows unchanged.
Transient inspection errors preserve the last persisted status. Missing movies remain in the
catalog; cards show a subtle missing indicator and Movie Details retains the
last known path.

Opening the application or entering Library performs no reconciliation. Check Library starts only
after the explicit user action. Its committed file-status progress includes stable media-file and
movie identities, and Library refreshes only those movie summaries/cards. Progress and completion
do not reload the full Library. Status writes still compare the inspected path with the row's current
path, preventing an old check from overwriting a successful Relink.

For a missing row, **Locate File** opens a native picker and a read-only Relink preview. The
candidate must be a supported regular non-reparse file with exact size/extension, compatible
title/year/technical facts, and no Windows-casefold catalog owner. The old path must remain confirmed
missing. Explicit one-shot confirmation revalidates filesystem identity and catalog ownership,
updates the same row transactionally to PRESENT, and creates no file-operation journal or physical
mutation. A successful Relink closes its dialog before refreshing Library and Movie Details.

Settings now includes **Clear Library Data** behind explicit physical-media safety wording. One
transaction clears movies, media-file links, and cached metadata only when no file operation is
nonterminal. The immutable operation/recovery journal is preserved. Poster cleanup happens after
commit and is confined to validated regular assets under the application cache root; cleanup failure
is reported as a warning. The Library becomes empty immediately and normal reimport remains available.

Settings now also includes a persisted English / Arabic selector. English is the default. The shared
localization catalog updates bound application text immediately, applies RTL for Arabic, restores LTR
for English, and marks paths plus technical values explicitly LTR. The preference is stored behind
the existing SQLite settings repository; no new migration or runtime dependency was required.

Metadata proposal makes at most four deterministic, deduplicated title/year fallbacks for bounded
site-prefix, release-token, and edition-suffix noise. Candidate identity remains provider plus
external ID, provider failure remains distinct from zero results, and matcher thresholds/import
authorization are unchanged.

Production runtime state is CWD-independent under `%LOCALAPPDATA%\DropSort`: `dropsort.db`,
`poster-cache\`, and bounded redacted `logs\`. Clean first run creates/migrates the database;
database startup failure is controlled and never silently replaces the existing path.

## Important runtime modules

- `src/dropsort/application/bootstrap/desktop.py` - desktop composition and orderly worker shutdown.
- `src/dropsort/application/configuration/metadata_credentials.py` - in-memory credential priority,
  local validation, redacted status, and Settings application actions.
- `src/dropsort/application/dto/import_review.py` - immutable folder-review session DTO.
- `src/dropsort/application/use_cases/prepare_folder_import_review.py` - discovery/proposal composition only.
- `src/dropsort/ui/contracts.py` - library and import UI action protocols.
- `src/dropsort/ui/common/theme.py` - centralized DropSort palette and QSS tokens.
- `src/dropsort/ui/assets/fonts/` - bundled Inter Regular/Bold and Noto Sans Arabic Regular/Bold,
  their OFL texts, and source notes.
- `src/dropsort/ui/common/tasks.py` - background task boundary and stale-safe result delivery.
- `src/dropsort/media/discovery/models.py` - immutable monotonic discovery progress counters.
- `src/dropsort/application/dto/import_review.py` - phased progress, summary, and review-session DTOs.
- `src/dropsort/application/use_cases/prepare_folder_import_review.py` - cooperative cancellation,
  bounded sequential proposal scheduling, and provider-failure short circuit.
- `src/dropsort/ui/library/` - responsive movie cards and the primary Library state.
- `src/dropsort/ui/movie_details/details_view.py` - read-only movie and media-file details.
- `src/dropsort/ui/main_window/window.py` - Library, Details, Add Movies, History, Settings, and
  persisted resizable/icon-only sidebar navigation.
- `src/dropsort/ui/localization.py` - canonical English/Arabic strings, widget bindings, and direction policy.
- `src/dropsort/application/configuration/localization.py` - provider-neutral persisted language setting.
- `src/dropsort/database/repositories/settings.py` - parameterized SQLite language preference adapter.
- `src/dropsort/ui/settings/settings_view.py` - masked, session-only TMDB Settings UI and themeable
  scroll/card surfaces.
- `src/dropsort/application/configuration/theme.py` - persisted theme and sidebar-width settings.
- `src/dropsort/database/repositories/settings.py` - parameterized SQLite theme and sidebar settings.
- `src/dropsort/ui/scan/import_view.py` - folder selection, scan session, errors, and explicit import orchestration.
- `src/dropsort/ui/scan/import_review_row.py` - proposal explanation, candidate selection, and import button.
- `src/dropsort/ui/scan/manual_search_dialog.py` - background manual TMDB search state and bounded
  five-result card host.
- `src/dropsort/ui/scan/manual_search_result_card.py` - localized semantic candidate card with
  separate metadata, wrapped overview, and explicit selection action.
- `src/dropsort/media/discovery/scanner.py` - read-only Windows directory identity reinspection and
  pre-enumeration link/identity validation.
- `src/dropsort/metadata/providers/session_tmdb.py` - thread-safe runtime credential-to-provider adapter.
- `src/dropsort/posters/contracts.py` - provider-neutral request/source/asset contracts and image validation.
- `src/dropsort/posters/cache.py` - application-owned, bounded, atomic local poster cache.
- `src/dropsort/posters/service.py` - cache-first/offline poster orchestration and duplicate suppression.
- `src/dropsort/posters/providers/tmdb.py` - bounded TMDB poster download adapter using runtime credentials.
- `src/dropsort/ui/posters/loader.py` - shared four-worker Qt pool, in-flight coalescing, and weak delivery.
- `src/dropsort/ui/library/movie_card.py` - fixed-size aspect-preserving poster presentation.
- `src/dropsort/ui/movie_details/details_view.py` - details poster presentation with stale-result protection.
- `src/dropsort/library/playback/` - local-action protocol, controlled errors, and Windows adapter.
- `src/dropsort/application/dto/organization.py` - immutable organization preview/result DTOs.
- `src/dropsort/application/use_cases/organize_media_file.py` - read-only preview, one-time explicit
  authorization, pre-journal revalidation, and controlled result translation.
- `src/dropsort/ui/organization/dialog.py` - exact operation preview and explicit confirmation UI.
- `src/dropsort/ui/movie_details/details_view.py` - exact per-file Play/Open Folder presentation and
  per-file Organize presentation with controlled action feedback.
- `src/dropsort/library/operations/` - operation-history read contracts and projections.
- `src/dropsort/database/repositories/operation_history.py` - bounded newest-first journal queries.
- `src/dropsort/application/dto/operation_history.py` - immutable history/Undo/recovery DTOs.
- `src/dropsort/application/use_cases/operation_history.py` - history, dynamic Undo eligibility,
  one-shot reverse execution, and explicit recovery orchestration.
- `src/dropsort/ui/history/` - Operation History, Details, Undo Preview, and recovery presentation.
- `src/dropsort/library/availability/` - provider-neutral no-follow catalog-path inspection.
- `src/dropsort/application/use_cases/reconcile_library_files.py` - bounded explicit reconciliation,
  progress, cancellation, and transient-error preservation.
- `src/dropsort/application/use_cases/relink_media_file.py` - conservative preview, one-shot TOCTOU
  revalidation, and transactional catalog-only Relink.
- `src/dropsort/application/use_cases/movie_search_fallbacks.py` - bounded deterministic noisy-title
  query planning without matcher-policy changes.
- `src/dropsort/application/use_cases/clear_library_data.py` - explicit catalog-maintenance boundary
  and post-commit cache cleanup translation.
- `src/dropsort/database/repositories/library_maintenance.py` - transactional catalog clearing with
  nonterminal-journal blocking and history preservation.
- `src/dropsort/ui/reconciliation/` - Library Check progress/cancel and Relink preview/confirmation.
- `src/dropsort/application/runtime/` - per-user database/cache/log paths and redacted rotating logs.
- `src/dropsort/__main__.py` - desktop entry point.

## Verified quality baseline

```text
Final acceptance follow-up focused UI/parser/package gate: 256 passed, 0 failed
Final remaining-issues focused gate: 80 passed, 0 failed
Manual Search UI focused gate: 17 passed, 0 failed; affected UI/localization gate: 57 passed, 0 failed
Final full suite: 1023 passed, 5 skipped, 0 failed
Total branch coverage: 95%
Reconciliation use case: 98% branch coverage
Relink use case: 97% branch coverage
Reconciliation UI: 98% branch coverage
Availability inspector: 96% branch coverage
```

Current Python Stabilization Pass 1 gate: `29 passed` focused; full suite `1,175 passed, 11 failed,
5 skipped`. The 11 failures exactly match the audited pre-Pass-1 list; no new failure was introduced.
Compileall and the guarded offscreen startup smoke passed.

The five skips are legitimate Windows symlink-creation privilege limitations. Link/reparse behavior
also has deterministic mocked coverage. UI tests run headlessly; the offscreen Qt plugin exposes no
font families, so screenshot text rendering is not representative of the normal Windows platform
plugin.

## Review findings fixed

- Central theme QA found and fixed a light Qt-default grid background.
- Review states were made explicit for TV, unknown media, discovery failures, authentication, rate
  limits, no-match, and already-cataloged paths.
- Session tokens prevent stale scan/import results and results delivered after window close from
  changing widgets.
- A critical PySide worker/widget lifetime crash was removed: callbacks are delivered only after a
  task thread stops, active runners are retained, widget removal avoids unsafe deferred deletion,
  and application shutdown waits for bounded backend tasks.
- Native Windows directory identities from `DirEntry.stat()` could collapse to `(0, 0)` and falsely
  mark normal sibling folders as loops. Directory identity now comes from `os.lstat()` and the
  representative multi-folder UI layout has a regression test.
- Adversarial scan review found a replacement window between scheduling and enumeration. DropSort
  now rechecks directory type, reparse state, and expected identity immediately before `scandir`.
- Credential review found that UI and worker activity could overlap while a session token changes.
  Credential access and provider replacement are synchronized, and token values never appear in
  status DTOs, application reprs, logs, or error messages.
- The README previously described environment-only setup. It now documents Settings-first,
  session-only setup and the environment fallback without embedding a credential value.
- Poster references are never used as paths. Cache identity is SHA-256 over normalized provider plus
  reference, separating provider namespaces and preventing traversal/invalid-filename injection.
- Poster downloads are bounded to 8 MiB with a finite timeout. JPEG/PNG content is validated from
  bytes; PNG chunk framing and CRCs are checked before atomic cache promotion.
- Cache writes use unique files inside the configured root, flush and `fsync`, then `os.replace`.
  Abandoned temp files and corrupt entries are removed as disposable application assets.
- Adversarial cache review found that an internal link/reparse entry could otherwise escape through
  reads or timestamp updates. Every cache entry now uses no-follow `lstat`; link/reparse entries are
  removed without reading or touching their targets, and the cache root is revalidated on use.
- The cache is capped at 256 MiB and evicts least-recently-used entries deterministically by touched
  modification time. Service request locks are reference-counted and removed after use.
- Poster loading uses one shared `QThreadPool` capped at four workers. Identical in-flight requests
  are coalesced, receivers are weakly referenced, stale card/details results are rejected, window
  close invalidates callbacks, and orderly application shutdown waits for active work.
- Phase 4D review found that NTFS timestamp ties could make poster-cache eviction choose the wrong
  LRU entry under instrumentation. Cache touches now use strictly monotonic nanosecond values, with
  a deterministic tied-clock regression test.
- Local-action review confirmed that special-character and Unicode paths remain data arguments:
  playback uses `os.startfile`, Explorer receives an explicit argument tuple, and `shell=True` is
  forbidden by architecture tests. Missing files, directories, links/reparse points, permission
  failures, absent associations, Explorer failures, and post-validation disappearance are covered.
- Theme/typography review removed the old active palette, centralized every authoritative color and
  size token, bundled the official Google Fonts asset and OFL text, and verified safe fallback with
  no runtime font networking.
- Organization review found and fixed competing-preview journal races, catalog-owned destination
  conflicts, unbounded stale preview tokens, conservative recovery translation, and pre-journal DB
  failure handling. Confirmation is serialized and repeats every safety check before journaling.
- A fresh desktop process exposed a circular import hidden by pytest import order. Bootstrap now
  initializes the operations public boundary before the transfer implementation, with a subprocess
  startup regression.
- A real Windows UI smoke test moved one deterministic repository-local fixture. SHA-256 matched,
  the journal reached `COMMITTED`, the catalog path refreshed, and association/technical metadata
  remained intact. Cross-volume execution remains automated-simulation verified only.
- Phase 5B review fixed competing reverse journals, failed-reverse blind retry, late dialog delivery,
  separate mutation locks, and missing digest revalidation. Reverse creation is serialized in the
  SQLite transaction; any prior reverse blocks retry; closed dialogs invalidate delivery; desktop
  mutations share one coordinator; and persisted SHA-256 is checked when present.
- A native Windows UI smoke moved and explicitly undid a 1,015,811-byte repository-local file. The
  linked reverse reached `COMMITTED`, exact original path and SHA-256 were restored, and association
  plus technical metadata remained intact. Disposable artifacts were removed.
- Phase 5C review fixed late progress overwriting cancellation, unsafe root-level failure downgrades,
  repeated provider requests after session-wide failure, delayed cancellation during huge directory
  enumeration, duplicate batched rows, and incomplete zero-mutation assertions.
- A native Windows smoke scanned a repository-local 5,000-file/50-directory fixture. Live progress
  remained responsive, cancellation discarded partial results and restored controls, immediate restart
  completed with exact counters, and path/catalog/journal state remained unchanged.
- Phase 6B.1 review fixed stale reconciliation writes after Relink, duplicate/noisy metadata queries,
  clearing during active operation state, unsafe poster-cache cleanup assumptions, and incomplete
  callback exception logging. Status updates now compare paths, fallbacks and candidates are bounded
  and deduplicated, the maintenance transaction blocks nonterminal operations, cache deletion is
  root/type/identity guarded, and actual exceptions are logged without credential data.
- Final packaged localization review found that already-rendered TMDB feedback remained English after
  a live switch to Arabic. Settings now retains the stable text identifier and re-renders dynamic
  feedback on language changes; the regression test and final source/package gates pass.
- Single-instance review found that Windows named-pipe `QLocalServer.listen()` alone is not an atomic
  election primitive. Ownership now uses Qt `QLockFile`; QLocalServer/QLocalSocket carry only the
  `ACTIVATE` protocol. Secondary startup exits before runtime/database/window composition, and late
  activation is ignored once MainWindow shutdown begins.

No unresolved BLOCKER or CRITICAL finding remains.

## Safety boundaries still in force

- `MATCHED != CATALOG IMPORT AUTHORIZATION != FILESYSTEM AUTHORIZATION`.
- Proposal generation performs zero movie-catalog writes; import requires an explicit per-row click.
- Catalog import records the file at its existing path and never moves, renames, copies, or deletes it.
- The UI executes no SQL, HTTP, parser/matcher implementation, or filesystem mutation directly.
- Poster writes are confined to the centralized application cache root. Poster code does not import
  the File Engine or mutate user media.
- `PLAY / OPEN FOLDER != FILESYSTEM MUTATION AUTHORIZATION`. Local actions do not import the File
  Engine, write SQL, create journal entries, or change catalog/media paths, sizes, or bytes.
- An organization preview is not authorization. It creates no journal or mutation; only one explicit
  confirmation may invoke the File Engine, and the preview cannot be reused.
- Organization never overwrites. Source identity, approved roots, destination availability/casing,
  catalog path ownership, and current catalog source are revalidated immediately before journaling.
- Folder discovery is read-only and does not follow link/reparse entries or escape its root.
- Normal child directories are separately re-inspected for stable identity; reparse/type/identity
  changes before enumeration are rejected rather than followed.
- Never overwrite a destination or perform an unjournaled filesystem mutation.
- Authoritative database paths change only after verified filesystem operations.
- Ambiguous recovery states preserve files rather than deleting or guessing.
- A committed operation is not Undo authorization. Undo requires current eligibility, an exact
  read-only preview, and one explicit confirmation; it creates a separate linked reverse journal.
- Recovery inspection is not mutation authorization. Only deterministic established reconciliation
  actions are exposed; ambiguous states have no automatic action.
- Missing physical media never deletes catalog state. Reconciliation inspects only cataloged paths;
  inspection errors do not become MISSING automatically.
- Relink is catalog correction, not filesystem authorization. It requires explicit confirmation,
  preserves ambiguous files, creates no file-operation journal, and performs zero media mutation.
- Packaging changes none of these rules. Runtime data is per-user; the release contains no user DB,
  cache, logs, credential, fixture, developer path, or source-checkout dependency.

At this stopping point, explicit per-file Move/Rename exists; **no automatic, bulk, or delete
workflow exists**.

## Known limitations / not implemented

- Metadata proposal work remains sequential and cooperative cancellation cannot interrupt one HTTP
  request already in flight; it stops before scheduling the next request.
- Review rows are batched but not virtualized, so an extremely large completed review still retains
  one QWidget per row.
- Import proposals are in-memory and are not persisted as a later review queue.
- No automatic/bulk organization or folder watcher.
- History has bounded offset pagination but no search/filter/export, bulk Undo, or cascade Undo.
- Real cross-volume Undo and manual destructive recovery were not performed; both have deterministic
  automated coverage, and ambiguous real files were not created solely for testing.
- No TV library, subtitles, watch tracking, ratings, favorites, collections, or storage dashboard.
- Discovery facts are point-in-time and are not re-statted during later confirmation.
- TMDB tokens entered in Settings are session-only and disappear when the process exits. Secure
  persistent storage such as Windows Credential Manager is not implemented.
- Manual live TMDB poster verification from Phase 4C: **PASS - confirmed by user.** The exact
  `AnimeSanka.com Kaze Tachinu ...` live candidate lookup is not yet verified because no credential
  was available to this acceptance-follow-up process; it remains required in the user retest.
- Packaged Clear Library Data confirmation/execution: **PASS - confirmed by user.**
- Packaged localization: English/LTR and Arabic/RTL rendered successfully; Windows media paths stayed
  LTR, Arabic persisted across a full restart, English was restored, and final shutdown was clean.
- Packaged typography: Inter Regular/Bold and Noto Sans Arabic Regular/Bold were present in the
  artifact and rendered offline. Recently Added was absent from navigation after restart.
- The cache supports JPEG and PNG poster assets only, has no user-facing cache-management screen,
  and does not cancel an already-running HTTP request when a view closes; delivery is made inert and
  shutdown waits for the bounded request timeout.
- TMDB is the only real metadata adapter; no multi-provider aggregation exists.
- Play depends on the user's Windows file association and does not embed a player.
- Reconciliation is manual-only through Check Library; it inspects only cataloged paths and does not
  search whole drives.
- Relink blocks different-size/different-extension/weak-title/technical-conflict candidates; no
  advanced Replace Media File flow or persistent full-film hash catalog exists.
- The Phase 4D Windows visual smoke test was performed at the current desktop scale; 125% and 150%
  scaling remain covered by Qt's device-independent layout behavior rather than separate manual
  system-scale changes.
- Phase 6B used a clean-profile approximation because no separate clean machine/VM was available.
- The portable executable is unsigned and may trigger Windows SmartScreen.
- No live TMDB request was performed during Phase 6B because no credential was available; normal
  launch/offline behavior passed and the prior user-confirmed Phase 4C live poster check remains history.
- Exact rebuilt packaged duplicate-launch verification passed: normal second launch, five repeated
  launches, minimized activation, rapid double launch, normal relaunch, and disposable crash/stale
  recovery all left one functional window/process and no catalog/media/file-operation side effects.
- DropSort has no explicit project license; third-party notices and applicable license texts ship.

Dependencies changed in Phase 4D: none. The active bundled font assets are now Inter Regular/Bold
and Noto Sans Arabic Regular/Bold, not Python dependencies. The existing constraint remains
`PySide6>=6.11.1,<7`.

Dependencies changed in Phase 5A: none. No database migration was required.

Dependencies changed in Phase 5B: none. No database migration was required; the existing reverse
operation self-reference was reused.

Dependencies changed in Phase 5C: none. No database migration was required; scan sessions remain
ephemeral.

Dependencies changed in Phase 6A: none. No database migration was required; the existing PRESENT /
MISSING schema and path identity fields were reused.

Dependencies changed in Phase 6B: runtime dependencies none. PyInstaller `>=6.21,<7` was added only
to the optional packaging dependency group; the verified build used 6.22.0. No migration was added.

Dependencies changed in Phase 6B.1: none. No migration was added.

Dependencies changed in Python Stabilization Pass 1: none. No migration was added.

## Exact recommended next phase

**No next phase is approved.** The exact next action is the user's live-TMDB retest of
`AnimeSanka.com Kaze Tachinu [Bluray - 1080p - Ar - x265].mp4`. Expected: the proposed search title
is `Kaze Tachinu`, a real TMDB candidate can be reviewed, and no automatic catalog or filesystem
authorization occurs. All other requested packaged follow-up checks are complete. Do not begin
Release Freeze or V2 until the user accepts this release candidate.

Final release candidate: `release\DropSort\DropSort.exe` in a 190-file one-directory artifact,
120,116,153 bytes (114.55 MiB) after the final manual-search UI rebuild. The artifact contains the bundled font files and both OFL
licenses, all six migration scripts, no Margarine asset, no forbidden runtime/test artifacts, no
developer-path match, and no assigned TMDB token or bearer-token pattern.

The final V1 UX follow-up is documented in `docs/reports/v1/PHASE_6B_2_REPORT.md`; the preceding polish is documented in
`docs/reports/v1/PHASE_6B_3_REPORT.md`; this remaining-issues pass is documented in
`docs/reports/v1/PHASE_6B_FINAL_REMAINING_ISSUES_REPORT.md`. Source verification is `1023 passed, 5 skipped, 0 failed`
with `95%` total branch coverage. Manual TMDB verification is
still pending because this process had no credential; the expected user retest is Edit Search ->
`Kaze Tachinu` or `The Wind Rises` -> review candidate -> explicit Add to DropSort Library, with
zero filesystem/file-operation side effects.
