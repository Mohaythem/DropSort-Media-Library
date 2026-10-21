using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DropSort.Application.Repositories;
using DropSort.Application.Trakt;
using DropSort.Infrastructure.Trakt;
using Xunit;

namespace DropSort.Tests.Application;

public sealed class TraktSyncCoordinatorTests : IDisposable
{
    [Fact]
    public async Task Second_401_with_real_auth_requires_reconnect_and_prevents_further_refresh()
    {
        var store = new MemoryCredentials();
        store.Write(new TraktCredentialPayload(_config.ClientId, "old-access", "old-refresh", "bearer", 7200,
            "public", _timeProvider.GetUtcNow().ToUnixTimeSeconds(), "uuid-123"));
        using var handler = new RejectedTokenHandler(_timeProvider.GetUtcNow().ToUnixTimeSeconds());
        using var http = new HttpClient(handler);
        using var authClient = new TraktAuthClient(http);
        using var dataClient = new TraktDataClient(http);
        var auth = new TraktAuthService(authClient, store, _config, _timeProvider);
        using var service = new TraktSyncService(auth, dataClient, _cacheRepository, _config, _settingsRepository, _timeProvider);

        var result = await service.SyncAsync();
        Assert.Equal(TraktSyncFailureKind.Unauthorized, result.FailureKind);
        Assert.True(service.GetSyncState().AuthState.RequiresReconnect);
        Assert.False(service.GetSyncState().AuthState.IsConnected);
        Assert.True(store.Read()!.RequiresReconnect);
        Assert.True(new TraktAuthService(authClient, store, _config, _timeProvider).GetAuthState().RequiresReconnect);
        Assert.Equal(1, handler.RefreshCalls);
        Assert.Equal(2, handler.AccountCalls);

        await service.SyncAsync();
        Assert.Equal(1, handler.RefreshCalls);
        Assert.Equal(2, handler.AccountCalls);
        var code = await service.BeginAuthAsync();
        Assert.Equal("TEST", code.UserCode);
        Assert.Equal(1, handler.DeviceCalls);
    }

    [Fact]
    public async Task Delayed_state_reader_cannot_restore_busy_after_sync_completes()
    {
        _authService.SetConnected("uuid-123", "old-token");
        _dataClient.AccountToReturn = JsonDocument.Parse("""{"user":{"ids":{"uuid":"uuid-123"}}}""").RootElement;
        var resourceStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishResources = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _dataClient.AsyncResourceHandler = async (_, ct) =>
        {
            resourceStarted.TrySetResult();
            await finishResources.Task.WaitAsync(ct);
            return JsonDocument.Parse("[]").RootElement;
        };
        using var service = CreateService();
        var sync = service.SyncAsync();
        await resourceStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        _authService.SetConnected("uuid-123", "refreshed-token");
        using var readerCaptured = new ManualResetEventSlim();
        using var releaseReader = new ManualResetEventSlim();
        service.AfterSyncStateRead = () =>
        {
            readerCaptured.Set();
            Assert.True(releaseReader.Wait(TimeSpan.FromSeconds(10)));
        };
        var reader = Task.Run(() => service.GetSyncState());
        try
        {
            Assert.True(readerCaptured.Wait(TimeSpan.FromSeconds(5)));
            finishResources.TrySetResult();
            Assert.True((await sync.WaitAsync(TimeSpan.FromSeconds(5))).Success);
        }
        finally
        {
            releaseReader.Set();
            service.AfterSyncStateRead = null;
        }
        await reader;
        var state = service.GetSyncState();
        Assert.False(state.IsSyncing);
        Assert.NotNull(state.LastSyncSuccessAt);
        Assert.Null(state.LastSyncError);
        Assert.True((await service.SyncAsync()).Success);
    }

    private sealed class MemoryCredentials : ITraktCredentialStore
    {
        private TraktCredentialPayload? _value;
        public TraktCredentialPayload? Read() => _value;
        public void Write(TraktCredentialPayload payload) => _value = payload;
        public void Delete() => _value = null;
    }

    private sealed class RejectedTokenHandler(long createdAt) : HttpMessageHandler
    {
        public int RefreshCalls { get; private set; }
        public int AccountCalls { get; private set; }
        public int DeviceCalls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/users/settings")
            {
                AccountCalls++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            }
            string body;
            if (request.RequestUri.AbsolutePath == "/oauth/token")
            {
                RefreshCalls++;
                body = JsonSerializer.Serialize(new { access_token = "new-access", refresh_token = "new-refresh",
                    token_type = "bearer", expires_in = 7200, scope = "public", created_at = createdAt });
            }
            else
            {
                Assert.Equal("/oauth/device/code", request.RequestUri.AbsolutePath);
                DeviceCalls++;
                body = """{"device_code":"test-device","user_code":"TEST","verification_url":"https://trakt.tv/activate","expires_in":600,"interval":5}""";
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    private readonly FakeTraktAuthService _authService = new();
    private readonly FakeTraktDataClient _dataClient = new();
    private readonly FakeTraktCacheRepository _cacheRepository = new();
    private readonly FakeSettingsRepository _settingsRepository = new();
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 10, 21, 0, 0, 0, TimeSpan.Zero));
    private readonly TraktAuthConfig _config = new("test_client_id", "https://localhost/callback");

    private TraktSyncService CreateService()
    {
        return new TraktSyncService(
            _authService,
            _dataClient,
            _cacheRepository,
            _config,
            _settingsRepository,
            _timeProvider);
    }

    public void Dispose()
    {
    }

