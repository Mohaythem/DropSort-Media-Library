using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DropSort.Application.Repositories;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("DropSort.Tests")]


namespace DropSort.Application.Trakt;

/// <summary>
/// Production UI sync coordinator for Trakt integrations.
/// Manages device authorization, background sync, offline cache reading,
/// thread-safe generation checks, immediate network cancellation on disconnect,
/// active session isolation, and safe error tracking without touching local catalog rows.
/// </summary>
public sealed class TraktSyncService : ITraktUiActions, IDisposable
{
    private const string LastAccountIdSettingsKey = "trakt_last_account_id";

    private readonly ITraktAuthService _authService;
    private readonly ITraktDataClient _dataClient;
    private readonly ITraktCacheRepository _cacheRepository;
    private readonly TraktAuthConfig _config;
    private readonly ISettingsRepository? _settingsRepository;
    private readonly TimeProvider _timeProvider;

    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _ctsLock = new();
    private CancellationTokenSource? _activeNetworkCts;
    private long _generation;

    private string? _currentAccountId;
    private string? _lastAccountId;
    private volatile TraktSyncState _syncState;
    private volatile bool _disposed;

    // Minimum internal test seams for deterministic verification of gate boundary races
    internal Func<Task>? BeforeSyncRegistrationAsync { get; set; }
    internal Func<Task>? BeforeCompleteAuthRegistrationAsync { get; set; }
    internal Func<Task>? BeforeBeginAuthRegistrationAsync { get; set; }
    internal Action? OnCancelActiveNetworkFinished { get; set; }
    internal Action? AfterSyncStateRead { get; set; }

    public TraktSyncService(
        ITraktAuthService authService,
        ITraktDataClient dataClient,
        ITraktCacheRepository cacheRepository,
        TraktAuthConfig config,
        ISettingsRepository? settingsRepository = null,
        TimeProvider? timeProvider = null)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _dataClient = dataClient ?? throw new ArgumentNullException(nameof(dataClient));
        _cacheRepository = cacheRepository ?? throw new ArgumentNullException(nameof(cacheRepository));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _settingsRepository = settingsRepository;
        _timeProvider = timeProvider ?? TimeProvider.System;

        var authState = _authService.GetAuthState();
        _currentAccountId = authState.IsConnected ? authState.AccountId : null;

        var persistedLastAccount = _settingsRepository?.Get(LastAccountIdSettingsKey);
        _lastAccountId = _currentAccountId ?? (string.IsNullOrWhiteSpace(persistedLastAccount) ? null : persistedLastAccount.Trim());

