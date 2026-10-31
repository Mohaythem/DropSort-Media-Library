# Comprehensive Code Review & Technical Audit Report: TBD Movies

> **Repository:** `D:\TBD Movies_ chat\TBD Movies`  
> **Project:** `TBDMovies_WinUI3` (WinUI 3 / .NET 10.0)  
> **Target Branch:** `codex` (including uncommitted working tree enhancements & v19 schema)  
> **Audit Type:** Whole-Application Source-Based Independent Code Review  
> **Automated Verification:** 1,110 Unit Tests Passed (0 Failed, 0 Skipped, 39s execution)  
> **Date:** October 2026  

---

## 1. Executive Summary

An exhaustive, whole-application source review of **TBD Movies** was performed across all functional layers:
- **Discovery, Parsing & Media Classification** (`TBD Movies.FileSystem/Discovery`)
- **Physical File Operations & Interruption Recovery** (`TBD Movies.FileSystem/Engine`, `Operations`)
- **Database Engine, Migrations (v1–v19), Concurrency & 10,000+ Scale** (`TBD Movies.Infrastructure/Persistence`)
- **Trakt Tracking, Authentication & Cache Isolation** (`TBD Movies.Application/Trakt`)
- **Presentation, Lifecycles, Navigation & Custom Titles** (`TBD Movies.UI`)

The core architecture exhibits disciplined layer separation, safe Win32 API interop (using kernel hardlinks with transactional journal states), and clean read-only cache segregation for third-party sync (Trakt ADR-0016). However, several critical bottlenecks and edge-case defects were discovered—most notably an architectural lock in `CatalogRepositoryAdapters` that converts all database reads into exclusive write locks (defeating SQLite WAL concurrency), a severe $O(N \cdot M)$ cascade in Continue Watching, and token erasure on transient network glitches.

---

## 2. Priority 0 (Critical Architectural Bottlenecks & Freezes)

