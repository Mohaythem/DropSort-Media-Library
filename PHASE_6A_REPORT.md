# DropSort Phase 6A Report

## Result

**Phase 6A - V1 Hardening, Missing-File Reconciliation, and Final Runtime Safety: GREEN**

This report covers Phase 6A only. No executable, installer, release archive, watcher, automatic
organization, delete workflow, TV library, or other Phase 6B/later feature was implemented.

## Goal and missing-file model

DropSort now reconciles catalog paths with real filesystem availability without deleting catalog
records. The existing persisted states were reused:

- `PRESENT`: the catalog path was confirmed as a regular, non-link/reparse file.
- `MISSING`: the path is absent, is no longer a regular file, or is an unsafe link/reparse entry.
- transient inspection failures remain application-level errors and preserve the last persisted
  status.

Movies, metadata, posters, associations, technical facts, last known paths, and operation history
remain in the catalog when physical media is missing. No migration was required.

## Reconciliation architecture

`NoFollowMediaFileInspector` is a provider-neutral read boundary under `library/availability`. It
walks catalog path components with `lstat`, rejects symlink/reparse traversal, and returns controlled
present/missing/error results. It does not crawl directories or search drives.

`ReconcileLibraryFiles` performs an explicit catalog-path check. It obtains a deterministic ID-ordered
catalog page, checks only those exact paths, and commits status changes in bounded batches of 100.
Completed earlier batches remain truthful if a later batch fails or the user cancels; unchecked rows
are never changed. Progress reports exact total/checked/present/missing/error/change counts.

Checks occur only through the explicit **Check Library Files** action in Library/Recently Added.
Startup and grid repaint do not stat every file. Play/Open Folder retain their own click-time
validation; Organize is disabled for a persisted missing row.

## Movie Details and library presentation

- Missing media rows remain visible and display **Missing** plus **Last known location**.
- A subtle card indicator reports when all files, or only some copies, are missing.
- Recently Added remains catalog-history based; missing movies are not removed.
- The explicit library check runs on the existing Qt task boundary, has determinate progress and
  cooperative cancellation, ignores stale delivery, and refreshes the local catalog snapshot only
  after successful committed results.

## Relink architecture and policy

**Locate File** is visible only for persisted missing rows. The native file picker is followed by a
read-only **Relink Media File** preview with exact old/new paths, size, and validation state. Only
**Confirm Relink** changes the catalog.

A candidate is accepted for preview only when it is:

- an absolute, supported, regular, non-link/reparse file;
- not owned by another catalog row under the established Windows case-folded `path_key`;
- exactly the known file size and the same known extension;
- an exact normalized match to the catalog title or provider-neutral original title;
- free of a known year, resolution, codec, or source conflict.

The old catalog path must still be confirmed missing. If it has returned, or cannot be safely
inspected, relink is blocked to preserve both possible files. TMDB and external matching are never
called.

Preview records the candidate's no-follow `(size, mtime_ns, ctime_ns, dev, ino)` identity.
Confirmation is serialized and revalidates the old path, candidate identity/type, catalog
association/status, and destination ownership. Tokens are one-shot and bounded; double-click,
replay, stale preview, rapid same-size replacement, catalog race, and competing confirmation are
blocked. The same `MediaFile` ID/movie association/technical facts are preserved while only
`current_path`, `path_key`, `status`, and `last_seen_at` are transactionally corrected.

Relink writes no `file_operations` row and never moves, renames, copies, deletes, or overwrites media.

## Startup and runtime hardening

Runtime locations are centralized and CWD-independent:

```text
%LOCALAPPDATA%\DropSort\dropsort.db
%LOCALAPPDATA%\DropSort\poster-cache\
%LOCALAPPDATA%\DropSort\logs\dropsort.log
```

If `LOCALAPPDATA` is unavailable, DropSort uses the current user's
`AppData\Local\DropSort` path, never the process working directory. Clean first run creates required
directories and runs migrations. Database startup failure shows a controlled message, logs the
technical failure, and preserves the existing path rather than replacing it.

Logging uses a bounded 1 MiB rotating file with three backups and redacts Authorization/TMDB token
patterns. Session-only TMDB credentials and environment fallback are unchanged. Font and migration
resources resolve from package files, not CWD; the Margarine font and OFL license remain package
data. Poster-cache behavior remains the established bounded/atomic Phase 4C design.

## Files created

- `src/dropsort/library/availability/__init__.py`
- `src/dropsort/library/availability/models.py`
- `src/dropsort/library/availability/inspector.py`
- `src/dropsort/application/runtime/__init__.py`
- `src/dropsort/application/runtime/paths.py`
- `src/dropsort/application/runtime/logging.py`
- `src/dropsort/application/dto/reconciliation.py`
- `src/dropsort/application/use_cases/reconcile_library_files.py`
- `src/dropsort/application/use_cases/relink_media_file.py`
- `src/dropsort/ui/reconciliation/__init__.py`
- `src/dropsort/ui/reconciliation/dialogs.py`
- `tests/unit/library/test_media_availability.py`
- `tests/unit/application/test_reconciliation_dto.py`
- `tests/unit/application/test_reconciliation_architecture.py`
- `tests/unit/application/test_runtime_hardening.py`
- `tests/integration/database/test_media_file_reconciliation_repository.py`
- `tests/integration/application/test_library_reconciliation.py`
- `tests/integration/application/test_large_library_reconciliation.py`
- `tests/integration/application/test_media_relink.py`
- `tests/unit/ui/test_reconciliation_dialogs.py`
- `PHASE_6A_REPORT.md`

