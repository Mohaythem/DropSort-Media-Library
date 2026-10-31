# TBD Movies WinUI 3 — Forward Plan

This is the authoritative forward plan for the WinUI rewrite as of 2026-09-24. Historical phase reports explain how the current system was built; they do not override this roadmap. Product Phase 3 is **closed and manually accepted**; the revised release direction below supersedes the earlier Phase 4–9 ordering, whose detailed scope is retained for reference.

**Release 1 / Release 2 boundary:** This file remains the source of truth for Release 1 / 1.0 correctness, scope, and release hardening. Future Release 2 ideas are recorded in [RELEASE_2_V2_ROADMAP.md](RELEASE_2_V2_ROADMAP.md). That document authorizes no implementation and closes no Release 1 gate.

## Baseline and boundaries

- Product stack: C#, .NET 10, WinUI 3, Windows App SDK, and SQLite through `Microsoft.Data.Sqlite`.
- The Python/PySide6 V1 project is a **Frozen Reference**. It may be read for behavior, safety rules, and tests, but must not be modified or used as the active runtime.
- Active development branch: `codex`. Treat `main` as protected. Development stays on a separate branch and reaches `main` only through an explicitly approved integration step.
- The committed baseline before the Product Phase 3 work was `f59ce9c5647e08235076f0495990b332cfe13c6d`. Phase 3 implementation and this record are delivered together in one local checkpoint; its resulting hash is reported in the handoff rather than self-referenced here.
- Work is phase-gated: one explicit Goal at a time, one verified local phase commit, then STOP for review or manual acceptance. Do not begin a later phase implicitly.

## Accepted product and design contracts

These contracts remain in force unless a later explicit Goal changes them.

- TBD Movies is local-first. Core library access remains usable without TMDB, and metadata service failures never undo successful local registration.
- Add Media distinguishes local registration readiness from metadata state. `Ready to Add`, `Matched`, `Needs Match`, metadata service errors, and `Already in Library` are not interchangeable.
- Existing-media detection uses the registered media path identity, not movie/show identity. A different valid file for the same title is not automatically a duplicate.
- Metadata matching stays conservative. Ambiguous or low-confidence results require review; provider/auth/network failures are service errors, not legitimate no-match results.
- Filesystem changes retain the existing safety boundary: approved roots, reparse-point rejection, source identity locking, no overwrite, journal-first mutation, destination verification before source deletion, and recoverable failure handling.
- Missing media remains represented in the catalog unless an explicit user action says otherwise. Unexpected filesystem/system incidents start with forensic, read-only inspection.
- UI code remains presentation-focused and accesses data/services through Application contracts. Domain/Application do not depend on Infrastructure or UI.
- Preserve the accepted TBD Movies WinUI/Fluent visual system. Retain native controls, adaptive layouts, accessible focus/pointer/disabled states, English LTR, Arabic RTL, and Slate/Dark/Light behavior. Do not redesign approved screens while delivering backend work.
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

**Product identity (fixed).** TBD Movies is a **Local-First Personal Media Library + Real File Intelligence + Safe File Management** application. Major **1.0 feature expansion is frozen**: the path to 1.0 is hardening, trust, and lifecycle QA, not new surface area.

**Prioritization rule.** Features that strengthen TBD Movies's understanding of local media, real files, storage, identity, or local viewing state are high priority. Features that would work equally well as a generic website are usually lower priority.

**Ordered path to 1.0.**

1. **Phase 3 — Closed** (manually accepted 2026-09-24).
2. **Persistence Write Coordination — Done** (300/300; see the contract below).
3. **Freeze 1.0 Scope — Done** (frozen 2026-09-24, adversarial-reviewed; see the freeze below).
4. **Data / File Trust Pass — Done** (323/323, adversarial-reviewed; single-instance ownership, stable TV import identity, honest TV classification; see `PHASES.md` §12).
5. **Drive-Aware Availability Model — Done** (329/329, adversarial-reviewed; StorageUnavailable vs Missing, non-destructive reconciliation; see `PHASES.md` §13).
6. **Early Backup Protection — Done** (337/337, adversarial-reviewed; verified pre-migration and pre-clear backups, backend restore primitive; see `PHASES.md` §14).
7. **Confirmed UX Corrective Pass** — next step (not started).
5. Drive-Aware Availability Model.
6. Early Backup Protection.
7. Confirmed UX Corrective Pass.
8. Golden Journeys + Scale QA.
9. Release Candidate.
10. Installer Lifecycle QA.
11. Clean-Machine / Installed-App Verification.
12. 0.9.x Preview.
13. 1.0.

