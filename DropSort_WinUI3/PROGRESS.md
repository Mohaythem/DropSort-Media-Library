# DropSort WinUI 3 — Current Progress

Last updated: 2026-09-22

## Current checkpoint

- Branch: `codex`
- Product-code checkpoint: `b158f9daa8660f77dce04529e0de31893001f7d6`. This governance checkpoint changes documentation only.
- At the pre-documentation inspection, the locally stored `origin/codex` tracking ref was `9a635e09dcb7e170ba9aa65cbecba413d9d479e8` and the product-code checkpoint was six commits ahead. No network fetch was performed, so current server state is not asserted and those commits are **not claimed as pushed**. The documentation commit created from this file is reported in the checkpoint handoff rather than self-referenced here.
- Python V1 is a Frozen Reference. Active product work is in `DropSort_WinUI3`.

## Completed and accepted

- Rewrite foundation: Domain, Application, Infrastructure, FileSystem, WinUI shell, SQLite migrations 0001–0007, functional wiring, TV hierarchy, filesystem/recovery hardening, relink, TMDB provider, poster cache, and manual metadata matching.
- Product Phase 1 — Add Media: completed through selective mixed Movie/TV import, truthful pre-add/post-add metadata states, local-registration-first failure handling, and exact existing-media detection. Corrective commits are recorded in `PHASES.md`.
- Product Phase 2 — TMDB Search & Matching Quality: completed at `b158f9d` and manually accepted per the project checkpoint instruction.

## Latest verification

Fresh on 2026-09-22 at the checkpoint above:

- `dotnet test tests\DropSort.Tests\DropSort.Tests.csproj --no-restore --nologo --verbosity minimal` — **270 passed, 0 failed, 0 skipped**.
- Visual Studio MSBuild solution build, `Debug|Any CPU` — passed.
- Visual Studio MSBuild WinUI build, `Debug|x64` — passed.
- Plain `dotnet build` of the WinUI project still fails with `MSB4062` because the .NET SDK path does not contain `Microsoft.Build.Packaging.Pri.Tasks.dll`. Use Visual Studio 18 MSBuild for the solution/UI workflow; this is a confirmed tooling-path limitation, not a source warning to suppress.

These checks validate the current code/build baseline; this documentation-only checkpoint does not substitute for a fresh runtime acceptance pass after product changes.

## Next and remaining

1. **Phase 3 — Home Artwork + Real Watch History** is next. It must use real persisted Movie/TV watch events and artwork, preserve history across individual media removal/unavailability, and handle unavailable playback safely without changing explicit Clear Library Data semantics.
2. End-to-End Stabilization.
3. Release Candidate.
4. Installer lifecycle QA.
5. Repository/security hygiene.
6. Installed-app verification independent of the development tree.
7. Final release preparation.

The detailed scope, non-goals, Definition of Done, and verification gates are in `FUTURE_PLAN.md`. No Phase 3 implementation has begun in this checkpoint.

## Current cautions

- Historical reports use “Phase 1–4” for the original rewrite foundation; current product phases are separately identified in `PHASES.md`.
- Some early reports describe demo UI or unavailable TMDB/TV behavior that later commits replaced. Use current code, `PROGRESS.md`, and `FUTURE_PLAN.md` for present-tense decisions.
- Some early reports say Clear Library preserves personal history. Current code deletes personal state/watch events during the explicit full clear while preserving media bytes and the operation journal; Phase 3 must preserve history only across ordinary individual removal/unavailability unless a later Goal explicitly changes full-clear semantics.
