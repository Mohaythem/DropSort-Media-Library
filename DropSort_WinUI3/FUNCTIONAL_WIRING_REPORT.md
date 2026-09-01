# DropSort WinUI 3 — Functional Wiring Report

Date: 2026-09-02 · Branch `codex` · HEAD `e96a9b1` (working tree dirty, see *Files changed*)

## Verdict

**PASS** for every flow the current backend supports. The shell is no longer a demo: Library, Movie
Details, My Lists, Home, Add Media, Check Library, Operations Log and Settings all read and write the
real SQLite catalog through the `DropSort.Application` contracts. What remains unwired is unwired
because the contract does not exist in this build (TV seasons/episodes, TMDB client, library
snapshots) — each is listed below with its reason.

## Method

Every visible control was traced UI → handler → application service → repository/filesystem → result.
A handler that existed but did nothing counted as broken. Verification was done by driving the real
app (window-scoped `PrintWindow` captures) against real media files in `%TEMP%\dsmedia`, and by
reading the resulting rows straight out of `%LOCALAPPDATA%\DropSort\library.db`.

## What was wired

### Composition root — new

`src/DropSort.UI/Services/AppServices.cs` builds the V1 stack once: data folder
`%LOCALAPPDATA%\DropSort`, `DatabaseMigrator.Migrate()`, `CatalogUnitOfWorkFactory`,
`PersonalLibraryRepository`, `SettingsRepository`, `LibraryMaintenanceRepository`,
`FileOperationStore` + `FileOperationCoordinator`, `MediaDiscoveryService`, `AvailabilityInspector`,
and the seven `IUiActions` facades. Initialization is lazy and never throws into a view; a failure
sets `InitializationError` and the pages fall back to their error/empty states.

Two support files: `CatalogRepositoryAdapters.cs` (one short-lived unit of work per repository call,
so no SQLite connection is shared between the UI thread and a background scan) and
`LibraryProjection.cs` (application DTOs → the `MovieRecord` the existing templates already bind to,
so no XAML was redesigned). `AppSettingsStore.cs` holds the UI's own configuration — scan folders and
the user's custom list — in the same settings table.

### Add Media — real end to end

Browse opens a `FolderPicker` (owner-initialized with the shell HWND) and remembers the root; Scan
runs `IImportUiActions.PrepareImportReview` on a worker thread with live progress and a working
Cancel; the review list shows real parsed titles, years and quality; Add Selected registers every
candidate through `RegisterMovieImport` (the offline path — no metadata provider needed) and reports
how many were added; Start Over clears the result. Registration is per file path and idempotent, so
re-scanning the same folder does not duplicate a movie.

**Verified:** scanned 4 files → 4 candidates with correct titles/years/quality → Add Selected → 4
movies and 4 media files in the catalog → Library and Home immediately showed them.

### Check Library — real, manual, cancellable

`IReconciliationUiActions.CheckLibrary` runs on a worker thread: every registered file is stat'd, a
file that disappeared is marked missing (and **stays registered**), and each movie's metadata is
graded. The page now starts **Idle** with a *Start Check* button — the old build opened in the
"Check complete" state, which was a fabricated result. Cancel stops the pass between files;
cancelled, failed and completed are distinct states. Nothing starts a check but this button: there is
no scan at startup or on navigation.

**Verified (healthy):** 4 present files → Passed 4 / Needs attention 4 / 8 items, the four
"Metadata needs review" rows being honest (no TMDB, so no overview, runtime, genres or rating).
**Verified (missing):** deleted one file on disk → re-ran → "Perfect Days (2023) 720p.mkv — Movie
file is missing" with an *Open Folder* action, Passed 4→3, the movie still registered, `media_files`
row status `MISSING`.

### Library / Movies

Cards come from `ILibraryUiActions.ListMovies()`. Availability filter, sort (Added/Title/Year) and
in-page search all run against those rows; a catalog failure shows the page's error state and Retry
re-reads. A card click re-reads the movie by id, so details never show a stale card projection.

### Movie Details — every action persists

Like / Exclude / Clear preference, Add to Watchlist, Mark Watched, Mark on Date and per-row watch
removal all go through `IPersonalLibraryUiActions` and then redraw from the snapshot the store
returned. Play and Open Folder were **dead controls with no handler at all** — they now shell-execute
the registered player and open Explorer with the file selected, and report a missing file instead of
doing nothing. Organize File runs the journalled preview → confirm through
`IOrganizationUiActions`, and states plainly that a movies root must be configured first when it is
not (DropSort only ever moves into an approved root).

**Verified:** liked + watchlisted + marked watched → navigated away → re-opened: all three states
came back from the database, watch history listed with a working remove button.

