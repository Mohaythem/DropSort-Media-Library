# DropSort — Engineering Phase Record

This is a concise chronological record, not a substitute for Git history. “Rewrite phases” below refer to the original .NET foundation reports; “Product Phase 1/2/3” refer to the later user-accepted product roadmap.

## 0. Python V1 frozen baseline

**Purpose.** Preserve the Python/PySide6/SQLite application as the behavioral and safety reference while replacing the active implementation with C#/.NET/WinUI 3.

**Important decisions.** Python V1 is read-only. Its source/tests inform parity decisions, but historical reports are not accepted blindly and Python-specific limitations do not define the newer WinUI implementation. The Git history contains the Python V1 release tag `v1.0.0` at `45e8346`.

**Meaningful baseline behavior.** Local-first SQLite catalog, explicit import and organization actions, durable operation history/recovery, missing-media retention, English/Arabic UI, and conservative metadata matching formed the reference contract.

**Verification.** The Python repository contains its own audit/test history. This record intentionally does not claim that all Python tests passed at freeze time; the rewrite audit used source and tests as evidence rather than treating the release label as proof.

**Why later correction was needed.** V1 behavior, reports, and implementation sometimes diverged. The rewrite therefore required direct source/test comparison and explicit decisions for ambiguous behavior instead of mechanical translation.

## 1. .NET rewrite foundation (historical rewrite Phases 1–4)

**Purpose.** Establish the layered .NET solution and port the safety-critical Domain, Application, SQLite, and FileSystem foundations before functional UI integration.

**Important decisions.** The solution separates Domain, Application, Infrastructure, FileSystem, UI, and tests. Domain/Application remain platform-light and inward-facing; Infrastructure owns SQLite; the FileSystem layer owns safe transfer behavior; UI is the composition/presentation boundary. `DateTimeOffset` carries aware timestamps and stable integer/string IDs cross persistence boundaries. ADO.NET was selected over an ORM. Migrations 0001–0005 initially reproduced the V1 schema and `PRAGMA user_version` progression.

**Meaningful behavior.** Domain invariants, repository/application contracts, transactional catalog writes, journal FSM enforcement, approved-root transfer policies, source identity locking, hardlink/copy paths, `Flush(true)`, SHA-256 verification, no overwrite, and verify-before-delete were implemented.

**Verification.** `PHASE1_REPORT.md` through `PHASE4_REPORT.md` record progressive totals of 3, 40, 43, and 48 passing tests. These are historical snapshots, not the current test total. Because the tree was initially untracked, the first Git commit proving the checked-in WinUI solution is `73b248326a3f9b3369639be6da12919c64b60a4a`; its commit message records a clean MrtCore-safe build and 80/80 tests.

**Why later correction was needed.** The initial reports predated full UI/application wiring and real lifecycle use. Several assumptions—including old SQLite dependency notes and future-phase labels—became stale as the project advanced.

## 2. WinUI 3 shell and accepted design foundation

**Purpose.** Deliver the native WinUI shell and the approved adaptive English/Arabic interface without beginning unrelated backend features.

**Important decisions.** Use native WinUI/Fluent controls and shared resources; preserve responsive wide/compact layouts; keep technical values LTR inside Arabic RTL; support Slate, Dark, and Light; retain accessible names and keyboard/focus behavior.

**Meaningful behavior.** Navigation, Home, Library, lists, Add Media, Check Library, Movie/TV details, Settings, and Operations Log surfaces were established. Arabic typography/digit direction and page-level adaptive visual states were repaired. The accepted structure was later runtime-walked across navigation, themes, RTL/LTR, and accessibility.

**Verification and commits.** `73b2483` checked in the shell/adaptive UI with 80 tests reported. `UI_IMPLEMENTATION_REPORT.md` records a historical 17-test focused UI set, 80-test suite, successful Visual Studio MSBuild x64 build, and runtime theme/RTL/navigation checks.

**Why later correction was needed.** The initial surface still contained demo-backed sections and inert backend-dependent controls. Visual completeness did not prove real persistence or safe runtime integration.

## 3. Functional application wiring

**Purpose.** Replace demo behavior with the real Application/SQLite/FileSystem stack while keeping views behind narrow contracts.

