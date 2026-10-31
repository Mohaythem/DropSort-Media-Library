# TBD Movies vs Seanime: Scanner Research, Comparison, and Implementation Report

## 1. Upstream Research & Baseline

### Pinned Upstream Revisions with Clickable Source Links
- **Seanime**: [`5rahim/seanime`](https://github.com/5rahim/seanime)
  - Pinned Commit SHA: [`4bdff7b27b6c043dc87c0ccad3ead54596133a05`](https://github.com/5rahim/seanime/tree/4bdff7b27b6c043dc87c0ccad3ead54596133a05)
  - Upstream License: **GNU General Public License v3.0 (GPL-3.0)**
  - Core Modules (Verified Source):
    - Scanner engine: [`internal/library/scanner/scan.go`](https://github.com/5rahim/seanime/blob/4bdff7b27b6c043dc87c0ccad3ead54596133a05/internal/library/scanner/scan.go)
    - Matching algorithm: [`internal/library/scanner/matcher.go`](https://github.com/5rahim/seanime/blob/4bdff7b27b6c043dc87c0ccad3ead54596133a05/internal/library/scanner/matcher.go)
    - Media tree analysis: [`internal/library/scanner/media_tree_analysis.go`](https://github.com/5rahim/seanime/blob/4bdff7b27b6c043dc87c0ccad3ead54596133a05/internal/library/scanner/media_tree_analysis.go)
    - Title normalization: [`internal/library/scanner/title_normalization.go`](https://github.com/5rahim/seanime/blob/4bdff7b27b6c043dc87c0ccad3ead54596133a05/internal/library/scanner/title_normalization.go)
    - Local file abstraction: [`internal/library/anime/localfile.go`](https://github.com/5rahim/seanime/blob/4bdff7b27b6c043dc87c0ccad3ead54596133a05/internal/library/anime/localfile.go), [`localfile_helper.go`](https://github.com/5rahim/seanime/blob/4bdff7b27b6c043dc87c0ccad3ead54596133a05/internal/library/anime/localfile_helper.go)
    - Hydration: [`internal/library/scanner/hydrator.go`](https://github.com/5rahim/seanime/blob/4bdff7b27b6c043dc87c0ccad3ead54596133a05/internal/library/scanner/hydrator.go)
- **Habari**: [`5rahim/habari`](https://github.com/5rahim/habari)
  - Pinned Commit SHA: [`2626d57ad205a5ea41a5d060355e9314e0457c3e`](https://github.com/5rahim/habari/tree/2626d57ad205a5ea41a5d060355e9314e0457c3e)
  - Upstream License: **GNU General Public License v3.0 (GPL-3.0)**
  - Core Modules (Verified Source):
    - Main entry & parser: [`parser.go`](https://github.com/5rahim/habari/blob/2626d57ad205a5ea41a5d060355e9314e0457c3e/parser.go)
    - Data schemas: [`metadata.go`](https://github.com/5rahim/habari/blob/2626d57ad205a5ea41a5d060355e9314e0457c3e/metadata.go)
    - Season parsing: [`parser_season.go`](https://github.com/5rahim/habari/blob/2626d57ad205a5ea41a5d060355e9314e0457c3e/parser_season.go)
    - Episode parsing: [`parser_episode.go`](https://github.com/5rahim/habari/blob/2626d57ad205a5ea41a5d060355e9314e0457c3e/parser_episode.go)
    - Title parsing: [`parser_title.go`](https://github.com/5rahim/habari/blob/2626d57ad205a5ea41a5d060355e9314e0457c3e/parser_title.go)
    - Keyword & token engines: [`keyword_manager.go`](https://github.com/5rahim/habari/blob/2626d57ad205a5ea41a5d060355e9314e0457c3e/keyword_manager.go), [`token_manager.go`](https://github.com/5rahim/habari/blob/2626d57ad205a5ea41a5d060355e9314e0457c3e/token_manager.go), [`tokenizer.go`](https://github.com/5rahim/habari/blob/2626d57ad205a5ea41a5d060355e9314e0457c3e/tokenizer.go)
- **TBD Movies**:
  - Baseline Branch: `codex`
  - Baseline Commit: `3b31565` (`release: add read-only Trakt integration in 0.11.0`)
  - Target Project: `TBDMovies_WinUI3` (.NET 10, C# 13, WinUI 3, SQLite)

### Licensing and Clean-Room Provenance Note
Both `5rahim/seanime` and `5rahim/habari` are strictly licensed under the **GNU General Public License v3.0 (GPL-3.0)**.
Under no circumstances may source code from Seanime or Habari be copied, imported, or translated directly into TBD Movies. All techniques, heuristics, and algorithmic patterns implemented in TBD Movies are independently authored clean-room implementations in C#, adhering strictly to TBD Movies's proprietary/MIT architectural boundaries.

---

## 2. Upstream Discovery, Parsing, Context, and Matching Architecture

### A. Media Folder Traversal and Discovery
- **Seanime (`internal/library/scanner/scan.go`)**:
  - Traverses the primary `DirPath` and optional `OtherDirPaths` recursively using `filesystem.GetMediaFilePathsFromDirS`.
  - Normalizes file paths and maintains a path deduplication set (`retrievedPathMap`).
  - Supports incremental scanning: files marked `Locked` or `Ignored` in the existing library (`ExistingLocalFiles`) are collected in `skippedLfs` and completely bypassed from re-parsing and re-matching.
  - Implements a "shelving" mechanism (`WithShelving`): if a library root becomes unavailable (e.g. unplugged external drive), locked items are preserved in `shelvedLocalFiles` instead of being pruned as missing or deleted.
- **TBD Movies (`TBD Movies.FileSystem/Discovery/MediaDiscoveryService.cs`)**:
  - Uses `NetworkReadGuard` to guard UNC/network file I/O against long blocking operations.
  - Verifies paths against reparse-point loops (`PathPolicy.AssertNoReparseComponents`).
  - Implements breadth-first enumeration with per-directory snapshots (`ReadDirectorySnapshot`), isolating disappeared or permission-denied sibling entries.
  - Distinguishes `StorageUnavailable` (disconnected storage root) from `DirectoryReadFailed` or `PermissionDenied`.

### B. Habari's Parsing Pipeline & Capabilities
- **Tokenization**:
  - Does not rely purely on monolithic regular expressions. Splits the filename into tokens (words, numbers, delimiters, opening/closing brackets `[]`, `()`, `{}`).
  - Classifies tokens using keyword lookup tables for video resolutions (`1080p`, `2160p`, `4K`), release groups, audio/video terms (`x265`, `HEVC`, `AAC`), and anime types (`OVA`, `ONA`, `Special`).
- **Season and Episode Resolution**:
  - `parser_season.go`: Detects `SxxExx`, `Season X`, `1st Season`, ordinal/word markers, and season ranges (`S01-03`).
  - `parser_episode.go`: Detects episode numbers through multiple strategies:
    1. Alternative numbers: `01 (12)`
    2. Prefixed keywords: `Ep 01`, `Episode 01`, `OVA 01`
    3. Range separators: `01 of 24`
    4. Delimited trailing numbers: `Title - 01`
    5. Enclosed brackets: `[12]`
    6. Trailing word-separated numbers: `Title 01`
- **Role and Limitations**:
  - Habari is tailored for anime naming styles (which frequently feature absolute episode counts, e.g. `Episode 105`, rather than Western `S01E05`).
  - Habari produces raw metadata fields (`Title`, `SeasonNumber`, `EpisodeNumber`, `EpisodeTitle`, `ReleaseGroup`). It performs **no** external network matching, database lookup, or confidence scoring itself.

### C. Context: Filename vs Parent Folders
- **Seanime's Multi-Tier Path Deconstruction (`internal/library/anime/localfile.go`)**:
  - Seanime calls `filesystem.SeparateFilePathS(path, libraryRoots)`:
    - Splits the path into `info.Filename` and `info.Dirnames` (all directory segments between the library root and the file).
    - Habari parses the filename into `ParsedData`.
    - Habari also parses directory segments in `info.Dirnames` into `ParsedFolderData`.
  - In `localfile_helper.go`:
    - `GetParsedTitle()`: returns `cmp.Or(f.ParsedData.Title, f.GetFolderTitle())`.
    - `GetFolderTitle()`: scans `ParsedFolderData` from nearest to furthest, skipping generic keywords ("specials", "extras", "season 1", etc.), and returns the closest folder's parsed title.
    - `GetAllFolderTitles()`: returns all valid ancestor directory titles.
    - `GetTitleVariations()`: constructs candidate titles by combining filename titles, closest folder titles (primary), ancestor folder titles (extra, scored with lower weight), and combines them with extracted season/part numbers.
- **TBD Movies Architecture & Separation of Concerns**:
  - `MediaDiscoveryService.cs` acts strictly as the honest, low-level factual file parser. It extracts facts directly evident from the filename without guessing or mutating filenames based on parent folder names.
  - `TvBatchAnalysisService.cs` owns contextual hierarchy analysis. It evaluates folder structure (`folderShowTitle`, `folderSeason`), extracts evidence, resolves missing title/season coordinates, and identifies conflicts.
  - `InferSeasonEpisode` provides best-effort coordinates for manual repair actions ("Move to series") while strictly guarding against false-positive movie conversion.

---

## 3. Filename Test Matrix: Verified Analysis

| Filename | Direct Parse Behavior | Context Required? | Ambiguity / Failure Mode | Seanime Verified Result | TBD Movies Verified Behavior |
| :--- | :--- | :--- | :--- | :--- | :--- |
| `Breaking.Bad.S01E05.1080p.mkv` | Direct | No | None. Explicit title, season 1, episode 5, resolution 1080p. | Title: "Breaking Bad", S1, E5, 1080p. | `TvEpisodeCandidate`: "Breaking Bad", S1, E5, 1080p. |
| `S01E05.mkv` | S1, E5 extracted; Title is missing | **Yes (Parent Folder)** | Without folder, show identity is unknown. | Habari parses S1 E5, title empty. `GetParsedTitle()` resolves to closest folder title. | `MediaDiscoveryService` marks `TvEpisodeSkipped` (title=null). `TvBatchAnalysisService` resolves show title from parent folder. |
| `Episode 05.mkv` | E5 extracted; S and Title missing | **Yes (Parent Folder)** | Without folder, series and season are unknown. | Habari parses E5. Title resolves from parent folder. Season defaults to 1 or folder season. | `MediaDiscoveryService` marks `TvEpisodeSkipped` (title=null). `TvBatchAnalysisService` resolves folder title and season. |
| `05.mkv` | Pure numeric stem | **Yes (Parent Folder + Season)** | Ambiguous: could be track 05, episode 05, movie chapter 05, or sequel. | Habari parses E5. If in show folder, matches episode 5. If standalone, fails title match. | `TvBatchAnalysisService` and `InferSeasonEpisode` safely resolve to (folderSeason, 5) only when inside explicit season folder (`Season 01/05.mkv`). |
| `Breaking Bad - 1x05.mkv` | Direct | No | None. `1x05` is standard Western season x episode format. | Title: "Breaking Bad", S1, E5. | `TvEpisodeCandidate`: "Breaking Bad", S1, E5 via `AlternateEpisodePattern`. |
| `BB - 105.mkv` | Ambiguous numbering | **Yes (Metadata & Schema Context)** | `105` can mean Season 1 Episode 05 (Western 3-digit) or Episode 105 (Anime absolute). | Habari parses `EpisodeNumber = ["105"]`, Title="BB". Does NOT treat 105 as S1E5. | Falls through to `MovieCandidate` ("BB 105"). `TvSeriesGrouping` only detects when 4+ items exist; `InferSeasonEpisode` conservatively returns null. |
| `Toy Story 2.mkv` | Direct Movie | No | Trailing digit 2 is a movie franchise sequel, not a TV episode. | Movie title "Toy Story 2". | `MovieCandidate` ("Toy Story 2"). Preserved as Movie in season folders; `InferSeasonEpisode` returns null. |

---

## 4. Seanime vs TBD Movies: Detailed Comparison

| Area | Seanime Verified Behavior | TBD Movies Verified Behavior | Demonstrated Gap | Status & Implementation Strategy |
| :--- | :--- | :--- | :--- | :--- |
| **Directory Traversal & Paths** | Multi-directory traversal (`DirPath` + `OtherDirPaths`). Path deduplication. Shelving of files on disconnected drives. | Single root discovery with `NetworkReadGuard`, anti-symlink loop traversal (`PathPolicy`), snapshotting. | TBD Movies handles UNC/storage availability robustly, but discovery ignores parent folder context for media naming. | **Preserved Architectural Boundary**: Discovery remains factual; contextual hierarchy is handled in `TvBatchAnalysisService`. |
| **Season Folder Numeric Episodes** | Habari extracts episode numbers even from bare numbers if in series/season folder. | Bare numeric files (`Season 01/05.mkv`, `[05].mkv`) previously remained unresolved. | Automated batch ingestion failed on bare episode numbers under season folders. | **Implemented**: Added `TryExtractBareNumericEpisode` in `TvBatchAnalysisService` and `MediaDiscoveryService.InferSeasonEpisode`. |
| **Season Conflict Precedence** | Upstream resolves episode and applies folder context. | `MediaDiscoveryService.InferSeasonEpisode` previously allowed `folderSeason` to overwrite `parsed.SeasonNumber`. | Conflicting season folder (`Season 02/Show.S03E05.mkv`) silently overwrote explicit S03 with S02. | **Implemented**: Explicit filename season (`SxxExx`, `1x02`) strictly takes precedence over folder season. |
| **Movie Sequel Protection** | Distinguishes anime movies from episodes via offline anime database and metadata schemas. | Naive trailing number patterns risked converting movie sequels (`Toy Story 2`) into TV episodes. | Over-broad trailing-number matching converted movie franchise titles into TV show episodes. | **Implemented**: Conservative exclusion in `IsAmbiguousOrMovieSequel` and `TryExtractBareNumericEpisode` rejects sequels, resolutions, and ambiguous 3-digit numbers. |
| **Parent-Folder Title Fallback in Raw Parse** | Seanime parses ancestor folders and sets `info.ParsedData.Title` from folder. | `MediaDiscoveryService.Parse` operates strictly on filename facts. | Unlabeled files (`S01E05.mkv`) have null title in discovery. | **Deferred with Technical Rationale**: Raw discovery must not guess titles from arbitrary folders (e.g. `Downloads/S01E05.mkv`). Handled safely in `TvBatchAnalysisService`. |
| **Roman Numeral & Matching Polish** | Explicit weight/penalty matrix with title aliases and AniList romanized titles. | `MetadataScorer` uses Levenshtein ratio, token overlap, and year penalty against TMDB API. | Roman numeral variations (e.g. `Part II` vs `Part 2`) could score lower. | **Deferred with Technical Rationale**: Modifying core TMDB title normalization without extensive regression corpora risks unexpected matching shifts across live cinema catalogs. |
| **Custom Display Title Override** | Allows custom display title via web UI and manual metadata overrides. | Titles displayed in UI previously bound directly to canonical `Title`. | Users could not rename or provide custom titles without mutating canonical metadata. | **Implemented**: Schema v19 (`custom_display_title TEXT NULL`), domain models, repositories, projections, and UI inline editing controller. |

---

## 5. Implementation Status Breakdown

### Implemented & Verified
1. **Conservative Bare Numeric Episode Extraction**:
   - `TryExtractBareNumericEpisode` supports pure numeric stems (`05`), brackets (`[05]`, `(05)`), and explicit episode keywords (`Episode 05`, `الحلقة 05`).
   - Excludes movie sequels (`Toy Story 2`, `The Godfather Part 2`, `Avatar 2`), resolutions (`480`, `576`, `720`, `1080`, `2160`), and ambiguous numbers (`BB - 105`).
2. **Conflict Resolution & Precedence**:
   - In `MediaDiscoveryService.InferSeasonEpisode`, explicit filename markers (`SxxExx`, `1x02`) take strict priority over conflicting season folders.
3. **Custom Display Title (Movies & TV Shows)**:
   - SQLite migration v19 adds `custom_display_title TEXT NULL` to `movies` and `tv_shows`.
   - Domain models `Movie` and `TvShow` expose `CustomDisplayTitle` and `DisplayTitle => CustomDisplayTitle ?? Title`.
   - Repositories persist and query `custom_display_title`; metadata refresh (`UpdateMetadata`), Trakt synchronization, and rescans preserve custom titles.
   - Projections (`LibraryProjection`, `TvProjection`, DTOs) expose effective display titles and canonical titles across UI surfaces.
4. **Decoupled UI Interaction Controller (`TitleInteractionController`)**:
   - Configurable Windows double-click timing via injected `IDoubleClickTimeProvider` (P/Invoke `GetDoubleClickTime` with safe fallback).
   - Enforces execution on the WinUI 3 `DispatcherQueue` for all clipboard operations and UI callbacks.
   - Safe blur policy: persistence failure retains unsaved input and editing mode rather than discarding edits.
   - Strict length validation: titles exceeding 500 characters report localized errors and reject saving without silent truncation.
   - Generation counter (`_interactionGeneration`) invalidates stale queued callbacks across page deactivation and reuse.

### Deferred with Technical Rationale
1. **Parent-Folder Title Fallback in `MediaDiscoveryService.Parse`**:
   - *Rationale*: TBD Movies intentionally separates raw file discovery (`MediaDiscoveryService`) from contextual batch analysis (`TvBatchAnalysisService`). Mutating titles during raw discovery would bypass this separation and risk assigning transient directory names (e.g., `Downloads`, `Media`, `Temp`) as show titles.
2. **Punctuation & Roman Numeral Normalization in `MetadataMatchingService`**:
   - *Rationale*: TBD Movies's `MetadataScorer` mathematically evaluates Levenshtein ratio and token overlap against TMDB search results. Altering title tokens or mapping Roman numerals without an exhaustive live TMDB regression baseline risks false matches on franchise titles (e.g., *Rocky II*, *Star Wars Episode IV*).

---

## 6. Verification Summary
- **Focused Unit Tests**: 57 passed, 0 failed (including double-click cancellation, safe blur, length validation, conflict resolution, sequel negative fixtures, and persistence).
- **Applicable Broader Test Suite**: 1,004 passed, 0 failed (excluding only `WindowsTmdbCredentialStoreTests` due to sandbox Credential Manager requirements).
- **Visual Studio MSBuild Production Build**: Release x64 compilation succeeded with 0 warnings and 0 errors (`TBD Movies.UI.dll`).