### My Lists

Watchlist, Favorites and Watch Later are the real personal-library sections (`Watchlist`, `Liked`,
`ReadyToWatch`). New List / Rename / Delete now actually create, rename and delete the user's list and
the name persists in the settings table; the tab appears and disappears with it.

**Verified:** Watchlist showed exactly the one movie watchlisted on the details page.

### Settings

Theme and language are persisted through `ISettingsUiActions` and restored at startup. Movie/TV folder
rows open a real picker and show the configured path. The TMDB token goes through
`ApplyTmdbSessionToken` / `ClearTmdbSessionToken`. Clear watch history removes every watch event
movie by movie and keeps preferences. Clear library data calls `ClearLibraryData()` and reports the
counts — index only, never a media file.

**Verified:** Light and Arabic survived a restart (`ui_theme=light`, `ui_language=ar` in the settings
table), then Slate + English were restored the same way.

### Operations Log

Reads the real journal via `ListOperationHistory`, newest first, with the Success / Needs attention
filters mapped onto `OperationState`. Save now writes through `SaveOperationHistory` (the product's
export format) instead of this page's own text. With no move or rename performed yet, the page
correctly shows **0 entries** and disables Copy/Save rather than inventing rows.

## Broken controls found, root cause, repair

| Control / behaviour | Root cause | Repair |
| --- | --- | --- |
| Every movie surface | UI never referenced the Application layer at all — 100% `DemoData` | Composition root + projection; `DemoData` now holds only the TV sample |
| `PlayButton`, `OpenFolderButton`, `OrganizeButton` (Movie Details) | No `Click` handler in the XAML | Handlers added: shell-execute, Explorer `/select`, journalled organize |
| `TmdbSearchButton` (Add Media) | No `Click` handler | Wired to `ManualMovieSearch`, reports "not configured" honestly |
| `BrowseButton`, `AddFilesButton`, `AddSelectedButton`, `AddEpisodesButton` | Empty handlers | Real pickers, real registration, honest refusal for episodes |
| `ScanButton` | Flipped a state enum over demo rows | Real discovery on a worker thread with progress + Cancel |
| Check Library initial state | Field initialized to `Complete` | Starts `Idle`; `Cancelled` / `Failed` states added |
| `IssueAction` (Check Library) | Empty handler | Opens the folder a missing file lived in |
| Preference / watchlist / watch history (Movie Details) | Mutated local view state only | Written through the personal library and redrawn from the store |
| New List / Rename / Delete (My Lists) | Dialog shown, result discarded | Persisted in the settings table |
| Clear watch history / Clear library data | Confirmation shown, nothing done | Real deletions with counts reported; media files untouched |
| Theme / language | In-memory only, lost on restart | Persisted and restored |
| Stored theme reset to Slate on every start | ComboBox raises `SelectionChanged` for its initial item before the sync flag is set | `_isSyncing` starts `true`; selectors ignore events until the page has been shown |
| Stored language reset to English on start | Same class of bug on a page that was never displayed | Same guard (`_hasBeenShown`) |
| Whole shell showed English text after starting in Arabic | Pages are field initializers, constructed before the stored language was restored | `LocalizeViews()` runs once after restore |
| Watch-history dates rendered in Arabic under an English UI | Formatting used `CultureInfo.CurrentCulture`; this machine's region is Arabic | `LocalizationService.Culture` follows the UI language; `Digits()` also folds Arabic-Indic digits to ASCII |
| Review list titles read "Dune Part Two (" | Parser cut the year out and left the bracket | Title trimmed of wrapping punctuation |
| Quality column always empty | Parser never extracted resolution/source/codec | Extracted from the release tags; stored as the file's verified facts |
| Check Library progress row frozen for the whole scan | `CheckLibrary` passed `null` progress into the file stage | File-stage progress forwarded (minimal Application fix) |
| Watch history unreachable through the UI contract | `IPersonalLibraryUiActions` had `RemoveWatchEvent(eventId)` but no way to read event ids | `ListWatchEvents(movieId)` added to the contract and service |

## Backend services now connected

`LibraryService` (as `ILibraryUiActions` + `IPersonalLibraryUiActions`), `ImportService`,
`ReconciliationService`, `OperationHistoryService`, `SettingsService`, `OrganizationService`,
`MediaDiscoveryService`, `AvailabilityInspector`, `FileOperationCoordinator` + `FileOperationStore`,
`DatabaseMigrator`, and the SQLite repositories (movies, media files, personal library, settings,
maintenance).

## Intentionally not wired, with the reason

