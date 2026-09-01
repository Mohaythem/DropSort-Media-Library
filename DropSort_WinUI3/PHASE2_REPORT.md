# DropSort .NET Rewrite — Phase 2 Report

**Date:** 2026-08-31  
**Status:** ✅ Phase 2 Complete  

---

## 1. Domain Entities & Enums Ported

The entire core domain model from V1 Python has been translated into pure C# 10 records and enums within the `DropSort.Domain` project. The port strictly adheres to the original boundaries and enforces the same invariants.

### Enums (14 total)
All enums explicitly mapped:
- `Library.Movies.MediaFileStatus` (Present, Missing)
- `Library.Movies.MetadataStatus` (Pending, Ready, Failed, NeedsMatch)
- `Library.Personal.PersonalPreference` (NoOpinion, Liked, Blacklisted)
- `Library.Personal.PersonalLibrarySection` (Watchlist, ReadyToWatch, Liked, Blacklisted)
- `Library.Availability.AvailabilityInspectionStatus` (Present, Missing, Error)
- `Media.Parser.MediaType` (Movie, TvEpisode, Unknown)
- `Media.Matcher.MatchStatus` (Matched, ReviewRequired, NoMatch)
- `Media.Matcher.MatchReason` (15 values exactly matching Python)
- `Media.Discovery.DiscoveryClassification` (MovieCandidate, TvEpisodeSkipped, UnknownMedia, Error)
- `Media.Discovery.DiscoveryErrorCode` (10 values representing filesystem/parsing failures)
- `Core.Operations.OperationType` (Move, Rename)
- `Core.Operations.OperationState` (7-state journal sequence)
- `Core.Operations.RecoverySituation` (7 states for disaster recovery)
- `Configuration.UiLanguage` / `Configuration.UiTheme`

### Entities & Value Objects (25 total)
Translated as strictly immutable `record` types with rigorous constructor validation matching Python's `__post_init__`:
- **Library.Movies**: `MovieCatalogData`, `Movie`, `VerifiedMediaFileFacts`, `MediaFile`, `MediaFileStatusUpdate`
- **Library.Personal**: `PersonalMovieState`, `WatchEvent`, `ReadyToWatchMovie`, `PersonalMovieSummary`
- **Library.Operations**: `OperationJournalSnapshot`
- **Library.Availability**: `MediaFileIdentity`, `AvailabilityInspection`
- **Metadata.Contracts**: `MovieSearchQuery`, `MovieCandidate`, `MovieMetadata`
- **Media.Discovery**: `DiscoveryProgress`, `DiscoveryIssue`, `DiscoveredMedia`
- **Media.Matcher**: `CandidateScore`, `MatchDecision`
- **Media.Parser**: `ParsedMedia`
- **Core.Operations**: `FileOperationPlan`, `OperationUpdate`, `FileOperationRecord`, `PreparedTransfer`, `RecoveryInspection`
- **Core.Safety**: `SourceIdentity`

---

## 2. Invariants & Validation Contracts

All business rules proven by the V1 Python tests are preserved:
- **DateTime Handling**: `DateTimeOffset` is used exclusively to guarantee timezone-aware timestamps, equivalent to Python's `datetime(..., tzinfo=timezone.utc)`.
- **Identity Types**: Movie/MediaFile/Event IDs use `int` (matching SQLite `INTEGER PK`). FileOperation IDs use `string` (UUID). Source/Destination identity facts (size, mtime, dev, ino) use `long` to match `stat()` results without overflow.
- **Catalog Rules**: `provider` and `externalId` must be coupled. MetadataStatus.Ready strictly requires a provider. Ratings must be finite between 0.0-10.0. Text fields are trimmed and reject whitespace-only.
- **File System Rules**: Paths must be absolute. File sizes must be non-negative. Extensions must include a leading dot.
- **Discovery Rules**: A `DiscoveredMedia` of type Error must contain a `DiscoveryIssue` and cannot contain file facts; vice versa for non-error items.
- **Matcher Rules**: Confidence scores must be between 0.0 and 1.0. A `Matched` decision mandates a candidate. A `ReviewRequired` decision mandates that the candidate is top-ranked.

---

## 3. Verification & Testing

- **Mapping Artifact**: `domain_mapping.md` provides a 1:1 trace matrix of Python structures to C# files.
- **Tests Ported**: 4 test classes were created in `tests/DropSort.Tests/Domain/`:
  - `CatalogModelsTests.cs` (Movie/MediaFile rules)
  - `MatcherModelsTests.cs` (MatchDecision invariants)
  - `DiscoveryModelsTests.cs` (DiscoveredMedia coherence)
  - `OperationJournalModelsTests.cs` (Snapshot validation)
- **Results**: 
  - `dotnet test` completed with **0 failures** (40 domain tests passing).

---

## 4. Dependencies & Vulnerability Investigation

During Phase 1, a NuGet warning (`NU1903`) was flagged for `SQLitePCLRaw.lib.e_sqlite3` version 2.1.11 having a known vulnerability (`GHSA-2m69-gcr7-jv3q`).
- **Source**: This was transitively introduced by the preview `Microsoft.Data.Sqlite` 10.0 SDK package.
- **Resolution**: I explicitly pinned `SQLitePCLRaw.lib.e_sqlite3` to version `2.1.12` directly in the Infrastructure, UI, and Test projects.
- **Outcome**: The warning is completely resolved. `dotnet build` now succeeds with **0 warnings and 0 errors**.

---

## 5. Next Steps

Phase 2 is fully complete. The Domain layer is solid, isolated, and verified against the V1 business rules. 

Phase 3 is ready to commence, which involves defining the Application Interfaces (Repositories, File Engine contracts) and implementing them in the Infrastructure/FileSystem projects.
