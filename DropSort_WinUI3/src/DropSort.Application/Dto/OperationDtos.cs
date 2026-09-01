using System;
using System.Collections.Generic;
using DropSort.Domain.Core.Operations;
using DropSort.Domain.Library.Movies;

namespace DropSort.Application.Dto;

public record OrganizationPreview(string PreviewId, FileOperationPlan Plan);
public record OrganizationResult(FileOperationRecord Record);

public record OperationHistoryQuery(int Limit = 50, int Offset = 0);
public record OperationHistoryItem(
    string Id,
    OperationType Type,
    string MovieTitle,
    OperationState State,
    DateTimeOffset Timestamp,
    string? SourcePath = null,
    string? DestinationPath = null,
    int? MediaFileId = null,
    DateTimeOffset? UpdatedAt = null,
    string? ReversesOperationId = null);
public record OperationDetails(FileOperationRecord Record, string MovieTitle, string? ReversedBy);

public record UndoPreview(string PreviewId, FileOperationPlan ReversePlan);
public record UndoResult(FileOperationRecord Record);

public record RecoveryAssessment(FileOperationRecord Record, RecoverySituation Situation, string? Details);
public record RecoveryResult(FileOperationRecord Record, OperationState NewState);

public record LibraryReconciliationProgress(int CheckedFiles, int MissingFiles, int RelinkedFiles, int Errors, string? CurrentPath);
public record RelinkPreview(string PreviewId, int MediaFileId, string CandidatePath, bool MatchesIdentity);
public record RelinkResult(MediaFile MediaFile, FileOperationRecord? MoveRecord);

public record LibraryHealthProgress(
    LibraryReconciliationProgress FileProgress, 
    int TotalMovies, 
    int CheckedMovies, 
    int CompleteMovies, 
    int IssuesFound, 
    int Repaired, 
    int NeedsReview, 
    int ProviderUnavailable,
    IReadOnlyList<MetadataHealthItem> CurrentIssues,
    IReadOnlyList<int> RecentlyRepaired);

public enum MetadataHealthStatus { Complete, Incomplete, MissingPoster, NeedsMatch, ProviderValueUnavailable, ProviderUnavailable }
public enum MetadataHealthIssue { Overview, Runtime, Genres, Year, Poster, NeedsMatch }
public enum MetadataProviderError { Authentication, RateLimit, Unavailable, InvalidResponse }

public record MetadataHealthItem(
    int MovieId, 
    string Title, 
    MetadataHealthStatus Status, 
    IReadOnlyList<MetadataHealthIssue> MissingFields, 
    IReadOnlyList<MetadataHealthIssue> RepairedFields, 
    MetadataProviderError? ProviderError);