        _syncState = TraktSyncState.Initial(authState, _lastAccountId);
    }

    /// <inheritdoc />
    public TraktSyncState GetSyncState()
    {
        var authState = _authService.GetAuthState();
        var current = _syncState;
        AfterSyncStateRead?.Invoke();
        // Readers must never republish a snapshot captured before a writer completed.
        return current.AuthState == authState ? current : current with { AuthState = authState };
    }

    /// <inheritdoc />
    public async Task<TraktDeviceCode> BeginAuthAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Invalidate active operations before acquiring gate
        var currentGen = CancelActiveNetwork();

        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (BeforeBeginAuthRegistrationAsync != null)
            {
                await BeforeBeginAuthRegistrationAsync();
            }

            CancellationTokenSource linkedCts;
            lock (_ctsLock)
            {
                if (_disposed || Interlocked.Read(ref _generation) != currentGen)
                {
                    throw new OperationCanceledException("BeginAuth superseded or cancelled before registration.");
                }

                linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _activeNetworkCts = linkedCts;
            }

            try
            {
                var opToken = linkedCts.Token;
                opToken.ThrowIfCancellationRequested();

                var code = await _authService.BeginDeviceAuthAsync(opToken);

                // Recheck token and generation before publishing state and returning code
                opToken.ThrowIfCancellationRequested();
                if (Interlocked.Read(ref _generation) != currentGen)
                {
                    throw new OperationCanceledException("BeginAuth superseded or disconnected before completion.");
                }

                UpdateState(auth: _authService.GetAuthState(), isSyncing: false);
                return code;
            }
            finally
            {
                lock (_ctsLock)
                {
                    if (ReferenceEquals(_activeNetworkCts, linkedCts))
                    {
                        _activeNetworkCts = null;
                    }
                }
                linkedCts.Dispose();
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<TraktAuthState> CompleteAuthAsync(TraktDeviceCode deviceCode, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deviceCode);
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Invalidate active operations before acquiring gate
        var currentGen = CancelActiveNetwork();

        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (BeforeCompleteAuthRegistrationAsync != null)
            {
                await BeforeCompleteAuthRegistrationAsync();
            }

            // Register linkedCts ONLY after acquiring operation gate, under _ctsLock with generation recheck
            CancellationTokenSource linkedCts;
            lock (_ctsLock)
            {
                if (_disposed || Interlocked.Read(ref _generation) != currentGen)
                {
                    throw new OperationCanceledException("CompleteAuth superseded or cancelled before registration.");
                }

                linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _activeNetworkCts = linkedCts;
            }

            try
            {
                var opToken = linkedCts.Token;
                opToken.ThrowIfCancellationRequested();

                var authState = await _authService.CompleteDeviceAuthAsync(deviceCode, opToken);

                // Recheck token and generation before writing settings or publishing state
                opToken.ThrowIfCancellationRequested();
                if (Interlocked.Read(ref _generation) != currentGen)
                {
                    throw new OperationCanceledException("CompleteAuth superseded or disconnected before state publication.");
                }

                _currentAccountId = authState.AccountId;
                if (!string.IsNullOrWhiteSpace(_currentAccountId))
                {
                    _lastAccountId = _currentAccountId;
                    _settingsRepository?.Set(LastAccountIdSettingsKey, _currentAccountId, _timeProvider.GetUtcNow());
                }

                UpdateState(auth: authState, isSyncing: false, clearError: true);
                return authState;
            }
            finally
            {
                lock (_ctsLock)
                {
                    if (ReferenceEquals(_activeNetworkCts, linkedCts))
                    {
                        _activeNetworkCts = null;
                    }
                }
                linkedCts.Dispose();
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        // 1. Immediately invalidate generation and capture CTS atomically under _ctsLock, then cancel outside lock
        CancelActiveNetwork();

        // 2. Wait for gate
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            // 3. Disconnect credentials in auth service
            await _authService.DisconnectAsync(cancellationToken);

            // 4. Update in-memory state:
            // Clear current account connection, but retain explicit last account ID for offline cache browsing.
            _currentAccountId = null;
            UpdateState(auth: _authService.GetAuthState(), isSyncing: false, clearError: true);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<TraktSyncResult> SyncAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var authState = _authService.GetAuthState();
        if (!authState.IsConnected || authState.RequiresReconnect)
        {
            UpdateState(isSyncing: false, error: "Trakt account is not connected or requires reconnection.", failureKind: TraktSyncFailureKind.Unauthorized);
            return TraktSyncResult.Aborted(null, "Trakt account is not connected.", TraktSyncFailureKind.Unauthorized);
        }

        // Blocker 7: Reject busy if already syncing or gate is held; do not queue unlimited duplicates
        if (_syncState.IsSyncing)
        {
            return TraktSyncResult.Aborted(_currentAccountId, "Sync is already in progress.", TraktSyncFailureKind.Busy);
        }

        // Capture generation before waiting for or acquiring operation gate
        var currentGen = Interlocked.Read(ref _generation);

        if (!await _operationGate.WaitAsync(0, cancellationToken))
        {
            return TraktSyncResult.Aborted(_currentAccountId, "Coordinator is busy with another operation.", TraktSyncFailureKind.Busy);
        }

        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (BeforeSyncRegistrationAsync != null)
            {
                await BeforeSyncRegistrationAsync();
            }

            // Blocker 3: Active operation owns linkedCts creation, registration, and disposal.
            // Atomic operation-registration/invalidation boundary under _ctsLock:
            // Recheck generation captured before gate to prevent Disconnect race before CTS installation.
            CancellationTokenSource linkedCts;
            lock (_ctsLock)
            {
                if (_disposed || Interlocked.Read(ref _generation) != currentGen)
                {
                    throw new OperationCanceledException("Sync superseded or disconnected before registration.");
                }

                linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _activeNetworkCts = linkedCts;
            }

            try
            {
                var opToken = linkedCts.Token;
                opToken.ThrowIfCancellationRequested();
                if (Interlocked.Read(ref _generation) != currentGen)
                {
                    throw new OperationCanceledException("Sync superseded before execution.");
                }

                var startTime = _timeProvider.GetUtcNow();
                UpdateState(isSyncing: true, lastAttempt: startTime, clearError: true);

                // Step 1: Obtain users/settings UUID first without holding any database lock
                // Blocker 1: FetchWithAuthRetryAsync returns exact token used with account response
                JsonElement accountJson;
                string boundToken;
                try
                {
                    (accountJson, boundToken) = await FetchWithAuthRetryAsync(
                        token => _dataClient.GetAccountAsync(_config.ClientId, token, opToken),
                        opToken);
                }
                catch (Exception ex) when (
                    ex is TraktAuthException ||
                    ex is TraktInvalidGrantException ||
                    (ex is TraktClientException tex && tex.FailureKind == TraktFailureKind.Unauthorized))
                {
                    // Blocker 9: Second 401 or auth failure aborts sync with sanitized error
                    var sanitized = "Trakt authorization expired or rejected. Reconnection required.";
                    UpdateState(isSyncing: false, error: sanitized, failureKind: TraktSyncFailureKind.Unauthorized);
                    return TraktSyncResult.Aborted(_currentAccountId ?? authState.AccountId, sanitized, TraktSyncFailureKind.Unauthorized);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    UpdateState(isSyncing: false, error: "Failed to fetch account profile: " + ex.Message, failureKind: TraktSyncFailureKind.NetworkError);
                    return TraktSyncResult.Aborted(_currentAccountId, "Account profile fetch failed.", TraktSyncFailureKind.NetworkError);
                }

                var profile = TraktDataProjections.ParseAccountProfile(accountJson);
                var uuid = profile.Uuid;
                if (string.IsNullOrWhiteSpace(uuid))
                {
                    UpdateState(isSyncing: false, error: "Invalid account UUID returned from Trakt.", failureKind: TraktSyncFailureKind.InvalidResponse);
                    return TraktSyncResult.Aborted(_currentAccountId, "Invalid account UUID.", TraktSyncFailureKind.InvalidResponse);
                }

                opToken.ThrowIfCancellationRequested();
                if (Interlocked.Read(ref _generation) != currentGen)
                {
                    throw new OperationCanceledException("Sync superseded after account verification.");
                }

                // Step 2: Prevent silent account change (Blocker 11: fail mismatch with typed fail state)
                authState = _authService.GetAuthState();
                var knownAccountId = _currentAccountId ?? authState.AccountId;
                if (knownAccountId != null && !string.Equals(knownAccountId, uuid, StringComparison.Ordinal))
                {
                    UpdateState(
                        isSyncing: false,
                        error: $"Account ID mismatch: expected {knownAccountId}, got {uuid}. Reconnect required.",
                        failureKind: TraktSyncFailureKind.AccountMismatch);
                    return TraktSyncResult.AccountMismatch(knownAccountId, uuid);
                }

                // Step 3: RecordVerifiedAccountAsync must succeed using exact boundToken (Blocker 1)
                opToken.ThrowIfCancellationRequested();
                if (Interlocked.Read(ref _generation) != currentGen)
                {
                    throw new OperationCanceledException("Sync superseded before account recording.");
                }

                var recordSuccess = await _authService.RecordVerifiedAccountAsync(uuid, boundToken, opToken);
                if (!recordSuccess)
                {
                    UpdateState(isSyncing: false, error: "Failed to record verified account.", failureKind: TraktSyncFailureKind.RecordAccountFailed);
                    return TraktSyncResult.Aborted(uuid, "RecordVerifiedAccountAsync failed.", TraktSyncFailureKind.RecordAccountFailed);
                }

                // Blocker 8: Recheck cancel and generation before writing last-account settings
                opToken.ThrowIfCancellationRequested();
                if (Interlocked.Read(ref _generation) != currentGen)
                {
                    throw new OperationCanceledException("Sync superseded before writing account settings.");
                }

                _currentAccountId = uuid;
                _lastAccountId = uuid;
                _settingsRepository?.Set(LastAccountIdSettingsKey, uuid, startTime);
                UpdateState(isSyncing: true, auth: _authService.GetAuthState());

                // Step 4: Fetch and sync all independent resources
                var outcomes = new List<TraktResourceSyncOutcome>();
                int completedCount = 0;
                int failedCount = 0;

                // Resource A: UserLists (Blocker 6: validate required array and list IDs before commit)
                var activeListIds = new HashSet<long>();
                bool userListsSuccess;
                try
                {
                    userListsSuccess = await SyncResourceAsync(
                        uuid,
                        TraktCacheResourceKeys.Lists,
                        TraktResourceSpec.UserLists(),
                        currentGen,
                        opToken,
                        outcomes,
                        validateAndProcess: root =>
                        {
                            foreach (var item in root.EnumerateArray())
                            {
                                if (item.ValueKind != JsonValueKind.Object)
                                {
                                    throw new TraktClientException(
                                        TraktFailureKind.InvalidResponse,
                                        "Malformed list catalog entry: item is not a JSON object.",
                                        "users/me/lists");
                                }

                                if (!item.TryGetProperty("ids", out var idsEl) ||
                                    idsEl.ValueKind != JsonValueKind.Object ||
                                    !idsEl.TryGetProperty("trakt", out var traktProp) ||
                                    traktProp.ValueKind != JsonValueKind.Number ||
                                    !traktProp.TryGetInt64(out var listId) ||
                                    listId <= 0)
                                {
                                    throw new TraktClientException(
                                        TraktFailureKind.InvalidResponse,
                                        "Malformed list catalog entry: missing or non-positive trakt list ID.",
                                        "users/me/lists");
                                }

                                activeListIds.Add(listId);
                            }
                        });
                }
                catch (Exception ex) when (
                    ex is TraktAuthException ||
                    ex is TraktInvalidGrantException ||
                    (ex is TraktClientException tex && tex.FailureKind == TraktFailureKind.Unauthorized))
                {
                    // Blocker 9: Second 401 aborts sync immediately, does not continue
                    var sanitized = "Trakt authorization expired or rejected. Reconnection required.";
                    UpdateState(isSyncing: false, error: sanitized, failureKind: TraktSyncFailureKind.Unauthorized);
                    return TraktSyncResult.Aborted(uuid, sanitized, TraktSyncFailureKind.Unauthorized);
                }

                if (userListsSuccess) completedCount++; else failedCount++;

                // If lists succeeded, fetch each positive numeric list items resource
                if (userListsSuccess)
                {
                    foreach (var listId in activeListIds)
                    {
                        var listKey = TraktCacheResourceKeys.ListItems(listId.ToString());
                        bool itemSuccess;
                        try
                        {
                            itemSuccess = await SyncResourceAsync(
                                uuid,
                                listKey,
                                TraktResourceSpec.ListItems(listId),
                                currentGen,
                                opToken,
                                outcomes);
                        }
                        catch (Exception ex) when (
                            ex is TraktAuthException ||
                            ex is TraktInvalidGrantException ||
                            (ex is TraktClientException tex && tex.FailureKind == TraktFailureKind.Unauthorized))
                        {
                            var sanitized = "Trakt authorization expired or rejected. Reconnection required.";
                            UpdateState(isSyncing: false, error: sanitized, failureKind: TraktSyncFailureKind.Unauthorized);
                            return TraktSyncResult.Aborted(uuid, sanitized, TraktSyncFailureKind.Unauthorized);
                        }

                        if (itemSuccess) completedCount++; else failedCount++;
                    }
                }

                // Resource B: Watchlist
                try
                {
                    var watchlistSuccess = await SyncResourceAsync(
                        uuid,
                        TraktResourceSpec.Watchlist().ToCacheResourceKey(),
                        TraktResourceSpec.Watchlist(),
                        currentGen,
                        opToken,
                        outcomes);
                    if (watchlistSuccess) completedCount++; else failedCount++;
                }
                catch (Exception ex) when (
                    ex is TraktAuthException ||
                    ex is TraktInvalidGrantException ||
                    (ex is TraktClientException tex && tex.FailureKind == TraktFailureKind.Unauthorized))
                {
                    var sanitized = "Trakt authorization expired or rejected. Reconnection required.";
                    UpdateState(isSyncing: false, error: sanitized, failureKind: TraktSyncFailureKind.Unauthorized);
                    return TraktSyncResult.Aborted(uuid, sanitized, TraktSyncFailureKind.Unauthorized);
                }

                // Resource C: WatchedMovies
                try
                {
                    var moviesSuccess = await SyncResourceAsync(
                        uuid,
                        TraktResourceSpec.WatchedMovies().ToCacheResourceKey(),
                        TraktResourceSpec.WatchedMovies(),
                        currentGen,
                        opToken,
                        outcomes);
                    if (moviesSuccess) completedCount++; else failedCount++;
                }
                catch (Exception ex) when (
                    ex is TraktAuthException ||
                    ex is TraktInvalidGrantException ||
                    (ex is TraktClientException tex && tex.FailureKind == TraktFailureKind.Unauthorized))
                {
                    var sanitized = "Trakt authorization expired or rejected. Reconnection required.";
                    UpdateState(isSyncing: false, error: sanitized, failureKind: TraktSyncFailureKind.Unauthorized);
                    return TraktSyncResult.Aborted(uuid, sanitized, TraktSyncFailureKind.Unauthorized);
                }

                // Resource D: WatchedShows
                try
                {
                    var showsSuccess = await SyncResourceAsync(
                        uuid,
                        TraktResourceSpec.WatchedShows().ToCacheResourceKey(),
                        TraktResourceSpec.WatchedShows(),
                        currentGen,
                        opToken,
                        outcomes);
                    if (showsSuccess) completedCount++; else failedCount++;
                }
                catch (Exception ex) when (
                    ex is TraktAuthException ||
                    ex is TraktInvalidGrantException ||
                    (ex is TraktClientException tex && tex.FailureKind == TraktFailureKind.Unauthorized))
                {
                    var sanitized = "Trakt authorization expired or rejected. Reconnection required.";
                    UpdateState(isSyncing: false, error: sanitized, failureKind: TraktSyncFailureKind.Unauthorized);
                    return TraktSyncResult.Aborted(uuid, sanitized, TraktSyncFailureKind.Unauthorized);
                }

                // Resource E: History
                try
                {
                    var historySuccess = await SyncResourceAsync(
                        uuid,
                        TraktResourceSpec.History().ToCacheResourceKey(),
                        TraktResourceSpec.History(),
                        currentGen,
                        opToken,
                        outcomes);
                    if (historySuccess) completedCount++; else failedCount++;
                }
                catch (Exception ex) when (
                    ex is TraktAuthException ||
                    ex is TraktInvalidGrantException ||
                    (ex is TraktClientException tex && tex.FailureKind == TraktFailureKind.Unauthorized))
                {
                    var sanitized = "Trakt authorization expired or rejected. Reconnection required.";
                    UpdateState(isSyncing: false, error: sanitized, failureKind: TraktSyncFailureKind.Unauthorized);
                    return TraktSyncResult.Aborted(uuid, sanitized, TraktSyncFailureKind.Unauthorized);
                }

                // Blocker 8: Recheck cancel and generation before publishing success state
                EnsureOperationValid(currentGen, uuid, opToken);

                var overallSuccess = failedCount == 0;
                var finishTime = _timeProvider.GetUtcNow();
                UpdateState(
                    isSyncing: false,
                    lastSuccess: overallSuccess ? finishTime : _syncState.LastSyncSuccessAt,
                    error: overallSuccess ? null : $"Sync completed with {failedCount} failed resource(s).",
                    clearError: overallSuccess,
                    failureKind: overallSuccess ? TraktSyncFailureKind.None : TraktSyncFailureKind.NetworkError);

                return new TraktSyncResult(overallSuccess, uuid, completedCount, failedCount, outcomes);
            }
            finally
            {
                // Blocker 4: finally must reset IsSyncing after cancellation or unhandled errors
                if (_syncState.IsSyncing)
                {
                    UpdateState(isSyncing: false);
                }

                // Blocker 3: clear pointer iff reference same, active op disposes linkedCts
                lock (_ctsLock)
                {
                    if (ReferenceEquals(_activeNetworkCts, linkedCts))
                    {
                        _activeNetworkCts = null;
                    }
                }
                linkedCts.Dispose();
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<TraktCacheSummary> GetCacheSummaries(string? accountId = null)
    {
        var targetAccount = ResolveTargetAccount(accountId);
        if (string.IsNullOrWhiteSpace(targetAccount)) return Array.Empty<TraktCacheSummary>();

        // Blocker 10: repository calls are outside try-catch to propagate DB/programmer errors
        var allSummaries = _cacheRepository.List(targetAccount);
        var activeListIds = GetActiveListIds(targetAccount);

        if (activeListIds is null)
        {
            return allSummaries;
        }

        var filtered = new List<TraktCacheSummary>(allSummaries.Count);
        foreach (var summary in allSummaries)
        {
            if (TryParseListIdFromKey(summary.ResourceKey, out var listId))
            {
                // Hide orphan deleted-list rows
                if (activeListIds.Contains(listId))
                {
                    filtered.Add(summary);
                }
            }
            else
            {
                filtered.Add(summary);
            }
        }

        return filtered;
    }

    /// <inheritdoc />
    public IReadOnlyList<TraktCustomList> GetLists(string? accountId = null)
    {
        var targetAccount = ResolveTargetAccount(accountId);
        if (string.IsNullOrWhiteSpace(targetAccount)) return Array.Empty<TraktCustomList>();

        var entry = _cacheRepository.Get(targetAccount, TraktCacheResourceKeys.Lists);
        if (entry?.PayloadJson is null) return Array.Empty<TraktCustomList>();

        // Blocker 10: catch only JsonException on corrupted payload, not DB/programmer errors
        try
        {
            using var doc = JsonDocument.Parse(entry.PayloadJson);
            return TraktDataProjections.ParseLists(doc.RootElement.Clone());
        }
        catch (JsonException)
        {
            return Array.Empty<TraktCustomList>();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<TraktListItem> GetListItems(long listId, string? accountId = null)
    {
        if (listId <= 0) return Array.Empty<TraktListItem>();
        var targetAccount = ResolveTargetAccount(accountId);
        if (string.IsNullOrWhiteSpace(targetAccount)) return Array.Empty<TraktListItem>();

        var activeListIds = GetActiveListIds(targetAccount);
        if (activeListIds != null && !activeListIds.Contains(listId))
        {
            // Orphan or deleted list: do not display
            return Array.Empty<TraktListItem>();
        }

        var entry = _cacheRepository.Get(targetAccount, TraktCacheResourceKeys.ListItems(listId.ToString()));
        if (entry?.PayloadJson is null) return Array.Empty<TraktListItem>();

        // Blocker 10: catch only JsonException on corrupted payload
        try
        {
            using var doc = JsonDocument.Parse(entry.PayloadJson);
            return TraktDataProjections.ParseListItems(doc.RootElement.Clone());
        }
        catch (JsonException)
        {
            return Array.Empty<TraktListItem>();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<TraktWatchlistItem> GetWatchlist(string? accountId = null)
    {
        var targetAccount = ResolveTargetAccount(accountId);
        if (string.IsNullOrWhiteSpace(targetAccount)) return Array.Empty<TraktWatchlistItem>();

        var entry = _cacheRepository.Get(targetAccount, TraktResourceSpec.Watchlist().ToCacheResourceKey());
        if (entry?.PayloadJson is null) return Array.Empty<TraktWatchlistItem>();

        try
        {
            using var doc = JsonDocument.Parse(entry.PayloadJson);
            return TraktDataProjections.ParseWatchlist(doc.RootElement.Clone());
        }
        catch (JsonException)
        {
            return Array.Empty<TraktWatchlistItem>();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<TraktWatchedMovie> GetWatchedMovies(string? accountId = null)
    {
        var targetAccount = ResolveTargetAccount(accountId);
        if (string.IsNullOrWhiteSpace(targetAccount)) return Array.Empty<TraktWatchedMovie>();

        var entry = _cacheRepository.Get(targetAccount, TraktResourceSpec.WatchedMovies().ToCacheResourceKey());
        if (entry?.PayloadJson is null) return Array.Empty<TraktWatchedMovie>();

        try
        {
            using var doc = JsonDocument.Parse(entry.PayloadJson);
            return TraktDataProjections.ParseWatchedMovies(doc.RootElement.Clone());
        }
        catch (JsonException)
        {
            return Array.Empty<TraktWatchedMovie>();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<TraktWatchedShow> GetWatchedShows(string? accountId = null)
    {
        var targetAccount = ResolveTargetAccount(accountId);
        if (string.IsNullOrWhiteSpace(targetAccount)) return Array.Empty<TraktWatchedShow>();

        var entry = _cacheRepository.Get(targetAccount, TraktResourceSpec.WatchedShows().ToCacheResourceKey());
        if (entry?.PayloadJson is null) return Array.Empty<TraktWatchedShow>();

        try
        {
            using var doc = JsonDocument.Parse(entry.PayloadJson);
            return TraktDataProjections.ParseWatchedShows(doc.RootElement.Clone());
        }
        catch (JsonException)
        {
            return Array.Empty<TraktWatchedShow>();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<TraktHistoryItem> GetHistory(string? accountId = null)
    {
        var targetAccount = ResolveTargetAccount(accountId);
        if (string.IsNullOrWhiteSpace(targetAccount)) return Array.Empty<TraktHistoryItem>();

        var entry = _cacheRepository.Get(targetAccount, TraktResourceSpec.History().ToCacheResourceKey());
        if (entry?.PayloadJson is null) return Array.Empty<TraktHistoryItem>();

        try
        {
            using var doc = JsonDocument.Parse(entry.PayloadJson);
            return TraktDataProjections.ParseHistory(doc.RootElement.Clone());
        }
        catch (JsonException)
        {
            return Array.Empty<TraktHistoryItem>();
        }
    }

    /// <inheritdoc />
    public TraktCacheEntry? GetRawResource(string resourceKey, string? accountId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceKey);
        var targetAccount = ResolveTargetAccount(accountId);
        if (string.IsNullOrWhiteSpace(targetAccount)) return null;

        return _cacheRepository.Get(targetAccount, resourceKey);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _disposed = true;
        // Blocker 5: cancel network operations and avoid disposing _operationGate
        // while active operations unwind to prevent ObjectDisposedException on Release().
        CancelActiveNetwork();
    }

    private async Task<bool> SyncResourceAsync(
        string accountId,
        string resourceKey,
        TraktResourceSpec spec,
        long boundGen,
        CancellationToken opToken,
        List<TraktResourceSyncOutcome> outcomes,
        Action<JsonElement>? validateAndProcess = null)
    {
        EnsureOperationValid(boundGen, accountId, opToken);

        try
        {
            // Execute HTTP fetch outside of any database transaction or write gate
            var (element, _) = await FetchWithAuthRetryAsync(
                token => _dataClient.GetResourceAsync(spec, _config.ClientId, token, opToken),
                opToken);

            // Blocker 6: Validate array format before commit
            if (element.ValueKind != JsonValueKind.Array)
            {
                throw new TraktClientException(
                    TraktFailureKind.InvalidResponse,
                    $"Expected JSON array for resource '{resourceKey}', but got {element.ValueKind}.",
                    spec.GetRelativePath());
            }

            // Blocker 6: Validate required IDs and parsed structure before writing payload to cache
            validateAndProcess?.Invoke(element);

            var payloadJson = element.GetRawText();

            // Recheck cancel, generation, and account before writing
            EnsureOperationValid(boundGen, accountId, opToken);

            // SaveComplete only after whole resource complete and validated
            _cacheRepository.SaveComplete(accountId, resourceKey, payloadJson, _timeProvider.GetUtcNow(), opToken);
            outcomes.Add(new TraktResourceSyncOutcome(resourceKey, true));
            return true;
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException)
            {
                throw;
            }

            // Blocker 9: Second 401 or auth failure must abort sync, not continue other resources
            if (ex is TraktInvalidGrantException ||
                ex is TraktAuthException ||
                (ex is TraktClientException tex && tex.FailureKind == TraktFailureKind.Unauthorized))
            {
                throw;
            }

            var errorCode = MapToErrorCode(ex);

            // Recheck cancel, generation, and account before failure recording
            if (Interlocked.Read(ref _generation) == boundGen &&
                string.Equals(_currentAccountId, accountId, StringComparison.Ordinal) &&
                !opToken.IsCancellationRequested)
            {
                _cacheRepository.RecordFailure(accountId, resourceKey, errorCode, _timeProvider.GetUtcNow(), opToken);
            }

            outcomes.Add(new TraktResourceSyncOutcome(resourceKey, false, errorCode));
            return false;
        }
    }

    private async Task<(T Result, string AccessToken)> FetchWithAuthRetryAsync<T>(
        Func<string, Task<T>> fetchFunc,
        CancellationToken cancellationToken)
    {
        var token = await _authService.GetAccessTokenAsync(forceRefresh: false, cancellationToken: cancellationToken);
        try
        {
            var result = await fetchFunc(token);
            return (result, token);
        }
        catch (TraktClientException ex) when (ex.FailureKind == TraktFailureKind.Unauthorized)
        {
            // HTTP 401: retry once using GetAccessTokenAsync(forceRefresh: true, rejectedAccessToken: token)
            var refreshedToken = await _authService.GetAccessTokenAsync(
                forceRefresh: true,
                rejectedAccessToken: token,
                cancellationToken: cancellationToken);

            try
            {
                var result = await fetchFunc(refreshedToken);
                return (result, refreshedToken);
            }
            catch (TraktClientException secondEx) when (secondEx.FailureKind == TraktFailureKind.Unauthorized)
            {
                await _authService.RejectAccessTokenAsync(refreshedToken, cancellationToken);
                // Blocker 9: Second 401 after refresh must abort sync as Unauthorized/reconnect
                throw new TraktAuthException("Trakt authorization rejected refreshed token. Reconnection required.", secondEx);
            }
        }
    }

    private void EnsureOperationValid(long boundGen, string expectedAccountId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.Read(ref _generation) != boundGen)
        {
            throw new OperationCanceledException("Trakt sync generation changed or superseded.");
        }
        if (_currentAccountId == null || !string.Equals(_currentAccountId, expectedAccountId, StringComparison.Ordinal))
        {
            throw new OperationCanceledException("Trakt account changed or disconnected during sync operation.");
        }
    }

    private string? ResolveTargetAccount(string? explicitAccountId)
    {
        var authState = _authService.GetAuthState();
        if (authState.IsConnected)
        {
            // Blocker 2: When connected with unknown UUID, return no cache (never leak old account)
            if (string.IsNullOrWhiteSpace(_currentAccountId))
            {
                return null;
            }

            // Blocker 2: Explicit accountId cannot bypass active-session isolation: must match current UUID
            if (!string.IsNullOrWhiteSpace(explicitAccountId) &&
                !string.Equals(explicitAccountId.Trim(), _currentAccountId, StringComparison.Ordinal))
            {
                return null;
            }

            return _currentAccountId;
        }

        // Blocker 2: Permit last account ONLY while disconnected.
        // Explicit arbitrary accountId must only permit lastAccount while disconnected.
        if (string.IsNullOrWhiteSpace(_lastAccountId))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(explicitAccountId) &&
            !string.Equals(explicitAccountId.Trim(), _lastAccountId, StringComparison.Ordinal))
        {
            return null;
        }

        return _lastAccountId;
    }

    private HashSet<long>? GetActiveListIds(string accountId)
    {
        var entry = _cacheRepository.Get(accountId, TraktCacheResourceKeys.Lists);
        if (entry?.PayloadJson is null) return null;

        try
        {
            using var doc = JsonDocument.Parse(entry.PayloadJson);
            var lists = TraktDataProjections.ParseLists(doc.RootElement);
            var set = new HashSet<long>();
            foreach (var l in lists)
            {
                if (l.TraktId > 0) set.Add(l.TraktId);
            }
            return set;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryParseListIdFromKey(string resourceKey, out long listId)
    {
        listId = 0;
        if (resourceKey.StartsWith("list:", StringComparison.Ordinal) &&
            resourceKey.EndsWith(":items", StringComparison.Ordinal))
        {
            var middle = resourceKey.Substring(5, resourceKey.Length - 11);
            return long.TryParse(middle, out listId);
        }
        return false;
    }

    private static TraktCacheErrorCode MapToErrorCode(Exception ex) => ex switch
    {
        TraktClientException tex => tex.FailureKind switch
        {
            TraktFailureKind.Unauthorized => TraktCacheErrorCode.Unauthorized,
            TraktFailureKind.RateLimited => TraktCacheErrorCode.RateLimited,
            TraktFailureKind.Network => TraktCacheErrorCode.NetworkError,
            TraktFailureKind.InvalidResponse => TraktCacheErrorCode.InvalidResponse,
            TraktFailureKind.HttpError => TraktCacheErrorCode.HttpError,
            _ => TraktCacheErrorCode.Unknown
        },
        TimeoutException => TraktCacheErrorCode.Timeout,
        OperationCanceledException => TraktCacheErrorCode.Cancelled,
        _ => TraktCacheErrorCode.Unknown
    };

    private long CancelActiveNetwork()
    {
        CancellationTokenSource? cts;
        long invalidationGeneration;
        lock (_ctsLock)
        {
            invalidationGeneration = Interlocked.Increment(ref _generation);
            cts = _activeNetworkCts;
        }

        if (cts != null)
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        OnCancelActiveNetworkFinished?.Invoke();
        return invalidationGeneration;
    }

    private void UpdateState(
        TraktAuthState? auth = null,
        bool? isSyncing = null,
        DateTimeOffset? lastAttempt = null,
        DateTimeOffset? lastSuccess = null,
        string? error = null,
        bool clearError = false,
        TraktSyncFailureKind failureKind = TraktSyncFailureKind.None)
    {
        var prev = _syncState;

        // Blocker 4: Successful error=null or clearError must clear old errors rather than ?? preserving previous
        string? nextError;
        TraktSyncFailureKind nextFailureKind;

        if (clearError || (lastSuccess.HasValue && error == null))
        {
            nextError = null;
            nextFailureKind = TraktSyncFailureKind.None;
        }
        else if (error != null)
        {
            nextError = error;
            nextFailureKind = failureKind;
        }
        else if (isSyncing == true)
        {
            nextError = null;
            nextFailureKind = TraktSyncFailureKind.None;
        }
        else
        {
            nextError = prev.LastSyncError;
            nextFailureKind = prev.LastFailureKind;
        }

        _syncState = new TraktSyncState(
            auth ?? prev.AuthState,
            isSyncing ?? prev.IsSyncing,
            _currentAccountId,
            _lastAccountId,
            lastAttempt ?? prev.LastSyncAttemptAt,
            lastSuccess ?? prev.LastSyncSuccessAt,
            nextError,
            nextFailureKind);
    }
}
