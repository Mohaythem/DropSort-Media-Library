# DropSort — Final V1 Preview Release Manifest

## Product

- **Product:** DropSort
- **Version:** `0.9.0-preview` (assembly/file version `0.9.0.0`).
- **Release:** Final artifact for the V1 preview cycle. GitHub **Pre-release**, not stable 1.0 or a claim that deferred gates are closed.
- **Publisher/Company:** `DropSort` (project name; no authoritative legal publisher metadata exists).

## Artifact

- **Filename:** `DropSortSetup.exe`
- **Path:** `artifacts/release/0.9.0-preview/DropSortSetup.exe` (gitignored; installer binary is not committed).
- **Size:** 49,249,573 bytes.
- **SHA-256:** `C1370B1F10285F33FFFC9875987A5D1CA620D1A481F562043C1C07F79FD5592A`
- **Build date/time:** `2026-10-07T06:35:39+03:00` (Africa/Cairo); `2026-10-07T03:35:39Z` (UTC).
- **Supersedes:** The 2026-10-02 installer, size 34,725,671 bytes, SHA-256 `A28C3DBA28050A10B4615F005A76D3433275BAF585A3419A729320C3CCB90D72`. That earlier artifact lacked subsequent UNC/network and database-hardening source changes.
- **Signing:** **Unsigned.** Code signing / SmartScreen reputation remains a limitation.

## Source and final automated baseline

- **Branch:** `codex`; `main` is unchanged.
- **Release tag:** `v0.9.0-preview`.
- **Released source commit:** `v0.9.0-preview^{commit}`. This immutable tag identifies the exact final V1 commit; its full SHA is recorded in the GitHub release notes. The tracked manifest uses the tag reference to avoid a self-referential commit hash.
- **Build provenance:** The final current V1 source was tested and built before the final commit. Source/test content was frozen and checked through commit creation; the tag must point to that committed source state. Informational versions omit the earlier HEAD suffix through `IncludeSourceRevisionInInformationalVersion=false`.
- **Schema version:** 14. Finalization introduced no schema or migration.
- **Included source:** Current TV missing-episode gaps, Move/Add-to-Series repair, original-filename inference, ASCII season/episode editing, provider async enrichment, matching/Review Match, media-folder management, custom lists, UNC/network guards, and existing database/migration hardening.
- **Final narrow safety fix:** Conflicting movie identity matches return `IdentityConflict` before writes. Both movies, file links, preferences/watchlist, custom lists, and history are preserved; a disposable SQLite regression compares all relevant tables.
- **Final full-suite command** (from `DropSort_WinUI3`, .NET SDK `10.0.400`):

  ```powershell
  & 'C:\Program Files\dotnet\dotnet.exe' test 'tests/DropSort.Tests/DropSort.Tests.csproj' --no-restore --nologo --verbosity minimal --logger 'trx;LogFileName=final-v1-green.trx' --results-directory 'artifacts/release/0.9.0-preview/qa/final-v1-20261007/tests'
  ```

- **Final result:** **562 passed, 0 failed, 0 skipped; 562 total**. Runner duration: **16 seconds**. TRX start: `2026-10-07T06:32:24.3311130+03:00`; finish: `2026-10-07T06:32:41.0001535+03:00`.
- **Initial finalization run:** 557 passed, 3 failed, 0 skipped. Three stale UI source-contract assertions were corrected; focused verification passed 39 tests. Matching/import/SQLite preservation focused verification passed 74 tests before the final full-suite rerun.
- **Evidence boundary:** Backend and UI source-contract tests do not prove interactive installed-UI or clean-machine behavior. Logs and disposable QA data are excluded from the commit/assets.

## Build and packaging

- **Toolchain:** Visual Studio 18 Community MSBuild, Release / x64 / `win-x64`; unpackaged WinUI 3 (`WindowsPackageType=None`).
- **Self-contained:** .NET (`SelfContained=true`) and Windows App SDK (`WindowsAppSDKSelfContained=true`). The earlier payload's bootstrap/projection DLLs alone did not prove a bundled native WinUI runtime.
- **Fresh payload:** `src/DropSort.UI/bin/x64/Release/net10.0-windows10.0.22621/win-x64`. Only the stale payload was moved to a gitignored build-output archive; no user data was cleared.
- **Supported build:**

  ```powershell
  & 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe' 'src/DropSort.UI/DropSort.UI.csproj' -t:Rebuild -p:Configuration=Release -p:Platform=x64 -p:RuntimeIdentifier=win-x64 -p:SelfContained=true -p:WindowsAppSDKSelfContained=true -p:IncludeSourceRevisionInInformationalVersion=false -restore
  ```