### TBD Movies 1.0 Scope Freeze (frozen 2026-09-24, adversarial-reviewed)

**1.0 product definition.** TBD Movies 1.0 is a **local-first personal media library** that understands the user's real files, keeps logical media identity separate from physical file/path identity, and manages files safely. It is not a discovery/social/tracking product.

**A. Included in 1.0 (implemented; must stay release-quality).** Local-first SQLite catalog; Add Media (selective mixed Movie/TV import; local registration independent of metadata); optional TMDB enrichment + poster cache + manual match; Library / Home / My Lists / Movie & TV details; real persistent watch history; Recently Watched logical-media semantics (repeated movie → one card; TV episodes/seasons → one show-level card); Continue Watching; Play ≠ Mark Watched; journaled safe filesystem organize (approved roots, no overwrite, verify-before-delete, recoverable failure); missing ≠ deleted logical media; Clear Library Data semantics; settings/personal-state; Operations Log; English/Arabic RTL; single write-coordination contract (`DatabaseWriteGate`).

**B. Required stabilization before 1.0 (no new major features).** Data / File Trust Pass; narrow drive-aware availability; automatic pre-migration database backup; UX corrective pass; scale QA (~1K/5K/10K); installer + clean-machine + installed-app verification; Arabic/RTL release-quality pass.

**C. Post-1.0 (recorded, must not block 1.0).** Library Health, Smart Relink Suggestions, richer Drive intelligence/analytics, Copy Intelligence, Local Subtitle Intelligence, Organize Templates, Portable Local Backup / Manifest. These, together with the 2026-09-28 idea set (multiple versions, duplicate intelligence, smart TV batch import, folder watching, watch-system extras, smart lists, advanced search, metadata tools, local metadata editing, library views, artwork viewer, user-facing backup/restore), are expanded, merged, and prioritized in **Post-1.0 Feature Backlog — prioritized (added 2026-09-28)** below.

**D. Out of scope / avoid.** Discover expansion, ratings/reviews/social, analytics-heavy expansion, Letterboxd-style direction, large recommendation systems, and any generic media-tracking that does not depend on local filesystem intelligence.

**Release blockers (must be resolved before 1.0).** Blockers 1–3 are **CLOSED** by the Data / File Trust Pass (`PHASES.md` §12). Blocker 4 is **CLOSED** by the Drive-Aware Availability Model (`PHASES.md` §13). Blocker 5 is **CLOSED** by Early Backup Protection (`PHASES.md` §14). Blocker 6 (exact WinUI artifact + installed-app verification) remains open.
1. **Single-instance ownership** — a second process running startup migration/recovery over the same `library.db` can mark an in-flight file operation Failed while the first process is mid-copy, leaving an orphan destination and journal/catalog divergence. **Verified unconditional:** the UI project ships **unpackaged** (`TBD Movies.UI.csproj` `<WindowsPackageType>None</WindowsPackageType>`), has no `Package.appxmanifest`, no `AppInstance.FindOrRegisterForKey`/`RedirectActivationTo`, and no custom `Main` — `App.OnLaunched` unconditionally creates a window. So single-instance is enforced in neither dev nor the release artifact, and MSIX is not in use (adopting it would still require explicit `AppInstance` redirect code). Enforce per-user/per-database OS-level instance ownership before migration/recovery. (Resolve in Data / File Trust Pass.)
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

Retain, without implementing now: Library Health, Smart Relink Suggestions, Drive Awareness, Copy Intelligence, Local Subtitle Intelligence, Organize Templates, Portable Local Backup / Manifest. Each is subject to the prioritization rule above. These are expanded, merged with newly proposed ideas, and prioritized in the backlog below.

### Post-1.0 Feature Backlog — prioritized (added 2026-09-28)

The backlog below is retained as the earlier post-1.0 proposal. Use [RELEASE_2_V2_ROADMAP.md](RELEASE_2_V2_ROADMAP.md) for current V2 grouping and prioritization where they differ. Release 1 scope and correctness requirements in this file remain authoritative.

This backlog integrates the 2026-09-28 idea set with the existing post-1.0 priorities. It does **not** enlarge 1.0: the freeze holds, and every item here is After 1.0, Later, or Avoid unless it is a pure correctness prerequisite already tracked as a release blocker. Ordering within a tier follows the prioritization rule — features that deepen understanding of local media, real files, storage, identity, or local viewing state rank above features that would work equally well as a generic website. Nothing here changes TBD Movies's identity: local-first native media library with real-file intelligence and safe file management — not a social, streaming, or recommendation-first product.

