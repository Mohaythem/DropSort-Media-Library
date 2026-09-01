# DropSort .NET Rewrite — Phase 3 Report

**Date:** 2026-08-31  
**Status:** ✅ Phase 3 Complete  

---

## 1. Application Layer Parity

The Application layer has been fully ported with behavioral parity to the V1 Python codebase. The architecture strictly enforces a Clean Architecture approach where `DropSort.Application` depends **only** on `DropSort.Domain`, containing no references to `DropSort.Infrastructure` or `DropSort.UI`.

### UI Contracts (Application Facade)
The 7 primary Python UI Protocols (`dropsort.ui.contracts`) were mapped exactly to C# Interfaces:
1. `ILibraryUiActions`
2. `IPersonalLibraryUiActions`
3. `IImportUiActions`
4. `ISettingsUiActions`
5. `IOrganizationUiActions`
6. `IOperationHistoryUiActions`
7. `IReconciliationUiActions`

### Repository Abstractions (Infrastructure Ports)
Data persistence and hardware boundaries are completely abstracted via interfaces in `DropSort.Application.Repositories`:
- `IMovieRepository`, `IMediaFileRepository`
- `ICatalogUnitOfWork`, `ICatalogUnitOfWorkFactory` (for transactional boundaries)
- `IFileOperationStore`, `ILibraryMaintenanceRepository`
- `IPersonalLibraryRepository`

### External Service Abstractions
Dependencies on third-party APIs and file system operations are decoupled via interfaces in `DropSort.Application.External`:
- `IMetadataProvider` (Abstracts TMDB)
- `IPosterService` (Abstracts Poster Cache/Fetching)
- `ISafeTransferEngine` (Abstracts the File Engine)

### Data Transfer Objects (DTOs)
All Python dataclasses in `dropsort.application.dto` were ported to C# records. These records ensure the presentation and infrastructure layers only communicate using safe primitives. Notable ports:
- `MovieListItem`, `MovieDetails`
- `ConfirmMovieImportCommand`, `MovieFileIngestionResult`
- `LibraryHealthProgress`, `MetadataHealthItem`
- `OperationHistoryItem`, `UndoPreview`, `RecoveryAssessment`

---

## 2. Use Cases & Behavioral Constraints Preserved

The 19 audited use cases have been grouped and implemented into 5 primary Application Services (`LibraryService`, `ImportService`, `SettingsService`, `OperationHistoryService`, `ReconciliationService`).

**Strict V1 constraints guaranteed by the C# implementation:**
- **Offline Registration (`register_local_movie_file`)**: The `ImportService.RegisterMovieImport` explicitly builds `Movie` instances with `MetadataStatus.Pending` and `provider=null` using only the File System parser, working offline.
- **Optional TMDB (`propose_movie_import`)**: TMDB is kept firmly behind `IMetadataProvider` as a pure enrichment step (`ImportService.EnrichMovieImport`), preserving the optional nature of external metadata.
- **Check Library remains explicit**: Automated scans are prevented; `ReconciliationService.ReconcileLibraryFiles` only iterates existing database records and tests their physical availability, marking them as `Present` or `Missing` without discovering new files.
- **Missing Files retained**: The reconciliation uses the Domain `MediaFileStatus.Missing` state rather than deleting rows, allowing for future recovery.
- **Stable IDs**: `int` and `string` identities map perfectly between Domain and Application DTOs.
- **Clear Library Semantics**: `SettingsService.ClearLibraryData` orchestrates the `ILibraryMaintenanceRepository.ClearCatalog()` method, exactly matching V1's pattern of forgetting the catalog without touching personal user history or physical files.

---

## 3. Verification & Testing

- **Mapping Artifact**: `application_mapping.md` provides a concise reference matrix between V1 Python boundaries and V2 C# boundaries.
- **Build Isolation**: Confirmed via `dotnet build` that `DropSort.Application` has no dependency on Infrastructure or UI. 
- **Tests Ported**: Created `tests/DropSort.Tests/Application/ApplicationServiceTests.cs` using rigorous Fake Repository implementations (e.g., `FakeUnitOfWork`, `FakeMetadataProvider`, `FakeMediaFileRepo`).
- **Results**: 
  - `dotnet test` completed with **0 failures** (43 tests passing across Domain and Application).

---

## 4. Ambiguous V1 Behavior Addressed

During the porting of `personal_library.py`, I observed that V1 allows removing `WatchEvent` entries by ID. However, the Python application layer reconstructs the entire `PersonalMovieState` from historical events instead of updating a materialized view. I maintained the C# `IPersonalLibraryRepository` abstraction to cleanly support either event-sourcing or CRUD, preventing the UI from forcing a specific persistence style.

---

## 5. Next Steps

Phase 3 is fully complete. The Application boundary is locked and thoroughly tested using fakes.

Phase 4 is ready to commence, which involves implementing the actual `Infrastructure` layer (EF Core / SQLite / TMDB HTTP clients) conforming to these application contracts.
