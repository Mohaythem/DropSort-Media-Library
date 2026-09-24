# DropSort WinUI 3 — Forward Plan

This is the authoritative forward plan for the WinUI rewrite as of 2026-09-24. Historical phase reports explain how the current system was built; they do not override this roadmap. Product Phase 3 is **closed and manually accepted**; the revised release direction below supersedes the earlier Phase 4–9 ordering, whose detailed scope is retained for reference.

## Baseline and boundaries

- Product stack: C#, .NET 10, WinUI 3, Windows App SDK, and SQLite through `Microsoft.Data.Sqlite`.
- The Python/PySide6 V1 project is a **Frozen Reference**. It may be read for behavior, safety rules, and tests, but must not be modified or used as the active runtime.
- Active development branch: `codex`. Treat `main` as protected. Development stays on a separate branch and reaches `main` only through an explicitly approved integration step.
- The committed baseline before the Product Phase 3 work was `f59ce9c5647e08235076f0495990b332cfe13c6d`. Phase 3 implementation and this record are delivered together in one local checkpoint; its resulting hash is reported in the handoff rather than self-referenced here.
- Work is phase-gated: one explicit Goal at a time, one verified local phase commit, then STOP for review or manual acceptance. Do not begin a later phase implicitly.

## Accepted product and design contracts

These contracts remain in force unless a later explicit Goal changes them.

- DropSort is local-first. Core library access remains usable without TMDB, and metadata service failures never undo successful local registration.
- Add Media distinguishes local registration readiness from metadata state. `Ready to Add`, `Matched`, `Needs Match`, metadata service errors, and `Already in Library` are not interchangeable.
- Existing-media detection uses the registered media path identity, not movie/show identity. A different valid file for the same title is not automatically a duplicate.
- Metadata matching stays conservative. Ambiguous or low-confidence results require review; provider/auth/network failures are service errors, not legitimate no-match results.
- Filesystem changes retain the existing safety boundary: approved roots, reparse-point rejection, source identity locking, no overwrite, journal-first mutation, destination verification before source deletion, and recoverable failure handling.
- Missing media remains represented in the catalog unless an explicit user action says otherwise. Unexpected filesystem/system incidents start with forensic, read-only inspection.
- UI code remains presentation-focused and accesses data/services through Application contracts. Domain/Application do not depend on Infrastructure or UI.
- Preserve the accepted DropSort WinUI/Fluent visual system. Retain native controls, adaptive layouts, accessible focus/pointer/disabled states, English LTR, Arabic RTL, and Slate/Dark/Light behavior. Do not redesign approved screens while delivering backend work.
- Do not introduce fake domain data merely to make a UI surface, metric, or report appear complete.
- Current explicit **Clear Library Data** semantics are a separate whole-library action: it clears catalog records, TV hierarchy, personal state/watch events, metadata cache, and the app-owned poster cache where cleanup succeeds; it preserves physical media files and the operation journal, and it is blocked by nonterminal file operations. Phase 3 must not silently change this contract.

## Engineering and data policies

- Before important work, inspect the current branch, HEAD, `git status`, and relevant diffs. Preserve unrelated modified, deleted, and untracked files.
- Never use `git reset --hard`, `git clean`, force-push, rewrite published history, or perform broad destructive cleanup without explicit approval.
- Every fix or feature receives regression coverage proportional to its risk. Build success alone is insufficient for UI work; exercise the built runtime and the affected navigation/state path.
- Schema changes are additive, ordered, numbered SQLite migrations. Never edit an already-released migration in place.
- Keep user data outside application binaries. Runtime paths must be machine-independent and resolved from supported application-data locations, not a developer checkout or launch working directory.
- Before any migration, import, or manual database operation touches real user data, create a timestamped backup and verify its target. Tests and smoke runs use isolated temporary/test data roots.
- Migrations, imports, and startup initialization must be idempotent and safe to retry after interruption.
- Release Candidate validation must precede final release. The installed application must be tested independently from development execution.

## Revised release direction (2026-09-24)

**Product identity (fixed).** DropSort is a **Local-First Personal Media Library + Real File Intelligence + Safe File Management** application. Major **1.0 feature expansion is frozen**: the path to 1.0 is hardening, trust, and lifecycle QA, not new surface area.

