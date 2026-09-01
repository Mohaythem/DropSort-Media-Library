# Domain Parity Mapping (Python to C#)

## Enums
| Python Enum | C# Enum | Location in Domain |
|---|---|---|
| `MediaFileStatus` | `MediaFileStatus` | `Library/Movies/MediaFileStatus.cs` |
| `MetadataStatus` | `MetadataStatus` | `Library/Movies/MetadataStatus.cs` |
| `OperationType` | `OperationType` | `Core/Operations/OperationType.cs` |
| `OperationState` | `OperationState` | `Core/Operations/OperationState.cs` |
| `RecoverySituation` | `RecoverySituation` | `Core/Operations/RecoverySituation.cs` |
| `MediaType` | `MediaType` | `Media/Parser/MediaType.cs` |
| `MatchStatus` | `MatchStatus` | `Media/Matcher/MatchStatus.cs` |
| `MatchReason` | `MatchReason` | `Media/Matcher/MatchReason.cs` |
| `PersonalPreference` | `PersonalPreference` | `Library/Personal/PersonalPreference.cs` |
| `PersonalLibrarySection` | `PersonalLibrarySection` | `Library/Personal/PersonalLibrarySection.cs` |
| `DiscoveryClassification` | `DiscoveryClassification` | `Media/Discovery/DiscoveryClassification.cs` |
| `DiscoveryErrorCode` | `DiscoveryErrorCode` | `Media/Discovery/DiscoveryErrorCode.cs` |
| `UiLanguage` | `UiLanguage` | `Configuration/UiLanguage.cs` |
| `UiTheme` | `UiTheme` | `Configuration/UiTheme.cs` |

## Value Objects & Entities
| Python Model | C# Model | Note |
|---|---|---|
| `MovieCatalogData` | `record MovieCatalogData` | Validates provider/id coupling, ratings, text fields |
| `Movie` | `record Movie` | Validates ID and aware timestamps (C# `DateTimeOffset`) |
| `VerifiedMediaFileFacts` | `record VerifiedMediaFileFacts` | Validates path is absolute, size non-negative |
| `MediaFile` | `record MediaFile` | Same |
| `MediaFileStatusUpdate` | `record MediaFileStatusUpdate` | Same |
| `ParsedMedia` | `record ParsedMedia` | Same |
| `DiscoveryProgress` | `record DiscoveryProgress` | Validates monotonic counters |
| `DiscoveryIssue` | `record DiscoveryIssue` | Same |
| `DiscoveredMedia` | `record DiscoveredMedia` | Enforces Error classification requires Issue, etc. |
| `CandidateScore` | `record CandidateScore` | Validates confidence bounds (0-1) |
| `MatchDecision` | `record MatchDecision` | Validates match consistency rules |
| `MovieSearchQuery` | `record MovieSearchQuery` | Same |
| `MovieCandidate` | `record MovieCandidate` | Same |
| `MovieMetadata` | `record MovieMetadata` | Same |
| `MediaFileIdentity` | `record MediaFileIdentity` | Same |
| `AvailabilityInspection` | `record AvailabilityInspection` | Same |
| `AvailabilityInspectionStatus` | `enum AvailabilityInspectionStatus` | PRESENT/MISSING/ERROR |
| `PersonalMovieState` | `record PersonalMovieState` | Same |
| `WatchEvent` | `record WatchEvent` | Same |
| `ReadyToWatchMovie` | `record ReadyToWatchMovie` | Same |
| `PersonalMovieSummary` | `record PersonalMovieSummary` | Same |
| `FileOperationPlan` | `record FileOperationPlan` | Same |
| `OperationUpdate` | `record OperationUpdate` | Same |
| `FileOperationRecord` | `record FileOperationRecord` | Same |
| `PreparedTransfer` | `record PreparedTransfer` | Same |
| `RecoveryInspection` | `record RecoveryInspection` | Same |
| `OperationJournalSnapshot` | `record OperationJournalSnapshot` | Same |
| `SourceIdentity` | `record SourceIdentity` | Moved from FileSystem to Core/Safety Domain |

## Data Types Mapping
| Python | C# |
|---|---|
| `str` | `string` |
| `int` | `int` or `long` (for `file_size`, `mtime_ns`) |
| `float` | `double` (for `rating`, `confidence`) |
| `datetime` (aware) | `DateTimeOffset` |
| `pathlib.Path` | `string` (C# doesn't have a distinct Path class for properties, though we could use `FileInfo`, `string` is standard) |
| `tuple[T, ...]` | `IReadOnlyList<T>` (typically implemented with `ImmutableArray<T>`) |