**Important decisions.** A single composition root opens/migrates the database under the application data root and supplies UI actions. Add Media registers local files offline; TMDB remains optional enrichment. Check Library stays explicit/manual. Missing rows remain registered. File organization is journaled and only updates the catalog after verified filesystem completion.

**Meaningful behavior.** Add Media, Library/Home/My Lists, Movie Details, watch state/history, Settings, Clear Library, Check Library, and Operations Log became persistent. `b34ac27293984bc2d57b2b2a6c418e2a1698b35d` reports 88 passing tests.

**Verification and commits.** `FUNCTIONAL_WIRING_REPORT.md` records real temporary-media runtime checks and 88 passing tests. `4ee14f9d6e1f37869f32fead9a2dc9f22ceba685` repaired the wiring review and reports 104 passing tests.

**Why corrective work was needed.** Initial wiring exposed UI blocking during organization, resource/WAL lifetime problems, latched initialization failure, misleading Check Library counts, cancellation races, incorrect filtered log export, and misleading Add Media action text. These were integration defects that isolated layer tests could not reveal.

## 4. TV hierarchy and safety/lifecycle stabilization

**Purpose.** Complete real Movie/TV hierarchy support and harden restart, recovery, reconciliation, relink, and long-path behavior.

**Important decisions.** Migration 0006 adds TV hierarchy through an ordered schema upgrade. Startup recovery and SQLite writes use explicit safe transaction behavior. Stale crash temporary files are reported read-only rather than deleted automatically. Relink corrects catalog identity without moving media or creating a filesystem operation.

**Meaningful behavior.** Real TV shows/seasons/episodes replaced demo hierarchy; startup recovery evaluates incomplete operations; long paths and crash artifacts are handled conservatively; mixed lifecycle integration and media relink were added.

**Verification and commits.** `65903bb3cc932f4e7d4668c76b189d8835edd697` delivers TV hierarchy, `ffd048d52097a8ff8e9694ecc5f09f86a1d1cbe4` delivers recovery/transaction/long-path/crash-temp hardening, and `c3851e2aa3928209aa9b0cbedcf8a263f5155a5f` delivers relink and lifecycle integration tests.

**Why corrective work was needed.** Functional happy paths did not cover process interruption, lock contention, long Windows paths, abandoned temporary files, or missing-media identity repair. Those omissions could otherwise leave journal/catalog state ambiguous.

## 5. TMDB metadata and poster integration

**Purpose.** Add production optional metadata for Movies and TV without weakening local-first behavior.

**Important decisions.** TMDB is behind Application contracts; disk posters use deterministic, validated atomic cache writes; scoring stays conservative; metadata writes preserve local IDs/paths/hierarchy; migration 0007 adds enrichment fields. Credentials are session-only.

**Meaningful behavior.** Movie/TV search and enrichment, poster caching, manual match/fix-match UI, provider retry/rate-limit handling, and localized safe error presentation were added.

**Verification and commits.** `232ed8dbb94f841ee97148b10ed79b7f749b1a92` implements provider/cache/matching/migration 0007. The independent review at that checkpoint recorded 186 passing tests but found correctness gaps. `27c0449a4e3772b33b4558f9c48cdee6e484191d` repaired those findings; `CODEX_POST_HANDOFF_CORRECTIONS.md` records 215 passed, focused provider/TV/credential/presentation/request/migration suites, clean supported builds, and isolated runtime checks. `9a635e09dcb7e170ba9aa65cbecba413d9d479e8` serialized ContentDialogs and added a crash regression.

**Why corrective work was needed.** The first integration accidentally persisted a session credential, could expose/flatten provider failures, allowed cancellation to poison shared poster work, risked stale async UI completion, and allowed overlapping dialogs to crash. The corrective pass restored the intended security and lifecycle contracts.

## 6. Product Phase 1 — Add Media

**Purpose.** Unify selective Movie/TV discovery, local registration, metadata preflight, and completion reporting into one truthful workflow.

**Important decisions.** Metadata preflight is read-only and may run before the user adds a row. After Add is confirmed, local registration is committed before staged enrichment is applied or any post-registration provider work, so metadata failure cannot roll back the local record. Selection is per row. `Ready to Add` means only that a file can be locally registered. Matched, Needs Match/no safe match, provider error, and Already in Library remain distinct. Existing media is detected by canonical registered media path identity before metadata calls; a second file for the same title is not blocked.

