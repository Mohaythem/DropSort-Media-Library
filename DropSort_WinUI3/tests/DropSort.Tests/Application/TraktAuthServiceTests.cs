using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DropSort.Application.Trakt;
using Xunit;

namespace DropSort.Tests.Application;

public sealed class TraktAuthServiceTests
{
    [Fact]
    public async Task Late_rejection_cannot_invalidate_a_different_active_token()
    {
        _credentialStore.Write(new TraktCredentialPayload(_config.ClientId, "replacement", "refresh", "bearer", 7200,
            "public", _timeProvider.GetUtcNow().ToUnixTimeSeconds()));
        var service = CreateService();
        await service.RejectAccessTokenAsync("old-token");
        Assert.True(service.GetAuthState().IsConnected);
        Assert.Equal("replacement", await service.GetAccessTokenAsync());
    }

    [Fact]
    public async Task Failed_rejection_marker_clears_memory_and_attempts_credential_removal()
    {
        _credentialStore.Write(new TraktCredentialPayload(_config.ClientId, "rejected", "refresh", "bearer", 7200,
            "public", _timeProvider.GetUtcNow().ToUnixTimeSeconds()));
        var service = CreateService();
        _credentialStore.ThrowOnWrite = new InvalidOperationException("synthetic storage failure");
        await Assert.ThrowsAsync<TraktAuthException>(() => service.RejectAccessTokenAsync("rejected"));
        Assert.True(service.GetAuthState().RequiresReconnect);
        Assert.False(service.GetAuthState().IsConnected);
        Assert.Null(_credentialStore.Read());
        await Assert.ThrowsAsync<TraktAuthException>(() => service.GetAccessTokenAsync());
        Assert.Equal(0, _authClient.RefreshCallsCount);
    }

