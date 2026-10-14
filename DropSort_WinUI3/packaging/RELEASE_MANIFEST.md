# DropSort 0.10.0-rc.2 — Windows x64 Pre-release

- Version: `0.10.0-rc.2`; numeric assembly/file/installer version: `0.10.0.0`.
- Branch: `codex`. The user authorized a new tag and GitHub pre-release on 2026-10-14. Never target `main` or overwrite prior tags/releases.
- Exact source commit, payload inventory, SHA256 values, packaging commands, and final runtime results are recorded in the artifact-side `BUILD_MANIFEST.json` and `VERIFICATION.md`.
- The older `v1.0.0` tag belongs to the frozen Python application. The active WinUI release line previously published `v0.9.0-preview`.

## Scope

- Folder Watcher and Smart Relink are permanently removed, including navigation, background monitors, configuration/persistence services, candidate matching, cache integration, and feature-specific tests.
- The original Check Library checks and manual file selection remain. Movie/TV Details, Media Files, watch history, custom lists, metadata, and posters remain.
- Add Media, Smart TV Batch Import, compact grouped review, and the clickable native Add Media Folder Picker remain.
- TMDB movie reuse attaches distinct editions to the existing movie identity and rejects repeated registration of the same path.
- Schema v15 is unchanged. No migration or deletion of installed-profile watcher/verification JSON is introduced.

## Verification and packaging

- Full Debug .NET suite: **737 passed, 0 failed, 0 skipped** on 2026-10-14.
- Focused import, availability, reconciliation, and UI tests: **125 passed, 0 failed, 0 skipped**.
- Independent Luna review completed. Two dead namespace imports were corrected and the correction was reviewed. No outstanding actionable findings were reported.
- Final release verification must use a new export of the exact source commit with no inherited bin/obj output, followed by Visual Studio 18 MSBuild Rebuild: Release, x64, win-x64, SelfContained=true, WindowsAppSDKSelfContained=true.
- Package the Release build output, not the nested publish directory. Preserve PRI/XBF, .NET/Windows App SDK runtime, and SQLite dependencies.
- Inno Setup 6 retains the established per-user AppId. Its PayloadDir override allows packaging the exact fresh portable payload.
- PDBs, nested publish output, databases, credentials, logs, and QA/test files are excluded.
- New artifacts live only in `artifacts/release/0.10.0-rc.2/`: installer, portable ZIP, portable directory, release notes, checksums, and build/verification manifests.
- Accepted `0.10.0-rc.1` artifacts remain unchanged. Test and build evidence is stored under `artifacts/qa/remove-features-rc2-20261014/`.

## Limits

Unsigned pre-release; no stable-release or signing claim. Clean-machine/VM testing, broader Windows hardware, physical SMB/removable-drive scenarios, and real video decoding remain unexecuted. Existing deferred roadmap issues remain open. Automated QA uses isolated databases and synthetic media; production data and real media are not modified.