**Meaningful behavior.** Mixed Movie/TV scans, selective import, grouped TV matching, staged metadata outcomes, manual-match precedence, cancellation, and failure-surviving local registration were delivered by `717972d813b237c6585ed240efaaa182656c4077`.

**Corrective iterations.** `d5e3be1b5a90d741ac3eb7083fe2bb34a53745e5` made completion counts truthful; `2cd08263419ebe97d0143dd5215711ec99dd82fa` separated pre-add metadata/service states; `a115a10ef8b66ae483906ea5d02ac5e564e28400` restored selectable static row text; `50aaa07c532ad3b47c6a20fc44885398d3255ed8` detects existing Movie/TV media before TMDB and prevents duplicate registration/network work.

**Verification.** `ImportSelectedTests` covers selective/mixed registration, metadata success/ambiguity/no-result/service failure, manual-match races, local survival, grouped TV behavior, exact existing-media detection, different-file identity, and zero provider calls for existing rows. UI source contracts cover selection/count/status presentation. Commit-specific suite totals were not recorded consistently; the current checkpoint suite is 270/270.

**Why corrective work was needed.** Manual acceptance showed that “Ready” incorrectly implied metadata success, provider failures looked like legitimate no-match, completion summaries hid mixed outcomes, and already-registered files still entered metadata preflight. Each issue required state propagation changes, not label-only fixes.

## 7. Product Phase 2 — TMDB Search & Matching Quality

**Purpose.** Improve noisy filename/search handling and candidate ordering while preserving conservative automatic-match thresholds and manual choice.

**Important decisions.** Search uses at most three deterministic, deduplicated query forms (raw, release-noise-cleaned, normalized). Results from fallback queries are accumulated/deduplicated. Provider failures propagate as service errors. Popularity is only a tie-breaker after title/year score and cannot turn an ambiguous result into a safe automatic match.

**Meaningful behavior.** Release-site prefixes, technical/language tags, punctuation, and common filename noise are handled more accurately; meaningful title words/brackets are retained; equal-score fallback candidates remain available; Movie and TV manual review continue to receive candidates without weakening auto-match safety.

**Verification and commit.** `b158f9daa8660f77dce04529e0de31893001f7d6` adds Application, scorer, parser, provider, UI, and regression coverage. Manual acceptance is recorded by the project owner for this checkpoint. A fresh 2026-09-22 run at this commit passed **270 tests with 0 failures/skips**; Visual Studio MSBuild solution `Debug|Any CPU` and WinUI `Debug|x64` builds passed.

**Why the phase was needed.** Safe matching still produced poor searches for noisy release filenames and could discard useful fallback candidates. The phase improved query quality and ordering without expanding automatic-match authority.

## 8. Product Phase 3 — Home Artwork + Real Watch History

**Purpose.** Replace the remaining Home watch-state placeholders with durable Movie/TV history, real cached artwork, and truthful behavior when formerly watched media is removed or unavailable.

**Important decisions.** Migration 0008 converts watch events into a typed Movie/Episode history stream with nullable catalog links and durable title, year, episode identity, and ordered artwork snapshots. Ordinary catalog deletion uses `ON DELETE SET NULL`, so historical identity survives without weakening explicit Clear Library Data semantics. Episode watched state and Continue Watching are derived through Application contracts; UI code receives DTOs and does not query SQLite directly. Playback remains separate from the explicit `Mark Watched` action.

**Meaningful behavior.** Home now projects real Continue Watching and Recently Watched data for Movies and TV. Continue Watching advances to the next present unwatched episode. History cards use cached still/backdrop/poster candidates before attempting network access, survive restart and individual removal, show an explicit unavailable state, and open a safe explanatory dialog instead of attempting invalid playback. Add Media completion presentation was tightened without changing its matching or registration rules.

**Verification.** Focused migration/history/UI contract tests passed **44/44** and the full suite passed **279/279**. Visual Studio 18 MSBuild solution `Debug|Any CPU` and WinUI `Debug|x64` rebuilds passed with no warnings shown. A fresh self-contained `win-x64` artifact launched against an isolated schema-8 data root; the runtime journey verified Mark Watched, next-episode progression, restart persistence, deletion-surviving snapshots, cached poster fallback, unavailable presentation, and the safe dialog. Manual acceptance remains pending.

