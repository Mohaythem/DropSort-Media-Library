# TBD Movies WinUI 3 — Current Progress

Last updated: 2026-10-15

## Accepted UI and installer checkpoint — 2026-10-15

- The user accepted the expanded TV Details layout: bounded width 1280, wide poster 240 x 360, compact poster 170 x 255. TV Details now offers confirmed catalog-only removal, preserving physical media and watch-history snapshots.
- This checkpoint includes Home viewing activity, native landscape cards and Slate styling, frameless empty states, full-width Check Library with metadata issues first, and mixed movie/TV Watch Later and Favorites alongside custom lists.
- Schema v16 adds active viewings. Schema v17 adds TV personal memberships. These are additive migrations and use the existing verified pre-migration backup path. Historical watch events and existing movie memberships remain intact.
- Implementation/source commit: `52ed8914b69e044c5242e18ef5cc02b16656a78d`. Installer version: `0.10.0-rc.3`; file/installer version: `0.10.0.3`; assembly version remains `0.10.0.0`.
- Independent Luna source review found no material correctness or data-safety issue. The root corrected a stale artwork expectation in an existing source-contract test. Tests and runtime/UI verification are explicitly left to the user; no fresh suite result is claimed for this checkpoint.
- Release packaging uses a fresh export of the source commit and a Visual Studio Release x64 self-contained Rebuild. Final build results, checksums, and payload provenance are recorded beside the new installer under `artifacts/release/0.10.0-rc.3/BUILD_MANIFEST.json`.
- The per-user Inno installer preserves its existing AppId, creates a Start menu entry, and offers a desktop shortcut selected by default. No GitHub release or tag is created in this phase. Previous release artifacts, production data, unrelated changes, and legacy deletions are preserved.

The following sections record earlier checkpoints. Their version numbers and test totals are historical.

## Current checkpoint

- Branch: `codex`
- Folder Watcher and Smart Relink are **permanently removed by user decision on 2026-10-14**. Their UI, background monitoring, configuration, candidate search, cache integration, and feature tests are removed. Existing profile JSON files are retained untouched. The original Check Library checks and manual file selection remain available.
- Release preparation targets **0.10.0-rc.2**, an unsigned Windows x64 pre-release. Add Media, Smart TV Batch Import, Movie/TV Details, the clickable native Add Media Folder Picker, and the TMDB movie-reuse fix are retained. No database migration is introduced. The accepted `0.10.0-rc.1` artifacts remain unchanged.
- Removal verification: **737 full Debug tests passed, 0 failed, 0 skipped**; **125 focused tests passed**; Visual Studio Release x64 self-contained **Rebuild passed**. Independent Luna review completed, and its two dead-import findings were corrected and rechecked. Isolated schema-v15 compatibility checks passed for SQLite integrity, foreign keys, identities, editions, metadata, history, lists, and retained legacy JSON. Exact committed-source packaging and final runtime evidence are recorded in the RC2 artifact-side manifests.
- Movie Details: the native Figma redesign (node `0:1` / `137:21918`) and subsequent UI corrections are complete and manually accepted by the user. This includes responsive layout, theme/RTL presentation, watch history, metadata actions, file availability, and separate reviewed Rename/Move flows.
- Fresh verification on 2026-10-10: all **694 tests passed, 0 failed, 0 skipped**; Visual Studio Debug x64 **Rebuild passed**. The sandbox test attempt had one Windows Credential Manager access failure; the full rerun outside the sandbox passed using the test's isolated QA credential target. No real-library runtime check was performed in this verification.
- Smart TV Batch Import: **implemented locally; user manual acceptance pending**. The native Add Media workflow now supports folder-aware TV analysis, grouped series/season review, tri-state selection, corrections and exclusions, local-only import, existing-series targeting, duplicate-safe registration, cancellation, partial failures, and retry. Existing TV repositories, metadata services, aliases, and schema **v15** are reused. No migration, media move/rename/delete, commit, or push.
- Current TV verification on 2026-10-11: **734 tests passed, 0 failed, 0 skipped**; focused import/analysis/review tests **108 passed**; fresh Visual Studio Debug x64 **Rebuild passed**, with no reported warnings or errors. Native isolated-QA runtime checks passed for pre-confirmation read-only review, import/reimport, corrections, exclusions, additional versions, cancellation, navigation cancellation, missing-file recovery, Slate/Light/Dark, narrow layouts, and Arabic/RTL. The application is open with the isolated QA profile. Evidence and launch instructions: `artifacts/qa/smart-tv-complete-2094ff368df74cda96257db20e6b088b/HANDOFF.md`. Live credentialed TMDB and physical drive removal were not exercised.
- Product Phase 3 is **manually accepted and closed**. Persistence Write Coordination, the 1.0 Scope Freeze (2026-09-24), the Data / File Trust Pass, the Drive-Aware Availability Model, and **Early Backup Protection** are done. Early Backup Protection was implemented, adversarial-reviewed, and verified at **337/337** at that historical checkpoint; the last pre-1.0 trust blocker was closed. `main` remains untouched. Current remote synchronization is verified separately in the Git handoff.
- Python V1 is a Frozen Reference. Active product work is in `TBDMovies_WinUI3`.

