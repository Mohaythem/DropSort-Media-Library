# DropSort WinUI 3 — Current Progress

Last updated: 2026-09-24

## Current checkpoint

- Branch: `codex`
- Product Phase 3 is **manually accepted and closed**. The **Persistence Write Coordination** mini-phase is implemented and locally verified at **300/300**. Its implementation commit and a separate governance/docs commit are local only; hashes are reported in the handoff. No network fetch or push was performed; `main` is untouched.
- Python V1 is a Frozen Reference. Active product work is in `DropSort_WinUI3`.

## Completed and accepted

- Rewrite foundation: Domain, Application, Infrastructure, FileSystem, WinUI shell, SQLite migrations 0001–0007, functional wiring, TV hierarchy, filesystem/recovery hardening, relink, TMDB provider, poster cache, and manual metadata matching.
- Product Phase 1 — Add Media: completed through selective mixed Movie/TV import, truthful pre-add/post-add metadata states, local-registration-first failure handling, and exact existing-media detection. Corrective commits are recorded in `PHASES.md`.
- Product Phase 2 — TMDB Search & Matching Quality: completed at `b158f9d` and manually accepted per the project checkpoint instruction.
- Product Phase 3 — Home Artwork + Real Watch History (incl. reliability closure): real Movie/TV watch events, episode watched state, Continue Watching progression, cached Home artwork, durable watch history, and — from the closure pass — migration v9 drift reconciliation, `DatabaseWriteGate` write coordination, Clear History / `DeleteWatchEvent` fixes, and logical-media Recently Watched (repeated movie → one card; TV episodes/seasons → one show-level card) with raw `watch_events` preserved. Manually accepted 2026-09-24.
- Persistence Write Coordination (mini-phase): one write-coordination contract for every writer to `library.db`; `FileOperationStore` and `SettingsRepository` now acquire `DatabaseWriteGate` like the catalog/personal/maintenance writers. Migration remains startup-exclusive. Verified 300/300.

## Completed locally; manual acceptance pending

- None. (Product Phase 3 acceptance is recorded above.)

## Latest verification

Fresh on 2026-09-24 from the Persistence Write Coordination source tree:

- Focused coordination/concurrency/recovery/file-operation run — **30 passed, 0 failed, 0 skipped**.
- `dotnet test tests\DropSort.Tests\DropSort.Tests.csproj` — **300 passed, 0 failed, 0 skipped** (was 296 at Phase 3 closure; +4 `WriteCoordinationTests`).
- `WriteCoordinationTests` prove: all writer kinds (catalog, personal, journal, settings) serialise under concurrency with no "database is locked"; a held catalog unit of work blocks a `FileOperationStore` write until released; the gate is released on the exception path; `CommitVerified` updates `media_files` under the gate without self-deadlock.
- Phase 3 acceptance evidence (unchanged): Visual Studio MSBuild rebuild + manual GUI acceptance against a real upgraded (drifted v8 → v9) database.
- Plain `dotnet build` of the WinUI project still fails with `MSB4062` (missing `Microsoft.Build.Packaging.Pri.Tasks.dll` on the .NET SDK path); no C# errors. Use Visual Studio MSBuild for the UI workflow — a confirmed tooling-path limitation, not a blocker.

## Next and remaining

Revised release direction (2026-09-24). Product identity is fixed: **Local-First Personal Media Library + Real File Intelligence + Safe File Management**. Major 1.0 feature expansion is frozen.

Ordered path to 1.0:

1. **Phase 3 — Closed.**
2. **Persistence Write Coordination — Done** (300/300; `FileOperationStore` + `SettingsRepository` now gated).
3. **Freeze 1.0 Scope** — the next step (not started).
4. Data / File Trust Pass.
5. Drive-Aware Availability Model.
6. Early Backup Protection.
7. Confirmed UX Corrective Pass.
8. Golden Journeys + Scale QA.
9. Release Candidate.
10. Installer Lifecycle QA.
11. Clean-Machine / Installed-App Verification.
12. 0.9.x Preview.
13. 1.0.

Do not begin Freeze 1.0 Scope or any later step without an explicit Goal. Detailed scope, the write-coordination contract, and preserved future product priorities are in `FUTURE_PLAN.md`.

## Current cautions

- Historical reports use “Phase 1–4” for the original rewrite foundation; current product phases are separately identified in `PHASES.md`.
- Some early reports describe demo UI or unavailable TMDB/TV behavior that later commits replaced. Use current code, `PROGRESS.md`, and `FUTURE_PLAN.md` for present-tense decisions.
- Some early reports say Clear Library preserves personal history. Current code deletes personal state/watch events during the explicit full clear while preserving media bytes and the operation journal; Phase 3 preserves history only across ordinary individual removal/unavailability.