    [Fact]
    public async Task SyncAsync_Success_AllResourcesFetched_BindsExactTokenUsed()
    {
        // Blocker 1: FetchWithAuthRetryAsync returns exact token used, and RecordVerifiedAccountAsync binds that token
        _authService.SetConnected("uuid-123", "token-bound-1");

        _dataClient.AccountToReturn = JsonDocument.Parse("""
        {
            "user": {
                "username": "tester",
                "name": "Test User",
                "ids": { "uuid": "uuid-123" }
            }
        }
        """).RootElement;

        _dataClient.ResourceHandlers[TraktResourceType.UserLists] = () => JsonDocument.Parse("""
        [
            {
                "name": "Favorites",
                "ids": { "trakt": 101, "slug": "favorites" }
            },
            {
                "name": "Watch Queue",
                "ids": { "trakt": 102, "slug": "watch-queue" }
            }
        ]
        """).RootElement;

        _dataClient.ResourceHandlers[TraktResourceType.ListItems] = () => JsonDocument.Parse("""
        [
            {
                "id": 9001,
                "rank": 1,
                "type": "movie",
                "movie": { "title": "Inception", "year": 2010, "ids": { "trakt": 1 } }
            }
        ]
        """).RootElement;

        _dataClient.ResourceHandlers[TraktResourceType.Watchlist] = () => JsonDocument.Parse("""
        [
            {
                "id": 8001,
                "rank": 1,
                "type": "movie",
                "movie": { "title": "Interstellar", "year": 2014, "ids": { "trakt": 2 } }
            }
        ]
        """).RootElement;

        _dataClient.ResourceHandlers[TraktResourceType.WatchedMovies] = () => JsonDocument.Parse("""
        [
            {
                "plays": 2,
                "movie": { "title": "The Matrix", "year": 1999, "ids": { "trakt": 3 } }
            }
        ]
        """).RootElement;

        _dataClient.ResourceHandlers[TraktResourceType.WatchedShows] = () => JsonDocument.Parse("""
        [
            {
                "plays": 10,
                "show": { "title": "Breaking Bad", "year": 2008, "ids": { "trakt": 4 } },
                "seasons": []
            }
        ]
        """).RootElement;

        _dataClient.ResourceHandlers[TraktResourceType.History] = () => JsonDocument.Parse("""
        [
            {
                "id": 7001,
                "action": "watch",
                "type": "movie",
                "movie": { "title": "Memento", "ids": { "trakt": 5 } }
            }
        ]
        """).RootElement;

        using var service = CreateService();
        var result = await service.SyncAsync();

        Assert.True(result.Success);
        Assert.Equal("uuid-123", result.AccountId);
        Assert.Equal(7, result.CompletedResources);
        Assert.Equal(0, result.FailedResources);

        // Blocker 1: RecordVerifiedAccountAsync bound to the exact token used
        Assert.Equal("token-bound-1", _authService.RecordedTokenPassed);

        // Verify SaveComplete called for each resource
        Assert.True(_cacheRepository.HasEntry("uuid-123", TraktCacheResourceKeys.Lists));
        Assert.True(_cacheRepository.HasEntry("uuid-123", TraktCacheResourceKeys.ListItems("101")));
        Assert.True(_cacheRepository.HasEntry("uuid-123", TraktCacheResourceKeys.ListItems("102")));
        Assert.True(_cacheRepository.HasEntry("uuid-123", "watchlist"));
        Assert.True(_cacheRepository.HasEntry("uuid-123", "watched:movies"));
        Assert.True(_cacheRepository.HasEntry("uuid-123", "watched:shows"));
        Assert.True(_cacheRepository.HasEntry("uuid-123", "history"));

        // Verify settings repository recorded the last account ID
        Assert.Equal("uuid-123", _settingsRepository.Get("trakt_last_account_id"));

        var state = service.GetSyncState();
        Assert.False(state.IsSyncing);
        Assert.Equal("uuid-123", state.CurrentAccountId);
        Assert.Equal("uuid-123", state.LastAccountId);
        Assert.NotNull(state.LastSyncSuccessAt);
        Assert.Null(state.LastSyncError);
    }

    [Fact]
    public async Task SyncAsync_PartialRetention_OneResourceFails_RetainsOthers()
    {
        _authService.SetConnected("uuid-123", "token-abc");

        _dataClient.AccountToReturn = JsonDocument.Parse("""
        {
            "user": { "username": "tester", "ids": { "uuid": "uuid-123" } }
        }
        """).RootElement;

        _dataClient.ResourceHandlers[TraktResourceType.UserLists] = () => JsonDocument.Parse("[]").RootElement;
        _dataClient.ResourceHandlers[TraktResourceType.Watchlist] = () => JsonDocument.Parse("[]").RootElement;
        _dataClient.ResourceHandlers[TraktResourceType.WatchedMovies] = () => JsonDocument.Parse("[]").RootElement;
        _dataClient.ResourceHandlers[TraktResourceType.WatchedShows] = () =>
            throw new TraktClientException(TraktFailureKind.Network, "Network drop during shows sync", "sync/watched/shows");
        _dataClient.ResourceHandlers[TraktResourceType.History] = () => JsonDocument.Parse("[]").RootElement;

        using var service = CreateService();
        var result = await service.SyncAsync();

        Assert.False(result.Success);
        Assert.Equal(4, result.CompletedResources);
        Assert.Equal(1, result.FailedResources);

        Assert.True(_cacheRepository.HasFailure("uuid-123", "watched:shows", TraktCacheErrorCode.NetworkError));
        Assert.True(_cacheRepository.HasEntry("uuid-123", "watched:movies"));
        Assert.True(_cacheRepository.HasEntry("uuid-123", "watchlist"));
    }

    [Fact]
    public async Task SyncAsync_Cancel_MidOperation_NoLateWrites_And_ResetsIsSyncing()
    {
        // Blocker 4: finally must reset IsSyncing after cancellation
        _authService.SetConnected("uuid-123", "token-abc");
        _dataClient.AccountToReturn = JsonDocument.Parse("""
        {
            "user": { "username": "tester", "ids": { "uuid": "uuid-123" } }
        }
        """).RootElement;

        using var cts = new CancellationTokenSource();

        _dataClient.ResourceHandlers[TraktResourceType.UserLists] = () =>
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        };

