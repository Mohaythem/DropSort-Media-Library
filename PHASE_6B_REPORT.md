# DropSort Phase 6B Report

## Result

**Phase 6B: GREEN — DropSort V1 is complete.**

The final Windows artifact is a portable PyInstaller one-directory distribution. It launches
without the source checkout or an activated virtual environment, initializes an isolated user-data
profile, contains the required runtime resources, and passes the source and packaged-core gates.

## Packaging strategy and why chosen

DropSort uses one packaging system: **PyInstaller 6.22.0**, in one-directory mode. This keeps Qt
plugins and native DLLs explicit, avoids one-file extraction complexity, and makes resource and
license auditing straightforward. No installer, updater, service, registry setup, or administrator
requirement was added.

PyInstaller was added only to the `package` optional dependency group as
`pyinstaller>=6.21,<7`. Runtime dependencies were not changed.

## Build configuration

- `DropSort.spec` freezes `src/dropsort/__main__.py` as a windowed `DropSort.exe`.
- `packaging/build_release.ps1` resolves every path from `$PSScriptRoot`, runs a clean build, and
  places the user README and third-party notices beside the executable.
- Build command:
  `powershell.exe -NoProfile -ExecutionPolicy Bypass -File packaging\build_release.ps1`
- PyInstaller warnings contain only conditional/non-Windows optional modules; no required DropSort
  or Qt module was missing in launch verification.

## Release artifact