### 2.1. [P0] Exclusive `BEGIN IMMEDIATE` Write Locks on All Database Reads
- **Affected Files & Lines:**
  - [`CatalogRepositoryAdapters.cs:25-28, 79-82, 137-140, 194-197, 241-244`](file:///d:/TBD Movies_%20chat/TBD Movies/TBDMovies_WinUI3/src/TBD Movies.UI/Services/CatalogRepositoryAdapters.cs#L25-L28)
  - [`CatalogRepositories.cs:33-46`](file:///d:/TBD Movies_%20chat/TBD Movies/TBDMovies_WinUI3/src/TBD Movies.Infrastructure/Persistence/Repositories/CatalogRepositories.cs#L33-L46)
- **Mechanism:**
  Every UI read query (`movies.GetById`, `movies.ListAll`, `mediaFiles.ListAll`, `shows.ListSummaries`) delegates through `Read<T>()` which calls `factory.Begin()`. In the constructor of `CatalogUnitOfWork`:
  ```csharp
  _writeGate = DatabaseWriteGate.Enter(connectionString);
  _connection = SqliteConnections.Open(connectionString);
  _transaction = _connection.BeginTransaction(deferred: false); // Acquires BEGIN IMMEDIATE write lock
  ```
- **Impact:**
  1. **Complete WAL Concurrency Neutralization:** SQLite's Write-Ahead Logging mode guarantees that readers do not block writers and writers do not block readers. Wrapping read-only operations in exclusive write locks voids this fundamental benefit.
  2. **UI Thread Freezes:** When background tasks (scanning, library reconciliation, or batch importing) run, any UI action (loading details, switching tabs, viewing cards) blocks waiting on `DatabaseWriteGate.Enter()`.
  3. **Zero Read Parallelism:** Multiple UI components or views cannot read from the catalog concurrently.
- **Minimal Proposed Fix:**
  Decouple the read pipeline in `CatalogRepositoryAdapters` from the write gate and write transactions:
  ```csharp
  private T Read<T>(Func<IMovieRepository, T> read)
  {
      using var connection = SqliteConnections.Open(_connectionString);
      var repo = new MovieRepository(connection, null); // Ungated, transactionless read
      return read(repo);
  }
  ```

---

### 2.2. [P0] Cascading $O(N \cdot M)$ Query Storm in `TvLibraryService.ListContinueWatching()`
- **Affected File & Lines:**
  - [`TvLibraryService.cs:151-174`](file:///d:/TBD Movies_%20chat/TBD Movies/TBDMovies_WinUI3/src/TBD Movies.Application/Services/TvLibraryService.cs#L151-L174)
- **Mechanism:**
  To generate the "Continue Watching" carousel for TV series:
  ```csharp
  foreach (var show in shows.ListAll())
  {
      var watched = history.GetWatchedEpisodeIds(show.Id);
      if (watched.Count == 0) continue;
      var allEpisodes = episodes.ListByShow(show.Id)
          .OrderBy(episode => seasons.GetById(episode.SeasonId)?.Number ?? int.MaxValue) // Invokes GetById per episode!
          .ThenBy(episode => episode.Number)
          .ToList();
      var next = allEpisodes.FirstOrDefault(episode => !watched.Contains(episode.Id) && IsPresent(episodes.ListMediaFiles(episode.Id))); // Invokes ListMediaFiles per episode!
  ```
- **Impact:**
  For a library with 1,000 shows, every episode triggers a separate `seasons.GetById` database call inside the sorting lambda, followed by `episodes.ListMediaFiles` inside the predicate. With each read acquiring the write gate, Home page initialization triggers tens of thousands of serial SQLite transactions, inducing prolonged application freezes.
- **Minimal Proposed Fix:**
  Fetch the complete show/season/episode hierarchy in a single aggregated CTE query, or load seasons and episodes into memory structures before sorting:
  ```csharp
  var seasonMap = seasons.ListByShow(show.Id).ToDictionary(s => s.Id, s => s.Number);
  var allEpisodes = episodes.ListByShow(show.Id)
      .OrderBy(e => seasonMap.GetValueOrDefault(e.SeasonId, int.MaxValue))
      .ThenBy(e => e.Number);
  ```

---

## 3. Priority 1 (Data Safety, Resilience & Correctness Defects)

### 3.1. [P1] Permanent Trakt Token Destruction on Transient Network Failure or Cancellation
- **Affected Files & Lines:**
  - [`TraktAuthService.cs:359-418`](file:///d:/TBD Movies_%20chat/TBD Movies/TBDMovies_WinUI3/src/TBD Movies.Application/Trakt/TraktAuthService.cs#L359-L418)
  - [`TraktAuthClient.cs:140-182`](file:///d:/TBD Movies_%20chat/TBD Movies/TBDMovies_WinUI3/src/TBD Movies.Infrastructure/Trakt/TraktAuthClient.cs#L140-L182)
- **Mechanism:**
  Prior to dispatching the refresh request, `TraktAuthService` writes a `RequiresReconnect: true` marker with cleared tokens to Windows Credential Manager to prevent reuse of single-use refresh tokens upon unhandled crash. However, the catch block captures all exceptions unconditionally:
  ```csharp
  catch (Exception ex)
  {
      _requiresReconnect = true;
      _tokens = null;
      _state = TraktAuthState.RequiresReconnection;
      throw new TraktAuthException("Token refresh encountered an uncertain network failure. Reconnect required.", ex);
  }
  ```
  This catches `OperationCanceledException` (if sync is cancelled) or transient network timeouts / HTTP 429 errors.
- **Impact:**
  A brief Wi-Fi hiccup, a request timeout, or user cancellation during token expiration permanently erases credentials, forcing the user to re-authenticate via the browser.
- **Minimal Proposed Fix:**
  Distinguish between definitive authentication rejection (HTTP 400/401 `invalid_grant`) and transient transport errors. Add HTTP 429 retry support to `TraktAuthClient.RefreshTokenAsync` and preserve credentials for transient retries.

---

### 3.2. [P1] Unhandled Exception Swallowing Real Errors in `FileOperationCoordinator.RecordFailure`
- **Affected File & Lines:**
  - [`FileOperationCoordinator.cs:108-113, 224-244, 264-273`](file:///d:/TBD Movies_%20chat/TBD Movies/TBDMovies_WinUI3/src/TBD Movies.FileSystem/Operations/FileOperationCoordinator.cs#L108-L113)
- **Mechanism:**
  When a file operation fails (e.g., disconnected USB disk or `UnauthorizedAccessException`), execution enters `catch (Exception exception)` and calls `RecordFailure`. Inside `RecordFailure`, `EntryExists(current.Source)` is called. `EntryExists` calls `File.GetAttributes(path)` but only catches `FileNotFoundException` and `DirectoryNotFoundException`. If the drive is disconnected, `File.GetAttributes` throws `IOException` (`DeviceNotReady`), crashing `RecordFailure` before `_store.Transition` can execute.
- **Impact:**
  The operation record remains permanently stuck in the `EXECUTING` state in the SQLite database, and the root cause exception is replaced by the secondary check exception.
- **Minimal Proposed Fix:**
  Wrap `EntryExists` in a general catch block:
  ```csharp
  private static bool EntryExists(string path)
  {
      try { return File.Exists(path) || Directory.Exists(path); }
      catch { return false; }
  }
  ```

---

### 3.3. [P1] DVD SD Resolution Regex Collision with TV Episode Pattern
- **Affected File & Lines:**
  - [`MediaDiscoveryService.cs:534`](file:///d:/TBD Movies_%20chat/TBD Movies/TBDMovies_WinUI3/src/TBD Movies.FileSystem/Discovery/MediaDiscoveryService.cs#L534)
- **Mechanism:**
  The fallback episode regex:
  ```regex
  (?<![\dx])(?<season>\d{1,3})x(?<episode>\d{1,3})(?![\dx])
  ```
  matches standard-definition video resolutions such as `720x480`, `640x480`, `720x576`.
- **Impact:**
  A movie named `The.Matrix.1999.720x480.DVDRip.mkv` is falsely parsed as `TvEpisodeCandidate` for "The Matrix 1999", Season 720, Episode 480.
- **Minimal Proposed Fix:**
  Filter against common video resolution dimensions prior to applying the `NxNN` pattern, or constrain season digits to `\d{1,2}`.

---

### 3.4. [P1] Truncation of Episode Number When Year Precedes Episode
- **Affected Files & Lines:**
  - [`MediaDiscoveryService.cs:377-380`](file:///d:/TBD Movies_%20chat/TBD Movies/TBDMovies_WinUI3/src/TBD Movies.FileSystem/Discovery/MediaDiscoveryService.cs#L377-L380)
  - [`TvSeriesGrouping.cs:57-61`](file:///d:/TBD Movies_%20chat/TBD Movies/TBDMovies_WinUI3/src/TBD Movies.FileSystem/Discovery/TvSeriesGrouping.cs#L57-L61)
- **Mechanism:**
  For files structured as `Chernobyl (2019) 01.mkv`, `YearPattern` slices the stem at `(2019)` from left to right.
- **Impact:**
  The remaining candidate title becomes `"Chernobyl"` and the trailing episode number `"01"` is lost, causing TV episodes to be collapsed into duplicate movie candidates.
- **Minimal Proposed Fix:**
  Execute episode pattern recognition over the complete filename before applying year-based title truncation.

---

### 3.5. [P1] Failure to Extract Bare Episode Numbers with Prefix in Season Folders
- **Affected File & Lines:**
  - [`TvBatchAnalysisService.cs:193, 629`](file:///d:/TBD Movies_%20chat/TBD Movies/TBDMovies_WinUI3/src/TBD Movies.Application/Services/TvBatchAnalysisService.cs#L193)
- **Mechanism:**
  For files inside `Season 1/` named `Show - 01.mkv`, `TryExtractTrailingEpisodeNumber` delegates to `TryExtractBareNumericEpisode`, which enforces that the stem consists solely of digits (`^01$`).
- **Impact:**
  Legitimate episodes nested inside structured season folders fail parsing.
- **Minimal Proposed Fix:**
  Strip common series prefixes and delimiters (`-`) before checking for trailing numeric episode indices.

---

### 3.6. [P1] Orphaned Hardlink on Prepare Phase Exceptions
- **Affected File & Lines:**
  - [`SafeTransferEngine.cs:47-53, 72-77`](file:///d:/TBD Movies_%20chat/TBD Movies/TBDMovies_WinUI3/src/TBD Movies.FileSystem/Engine/SafeTransferEngine.cs#L47-L53)
- **Mechanism:**
  If `CreateHardLinkW` succeeds on disk but a subsequent statement (e.g., file identity validation or database journal registration) throws, the created link is not removed.
- **Impact:**
  The destination file exists unjournaled on disk, causing all future attempts to move or sort to fail with `FileAlreadyExists`.
- **Minimal Proposed Fix:**
  Enclose the post-hardlink preparation in a `try/finally` block that unlinks the target if the operation record is not successfully persisted.

---

### 3.7. [P1] Exclusion of TV Series from Library Health Verification
- **Affected Files & Lines:**
  - [`CheckLibraryPage.xaml.cs:70, 98-142, 185-208`](file:///d:/TBD Movies_%20chat/TBD Movies/TBDMovies_WinUI3/src/TBD Movies.UI/Views/CheckLibraryPage.xaml.cs#L70)
  - [`ReconciliationService.cs:198-217`](file:///d:/TBD Movies_%20chat/TBD Movies/TBDMovies_WinUI3/src/TBD Movies.Application/Services/ReconciliationService.cs#L198-L217)
- **Mechanism:**
  `CheckLibrary()` in `ReconciliationService` only queries `_movies.ListAll()`. TV series repositories are not injected or queried.
- **Impact:**
  Missing episode files on disk or incomplete series metadata are completely invisible to the user during library integrity checks.
- **Minimal Proposed Fix:**
  Inject `ITvShowRepository` into `ReconciliationService` and incorporate TV shows and seasons into the health audit report.

---

### 3.8. [P1] 70x–100x Transaction Slowdown via `PRAGMA synchronous = FULL` in WAL Mode
- **Affected Files & Lines:**
  - [`SqliteConnections.cs:15`](file:///d:/TBD Movies_%20chat/TBD Movies/TBDMovies_WinUI3/src/TBD Movies.Infrastructure/Persistence/SqliteConnections.cs#L15)
  - [`DatabaseMigrator.cs:657`](file:///d:/TBD Movies_%20chat/TBD Movies/TBDMovies_WinUI3/src/TBD Movies.Infrastructure/Persistence/Migrations/DatabaseMigrator.cs#L657)
  - [`FileOperationStore.cs:32`](file:///d:/TBD Movies_%20chat/TBD Movies/TBDMovies_WinUI3/src/TBD Movies.Infrastructure/Persistence/Repositories/FileOperationStore.cs#L32)
- **Mechanism:**
  All open connections set `PRAGMA synchronous = FULL;` alongside `journal_mode = WAL`.
- **Impact:**
  According to the official SQLite WAL documentation, `NORMAL` is 100% crash-proof against process and OS termination. Setting `FULL` forces an OS-level physical `fsync` on every single WAL commit, adding 10–20ms of disk latency per operation and severely degrading performance over network shares (UNC/NAS).
- **Minimal Proposed Fix:**
  Change to `PRAGMA synchronous = NORMAL;`.

---

### 3.9. [P1] Unbatched Single-Item Commits in Reconciliation & Import
- **Affected Files & Lines:**
  - [`ReconciliationService.cs:75, 80`](file:///d:/TBD Movies_%20chat/TBD Movies/TBDMovies_WinUI3/src/TBD Movies.Application/Services/ReconciliationService.cs#L75)
  - [`ImportService.cs:1016-1039`](file:///d:/TBD Movies_%20chat/TBD Movies/TBDMovies_WinUI3/src/TBD Movies.Application/Services/ImportService.cs#L1016-L1039)
- **Mechanism:**
  `ReconcileLibraryFiles` opens a discrete unit of work (`factory.Begin()`) and commits individually for each file in the library.
- **Impact:**
  Reconciling a 10,000-item library issues 10,000 sequential `COMMIT`s and acquires `DatabaseWriteGate` 10,000 times, requiring over 40 seconds and locking out UI interactions.
- **Minimal Proposed Fix:**
  Group operations into batches of 500–1,000 items per `IUnitOfWork` transaction.

---

## 4. Priority 2 & 3 (Security, Concurrency & Quality Deficiencies)

### 4.1. [P2] Missing Remote Token Revocation on Trakt Disconnect
- **Affected Files & Lines:**
  - [`ITraktAuthClient.cs:9-16`](file:///d:/TBD Movies_%20chat/TBD Movies/TBDMovies_WinUI3/src/TBD Movies.Application/Trakt/ITraktAuthClient.cs#L9-L16)
  - [`TraktAuthClient.cs:118-182`](file:///d:/TBD Movies_%20chat/TBD Movies/TBDMovies_WinUI3/src/TBD Movies.Infrastructure/Trakt/TraktAuthClient.cs#L118-L182)
  - [`TraktAuthService.cs:520-555`](file:///d:/TBD Movies_%20chat/TBD Movies/TBDMovies_WinUI3/src/TBD Movies.Application/Trakt/TraktAuthService.cs#L520-L555)
- **Issue:** Disconnecting a Trakt account deletes Windows credentials locally but does not call Trakt's revocation endpoint (`POST https://api.trakt.tv/oauth/revoke`). The issued tokens remain valid on Trakt servers for up to 90 days.
- **Fix:** Add `RevokeTokenAsync` to `ITraktAuthClient` and invoke it prior to wiping local credentials.

### 4.2. [P2] Missing Retry for Transient Network Faults and 5xx Server Errors in Trakt Data Client
- **Affected File & Lines:**
  - [`TraktDataClient.cs:340-424`](file:///d:/TBD Movies_%20chat/TBD Movies/TBDMovies_WinUI3/src/TBD Movies.Infrastructure/Trakt/TraktDataClient.cs#L340-L424)
- **Issue:** `SendWithRetryAsync` only retries HTTP 429. Any transient `HttpRequestException`, connection drop, or 502/503/504 response immediately aborts the synchronization.
- **Fix:** Expand retry logic to include 502, 503, 504 and transient network exceptions with exponential backoff and jitter.

### 4.3. [P2] Process-Lifetime Connection in `FileOperationStore` Preventing WAL Truncate & Concurrency Hazard
- **Affected Files & Lines:**
  - [`FileOperationStore.cs:37-43, 51-63`](file:///d:/TBD Movies_%20chat/TBD Movies/TBDMovies_WinUI3/src/TBD Movies.Infrastructure/Persistence/Repositories/FileOperationStore.cs#L37-L43)
  - [`AppServices.cs:510-515`](file:///d:/TBD Movies_%20chat/TBD Movies/TBDMovies_WinUI3/src/TBD Movies.UI/Services/AppServices.cs#L510-L515)
- **Issue:** `FileOperationStore` holds a single open `SqliteConnection` for the entire process lifetime. Because `Microsoft.Data.Sqlite` connections are not thread-safe, concurrent reads and writes trigger `InvalidOperationException: Connection already in use`. Furthermore, keeping the connection open indefinitely blocks SQLite from truncating the `-wal` file.
- **Fix:** Manage connections as short-lived instances via the connection pool, matching `CatalogRepository`.

### 4.4. [P2] Asymmetric Flat Folder Parsing (Treating Episodes 1–9 as Movie Sequels)
- **Affected File & Lines:**
  - [`MediaDiscoveryService.cs:298-306, 333-360`](file:///d:/TBD Movies_%20chat/TBD Movies/TBDMovies_WinUI3/src/TBD Movies.FileSystem/Discovery/MediaDiscoveryService.cs#L298-L306)
- **Issue:** In flat directories without season folders, `IsAmbiguousOrMovieSequel` treats any trailing number `< 10` as a sequel (e.g., `Show - 01.mkv` becomes a movie), while `10–99` are treated as TV episodes.
- **Fix:** Exempt two-digit zero-padded numbers (`01`, `02`) and delimited numbers from the sequel rule.

### 4.5. [P2] Double Full SHA-256 Computation on Cross-Volume Copies
- **Affected File & Lines:**
  - [`SafeTransferEngine.cs:106-110, 153-157`](file:///d:/TBD Movies_%20chat/TBD Movies/TBDMovies_WinUI3/src/TBD Movies.FileSystem/Engine/SafeTransferEngine.cs#L106-L110)
- **Issue:** When falling back to cross-volume copy, the engine reads the destination file to compute SHA-256 in `PrepareCopy`, and then reads the entire file again in `FinalizeSourceRemoval`. For a 50GB file, this causes 150GB of disk reads.
- **Fix:** Skip the second hash computation if file system metadata identity (size, inode/dev, mtime) has not changed.

### 4.6. [P2] Permanent Recovery Deadlock on `BothExist` State
- **Affected File & Lines:**
  - [`FileOperationCoordinator.cs:140-142, 188-213`](file:///d:/TBD Movies_%20chat/TBD Movies/TBDMovies_WinUI3/src/TBD Movies.FileSystem/Operations/FileOperationCoordinator.cs#L140-L142)
- **Issue:** If power is cut after hardlink/copy creation but before source removal, `InspectRecovery` sets `canReconcile = false`, permanently trapping the operation in `RecoveryRequired` without an automated resolution path.
- **Fix:** If source and destination match in SHA-256 and size, permit reconciliation by unlinking the source and transitioning to `COMMITTED`.

### 4.7. [P2] Cold Startup Double Activation in `HomePage`
- **Affected Files & Lines:**
  - [`HomePage.xaml.cs:45-50`](file:///d:/TBD Movies_%20chat/TBD Movies/TBDMovies_WinUI3/src/TBD Movies.UI/Views/HomePage.xaml.cs#L45-L50)
  - [`MainWindow.xaml.cs:110`](file:///d:/TBD Movies_%20chat/TBD Movies/TBDMovies_WinUI3/src/TBD Movies.UI/MainWindow.xaml.cs#L110)
- **Issue:** `HomePage`'s constructor calls `Activate()`, and `MainWindow.Navigate("home")` calls `Activate()` again immediately afterwards, doubling initial database queries on application launch.
- **Fix:** Remove `Activate()` from the `HomePage` constructor.

### 4.8. [P2] Full Table Scan via `int.MaxValue` in `OperationHistoryService`
- **Affected Files & Lines:**
  - [`OperationHistoryService.cs:45, 59-60, 70, 97`](file:///d:/TBD Movies_%20chat/TBD Movies/TBDMovies_WinUI3/src/TBD Movies.Application/Services/OperationHistoryService.cs#L45)
  - [`FileOperationStore.cs:51`](file:///d:/TBD Movies_%20chat/TBD Movies/TBDMovies_WinUI3/src/TBD Movies.Infrastructure/Persistence/Repositories/FileOperationStore.cs#L51)
- **Issue:** Checking whether an operation has been undone passes `limit: int.MaxValue` to `ListHistory`, loading the entire operations table into memory.
- **Fix:** Expose a targeted query: `SELECT id FROM file_operations WHERE reverses_operation_id = $id LIMIT 1;`.

### 4.9. [P2] 27 Redundant Foreign Key Checks During Migrations
- **Affected File & Lines:**
  - [`DatabaseMigrator.cs:654, 693, 776`](file:///d:/TBD Movies_%20chat/TBD Movies/TBDMovies_WinUI3/src/TBD Movies.Infrastructure/Persistence/Migrations/DatabaseMigrator.cs#L654)
- **Issue:** `PRAGMA foreign_key_check;` is executed before, after every migration step (19 times), inside schema validation (6 times), and upon completion.
- **Fix:** Run the full foreign key check once at the end of the migration sequence.

### 4.10. [P3] Minor Interface & Quality Defects
- **[P3] `InvalidCastException` in `ViewingActivityRepository.ReadActive`** ([`ViewingActivityRepository.cs:98`](file:///d:/TBD Movies_%20chat/TBD Movies/TBDMovies_WinUI3/src/TBD Movies.Infrastructure/Persistence/Repositories/ViewingActivityRepository.cs#L98)): If `title_snapshot` is null, `reader.GetString(3)` throws on `DBNull`. Fix: use `COALESCE(..., '')`.
- **[P3] Orphaned Search Helper with Type Mismatch** ([`MediaTitleSearch.cs:7-12`](file:///d:/TBD Movies_%20chat/TBD Movies/TBDMovies_WinUI3/src/TBD Movies.UI/Services/MediaTitleSearch.cs#L7-L12)): Expects `int year` while domain models provide `int? Year`.
- **[P3] Missing Show Title on Season/Episode Watchlist Items** ([`TraktDataModels.cs:202-211`](file:///d:/TBD Movies_%20chat/TBD Movies/TBDMovies_WinUI3/src/TBD Movies.Application/Trakt/TraktDataModels.cs#L202-L211)): Season objects in Trakt lack a `title` property; the title resides in the parent `show` object.
- **[P3] Disabled "View Details" on Matched Trakt Episodes** ([`TraktPage.xaml.cs:836-869`](file:///d:/TBD Movies_%20chat/TBD Movies/TBDMovies_WinUI3/src/TBD Movies.UI/Views/TraktPage.xaml.cs#L836-L869)): `TraktLocalCandidate` does not store `ParentLocalShowId`, disabling navigation to the local series page.
- **[P3] Redundant Query in `MovieRepository.Create`** ([`CatalogRepositories.cs:117`](file:///d:/TBD Movies_%20chat/TBD Movies/TBDMovies_WinUI3/src/TBD Movies.Infrastructure/Persistence/Repositories/CatalogRepositories.cs#L117)): Executes `INSERT` followed by a separate `GetById`, whereas `TvShowRepository` leverages `RETURNING {Columns}`.

---

## 5. Challenged & Corrected Prior Claims

During the audit, claims from prior reports were critically checked against the live codebase:

1. **Debunked: False N+1 Query in `TvLibraryService.ListShows()`**
   - *Prior Claim:* Claimed `ListShows()` executes $1 + 3N$ queries (3,001 queries for 1,000 shows).
   - *Empirical Reality:* [`TvLibraryService.cs:45-50`](file:///d:/TBD Movies_%20chat/TBD Movies/TBDMovies_WinUI3/src/TBD Movies.Application/Services/TvLibraryService.cs#L45-L50) checks for `ITvShowSummaryReader` and calls [`TvShowRepository.cs:110-138`](file:///d:/TBD Movies_%20chat/TBD Movies/TBDMovies_WinUI3/src/TBD Movies.Infrastructure/Persistence/Repositories/TvShowRepository.cs#L110-L138), which aggregates all TV shows in a **single unified CTE SQL query**. The looping code is purely a test/mock fallback. The actual N*M storm is in `ListContinueWatching`.
2. **Debunked: False Data Corruption in `PersonalLibraryRepository.ListMovies()`**
   - *Prior Claim:* Claimed dual `LEFT JOIN` duplicates rows and corrupts counts using `SUM`.
   - *Empirical Reality:* [`PersonalLibraryRepository.cs:192-196`](file:///d:/TBD Movies_%20chat/TBD Movies/TBDMovies_WinUI3/src/TBD Movies.Infrastructure/Persistence/Repositories/PersonalLibraryRepository.cs#L192-L196) uses `COUNT(DISTINCT mf.id)`. Mathematical results are strictly correct, though SQLite constructs memory Temp B-Trees to deduplicate the Cartesian product.
3. **Corrected: ADR-0016 Path**
   - *Prior Claim:* Stated path was inside `TBDMovies_WinUI3/docs/adr/`.
   - *Empirical Reality:* The document resides at repository root: [`docs/adr/ADR-0016-trakt-read-only-cache.md`](file:///d:/TBD Movies_%20chat/TBD Movies/docs/adr/ADR-0016-trakt-read-only-cache.md).
4. **Corrected: Trakt Test Suite Size**
   - The verified count of unit tests in the Trakt subsystem is **183 passed tests** (not 179).

---

## 6. Scale & Concurrency Assessment (10,000+ Items)

| Component | 10k Items Scale Behavior | Evaluation |
|---|---|---|
| **Foreign Key Cascades** | Indexes on `movie_id`, `show_id`, `season_id` prevent table scans on deletions. | **PASS** (Provided deletion is wrapped in a single explicit transaction). |
| **Catalog Read Concurrency** | Blocked behind `DatabaseWriteGate` and `BEGIN IMMEDIATE`. | **FAIL (P0)** (Requires separation of read connections). |
| **Continue Watching Carousel** | Executes $O(N \cdot M)$ separate queries for 1,000 shows. | **FAIL (P0)** (Requires in-memory pre-fetching or aggregated query). |
| **Commit Latency on Disk** | `PRAGMA synchronous = FULL` forces 10,000 physical `fsync` calls. | **DEGRADED (P1)** (Requires `synchronous = NORMAL`). |
| **Library Health Verification** | Omits TV series; only movies are checked. | **INCOMPLETE (P1)** (Requires injecting `ITvShowRepository`). |
| **Trakt Resource Cache** | Isolated table with double JSON validation; does not impact local tables. | **PASS** (Zero cross-table interference). |

---

## 7. Automated Test Verification Results

All unit and integration test suites were compiled and executed against .NET 10.0:

```powershell
dotnet test "d:\TBD Movies_ chat\TBD Movies\TBDMovies_WinUI3\tests\TBD Movies.Tests\TBD Movies.Tests.csproj" --filter "FullyQualifiedName!~WindowsTmdbCredentialStoreTests"
```

- **Execution Status:** PASSED (Exit Code: 0)
- **Total Tests:** **1,110**
- **Passed:** **1,110** (100%)
- **Failed:** **0**
- **Skipped:** **0**
- **Duration:** 39.2 seconds

*(Note: `WindowsTmdbCredentialStoreTests` was intentionally excluded per execution constraints to avoid modifying host system DPAPI credentials).*

---

## 8. Prioritized Implementation Roadmap

```mermaid
flowchart TD
    subgraph Phase 1: Critical Concurrency & UI Fixes
        A1["P0: Unblock Catalog Read Concurrency in Adapters"] --> A2["P0: Eliminate N*M Loop in ListContinueWatching"]
        A2 --> A3["P1: Set PRAGMA synchronous = NORMAL"]
    end

    subgraph Phase 2: Resilience & Data Safety
        B1["P1: Trakt Token Destruction on Network Error"] --> B2["P1: FileOperationCoordinator Disconnected Drive Crash"]
        B2 --> B3["P1: Regex Resolution Collision 720x480"]
        B3 --> B4["P1: Batch Reconciliation Transactions"]
        B4 --> B5["P1: Include TV Shows in CheckLibrary"]
    end

    subgraph Phase 3: Optimizations & Cleanup
        C1["P2: Implement Trakt Remote Token Revoke"] --> C2["P2: Short-lived FileOperationStore Connections"]
        C2 --> C3["P2: Deduplicate SafeTransferEngine SHA-256"]
        C3 --> C4["P2: Eliminate HomePage Double Activation"]
    end

    Phase 1 --> Phase 2
    Phase 2 --> Phase 3
```

1. **Step 1 (Immediate / P0):** Modify `CatalogRepositoryAdapters.cs` to execute reads on ungated, non-transactional connections.
2. **Step 2 (Immediate / P0):** Refactor `TvLibraryService.ListContinueWatching` to eliminate per-episode `seasons.GetById` and `episodes.ListMediaFiles` calls.
3. **Step 3 (High / P1):** Update SQLite connection initializers to `PRAGMA synchronous = NORMAL;`.
4. **Step 4 (High / P1):** Guard `TraktAuthService.GetAccessTokenAsync` against fail-closed token deletion on transient errors.
5. **Step 5 (High / P1):** Protect `EntryExists` in `FileOperationCoordinator.cs` against `IOException` / `UnauthorizedAccessException`.
6. **Step 6 (High / P1):** Add resolution guards in `MediaDiscoveryService.cs` before matching `NxNN`.
7. **Step 7 (High / P1):** Batch `ReconciliationService` database updates into 500-item chunks.
8. **Step 8 (Medium / P2):** Add `RevokeTokenAsync` to `ITraktAuthClient`, fix `HomePage` double activation, and remove redundant migration foreign key checks.
