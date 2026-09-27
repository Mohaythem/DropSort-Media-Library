# DropSort — Release Manifest

## Product
- **Product:** DropSort
- **Version:** `0.9.0-preview` (assembly/file version `0.9.0.0`)
- **Why preview:** no authoritative public 1.0 version exists and roadmap risks remain open (see below); producing an installer does not declare 1.0 readiness.
- **Publisher/Company:** `DropSort` (project name; no authoritative legal publisher metadata exists).

## Artifact
- **Filename:** `DropSortSetup.exe`
- **Path:** `artifacts/release/0.9.0-preview/DropSortSetup.exe` (gitignored; not committed)
- **Size:** 34,718,349 bytes (~33.11 MiB)
- **SHA-256:** `D14B1F98A024B59A25D7D7506E580A48E46BB302FA695C96CE62F451F92CA430`
- **Build date:** 2026-09-27 (rebuilt from the current accepted `codex` state at commit c36fac9 — no code change; clean Release Restore→Build, installer rebuilt and locally install/launch verified. Earlier preview builds are superseded.)
- **Signing:** **Unsigned.** No code-signing credentials were available. The installer is not signed and is not claimed to be signed; Windows SmartScreen reputation / Authenticode signing is a remaining release concern.

## Build
- **Configuration:** Release
- **Platform / RID:** x64 / `win-x64`
- **Self-contained:** yes (.NET runtime + Windows App Runtime included; unpackaged, `WindowsPackageType=None`)
- **Payload:** the self-contained Release **build** output (`src/DropSort.UI/bin/x64/Release/net10.0-windows10.0.22621/win-x64`), excluding `*.pdb` and the `publish/` subfolder.
  - Note: the WinUI `Publish` target drops the compiled resource index (`DropSort.UI.pri`) and XAML binaries (`*.xbf`), so a published build exits at startup. The self-contained build output contains PRI + XBF + runtimes and launches correctly, so it is the packaged payload.
- **Clean build (supported VS MSBuild), first pass:** Restore then Build as separate commands — succeeded on the first Build pass. Raw Release EXE verified launching before packaging.
- **Installer technology:** Inno Setup 6 (ISCC command-line). Rationale: single polished per-user Setup EXE, no admin, Start-menu + Add/Remove Programs entries, custom icon, reproducible from CLI — the right fit for an unpackaged self-contained WinUI 3 desktop app.

## Source
- **Branch:** `codex` (main untouched; not pushed)
- **Commits:**
  - `b666fef` feat — corrective-pass implementation + Unicode duplicate-name fix + tests
  - `bbcfd03` docs — corrective-pass governance
  - `f9bb96f` build — installer, icon, release metadata
  - (this manifest commit follows)
- **Schema version:** 13
- **Automated tests:** 381 / 381 passing

## Install model
- Per-user install to `%LOCALAPPDATA%\Programs\DropSort` (no elevation).
- User library data lives in `%LOCALAPPDATA%\DropSort` — outside the install directory, so install/uninstall never touch the database, settings, posters, backups, logs, or any media.

## Installer lifecycle checks performed (this machine)
- **Fresh install:** silent install exit 0; files present (254, 0 PDBs; `DropSort.UI.pri` + `App.xbf` present); Start-menu shortcut created targeting the installed EXE; installed app launches and **stays running** from `%LOCALAPPDATA%\Programs\DropSort` (self-contained; no source tree needed).
- **Existing-data install:** installing with a populated `%LOCALAPPDATA%\DropSort` left `library.db`, `dropsort.db` and a preservation marker intact.
- **Uninstall:** install directory emptied; Start-menu shortcut removed; user data preserved.
- **Reinstall:** succeeds; app launches; preserved data still present.
- **Upgrade:** no earlier released installer artifact exists, so a true prior-version upgrade was not tested. In-place reinstall over an existing install (same `AppId`) was verified and leaves a single uninstall entry; a real cross-version upgrade test is deferred.
- **User media:** never referenced by install or uninstall; untouched.

## Codex review status
The adversarial Codex review was **partial**: Codex hit its external usage limit mid-run and returned only two candidate invariants. Both were independently reconciled — the Unicode duplicate-name folding gap was accepted and fixed; "Date added" sort by catalog id was deferred as a documented monotonic proxy — and an independent A–G review found no ship blocker. This was not a completed full Codex pass.

## Known deferred release risks (still open — not closed by this artifact)
- Crash-safe journaled restore + user-facing restore workflow.
- Backup ACL / privacy hardening (backups are a full copy in a per-user path).
- UNC / network-path timeout behaviour.
- Real removable-drive runtime verification.
- Home / Recently Watched stale availability hints.
- Large-library scale / performance verification.
- Clean-machine / installed-app verification on a machine without the dev toolchain.
- Code signing (installer is unsigned).