        using var service = CreateService();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SyncAsync(cts.Token));

        // Ensure IsSyncing reset to false
        Assert.False(service.GetSyncState().IsSyncing);

        // Ensure no cache writes took place after cancellation
        Assert.Empty(_cacheRepository.AllEntries);
        Assert.Empty(_cacheRepository.AllFailures);
    }

    [Fact]
    public async Task SyncAsync_DisconnectRace_CancelsNetworkImmediately_ActiveOpUnwindsCleanly()
    {
        // Blocker 3 & 5: Active op owns linked CTS disposal, Disconnect cancels without premature dispose
        _authService.SetConnected("uuid-123", "token-abc");
        _dataClient.AccountToReturn = JsonDocument.Parse("""
        {
            "user": { "username": "tester", "ids": { "uuid": "uuid-123" } }
        }
        """).RootElement;

        var networkStarted = new TaskCompletionSource<bool>();
        var networkCancelled = new TaskCompletionSource<bool>();

        _dataClient.AsyncResourceHandler = async (spec, ct) =>
        {
            networkStarted.TrySetResult(true);
            using (ct.Register(() => networkCancelled.TrySetResult(true)))
            {
                await networkCancelled.Task;
            }
            ct.ThrowIfCancellationRequested();
            return JsonDocument.Parse("[]").RootElement;
        };

        using var service = CreateService();

        var syncTask = Task.Run(async () =>
        {
            try
            {
                await service.SyncAsync();
            }
            catch (OperationCanceledException)
            {
            }
        });

        await networkStarted.Task;

        // Disconnect immediately while HTTP is in-flight
        await service.DisconnectAsync();
        await syncTask;

        Assert.Null(service.GetSyncState().CurrentAccountId);
        Assert.False(_authService.GetAuthState().IsConnected);
        Assert.False(service.GetSyncState().IsSyncing);
        Assert.Empty(_cacheRepository.AllEntries);
    }

    [Fact]
    public async Task SyncAsync_AccountMismatch_AbortsImmediately_TypedFailState_NoSilentSwitch()
    {
        // Blocker 11: profile mismatch must not silently switch under current credential; typed fail state
        _authService.SetConnected("account-1", "token-abc");

        _dataClient.AccountToReturn = JsonDocument.Parse("""
        {
            "user": { "username": "different_user", "ids": { "uuid": "account-2" } }
        }
        """).RootElement;

        using var service = CreateService();
        var result = await service.SyncAsync();

        Assert.False(result.Success);
        Assert.Equal(TraktSyncFailureKind.AccountMismatch, result.FailureKind);
        Assert.Contains("Account ID mismatch", result.AbortReason);

        // Verification: no silent switch under current credential
        var state = service.GetSyncState();
        Assert.Equal("account-1", state.CurrentAccountId);
        Assert.Equal(TraktSyncFailureKind.AccountMismatch, state.LastFailureKind);

        // No cache writes for either account
        Assert.Empty(_cacheRepository.AllEntries);
    }

    [Fact]
    public async Task SyncAsync_RecordVerifiedAccountFails_AbortsSync_TypedFailState()
    {
        _authService.SetConnected("uuid-123", "token-abc");
        _authService.RecordVerifiedAccountResult = false;

        _dataClient.AccountToReturn = JsonDocument.Parse("""
        {
            "user": { "username": "tester", "ids": { "uuid": "uuid-123" } }
        }
        """).RootElement;

        using var service = CreateService();
        var result = await service.SyncAsync();

        Assert.False(result.Success);
        Assert.Equal(TraktSyncFailureKind.RecordAccountFailed, result.FailureKind);
        Assert.Contains("RecordVerifiedAccountAsync failed", result.AbortReason);
        Assert.Empty(_cacheRepository.AllEntries);
    }

    [Fact]
    public async Task SyncAsync_401Unauthorized_RefreshesTokenOnce_AndRetries_BindsRefreshedToken()
    {
        // Blocker 1 & 9: 401 retries once and binds verified account with refreshed token
        _authService.SetConnected("uuid-123", "token-stale");
        _authService.RefreshedTokenToReturn = "token-fresh";

        int accountAttempts = 0;
        _dataClient.CustomAccountHandler = token =>
        {
            accountAttempts++;
            if (accountAttempts == 1)
            {
                throw new TraktClientException(TraktFailureKind.Unauthorized, "Unauthorized", "users/settings", HttpStatusCode.Unauthorized);
            }
            return JsonDocument.Parse("""
            {
                "user": { "username": "tester", "ids": { "uuid": "uuid-123" } }
            }
            """).RootElement;
        };

        _dataClient.ResourceHandlers[TraktResourceType.UserLists] = () => JsonDocument.Parse("[]").RootElement;
        _dataClient.ResourceHandlers[TraktResourceType.Watchlist] = () => JsonDocument.Parse("[]").RootElement;
        _dataClient.ResourceHandlers[TraktResourceType.WatchedMovies] = () => JsonDocument.Parse("[]").RootElement;
        _dataClient.ResourceHandlers[TraktResourceType.WatchedShows] = () => JsonDocument.Parse("[]").RootElement;
        _dataClient.ResourceHandlers[TraktResourceType.History] = () => JsonDocument.Parse("[]").RootElement;

        using var service = CreateService();
        var result = await service.SyncAsync();

        Assert.True(result.Success);
        Assert.Equal(2, accountAttempts);
        Assert.Equal(1, _authService.ForcedRefreshCallsCount);

        // Blocker 1: Exact refreshed token was bound
        Assert.Equal("token-fresh", _authService.RecordedTokenPassed);
    }

    [Fact]
    public async Task SyncAsync_401Unauthorized_RefreshThrowsInvalidGrant_AbortsSync_ResetsIsSyncing()
    {
        _authService.SetConnected("uuid-123", "token-stale");
        _authService.ExceptionToThrowOnRefresh = new TraktInvalidGrantException("Invalid grant on refresh");

        _dataClient.AccountToReturn = JsonDocument.Parse("""
        {
            "user": { "username": "tester", "ids": { "uuid": "uuid-123" } }
        }
        """).RootElement;

        _dataClient.ResourceHandlers[TraktResourceType.UserLists] = () =>
            throw new TraktClientException(TraktFailureKind.Unauthorized, "Unauthorized", "users/me/lists", HttpStatusCode.Unauthorized);

        using var service = CreateService();
        var result = await service.SyncAsync();

        Assert.False(result.Success);
        Assert.Equal(TraktSyncFailureKind.Unauthorized, result.FailureKind);
        Assert.False(service.GetSyncState().IsSyncing);
        Assert.Empty(_cacheRepository.AllEntries);
    }

    [Fact]
    public async Task SyncAsync_Second401AfterRefresh_AbortsSyncImmediately_SanitizesError_DoesNotContinue()
    {
        // Blocker 9: Second 401 after refresh must abort sync as Unauthorized/reconnect, not continue all resources; metadata error state sanitized
        _authService.SetConnected("uuid-123", "token-stale");
        _authService.RefreshedTokenToReturn = "token-fresh-rejected";

        _dataClient.AccountToReturn = JsonDocument.Parse("""
        {
            "user": { "username": "tester", "ids": { "uuid": "uuid-123" } }
        }
        """).RootElement;

        int userListsCalls = 0;
        int watchlistCalls = 0;

        _dataClient.ResourceHandlers[TraktResourceType.UserLists] = () =>
        {
            userListsCalls++;
            // Both the first attempt and retried attempt fail with 401
            throw new TraktClientException(TraktFailureKind.Unauthorized, "Unauthorized", "users/me/lists", HttpStatusCode.Unauthorized);
        };

        _dataClient.ResourceHandlers[TraktResourceType.Watchlist] = () =>
        {
            watchlistCalls++;
            return JsonDocument.Parse("[]").RootElement;
        };

        using var service = CreateService();
        var result = await service.SyncAsync();

        Assert.False(result.Success);
        Assert.Equal(TraktSyncFailureKind.Unauthorized, result.FailureKind);
        Assert.Equal(2, userListsCalls); // initial + 1 retry

        // Verification: Did NOT continue with other resources
        Assert.Equal(0, watchlistCalls);

        // Verification: Metadata error state sanitized (no raw token or headers)
        var state = service.GetSyncState();
        Assert.False(state.IsSyncing);
        Assert.Equal("Trakt authorization expired or rejected. Reconnection required.", state.LastSyncError);
        Assert.Equal(TraktSyncFailureKind.Unauthorized, state.LastFailureKind);
    }

    [Fact]
    public async Task SyncAsync_DuplicateCalls_RejectsBusy_DoesNotQueueDuplicates()
    {
        // Blocker 7: duplicate Sync calls should coalesce/reject busy, not queue unlimited duplicates
        _authService.SetConnected("uuid-123", "token-abc");
        _dataClient.AccountToReturn = JsonDocument.Parse("""
        {
            "user": { "username": "tester", "ids": { "uuid": "uuid-123" } }
        }
        """).RootElement;

        var networkStarted = new TaskCompletionSource<bool>();
        var allowFinish = new TaskCompletionSource<bool>();

        _dataClient.AsyncResourceHandler = async (spec, ct) =>
        {
            networkStarted.TrySetResult(true);
            await allowFinish.Task;
            return JsonDocument.Parse("[]").RootElement;
        };

        using var service = CreateService();

        var syncTask1 = Task.Run(() => service.SyncAsync());

        await networkStarted.Task;

        // Second call while first is in-flight
        var result2 = await service.SyncAsync();

        // Must reject busy immediately without queuing
        Assert.False(result2.Success);
        Assert.Equal(TraktSyncFailureKind.Busy, result2.FailureKind);

        // Allow first to complete
        allowFinish.TrySetResult(true);
        var result1 = await syncTask1;

        Assert.True(result1.Success);
    }

    [Fact]
    public async Task SyncAsync_MalformedListCatalog_RejectsBeforeCommit_RetainsGoodCache()
    {
        // Blocker 6: SyncResource saves payload before parsing list catalog => malformed entries can replace valid catalog.
        // Validate required array/list IDs before commit; do not drop malformed list catalog rows silently; failure retains good cache.
        const string accountId = "uuid-123";
        _authService.SetConnected(accountId, "token-abc");

        _dataClient.AccountToReturn = JsonDocument.Parse("""
        {
            "user": { "username": "tester", "ids": { "uuid": "uuid-123" } }
        }
        """).RootElement;

        // Pre-populate cache with a valid list
        _cacheRepository.SaveComplete(
            accountId,
            TraktCacheResourceKeys.Lists,
            """[{"ids":{"trakt":10,"slug":"good-list"},"name":"Good List"}]""",
            DateTimeOffset.UtcNow);

        // Remote returns malformed list catalog: entry missing 'ids.trakt'
        _dataClient.ResourceHandlers[TraktResourceType.UserLists] = () => JsonDocument.Parse("""
        [
            {
                "name": "Corrupted List Entry Without Trakt ID",
                "ids": { "slug": "bad-list" }
            }
        ]
        """).RootElement;

        _dataClient.ResourceHandlers[TraktResourceType.Watchlist] = () => JsonDocument.Parse("[]").RootElement;
        _dataClient.ResourceHandlers[TraktResourceType.WatchedMovies] = () => JsonDocument.Parse("[]").RootElement;
        _dataClient.ResourceHandlers[TraktResourceType.WatchedShows] = () => JsonDocument.Parse("[]").RootElement;
        _dataClient.ResourceHandlers[TraktResourceType.History] = () => JsonDocument.Parse("[]").RootElement;

        using var service = CreateService();
        var result = await service.SyncAsync();

        // Sync must record failure on lists
        Assert.False(result.Success);
        var listOutcome = result.ResourceOutcomes.Single(o => o.ResourceKey == TraktCacheResourceKeys.Lists);
        Assert.False(listOutcome.Success);
        Assert.Equal(TraktCacheErrorCode.InvalidResponse, listOutcome.ErrorCode);

        // Verification: Valid catalog in cache was RETAINED, not replaced by malformed payload!
        var cached = _cacheRepository.Get(accountId, TraktCacheResourceKeys.Lists);
        Assert.NotNull(cached?.PayloadJson);
        Assert.Contains("Good List", cached.PayloadJson);
    }

    [Fact]
    public async Task ResolveTargetAccount_OfflineBrowsing_And_ActiveSessionIsolation()
    {
        // Blocker 2: ResolveTargetAccount current??last leaks old account offline cache during new connection with null current UUID;
        // permit last ONLY while disconnected; when connected unknownUUID return no cache.
        // Explicit accountId cannot bypass active-session isolation; enforce current UUID for connected session.

        const string oldAccountId = "account-old";
        const string newAccountId = "account-new";

        _cacheRepository.SaveComplete(oldAccountId, TraktCacheResourceKeys.Lists, """[{"ids":{"trakt":1,"slug":"old"},"name":"Old List"}]""", DateTimeOffset.UtcNow);
        _cacheRepository.SaveComplete(newAccountId, TraktCacheResourceKeys.Lists, """[{"ids":{"trakt":2,"slug":"new"},"name":"New List"}]""", DateTimeOffset.UtcNow);

        _settingsRepository.Set("trakt_last_account_id", oldAccountId, DateTimeOffset.UtcNow);

        // Scenario 1: Disconnected -> permits last account for offline browsing
        await _authService.DisconnectAsync();
        using (var serviceDisconnected = CreateService())
        {
            var listsDefault = serviceDisconnected.GetLists();
            Assert.Single(listsDefault);
            Assert.Equal("Old List", listsDefault[0].Name);

            // Explicit accountId matching lastAccountId permitted while disconnected
            var listsExplicit = serviceDisconnected.GetLists(oldAccountId);
            Assert.Single(listsExplicit);
            Assert.Equal("Old List", listsExplicit[0].Name);
        }

        // Scenario 2: Connected with unknown UUID (e.g. fresh connection before first sync) -> return no cache
        _authService.SetConnected(null!, "token-xyz"); // connected but null accountId
        using (var serviceConnectedUnknown = CreateService())
        {
            var listsUnknown = serviceConnectedUnknown.GetLists();
            // Must NOT leak old account cache!
            Assert.Empty(listsUnknown);
        }

        // Scenario 3: Connected with new account -> explicit accountId cannot bypass active session isolation
        _authService.SetConnected(newAccountId, "token-new");
        using (var serviceConnected = CreateService())
        {
            // Default returns active account cache
            var listsActive = serviceConnected.GetLists();
            Assert.Single(listsActive);
            Assert.Equal("New List", listsActive[0].Name);

            // Attempting to query old account while connected to new account must be isolated
            var listsOldBypassAttempt = serviceConnected.GetLists(oldAccountId);
            Assert.Empty(listsOldBypassAttempt);
        }
    }

    [Fact]
    public async Task Dispose_CancelsActiveNetwork_DoesNotDisposeSemaphoreWhileOpUnwinds()
    {
        // Blocker 3 & 5: Dispose cancels active network and avoids disposing semaphore while active ops unwind
        _authService.SetConnected("uuid-123", "token-abc");
        _dataClient.AccountToReturn = JsonDocument.Parse("""
        {
            "user": { "username": "tester", "ids": { "uuid": "uuid-123" } }
        }
        """).RootElement;

        var networkStarted = new TaskCompletionSource<bool>();
        var allowUnwind = new TaskCompletionSource<bool>();

        _dataClient.AsyncResourceHandler = async (spec, ct) =>
        {
            networkStarted.TrySetResult(true);
            await allowUnwind.Task;
            ct.ThrowIfCancellationRequested();
            return JsonDocument.Parse("[]").RootElement;
        };

        var service = CreateService();

        var syncTask = Task.Run(async () =>
        {
            try
            {
                await service.SyncAsync();
            }
            catch (OperationCanceledException)
            {
            }
        });

        await networkStarted.Task;

        // Dispose service while op is running
        service.Dispose();

        // Allow active op to unwind
        allowUnwind.TrySetResult(true);

        // Verification: Task completes without ObjectDisposedException
        await syncTask;
    }

    [Fact]
    public void CacheReadMethods_CorruptedPayloadDegradesGracefully_DbErrorPropagates()
    {
        // Blocker 10: cache read methods must not swallow all programmer/DB errors into empty list; report invalid cache safely.
        const string accountId = "uuid-123";
        _authService.SetConnected(accountId, "token-abc");

        // Scenario A: Corrupted JSON payload degrades safely to empty list
        _cacheRepository.SaveComplete(accountId, TraktCacheResourceKeys.Lists, "{ malformed json...", DateTimeOffset.UtcNow);
        using var service = CreateService();

        var lists = service.GetLists(accountId);
        Assert.Empty(lists);

        // Scenario B: Database error from repository is NOT swallowed into empty list; must propagate
        _cacheRepository.ThrowOnGet = new InvalidOperationException("Simulated SQLite disk corruption error");

        Assert.Throws<InvalidOperationException>(() => service.GetLists(accountId));
    }

    [Fact]
    public async Task SyncAsync_SuccessfulSync_ClearsPreviousError()
    {
        // Blocker 4: UpdateState successful error=null must clear old errors rather than ?? preserving previous
        _authService.SetConnected("uuid-123", "token-abc");

        _dataClient.AccountToReturn = JsonDocument.Parse("""
        {
            "user": { "username": "tester", "ids": { "uuid": "uuid-123" } }
        }
        """).RootElement;

        // Run 1: Fails on watchlist
        _dataClient.ResourceHandlers[TraktResourceType.UserLists] = () => JsonDocument.Parse("[]").RootElement;
        _dataClient.ResourceHandlers[TraktResourceType.Watchlist] = () =>
            throw new TraktClientException(TraktFailureKind.Network, "Drop", "sync/watchlist");
        _dataClient.ResourceHandlers[TraktResourceType.WatchedMovies] = () => JsonDocument.Parse("[]").RootElement;
        _dataClient.ResourceHandlers[TraktResourceType.WatchedShows] = () => JsonDocument.Parse("[]").RootElement;
        _dataClient.ResourceHandlers[TraktResourceType.History] = () => JsonDocument.Parse("[]").RootElement;

        using var service = CreateService();
        var result1 = await service.SyncAsync();

        Assert.False(result1.Success);
        Assert.NotNull(service.GetSyncState().LastSyncError);

        // Run 2: Watchlist succeeds
        _dataClient.ResourceHandlers[TraktResourceType.Watchlist] = () => JsonDocument.Parse("[]").RootElement;

        var result2 = await service.SyncAsync();

        Assert.True(result2.Success);
        // Blocker 4 verification: LastSyncError was cleared to null
        Assert.Null(service.GetSyncState().LastSyncError);
    }

    [Fact]
    public async Task SyncAsync_CancelledBeforeSettingsWrite_PreventsSettingsWriteAndSuccessPublish()
    {
        // Blocker 8: Ensure cancelled/generation before last-account settings writes and before success publish
        _authService.SetConnected("uuid-123", "token-abc");

        using var cts = new CancellationTokenSource();

        _dataClient.CustomAccountHandler = token =>
        {
            cts.Cancel();
            return JsonDocument.Parse("""
            {
                "user": { "username": "tester", "ids": { "uuid": "uuid-123" } }
            }
            """).RootElement;
        };

        using var service = CreateService();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SyncAsync(cts.Token));

        // Settings repository must NOT have been written
        Assert.Null(_settingsRepository.Get("trakt_last_account_id"));

        // Success timestamp must NOT have been published
        Assert.Null(service.GetSyncState().LastSyncSuccessAt);
    }

    [Fact]
    public void GetCacheSummaries_And_GetListItems_HidesOrphanDeletedListRows()
    {
        const string accountId = "uuid-123";
        _settingsRepository.Set("trakt_last_account_id", accountId, DateTimeOffset.UtcNow);

        _cacheRepository.SaveComplete(
            accountId,
            TraktCacheResourceKeys.Lists,
            """[{"ids":{"trakt":42,"slug":"active-list"},"name":"Active"}]""",
            DateTimeOffset.UtcNow);

        _cacheRepository.SaveComplete(
            accountId,
            TraktCacheResourceKeys.ListItems("42"),
            """[{"id":1001,"rank":1,"type":"movie","movie":{"title":"Film A"}}]""",
            DateTimeOffset.UtcNow);

        _cacheRepository.SaveComplete(
            accountId,
            TraktCacheResourceKeys.ListItems("999"),
            """[{"id":2001,"rank":1,"type":"movie","movie":{"title":"Deleted List Film"}}]""",
            DateTimeOffset.UtcNow);

        using var service = CreateService();

        var summaries = service.GetCacheSummaries(accountId);

        Assert.Contains(summaries, s => s.ResourceKey == TraktCacheResourceKeys.Lists);
        Assert.Contains(summaries, s => s.ResourceKey == TraktCacheResourceKeys.ListItems("42"));
        Assert.DoesNotContain(summaries, s => s.ResourceKey == TraktCacheResourceKeys.ListItems("999"));

        var orphanItems = service.GetListItems(999, accountId);
        Assert.Empty(orphanItems);

        var activeItems = service.GetListItems(42, accountId);
        Assert.Single(activeItems);
        Assert.Equal(1001, activeItems[0].Id);
    }

    [Fact]
    public void UIReadMethods_PreservesRawElementOrder_64BitIds_AndUnknownPersonTypes()
    {
        const string accountId = "uuid-123";
        _settingsRepository.Set("trakt_last_account_id", accountId, DateTimeOffset.UtcNow);

        const long bigId1 = 5000000000L;
        const long bigId2 = 6000000000L;

        var json = $$"""
        [
            {
                "id": {{bigId1}},
                "rank": 1,
                "type": "person",
                "notes": "Director notes"
            },
            {
                "id": {{bigId2}},
                "rank": 2,
                "type": "unknown_future_type",
                "notes": "Special unknown type"
            },
            {
                "id": 7001,
                "rank": 3,
                "type": "movie",
                "movie": { "title": "Test Movie", "ids": { "trakt": 999 } }
            }
        ]
        """;

        _cacheRepository.SaveComplete(
            accountId,
            TraktCacheResourceKeys.Lists,
            """[{"ids":{"trakt":1,"slug":"mixed"},"name":"Mixed"}]""",
            DateTimeOffset.UtcNow);

        _cacheRepository.SaveComplete(
            accountId,
            TraktCacheResourceKeys.ListItems("1"),
            json,
            DateTimeOffset.UtcNow);

        using var service = CreateService();
        var items = service.GetListItems(1, accountId);

        Assert.Equal(3, items.Count);

        Assert.Equal(bigId1, items[0].Id);
        Assert.Equal("person", items[0].Type);
        Assert.Equal("Director notes", items[0].Notes);
        Assert.Equal(JsonValueKind.Object, items[0].RawElement.ValueKind);

        Assert.Equal(bigId2, items[1].Id);
        Assert.Equal("unknown_future_type", items[1].Type);

        Assert.Equal(7001, items[2].Id);
        Assert.Equal("movie", items[2].Type);
        Assert.Equal("Test Movie", items[2].Title);
    }

    // --- Material Race & Coordinator Concurrency Fix Tests ---

    [Fact]
    public async Task SyncAsync_GateBoundaryRace_DisconnectBeforeCtsRegistration_AbortsSyncWithoutWrites()
    {
        // Finding agy-527ccaeb: Sync acquires gate, then Disconnect invalidates generation and cancels CTS (null)
        // before Sync installs CTS and captures generation.
        // Verification: Atomic generation capture before gate and recheck under _ctsLock detects generation invalidation,
        // aborts sync cleanly without any HTTP fetches, cache writes, settings writes, or state updates.
        _authService.SetConnected("uuid-123", "token-bound-1");

        _dataClient.AccountToReturn = JsonDocument.Parse("""
        {
            "user": {
                "username": "tester",
                "ids": { "uuid": "uuid-123" }
            }
        }
        """).RootElement;

        var gateAcquired = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var disconnectInvalidated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowRegistration = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var service = CreateService();

        // Minimum test seam: intercept execution after gate acquisition but BEFORE entering _ctsLock
        service.BeforeSyncRegistrationAsync = async () =>
        {
            gateAcquired.TrySetResult(true);
            await allowRegistration.Task;
        };

        service.OnCancelActiveNetworkFinished = () => disconnectInvalidated.TrySetResult(true);

        var syncTask = Task.Run(async () =>
        {
            try
            {
                await service.SyncAsync();
            }
            catch (OperationCanceledException)
            {
            }
        });

        // Deterministic wait for gate acquisition without sleeps
        await gateAcquired.Task;

        // Disconnect while Sync holds gate but before CTS registration
        var disconnectTask = Task.Run(async () => await service.DisconnectAsync());

        // Wait for Disconnect to execute CancelActiveNetwork and invalidate generation
        await disconnectInvalidated.Task;

        // Now allow Sync to proceed to its registration boundary
        allowRegistration.TrySetResult(true);

        await syncTask;
        await disconnectTask;

        // Verification: Zero network requests made by sync
        Assert.Equal(0, _dataClient.AccountCallsCount);

        // Verification: Zero cache writes occurred
        Assert.Empty(_cacheRepository.AllEntries);
        Assert.Empty(_cacheRepository.AllFailures);

        // Verification: Settings repository was not touched
        Assert.Null(_settingsRepository.Get("trakt_last_account_id"));

        // Verification: State is completely disconnected and clean
        var state = service.GetSyncState();
        Assert.False(state.IsSyncing);
        Assert.Null(state.CurrentAccountId);
        Assert.Null(state.LastSyncSuccessAt);
        Assert.False(_authService.GetAuthState().IsConnected);
    }

    [Fact]
    public async Task BeginAuthAsync_InFlight_CancelledByDisconnect_ThrowsAndUnwindsCleanly()
    {
        // Finding: BeginAuth HTTP request must be registered in _activeNetworkCts so Disconnect/Dispose cancels it
        var authStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var authCancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        _authService.AsyncBeginAuthHandler = async ct =>
        {
            authStarted.TrySetResult(true);
            using (ct.Register(() => authCancelled.TrySetResult(true)))
            {
                await authCancelled.Task;
            }
            ct.ThrowIfCancellationRequested();
            return new TraktDeviceCode("dev-1", "USER-1", "https://trakt.tv/activate", 600, 5, DateTimeOffset.UtcNow);
        };

        using var service = CreateService();

        var beginAuthTask = Task.Run(async () =>
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.BeginAuthAsync());
        });

        await authStarted.Task;

        // Disconnect immediately while BeginAuth is in-flight
        await service.DisconnectAsync();
        await beginAuthTask;

        // Gate released, service still operational
        Assert.False(service.GetSyncState().IsSyncing);
    }

    [Fact]
    public async Task BeginAuthAsync_GenerationChangedDuringAuth_RejectsLateCode()
    {
        // Finding: Return code only after generation/token recheck. No late state publication.
        var authStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var disconnectInvalidated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowAuthReturn = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        _authService.AsyncBeginAuthHandler = async ct =>
        {
            authStarted.TrySetResult(true);
            await allowAuthReturn.Task;
            return new TraktDeviceCode("dev-late", "USER-LATE", "https://trakt.tv/activate", 600, 5, DateTimeOffset.UtcNow);
        };

        using var service = CreateService();

        var beginAuthTask = Task.Run(async () =>
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.BeginAuthAsync());
        });

        await authStarted.Task;

        // Wire hook only after BeginAuth has started so it catches Disconnect's invalidation
        service.OnCancelActiveNetworkFinished = () => disconnectInvalidated.TrySetResult(true);

        // Concurrent disconnect increments generation
        var disconnectTask = Task.Run(async () => await service.DisconnectAsync());

        // Wait until Disconnect has invalidated generation
        await disconnectInvalidated.Task;

        // Unblock auth handler
        allowAuthReturn.TrySetResult(true);

        await beginAuthTask;
        await disconnectTask;

        // State was not polluted by late auth code
        var state = service.GetSyncState();
        Assert.False(state.AuthState.IsConnected);
    }

    [Fact]
    public async Task CompleteAuthAsync_CancelledGateWait_NoLeak_AllowsSubsequentOperations()
    {
        // Finding: CompleteAuth registered before gate lifetime caused CTS leakage; cancelled gate wait must not leak.
        var holdGateStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowHoldGateRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var service = CreateService();

        // Hold gate using a dummy BeginAuth
        _authService.AsyncBeginAuthHandler = async ct =>
        {
            holdGateStarted.TrySetResult(true);
            await allowHoldGateRelease.Task;
            return new TraktDeviceCode("d", "u", "https://trakt.tv", 60, 5, DateTimeOffset.UtcNow);
        };

        var holdTask = Task.Run(() => service.BeginAuthAsync());
        await holdGateStarted.Task;

        // Now call CompleteAuth with a token that gets cancelled while waiting for gate
        using var waitCts = new CancellationTokenSource();
        var completeTask = Task.Run(async () =>
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                service.CompleteAuthAsync(new TraktDeviceCode("d", "u", "uri", 60, 5, DateTimeOffset.UtcNow), waitCts.Token));
        });

        // Cancel while waiting for gate
        waitCts.Cancel();
        await completeTask;

        // Release holding operation
        allowHoldGateRelease.TrySetResult(true);
        try
        {
            await holdTask;
        }
        catch (OperationCanceledException)
        {
        }

        // Subsequent operation can immediately acquire gate and succeed cleanly
        var state = await service.CompleteAuthAsync(new TraktDeviceCode("d2", "u2", "uri", 60, 5, DateTimeOffset.UtcNow));
        Assert.True(state.IsConnected);
    }

    [Fact]
    public async Task ResolveTargetAccount_OfflineArbitraryAccount_IsIsolatedAndRejected()
    {
        // Finding: Offline ResolveTargetAccount explicit arbitrary accountId must only permit lastAccount while disconnected,
        // current account while connected; null connectedUUID no cache.
        const string lastAccountId = "verified-last-account";
        const string arbitraryAccountId = "arbitrary-foreign-account";

        _cacheRepository.SaveComplete(lastAccountId, TraktCacheResourceKeys.Lists, """[{"ids":{"trakt":1,"slug":"last"},"name":"Last List"}]""", DateTimeOffset.UtcNow);
        _cacheRepository.SaveComplete(arbitraryAccountId, TraktCacheResourceKeys.Lists, """[{"ids":{"trakt":99,"slug":"foreign"},"name":"Foreign List"}]""", DateTimeOffset.UtcNow);

        _settingsRepository.Set("trakt_last_account_id", lastAccountId, DateTimeOffset.UtcNow);

        await _authService.DisconnectAsync();
        using var service = CreateService();

        // 1. Default disconnected access returns last account cache
        var listsDefault = service.GetLists();
        Assert.Single(listsDefault);
        Assert.Equal("Last List", listsDefault[0].Name);

        // 2. Explicit query for last account returns last account cache
        var listsLast = service.GetLists(lastAccountId);
        Assert.Single(listsLast);
        Assert.Equal("Last List", listsLast[0].Name);

        // 3. Explicit arbitrary foreign account MUST BE REJECTED while disconnected
        var listsArbitrary = service.GetLists(arbitraryAccountId);
        Assert.Empty(listsArbitrary);

        var itemsArbitrary = service.GetListItems(99, arbitraryAccountId);
        Assert.Empty(itemsArbitrary);

        var watchlistArbitrary = service.GetWatchlist(arbitraryAccountId);
        Assert.Empty(watchlistArbitrary);

        var moviesArbitrary = service.GetWatchedMovies(arbitraryAccountId);
        Assert.Empty(moviesArbitrary);

        var showsArbitrary = service.GetWatchedShows(arbitraryAccountId);
        Assert.Empty(showsArbitrary);

        var historyArbitrary = service.GetHistory(arbitraryAccountId);
        Assert.Empty(historyArbitrary);

        var rawArbitrary = service.GetRawResource(TraktCacheResourceKeys.Lists, arbitraryAccountId);
        Assert.Null(rawArbitrary);

        var summariesArbitrary = service.GetCacheSummaries(arbitraryAccountId);
        Assert.Empty(summariesArbitrary);

        // 4. Disconnected with null lastAccountId returns empty for everything
        _settingsRepository.Delete("trakt_last_account_id");
        using var serviceNoLastAccount = CreateService();
        Assert.Empty(serviceNoLastAccount.GetLists());
        Assert.Empty(serviceNoLastAccount.GetLists(lastAccountId));
    }

    [Fact]
    public async Task Dispose_ActiveOperations_SyncBeginComplete_AllUnwindCleanlyWithoutException()
    {
        // Finding: Dispose cancels active network operations without throwing ObjectDisposedException on semaphore release
        _authService.SetConnected("uuid-123", "token-abc");

        // Test 1: Sync active during Dispose
        var syncStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var syncCancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        _dataClient.AccountToReturn = JsonDocument.Parse("""{"user":{"username":"t","ids":{"uuid":"uuid-123"}}}""").RootElement;
        _dataClient.AsyncResourceHandler = async (spec, ct) =>
        {
            syncStarted.TrySetResult(true);
            using (ct.Register(() => syncCancelled.TrySetResult(true)))
            {
                await syncCancelled.Task;
            }
            ct.ThrowIfCancellationRequested();
            return JsonDocument.Parse("[]").RootElement;
        };

        var service1 = CreateService();
        var syncTask = Task.Run(async () =>
        {
            try
            {
                await service1.SyncAsync();
            }
            catch (OperationCanceledException)
            {
            }
        });

        await syncStarted.Task;
        service1.Dispose();
        await syncTask; // must not throw ObjectDisposedException

        // Subsequent call throws ObjectDisposedException
        await Assert.ThrowsAsync<ObjectDisposedException>(() => service1.SyncAsync());

        // Test 2: BeginAuth active during Dispose
        var beginStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var beginCancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        _authService.AsyncBeginAuthHandler = async ct =>
        {
            beginStarted.TrySetResult(true);
            using (ct.Register(() => beginCancelled.TrySetResult(true)))
            {
                await beginCancelled.Task;
            }
            ct.ThrowIfCancellationRequested();
            return new TraktDeviceCode("d", "u", "uri", 60, 5, DateTimeOffset.UtcNow);
        };

        var service2 = CreateService();
        var beginTask = Task.Run(async () =>
        {
            try
            {
                await service2.BeginAuthAsync();
            }
            catch (OperationCanceledException)
            {
            }
        });

        await beginStarted.Task;
        service2.Dispose();
        await beginTask; // must not throw ObjectDisposedException
        await Assert.ThrowsAsync<ObjectDisposedException>(() => service2.BeginAuthAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Auth_invalidation_keeps_its_own_generation_when_disconnect_intervenes(bool complete)
    {
        using var service = CreateService();
        Task? disconnect = null;
        service.OnCancelActiveNetworkFinished = () =>
        {
            service.OnCancelActiveNetworkFinished = null;
            disconnect = service.DisconnectAsync();
        };

        if (complete)
        {
            var code = new TraktDeviceCode("test-device", "test-user", "https://trakt.tv/activate", 60, 5, DateTimeOffset.UtcNow);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CompleteAuthAsync(code));
        }
        else
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.BeginAuthAsync());
        }

        Assert.NotNull(disconnect);
        await disconnect!;
        Assert.False(service.GetSyncState().AuthState.IsConnected);
    }

    // --- Fakes ---

    private sealed class FakeTraktAuthService : ITraktAuthService
    {
        private TraktAuthState _state = TraktAuthState.NotConfigured;
        private string _token = "fake_access_token";

        public int ForcedRefreshCallsCount { get; private set; }
        public string? RefreshedTokenToReturn { get; set; }
        public Exception? ExceptionToThrowOnRefresh { get; set; }
        public bool RecordVerifiedAccountResult { get; set; } = true;
        public string? RecordedTokenPassed { get; private set; }

        public Func<CancellationToken, Task<TraktDeviceCode>>? AsyncBeginAuthHandler { get; set; }
        public Func<TraktDeviceCode, CancellationToken, Task<TraktAuthState>>? AsyncCompleteAuthHandler { get; set; }

        public void SetConnected(string accountId, string token)
        {
            _token = token;
            _state = TraktAuthState.ConnectedState(accountId, DateTimeOffset.UtcNow.AddHours(1));
        }

        public TraktAuthState GetAuthState() => _state;

        public async Task<TraktDeviceCode> BeginDeviceAuthAsync(CancellationToken cancellationToken = default)
        {
            if (AsyncBeginAuthHandler != null)
            {
                return await AsyncBeginAuthHandler(cancellationToken);
            }
            return new TraktDeviceCode("dev-1", "USER-1", "https://trakt.tv/activate", 600, 5, DateTimeOffset.UtcNow);
        }

        public async Task<TraktAuthState> CompleteDeviceAuthAsync(TraktDeviceCode deviceCode, CancellationToken cancellationToken = default)
        {
            if (AsyncCompleteAuthHandler != null)
            {
                return await AsyncCompleteAuthHandler(deviceCode, cancellationToken);
            }
            _state = TraktAuthState.ConnectedState("uuid-from-device", DateTimeOffset.UtcNow.AddHours(1));
            return _state;
        }

        public Task<string> GetAccessTokenAsync(bool forceRefresh = false, string? rejectedAccessToken = null, CancellationToken cancellationToken = default)
        {
            if (forceRefresh)
            {
                ForcedRefreshCallsCount++;
                if (ExceptionToThrowOnRefresh != null)
                {
                    throw ExceptionToThrowOnRefresh;
                }
                _token = RefreshedTokenToReturn ?? "refreshed_token";
            }
            return Task.FromResult(_token);
        }

        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            _state = TraktAuthState.NotConfigured;
            _token = "";
            return Task.CompletedTask;
        }

        public Task RejectAccessTokenAsync(string rejectedAccessToken, CancellationToken cancellationToken = default)
        {
            if (_token == rejectedAccessToken) _state = TraktAuthState.RequiresReconnection;
            return Task.CompletedTask;
        }

        // Blocker 1: expectedAccessToken is non-null required string, synchronous method removed
        public Task<bool> RecordVerifiedAccountAsync(string accountId, string expectedAccessToken, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(expectedAccessToken);
            RecordedTokenPassed = expectedAccessToken;

            if (RecordVerifiedAccountResult)
            {
                _state = TraktAuthState.ConnectedState(accountId, DateTimeOffset.UtcNow.AddHours(1));
            }
            return Task.FromResult(RecordVerifiedAccountResult);
        }
    }

    private sealed class FakeTraktDataClient : ITraktDataClient
    {
        public JsonElement? AccountToReturn { get; set; }
        public Func<string, JsonElement>? CustomAccountHandler { get; set; }
        public Dictionary<TraktResourceType, Func<JsonElement>> ResourceHandlers { get; } = new();
        public Func<TraktResourceSpec, CancellationToken, Task<JsonElement>>? AsyncResourceHandler { get; set; }
        public int AccountCallsCount { get; private set; }

        public Task<JsonElement> GetAccountAsync(string clientId, string accessToken, CancellationToken cancellationToken = default)
        {
            AccountCallsCount++;
            if (CustomAccountHandler != null)
            {
                return Task.FromResult(CustomAccountHandler(accessToken));
            }

            if (AccountToReturn == null)
            {
                throw new InvalidOperationException("AccountToReturn not configured.");
            }
            return Task.FromResult(AccountToReturn.Value);
        }

        public async Task<JsonElement> GetResourceAsync(TraktResourceSpec resource, string clientId, string accessToken, CancellationToken cancellationToken = default)
        {
            if (AsyncResourceHandler != null)
            {
                return await AsyncResourceHandler(resource, cancellationToken);
            }

            if (ResourceHandlers.TryGetValue(resource.ResourceType, out var handler))
            {
                return handler();
            }

            return JsonDocument.Parse("[]").RootElement;
        }
    }

    private sealed class FakeTraktCacheRepository : ITraktCacheRepository
    {
        private readonly Dictionary<(string AccountId, string ResourceKey), TraktCacheEntry> _entries = new();
        private readonly List<(string AccountId, string ResourceKey, TraktCacheErrorCode ErrorCode)> _failures = new();

        public Exception? ThrowOnGet { get; set; }

        public IReadOnlyDictionary<(string, string), TraktCacheEntry> AllEntries => _entries;
        public IReadOnlyList<(string AccountId, string ResourceKey, TraktCacheErrorCode ErrorCode)> AllFailures => _failures;

        public bool HasEntry(string accountId, string resourceKey) =>
            _entries.ContainsKey((accountId, resourceKey));

        public bool HasFailure(string accountId, string resourceKey, TraktCacheErrorCode code) =>
            _failures.Any(f => f.AccountId == accountId && f.ResourceKey == resourceKey && f.ErrorCode == code);

        public TraktCacheEntry? Get(string accountId, string resourceKey)
        {
            if (ThrowOnGet != null)
            {
                throw ThrowOnGet;
            }

            _entries.TryGetValue((accountId, resourceKey), out var entry);
            return entry;
        }

        public IReadOnlyList<TraktCacheSummary> List(string accountId)
        {
            if (ThrowOnGet != null)
            {
                throw ThrowOnGet;
            }

            return _entries.Values
                .Where(e => e.AccountId == accountId)
                .Select(e => new TraktCacheSummary(e.AccountId, e.ResourceKey, e.SucceededAt, e.AttemptedAt, e.ErrorCode))
                .ToList();
        }

        public void SaveComplete(string accountId, string resourceKey, string payloadJson, DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _entries[(accountId, resourceKey)] = new TraktCacheEntry(accountId, resourceKey, payloadJson, now, now, null);
        }

        public void RecordFailure(string accountId, string resourceKey, TraktCacheErrorCode errorCode, DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _failures.Add((accountId, resourceKey, errorCode));

            if (_entries.TryGetValue((accountId, resourceKey), out var existing))
            {
                _entries[(accountId, resourceKey)] = existing with { AttemptedAt = now, ErrorCode = errorCode.ToString() };
            }
            else
            {
                _entries[(accountId, resourceKey)] = new TraktCacheEntry(accountId, resourceKey, null, null, now, errorCode.ToString());
            }
        }

        public void DeleteAccount(string accountId, CancellationToken cancellationToken = default)
        {
            var keysToRemove = _entries.Keys.Where(k => k.AccountId == accountId).ToList();
            foreach (var k in keysToRemove)
            {
                _entries.Remove(k);
            }
        }
    }

    private sealed class FakeSettingsRepository : ISettingsRepository
    {
        private readonly Dictionary<string, string> _settings = new();

        public string? Get(string key) => _settings.TryGetValue(key, out var val) ? val : null;

        public void Set(string key, string value, DateTimeOffset updatedAt) => _settings[key] = value;

        public void Delete(string key) => _settings.Remove(key);
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;
        public FakeTimeProvider(DateTimeOffset initial) => _utcNow = initial;
        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
