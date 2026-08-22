# Phase 1 Test Report

## Final command

```text
python -m pytest -W error --basetemp=.test-tmp --cov=dropsort --cov-report=term -q
```

The temporary base directory is intentionally inside the sandbox repository, not a real media folder.

## Result

- 36 passed
- 0 failed
- warnings treated as errors
- total branch-aware coverage: 90%

## Required safety scenarios covered

- Destination already exists; both files preserved.
- Source disappears after planning.
- Permission denied.
- Destination root becomes unavailable.
- Interruption after destination creation.
- Database write failure after filesystem success.
- Source and destination are the same file.
- Path outside approved roots.
- Case-insensitive Windows destination collision.
- Restart after filesystem success but before database commit.
- Source and destination both exist after interruption.
- Neither source nor destination exists.
- Cross-volume fallback with SHA-256 verification.
- Reverse-plan creation for committed Move/Rename.
- FS_VERIFIED is durably recorded before source removal.
- Source identity changes between plan and execution.
- Tampered verified destination during recovery.
- Partial cross-volume temp cleanup.
- Persistent database failure during recovery.
- Recovery from RECOVERY_REQUIRED after manual source disappearance only when stored verification evidence matches.
- Database path remains unchanged when source removal fails.
- Recovery rejects a destination replaced by a symlink.
- Initial migration is idempotent and a broken migration rolls back partial schema.
- Database media path identity is case-insensitive via a unique normalized path key.

## Test safety

All filesystem mutation tests use pytest temporary/sandbox directories. No real user media path is referenced by the test suite.