**Why corrective review mattered.** Independent review caught two issues before the checkpoint: an accidental edit to historical migration 0004 and a single-reference artwork snapshot that could discard a usable cached fallback. Migration 0004 was restored unchanged; the new expression index remains confined to migration 0008, and history now persists separate still/backdrop/poster candidates.

## 9. Product Phase 3 closure — watch-history reliability corrections (manually accepted)

**Purpose.** Take Product Phase 3 from locally verified to manually accepted by fixing the reliability defects found during real upgraded-database use, without expanding scope.

**Important decisions.** Raw `watch_events` are the durable source of truth and are never collapsed, deduped, or altered in persistence; Movie Details / TV Details history preserves every individual event. Home → Recently Watched is a *read-time* projection over logical media, not raw events. Movie identity is `movie_id`; TV identity resolves to the canonical parent `show_id` (through `tv_seasons`), so episodes and seasons of one show share a single card. All writers to a database queue behind one per-database `DatabaseWriteGate`; the gate is non-reentrant, so units of work are scoped and disposed before the next write. Migration scripts are immutable once stamped — schema drift is reconciled with a new forward migration (v9), never by editing a released script.

**Meaningful behavior.**
- Migration v9 reconciles previously stamped, drifted v8 `watch_events` schemas, so Mark Watched works on upgraded databases.
- `DatabaseWriteGate` coordinates catalog unit-of-work, maintenance, and personal-library writes to avoid SQLite write contention/deadlock.
- Clear History removes movie, episode, and orphaned events in one pass; `DeleteWatchEvent` rejects episode events cleanly instead of failing on a `DBNull` cast.
- Recently Watched shows one card per repeated movie (latest event) and one show-level card per TV show — show title/artwork with a season-oriented context line surfacing the latest watched season/episode; ordering uses each logical item's latest watch event.
- A speculative `MovieDetailsPage` ItemsRepeater workaround built against a misdiagnosed defect was reverted; the bound `ObservableCollection` already reflects in-place rebuilds.

**Verification.** Focused watch-history/migration/concurrency/UI-contract suites and the full suite passed **296/296** (0 failed, 0 skipped). Visual Studio MSBuild rebuild succeeded and the freshly built WinUI app passed **manual GUI acceptance** on 2026-09-24 against a real upgraded library database: Mark Watched, v9 reconciliation, repeated-movie single card, TV show-level grouping, and preserved raw movie/episode history were all confirmed. Plain `dotnet build` of the WinUI project still fails only at the WinUI PRI task (`MSB4062`) — a known SDK-path tooling gap, not a source defect.

**Commits.** Implementation: `15c676d`. The governance/roadmap update is delivered in the immediately following local docs commit (hash reported in the closure handoff). Neither is pushed.

## 10. Persistence Write Coordination

**Purpose.** Establish one explicit write-coordination contract for every writer to `library.db` and close the writers that bypassed `DatabaseWriteGate`.