#### Tier 0 — Before 1.0 (correctness only; already tracked, no new surface)

These are not new features; they are the correctness foundations several backlog items depend on, and they are already release blockers or accepted bugs. Listed here only to record the dependency.

- **Logical movie identity — one Movie, many MediaFiles.** The same title with more than one local file must resolve to a single Movie with multiple associated files, never a second empty/duplicate card. This is the correctness base for *Multiple Versions* and *Duplicate & Version Intelligence* below. Tracked as the duplicate-movie/empty-card correctness work; consistent with the existing identity contract (logical media identity is separate from physical file/path identity).
- **Stable TV import identity + honest unsupported-TV classification.** Release blockers 2 and 3. These are the correctness half of *Smart TV Batch Import*; the batch efficiency/UX is deferred to Tier 1.

#### Tier 1 — After 1.0, near-term (real-file / local-media intelligence; highest post-1.0 priority)

Ranked; earlier items unblock later ones.

- **Multiple Versions.** One Movie holds multiple version files (1080p, 4K, Extended, Director's Cut, alternate encodes) with a user-preferred version. *Depends on:* Tier 0 logical movie identity. *Schema:* additive version metadata on media files.
- **Duplicate & Version Intelligence.** Detect duplicate files, different encodes, and the same movie across multiple drives; offer **safe merge suggestions** (never automatic destructive merges). *Depends on:* Multiple Versions; file fingerprints. Merges the existing duplicate-intelligence intent.
- **Smart TV Batch Import.** Identify the show once, parse `SxxEyy`, map local files to episodes, fetch episode metadata efficiently, **bulk-add matched episodes**, and route only ambiguous ones to review. Its own phase (kept separate per prior direction). *Depends on:* Tier 0 TV identity + honest classification. Confirms the existing 1.0 TV model (one show = one card; Show → Seasons → Episodes) rather than replacing it.
- **Automatic Library Monitoring (Folder Watcher).** Watch approved roots for new media, wait until copy/download completes, then send items through the **normal Review/import pipeline** (no silent auto-add). Merges the recorded *Copy Intelligence*. *Depends on:* import pipeline; copy-complete detection.
- **Smart Relink.** Find moved/renamed files by filename, size, and fingerprints; especially for external drives. Merges *Smart Relink Suggestions*; extends today's manual relink. *Depends on:* fingerprints; drive-aware availability (done).
- **Storage Intelligence.** Usage by drive, Movies vs TV, largest media, offline/removable drives, low-space awareness. Merges *Drive Awareness / richer drive analytics*. *Depends on:* drive-aware availability (done).
- **Library Health dashboard.** One surface for missing files, offline drives, duplicates, missing metadata/posters, unmatched files, broken episode mappings, and stale cache. Merges *Library Health*; extends today's Check Library. *Depends on:* duplicate intelligence, drive-aware (done).
- **Local Subtitle Intelligence.** Detect local subtitle files, identify language, associate them with the movie/episode, and show availability/missing state. Merges the recorded *Local Subtitle Intelligence*. *Depends on:* real-file scan.
- **Metadata management.** Refresh metadata, refresh poster only, change match (exists today), and **metadata lock**. *Depends on:* none major; lock is the prerequisite for safe local editing.
- **Local metadata editing.** Edit local Title / Overview / Year / Poster for rare or non-TMDB titles. *Depends on:* metadata lock (so a later refresh cannot clobber local edits).
- **User-facing Backup / Restore.** A strong backup/restore for the database, lists, watch history, local metadata edits, and preferences — **never the video files**. Merges *Portable Local Backup / Manifest*; builds on the shipped verified pre-migration backup + backend restore primitive. *Depends on:* existing backup primitive (done).
- **Advanced search filters.** Year, genre, type, watched state, resolution, drive, metadata state, date added; optionally saved as Smart Lists. *Depends on:* indexed metadata/file/watch state.
- **Smart Lists.** Rule-based lists beside custom lists (Unwatched, Recently Added, 4K, Missing Metadata, Missing Files, Favorites, specific drive, …). *Depends on:* Advanced search predicates; watched + file/metadata state. Extends existing custom lists.
- **Safe Organize / Rename Templates.** File organization must not assume that the user's media belongs in one central library folder. TBD Movies supports libraries spread across multiple folders, drives, removable media, and network locations. The default Organize action should therefore be **Rename in current location**, preserving the file's existing parent folder. Moving is an explicit secondary choice: **Move to another folder...**, optionally combined with rename. Movie and TV naming templates may suggest clean filenames, but the user sees a full source → destination preview before any filesystem mutation. All operations continue through the journaled safe-file pipeline with no overwrite, source identity validation, destination verification, and recoverable failure handling. Future batch organize must support Preview / Dry Run and never silently consolidate media into a single root. *Depends on:* journaled organize (exists).
- **Operation history + safe Undo.** Track Move / Rename / Relink / Merge and allow safe Undo where reversible. Extends today's Operations Log + organize-undo. *Depends on:* Relink and Merge features above.

#### Tier 2 — After 1.0, experience/UI (useful, lower priority; nearer the "generic-website" line)

- **Add Media Review UX.** Expand Add Media to use the available Library content width instead of keeping the scan/review workflow in a narrow left-side column. The page should prioritize media that requires user attention: new items, Ready to Add items, metadata matches, and items needing review. Files already registered in the Library should not dominate repeated scans of the same folder. Move **Already in Library** results into a separate section at the end of the review flow, collapsed by default and showing a count such as `265 items already in Library`. The user may expand that section when needed. Provide a dedicated **Refresh metadata** action for existing items, with an explicit confirmation before refreshing a large batch. Refreshing metadata must never modify, move, rename, re-register, or otherwise mutate the physical media files. Preserve selective import, mixed Movie/TV review, path-identity duplicate detection, virtualization, and conservative metadata matching.
- **Library Views: Poster Grid ↔ Horizontal Cards.** Provide two persistent Library presentation modes: the existing large Poster Grid and real landscape Horizontal Cards with a portrait poster, title/year, and lightweight local-library status. Horizontal Cards must remain a distinct Fluent card surface rather than a compact list item. Switching modes preserves search, filters, sorting, virtualization, lazy artwork loading, and scroll state where practical. Movie and TV use the same presentation concept, with media-specific lightweight summaries. The selected mode persists across restart. RTL mirrors card composition without mirroring artwork. *Status:* initial V2 implementation completed and manually accepted for continued use; further visual polish remains non-blocking.
- **Artwork Viewer.** Clicking a poster opens an in-app image viewer: zoom (`Ctrl +`, `Ctrl -`, mouse wheel), Fit to window, 100%, reset zoom, and pan while zoomed. *Depends on:* poster cache (exists).
- **Watch-system extras.** Explicit Watched/Unwatched toggle and **Up Next for TV**. The 1.0 watch model (watch history, Recently Watched, Continue Watching, Play ≠ Mark Watched, logical-media semantics) already ships; these extend it. *Depends on:* TV episode model; watch events (exist).

#### Tier 3 — Later (needs a bigger design or an integrated/observed player; revisit after the above)

- **Resume position.** Accurate resume/Continue-Watching by playback position requires an integrated player or a reliable way to observe an external player's position; deferred until that design exists.
- **Artwork Viewer — Poster/Backdrop navigation** and **manual artwork selection** (choosing among multiple provider images).
- **Letterboxd import/export.** Data interop only (import/export lists/history), explicitly **not** social/ratings features. Kept Later and narrowly scoped so it does not pull TBD Movies toward a tracking/social product.

#### Tier 4 — Avoid / only if a concrete need appears

Consistent with the frozen "Out of scope / avoid" list.

- **Viewing statistics / analytics-heavy dashboards** beyond simple local counts — risks analytics-first drift.
- **Richer Discover / catalog browsing** — discovery-first direction, out of the local-first identity.
- **Provider/plugin architecture** — only if a real second-provider need materializes; do not build speculative extensibility.
- **Any social/ratings/reviews/recommendation-first surface** — already out of scope; restated so backlog growth cannot reintroduce it.

#### Proposed order (post-1.0)

Tier 0 correctness lands with/inside 1.0 hardening. Then, post-1.0: **(1)** Multiple Versions → **(2)** Duplicate & Version Intelligence → **(3)** Smart TV Batch Import → **(4)** Automatic Library Monitoring → **(5)** Smart Relink → **(6)** Storage Intelligence → **(7)** Library Health → **(8)** Local Subtitle Intelligence → **(9)** Metadata management → **(10)** Local metadata editing → **(11)** User-facing Backup/Restore → **(12)** Advanced search → **(13)** Smart Lists → **(14)** Organize Templates → **(15)** Operation history + Undo, with Tier 2 experience items slotted opportunistically between file-intelligence phases. Tier 3/4 are revisited only when their prerequisites or a concrete need exist. Each phase stays gated: one explicit Goal, one verified commit, then STOP.



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