- Path: `release\DropSort\`
- Type: portable one-directory Windows distribution
- Executable: `release\DropSort\DropSort.exe`
- Total size: **118,482,564 bytes (112.99 MiB)** across 184 files
- Executable size: 2,323,795 bytes
- Signature: unsigned (`NotSigned`)

## Bundled resources

The final artifact contains all six actual SQL migration files, PySide6/Shiboken6 and the Qt
Windows platform/image plugins, the offline Margarine font and OFL text, the approved TMDB logo,
README, third-party notices, and complete LGPL/GPL/Python/PyInstaller license texts. The packaged
migration filename set exactly matches the source migration filename set.

## Runtime paths

The packaged application continues to use:

- `%LOCALAPPDATA%\DropSort\dropsort.db`
- `%LOCALAPPDATA%\DropSort\poster-cache\`
- `%LOCALAPPDATA%\DropSort\logs\`

The executable directory remains read-only application content. Runtime path resolution is
independent of process CWD; no data migration or replacement behavior was added.

## TMDB attribution

The current official TMDB FAQ was checked on 2026-08-13. Settings > About & Credits contains the
approved TMDB logo, identifies TMDB as the metadata/poster source, and prominently displays:

> This product uses the TMDB API but is not endorsed or certified by TMDB.

The TMDB mark is less prominent than DropSort's identity. Metadata contracts and credential
behavior were not changed.

## Credential and secret audit

The source inputs and final artifact were scanned without printing secret values. The artifact has
zero matches for an assigned `DROPSORT_TMDB_READ_ACCESS_TOKEN` or bearer-token prefix, and contains
no `.env`, user database, cache, or log. A generic `Authorization:` label exists only inside the
standard `Qt6Network.dll` and `libcrypto-3.dll`; no credential value or bearer-token signature was
found. Credential priority remains in-memory session token, then
`DROPSORT_TMDB_READ_ACCESS_TOKEN`; plaintext persistence was not added.

## Dependency audit

- Runtime: PySide6 6.11.1 and Shiboken6 6.11.1, plus Python standard-library SQLite/HTTP support.
- Packaging only: PyInstaller 6.22.0. Its helper packages remain build-environment dependencies,
  not application runtime requirements.
- Test tooling is not bundled. No ORM, second HTTP client, installer framework, or updater was added.

## License audit

- Margarine OFL is bundled beside the font.
- LGPL 3, GPL 2, GPL 3, Python 3.12, and PyInstaller license texts are bundled.
- `THIRD_PARTY_NOTICES.md` records Qt/PySide6/Shiboken6, Python, SQLite, PyInstaller, Margarine, and
  TMDB considerations.
- DropSort itself has **no explicit project license**. This is documented as a public-distribution
  consideration; no license was invented.

## Normal-user privilege behavior

The portable release has no installer or service, embeds the default as-invoker manifest, and
launched in the current desktop session without a UAC request. It writes only to the normal user
AppData location. The executable is unsigned, so SmartScreen may warn on external distribution.

## First-run verification

A fresh repository-local `LOCALAPPDATA` sandbox with no database, cache, logs, credential,
`PYTHONPATH`, or `PYTHONHOME` was used. The packaged executable stayed running and created an
81,920-byte migrated database plus empty poster-cache and logs directories. The local library opened
without a TMDB credential. The real user profile was not touched.

## Existing-database and invalid-database verification

A disposable copy of a populated current database opened in the packaged UI and displayed its two
movies and linked files. Its SHA-256 and size were unchanged after the session, proving no silent
reset. A frozen packaged-core verifier also opened a copied valid database, confirmed migration/data
preservation, and confirmed that an invalid database is rejected while its bytes remain unchanged.

## CWD and repository independence

The packaged application was launched from unrelated working directories containing spaces with
`PYTHONPATH` and `PYTHONHOME` removed. Font, migrations, SQLite, logs, poster-cache, and Qt plugins
resolved correctly. The final artifact scan contains zero occurrences of the repository path or
developer profile. A standalone frozen verification executable exercised application modules
without an activated venv or source import path.

Verification level: **CLEAN-PROFILE APPROXIMATION**. No separate machine or VM was available, so no
claim of separate-machine testing is made.

## Packaged UI smoke

The packaged desktop UI was visually inspected on Windows: Library, Recently Added, Add Movies,
Operation History, Settings, TMDB attribution, Movie Details, and Check Library Files were present;
the DropSort theme/font/resources rendered and the window remained responsive. A final clean-build
launch repeated process/runtime initialization; the unavailable desktop-control connector prevented
a second screenshot, but the final rebuild changed only release documentation placement.

## Scan, cancel, and restart smoke

A frozen packaged-core verifier scanned disposable media, observed live progress, cancelled without
presenting partial completion, restarted successfully with six deterministic results, preserved all
file hashes, and created zero operation-journal rows. Unicode scan paths were included. No real media
was used.

## Play and Open Folder smoke

The frozen verifier used the real established Windows local-action boundary with controlled,
non-disruptive injected launchers. It verified exact argument tuples, regular-file validation, and
unchanged bytes/catalog/journal state. No `shell=True` path exists. An external player or Explorer
window was intentionally not launched during automation.

## Organize smoke

Using disposable same-volume media, preview caused zero mutation and zero journal creation. Explicit
confirmation produced a committed journal operation, preserved SHA-256, and advanced the catalog
path only after filesystem verification. Existing-destination and stale-authorization protections
remain covered by the complete suite.

## History and Undo smoke

The disposable organize operation was read through Operation History. Undo preview was read-only;
explicit confirmation restored the original path and hash, restored the catalog path, left the
original operation immutable, and created a new linked committed reverse journal operation.
History ordering remains explicit and deterministic:
`ORDER BY julianday(fo.created_at) DESC, fo.created_at DESC, fo.rowid DESC`.

## Missing and Relink smoke

The verifier externally moved disposable setup media, reconciled the catalog row to MISSING, then
confirmed Relink to the new path. The same media-file ID, movie association, size, extension, and
technical metadata were preserved; status became PRESENT; bytes were unchanged; and Relink added
zero journal rows. Wrong-size and catalog-owned candidate paths were blocked. Phase 6B also fixed a
native NTFS same-size-rewrite gap by binding previews to a stable SHA-256 fingerprint in addition to
no-follow identity; confirmation recomputes both before the catalog-only update.

## Recovery and offline behavior

Dangerous ambiguous recovery states were not manufactured manually. The complete automated suite
retains deterministic coverage and confirms both/neither/tampered states preserve files. With no
credential/network dependency, the packaged app opened and local catalog/cache behavior remained
available; unavailable metadata is controlled. Live TMDB metadata/poster download was **NOT
VERIFIED** in Phase 6B because no credential was available.

## Source test results and coverage

- Phase 6B packaging/attribution focused gate: 10 passed, 0 failed
- Final complete suite: **907 passed, 5 skipped, 0 failed**
- Total branch coverage: **95%**

The five skips are legitimate Windows symlink-creation privilege limitations. Phase 6B added six
tests over the Phase 6A baseline: packaging/resource/attribution contracts plus the Relink content
fingerprint regression.

## Adversarial findings and fixes

- **HIGH — Relink TOCTOU:** same-size content replacement could retain unchanged NTFS stat identity.
  Fixed with stable SHA-256 fingerprint capture/revalidation and a regression test.
- **HIGH — release license completeness:** notices alone did not provide all relevant full texts.
  Fixed by bundling LGPL/GPL/Python/PyInstaller texts plus the existing Margarine OFL.
- **MEDIUM — user documentation visibility:** PyInstaller data defaults to `_internal`.
  Fixed with a tested CWD-independent build entry point that places README/notices beside the EXE.
- **LOW — generic authorization label:** standard Qt/OpenSSL binaries contain the HTTP header label;
  targeted scans confirm no assigned environment token or bearer-token prefix.

No unresolved BLOCKER, CRITICAL, or release-relevant HIGH finding remains. Review reconfirmed
single-use stale Organize/Undo/Relink authorization, approved-root/collision checks, immutable Undo
history, ambiguous recovery preservation, scan/relink non-mutation, and Qt stale-callback shutdown
guards.

## Known V1 limitations

- No separate clean Windows machine/VM was available; verification used a clean-profile approximation.
- The executable is unsigned and may trigger SmartScreen.
- Live TMDB search/poster acquisition was not reverified in Phase 6B without a user credential.
- DropSort has no explicit project license.
- Cross-volume execution and destructive/ambiguous recovery remain automated-simulation verified,
  not manually exercised on real ambiguous files.
- There is no installer, updater, automatic/bulk organization, folder watcher, TV library, subtitle
  management, cloud sync, or V2 functionality.

## Release checklist

- [x] Clean artifact built and launchable
- [x] Required runtime resources and licenses bundled
- [x] TMDB attribution handled
- [x] Secret, developer-path, and artifact-cleanliness scans clean
- [x] Disposable first-run and copied-database verification passed
- [x] CWD/source independence passed
- [x] Packaged UI and frozen packaged-core V1 smokes passed
- [x] Source suite has zero failures and 95% branch coverage
- [x] No unresolved release-blocking review finding
- [ ] Separate clean machine/VM (not available)
- [ ] Code signing (no certificate supplied)
- [ ] Live TMDB request in this phase (no credential supplied)
