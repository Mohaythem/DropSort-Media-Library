# DropSort — AI Handoff & Project Checkpoint Guide

> **CRITICAL CHECKPOINT NOTICE (GOLDEN BASELINE)**
> **Checkpoint Tag**: `v0.11.0-checkpoint-golden`
> **Backup Branch**: `backup/golden-baseline`
> **Base Working Branch**: `codex`
> **Test Status**: 1,147 tests passed (0 failed, 100% pass rate).
> **Date**: October 2026

---

## 1. Project Overview & Current State

DropSort is a modern Windows desktop media organizer and library manager built with **C# / .NET 10** and **WinUI 3 (Windows App SDK)**.

### Implemented & Verified Capabilities:
1. **Core Library & Scanning**:
   - Deep file system discovery for Movies and TV Shows.
   - Robust media naming parser, title extraction, and format policies (`MediaFormatPolicy.cs`).
   - SQLite persistent database with migration system (`DatabaseMigrator.cs`).
   - Library repair, health checking, and reconciliation services.

2. **Trakt Integration (Phase 1 & Phase 2)**:
   - Device Code OAuth authentication flow (`TraktAuthClient.cs`) with 403 / User-Agent hardening (`TraktRequestHeaders.cs`).
   - Read-only and two-way list actions (`ITraktListActions.cs`, `ITraktListWriter.cs`, `TraktDataClient.ListWriter.cs`).
   - Custom lists management and synchronization (`MyListsPage.xaml.cs`, `MyListsPage.Trakt.cs`).
   - Trakt local snapshot caching and offline fallback (`TraktCacheRepository.cs`).

3. **WinUI 3 Presentation**:
   - Fluent UI with Mica styling and dark/light theme support.
   - Movie Details & TV Show Details pages with responsive layouts and metadata projections.
   - Interactive dialogs: `MovieOrganizationDialog.cs`, clipboard helpers, and search controls (`MediaTitleSearch.cs`).
   - Full Arabic & English localization support (`LocalizationService.cs`).

---

## 2. Strict Operational Rules for AI Assistants

Any AI model working on this codebase **MUST** strictly adhere to the following rules:

1. **NO Destructive Git Operations**:
   - NEVER execute `git reset --hard`, `git clean -fd`, or force-push (`git push --force`).
   - NEVER delete files or directories unless explicitly instructed by the user.

2. **Preserve Test Integrity**:
   - All 1,147+ tests MUST pass after any modification.
   - NEVER delete, skip, or disable existing tests to make a build succeed.
   - Always run the test command before claiming a task is complete.

3. **Surgical, Bounded Changes**:
   - Make focused, minimal edits. Do not rewrite entire files when a small method fix suffices.
   - Preserve existing architecture, namespace conventions, and docstrings.

4. **Rollback Safety**:
   - If a code experiment fails or corrupts the build, you can safely return to this baseline using:
     ```bash
     git checkout backup/golden-baseline
     ```

---

## 3. Essential Commands

### Build Solution:
```powershell
dotnet build DropSort_WinUI3\DropSort.sln -c Debug
```

### Run All Unit Tests:
```powershell
dotnet test DropSort_WinUI3\tests\DropSort.Tests\DropSort.Tests.csproj --no-restore -c Debug
```
*Expected result: 1,147 Passed, 0 Failed.*

### Run WinUI 3 Desktop App:
```powershell
dotnet run --project DropSort_WinUI3\src\DropSort.UI\DropSort.UI.csproj -c Debug
```

### Inno Setup Installer:
- Script location: `DropSort_WinUI3/packaging/DropSort.iss`

---

## 4. Architecture & Key Files Map

| Layer | Project | Key Files & Responsibilities |
|---|---|---|
| **Domain** | `DropSort.Domain` | Entities, Value Objects: `Movies/Models.cs`, `Shows/ShowModels.cs`, `MediaFormatPolicy.cs` |
| **Application** | `DropSort.Application` | Use cases & services: `LibraryService.cs`, `TvLibraryService.cs`, `LibraryRepairService.cs`, `TraktSyncService.cs`, `TitleInteractionController.cs` |
| **FileSystem** | `DropSort.FileSystem` | Disk I/O & Scanners: `MediaDiscoveryService.cs`, `MediaFileActions.cs` |
| **Infrastructure** | `DropSort.Infrastructure` | SQLite & External APIs: `DatabaseMigrator.cs`, `CatalogRepositories.cs`, `TraktAuthClient.cs`, `TraktDataClient.cs` |
| **UI** | `DropSort.UI` | WinUI 3 Views & Models: `LibraryPage.xaml`, `MovieDetailsPage.xaml`, `TVShowDetailsPage.xaml`, `MyListsPage.xaml`, `LocalizationService.cs` |
| **Tests** | `DropSort.Tests` | Comprehensive unit & contract tests across all layers |

---

## 5. Active Roadmaps & Worklogs

For detailed historical context and upcoming features, refer to:
- `DropSort_WinUI3/RELEASE_2_V2_ROADMAP.md`: Next-phase roadmap.
- `DropSort_WinUI3/FUTURE_PLAN.md`: Strategic enhancements.
- `DropSort_WinUI3/TRAKT_PHASE1_WORKLOG.md` & `TRAKT_DEVICE_CODE_403_WORKLOG.md`: Trakt client details.
- `DropSort_WinUI3/P2_FIXES_WORKLOG.md`: Priority 2 bugfixes and stability work.
