# DropSort .NET Rewrite — Phase 4 Report

**Date:** 2026-08-31  
**Status:** ✅ Phase 4 Complete  

---

## 1. Infrastructure & Persistence Parity

The Infrastructure layer has been fully aligned with the V1 SQLite persistence strategy. I chose to use pure ADO.NET (`Microsoft.Data.Sqlite`) instead of EF Core to guarantee that the generated SQL exactly matches the historical V1 expectations without ORM side-effects (like shadow properties or accidental lazy loading).

### Migrations (0001–0005)
All 5 historical Python migrations (`0001_initial.sql` to `0005_offline_movie_registration.sql`) were ported verbatim into `DatabaseMigrator.cs`. 
- **Verification:** Unit tests confirm that running the migrator on an empty database successfully creates the V1 schema, tracking state via `PRAGMA user_version`.
- **Constraint Safety:** `PRAGMA foreign_keys = ON` is actively enforced per-connection, ensuring `ON DELETE CASCADE` and `ON DELETE SET NULL` behave exactly as they did in Python.

### Repository Implementations
- **`CatalogUnitOfWork`**: Wraps `SqliteTransaction`, providing transactional consistency for `Movie` and `MediaFile` creation.
- **`FileOperationStore`**: Enforces the FileEngine state machine at the data layer. It rigorously checks valid transitions (`PLANNED` → `VALIDATED` → `EXECUTING` → `FS_VERIFIED` → `COMMITTED` / `FAILED` / `RECOVERY_REQUIRED`).
- **`LibraryMaintenanceRepository`**: Implements the V1 explicit clearance behavior by executing raw `DELETE` operations on `movies`, `media_files`, and `metadata_cache`, safely leaving physical files intact.

---

## 2. File System Engine Parity

The core OS-level abstractions from `dropsort.filesystem` were successfully ported to `DropSort.FileSystem.Engine.SafeTransferEngine`.

### Constraints Enforced
- **Source Identity Locking**: The engine explicitly checks `FileIdentity` (mimicking `st_dev`, `st_ino`, `st_mtime`, and size) to guarantee the source file was not tampered with while queued.
- **Overwrite Prevention**: Safe execution paths rigorously check for destination collisions before proceeding.
- **Same-Volume Transfers**: Leverages `Kernel32.CreateHardLink` to instantly prepare files on the same volume (matching Python's `os.link`).
- **Cross-Volume / Copy Fallback**: If hardlinks fail (e.g., `EXDEV`), the engine falls back to a chunked binary copy, issues a `FileStream.Flush(flushToDisk: true)` (equivalent to `os.fsync`), and asserts a SHA-256 match before finalizing with `File.Move` (atomic rename) and deleting the source.
- **Verify-Before-Delete**: The source file is explicitly retained until the destination has been cryptographically and dimensionally verified.

---

## 3. Verification & Testing

- **Mapping Artifact**: `infra_mapping.md` provides a concise reference matrix detailing how V1 components (`sqlite3`, `os.link`, `hashlib.sha256`) map to .NET 10 APIs.
- **FSM & Migration Tests**: `PersistenceTests.cs` explicitly validates that the `FileOperationStore` throws `InvalidOperationException` if an illegal journal transition is attempted (e.g., skipping the `EXECUTING` phase).
- **Engine Tests**: `EngineTests.cs` exercises the `SafeTransferEngine`, proving hardlink utilization, copy fallback, and overwrite prevention mechanics.
- **Build & Test Output**: The complete .NET solution builds cleanly. The test suite now comprises 48 total tests spanning Domain, Application, Infrastructure, and FileSystem logic—**all 48 pass successfully**.

---

## 4. Unrepresentable / Ambiguous Behavior Addressed

- **`os.link` equivalent**: .NET's `File.CreateHardLink` has historically been unreliable across all Windows targets, so I utilized a robust P/Invoke directly to `Kernel32.CreateHardLinkW` to ensure parity with Python's low-level `os.link` behavior on NTFS.
- **Enum Case Matching**: SQLite `CHECK` constraints are case-sensitive. C#'s `Enum.ToString().ToUpperInvariant()` strips underscores (e.g., `FSVERIFIED`), causing SQLite Error 19. I mapped these explicitly inside the `FileOperationStore` to guarantee identical string representations (e.g., `FS_VERIFIED`, `RECOVERY_REQUIRED`).

---

## 5. Next Steps

Phase 4 is complete. The backend core (Domain, Application, Infrastructure, FileSystem) is fully tested, compliant with V1 constraints, and physically capable of offline execution.

The solution is now ready for the final UI integration (Phase 5).
