# DropSort WinUI 3 — Current Progress

Last updated: 2026-09-24

## Current checkpoint

- Branch: `codex`
- Product Phase 3 is **manually accepted and closed** locally. The reliability-closure implementation commit is `15c676d`; the governance/docs update follows it as a separate local commit. No network fetch or push was performed, so current server state is not asserted and the Phase 3 checkpoint is **not claimed as pushed**. `main` is untouched.
- Python V1 is a Frozen Reference. Active product work is in `DropSort_WinUI3`.

## Completed and accepted

- Rewrite foundation: Domain, Application, Infrastructure, FileSystem, WinUI shell, SQLite migrations 0001–0007, functional wiring, TV hierarchy, filesystem/recovery hardening, relink, TMDB provider, poster cache, and manual metadata matching.
- Product Phase 1 — Add Media: completed through selective mixed Movie/TV import, truthful pre-add/post-add metadata states, local-registration-first failure handling, and exact existing-media detection. Corrective commits are recorded in `PHASES.md`.
- Product Phase 2 — TMDB Search & Matching Quality: completed at `b158f9d` and manually accepted per the project checkpoint instruction.
- Product Phase 3 — Home Artwork + Real Watch History (incl. reliability closure): real Movie/TV watch events, episode watched state, Continue Watching progression, cached Home artwork, durable watch history, and — from the closure pass — migration v9 drift reconciliation, `DatabaseWriteGate` write coordination, Clear History / `DeleteWatchEvent` fixes, and logical-media Recently Watched (repeated movie → one card; TV episodes/seasons → one show-level card) with raw `watch_events` preserved. Manually accepted 2026-09-24.

## Completed locally; manual acceptance pending

- None. (Product Phase 3 acceptance is recorded above.)

## Latest verification

Fresh on 2026-09-24 from the Phase 3 closure source tree:

- Focused watch-history/migration/concurrency/UI-contract run — **25 passed, 0 failed, 0 skipped**.
- `dotnet test tests\DropSort.Tests\DropSort.Tests.csproj` — **296 passed, 0 failed, 0 skipped** (accepted closure baseline).
- Visual Studio MSBuild rebuild passed and the freshly built WinUI app passed **manual GUI acceptance** against a real upgraded (drifted v8 → v9) library database.
- Manual acceptance confirmed: Movie Mark Watched on the upgraded DB; migration v9 reconciles the drifted v8 `watch_events` schema; repeated movie watches stay as individual history events; Recently Watched shows a repeated movie once (latest event); TV episodes/seasons of one show collapse to a single show-level card using show identity/title/artwork and the latest watched season/episode; raw movie/episode history intact.
- Plain `dotnet build` of the WinUI project still fails with `MSB4062` because the .NET SDK path does not contain `Microsoft.Build.Packaging.Pri.Tasks.dll`. Use Visual Studio MSBuild for the solution/UI workflow; this is a confirmed tooling-path limitation, not a source warning. It is not a blocker because the accepted build was produced with Visual Studio MSBuild.

These checks and the owner's manual acceptance close Product Phase 3.

## Next and remaining

Revised release direction (2026-09-24). Product identity is fixed: **Local-First Personal Media Library + Real File Intelligence + Safe File Management**. Major 1.0 feature expansion is frozen.

Ordered path to 1.0:

1. **Phase 3 — Closed.**
2. **Persistence Write Coordination** — the next implementation mini-phase (recorded only; not started). Audit every writer to `library.db` and establish one write-coordination contract. Known gap: `FileOperationStore` writes `library.db` outside `DatabaseWriteGate`.
3. Freeze 1.0 Scope.
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

Do not begin Persistence Write Coordination or any later phase without an explicit Goal. Detailed scope, non-goals, and the preserved future product priorities are in `FUTURE_PLAN.md`.

## Current cautions

- Historical reports use “Phase 1–4” for the original rewrite foundation; current product phases are separately identified in `PHASES.md`.
- Some early reports describe demo UI or unavailable TMDB/TV behavior that later commits replaced. Use current code, `PROGRESS.md`, and `FUTURE_PLAN.md` for present-tense decisions.
- Some early reports say Clear Library preserves personal history. Current code deletes personal state/watch events during the explicit full clear while preserving media bytes and the operation journal; Phase 3 preserves history only across ordinary individual removal/unavailability.
