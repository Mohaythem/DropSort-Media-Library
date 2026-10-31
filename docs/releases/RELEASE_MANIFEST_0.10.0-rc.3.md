# DropSort 0.10.0-rc.3 — Local Windows x64 installer

- Source commit: `52ed8914b69e044c5242e18ef5cc02b16656a78d`, branch `codex`.
- Version: `0.10.0-rc.3`. Application/installer file version: `0.10.0.3`. Assembly version: `0.10.0.0`.
- The payload comes from a new Git archive of that commit. No prior bin/obj output is reused.
- Visual Studio 18 MSBuild: `/restore /t:Rebuild /p:Configuration=Release /p:Platform=x64 /p:RuntimeIdentifier=win-x64 /p:SelfContained=true /p:WindowsAppSDKSelfContained=true /m:1`.
- Release Rebuild succeeded with 0 errors and 6 NU1900 warnings: NuGet vulnerability metadata was unavailable. This does not establish dependency security status.
- Independent Luna source review completed without a material finding. Automated tests, UI/runtime verification, and installed-app acceptance were not run for this candidate, by user instruction. Earlier test totals are historical.
- Inno Setup uses the established per-user AppId and packages the build output, including PRI/XBF, .NET/WinUI, and SQLite. PDBs and the publish subtree are excluded.
- The installer creates a Start menu entry and offers a desktop shortcut selected by default. It does not install or remove personal library data or media.
- Artifacts: `artifacts/release/0.10.0-rc.3/DropSortSetup.exe` and `DropSort-0.10.0-rc.3-win-x64.zip`. Exact payload hashes and SHA256 checksums accompany these files in `BUILD_MANIFEST.json` and `SHA256SUMS.txt`.
- Schema v16/v17 are forward-only additions with existing pre-migration backup protection. An older rc.2 application cannot open the upgraded schema; do not use it after migrating a profile.
- Previous artifacts and tags remain unchanged. This task pushes source to `origin/codex` only; it does not create a tag or GitHub release.
- Unsigned release candidate. Clean-machine installation, code signing, and current automated/runtime verification remain unverified.