    private readonly FakeTraktAuthClient _authClient = new();
    private readonly FakeTraktCredentialStore _credentialStore = new();
    private readonly TraktAuthConfig _config = new("test-client-id", "https://localhost/callback");
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 10, 21, 0, 0, 0, TimeSpan.Zero));
    private readonly List<TimeSpan> _recordedDelays = [];

    private TraktAuthService CreateService()
    {
        return new TraktAuthService(
            _authClient,
            _credentialStore,
            _config,
            _timeProvider,
            (delay, ct) =>
            {
                _recordedDelays.Add(delay);
                _timeProvider.Advance(delay);
                return Task.CompletedTask;
            });
    }

    [Fact]
    public async Task BeginDeviceAuth_AssignsUniqueSessionAndCode_TracksActiveAttempt()
    {
        _authClient.DeviceCodeToReturn = new TraktDeviceCode("dev-1", "USER-1", "https://trakt.tv/activate", 600, 5, _timeProvider.GetUtcNow());
        var service = CreateService();

        var result = await service.BeginDeviceAuthAsync();

        Assert.Equal("dev-1", result.DeviceCode);
        Assert.Equal("USER-1", result.UserCode);
        Assert.True(result.SessionId > 0);
        Assert.Equal("test-client-id", _authClient.LastClientIdUsedForCode);
    }

    [Fact]
    public async Task CompleteDeviceAuth_WaitsAtLeastServerInterval_BeforeFirstPoll()
    {
        var service = CreateService();
        _authClient.DeviceCodeToReturn = new TraktDeviceCode("dev-1", "USER-1", "https://trakt.tv/activate", 600, 5, _timeProvider.GetUtcNow());
        var deviceCode = await service.BeginDeviceAuthAsync();

        var expectedTokens = new TraktTokenPair("access-1", "refresh-1", "bearer", 3600, "public", _timeProvider.GetUtcNow().ToUnixTimeSeconds());
        _authClient.PollResponses.Enqueue(new TraktPollResponse(TraktPollStatus.Pending));
        _authClient.PollResponses.Enqueue(new TraktPollResponse(TraktPollStatus.Success, Tokens: expectedTokens));

        var state = await service.CompleteDeviceAuthAsync(deviceCode);

        // Verification: delay occurs before first poll and subsequent poll
        Assert.Equal(2, _recordedDelays.Count);
        Assert.Equal(TimeSpan.FromSeconds(5), _recordedDelays[0]);
        Assert.Equal(TimeSpan.FromSeconds(5), _recordedDelays[1]);

        Assert.True(state.IsConnected);
        Assert.False(state.RequiresReconnect);
        Assert.Null(state.AccountId);
    }

    [Fact]
    public async Task CompleteDeviceAuth_ReturnsAuthStateWithoutExposingRawTokens()
    {
        var service = CreateService();
        _authClient.DeviceCodeToReturn = new TraktDeviceCode("dev-1", "USER-1", "https://trakt.tv/activate", 600, 5, _timeProvider.GetUtcNow());
        var deviceCode = await service.BeginDeviceAuthAsync();

        var expectedTokens = new TraktTokenPair("secret-access", "secret-refresh", "bearer", 3600, "public", _timeProvider.GetUtcNow().ToUnixTimeSeconds());
        _authClient.PollResponses.Enqueue(new TraktPollResponse(TraktPollStatus.Success, Tokens: expectedTokens));

        TraktAuthState authState = await service.CompleteDeviceAuthAsync(deviceCode);

        Assert.True(authState.IsConnected);
        Assert.False(authState.RequiresReconnect);
        Assert.Equal(deviceCode.CreatedAt.AddSeconds(3600), authState.ExpiresAt);

        // Caller obtains tokens securely only via GetAccessTokenAsync
        var token = await service.GetAccessTokenAsync();
        Assert.Equal("secret-access", token);
    }

    [Fact]
    public async Task CompleteDeviceAuth_ExpiresAtBasedOnOriginalCreatedAt_ExpiresWhenDeadlineReached()
    {
        var createdAt = _timeProvider.GetUtcNow();
        _timeProvider.Advance(TimeSpan.FromSeconds(8));

        var service = CreateService();
        // ExpiresIn is 10 seconds, but 8 seconds have already elapsed since createdAt
        _authClient.DeviceCodeToReturn = new TraktDeviceCode("dev-1", "USER-1", "https://trakt.tv/activate", 10, 5, createdAt);
        var deviceCode = await service.BeginDeviceAuthAsync();

        _authClient.PollResponses.Enqueue(new TraktPollResponse(TraktPollStatus.Pending));

        var ex = await Assert.ThrowsAsync<TraktAuthException>(() => service.CompleteDeviceAuthAsync(deviceCode));
        Assert.Contains("expired", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(_credentialStore.Read());
    }

    [Fact]
    public async Task CompleteDeviceAuth_Slowdown_429NeverShrinksInterval_UsesMaxOfCurrentPlus5AndRetryAfter()
    {
        var service = CreateService();
        _authClient.DeviceCodeToReturn = new TraktDeviceCode("dev-1", "USER-1", "https://trakt.tv/activate", 600, 10, _timeProvider.GetUtcNow());
        var deviceCode = await service.BeginDeviceAuthAsync();

        var expectedTokens = new TraktTokenPair("access-1", "refresh-1", "bearer", 3600, "public", _timeProvider.GetUtcNow().ToUnixTimeSeconds());

        // Slowdown 1: server suggests 2 seconds (less than current 10 + 5 = 15). Interval must become 15, not 2.
        _authClient.PollResponses.Enqueue(new TraktPollResponse(TraktPollStatus.Slowdown, RetryAfterSeconds: 2));
        // Slowdown 2: server suggests 25 seconds (greater than 15 + 5 = 20). Interval must become 25.
        _authClient.PollResponses.Enqueue(new TraktPollResponse(TraktPollStatus.Slowdown, RetryAfterSeconds: 25));
        _authClient.PollResponses.Enqueue(new TraktPollResponse(TraktPollStatus.Success, Tokens: expectedTokens));

        await service.CompleteDeviceAuthAsync(deviceCode);

        Assert.Equal(3, _recordedDelays.Count);
        Assert.Equal(TimeSpan.FromSeconds(10), _recordedDelays[0]);
        Assert.Equal(TimeSpan.FromSeconds(15), _recordedDelays[1]);
        Assert.Equal(TimeSpan.FromSeconds(25), _recordedDelays[2]);
    }

    [Fact]
    public async Task CompleteDeviceAuth_MultipleBeginCalls_InvalidatePreviousAttempt()
    {
        var service = CreateService();
        _authClient.DeviceCodeToReturn = new TraktDeviceCode("dev-1", "USER-1", "https://trakt.tv/activate", 600, 5, _timeProvider.GetUtcNow());
        var firstCode = await service.BeginDeviceAuthAsync();

        _authClient.DeviceCodeToReturn = new TraktDeviceCode("dev-2", "USER-2", "https://trakt.tv/activate", 600, 5, _timeProvider.GetUtcNow());
        var secondCode = await service.BeginDeviceAuthAsync();

        // Attempting to complete the first superseded device code must fail immediately
        var ex = await Assert.ThrowsAsync<TraktAuthException>(() => service.CompleteDeviceAuthAsync(firstCode));
        Assert.Contains("superseded", ex.Message, StringComparison.OrdinalIgnoreCase);

        // Completing the second active code succeeds
        var expectedTokens = new TraktTokenPair("access-2", "refresh-2", "bearer", 3600, "public", _timeProvider.GetUtcNow().ToUnixTimeSeconds());
        _authClient.PollResponses.Enqueue(new TraktPollResponse(TraktPollStatus.Success, Tokens: expectedTokens));

        var state = await service.CompleteDeviceAuthAsync(secondCode);
        Assert.True(state.IsConnected);
    }

    [Fact]
    public async Task CompleteDeviceAuth_LateCompletionAfterDisconnect_ThrowsAndDoesNotPersist()
    {
        var service = CreateService();
        _authClient.DeviceCodeToReturn = new TraktDeviceCode("dev-1", "USER-1", "https://trakt.tv/activate", 600, 5, _timeProvider.GetUtcNow());
        var deviceCode = await service.BeginDeviceAuthAsync();

        var tokens = new TraktTokenPair("access-1", "refresh-1", "bearer", 3600, "public", _timeProvider.GetUtcNow().ToUnixTimeSeconds());

        _authClient.OnPollAsync = async () =>
        {
            await service.DisconnectAsync();
            return new TraktPollResponse(TraktPollStatus.Success, Tokens: tokens);
        };

        await Assert.ThrowsAsync<TraktAuthException>(() => service.CompleteDeviceAuthAsync(deviceCode));

        Assert.Null(_credentialStore.Read());
        Assert.False(service.GetAuthState().IsConnected);
    }

    [Fact]
    public async Task CompleteDeviceAuth_CancelledAfterSuccessBeforeWrite_ThrowsAndDoesNotPersist()
    {
        var service = CreateService();
        _authClient.DeviceCodeToReturn = new TraktDeviceCode("dev-1", "USER-1", "https://trakt.tv/activate", 600, 5, _timeProvider.GetUtcNow());
        var deviceCode = await service.BeginDeviceAuthAsync();

        using var cts = new CancellationTokenSource();
        var tokens = new TraktTokenPair("access-1", "refresh-1", "bearer", 3600, "public", _timeProvider.GetUtcNow().ToUnixTimeSeconds());

        _authClient.OnPollAsync = () =>
        {
            cts.Cancel();
            return Task.FromResult(new TraktPollResponse(TraktPollStatus.Success, Tokens: tokens));
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CompleteDeviceAuthAsync(deviceCode, cts.Token));

        Assert.Null(_credentialStore.Read());
        Assert.False(service.GetAuthState().IsConnected);
    }

    [Fact]
    public async Task CompleteDeviceAuth_StoreFailureAfterNetworkSuccess_ThrowsAndRequiresReconnect()
    {
        var service = CreateService();
        _authClient.DeviceCodeToReturn = new TraktDeviceCode("dev-1", "USER-1", "https://trakt.tv/activate", 600, 5, _timeProvider.GetUtcNow());
        var deviceCode = await service.BeginDeviceAuthAsync();

        var tokens = new TraktTokenPair("access-1", "refresh-1", "bearer", 3600, "public", _timeProvider.GetUtcNow().ToUnixTimeSeconds());
        _authClient.PollResponses.Enqueue(new TraktPollResponse(TraktPollStatus.Success, Tokens: tokens));
        _credentialStore.ThrowOnWrite = new InvalidOperationException("OS credential vault write error");

        await Assert.ThrowsAsync<TraktAuthException>(() => service.CompleteDeviceAuthAsync(deviceCode));

        Assert.Null(_credentialStore.Read());
        var state = service.GetAuthState();
        Assert.False(state.IsConnected);
        Assert.True(state.RequiresReconnect);
    }

    [Fact]
    public async Task CompleteDeviceAuth_NetworkSuccess_AfterDeadlineExpiredDuringCall_ThrowsAndDoesNotPersist()
    {
        var service = CreateService();
        _authClient.DeviceCodeToReturn = new TraktDeviceCode("dev-1", "USER-1", "https://trakt.tv/activate", 600, 5, _timeProvider.GetUtcNow());
        var deviceCode = await service.BeginDeviceAuthAsync();

        var tokens = new TraktTokenPair("access-1", "refresh-1", "bearer", 3600, "public", _timeProvider.GetUtcNow().ToUnixTimeSeconds());

        _authClient.OnPollAsync = () =>
        {
            // Advance time past the deadline during network execution
            _timeProvider.Advance(TimeSpan.FromSeconds(605));
            return Task.FromResult(new TraktPollResponse(TraktPollStatus.Success, Tokens: tokens));
        };

        var ex = await Assert.ThrowsAsync<TraktAuthException>(() => service.CompleteDeviceAuthAsync(deviceCode));
        Assert.Contains("expired", ex.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Null(_credentialStore.Read());
        Assert.False(service.GetAuthState().IsConnected);
    }

    [Fact]
    public async Task CompleteDeviceAuth_NetworkSuccess_AfterSupersededDuringCall_ThrowsAndDoesNotPersist()
    {
        var service = CreateService();
        _authClient.DeviceCodeToReturn = new TraktDeviceCode("dev-1", "USER-1", "https://trakt.tv/activate", 600, 5, _timeProvider.GetUtcNow());
        var deviceCode = await service.BeginDeviceAuthAsync();

        var tokens = new TraktTokenPair("access-1", "refresh-1", "bearer", 3600, "public", _timeProvider.GetUtcNow().ToUnixTimeSeconds());

        _authClient.OnPollAsync = async () =>
        {
            // A new auth flow begins concurrently while polling is in-flight
            _authClient.DeviceCodeToReturn = new TraktDeviceCode("dev-2", "USER-2", "https://trakt.tv/activate", 600, 5, _timeProvider.GetUtcNow());
            await service.BeginDeviceAuthAsync();
            return new TraktPollResponse(TraktPollStatus.Success, Tokens: tokens);
        };

        var ex = await Assert.ThrowsAsync<TraktAuthException>(() => service.CompleteDeviceAuthAsync(deviceCode));
        Assert.Contains("superseded", ex.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Null(_credentialStore.Read());
    }

    [Fact]
    public async Task GetAccessToken_ValidToken_ReturnsWithoutRefresh()
    {
        var now = _timeProvider.GetUtcNow();
        _credentialStore.Write(new TraktCredentialPayload(
            "test-client-id",
            "active-access-token",
            "active-refresh-token",
            "bearer",
            3600,
            "public",
            now.ToUnixTimeSeconds()));

        var service = CreateService();
        var token = await service.GetAccessTokenAsync();

        Assert.Equal("active-access-token", token);
        Assert.Equal(0, _authClient.RefreshCallsCount);
    }

    [Fact]
    public async Task GetAccessToken_NearExpiry_RefreshesAtomicallyWithFailClosedMarker()
    {
        var now = _timeProvider.GetUtcNow();
        _credentialStore.Write(new TraktCredentialPayload(
            "test-client-id",
            "expiring-access-token",
            "old-refresh-token",
            "bearer",
            120,
            "public",
            now.ToUnixTimeSeconds()));

        var newTokens = new TraktTokenPair("refreshed-access-token", "new-refresh-token", "bearer", 7200, "public", now.ToUnixTimeSeconds());
        _authClient.RefreshTokenToReturn = newTokens;

        var service = CreateService();
        var token = await service.GetAccessTokenAsync();

        Assert.Equal("refreshed-access-token", token);
        Assert.Equal(1, _authClient.RefreshCallsCount);

        var stored = _credentialStore.Read();
        Assert.NotNull(stored);
        Assert.Equal("refreshed-access-token", stored.AccessToken);
        Assert.Equal("new-refresh-token", stored.RefreshToken);
        Assert.False(stored.RequiresReconnect);
    }

    [Fact]
    public async Task GetAccessToken_Concurrent401ForcedRefresh_CoalescesBasedOnRejectedAccessToken()
    {
        var now = _timeProvider.GetUtcNow();
        _credentialStore.Write(new TraktCredentialPayload(
            "test-client-id",
            "stale-token-1",
            "refresh-token-1",
            "bearer",
            3600,
            "public",
            now.ToUnixTimeSeconds()));

        var refreshedTokens = new TraktTokenPair("new-coalesced-token", "new-refresh-token", "bearer", 7200, "public", now.ToUnixTimeSeconds());
        _authClient.RefreshTokenToReturn = refreshedTokens;
        _authClient.RefreshDelay = TimeSpan.FromMilliseconds(50);

        var service = CreateService();

        // 3 concurrent callers received 401 on "stale-token-1"
        var task1 = service.GetAccessTokenAsync(forceRefresh: true, rejectedAccessToken: "stale-token-1");
        var task2 = service.GetAccessTokenAsync(forceRefresh: true, rejectedAccessToken: "stale-token-1");
        var task3 = service.GetAccessTokenAsync(forceRefresh: true, rejectedAccessToken: "stale-token-1");

        var results = await Task.WhenAll(task1, task2, task3);

        Assert.Equal("new-coalesced-token", results[0]);
        Assert.Equal("new-coalesced-token", results[1]);
        Assert.Equal("new-coalesced-token", results[2]);
        Assert.Equal(1, _authClient.RefreshCallsCount);
    }

    [Fact]
    public async Task GetAccessToken_Concurrent401ForcedRefresh_CoalescesBasedOnCapturedTokenBeforeGate()
    {
        var now = _timeProvider.GetUtcNow();
        _credentialStore.Write(new TraktCredentialPayload(
            "test-client-id",
            "stale-token-1",
            "refresh-token-1",
            "bearer",
            3600,
            "public",
            now.ToUnixTimeSeconds()));

        var refreshedTokens = new TraktTokenPair("new-token-gate", "new-refresh-token", "bearer", 7200, "public", now.ToUnixTimeSeconds());
        _authClient.RefreshTokenToReturn = refreshedTokens;
        _authClient.RefreshDelay = TimeSpan.FromMilliseconds(50);

        var service = CreateService();

        // Callers call forceRefresh without explicit rejectedAccessToken; gate capture coalesces
        var task1 = service.GetAccessTokenAsync(forceRefresh: true);
        var task2 = service.GetAccessTokenAsync(forceRefresh: true);

        var results = await Task.WhenAll(task1, task2);

        Assert.Equal("new-token-gate", results[0]);
        Assert.Equal("new-token-gate", results[1]);
        Assert.Equal(1, _authClient.RefreshCallsCount);
    }

    [Fact]
    public async Task GetAccessToken_UncertainNetworkFailure_PersistsFailClosedMarker_DoesNotReplayOnRestart()
    {
        var now = _timeProvider.GetUtcNow();
        _credentialStore.Write(new TraktCredentialPayload(
            "test-client-id",
            "expiring-token",
            "single-use-refresh-token",
            "bearer",
            60,
            "public",
            now.ToUnixTimeSeconds()));

        _authClient.RefreshExceptionToThrow = new HttpRequestException("Uncertain connection drop during POST /oauth/token");

        var service = CreateService();

        await Assert.ThrowsAsync<TraktAuthException>(() => service.GetAccessTokenAsync());

        // Verify fail-closed marker in persistent storage
        var storedMarker = _credentialStore.Read();
        Assert.NotNull(storedMarker);
        Assert.True(storedMarker.RequiresReconnect);

        // Simulate app restart by constructing a new service instance reading the same store
        var restartedService = CreateService();
        var state = restartedService.GetAuthState();
        Assert.False(state.IsConnected);
        Assert.True(state.RequiresReconnect);

        // Verification: No tokens are replayed on restart
        await Assert.ThrowsAsync<TraktAuthException>(() => restartedService.GetAccessTokenAsync());
        Assert.Equal(1, _authClient.RefreshCallsCount);
    }

    [Fact]
    public async Task GetAccessToken_SecureStoreFailureAfterNetworkSuccess_RequiresReconnect_DoesNotReplayOldTokens()
    {
        var now = _timeProvider.GetUtcNow();
        _credentialStore.Write(new TraktCredentialPayload(
            "test-client-id",
            "expiring-token",
            "refresh-token",
            "bearer",
            60,
            "public",
            now.ToUnixTimeSeconds()));

        _authClient.RefreshTokenToReturn = new TraktTokenPair("new-access", "new-refresh", "bearer", 7200, "public", now.ToUnixTimeSeconds());

        // Permit write of marker, but fail when writing new tokens
        var writeCount = 0;
        _credentialStore.OnWrite = _ =>
        {
            writeCount++;
            if (writeCount > 1)
            {
                throw new InvalidOperationException("Failed to persist new tokens to secure vault");
            }
        };

        var service = CreateService();

        await Assert.ThrowsAsync<TraktAuthException>(() => service.GetAccessTokenAsync());

        var state = service.GetAuthState();
        Assert.False(state.IsConnected);
        Assert.True(state.RequiresReconnect);

        // Restarting service cannot replay old tokens
        var restartedService = CreateService();
        Assert.True(restartedService.GetAuthState().RequiresReconnect);
    }

    [Fact]
    public async Task GetAccessToken_InvalidGrant_DeletesStoreAndRequiresReconnect()
    {
        var now = _timeProvider.GetUtcNow();
        _credentialStore.Write(new TraktCredentialPayload(
            "test-client-id",
            "expiring-token",
            "revoked-refresh-token",
            "bearer",
            60,
            "public",
            now.ToUnixTimeSeconds()));

        _authClient.RefreshExceptionToThrow = new TraktInvalidGrantException("Invalid grant");

        var service = CreateService();

        await Assert.ThrowsAsync<TraktAuthException>(() => service.GetAccessTokenAsync());

        Assert.Null(_credentialStore.Read());
        var state = service.GetAuthState();
        Assert.False(state.IsConnected);
        Assert.True(state.RequiresReconnect);
    }

    [Fact]
    public async Task GetAccessToken_UncertainRefreshWithDeleteFailure_LeavesFailClosedMarker_BlocksReuseOnRestart()
    {
        var now = _timeProvider.GetUtcNow();
        _credentialStore.Write(new TraktCredentialPayload(
            "test-client-id",
            "expiring-token",
            "single-use-refresh-token",
            "bearer",
            60,
            "public",
            now.ToUnixTimeSeconds()));

        _authClient.RefreshExceptionToThrow = new HttpRequestException("Uncertain connection drop during POST /oauth/token");
        _credentialStore.ThrowOnDelete = new InvalidOperationException("OS credential vault delete failure");

        var service = CreateService();

        await Assert.ThrowsAsync<TraktAuthException>(() => service.GetAccessTokenAsync());

        // Verify fail-closed marker in persistent storage survives delete failure
        var storedMarker = _credentialStore.Read();
        Assert.NotNull(storedMarker);
        Assert.True(storedMarker.RequiresReconnect);

        // App restart cannot replay old refresh token; blocks reuse
        var restartedService = CreateService();
        var state = restartedService.GetAuthState();
        Assert.False(state.IsConnected);
        Assert.True(state.RequiresReconnect);

        await Assert.ThrowsAsync<TraktAuthException>(() => restartedService.GetAccessTokenAsync());
        Assert.Equal(1, _authClient.RefreshCallsCount);
    }

    [Fact]
    public async Task RecordVerifiedAccount_StaleIdentityResponse_RejectedWhenTokenChanged()
    {
        var now = _timeProvider.GetUtcNow();
        _credentialStore.Write(new TraktCredentialPayload(
            "test-client-id",
            "token-alpha",
            "refresh-alpha",
            "bearer",
            3600,
            "public",
            now.ToUnixTimeSeconds()));

        var service = CreateService();

        // Downstream caller was authenticated with "stale-old-token", but active token is "token-alpha"
        var recorded = await service.RecordVerifiedAccountAsync("user-42", expectedAccessToken: "stale-old-token");

        Assert.False(recorded);
        Assert.Null(_credentialStore.Read()!.AccountId);
        Assert.Null(service.GetAuthState().AccountId);
    }

    [Fact]
    public async Task RecordVerifiedAccount_DoesNotUpdateMemoryIfStoreWriteFails()
    {
        var now = _timeProvider.GetUtcNow();
        _credentialStore.Write(new TraktCredentialPayload(
            "test-client-id",
            "token-alpha",
            "refresh-alpha",
            "bearer",
            3600,
            "public",
            now.ToUnixTimeSeconds()));

        _credentialStore.ThrowOnWrite = new InvalidOperationException("Store write failed");

        var service = CreateService();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RecordVerifiedAccountAsync("user-42", expectedAccessToken: "token-alpha"));

        Assert.Null(service.GetAuthState().AccountId);
    }

    [Fact]
    public async Task RecordVerifiedAccount_SuccessUpdatesStoreAndImmutableSnapshot()
    {
        var now = _timeProvider.GetUtcNow();
        _credentialStore.Write(new TraktCredentialPayload(
            "test-client-id",
            "token-alpha",
            "refresh-alpha",
            "bearer",
            3600,
            "public",
            now.ToUnixTimeSeconds()));

        var service = CreateService();

        var recorded = await service.RecordVerifiedAccountAsync("user-42", expectedAccessToken: "token-alpha");

        Assert.True(recorded);
        Assert.Equal("user-42", _credentialStore.Read()!.AccountId);
        Assert.Equal("user-42", service.GetAuthState().AccountId);
    }

    [Fact]
    public async Task Disconnect_DeletesStoreAndResetsState()
    {
        var now = _timeProvider.GetUtcNow();
        _credentialStore.Write(new TraktCredentialPayload(
            "test-client-id",
            "token",
            "refresh",
            "bearer",
            3600,
            "public",
            now.ToUnixTimeSeconds(),
            AccountId: "slug-123"));

        var service = CreateService();
        Assert.True(service.GetAuthState().IsConnected);

        await service.DisconnectAsync();

        Assert.Null(_credentialStore.Read());
        var state = service.GetAuthState();
        Assert.False(state.IsConnected);
        Assert.False(state.RequiresReconnect);
        Assert.Null(state.AccountId);
    }

    [Fact]
    public async Task Disconnect_delete_failure_blocks_restored_session_after_restart()
    {
        _credentialStore.Write(new TraktCredentialPayload("test-client-id", "token", "refresh", "bearer",
            3600, "public", _timeProvider.GetUtcNow().ToUnixTimeSeconds()));
        var service = CreateService();
        _credentialStore.ThrowOnDelete = new InvalidOperationException("synthetic deletion failure");
        await Assert.ThrowsAsync<TraktAuthException>(() => service.DisconnectAsync());
        Assert.True(service.GetAuthState().RequiresReconnect);
        Assert.True(CreateService().GetAuthState().RequiresReconnect);
        Assert.True(_credentialStore.Read()!.RequiresReconnect);
    }

    [Fact]
    public async Task Disconnect_still_removes_credential_when_marker_write_fails()
    {
        _credentialStore.Write(new TraktCredentialPayload("test-client-id", "token", "refresh", "bearer",
            3600, "public", _timeProvider.GetUtcNow().ToUnixTimeSeconds()));
        var service = CreateService();
        _credentialStore.ThrowOnWrite = new InvalidOperationException("synthetic write failure");
        await service.DisconnectAsync();
        Assert.Null(_credentialStore.Read());
        Assert.False(CreateService().GetAuthState().IsConnected);
    }

    [Fact]
    public async Task Account_binding_requires_the_token_used_for_profile_request()
    {
        var service = CreateService();
        await Assert.ThrowsAsync<ArgumentException>(() => service.RecordVerifiedAccountAsync("account", " "));
    }

    [Fact]
    public void FastStateAccess_GetAuthState_ReturnsImmutableSnapshotSafelyWithoutLock()
    {
        var now = _timeProvider.GetUtcNow();
        _credentialStore.Write(new TraktCredentialPayload(
            "test-client-id",
            "token",
            "refresh",
            "bearer",
            3600,
            "public",
            now.ToUnixTimeSeconds(),
            AccountId: "test-user"));

        var service = CreateService();
        var snapshot1 = service.GetAuthState();
        var snapshot2 = service.GetAuthState();

        Assert.Same(snapshot1, snapshot2);
        Assert.True(snapshot1.IsConnected);
        Assert.Equal("test-user", snapshot1.AccountId);
    }

    [Fact]
    public void Startup_StoredFailClosedMarker_RequiresReconnect_BlocksReuse()
    {
        var now = _timeProvider.GetUtcNow();
        _credentialStore.Write(new TraktCredentialPayload(
            "test-client-id",
            AccessToken: string.Empty,
            RefreshToken: string.Empty,
            TokenType: "bearer",
            ExpiresIn: 0,
            Scope: "public",
            CreatedAtUnixSeconds: now.ToUnixTimeSeconds(),
            AccountId: null,
            RequiresReconnect: true));

        var service = CreateService();

        var state = service.GetAuthState();
        Assert.False(state.IsConnected);
        Assert.True(state.RequiresReconnect);
        Assert.Equal(0, _authClient.RefreshCallsCount);
    }

    private sealed class FakeTraktAuthClient : ITraktAuthClient
    {
        public TraktDeviceCode? DeviceCodeToReturn { get; set; }
        public string? LastClientIdUsedForCode { get; private set; }
        public Queue<TraktPollResponse> PollResponses { get; } = new();
        public Func<Task<TraktPollResponse>>? OnPollAsync { get; set; }
        public TraktTokenPair? RefreshTokenToReturn { get; set; }
        public Exception? RefreshExceptionToThrow { get; set; }
        public int RefreshCallsCount { get; private set; }
        public TimeSpan RefreshDelay { get; set; } = TimeSpan.Zero;

        public Task<TraktDeviceCode> GenerateDeviceCodeAsync(string clientId, CancellationToken cancellationToken = default)
        {
            LastClientIdUsedForCode = clientId;
            return Task.FromResult(DeviceCodeToReturn ?? new TraktDeviceCode("code", "user", "url", 600, 5, DateTimeOffset.UtcNow));
        }

        public async Task<TraktPollResponse> PollDeviceTokenAsync(string deviceCode, string clientId, CancellationToken cancellationToken = default)
        {
            if (OnPollAsync != null)
            {
                return await OnPollAsync();
            }

            if (PollResponses.Count > 0)
            {
                return PollResponses.Dequeue();
            }

            return new TraktPollResponse(TraktPollStatus.Pending);
        }

        public async Task<TraktTokenPair> RefreshTokenAsync(string clientId, string redirectUri, string refreshToken, CancellationToken cancellationToken = default)
        {
            RefreshCallsCount++;
            if (RefreshDelay > TimeSpan.Zero)
            {
                await Task.Delay(RefreshDelay, cancellationToken);
            }

            if (RefreshExceptionToThrow != null)
            {
                throw RefreshExceptionToThrow;
            }

            return RefreshTokenToReturn ?? new TraktTokenPair("refreshed-acc", "refreshed-ref", "bearer", 7200, "public", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        }
    }

    private sealed class FakeTraktCredentialStore : ITraktCredentialStore
    {
        private TraktCredentialPayload? _payload;
        public Exception? ThrowOnWrite { get; set; }
        public Exception? ThrowOnDelete { get; set; }
        public Action<TraktCredentialPayload>? OnWrite { get; set; }

        public TraktCredentialPayload? Read() => _payload;

        public void Write(TraktCredentialPayload payload)
        {
            if (ThrowOnWrite != null)
            {
                throw ThrowOnWrite;
            }

            OnWrite?.Invoke(payload);
            _payload = payload;
        }

        public void Delete()
        {
            if (ThrowOnDelete != null)
            {
                throw ThrowOnDelete;
            }

            _payload = null;
        }
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public FakeTimeProvider(DateTimeOffset initialUtcNow)
        {
            _utcNow = initialUtcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan delta)
        {
            _utcNow = _utcNow.Add(delta);
        }
    }
}
