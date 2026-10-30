# Infrastructure & File System Parity Mapping (Python to C#)

## Database Persistence
| Python Component | C# Implementation | Notes |
|---|---|---|
| `sqlite3` + raw SQL | `Microsoft.Data.Sqlite` | Direct ADO.NET without EF Core to guarantee identical schema and migration paths. |
| `0001_initial.up.sql` to `0005...` | `Migrations/DatabaseMigrator.cs` | Uses `PRAGMA user_version` to track state. SQL text ported exactly. |
| `Database` (Context) | `SqliteConnectionFactory` | Manages connection strings and `PRAGMA foreign_keys = ON;`. |
| `CatalogUnitOfWork` | `CatalogUnitOfWork` | Wraps `SqliteTransaction` and implements `ICatalogUnitOfWork`. |
| `MovieRepository` | `MovieRepository` | Handles `movies` table mapping. |
| `MediaFileRepository` | `MediaFileRepository` | Handles `media_files` table mapping. |
| `FileOperationRepository` | `FileOperationStore` | Enforces the Journal FSM (PLANNED -> VALIDATED -> EXECUTING -> FS_VERIFIED -> COMMITTED) and guards reverse operations. |
| `PersonalLibraryRepository` | `PersonalLibraryRepository` | Handles `movie_personal_state` and `watch_events`. |
| `LibraryMaintenanceRepository` | `LibraryMaintenanceRepository` | Orchestrates `clear_catalog` via SQL `DELETE` cascades. |

## File System Engine
| Python Component | C# Implementation | Notes |
|---|---|---|
| `SafeTransferEngine` | `SafeTransferEngine` | Core filesystem orchestration. Validates identity locks before removing sources. |
| `os.link` (Hardlink) | `System.IO.File.CreateHardLink` | High-performance transfer within same volume. (Introduced in recent .NET). Fallback to copy. |
| `os.rename` | `System.IO.File.Move` | Finalizes temporary files atomically. |
| `hashlib.sha256` | `System.Security.Cryptography.SHA256` | Used for cross-volume verification. |
| `os.fsync` | `FileStream.Flush(true)` | Ensures directory/file persistence to disk before proceeding. |
| `SourceIdentity` (st_dev, st_ino) | `FileIdentity` (PInvoke) | Retrieves Volume Serial Number and File Index on Windows (via `GetFileInformationByHandle`) for true `st_dev` / `st_ino` parity. |
