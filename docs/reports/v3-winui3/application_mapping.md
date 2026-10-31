# Application Parity Mapping (Python to C#)

## UI Contracts (Application Facade)
| Python Protocol | C# Interface |
|---|---|
| `LibraryUiActions` | `ILibraryUiActions` |
| `PersonalLibraryUiActions` | `IPersonalLibraryUiActions` |
| `ImportUiActions` | `IImportUiActions` |
| `SettingsUiActions` | `ISettingsUiActions` |
| `OrganizationUiActions` | `IOrganizationUiActions` |
| `OperationHistoryUiActions` | `IOperationHistoryUiActions` |
| `ReconciliationUiActions` | `IReconciliationUiActions` |

## Repositories (Infrastructure Contracts)
| Python Repository (Implicit/Duck-typed) | C# Interface |
|---|---|
| `movies` repository | `IMovieRepository` |
| `media_files` repository | `IMediaFileRepository` |
| `file_operations` / `operation_store` | `IFileOperationStore` |
| `metadata_cache` repository | `IMetadataCacheRepository` |
| `settings` repository | `ISettingsRepository` |
| `personal_library` repository | `IPersonalLibraryRepository` |
| `catalog_uow` (Unit of Work) | `ICatalogUnitOfWork` (for transactional commits) |

## Cross-Layer Contracts
| Python Contract | C# Interface | Purpose |
|---|---|---|
| `MetadataProvider` / `TMDB Client` | `IMetadataProvider` | External TMDB searches and metadata fetch |
| `PosterActions` / `PosterSource` | `IPosterService` | Poster fetching and caching |
| `SafeTransferEngine` | `ISafeTransferEngine` | Core file operations (hardlink/copy/move) |
| `PathPolicy` | `IPathPolicy` | Filesystem safety bounds |

## DTOs (Mapped to C# Records)
- **Library**: `MovieListItem`, `MovieDetails`
- **Import**: `ImportReviewSession`, `ImportReviewProgress`, `ConfirmMovieImportCommand`, `MovieFileIngestionResult`, `ManualMovieSearchResult`
- **History**: `OperationHistoryItem`, `OperationHistoryQuery`, `OperationDetails`, `UndoPreview`, `UndoResult`, `RecoveryAssessment`, `RecoveryResult`
- **Organization**: `OrganizationPreview`, `OrganizationResult`
- **Reconciliation/Health**: `LibraryHealthProgress`, `LibraryReconciliationProgress`, `RelinkPreview`, `RelinkResult`
- **Settings/Personal**: `PersonalMovieSnapshot`, `ClearLibraryDataResult`

## Use Cases (Implementation in C# classes)
Python's standalone functions (e.g., `list_movies.py`, `confirm_movie_import.py`) will be grouped into service classes implementing the UI action interfaces.
- `LibraryService` implements `ILibraryUiActions`
- `ImportService` implements `IImportUiActions`
- `PersonalLibraryService` implements `IPersonalLibraryUiActions`
- `OrganizationService` implements `IOrganizationUiActions`
- `OperationHistoryService` implements `IOperationHistoryUiActions`
- `ReconciliationService` implements `IReconciliationUiActions`
- `SettingsService` implements `ISettingsUiActions`