## Completed and accepted

- Rewrite foundation: Domain, Application, Infrastructure, FileSystem, WinUI shell, SQLite migrations 0001–0007, functional wiring, TV hierarchy, filesystem/recovery hardening, relink, TMDB provider, poster cache, and manual metadata matching.
- Product Phase 1 — Add Media: completed through selective mixed Movie/TV import, truthful pre-add/post-add metadata states, local-registration-first failure handling, and exact existing-media detection. Corrective commits are recorded in `PHASES.md`.
- Product Phase 2 — TMDB Search & Matching Quality: completed at `b158f9d` and manually accepted per the project checkpoint instruction.
- Product Phase 3 — Home Artwork + Real Watch History (incl. reliability closure): real Movie/TV watch events, episode watched state, Continue Watching progression, cached Home artwork, durable watch history, and — from the closure pass — migration v9 drift reconciliation, `DatabaseWriteGate` write coordination, Clear History / `DeleteWatchEvent` fixes, and logical-media Recently Watched (repeated movie → one card; TV episodes/seasons → one show-level card) with raw `watch_events` preserved. Manually accepted 2026-09-24.
- Persistence Write Coordination (mini-phase): one write-coordination contract for every writer to `library.db`; `FileOperationStore` and `SettingsRepository` now acquire `DatabaseWriteGate` like the catalog/personal/maintenance writers. Migration remains startup-exclusive. Verified 300/300.
- 1.0 Scope Freeze (2026-09-24): the TBD Movies 1.0 boundary is frozen and adversarial-reviewed (Codex, verdict needs-attention). Included/stabilization-only/post-1.0/out-of-scope sets, release blockers, and golden journeys are recorded in `FUTURE_PLAN.md`; the adversarial findings and their ACCEPT/REJECT/DEFER reconciliation are in `PHASES.md` §11. No implementation code changed.
- Data / File Trust Pass: single-instance ownership, stable TV import identity (migration 0010 `tv_show_aliases`), and honest TV filename classification. Adversarial-reviewed (Codex needs-attention); H1/H2/H3/M1 accepted and fixed, M2/M3 deferred as pre-existing. Verified 323/323. Details in `PHASES.md` §12.
- Drive-Aware Availability Model: distinguishes StorageUnavailable (disconnected/unmounted drive) from Missing (reachable root, absent file) and Error (access/I/O); reconciliation is non-destructive on unavailable storage and probes each root once. Adversarial-reviewed (Codex needs-attention); H1 (access-denied false Missing) fixed; M1 (UNC timeout), M2 (offline shown as available) deferred; M3 real-drive test deferred to Golden Journeys/RC. Verified 329/329. Details in `PHASES.md` §13.
- Early Backup Protection: automatic verified SQLite snapshot before a forward migration of an existing database and before Clear Library Data; fail-closed; bounded retention (5); backend restore primitive with WAL-safe preservation and compatibility gating. Adversarial-reviewed (Codex needs-attention); C1/C3/M1/M2 fixed; C2 (fully journaled crash-safe restore + user-facing restore workflow) deferred to a pre-1.0 release gate. Verified 337/337 (test parallelization disabled for determinism). Details in `PHASES.md` §14.

## Smart TV Batch Import acceptance