- **Build result:** Exit 0. Sandboxed restore could not reach NuGet; the same supported build with network access succeeded.
- **Raw payload:** .NET runtime, native `Microsoft.ui.xaml.dll`, WinUI/WinRT projections, PRI, 15 XBF files, and native/managed SQLite dependencies exist. The fresh EXE opened a main window, initialized isolated SQLite data, and closed gracefully with exit 0.
- **Installer:** Existing Inno Setup 6 `packaging/DropSort.iss`; compiler exit 0. Packages the fresh build output; excludes PDBs and nested `publish/`. Publish output is not the packaging source because earlier verification found missing PRI/XBF resources there.

## Install model and final installed-artifact smoke checks

- **Model:** Per-user `%LOCALAPPDATA%\Programs\DropSort`, no elevation. Data remains in `%LOCALAPPDATA%\DropSort`, outside binaries. The installer never references physical media.
- **Upgrade:** New preview installed over the existing preview using the unchanged AppId; exit 0. Exactly one DropSort uninstall entry remains.
- **Binary identity:** All **520 packaged payload files** match the fresh build by SHA-256.
- **Start Menu:** Exactly one DropSort shortcut, targeting the installed `DropSort.UI.exe`.
- **Launch/relaunch:** Both opened a main window using the same isolated `DROPSORT_TEST_DATA_ROOT`; first launch initialized SQLite, relaunch reopened that profile, and both closed gracefully with exit 0.
- **Real data:** All **91 files** in the real DropSort data folder retained exact lengths and SHA-256 during raw and installed verification. Databases were hashed read-only; QA never opened the real library.
- **Uninstall:** The user's live installation was not destructively uninstalled. Unchanged Inno configuration writes to `{app}` only, with no `[UninstallDelete]` or media paths. Final-artifact uninstall/clean-machine lifecycle verification is not claimed.

## Codex review status (historical)

The earlier partial Codex review applied to an earlier artifact/development state. It does **not** represent a completed adversarial Codex review of this final working tree or installer.

That earlier review was **partial**: Codex reached an external usage limit and returned two candidate invariants. The Unicode duplicate-name folding gap was accepted and fixed; "Date added" ordering by catalog id was deferred as a monotonic proxy. An independent A–G review at that earlier state found no ship blocker. This was not a completed full Codex pass.

## Finalization source review (limited)

Independent Astra source review identified the movie identity-collision catalog-state deletion risk. The explicitly approved narrow fix and SQLite regression received focused re-review with no remaining blocker in that fix. The uncertain duplicate-import Review Match edge case remains deferred below. This was a focused source review, **not an exhaustive adversarial review of the whole final tree or installer**.

## Known deferred release risks (still open)

Finalization ends the V1 preview cycle; it does not close these release-readiness gaps:

- Unsigned installer / code signing / SmartScreen reputation.
- True clean-machine verification without the developer toolchain or prior state; broader supported-Windows matrix verification.
- Interactive installed-build navigation, Arabic RTL, and theme visual verification.
- Real SMB disconnect/reconnect, permission-denied behavior, and interactive responsiveness. Automated guarded-UNC tests do not replace manual SMB checks; mapped network drive letters retain existing behavior. A native read can remain blocked after the bounded caller returns; UNC journals remain pending at startup.
- Real removable-drive runtime verification.
- Home / Recently Watched stale availability hints (**no verified resolution; explicitly open**).
- Large-library scale / performance validation.
- Crash-safe restore/recovery hardening, journaled restore, and user-facing restore workflow.
- Backup ACL/privacy hardening. Persistent migration failure can retain backups beyond the normal five-file limit; backup verification does not certify every schema definition. See `DATABASE_MIGRATION_HARDENING_REPORT.md` for remaining fixture/restore limits.
- Uncertain duplicate import: a review-required or sole-result fallback match may associate a new file with an existing movie without preserving a Review Match indicator. Deferred; the separate destructive matching path was fixed.

No V2 implementation was started. V2 planning and unrelated local changes are outside the final V1 commit.
