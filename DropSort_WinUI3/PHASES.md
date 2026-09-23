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

## Current boundary

Product Phase 3 is implemented, locally verified at **296/296**, and **manually accepted** on 2026-09-24 against a freshly built WinUI app and a real upgraded database. It is committed locally on `codex` and **not pushed**; `main` is untouched.

The next implementation mini-phase is **Persistence Write Coordination** (recorded only — do not begin without an explicit Goal): inventory every writer to `library.db` and establish one clear write-coordination contract. The known remaining gap is that `FileOperationStore` writes `library.db` outside `DatabaseWriteGate`. Major 1.0 feature expansion is frozen; see `FUTURE_PLAN.md` for the revised release direction. Phase work stays gated: one explicit Goal at a time.
