# Database Boundaries

SQLite is accessed through repository classes only. Phase 1 uses `FileOperationRepository` and `MediaFileRepository`; the initial migration also reserves V1 tables needed later without implementing those later features.

For a filesystem move linked to a `media_files` row:

1. Persist journal record.
2. Validate.
3. Mark operation `EXECUTING`.
4. Create destination without overwrite.
5. Durably verify destination (SHA-256 for copied cross-volume data).
6. Persist verification evidence as `FS_VERIFIED`.
7. Remove source only after rechecking source/destination identities.
8. In one SQLite transaction, update `media_files.current_path` and transition the journal to `COMMITTED`.

If step 7 fails, the journal becomes `RECOVERY_REQUIRED` while retaining verification evidence and both files are preserved. If the filesystem succeeds but the SQLite transaction in step 8 fails, the journal remains `FS_VERIFIED`; recovery can retry the database commit without repeating the filesystem mutation.

`media_files` stores both the display path (`current_path`) and a canonical case-folded `path_key` with a UNIQUE constraint so database identity follows Windows case-insensitive path semantics instead of relying on SQLite `NOCASE` alone.
