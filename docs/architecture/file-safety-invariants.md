# File Safety Invariants

1. **No unjournaled mutation** — Move/Rename execution requires an existing `VALIDATED` journal record.
2. **No overwrite** — existing or case-insensitive-colliding destinations are rejected.
3. **Approved roots only** — source and destination must resolve inside configured roots.
4. **No standalone destructive delete** — source removal exists only as the final step of an explicitly approved Move/Rename after destination verification.
5. **Verify and journal before source removal** — destination is durably flushed/verified, its verification evidence is persisted as `FS_VERIFIED`, and only then may the source path be removed.
6. **Database path after verification only** — catalog state never leads physical state.
7. **Core reversibility** — every committed Move/Rename can produce a new reverse plan; undo UI is not Phase 1.
8. **Ambiguity preserves data** — if both source and destination exist, recovery keeps both and requests reconciliation.
9. **Missing/missing is never guessed** — if neither exists, mark recovery required.
10. **Crash recovery** — non-terminal journal states can be reconciled after restart.
11. **Reparse/symlink traversal rejected** — operations do not traverse link-like path components inside approved roots.
12. **Cross-volume safety** — fallback is copy-to-destination-temp, flush, verify, no-overwrite finalize, then source removal.
