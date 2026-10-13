# DropSort 0.10.0-rc.1 — Local Release Candidate

## Release identity

- Latest published release: [0.9.0-preview](https://github.com/Mohaythem/DropSort-Media-Library/releases/tag/v0.9.0-preview), published 2026-10-07. Verified through the public GitHub releases API on 2026-10-13.
- The older `v1.0.0` tag belongs to the frozen Python application, not the current WinUI release line.
- Candidate version: `0.10.0-rc.1`; numeric assembly/file/installer version: `0.10.0.0`.
- New functionality increments the minor version of the active 0.x line. The RC suffix preserves the acceptance gate. This is not a stable 1.0 or a published release.
- Branch: `codex`. No new release tag or GitHub release is authorized.
- Exact final source commit, payload inventory, build/test logs, artifact SHA256 values, and runtime results are recorded in the artifact-side `BUILD_MANIFEST.json` and `VERIFICATION.md`. They are generated after the source checkpoint; this avoids a self-referential source hash.
- Historical V1 manifest remains available at `v0.9.0-preview:DropSort_WinUI3/packaging/RELEASE_MANIFEST.md`.

## Included behavior

- Accepted Movie Details redesign and file actions.
- Review-first Smart TV Batch Import: folder scan, series/season/episode analysis, corrections, exclusions, selection, explicit import confirmation, partial results and cancellation.
- Compact grouped review: selection checkbox, leading chevron, title/count, correction action at the opposite edge; aligned series/seasons and indented episodes; English and Arabic/RTL.
- Read-only scanning and pre-import validation. Existing TV catalog services and schema v15 are reused. This release work adds no migration.
- Settings displays the generated application version.

## Verification and packaging

- Full Release test suite: 734 passed, 0 failed, 0 skipped on 2026-10-13.
- Focused TV/import/UI suite: 152 passed, 0 failed, 0 skipped.
- Seven existing xUnit analyzer warnings: five xUnit1031 blocking-call warnings and two xUnit2013 collection-size assertion warnings. They are not suppressed.
- Required native build: Visual Studio 18 MSBuild, Rebuild, Release, x64, win-x64, SelfContained=true, WindowsAppSDKSelfContained=true.
- Package the fresh Release build output, not the nested publish directory. Verify .NET/Windows App SDK runtimes, SQLite dependencies, PRI and XBF resources.
- Existing Inno Setup 6 per-user installer retains the established AppId. Portable ZIP uses the same payload. PDBs, nested publish output, test databases, credentials, logs and QA files are excluded.
- Local outputs: `artifacts/release/0.10.0-rc.1/DropSortSetup.exe`, `DropSort-0.10.0-rc.1-win-x64.zip`, `portable/DropSort.UI.exe`, and `SHA256SUMS.txt`.
- Installation binaries remain separate from `%LOCALAPPDATA%\DropSort` library data. Automated runtime verification uses disposable QA profiles and synthetic media only.

## Acceptance limits

- Local candidate for manual acceptance; not production-ready certification.
- Unsigned binaries/installer and no SmartScreen reputation certification.
- True clean-machine/VM validation, broader Windows matrix, physical removable-drive and real SMB disconnect/reconnect verification remain unexecuted.
- Existing deferred performance, Home availability hints, restore/recovery, backup privacy and uncertain movie-match limitations remain open; see the historical manifest and forward plan. Successful tests do not close those gates.
- No media moving/renaming/deletion during acceptance preparation. A real-profile launch requires schema/integrity and pending-operation checks first.
- No binary upload, public release or version tag before user approval.
