# TBD Movies Scanner Improvements & Custom Display Title Worklog

## Session Information
- Date: 2026-10-21
- Workspace: `D:/TBD Movies_ chat/TBD Movies`
- Project: `TBDMovies_WinUI3`
- Branch: `codex`
- Primary Model: Gemini 3.8 Flash

## Pinned Upstream References & Provenance
- `5rahim/seanime`: Commit [`4bdff7b27b6c043dc87c0ccad3ead54596133a05`](https://github.com/5rahim/seanime/tree/4bdff7b27b6c043dc87c0ccad3ead54596133a05) (GPL-3.0)
- `5rahim/habari`: Commit [`2626d57ad205a5ea41a5d060355e9314e0457c3e`](https://github.com/5rahim/habari/tree/2626d57ad205a5ea41a5d060355e9314e0457c3e) (GPL-3.0)
- Clean-Room Implementation Policy: All C# implementations in TBD Movies are independently authored clean-room code; no GPL upstream source code was copied or imported.

---

## Source Findings Resolved

### Finding 1: DispatcherQueue and UI Thread Safety for Clipboard & XAML Callbacks
- **Problem**: `TitleInteractionController.TaskDelayScheduler` executed timer callbacks on `TaskScheduler.Default`. `MovieDetailsPage` and `TVShowDetailsPage` constructed the controller without a `uiDispatcher`, causing clipboard operations and XAML UI property updates (`Visibility`, `Text`, etc.) to run on a background thread pool thread, risking COMException (RPC_E_WRONG_THREAD) and clipboard failures.
- **Correction**:
  - Injected UI dispatcher lambda into `TitleInteractionController` in both `MovieDetailsPage.xaml.cs` and `TVShowDetailsPage.xaml.cs`: `uiDispatcher: action => { if (DispatcherQueue.HasThreadAccess) action(); else DispatcherQueue.TryEnqueue(() => action()); }`.
  - Updated `WindowsClipboardService` in `TBD Movies.UI` to accept `DispatcherQueue` and ensure thread-safe dispatching to the UI thread if invoked from a non-UI thread.
  - Wired `_titleController.OnError` to display localized errors via `ShowMessageAsync(LocalizationService.Text("Failed"), error)`.

### Finding 2: Configurable Windows Double-Click Timing & Lifecycle Callback Invalidation
- **Problem**: `SingleClickDelay` was hard-coded to 250ms. Windows mouse double-click interval typically defaults to 500ms (and is user-configurable). A legitimate double-click occurring between 250ms and 500ms triggered clipboard copy before entering edit mode. Furthermore, enqueued timer callbacks from previous page visits could fire across navigation or deactivation.
- **Correction**:
  - Implemented `IDoubleClickTimeProvider` with `WindowsDoubleClickTimeProvider` utilizing P/Invoke `GetDoubleClickTime()` (with safe 500ms fallback).
  - Injected `IDoubleClickTimeProvider` in `TitleInteractionController` constructor for deterministic, controllable test execution.
  - Implemented a monotonic sequence/generation counter (`_interactionGeneration`). Any timer cancellation, editing mode entry (`HandleDoubleClick`, `HandleKeyF2`), re-initialization (`Initialize`), or disposal (`Dispose`) increments the generation, immediately invalidating any pending or queued dispatcher callbacks.
  - Verified controller lifecycle: page deactivation (`Deactivate()`) disposes pending timers cleanly; when navigating back to a page, `Initialize()` resets state, allowing the controller to be reused seamlessly.

### Finding 3: Safe Blur Policy, Length Validation, and Clipboard Resiliency
- **Problem**:
  - `HandleBlur` previously called `HandleKeyEscape()` when saving failed, silently discarding user edits.
  - `HandleKeyEnter` silently truncated titles exceeding 500 characters.
  - Clipboard errors could bubble unhandled.
- **Correction**:
  - Implemented safe blur policy: on blur, if the edit text is unchanged, it exits edit mode cleanly. If changed and save fails (e.g. database error, disk full), `IsEditing` remains `true` and the error is reported via `OnError`, preserving the user's unsaved input and error state on screen.
  - Replaced silent truncation with strict length validation: inputs exceeding 500 characters return `false` and report localized error `CustomTitleTooLong` ("Custom display title cannot exceed 500 characters." / "لا يمكن أن يتجاوز عنوان العرض المخصص 500 حرف.").
  - Wrapped clipboard operations in `try / catch` so clipboard failures remain completely harmless without crashing the application or showing incorrect copy feedback badges.

### Finding 4: Season Conflict Precedence & Conservative Context-Based Inference
- **Problem**:
  - `MediaDiscoveryService.InferSeasonEpisode` previously preferred `folderSeason` over `parsed.SeasonNumber`, causing explicit filename seasons (e.g. `Show.S03E05.mkv` under `Season 02/`) to be silently overwritten by the folder season.
  - Broad trailing number matching risked converting movie sequel titles (`Toy Story 2`, `The Godfather Part 2`, `Avatar 2`), resolutions (`Movie 720`), and ambiguous numbering (`BB - 105`) into TV episodes under season folders.
- **Correction**:
  - In `MediaDiscoveryService.InferSeasonEpisode`, explicit filename season markers (`EpisodePattern` or `AlternateEpisodePattern`) take strict priority over conflicting season folders: `var season = hasExplicitSeason && parsed.SeasonNumber.HasValue ? parsed.SeasonNumber.Value : (folderSeason ?? parsed.SeasonNumber ?? 1);`.
  - Replaced over-broad trailing number inference with conservative `TryExtractBareNumericEpisode` in `TvBatchAnalysisService` and `MediaDiscoveryService`: matches only genuine bare numeric stems (`05`, `[05]`, `(05)`) and explicit episode keywords (`Episode 05`, `الحلقة 05`).
  - Added `IsAmbiguousOrMovieSequel` check to prevent false-positive TV episode conversion for movie sequels (`Part 2`, `Vol 2`, `Toy Story 2`), resolutions (`480`, `576`, `720`, `1080`, `2160`), and ambiguous 3-digit stems (`BB - 105`).
  - Added comprehensive regression and negative fixtures in `TvBatchAnalysisTests.cs`.

### Finding 5: Custom Display Title Projection Consistency, Identity Boundary & Truthful Clipboard Semantics
- **Problem**:
  - `WatchHistoryService` returns `TitleSnapshot`; `ViewingActivityRepository.ListActive` / `ListRecentCompleted` and `PersonalLibraryRepository.ReadHistory` projected canonical `m.title` / `s.title` instead of live custom display titles, causing Home (Continue Watching & Recently Watched) to display canonical names even when custom titles were set.
  - In `PersonalLibraryRepository.ListSection`, `m.custom_display_title` was missing from the SELECT list, causing column ordinal misalignment (`COUNT(DISTINCT mf.id)` read at ordinal 19 where `MovieRepository.Map` expects `custom_display_title`).
  - `PersonalLibraryRepository.ListCustomListMembers` queried `m.title` and `s.title`, ignoring custom titles in user custom lists.
  - `TvLibraryService.ListContinueWatching` initialized `TvContinueWatchingItem` with `show.Title` rather than `show.DisplayTitle`.
  - Manual metadata matching dialogs in `MovieDetailsPage.FixMovieMatchAsync` and `TVShowDetailsPage.FixTvShowMatchAsync` passed `_movie.Title` / `_show.Title` (effective display titles) to TMDB search dialog, allowing custom display labels to contaminate remote metadata matching identity.
  - `MovieOrganizationDialog.cs` suggested disk filenames using `_movie.Title` rather than canonical title.
  - `WindowsClipboardService.TrySetText` returned `true` immediately when `dq.TryEnqueue` enqueued on background thread, violating synchronous boolean semantics if clipboard setting later failed.
- **Correction**:
  - Updated `ViewingActivityRepository` (`ActiveQuery`, `RecentQuery`) and `PersonalLibraryRepository` (`ReadHistory`) to project `COALESCE(NULLIF(m.custom_display_title, ''), m.title, w.title_snapshot)` and `COALESCE(NULLIF(s.custom_display_title, ''), s.title, w.show_title_snapshot)`. Live custom display titles are rendered without rewriting immutable historical snapshots or remote Trakt caches. If media is deleted, queries safely fall back to historical snapshots.
  - Added `m.custom_display_title` at ordinal 19 in `PersonalLibraryRepository.ListSection`, aligning column ordinals perfectly with `MovieRepository.Map`.
  - Updated `PersonalLibraryRepository.ListCustomListMembers` and `LibraryService.ListCustomListItems` to project `CustomDisplayTitle` and `CanonicalTitle`, ensuring custom lists show custom display titles while preserving canonical titles.
  - Updated `TvLibraryService.ListContinueWatching` to pass `show.DisplayTitle` for `ShowTitle`, while retaining `show.CanonicalTitle` and `show.CustomDisplayTitle` on `TvContinueWatchingItem`.
  - Updated `MovieDetailsPage.FixMovieMatchAsync`, `TVShowDetailsPage.FixTvShowMatchAsync`, and `MovieOrganizationDialog.cs` to explicitly use `CanonicalTitle ?? Title`, ensuring custom display labels NEVER contaminate metadata search identity or disk reorganization names.
  - Updated `LibraryPage.xaml.cs` local search filters to match against both `CanonicalTitle` and `Title` (custom display).
  - Updated `WindowsClipboardService.TrySetText` to return `false` truthfully if invoked off-thread (`!dq.HasThreadAccess`).
  - Added 8 focused integration tests in `CustomDisplayTitlePersistenceTests.cs` and `TitleInteractionControllerTests.cs` validating live watch history projection, historical snapshot preservation in DB, deleted media fallbacks, custom lists, continue watching, rescan retention with disposable fixtures, Trakt candidate identity independence, and truthful clipboard failure semantics.

---

## Verification Evidence & Artifact Logs

All fresh verification runs have been executed and their exact commands, exit codes, and raw outputs are persisted in the bounded project artifacts directory: `artifacts/scanner-title-verification/`.

### 1. Fresh Focused Test Suite (65 Passed, 0 Failed)
- **Command**: `dotnet test "tests/TBD Movies.Tests/TBD Movies.Tests.csproj" --filter "FullyQualifiedName~TvBatchAnalysisTests|FullyQualifiedName~ClassificationTests|FullyQualifiedName~TitleInteractionControllerTests|FullyQualifiedName~CustomDisplayTitlePersistenceTests" --verbosity normal`
- **Exit Code**: `0`
- **Result**: **65 passed, 0 failed, 0 skipped** (Duration: 3.90s).
- **Log File**: [`artifacts/scanner-title-verification/01_focused_tests.log`](file:///D:/TBD Movies_%20chat/TBD Movies/TBDMovies_WinUI3/artifacts/scanner-title-verification/01_focused_tests.log)
- **Coverage**:
  - `TvBatchAnalysisTests`: bare numbers under season folders, explicit filename season winning over folder conflict, movie sequel negative fixtures, resolution exclusion, ambiguous `BB - 105` exclusion, bracketed/parenthesized numbers, Arabic-Indic numbers.
  - `TitleInteractionControllerTests`: Windows double-click timing (double-click at 300ms cancels copy within 500ms window), safe blur retaining unsaved edits on failure, strict 500-char rejection, generation counter invalidation, lifecycle reuse after deactivation, clipboard exception safety, truthful clipboard failure semantics (returning false never displays badge), canonical clearing on whitespace/canonical match.
  - `CustomDisplayTitlePersistenceTests`: SQLite schema v19 migration, persistence across connection close/restart, metadata refresh preserving custom title, Trakt offline cache payload isolation, live watch history projection showing custom display title without rewriting historical snapshots, deleted media fallback to historical snapshot, custom list member custom title projection, continue watching custom title projection, rescan retention with disposable fixtures, and Trakt candidate identity extraction independence from custom titles.

### 2. Fresh Broad Test Suite (1,012 Passed, 0 Failed)
- **Command**: `dotnet test "tests/TBD Movies.Tests/TBD Movies.Tests.csproj" --filter "FullyQualifiedName!~WindowsTmdbCredentialStoreTests"`
- **Exit Code**: `0`
- **Result**: **1,012 passed, 0 failed, 0 skipped** (Duration: 51s).
- **Log File**: [`artifacts/scanner-title-verification/02_broad_test_suite.log`](file:///D:/TBD Movies_%20chat/TBD Movies/TBDMovies_WinUI3/artifacts/scanner-title-verification/02_broad_test_suite.log)
- **Documented Notes**:
  - Test restore logs contain transient `NU1900` vulnerability-feed network warnings (offline runner environment unable to reach `api.nuget.org`).
  - Documented exclusion: `WindowsTmdbCredentialStoreTests` (uses native Windows Credential Manager `CredRead`/`CredWrite` API requiring an interactive Windows user profile with full DPAPI vault permissions; excluded per repository testing convention in automated runner environments).

### 3. Production Release x64 MSBuild (Succeeded, 0 Errors, 0 Warnings)
- **Command**: `& "C:/Program Files/Microsoft Visual Studio/18/Community/MSBuild/Current/Bin/MSBuild.exe" "src/TBD Movies.UI/TBD Movies.UI.csproj" /p:Configuration=Release /p:Platform=x64 /p:RuntimeIdentifier=win-x64 /v:m`
- **Exit Code**: `0`
- **Result**: **Build succeeded with 0 warnings and 0 errors**.
- **Output Assembly**: `src/TBD Movies.UI/bin/x64/Release/net10.0-windows10.0.22621/win-x64/TBD Movies.UI.dll`
- **Log File**: [`artifacts/scanner-title-verification/03_release_msbuild.log`](file:///D:/TBD Movies_%20chat/TBD Movies/TBDMovies_WinUI3/artifacts/scanner-title-verification/03_release_msbuild.log)
- **Documented Acceptance Boundary**: Release `/v:m` exit 0 is build/compilation evidence proving type safety and assembly generation; it is not exhaustive UI visual verification or interactive runtime acceptance.

---

## Status of Proposed vs Deferred Work

| Feature / Technique | Upstream Reference | TBD Movies Status | Technical Rationale |
| :--- | :--- | :--- | :--- |
| **Bare Numeric Episodes in Season Folders** | Habari `parser_episode.go` | **Implemented & Verified** | Files like `Season 01/05.mkv` and `[05].mkv` resolve to Season 1 Episode 5 without manual intervention. |
| **Season Conflict Priority** | Seanime `matcher.go` | **Implemented & Verified** | Explicit filename marker (`S03E05`) always overrides contradictory season folder (`Season 02`). |
| **Movie Sequel Protection** | Seanime schemas & database | **Implemented & Verified** | Movie sequels (`Toy Story 2`, `Part 2`) and resolutions (`720`) are protected from TV reclassification. |
| **Custom Display Title (Movies & Shows)** | Seanime manual metadata overrides | **Implemented & Verified** | Schema v19, domain, persistence, projections, controller, WinUI 3 views, English/Arabic localization. |
| **Parent-Folder Title Fallback during Raw Parse** | Seanime `SeparateFilePathS` + `GetFolderTitle` | **Deferred** | TBD Movies enforces strict separation: `MediaDiscoveryService.Parse` is purely factual; contextual title resolution is owned by `TvBatchAnalysisService` to prevent folder pollution (`Downloads/`, `Temp/`). |
| **Roman Numeral & Matching Polish** | Seanime title normalization & aliases | **Deferred** | TMDB API search and `MetadataScorer` rely on clean Levenshtein ratio. Normalizing Roman numerals globally without an exhaustive live TMDB regression baseline risks false matches on franchise titles (*Rocky II*, *Star Wars*). |