| Control | Why | Behaviour today |
| --- | --- | --- |
| TV Shows tab, TV Show Details, seasons, episodes, Continue watching, TV stat tiles | The catalog has **no season or episode tables** and no TV repository or service exists. Building one is a new backend, not wiring | Renders the bundled sample in `DemoData`; episode actions inert; Add Media refuses to register episodes and says why |
| Add Media → Add N Episodes | Same | States that episodes cannot be registered yet; the picked files are not touched |
| TMDB Find Match / Search / Test Connection | No TMDB client in this build; the metadata provider is `UnconfiguredMetadataProvider`, which answers with no candidates instead of inventing them | Search reports "no results" / "add a token"; Test Connection states no client exists |
| Export / Import library | No snapshot contract in `DropSort.Application` | States that snapshots are not part of this build; writes and reads nothing |
| My Lists → items inside a user list | No membership table for custom lists; and there is no designed "add to list" control on Movie Details to feed one | The list exists, persists and is renameable; its empty state explains the gap |
| Movie Details → Mark show watched / Open show folder (TV) | TV backend | Disabled menu items, as designed |
| Relink a missing file | `PrepareMediaRelink` exists but needs a candidate-path picker flow that the design does not include | Missing files are reported with Open Folder |
| Poster artwork | No poster service or cache implementation | Every surface draws the 2:3 placeholder in the identical viewport |

## Safety

Nothing in this pass weakened a filesystem guarantee. Registration never moves a file; Organize File
goes through `IOrganizationUiActions` → `FileOperationCoordinator`, so the approved-root check,
collision protection, identity verification and the journal all still gate it. A missing file is
marked missing and stays registered. Clear library data clears catalog rows only — the service does
not touch media. Discovery still refuses reparse points. The only destructive UI actions (clear watch
history, clear library data) are behind the existing confirmation dialogs and now report what they
removed.

Two notes for the record:

- The WinUI app uses its own database, `%LOCALAPPDATA%\DropSort\library.db`. The Python V1 database
  (`dropsort.db`) in the same folder is **not** opened, read or migrated. Importing V1 data is a
  separate, explicitly-scoped job; running this schema's migrations against the user's V1 file could
  damage it.
- Verification left four registered movies in the catalog pointing at `%TEMP%\dsmedia`, one of them
  deliberately missing. Clear library data removes them if a clean slate is wanted.

## Build and tests

- `bash "$TEMP/dsprobe/build.sh"` (msbuild; `dotnet build` cannot build DropSort.UI because of the
  MrtCore PriGen task): **succeeded**, no warnings surfaced.
- `dotnet test tests/DropSort.Tests/DropSort.Tests.csproj`: **88 passed, 0 failed** (was 74 at the
  start of this pass).

New focused tests for the repaired contracts:

- `FileSystem/DiscoveryParsingTests.cs` — bracketed year not left on the title; release tags become
  the file's verified facts; a name without tags reports no facts; an episode file is classified TV.
- `Application/UiFacingContractTests.cs` — watch events listed newest-first with usable ids; Check
  Library reports progress while it walks the files.
- `UI/UiSourceContractTests.cs` — two guards: no movie surface may read a demo table again, and every
  page that writes stored state must surface a failure instead of swallowing it.

## Files changed

Modified: `Application/Contracts/IUiActions.cs`, `Application/Services/LibraryService.cs`,
`Application/Services/ReconciliationService.cs`, `FileSystem/Discovery/MediaDiscoveryService.cs`,
`UI/MainWindow.xaml(.cs)`, `UI/Models/DemoData.cs`, `UI/Models/DemoModels.cs`,
`UI/Resources/ThemeResources.xaml`, `UI/Services/LocalizationService.cs`,
`UI/Services/ThemeService.cs`, and the seven page code-behinds (+ three page XAMLs for the missing
`Click` handlers and one `Tag` binding), `tests/UI/UiSourceContractTests.cs`.

Added: `UI/Services/AppServices.cs`, `UI/Services/AppSettingsStore.cs`,
`UI/Services/CatalogRepositoryAdapters.cs`, `UI/Services/LibraryProjection.cs`,
`tests/Application/UiFacingContractTests.cs`, `tests/FileSystem/DiscoveryParsingTests.cs`.

Not committed — the working tree holds the change set for review.

## Recommended next

1. TV backend: seasons/episodes tables, repository and service, then wire the existing TV UI to it.
   This is the single largest remaining gap and the only one that leaves sample data on screen.
2. TMDB client behind `IMetadataProvider` + poster cache behind `IPosterService`. That fills every
   "Metadata needs review" issue, the Quality/overview/genre fields and all artwork at once.
3. Relink flow for missing files (the service half already exists).
4. Library snapshot export/import contract.
5. A "add to list" affordance on Movie Details plus a membership table, to make user lists useful.



