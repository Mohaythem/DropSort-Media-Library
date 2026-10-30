# DropSort Phase 5B Report

Phase 5B is GREEN on native Windows (Python 3.12 / NTFS).

## Implemented

Phase 5B adds a bounded, deterministic **Operation History** page and read-only details. History is
newest first using journal creation time plus SQLite row order as a stable tie-breaker. It shows
Move/Rename type, state, exact paths, movie context, timestamps, and reverse-operation context
without mutating the journal or catalog.

`COMMITTED` is not Undo authorization. **Preview Undo** verifies that the operation is committed,
linked to a current catalog file, has no prior reverse, is the latest relevant non-failed operation
for that media-file row, still owns the catalog path, matches persisted destination identity (and
SHA-256 when recorded), and has an exact unoccupied reverse destination inside the two historical
parent roots. Preview creates no journal or filesystem mutation. One explicit **Confirm Undo**
consumes it and creates a new journal operation whose `reverses_operation_id` points to the original.

Undo reuses Phase 1. Same-volume work uses exclusive hardlink/unlink; cross-volume work uses bounded
copy, flush/fsync, SHA-256 verification, exclusive finalization, then source removal. The media-file
row changes path only after verification and retains its movie association and technical metadata.
Original journal records remain immutable.

Out-of-order Undo is blocked by current catalog path and latest journal relationship. Reverse
creation is serialized inside the SQLite journal transaction, so competing processes cannot create
two reverse rows. Any existing reverse row, including a failed one, blocks blind retry.

Recovery UI inspects first. Only established deterministic actions are enabled: mark an EXECUTING
source-only interruption FAILED while retaining the source, or commit an identity-verified
destination-only operation. Both-files, neither-files, unsafe/tampered destination, and unavailable
historical-root states have no automatic action. Both files are preserved when ambiguous; there is
no delete action.

Organize, Undo, and recovery share one desktop operation coordinator. Background calls reuse the Qt
task runner; closed views invalidate stale results, active reverse execution cannot be closed, and
shutdown waits for pending tasks.

## Files created

- `src/dropsort/library/operations/__init__.py`
- `src/dropsort/library/operations/errors.py`
- `src/dropsort/library/operations/models.py`
- `src/dropsort/library/operations/repositories.py`
- `src/dropsort/database/repositories/operation_history.py`
- `src/dropsort/application/dto/operation_history.py`
- `src/dropsort/application/use_cases/operation_history.py`
- `src/dropsort/ui/history/__init__.py`
- `src/dropsort/ui/history/view.py`
- `tests/unit/application/test_operation_history_dto.py`
- `tests/unit/application/test_operation_history_failures.py`
- `tests/unit/application/test_operation_history_architecture.py`
- `tests/integration/application/test_operation_history_and_undo.py`
- `tests/integration/database/test_operation_history_repository.py`
- `tests/unit/core/test_recovery_inspection.py`
- `tests/unit/library/test_operation_journal_models.py`
- `tests/unit/ui/test_operation_history_view.py`
- `tests/unit/ui/test_operation_history_main_window.py`
- `PHASE_5B_REPORT.md`

## Files modified

- `src/dropsort/application/bootstrap/desktop.py`
- `src/dropsort/application/errors.py`
- `src/dropsort/application/use_cases/__init__.py`
- `src/dropsort/application/use_cases/organize_media_file.py`
- `src/dropsort/core/operations/__init__.py`
- `src/dropsort/core/operations/models.py`
- `src/dropsort/core/operations/recovery.py`
- `src/dropsort/core/operations/service.py`
- `src/dropsort/database/repositories/__init__.py`
- `src/dropsort/database/repositories/file_operations.py`
- `src/dropsort/ui/contracts.py`
- `src/dropsort/ui/main_window/window.py`
- `tests/unit/ui/test_desktop_bootstrap.py`
- `README.md`
- `docs/status/PROJECT_STATUS.md`

No migration was required; the existing reverse-operation foreign key and indexes were sufficient.
Dependencies changed: none.

## Tests and coverage

```text
Phase 5B focused: 100 passed, 0 failed
Full suite:       808 passed, 5 skipped, 0 failed
Branch coverage:  95% total
```

The five skips are accepted native-Windows symlink-creation privilege limitations. Tests cover
history ordering/bounds/read-only behavior; details; same-volume Move/Rename Undo; simulated
cross-volume copy/hash Undo; duplicate confirmation/reverse serialization; current-path, identity,
digest, collision, and out-of-order failures; preserved metadata; DB/source-removal failure;
safe/ambiguous recovery; architecture boundaries; and Qt lifecycle/error redaction.

## Review findings and fixes

- Competing processes could create two reverse rows. Reverse availability is now checked inside the
  same `BEGIN IMMEDIATE` journal transaction.
- A failed reverse was initially excluded from reversed-by lookup. Any reverse now blocks blind retry.
- A details dialog could accept a late preview after close. Close/reject invalidates all delivery.
- An invalid Undo result could leave no Close action. It now returns to a closeable controlled state.
- Organize, Undo, and recovery had separate locks. Desktop composition now shares one coordinator.
- Identity alone cannot always detect same-size replacement. Persisted SHA-256 is checked when present.
- Unavailable historical roots now produce `UNSAFE_DESTINATION` with no recovery action.

No unresolved BLOCKER or CRITICAL finding remains.

## Manual native-Windows verification

A repository-local 1,015,811-byte `.mkv` was moved from `source` to `destination`, then reversed
through the real PySide6 **Operation History -> Details -> Undo Preview -> Confirm Undo** flow. The
preview showed exact paths, `MOVE`, 1.0 MB, same-drive transfer, and stated no change had occurred.
Confirmation stayed responsive and refreshed history with a new `COMMITTED` **Reverse operation**.

Independent verification confirmed source restored, destination absent, SHA-256
`5c96f03aeafc50d08faa98b5a1df4519379f335f8ed383110c827037d5a42ef2`, a linked reverse using
`hardlink-unlink`, unchanged original `COMMITTED` journal, and preserved association plus `.mkv` /
`1080p` / `x264` / `Test` metadata. All disposable artifacts were removed.

Real cross-volume Undo was not manually performed; it is automated-simulation verified. Manual
destructive recovery was **not performed** because deliberately creating and resolving an ambiguous
real state added avoidable risk; deterministic integration tests cover it.

## Known limitations

- History has bounded offset pagination but no search/filter/export.
- Undo is one exact operation at a time; there is no bulk/cascade Undo.
- A failed/recovery-required reverse must be inspected/recovered; no second blind reverse is allowed.
- Cross-volume Undo is automated-simulation verified only.
- Ambiguous recovery requires external manual resolution and explicit reinspection.
- No automatic organization, delete workflow, folder watcher, or TV organization exists.

## Recommended next phase

**Phase 5C - Scan Progress, Cancellation, and Large-Library UX Hardening.**