**Prioritization rule.** Features that strengthen DropSort's understanding of local media, real files, storage, identity, or local viewing state are high priority. Features that would work equally well as a generic website are usually lower priority.

**Ordered path to 1.0.**

1. **Phase 3 — Closed** (manually accepted 2026-09-24).
2. **Persistence Write Coordination — Done** (300/300; see the contract below).
3. **Freeze 1.0 Scope — Done** (frozen 2026-09-24, adversarial-reviewed; see the freeze below).
4. **Data / File Trust Pass — Done** (323/323, adversarial-reviewed; single-instance ownership, stable TV import identity, honest TV classification; see `PHASES.md` §12).
5. **Drive-Aware Availability Model — Done** (329/329, adversarial-reviewed; StorageUnavailable vs Missing, non-destructive reconciliation; see `PHASES.md` §13).
6. **Early Backup Protection** — next step (not started). 1.0 blocker.
5. Drive-Aware Availability Model.
6. Early Backup Protection.
7. Confirmed UX Corrective Pass.
8. Golden Journeys + Scale QA.
9. Release Candidate.
10. Installer Lifecycle QA.
11. Clean-Machine / Installed-App Verification.
12. 0.9.x Preview.
13. 1.0.

### DropSort 1.0 Scope Freeze (frozen 2026-09-24, adversarial-reviewed)

**1.0 product definition.** DropSort 1.0 is a **local-first personal media library** that understands the user's real files, keeps logical media identity separate from physical file/path identity, and manages files safely. It is not a discovery/social/tracking product.

**A. Included in 1.0 (implemented; must stay release-quality).** Local-first SQLite catalog; Add Media (selective mixed Movie/TV import; local registration independent of metadata); optional TMDB enrichment + poster cache + manual match; Library / Home / My Lists / Movie & TV details; real persistent watch history; Recently Watched logical-media semantics (repeated movie → one card; TV episodes/seasons → one show-level card); Continue Watching; Play ≠ Mark Watched; journaled safe filesystem organize (approved roots, no overwrite, verify-before-delete, recoverable failure); missing ≠ deleted logical media; Clear Library Data semantics; settings/personal-state; Operations Log; English/Arabic RTL; single write-coordination contract (`DatabaseWriteGate`).

**B. Required stabilization before 1.0 (no new major features).** Data / File Trust Pass; narrow drive-aware availability; automatic pre-migration database backup; UX corrective pass; scale QA (~1K/5K/10K); installer + clean-machine + installed-app verification; Arabic/RTL release-quality pass.

**C. Post-1.0 (recorded, must not block 1.0).** Library Health, Smart Relink Suggestions, richer Drive intelligence/analytics, Copy Intelligence, Local Subtitle Intelligence, Organize Templates, Portable Local Backup / Manifest.

**D. Out of scope / avoid.** Discover expansion, ratings/reviews/social, analytics-heavy expansion, Letterboxd-style direction, large recommendation systems, and any generic media-tracking that does not depend on local filesystem intelligence.

