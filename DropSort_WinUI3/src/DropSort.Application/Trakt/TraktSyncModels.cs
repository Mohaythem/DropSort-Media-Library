using System;
using System.Collections.Generic;

namespace DropSort.Application.Trakt;

/// <summary>
/// Status outcome for a single Trakt resource synchronization.
/// </summary>
public sealed record TraktResourceSyncOutcome(
    string ResourceKey,
    bool Success,
    TraktCacheErrorCode? ErrorCode = null);

/// <summary>
/// Specific typed failure category for Trakt coordinator operations.
/// </summary>
public enum TraktSyncFailureKind
{
    None = 0,
    AccountMismatch,
    Unauthorized,
    RecordAccountFailed,
    InvalidResponse,
    NetworkError,
    Busy,
    Cancelled
}

/// <summary>
/// Overall result of a Trakt synchronization operation.
/// </summary>
public sealed record TraktSyncResult(
    bool Success,
    string? AccountId,
    int CompletedResources,
    int FailedResources,
    IReadOnlyList<TraktResourceSyncOutcome> ResourceOutcomes,
    string? AbortReason = null,
    TraktSyncFailureKind FailureKind = TraktSyncFailureKind.None)
{
    public static TraktSyncResult Aborted(string? accountId, string reason, TraktSyncFailureKind failureKind = TraktSyncFailureKind.None) =>
        new(false, accountId, 0, 0, Array.Empty<TraktResourceSyncOutcome>(), reason, failureKind);

    public static TraktSyncResult AccountMismatch(string? expectedAccountId, string actualAccountId) =>
        new(false, expectedAccountId, 0, 0, Array.Empty<TraktResourceSyncOutcome>(),
            $"Account ID mismatch: expected {expectedAccountId}, got {actualAccountId}. Reconnect required.",
            TraktSyncFailureKind.AccountMismatch);
}

/// <summary>
/// Public immutable UI snapshot representing Trakt synchronization and connection state.
/// </summary>
public sealed record TraktSyncState(
    TraktAuthState AuthState,
    bool IsSyncing,
    string? CurrentAccountId,
    string? LastAccountId,
    DateTimeOffset? LastSyncAttemptAt,
    DateTimeOffset? LastSyncSuccessAt,
    string? LastSyncError,
    TraktSyncFailureKind LastFailureKind = TraktSyncFailureKind.None)
{
    public static TraktSyncState Initial(TraktAuthState authState, string? lastAccountId = null) =>
        new(authState, false, authState.AccountId, lastAccountId ?? authState.AccountId, null, null, null, TraktSyncFailureKind.None);
}

/// <summary>
/// General Trakt synchronization exception.
/// </summary>
public class TraktSyncException : Exception
{
    public TraktSyncException(string message) : base(message) { }
    public TraktSyncException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Exception thrown when the remote Trakt account UUID does not match the local verified account.
/// </summary>
public sealed class TraktAccountMismatchException : TraktSyncException
{
    public TraktAccountMismatchException(string message) : base(message) { }
}