**Write-coordination contract.** Every writer acquires `DatabaseWriteGate.Enter(connectionString)` — a fair, per-database-path, in-process, non-reentrant semaphore — as its outermost scope, before opening its IMMEDIATE (write-lock) transaction, and releases it only after commit/rollback via `using` (so exceptions and failed commits always release it). Reads that take no write lock are not gated. The gate is non-reentrant: a writer holding it must not invoke another gated writer; nested write helpers (for example `FileOperationStore`'s private transition invoked by `CommitVerified`) run inside the already-open transaction and never re-acquire the gate. Critical sections are database-only and short — slow filesystem work (copy, hash, source removal) happens between journal calls, never while the gate is held, so `FileOperationCoordinator.Execute` performs each journal write as an independent gated transaction around ungated filesystem steps. Migration/startup initialization runs exclusively before any repository or service is exposed and is the one writer permitted to operate without the gate, because no concurrent writer exists at that point.

**Audited writers.** Catalog unit of work (movies/media_files/tv_*): already gated. Personal library (preferences, watchlist, `watch_events`, clear): already gated. `LibraryMaintenanceRepository.ClearCatalog`: already gated. `FileOperationStore.Create/Transition/CommitVerified` (`file_operations`, and `media_files` on commit): **was not gated — now gated**. `SettingsRepository.Set/Delete` (`settings`): **was not gated — now gated**. `DatabaseMigrator`: ungated by design (startup-exclusive).

**Gaps closed.** `FileOperationStore` and `SettingsRepository` now participate in the same gate as the rest of the app; `CommitVerified`'s `media_files` update serialises with catalog writers to that table.

**Verification.** New `WriteCoordinationTests` prove all writer kinds serialise under concurrency with no "database is locked", a held catalog unit of work blocks a journal write until released, the gate is released on the exception path, and `CommitVerified` does not self-deadlock. Focused coordination/recovery/file-operation suites passed **30/30**; full suite **300/300** (0 failed, 0 skipped). Plain `dotnet build` of the WinUI project still fails only at the WinUI PRI task (`MSB4062`); no C# errors.

**Remaining risks.** `FileOperationStore` keeps one long-lived connection; its ungated reads share that connection with gated writes, so writers serialise but a concurrent read+write on the single connection is not thread-safe (the store is driven sequentially from the UI/startup today). The gate is in-process only and does not coordinate a second process against the same database (out of scope for single-instance, local-first use).

## 11. DropSort 1.0 Scope Freeze (adversarial-reviewed)

**Purpose.** Freeze exactly what belongs in DropSort 1.0, name the true release blockers, defer the rest, and gate the decision through an independent adversarial review — a docs-only phase that changed no implementation code. The frozen scope (Included / Stabilization / Post-1.0 / Out-of-scope), release blockers, and golden journeys live in `FUTURE_PLAN.md`.

**Adversarial gate (Codex).** Verdict **needs-attention** ("NO-SHIP" for an as-is freeze). Codex confirmed the direction and the documented strengths (local-first registration, journal-first transfers, immutable migrations, logical-media watch projections), and raised six findings plus a scope-label disagreement. The full report is preserved in the session transcript.

**Claude reconciliation (evidence-first; independently verified against the repository).**
- **[critical] Single-instance ownership — ACCEPT (strengthened).** Verified `DropSort.UI.csproj` sets `<WindowsPackageType>None</WindowsPackageType>` (unpackaged), there is no `Package.appxmanifest`, and no `AppInstance` redirect or custom `Main`; `App.OnLaunched` always creates a window. Single-instance is enforced in neither dev nor the release artifact, and MSIX is not in play. Promoted to a 1.0 release blocker (Data / File Trust Pass).
- **[high] TV import identity — ACCEPT (concrete failure proven, not just fragility).** Verified the chain: import keys on the filename-derived UNIQUE `sort_title` (`ImportService` → `GetBySortTitle`); `ApplyTvShowMetadataAsync` overwrites `title` and `sort_title` (via `NormalizeTitle(canonicalTitle)`) and sets `provider`/`external_id` (`TvShowRepository.UpdateMetadata`); no filename alias is preserved. Re-importing an episode under the original alias creates a second show, and re-matching it to the same provider identity violates `tv_shows UNIQUE(provider, external_id)`. Promoted to a 1.0 blocker with a required alias→match→next-episode regression.
- **[high] Ambiguous drive/backup labels — ACCEPT.** `FUTURE_PLAN.md` listed "Drive Awareness" and "Portable Local Backup/Manifest" as post-1.0 while the roadmap kept "Drive-Aware Availability Model" and "Early Backup Protection" pre-1.0. Split with explicit criteria: 1.0 must distinguish offline/unmounted from deleted media and auto-create+verify a DB backup before migrations/risky maintenance; richer drive analytics and portable manifests stay post-1.0.

- **[high] 300 tests = backend only — ACCEPT.** Verified the test project references Domain/Application/Infrastructure/FileSystem and links two UI helper source files as text; it does not build the WinUI project or exercise XAML/navigation/RTL/packaging. The 300/300 baseline is backend evidence only; exact-artifact and installed-app verification remain hard release gates.
- **[medium] Unsupported-TV classification — ACCEPT (principle); exact regex coverage DEFER/NEEDS-EVIDENCE.** Verified `MediaDiscoveryService` recognises `S01E02`/`1x02` and otherwise classifies as Movie, so unsupported forms silently become movies. 1.0 must classify unsupported/ambiguous inputs as unresolved-TV-needing-review; the precise set of separators/multi-episode patterns and regression cases is deferred to the trust pass.
- **[medium] No reviewable freeze artifact / unrelated dirty files — ACCEPT (procedural).** Addressed by this dedicated docs-only freeze commit; the pre-existing unrelated deletions and untracked session artifacts are deliberately excluded.

**Disagreements preserved.** Codex framed single-instance as contradicting the roadmap's "second-process coordination out of scope" line; Claude accepts the risk and additionally establishes that the artifact is unpackaged (so the mitigation Codex hypothesised via MSIX does not exist here). Codex treated full multi-episode support as optional if unsupported inputs are surfaced safely; Claude agrees and keeps only the *honest classification* pre-1.0, not full multi-episode parsing.

**Verification.** Docs-only phase: no implementation code changed, so the accepted **300/300** backend baseline stands unchanged (not re-run for a docs edit). `git diff` confirms only `FUTURE_PLAN.md`, `PROGRESS.md`, and `PHASES.md` changed in this checkpoint.

## 12. Data / File Trust Pass (adversarial-reviewed)

**Purpose.** Fix confirmed trust defects that can corrupt identity, journal state, or catalog truth. No product features. Migration 0010 is a new forward script; no released migration was edited.

**Trust guarantees now enforced.**
- One process owns a library. `LibraryInstanceGuard` takes an exclusive OS lock on a `<db>.dropsort-lock` sibling file before any window and before `Migrate()`/recovery. A second process does not migrate, recover, or open `library.db`, and opens no window (it shows one "already running" message and exits). Ownership is released last at shutdown, after DB handles are disposed and the connection pool is cleared.
- TV import identity is stable across metadata matching. Migration 0010 adds `tv_show_aliases`. Import resolves the show by an immutable filename-derived alias, so a later episode under the original filename stays under one logical show. No duplicate show is created, and no `UNIQUE(provider, external_id)` conflict occurs. Re-importing the same file after a metadata rename is recognized as already registered (compared by show identity, not by the rewritten sort_title).
- TV-like filenames are classified honestly. Single `SxxExx` resolves across dot, underscore, space, and dash separators. Multi-episode and range forms (`S01E01E02`, `S01E01-E03`, `S01E01_S01E02`, `S01E01-03`, `1x01x02`) classify as `TvEpisodeUnsupported` (review), never as a movie or one arbitrary episode. A resolution tag such as `1x01-1080p` stays a single episode.

**Blockers closed (from the 1.0 freeze).** Single-instance ownership; stable TV import identity; honest unsupported-TV classification.

**Adversarial gate (Codex).** Verdict **needs-attention**. Three High, two Medium, no Critical. Independently verified and reconciled:
- **H1 shutdown releases ownership too early — ACCEPT, fixed.** The guard is now disposed last, after pools are cleared. The broader "await active background tasks at shutdown" concern is pre-existing and out of this phase; recorded.
- **H2 same-file re-import fails after a metadata rename — ACCEPT, fixed.** The existing-file check now compares the show by alias-resolved identity. New regression added.
- **H3 numeric ranges and repeated markers still registered as one episode — ACCEPT, fixed.** The multi-episode detector now covers `S01E01-03` and `S01E01_S01E02` (any separator). New cases added.
- **M1 x-form range consumed a resolution suffix — ACCEPT, fixed.** A trailing boundary stops `1x01-1080p` matching as a range. Negative case added.
- **M2 two distinct series whose filename titles normalize identically merge — DEFER.** Pre-existing under the `UNIQUE sort_title` contract; the alias mechanism does not worsen it. Needs a product decision on identity disambiguation.
- **M3 provider-identity `UNIQUE(provider, external_id)` collision throws a raw error on metadata match — DEFER.** Pre-existing; the alias fix reduces the trigger frequency. Needs a metadata-merge/refusal design.

**Verification.** Full backend suite **323 / 323** (0 failed, 0 skipped). One transient parallel flake occurred once in `ImportSelectedTests` construction; the isolated run passed 32/32 and the re-run passed 323/323. Focused trust suites (TV identity, TV classification, instance guard, single-instance UX) pass. `dotnet build` of the WinUI project fails only at the WinUI PRI task (`MSB4062`); no C# errors. Manual GUI acceptance passed for single-instance and TV classification; TV import identity is covered by automated regression by the owner's decision.

**Remaining risks.** M2 and M3 are recorded, not fixed. The guard is in-process and lock-file based; NTFS honors the share mode, but a deleted lock file mid-session or a filesystem that ignores share modes would weaken it. No two-process automated test exists; manual acceptance covers it. The backend suite does not build or run the WinUI application.

## 13. Drive-Aware Availability Model (adversarial-reviewed)

**Purpose.** Stop DropSort treating media on temporarily unavailable storage as deleted. No schema change: the new state is transient and is not persisted.

**Availability contract.** `AvailabilityInspectionStatus` has four states. **Available/Present**: the storage root is reachable and the file exists. **StorageUnavailable**: the root is not reachable or not ready (disconnected/unmounted drive). Not deleted; no path rewrite, no delete, no relink pressure. **Missing**: the root is reachable and the file is absent. The only true missing case. **Error**: access denied or another I/O condition; never collapsed into Missing. The model fails closed: if DropSort cannot prove a reachable root lacks the file, it does not claim the media was deleted.

**Storage identity.** The smallest reliable Windows check: the path's root (`Path.GetPathRoot`). A drive-letter root uses `DriveInfo(root).IsReady`; a UNC root uses `Directory.Exists`. The inspector probes with `File.GetAttributes`, not `File.Exists`, so access-denied and I/O conditions are distinguished from true absence. No new persistent identity data was added.

**Reconciliation safety.** `StorageUnavailable` does not `MarkMissing`, does not rewrite the last-known path, and creates no relink pressure (relink still requires status `Missing`). Availability is evaluated once per distinct root, so a disconnected drive with thousands of files is one probe and no per-file work. Reconnecting the storage restores `Present` on the next pass with no re-registration. `Error` and a late `StorageUnavailable` after a successful root probe are non-destructive. Watch history and personal state are untouched.

**Adversarial gate (Codex).** Verdict **needs-attention**. One High, three Medium, no Critical. Independently verified and reconciled:
- **H1 access-denied files could still be persisted as Missing — ACCEPT, fixed.** `Inspect` now probes with `File.GetAttributes` and returns `Error` for access/I/O conditions on a reachable root; only true not-found on a reachable root is `Missing`. A real-inspector test for parent-absent and directory-at-path was added.
- **M1 offline UNC probing has no timeout — DEFER.** UNC is not a confirmed 1.0 storage surface; local removable drives use the fast `DriveInfo.IsReady`. The per-root cache limits any UNC stall to once per root. Add a bounded, cancellable probe if and when UNC becomes a supported surface.
- **M2 an offline was-present file still shows as Present in the UI, and a completed check looks clean — DEFER to the Confirmed UX Corrective Pass.** No data loss and the hard "never falsely Missing" guarantee holds. Surfacing a transient "storage unavailable" signal (an unavailable-root count or a transient availability state) is a UI/reporting change; the user deferred UI work for this phase.
- **M3 tests simulate the outcome rather than exercising it — PARTIAL.** Added real-inspector classification tests (present/missing/storage-unavailable, parent-absent, directory-at-path). The real removable-drive round trip is deferred to Golden Journeys / RC / clean-machine verification per the owner's decision.

**Verification.** Full backend suite **329 / 329** (0 failed, 0 skipped). Focused availability and reconciliation suites pass. `dotnet build` of the WinUI project fails only at the WinUI PRI task (`MSB4062`); no C# errors. Manual external/removable-drive validation is **NOT APPLICABLE / NOT MANUALLY VERIFIED ON THIS MACHINE**; real removable-drive runtime verification is preserved for the Golden Journeys / RC / clean-machine stage.

**Remaining risks.** M1 (UNC probe latency) and M2 (offline file still displayed as available) are recorded, not fixed. Drive-letter volume-identity (a different volume mounted at the same letter is "ready" but holds different content) is out of scope; it is a relink-identity concern for a later phase. No real removable-drive automated test exists.

## 14. Early Backup Protection (adversarial-reviewed)

**Purpose.** Protect the local database before a risky mutation and provide a verified recovery primitive. This is not the future Portable Backup / Manifest feature. No new user-facing workflow: the backup is silent and its failure surfaces through the existing initialization-error path.

**Backup contract.** Automatic and pre-operation: a verified backup exists before the risky mutation begins. Consistent: taken with the SQLite online backup API (`SqliteConnection.BackupDatabase`), so committed WAL pages are captured, not a raw byte copy. Verified: opened read-only, `PRAGMA integrity_check` must return "ok" and the backup `user_version` must equal the source version. Fail closed: on any create/verify failure a partial file is removed and the caller aborts, leaving the database untouched. Local: backups live in `<dbDir>/backups`. Bounded retention: newest 5 kept, pruned only after the new backup verifies, never deleting the just-created backup, and a pruning failure never invalidates it.

**Protected operations.** Forward schema migration of an existing database (`DatabaseMigrator.Migrate`, only when `user_version` > 0 and a migration is pending; a fresh database has no data and is not backed up). Clear Library Data (`MaintenanceRepository.ClearCatalog`). Classified BACKUP NOT REQUIRED: ordinary watch-history/metadata/settings/import writes and transactions. `ClearWatchHistory` is NEEDS EVIDENCE and deferred (a targeted single-table clear; the whole-library clear is the protected path).

**Backup identity / naming.** `library.<UTCyyyyMMddTHHmmssfffZ>.v<sourceVersion>.<label>.<randomToken>.db`. The token makes same-millisecond collisions impossible; a `File.Exists` guard remains.

**Restore contract (backend primitive; not wired into any user/startup path).** Accepts only a structurally sound DropSort database within the supported schema range (`user_version` 1..latest and the core `movies` table). Preserves the current database with a WAL-safe snapshot, stages the replacement, swaps it in with an atomic same-volume `File.Replace`, removes stale WAL/SHM sidecars (fatal on failure), verifies the result, and rolls back to the preserved copy on failure.

**Adversarial gate (Codex).** Verdict **needs-attention**. Two Critical, one High, two Medium. Independently verified and reconciled:
- **C1 pre-restore preservation omitted committed WAL — ACCEPT, fixed.** The current database is preserved with a `BackupDatabase` snapshot, and sidecar removal is fatal before any swap.
- **C3 restore accepted any integrity-valid SQLite file — ACCEPT, fixed.** Restore now requires a supported DropSort schema version and the core table, before and after replacement.
- **M1 millisecond naming collision — ACCEPT, fixed.** A random token was added.
- **M2 no ClearCatalog backup test — ACCEPT, fixed.** Success and backup-failure tests were added.
- **C2 restore is not fully crash-safe/journaled — PARTIAL / DEFER.** Atomicity was improved (staged temp + same-volume `File.Replace` + rollback from the preserved snapshot), but a fully journaled restore with startup reconciliation, together with the user-facing restore workflow, is a documented pre-1.0 release gate. The primitive is not wired into any user or startup path, so no user can trigger it today.
- Codex privacy note — DEFER / DOCUMENT. Backups are a full copy of the library in a predictable local path. The app-data location is already per-user; an explicit owner-only ACL is a later hardening.

**Verification.** Full backend suite **337 / 337** (0 failed, 0 skipped) across repeated runs. Test parallelization was disabled so the real-SQLite suite is deterministic (it previously produced intermittent parallel-contention failures). `dotnet build` of the WinUI project fails only at the WinUI PRI task (`MSB4062`); no C# errors. No manual GUI acceptance is required: the behavior is backend and deterministically covered.

**Remaining risks.** The fully crash-safe journaled restore and the user-facing restore workflow (C2) are deferred to a pre-1.0 release gate. Backup-file privacy relies on the per-user app-data location. `ClearWatchHistory` is not backup-protected.

## Current boundary

Product Phase 3 is closed and manually accepted. Persistence Write Coordination, the Data / File Trust Pass, the Drive-Aware Availability Model, and **Early Backup Protection** are all done. Early Backup Protection is implemented, verified at **337/337**, and adversarial-reviewed; the last pre-1.0 trust blocker (automatic pre-migration backup) is closed. Committed locally on `codex`; not pushed; `main` untouched.

The next implementation phase is the **Confirmed UX Corrective Pass** (not started): make the accepted UI accurately represent current backend truth (availability wording, Check Library reporting, startup/backup failure messages) without a redesign. Do not begin it without an explicit Goal.
