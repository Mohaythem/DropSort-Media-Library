# DropSort .NET Rewrite — Phase 1 Report

**Date:** 2026-08-31  
**Status:** ✅ Phase 1 Complete  

---

## 1. V1 Contracts Discovered

### Domain Entities (9 tables, 5 migrations)

| Entity | ID | Type | Purpose |
|---|---|---|---|
| `movies` | INTEGER PK | Core aggregate | Movie catalog with provider/external_id identity, metadata, genres, metadata_status |
| `media_files` | INTEGER PK | Core entity | Physical file reference, linked to movie via FK, status tracking (PRESENT/MISSING) |
| `file_operations` | TEXT PK (UUID) | Journal record | 7-state FSM for journaled Move/Rename with full source/destination identity |
| `metadata_cache` | INTEGER PK | Cache | Provider query cache with TTL |
| `watched_folders` | INTEGER PK | Config | Scan roots with role (MOVIES/SCAN) |
| `settings` | TEXT PK | KV store | Persisted preferences |
| `movie_personal_state` | movie_id FK PK | Personal | Preference + watchlist per movie |
| `watch_events` | INTEGER PK | Personal | Watch history with rewatch derivation |
| `schema_migrations` | INTEGER PK | Internal | Applied migration tracking |

### Enums (12 stable enums)

- **OperationType**: MOVE, RENAME
- **OperationState**: PLANNED → VALIDATED → EXECUTING → FS_VERIFIED → COMMITTED / FAILED / RECOVERY_REQUIRED
- **RecoverySituation**: NOT_REQUIRED, NOT_ACTIONABLE, SOURCE_ONLY_EXECUTING, DESTINATION_ONLY_VERIFIED, BOTH_EXIST, NEITHER_EXISTS, DESTINATION_UNSAFE_OR_CHANGED
- **MediaFileStatus**: PRESENT, MISSING
- **MetadataStatus**: PENDING, READY, FAILED, NEEDS_MATCH
- **MediaType**: MOVIE, TV_EPISODE, UNKNOWN
- **MatchStatus**: MATCHED, REVIEW_REQUIRED, NO_MATCH
- **PersonalPreference**: NO_OPINION, LIKED, BLACKLISTED
- **PersonalLibrarySection**: WATCHLIST, READY_TO_WATCH, LIKED, BLACKLISTED
- **DiscoveryClassification**: MOVIE_CANDIDATE, TV_EPISODE_SKIPPED, UNKNOWN_MEDIA, ERROR
- **UiLanguage**: en, ar
- **UiTheme**: main (legacy→slate), dark, slate, light

### File Engine Safety Contracts

1. **PathPolicy** — approved-roots whitelist; rejects symlinks, reparse points, case-insensitive collisions; captures SourceIdentity (size, mtime_ns, dev, ino)
2. **SafeTransferEngine** — hardlink-first with copy-sha256-fsync fallback; separate prepare/finalize phases; destination verified before source removal
3. **7-state journal** — PLANNED→VALIDATED→EXECUTING→FS_VERIFIED→COMMITTED (or FAILED/RECOVERY_REQUIRED); all transitions persisted atomically
4. **Undo** — reverse plan through same pipeline; `reverses_operation_id` links parent
5. **Recovery** — filesystem inspection determines RecoverySituation; only deterministic safe actions exposed

### TMDB Boundaries

- Session-only credential (never persisted); env var fallback
- `MovieCandidate` / `MovieMetadata` strict dataclasses
- Poster pipeline: `PosterRequest` → `PosterSource.fetch()` → disk cache → `PosterAsset` (CRC-verified png/jpeg)
- `metadata_cache` table for provider query caching

### Localization / Theme

- 2 languages: English (LTR default), Arabic (RTL); paths/technical values always LTR
- ~200+ keyed `TextId` strings in single `localization.py` module
- 3 selectable themes: Slate (default), Dark, Light
- Legacy migration: main→slate, deep_ink→slate, charcoal→dark, light_blue→light
- Sidebar: persisted width 56–360, default 272, compact/expanded mode

### Use Cases (20)

