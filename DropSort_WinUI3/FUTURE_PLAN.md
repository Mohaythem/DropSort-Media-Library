# DropSort WinUI 3 — Forward Plan

This is the authoritative forward plan for the WinUI rewrite as of 2026-09-23. Historical phase reports explain how the current system was built; they do not override this roadmap.

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

## Ordered roadmap

### Phase 3 — Home Artwork + Real Watch History

**Status:** Implementation and automated/runtime verification completed locally on 2026-09-23. Manual acceptance is pending; Phase 4 must not begin until the owner explicitly approves it.

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