## Files modified

- `src/dropsort/application/bootstrap/__init__.py`
- `src/dropsort/application/bootstrap/desktop.py`
- `src/dropsort/application/errors.py`
- `src/dropsort/application/use_cases/__init__.py`
- `src/dropsort/application/use_cases/_library_mapping.py`
- `src/dropsort/application/dto/library.py`
- `src/dropsort/database/repositories/library_queries.py`
- `src/dropsort/database/repositories/media_files.py`
- `src/dropsort/library/movies/__init__.py`
- `src/dropsort/library/movies/models.py`
- `src/dropsort/library/movies/queries.py`
- `src/dropsort/library/movies/repositories.py`
- `src/dropsort/ui/common/theme.py`
- `src/dropsort/ui/contracts.py`
- `src/dropsort/ui/library/library_view.py`
- `src/dropsort/ui/library/movie_card.py`
- `src/dropsort/ui/main_window/window.py`
- `src/dropsort/ui/movie_details/details_view.py`
- `tests/integration/database/test_library_read_repository.py`
- `tests/unit/ui/conftest.py`
- `tests/unit/ui/test_desktop_bootstrap.py`
- `tests/unit/ui/test_main_window.py`
- `tests/unit/ui/test_movie_card.py`
- `tests/unit/ui/test_movie_details_view.py`
- `README.md`
- `PROJECT_STATUS.md`

## Database migration and dependencies

```text
Database migration: none
Dependencies changed: none
```

The existing `MISSING` status, `path_key`, and catalog fields were sufficient.

## Tests and performance

```text
Pre-change baseline:       834 passed, 5 skipped, 0 failed
Phase 6A focused gate:     101 passed, 0 failed
Final full/coverage suite: 901 passed, 5 skipped, 0 failed
Total branch coverage:     95%
Reconciliation use case:   98%
Relink use case:           97%
Reconciliation UI:         98%
Availability inspector:    96%
```

The five skips are the established Windows symlink-creation privilege limitations; deterministic
link/reparse tests remain active.

The 1,000-row synthetic catalog used 800 present, 150 missing, and 50 controlled inspection-error
results. It completed with deterministic monotonic progress, bounded 100-row database pages/batches,
150 truthful status changes, 50 preserved statuses, no duplicate rows, zero journal rows, and no
media mutation. No GB-scale fixtures or full-drive searches were used.

## Review findings and fixes

- Shared module-global progress state could cross-contaminate concurrent checks; progress delivery is
  now session-local.
- A persisted missing row could have its old path return before Relink; preview and confirmation now
  require the old path to remain confirmed missing, preserving both files when ambiguous.
- Rapid same-size replacement could retain size/mtime/device/inode on NTFS; candidate identity now
  includes no-follow `ctime_ns` and confirmation detects the replacement.
- Preview and consumed-token registries could grow indefinitely; both are bounded and one-shot.
- Closed check/relink dialogs could receive late results; close invalidates tokens, cancels checks,
  and discards unused previews.
- UI wording accidentally tripped the existing SQL/filesystem architecture gates; wording was made
  dependency-neutral while the gates remain unchanged.
- Production DB fallback used process CWD when `LOCALAPPDATA` was absent; it now uses the user-profile
  Local AppData path.
- Runtime failures lacked bounded local diagnostics; rotating redacted application logging and a
  controlled database-startup failure path were added.

No unresolved BLOCKER or CRITICAL finding remains.

## Manual native-Windows verification

A repository-local disposable catalog and tiny `.mkv` fixtures were used. Test setup externally
renamed `Prisoners.2013.1080p.BluRay.mkv` to `Prisoners (2013).mkv` before DropSort acted.

Observed through the real Qt desktop/application boundaries:

- Library Check reported `3 / 3`, `Present: 0`, `Missing: 3`, `Errors: 0`.
- Movie Details displayed Missing and the exact last known path.
- Relink preview displayed exact old/new paths.
- Explicit confirmation preserved the same media-file ID, movie ID, technical metadata, and candidate
  SHA-256; status became PRESENT at the new path.
- A different-size candidate was blocked.
- A candidate already owned by another catalog row was blocked.
- Blocked catalog rows remained unchanged.
- Relink created zero file-operation journal rows and did not mutate candidate bytes.

All disposable smoke/test/coverage artifacts were removed and their absence verified.

## Release-readiness audit

Runtime correctness is ready for packaging work: active production source has no developer-specific
or fixture paths; database/cache/log paths are user-scoped; clean first run and CWD independence are
tested; font/OFL resources are packaged; credential storage remains non-persistent; logging is
bounded/redacted; migrations pass from empty/current schemas; and the final regression gate is green.

Packaging-specific resource collection, formal TMDB attribution placement, executable/installer
creation, and clean-machine/profile verification remain for Phase 6B.

## Known Phase 6A limitations

- Reconciliation is explicit, not automatic at startup and not a whole-drive search.
- Cancellation is cooperative between path inspections/batches; one in-progress OS stat is not
  forcibly interrupted.
- A different-size, different-extension, weak-title, or conflicting-technical candidate is blocked;
  there is no advanced Replace Media File flow.
- Relink uses lightweight filesystem/filename evidence plus explicit confirmation, not full-film
  hashing or TMDB lookup.
- There is no catalog audit-history table for Relink; it intentionally does not overload the file
  mutation journal.
- Secure persistent TMDB credential storage remains unimplemented.

## Recommended next phase

**Phase 6B - Windows Release Packaging + Clean-Machine Final Verification.**