**Release blockers (must be resolved before 1.0).** Blockers 1–3 are **CLOSED** by the Data / File Trust Pass (`PHASES.md` §12). Blocker 4 is **CLOSED** by the Drive-Aware Availability Model (`PHASES.md` §13). Blockers 5–6 remain open.
1. **Single-instance ownership** — a second process running startup migration/recovery over the same `library.db` can mark an in-flight file operation Failed while the first process is mid-copy, leaving an orphan destination and journal/catalog divergence. **Verified unconditional:** the UI project ships **unpackaged** (`DropSort.UI.csproj` `<WindowsPackageType>None</WindowsPackageType>`), has no `Package.appxmanifest`, no `AppInstance.FindOrRegisterForKey`/`RedirectActivationTo`, and no custom `Main` — `App.OnLaunched` unconditionally creates a window. So single-instance is enforced in neither dev nor the release artifact, and MSIX is not in use (adopting it would still require explicit `AppInstance` redirect code). Enforce per-user/per-database OS-level instance ownership before migration/recovery. (Resolve in Data / File Trust Pass.)
2. **Stable TV import identity** — **verified concrete, not merely fragile.** Episode import resolves the show by the filename-derived `sort_title` (`ImportService.RegisterEpisode` → `uow.TvShows.GetBySortTitle`). Metadata matching (`MetadataMatchingService.ApplyTvShowMetadataAsync`) rebuilds the show as `TvShowCatalogData(title: showMetadata.Title, provider, externalId, …)`, and `TvShow`/`TvShowCatalogData` derive `SortTitle = NormalizeTitle(Title)`, so `TvShowRepository.UpdateMetadata` overwrites both `title` **and** `sort_title` (and sets `provider`/`external_id`). No original filename alias is preserved anywhere. A later episode arriving under the original filename alias therefore misses `GetBySortTitle` and **creates a second show** (`sort_title` is UNIQUE, so the insert succeeds), splitting seasons/episodes/viewing state; matching that second show to the same provider identity then **violates `UNIQUE(provider, external_id)`** on `tv_shows`. Keep an immutable local import identity/alias independent of display metadata, and add the alias → match → next-episode regression before 1.0.
3. **Honest unsupported-TV classification** — unrecognized TV filename forms (e.g. underscore-delimited, multi-episode ranges, absolute numbering, specials) currently fall through to Movie classification. Classify unsupported/ambiguous inputs as unresolved-TV-needing-review rather than silently registering them as movies.
4. **Narrow drive-aware availability** — distinguish offline/unmounted storage from deleted media (do not present an unmounted drive's media as gone).
5. **Automatic verified database backup** before migrations and risky maintenance, with a restore path.
6. **Exact WinUI release artifact + installed-app verification** — the 300/300 suite is backend evidence only and does not exercise the WinUI project or installed app. Clean-machine install, first launch, upgrade, restart, Arabic/RTL, theme, filesystem recovery, repair/uninstall, and user-data preservation must be verified against the exact artifact built with Visual Studio MSBuild.

**Non-blocking hardening (may wait).** The plain `dotnet build` MSB4062 PRI failure (Visual Studio MSBuild builds fine); `FileOperationStore` single-connection read/write isolation; broader drive analytics; performance micro-tuning below the scale budgets.

**1.0 golden journeys (future release-verification matrix).** Create/open a library; add existing media safely; metadata match without losing local identity; movie details + real-file state; TV registration across seasons/episodes; watch history + Recently Watched (movie one-card, show-level TV card); organize a file safely; interrupted organize → recovery; file disappears (missing ≠ deleted); external/removable storage unavailable (offline ≠ deleted); relaunch/restart persistence; Clear personal history without corrupting unrelated state; automatic DB backup + restore before migration; install/upgrade/uninstall/reinstall lifecycle on a clean machine; Arabic/RTL + theme across the above.

**Adversarial gate.** This freeze passed a Codex adversarial review (verdict: needs-attention). Accepted findings are folded into the release blockers above (single-instance ownership, TV import identity, honest unsupported-TV classification, narrow drive/backup criteria, backend-only test caveat, dedicated reviewable freeze commit). The dotnet MSB4062 failure was confirmed a non-blocker. Full findings and the Claude ACCEPT/REJECT/DEFER reconciliation are recorded in `PHASES.md` §11.

**Assumptions still needing validation.** Supported Windows matrix; upgrade origin versions; installer technology (MSIX vs other) and whether it provides single-instance activation; rollback policy; concrete scale budgets; exact-artifact identity. These are release-planning inputs, not implementation work for the next phase.

**Next implementation phase: Data / File Trust Pass** (not started) — carries release blockers 1–5 above plus the recorded residual risks. Do not begin without an explicit Goal.

### Persistence Write Coordination (Done — 2026-09-24)

**Outcome.** One explicit write-coordination contract now governs every writer to `library.db`, and the two writers that bypassed it (`FileOperationStore`, `SettingsRepository`) were brought under it. Verified at 300/300.

**The write-coordination contract.**
- Every writer acquires `DatabaseWriteGate.Enter(connectionString)` — a fair, per-database-path, in-process, **non-reentrant** semaphore — as its outermost scope, before opening its IMMEDIATE (write-lock) transaction.
- The gate is released only after commit/rollback, via `using`, so exceptions and failed commits always release it.
- Reads that take no write lock are not gated.
- Non-reentrancy: a writer holding the gate must not call another gated writer. Nested write helpers (e.g. `FileOperationStore`'s private transition used by `CommitVerified`) run inside the already-open transaction and never re-acquire the gate.
- Critical sections are database-only and short. Slow filesystem work (copy, hash, source removal) happens **between** journal calls, never while the gate is held: `FileOperationCoordinator.Execute` runs each journal write as an independent gated transaction around ungated filesystem steps.
- Migration/startup initialization runs exclusively before any repository/service is exposed and is the one writer permitted to operate without the gate (no concurrent writer exists then).

**Audited writers.** Catalog unit of work, Personal library, `LibraryMaintenanceRepository.ClearCatalog`: already gated. `FileOperationStore.Create/Transition/CommitVerified` and `SettingsRepository.Set/Delete`: **now gated** (were not). `DatabaseMigrator`: ungated by design (startup-exclusive).

**Remaining risks.** `FileOperationStore` keeps one long-lived connection whose ungated reads share it with gated writes — writers serialise, but a concurrent read+write on that single connection is not thread-safe (the store is driven sequentially from UI/startup today; a future hardening could isolate its reads). The gate is in-process only and does not coordinate a second OS process against the same database (out of scope for single-instance local-first use).

### Preserved future product priorities (recorded; not scheduled)

Retain, without implementing now: Library Health, Smart Relink Suggestions, Drive Awareness, Copy Intelligence, Local Subtitle Intelligence, Organize Templates, Portable Local Backup / Manifest. Each is subject to the prioritization rule above.

## Ordered roadmap

### Phase 3 — Home Artwork + Real Watch History

**Status:** **Closed — implemented, locally verified at 296/296, and manually accepted on 2026-09-24** against a freshly built WinUI app and a real upgraded (drifted v8 → v9) database. Closure added migration v9 drift reconciliation, `DatabaseWriteGate` write coordination, Clear History / `DeleteWatchEvent` fixes, and logical-media Recently Watched (repeated movie → one card; TV episodes/seasons → one show-level card) with raw `watch_events` preserved. Committed locally on `codex`; not pushed; `main` untouched.

**Scope**

- Replace remaining placeholder/demo Home projections in this area with real persisted data.
- Show real cached artwork for Continue Watching.
- Add real Recently Watched / Watch History content on Home.
- Support both Movies and TV in the persisted watch-event model and Home projections.
- Preserve historical watch events when an individual library media item is removed or becomes unavailable. Retain cached title/artwork snapshots where available.
- Handle playback attempts for unavailable media gracefully, with a truthful state and a safe recovery/navigation path.
- Add the next numbered migration if persistence changes are required; preserve stable IDs and migration idempotency.

**Non-goals**

- Do not change Clear Library Data semantics unless a separate explicit Goal requests it.
- Do not redesign Add Media, TMDB ranking/manual search, or the accepted WinUI/Fluent interface.
- Do not add unrelated V2 features, folder watching, cloud services, or fake history/artwork.

**Definition of Done**

- Continue Watching and Recently Watched are derived entirely from real repositories/services for Movies and TV.
- A real watch event survives ordinary media removal/unavailability and can still render its cached identity/artwork when available.
- Unavailable playback never crashes, silently succeeds, or mutates media; the user receives a clear outcome.
- New persistence is backward-compatible, retry-safe, and covered by migration/upgrade tests.
- Application boundaries remain intact and all accepted language/theme/adaptive behaviors still work.

**Verification expectations**

- Targeted unit/integration tests for Movie and TV watch-event persistence, ordering, snapshots, unavailable media, and removal/reconciliation behavior.
- Migration tests for empty and populated older databases, repeated startup, foreign-key integrity, and interrupted/retried initialization.
- Full .NET suite; supported Visual Studio MSBuild solution build and WinUI x64 build with no project warnings.
- Isolated runtime smoke covering Home, Movie/TV details, real watch actions, restart persistence, unavailable playback, English/Arabic direction, and Light/Dark/Slate themes.

### Phase 4 — End-to-End Stabilization

> Reference detail. The sections below (Phases 4–9) predate the 2026-09-24 revised release direction above. They are retained for their scope/Definition-of-Done/verification content and are reframed under the revised ordered path; do not treat their numbering as the current next step.

**Scope**

- Exercise and repair confirmed integration defects across startup, Add Media, matching, Library/Home/details, playback, watch history, Check Library, relink, organize/undo/recovery, settings, and shutdown/restart.
- Validate cancellation, concurrency, offline/provider failure, missing media, crash recovery, and data consistency with realistic mixed libraries.

**Non-goals**

- No new product features, visual redesign, speculative refactor, or weakened safety rule.

**Definition of Done**

- All supported workflows complete without crashes, stale state, duplicate records, unsafe file changes, or SQLite/filesystem divergence.
- Known defects are fixed with regressions, and unresolved risks are explicitly documented and accepted or blocking.

**Verification expectations**

- Focused failure-path and recovery suites, full suite, clean restore, supported solution/UI builds, and repeated isolated runtime journeys with real temporary media.
- Verify each filesystem mutation boundary, restart point, and rollback/recovery outcome without touching real user media.

### Phase 5 — Release Candidate

**Scope**

- Freeze a named RC commit and version, finalize the acceptance matrix, and produce a reproducible candidate build from a clean checkout.
- Consolidate release-blocking risks, migration notes, known limitations, and operator/user documentation.

**Non-goals**

- No feature expansion or cosmetic redesign. Any material code change invalidates the candidate and requires targeted revalidation.

**Definition of Done**

- One traceable RC commit has clean restore/build/test/runtime evidence and no unresolved critical/high safety or data-integrity defect.
- Release notes and known limitations match the actual candidate.

**Verification expectations**

- Re-run the complete automated and manual acceptance matrix on the exact RC artifacts; record commands, environment, results, and hashes.

### Phase 6 — Installer Lifecycle QA

**Scope**

- Validate install, first launch, upgrade from supported prior builds, repair/reinstall where supported, and uninstall behavior.
- Confirm binaries and user data remain separated and that upgrade migrations are backup-first and idempotent.

**Non-goals**

- No product feature work and no installer behavior that silently deletes user data.

**Definition of Done**

- Lifecycle operations are repeatable on clean and populated machines/VMs, shortcuts/registration are correct, upgrades preserve supported data, and uninstall behavior is explicitly documented.

**Verification expectations**

- Fresh-machine and upgrade matrices across supported Windows configurations, including interrupted/retried install and launch, with timestamped data backups and post-action inspection.

### Phase 7 — Repository and Security Hygiene

**Scope**

- Audit tracked artifacts, ignored build output, secrets/credentials, dependency advisories, licenses/notices, release configuration, and documentation accuracy.
- Remove or quarantine only confirmed repository-owned debris through reviewable changes.

**Non-goals**

- No broad cleanup, dependency churn, history rewriting, or deletion of user-owned/unrelated files.

**Definition of Done**

- No committed credentials or known unassessed critical dependency issue; repository instructions reproduce the supported build; release inputs are intentional and reviewable.

**Verification expectations**

- Secret and dependency scans, clean-checkout restore/build/test, tracked-file review, and manual verification of third-party notices. Document any accepted exception.

### Phase 8 — Installed-App Verification

**Scope**

- Validate the installed RC independently of source-tree execution, IDE state, developer environment variables, or build-output paths.
- Exercise startup/restart, persistence, Home/Library/Add Media, matching/offline behavior, Movie/TV history, file safety, language/theme, and recovery surfaces.

**Non-goals**

- No debugging-only test seams or source-tree assets may be required for the installed app to work.

**Definition of Done**

- The installed application passes the acceptance matrix using its installed binaries and isolated user data on a representative clean environment.

**Verification expectations**

- Capture installer/artifact hashes, installed paths, data paths, runtime logs, UI smoke evidence, restart/upgrade evidence, and cleanup results.

### Phase 9 — Final Release Preparation

**Scope**

- Finalize versioning, release notes, checksums, distribution artifacts, rollback/support notes, and the exact release commit/tag.
- Reconcile all governance/status documents with the shipped artifact.

**Non-goals**

- No last-minute feature work. A product-code fix returns the project to the appropriate verification phase and creates a new RC.

**Definition of Done**

- The release artifacts are reproducible from the recorded commit, all required approvals and evidence are present, and no blocking issue remains open.

**Verification expectations**

- Final hash/signature checks where applicable, artifact launch/install spot check, documentation link review, and confirmation that the shipped version equals the accepted installed RC.