- Smart TV Batch Import: implementation and isolated verification complete. The user confirmed successful manual functional testing. The final grouped-review placement is implemented and runtime-verified. On 2026-10-13 the user confirmed satisfaction with current application functionality, closing candidate functional/visual acceptance. Installer and stable-publication gates remain separate. Product Phase 3 acceptance remains recorded above.

## Final release-hardening status

- User accepted current functionality on 2026-10-13. Further app interaction stopped when requested.
- Fresh full Release tests: **734 passed, 0 failed, 0 skipped**; focused hardening tests: **165 passed, 0 failed, 0 skipped**. Fresh self-contained Release build and separate installer recompile passed.
- No product-code fix or migration was required. Original `0.10.0-rc.1` artifacts remain unchanged.
- Clean Windows installer lifecycle tests remain **not executed**: no Sandbox/VM is available, and the real installation must remain untouched. Unsigned distribution limits remain documented.
- Details and publication recommendation: [packaging/FINAL_RELEASE_HARDENING.md](packaging/FINAL_RELEASE_HARDENING.md). No stable tag or publication is authorized.

## Current release candidate

- Local version: **0.10.0-rc.1**. Latest published WinUI release: **0.9.0-preview**. The older Python `v1.0.0` tag does not identify the current native release.
- Fresh Release tests on 2026-10-13: **734 passed, 0 failed, 0 skipped**; focused TV/import/UI tests: **152 passed, 0 failed, 0 skipped**. Seven existing xUnit analyzer warnings remain.
- Release provenance and remaining acceptance limits: [packaging/RELEASE_MANIFEST.md](packaging/RELEASE_MANIFEST.md). Exact source commit, artifact checksums and packaged-runtime results are generated in the artifact-side manifest after the checkpoint.
- Installer/clean-machine acceptance and distribution signing remain open; application functional/visual acceptance is complete. No tag or public release is authorized.

## Latest verification

Fresh on 2026-10-13 for the manually accepted Folder Watcher checkpoint:

- Full suite: `dotnet test TBDMovies_WinUI3\tests\TBD Movies.Tests\TBD Movies.Tests.csproj --logger "trx;LogFileName=full-finalize.trx" --results-directory TBDMovies_WinUI3\artifacts\qa\folder-watcher-finalize-20261013` — **768 passed, 0 failed, 0 skipped**, exit **0**. Automated verification used isolated temporary fixtures, including the test-owned Windows credential target; no production library or real media was used.
- Fresh Visual Studio 18 Community `MSBuild.exe` rebuild of `TBD Movies.UI.csproj`, `Configuration=Debug`, `Platform=x64`, with a separate QA `BaseOutputPath` — exit **0**, no reported warnings or errors. This preserved the open QA build and existing release outputs. Local evidence: `artifacts/qa/folder-watcher-finalize-20261013/full-suite.log`, `full-finalize.trx`, and `debug-rebuild.log`.
- Regression coverage includes corrupt/unsupported JSON preservation, cancellation during file-fact reads, active-writer readiness, configuration-save rollback, persisted alerts and restart recovery, watcher-error recovery, bounded debounce, ambiguous moves, restored-file alert cleanup, and partial/offline baseline preservation.
- Prior native QA verified repeated live English/Arabic switching, Slate/Light/Dark, 800-pixel layouts, automatic synthetic-file detection, persisted alerts after restart, and visible corrupted-state errors. The user subsequently accepted the complete implementation and both pages' Folder Picker click/select/cancel behavior. This user acceptance is distinct from automated UI coverage.
- Codex inspected the final staged scope; Antigravity's final read-only review reported no confirmed commit-blocking correctness or security defect. The implementation commit contains only the 18 approved source/integration/localization/test files. QA artifacts, secrets, unrelated roadmap changes, and legacy deletions were excluded.
- Physical removable-drive, network-outage/sleep, and actual link-loop runtime scenarios were not exercised in this phase. Existing release/installer acceptance limits remain unchanged; this checkpoint does not rebuild or publish `0.10.0-rc.1`.

Fresh on 2026-10-11 for Smart TV Batch Import:

- Full suite: `dotnet test TBDMovies_WinUI3\tests\TBD Movies.Tests\TBD Movies.Tests.csproj --no-restore -v minimal`, isolated QA environment: **734 passed, 0 failed, 0 skipped**, exit **0**. The Windows credential fixture used its isolated QA target outside the sandbox. Seven pre-existing xUnit analyzer warnings remain.
- Focused suite: the same test project with `--no-build --no-restore -v minimal --filter` selecting `SmartTvImportSafetyTests`, `ImportFileValidatorTests`, `TvReviewGroupStateTests`, `TvBatchAnalysisTests`, `ImportSelectedTests`, and `AddMediaReviewStateTests`: **108 passed, 0 failed, 0 skipped**, exit **0**.
- Visual Studio 18 Community `MSBuild.exe TBDMovies_WinUI3\src\TBD Movies.UI\TBD Movies.UI.csproj /t:Rebuild /p:Configuration=Debug /p:Platform=x64 /v:minimal /m:1`: exit **0**, no reported warnings or errors. The exact executable, DLL, PRI, and Add Media XBF hashes are saved in the QA build manifest.
- Native runtime evidence: the bare-filename example imported **1 show / 2 seasons / 3 episodes / 3 files**; repeat import left these counts unchanged. Additional correction/version cases left the final manual QA profile at **1 show / 3 seasons / 4 logical episodes / 5 files**. Synthetic media hashes remained unchanged. Large-batch cancellation recorded **516 saved / 4246 not processed**; navigation cancellation, missing-file partial failure, and retry were verified separately. No real library was opened.
- Read-only Luna review identified three material issues; all were fixed and the follow-up review reported no additional material findings. The final count correction also has a regression test and a native runtime check.

The following Movie Details verification is a historical checkpoint:

Fresh on 2026-10-10 for the accepted Movie Details source tree:

- `dotnet test TBDMovies_WinUI3\tests\TBD Movies.Tests\TBD Movies.Tests.csproj --no-restore -v minimal` from the repository root, with a unique isolated `TBD Movies_TEST_DATA_ROOT`: **694 passed, 0 failed, 0 skipped**, exit code **0**, outside the sandbox. The initial sandbox attempt returned **693 passed, 1 failed** because its isolated Windows credential write was unavailable. Seven existing xUnit analyzer warnings appeared during that initial test compilation; no unrelated test code was changed.
- Visual Studio 18 Community `MSBuild.exe TBDMovies_WinUI3\src\TBD Movies.UI\TBD Movies.UI.csproj /t:Rebuild /p:Configuration=Debug /p:Platform=x64 /v:minimal /m:1` from the repository root: exit code **0**, no reported warnings or errors.
- Manual visual acceptance was supplied by the user. This verification did not launch the application, access the real media library, rebuild an installer, or establish clean-machine acceptance.

Historical Early Backup Protection verification follows; its totals and environment notes are not current Movie Details verification:

Fresh on 2026-09-24 from the Early Backup Protection source tree:

- `dotnet test tests\TBD Movies.Tests\TBD Movies.Tests.csproj` — **337 passed, 0 failed, 0 skipped**, stable across four consecutive runs. Test parallelization is disabled so the real-SQLite suite is deterministic (it previously produced intermittent parallel-contention failures).
- Focused suites pass: `DatabaseSafetyBackupTests` (create/verify/retention/restore, migration and ClearCatalog protection, fail-closed), plus availability, reconciliation, migration, and write-coordination suites.
- Codex adversarial review (needs-attention): C1 (WAL-safe pre-restore) and C3 (restore compatibility gating) fixed; M1 (naming collision) and M2 (ClearCatalog tests) fixed; C2 (fully journaled crash-safe restore + user-facing restore workflow) deferred to a pre-1.0 release gate. See `PHASES.md` §14.
- No manual GUI acceptance required: backup/restore behavior is backend and deterministically covered.
- Plain `dotnet build` of the WinUI project still fails only at `MSB4062` (missing `Microsoft.Build.Packaging.Pri.Tasks.dll` on the .NET SDK path); no C# errors. Use Visual Studio MSBuild for the UI workflow — a confirmed tooling-path limitation, not a blocker.

## Next and remaining

Revised release direction (2026-09-24). Product identity is fixed: **Local-First Personal Media Library + Real File Intelligence + Safe File Management**. Major 1.0 feature expansion is frozen.

Ordered path to 1.0:

1. **Phase 3 — Closed.**
2. **Persistence Write Coordination — Done** (300/300).
3. **Freeze 1.0 Scope — Done** (2026-09-24; adversarial-reviewed).
4. **Data / File Trust Pass — Done** (323/323; single-instance, TV identity, honest TV classification).
5. **Drive-Aware Availability Model — Done** (329/329; StorageUnavailable vs Missing; non-destructive reconciliation). Offline-vs-deleted blocker closed.
6. **Early Backup Protection — Done** (337/337; verified pre-migration and pre-clear backups; fail-closed; backend restore primitive). Last pre-1.0 trust blocker closed.
7. **Confirmed UX Corrective Pass — Done** (2026-09-25; 381/381). Schema advanced forward-only to **v13**. Real multiple custom lists with stable IDs; duplicate display names blocked on create/rename via .NET `OrdinalIgnoreCase` (handles accented non-ASCII case variants; SQLite `lower()` folds ASCII only); mixed Movie + TV custom lists (per-kind partial unique indexes; cascades never delete media); `Add media` picker over existing library items; Movie/TV Details "Add to list" show custom lists only while Watchlist/Favorites stay independent; integrated Sort SplitButton (criterion + direction); shell navigation moved to `ItemInvoked` so Home works from a detail page; shell-wide `Esc` Back sharing `GoBack()` with the visible Back, root-page Esc left unhandled. Manually accepted. The adversarial Codex review was **partial** — Codex hit its external usage limit mid-run; its two surfaced candidates were reconciled (Unicode name folding = accepted and fixed here; "Date added" by id = deferred documented proxy) and an independent A–G review found no ship blocker. A preview installer was produced (see item 10 / `packaging/`).
8. **Golden Journeys + Scale QA — done** (2026-09-26; 385/385). Isolated `TBD Movies_TEST_DATA_ROOT` QA at 1K/5K/10K. Fixed the large-library blocker (ListMovies N+1 → one bulk presence query, 34 s → 8 ms at 10K; restored GridView virtualization) and the Library interaction issues (vertical scroll ownership, full-width grid 7→8 columns at wide viewports, hidden Esc accelerator badge, single-focus cards) — all manually accepted. Installed 10K settles ~1.5 s at ~239 MB flat. See `QA_GOLDEN_JOURNEYS_0.9.0-preview.md`.
9. **Release Candidate — NOT READY (release hardening done 2026-09-26).** No reachable technical BLOCKER/HIGH found. Restore crash-safety is not a shipping risk (the primitive has no user/startup caller; atomic `File.Replace` + preserved `.pre-restore`); full journaled restore + startup reconciliation stays deferred. Backups inherit per-user `%LOCALAPPDATA%` ACLs (no unsafe change). Gate held only by resource-gated items, kept separate from product defects: removable-drive physical test, clean-machine install, UNC/slow-share behaviour (all `NOT EXECUTED` — no hardware/VM/share here), and code signing (`UNSIGNED, pending certificate`).
10. **Installer Lifecycle QA — preview produced** (2026-09-25; `0.9.0-preview`). Per-user Inno Setup single Setup EXE (`artifacts/release/0.9.0-preview/TBD MoviesSetup.exe`, unsigned) from the self-contained Release build; fresh install, launch-from-installed-location, uninstall, reinstall and user-data preservation verified on this machine. True cross-version upgrade not yet testable (no prior release); the fixed Inno `AppId` structurally supports it.
11. Clean-Machine / Installed-App Verification — `NOT EXECUTED` (needs a clean VM/Sandbox/second machine).
12. 0.9.x Preview.
13. 1.0.

Do not begin a later step without an explicit Goal. All pre-1.0 trust blockers are closed; remaining 1.0 gates are clean-machine/installed-app verification and code signing. The backend suite is backend evidence only. Details are in `FUTURE_PLAN.md`.

## Current cautions

- Historical reports use “Phase 1–4” for the original rewrite foundation; current product phases are separately identified in `PHASES.md`.
- Some early reports describe demo UI or unavailable TMDB/TV behavior that later commits replaced. Use current code, `PROGRESS.md`, and `FUTURE_PLAN.md` for present-tense decisions.
- Some early reports say Clear Library preserves personal history. Current code deletes personal state/watch events during the explicit full clear while preserving media bytes and the operation journal; Phase 3 preserves history only across ordinary individual removal/unavailability.
