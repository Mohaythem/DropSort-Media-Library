# DropSort Phase 5A Report

Phase 5A is GREEN on native Windows (Python 3.12 / NTFS).

## Implemented

Phase 5A adds a per-physical-file **Organize File** workflow to Movie Details. The user chooses one
existing destination folder, may retain or edit the filename, reviews the exact `FROM` and `TO`
paths, operation type, file size, volumes, transfer class, and validation state, then explicitly
confirms one Move, Rename, or Move & Rename operation.

Preview is read-only: it creates no journal record, performs no filesystem mutation, and does not
change the catalog. Confirmation consumes a one-time preview token, serializes execution, and
revalidates the source identity, catalog path, destination availability, path policy, and catalog
path ownership immediately before the existing Phase 1 journaled operation pipeline is invoked.

The destination policy is intentionally manual and narrow. The approved roots for one operation are
the verified source parent and the explicitly selected destination root. Filenames must preserve the
media extension and reject separators, traversal, Windows-invalid characters, reserved device names,
trailing dots/spaces, and excessive length. No destination is inferred from metadata, and no
automatic or bulk organization exists.

Same-volume work uses the established exclusive hardlink/unlink transfer. Cross-volume work reuses
the existing bounded copy, flush/fsync, SHA-256 verification, exclusive finalization, and recovery
pipeline. The authoritative catalog path changes only after filesystem verification. A failure after
filesystem success remains journaled/recoverable; ambiguous states preserve files.

## Application and UI boundary

- `OrganizationPreview` and `OrganizationResult` are immutable application DTOs.
- `OrganizeMediaFile` owns preview preparation, one-time authorization, conflict checks, and
  controlled error translation. It does not duplicate the File Engine.
- `OrganizationUiActions` keeps Movie Details independent from concrete SQLite/File Engine classes.
- `OrganizeFileDialog` performs no SQL, HTTP, or direct media mutation. It uses the existing retained
  Qt task runner, rejects stale results, disables repeat confirmation, and refuses unsafe close while
  execution is active.
- Successful completion refreshes the local library/details snapshot. The movie association,
  physical-file row identity, size, status, and technical metadata are preserved.

## Files created

- `src/dropsort/application/dto/organization.py`
- `src/dropsort/application/use_cases/organize_media_file.py`
- `src/dropsort/ui/organization/__init__.py`
- `src/dropsort/ui/organization/dialog.py`
- `tests/unit/application/test_organization_dto.py`
- `tests/unit/application/test_organization_architecture.py`
- `tests/integration/application/test_organization_flow.py`
- `tests/unit/ui/test_organization_dialog.py`
- `PHASE_5A_REPORT.md`

## Files modified

- `src/dropsort/application/errors.py`
- `src/dropsort/application/dto/__init__.py`
- `src/dropsort/application/use_cases/__init__.py`
- `src/dropsort/application/bootstrap/desktop.py`
- `src/dropsort/core/operations/service.py`
- `src/dropsort/ui/contracts.py`
- `src/dropsort/ui/common/theme.py`
- `src/dropsort/ui/movie_details/details_view.py`
- `src/dropsort/ui/main_window/window.py`
- `tests/unit/application/test_library_query_architecture.py`
- `tests/unit/ui/test_desktop_bootstrap.py`
- `tests/unit/ui/test_movie_details_view.py`
- `tests/unit/ui/test_ui_architecture.py`
- `README.md`
- `PROJECT_STATUS.md`

No database migration was required.

## Tests and coverage

```text
Phase 5A focused: 84 passed, 0 failed
Full suite:       716 passed, 5 skipped, 0 failed
Branch coverage:  95% total
Organize use case: 96%
Organize dialog:   98%
Movie Details:     95%
```

The five skips are the accepted native-Windows symlink-creation privilege limitations. Link/reparse
behavior remains covered by deterministic tests.

Tests cover read-only preview, exact Move/Rename/Move & Rename classification, same- and simulated
cross-volume execution, no-overwrite/case-collision/same-file checks, unsafe names and roots, stale
source identities, catalog-path races, one-shot/double-click authorization, two concurrent previews,
permission/source-removal/database failures, recovery-required results, transactional catalog
updates, preserved movie associations and technical metadata, Qt lifecycle behavior, and dependency
architecture gates.

## Review findings and fixes

- Competing previews for one source could otherwise create competing journals. Confirmation is now
  serialized and fully revalidated before journal creation.
- A physically absent destination could still belong to another catalog row. Destination catalog
  ownership is checked during preview and again immediately before execution.
- Canceled/stale preview tokens could grow indefinitely. Preview registries are bounded and dialogs
  explicitly discard invalidated previews.
- A post-filesystem operation-state lookup failure could hide recovery work. It now returns a
  conservative recovery-required error.
- Pre-journal database failures are translated without mutating the filesystem.
- A fresh desktop process exposed a circular import masked by pytest import order. The established
  operations public boundary is initialized before the transfer implementation, with a subprocess
  startup regression test.
- The Phase 3B architecture test previously scanned unrelated later use cases. Its scope now covers
  the actual read-query modules while preserving all original read-only dependency rules.

No unresolved BLOCKER or CRITICAL finding remains.

## Manual disposable-file smoke test

A repository-local 1.4 MB deterministic `.mkv` fixture was registered in an isolated SQLite catalog
and organized through the real Windows desktop UI. The dialog showed the exact source and
destination, `MOVE`, `D:\ -> D:\`, and a successful no-collision validation. One explicit **Move
File** click completed the operation; the UI remained responsive, disabled repeat confirmation, and
refreshed Movie Details to the new path.

Independent verification confirmed:

- source absent and destination present;
- destination size `1,441,795` bytes;
- expected and actual SHA-256 both
  `33f729e6177dfd3b6d85ae00ef2c3375a87a86acdadfe5f1ece28851cfe7ee63`;
- one `MOVE` journal record in `COMMITTED` state using `hardlink-unlink`;
- the same media-file/movie association and technical metadata persisted at the new catalog path.

Cross-volume behavior was verified by automated simulation only; no real cross-volume media move was
performed during the manual smoke test. All disposable manual/test artifacts were removed afterward.

## Dependencies

Dependencies changed: none.

## Known limitations

- Organization is one explicitly selected physical file at a time; there is no batch workflow.
- Destination naming and folder selection are manual; metadata does not generate destinations.
- Operation history, undo, and recovery UI are not implemented.
- Cross-volume behavior was not manually exercised on two physical volumes in this phase.
- No automatic organization, folder watcher, TV organization, or delete workflow exists.

## Recommended next phase

**Phase 5B - Operation History + Undo/Recovery UI.**

It should expose the existing journal/reverse/recovery boundaries without adding automatic
organization or weakening explicit user authorization.