`list_movies`, `get_movie_details`, `get_movie_list_item`, `discover_media`, `prepare_folder_import_review`, `propose_movie_import`, `confirm_movie_import`, `register_movie_file`, `register_local_movie_file`, `enrich_movie_metadata`, `manual_movie_search`, `movie_search_fallbacks`, `organize_media_file`, `operation_history`, `check_library`, `reconcile_library_files`, `relink_media_file`, `clear_library_data`, `personal_library`, `_library_mapping`

### Test Coverage

86 test files across `tests/unit/` and `tests/integration/` covering: domain model validation, database migration round-trips, repository CRUD, use case orchestration, file engine safety, scanner behavior, and desktop import flow.

---

## 2. Architecture / Parity Map

```
V1 Python Package          →  .NET Project              Target
─────────────────────────────────────────────────────────────────
dropsort.library.movies     →  DropSort.Domain           net10.0
dropsort.library.personal   →  DropSort.Domain           net10.0
dropsort.library.operations →  DropSort.Domain           net10.0
dropsort.library.availability → DropSort.Domain          net10.0
dropsort.media.parser       →  DropSort.Domain           net10.0
dropsort.media.discovery    →  DropSort.Domain           net10.0
dropsort.media.matcher      →  DropSort.Domain           net10.0
dropsort.metadata.contracts →  DropSort.Domain           net10.0
dropsort.core.operations    →  DropSort.Domain           net10.0
dropsort.core.safety        →  DropSort.FileSystem       net10.0-windows
dropsort.core.file_engine   →  DropSort.FileSystem       net10.0-windows
dropsort.application.*      →  DropSort.Application      net10.0
dropsort.posters            →  DropSort.Application      net10.0
dropsort.database.*         →  DropSort.Infrastructure   net10.0-windows
dropsort.ui.*               →  DropSort.UI               net10.0-windows (WinUI 3)
tests/                      →  DropSort.Tests            net10.0-windows
```

### Layer Rules

- **Domain** (`net10.0`): No external dependencies. Owns entities, enums, value objects, and domain logic.
- **Application** (`net10.0`): References Domain only. Owns use case interfaces, DTOs, and persistence abstractions (repository interfaces).
- **Infrastructure** (`net10.0-windows`): References Application. Provides SQLite implementation of persistence abstractions.
- **FileSystem** (`net10.0-windows`): References Domain only. Owns PathPolicy, SafeTransferEngine, and filesystem safety logic.
- **UI** (`net10.0-windows`, WinUI 3): References Application, Infrastructure, FileSystem. Owns XAML, windows, and composition root.

---

## 3. Created Solution / Projects

```
DropSort_WinUI3/
├── DropSort.slnx                                    (solution file)
├── src/
│   ├── DropSort.Domain/
│   │   ├── DropSort.Domain.csproj                   (net10.0)
│   │   └── DomainAssemblyMarker.cs
│   ├── DropSort.Application/
│   │   ├── DropSort.Application.csproj              (net10.0, refs Domain)
│   │   └── ApplicationAssemblyMarker.cs
│   ├── DropSort.Infrastructure/
│   │   ├── DropSort.Infrastructure.csproj            (net10.0-windows, refs Application)
│   │   └── Persistence/
│   │       └── DatabaseBootstrap.cs                  (Microsoft.Data.Sqlite)
│   ├── DropSort.FileSystem/
│   │   ├── DropSort.FileSystem.csproj               (net10.0-windows, refs Domain)
│   │   └── FileSystemAssemblyMarker.cs
│   └── DropSort.UI/
│       ├── DropSort.UI.csproj                        (WinUI 3, WindowsAppSDK 1.7)
│       ├── app.manifest
│       ├── App.xaml / App.xaml.cs
│       └── MainWindow.xaml / MainWindow.xaml.cs
└── tests/
    └── DropSort.Tests/
        ├── DropSort.Tests.csproj                     (xUnit, refs Domain/Application/Infrastructure)
        └── SolutionSmokeTests.cs
```

---

## 4. Build / Test / Launch Results

### Restore ✅

```
dotnet restore DropSort.slnx
```

