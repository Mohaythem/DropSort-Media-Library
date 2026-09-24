# DropSort WinUI 3 — Current Progress

Last updated: 2026-09-24

## Current checkpoint

- Branch: `codex`
- Product Phase 3 is **manually accepted and closed**. Persistence Write Coordination, the 1.0 Scope Freeze (2026-09-24), the Data / File Trust Pass, the Drive-Aware Availability Model, and **Early Backup Protection** are done. Early Backup Protection is implemented, adversarial-reviewed, and verified at **337/337**; the last pre-1.0 trust blocker is closed. All commits are local; `main` is untouched and nothing is pushed.
- Python V1 is a Frozen Reference. Active product work is in `DropSort_WinUI3`.

## Completed and accepted

- Rewrite foundation: Domain, Application, Infrastructure, FileSystem, WinUI shell, SQLite migrations 0001–0007, functional wiring, TV hierarchy, filesystem/recovery hardening, relink, TMDB provider, poster cache, and manual metadata matching.
- Product Phase 1 — Add Media: completed through selective mixed Movie/TV import, truthful pre-add/post-add metadata states, local-registration-first failure handling, and exact existing-media detection. Corrective commits are recorded in `PHASES.md`.
- Product Phase 2 — TMDB Search & Matching Quality: completed at `b158f9d` and manually accepted per the project checkpoint instruction.
- Product Phase 3 — Home Artwork + Real Watch History (incl. reliability closure): real Movie/TV watch events, episode watched state, Continue Watching progression, cached Home artwork, durable watch history, and — from the closure pass — migration v9 drift reconciliation, `DatabaseWriteGate` write coordination, Clear History / `DeleteWatchEvent` fixes, and logical-media Recently Watched (repeated movie → one card; TV episodes/seasons → one show-level card) with raw `watch_events` preserved. Manually accepted 2026-09-24.
- Persistence Write Coordination (mini-phase): one write-coordination contract for every writer to `library.db`; `FileOperationStore` and `SettingsRepository` now acquire `DatabaseWriteGate` like the catalog/personal/maintenance writers. Migration remains startup-exclusive. Verified 300/300.
- 1.0 Scope Freeze (2026-09-24): the DropSort 1.0 boundary is frozen and adversarial-reviewed (Codex, verdict needs-attention). Included/stabilization-only/post-1.0/out-of-scope sets, release blockers, and golden journeys are recorded in `FUTURE_PLAN.md`; the adversarial findings and their ACCEPT/REJECT/DEFER reconciliation are in `PHASES.md` §11. No implementation code changed.
- Data / File Trust Pass: single-instance ownership, stable TV import identity (migration 0010 `tv_show_aliases`), and honest TV filename classification. Adversarial-reviewed (Codex needs-attention); H1/H2/H3/M1 accepted and fixed, M2/M3 deferred as pre-existing. Verified 323/323. Details in `PHASES.md` §12.
- Drive-Aware Availability Model: distinguishes StorageUnavailable (disconnected/unmounted drive) from Missing (reachable root, absent file) and Error (access/I/O); reconciliation is non-destructive on unavailable storage and probes each root once. Adversarial-reviewed (Codex needs-attention); H1 (access-denied false Missing) fixed; M1 (UNC timeout), M2 (offline shown as available) deferred; M3 real-drive test deferred to Golden Journeys/RC. Verified 329/329. Details in `PHASES.md` §13.
- Early Backup Protection: automatic verified SQLite snapshot before a forward migration of an existing database and before Clear Library Data; fail-closed; bounded retention (5); backend restore primitive with WAL-safe preservation and compatibility gating. Adversarial-reviewed (Codex needs-attention); C1/C3/M1/M2 fixed; C2 (fully journaled crash-safe restore + user-facing restore workflow) deferred to a pre-1.0 release gate. Verified 337/337 (test parallelization disabled for determinism). Details in `PHASES.md` §14.

## Completed locally; manual acceptance pending

- None. (Product Phase 3 acceptance is recorded above.)

## Latest verification

Fresh on 2026-09-24 from the Early Backup Protection source tree:

- `dotnet test tests\DropSort.Tests\DropSort.Tests.csproj` — **337 passed, 0 failed, 0 skipped**, stable across four consecutive runs. Test parallelization is disabled so the real-SQLite suite is deterministic (it previously produced intermittent parallel-contention failures).
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
7. **Confirmed UX Corrective Pass** — next step (not started). Make the accepted UI match current backend truth (availability wording, Check Library reporting, failure messages); no redesign. Carries the deferred "storage unavailable" UI signal (`PHASES.md` §13 M2).
8. Golden Journeys + Scale QA. Carries real removable-drive runtime verification and the deferred user-facing restore workflow / crash-safe journaled restore (`PHASES.md` §14 C2).
9. Release Candidate.
10. Installer Lifecycle QA.
11. Clean-Machine / Installed-App Verification.
12. 0.9.x Preview.
13. 1.0.

Do not begin the Confirmed UX Corrective Pass or any later step without an explicit Goal. All pre-1.0 trust blockers are now closed; the remaining 1.0 release gate is exact-artifact / installed-app verification. The backend suite is backend evidence only. Details are in `FUTURE_PLAN.md`.

## Current cautions

- Historical reports use “Phase 1–4” for the original rewrite foundation; current product phases are separately identified in `PHASES.md`.
- Some early reports describe demo UI or unavailable TMDB/TV behavior that later commits replaced. Use current code, `PROGRESS.md`, and `FUTURE_PLAN.md` for present-tense decisions.
- Some early reports say Clear Library preserves personal history. Current code deletes personal state/watch events during the explicit full clear while preserving media bytes and the operation journal; Phase 3 preserves history only across ordinary individual removal/unavailability.
