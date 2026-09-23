# DropSort WinUI 3 — Current Progress

Last updated: 2026-09-23

## Current checkpoint

- Branch: `codex`
- The committed baseline before Product Phase 3 was `f59ce9c5647e08235076f0495990b332cfe13c6d`. Phase 3 implementation and this record are delivered in one local commit whose hash is reported in the handoff rather than self-referenced here.
- The locally stored `origin/codex` tracking ref remains `9a635e09dcb7e170ba9aa65cbecba413d9d479e8`. No network fetch or push was performed, so current server state is not asserted and the Phase 3 checkpoint is **not claimed as pushed**.
- Python V1 is a Frozen Reference. Active product work is in `DropSort_WinUI3`.

## Completed and accepted

- Rewrite foundation: Domain, Application, Infrastructure, FileSystem, WinUI shell, SQLite migrations 0001–0007, functional wiring, TV hierarchy, filesystem/recovery hardening, relink, TMDB provider, poster cache, and manual metadata matching.
- Product Phase 1 — Add Media: completed through selective mixed Movie/TV import, truthful pre-add/post-add metadata states, local-registration-first failure handling, and exact existing-media detection. Corrective commits are recorded in `PHASES.md`.
- Product Phase 2 — TMDB Search & Matching Quality: completed at `b158f9d` and manually accepted per the project checkpoint instruction.

## Completed locally; manual acceptance pending

- Product Phase 3 — Home Artwork + Real Watch History: real Movie/TV watch events, episode watched state, Continue Watching progression, cached Home artwork, durable Recently Watched snapshots, and graceful unavailable-history handling are implemented through additive migration 0008. Clear Library Data semantics were not changed.

## Latest verification

Fresh on 2026-09-23 from the final Phase 3 source tree:

- Focused migration/history/UI contract run — **44 passed, 0 failed, 0 skipped**.
- `dotnet test tests\DropSort.Tests\DropSort.Tests.csproj --no-restore --logger "console;verbosity=minimal"` — **279 passed, 0 failed, 0 skipped**.
- Visual Studio 18 MSBuild solution rebuild, `Debug|Any CPU` — passed with no warnings shown.
- Visual Studio 18 MSBuild WinUI rebuild, `Debug|x64` — passed with no warnings shown.
- The runnable x64 smoke artifact was rebuilt with `RuntimeIdentifier=win-x64`, `SelfContained=true`, and `WindowsAppSDKSelfContained=true`; it launched successfully against a new isolated data root.
- Runtime journey passed: mark TV episode watched from the real details UI, observe Continue Watching advance to the next episode, restart and retain history, remove the watched episode, retain its title/artwork snapshots, render the cached poster fallback, expose `Unavailable`, and show the safe unavailable dialog.
- Plain `dotnet build` of the WinUI project still fails with `MSB4062` because the .NET SDK path does not contain `Microsoft.Build.Packaging.Pri.Tasks.dll`. Use Visual Studio 18 MSBuild for the solution/UI workflow; this is a confirmed tooling-path limitation, not a source warning to suppress.

These checks validate the local Phase 3 checkpoint. They do not replace the owner's manual acceptance pass.

## Next and remaining

1. Manually accept or reject the local **Phase 3 — Home Artwork + Real Watch History** checkpoint.
2. After explicit approval only, begin Phase 4 — End-to-End Stabilization.
3. Release Candidate.
4. Installer lifecycle QA.
5. Repository/security hygiene.
6. Installed-app verification independent of the development tree.
7. Final release preparation.

The detailed scope, non-goals, Definition of Done, and verification gates are in `FUTURE_PLAN.md`. Phase 4 has not begun.

## Current cautions

- Historical reports use “Phase 1–4” for the original rewrite foundation; current product phases are separately identified in `PHASES.md`.
- Some early reports describe demo UI or unavailable TMDB/TV behavior that later commits replaced. Use current code, `PROGRESS.md`, and `FUTURE_PLAN.md` for present-tense decisions.
- Some early reports say Clear Library preserves personal history. Current code deletes personal state/watch events during the explicit full clear while preserving media bytes and the operation journal; Phase 3 preserves history only across ordinary individual removal/unavailability.