All 6 projects restored. NuGet packages resolved:
- `Microsoft.WindowsAppSDK` 1.7.260224002
- `Microsoft.Data.Sqlite` (preview)
- `xunit` 2.x, `Microsoft.NET.Test.Sdk` 17.x

**Warning:** `SQLitePCLRaw.lib.e_sqlite3` 2.1.11 has a known vulnerability (NU1903). Pin to a patched version in Phase 2.

### Build ✅

All 6 projects compile without errors.

**Note:** The WinUI 3 project (`DropSort.UI`) requires MSBuild from Visual Studio 2025 to build due to a known .NET 10 SDK issue with `Microsoft.Build.Packaging.Pri.Tasks.dll`. The remaining 5 projects build successfully with the `dotnet` CLI.

```powershell
# Non-UI projects (dotnet CLI works):
dotnet build DropSort.slnx --no-restore

# UI project (requires VS MSBuild):
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" `
    src\DropSort.UI\DropSort.UI.csproj -t:Build -p:Configuration=Debug -p:Platform=x64
```

### Tests ✅

```
dotnet test tests/DropSort.Tests/DropSort.Tests.csproj --no-restore
```

```
Total tests: 3
     Passed: 3
 Total time: 5.35 Seconds
```

| Test | Result |
|---|---|
| `DomainAssembly_Loads` | ✅ Passed |
| `ApplicationAssembly_Loads` | ✅ Passed |
| `SqliteInitializes_ReturnsVersion` | ✅ Passed |

### Launch Smoke Test ✅

```
DropSort.UI.exe launched (PID: 37388)
WinUI 3 window displayed with "DropSort" title and SQLite version status.
Process ran for 5 seconds without crashing, terminated cleanly.
```

---

## 5. Unknowns and Risks

| Risk | Severity | Mitigation |
|---|---|---|
| **WinUI 3 PRI build requires VS MSBuild** | Medium | Known .NET 10 SDK gap. Use `MSBuild.exe` from VS 2025 for UI project. May be resolved in a future SDK update. |
| **SQLitePCLRaw vulnerability (NU1903)** | Low | Pin `SQLitePCLRaw.lib.e_sqlite3` to a patched version in Phase 2. Does not affect functionality. |
| **V1 offline registration (migration 0005)** | Info | Movies can now have NULL provider/external_id with metadata_status tracking. The .NET schema must reproduce this exactly. |
| **V1 legacy theme migration** | Info | Settings may contain legacy theme IDs (main, deep_ink, charcoal, light_blue). The .NET layer must handle the same migration logic. |
| **V1 localization is a single 1,479-line Python file** | Info | Consider .NET resource files (.resw) or a structured JSON approach in Phase 2. |
| **No V1 data migration strategy yet** | Info | The existing SQLite database at `%LOCALAPPDATA%\DropSort\dropsort.db` will need a migration path. Not in scope until later. |

---

## 6. Phase 2 Recommendation

Phase 2 should port the **domain model layer** into `DropSort.Domain`:

1. **Port all 12 enums** exactly as documented in the parity map.
2. **Port domain entities**: Movie, MediaFile, FileOperationRecord, MovieCatalogData, VerifiedMediaFileFacts, ParsedMedia, MovieCandidate, MovieMetadata, PersonalMovieState, WatchEvent.
3. **Port value objects**: SourceIdentity, OperationUpdate, FileOperationPlan, PreparedTransfer, RecoveryInspection, DiscoveredMedia, MatchDecision, CandidateScore.
4. **Port validation rules**: All the `__post_init__` invariants from V1 dataclasses should become constructor validation or dedicated validator types.
5. **Define persistence interfaces** in `DropSort.Application`: IMovieRepository, IMediaFileRepository, IFileOperationStore, IMetadataCacheRepository, ISettingsRepository, IPersonalLibraryRepository.
6. **Write domain unit tests** in `DropSort.Tests` covering entity construction, validation, and enum exhaustiveness.

Do **not** implement:
- Infrastructure (SQLite repositories) — wait for Phase 3
- File Engine — wait for Phase 3
- TMDB client — wait for later phases
- UI beyond the current minimal window — wait for UI phase
- Any V2 features

---

*Phase 1 complete. Awaiting approval to proceed with Phase 2.*
