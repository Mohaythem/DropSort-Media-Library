using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TBDMovies.Application.Repositories;
using TBDMovies.Application.Trakt;
using Xunit;

namespace TBDMovies.Tests.Application;

public sealed class TraktListActionsTests
{
    private const string Account = "acct-1";
    private static readonly TraktAuthConfig Config = new("client", "https://localhost/callback");

    [Fact]
    public async Task Offline_account_mismatch_and_unowned_list_never_post()
    {
        var auth = new AuthFake();
        var data = new DataFake();
        using var service = Create(auth, data, out var cache);
        Assert.Equal(TraktListWriteStatus.Failed, (await service.AddMovieToListAsync(null, 10, Account)).Status);
        auth.Connect(Account);
        data.Account = Profile(Account);
        Assert.Equal(TraktListWriteStatus.Failed, (await service.AddMovieToListAsync(999, 10, Account)).Status);
        Assert.Equal(0, data.Posts);
        Assert.Empty(cache.Entries);
    }

    [Fact]
    public async Task Account_mismatch_is_rejected_before_post()
    {
        var auth = new AuthFake(); auth.Connect(Account);
        var data = new DataFake { Account = Profile("other") };
        using var service = Create(auth, data, out _);
        var result = await service.AddMovieToListAsync(null, 10, Account);
        Assert.Equal(TraktListWriteStatus.Failed, result.Status);
        Assert.Equal(0, data.Posts);
    }

    [Theory]
    [InlineData("added")]
    [InlineData("existing")]
    public async Task Confirmed_remote_outcome_survives_cache_refresh_failure(string category)
    {
        var auth = new AuthFake(); auth.Connect(Account);
        var data = new DataFake { Account = Profile(Account), PostResponse = Response(category), ThrowOnRefresh = true };
        using var service = Create(auth, data, out var cache);
        var result = await service.AddMovieToListAsync(null, 922337203685477000, Account);
        Assert.Equal(category == "added" ? TraktListWriteStatus.Added : TraktListWriteStatus.AlreadyExists, result.Status);
        Assert.False(result.CacheRefreshSucceeded);
        Assert.Equal(1, data.Posts);
        Assert.Empty(cache.Entries);
    }

    [Fact]
    public async Task Not_found_is_not_confirmed_and_unknown_response_is_not_retried()
    {
        var auth = new AuthFake(); auth.Connect(Account);
        var data = new DataFake { Account = Profile(Account), PostResponse = Response("not_found") };
        using var service = Create(auth, data, out _);
        var result = await service.AddMovieToListAsync(null, 10, Account);
        Assert.Equal(TraktListWriteStatus.NotFound, result.Status);
        Assert.False(result.IsConfirmed);

        data.PostResponse = JsonDocument.Parse("{\"added\":{},\"existing\":{},\"not_found\":{}}").RootElement;
        var unknown = await service.AddMovieToListAsync(null, 11, Account);
        Assert.Equal(TraktListWriteStatus.Failed, unknown.Status);
        Assert.Equal(2, data.Posts);
    }

    [Fact]
    public async Task List_action_fires_sync_completed_event_on_successful_cache_refresh()
    {
        var auth = new AuthFake(); auth.Connect(Account);
        var data = new DataFake { Account = Profile(Account) };
        using var service = Create(auth, data, out _);
        var eventFired = false;
        service.SyncCompleted += (_, _) => eventFired = true;

        var result = await service.AddMovieToListAsync(null, 10, Account);
        Assert.True(result.IsConfirmed);
        Assert.True(result.CacheRefreshSucceeded);
        Assert.True(eventFired);
    }

    [Fact]
    public async Task Cancelled_before_post_does_not_write()
    {
        var auth = new AuthFake(); auth.Connect(Account);
        var data = new DataFake { Account = Profile(Account), BlockAccount = true };
        using var service = Create(auth, data, out _);
        using var cts = new CancellationTokenSource();
        var task = service.AddMovieToListAsync(null, 10, Account, cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Equal(0, data.Posts);
    }

    [Fact]
    public async Task Connected_catalog_ownership_is_checked_and_owned_large_list_is_accepted()
    {
        var auth = new AuthFake(); auth.Connect(Account);
        var data = new DataFake { Account = Profile(Account) };
        using var service = Create(auth, data, out var cache);
        Assert.False((await service.AddMovieToListAsync(42, 10, Account)).IsConfirmed);
        Assert.Equal(0, data.Posts);
        cache.SaveComplete(Account, "lists", "[{\"ids\":{\"trakt\":922337203685477000},\"name\":\"Fixture\"}]", DateTimeOffset.UtcNow);
        Assert.False((await service.AddMovieToListAsync(42, 10, Account)).IsConfirmed);
        Assert.Equal(0, data.Posts);
        Assert.True((await service.AddMovieToListAsync(922337203685477000, 10, Account)).IsConfirmed);
        Assert.Equal(1, data.Posts);
        Assert.Contains((Account, "list:922337203685477000:items"), cache.Entries.Keys);
    }

    [Fact]
    public async Task Cancellation_after_post_dispatch_is_unknown_and_never_repeated()
    {
        var auth = new AuthFake(); auth.Connect(Account);
        var data = new DataFake { Account = Profile(Account) };
        using var cts = new CancellationTokenSource();
        data.PostHandler = token => { cts.Cancel(); return Task.FromCanceled<JsonElement>(token); };
        using var service = Create(auth, data, out _);
        var result = await service.AddMovieToListAsync(null, 10, Account, cts.Token);
        Assert.False(result.IsConfirmed);
        Assert.Contains("unknown", result.Error!);
        Assert.Contains("Sync", result.Error!);
        Assert.Equal(1, data.Posts);
    }

    [Fact]
    public async Task Confirmed_post_survives_cancellation_before_cache_refresh()
    {
        var auth = new AuthFake(); auth.Connect(Account);
        var data = new DataFake { Account = Profile(Account) };
        using var cts = new CancellationTokenSource();
        data.PostHandler = _ => { cts.Cancel(); return Task.FromResult(Response("added")); };
        using var service = Create(auth, data, out _);
        var result = await service.AddMovieToListAsync(null, 10, Account, cts.Token);
        Assert.True(result.IsConfirmed);
        Assert.False(result.CacheRefreshSucceeded);
        Assert.Equal(1, data.Posts);
    }

    [Fact]
    public async Task Disconnect_during_profile_verification_prevents_post_and_releases_busy_gate()
    {
        var auth = new AuthFake(); auth.Connect(Account);
        var data = new DataFake { Account = Profile(Account) };
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        data.AccountHandler = _ => { entered.TrySetResult(); return release.Task; };
        using var service = Create(auth, data, out _);
        var first = service.AddMovieToListAsync(null, 10, Account);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False((await service.AddMovieToListAsync(null, 11, Account)).IsConfirmed);
        var disconnect = service.DisconnectAsync();
        release.SetResult(Profile(Account));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        await disconnect;
        Assert.Equal(0, data.Posts);
        Assert.False(service.GetSyncState().AuthState.IsConnected);
    }

    private static TraktSyncService Create(AuthFake auth, DataFake data, out CacheFake cache)
    {
        cache = new CacheFake();
        return new TraktSyncService(auth, data, cache, Config);
    }

    private static JsonElement Profile(string id) => JsonDocument.Parse($"{{\"user\":{{\"ids\":{{\"uuid\":\"{id}\"}}}}}}").RootElement;
    private static JsonElement Response(string category) => JsonDocument.Parse($"{{\"added\":{{\"movies\":{(category == "added" ? "1" : "0")}}},\"existing\":{{\"movies\":{(category == "existing" ? "1" : "0")}}},\"not_found\":{{\"movies\":{(category == "not_found" ? "[{}]" : "[]")}}}}}").RootElement;

    private sealed class AuthFake : ITraktAuthService
    {
        private TraktAuthState _state = TraktAuthState.NotConfigured;
        public void Connect(string id) => _state = TraktAuthState.ConnectedState(id, DateTimeOffset.UtcNow.AddHours(1));
        public TraktAuthState GetAuthState() => _state;
        public Task<string> GetAccessTokenAsync(bool forceRefresh = false, string? rejectedAccessToken = null, CancellationToken cancellationToken = default) => Task.FromResult("token");
        public Task<bool> RecordVerifiedAccountAsync(string accountId, string expectedAccessToken, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task RejectAccessTokenAsync(string rejectedAccessToken, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken cancellationToken = default) { _state = TraktAuthState.NotConfigured; return Task.CompletedTask; }
        public Task<TraktDeviceCode> BeginDeviceAuthAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TraktAuthState> CompleteDeviceAuthAsync(TraktDeviceCode deviceCode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class DataFake : ITraktDataClient, ITraktListWriter
    {
        public JsonElement Account { get; set; }
        public JsonElement PostResponse { get; set; } = Response("added");
        public int Posts { get; private set; }
        public bool ThrowOnRefresh { get; set; }
        public bool BlockAccount { get; set; }
        public Func<CancellationToken, Task<JsonElement>>? AccountHandler { get; set; }
        public Func<CancellationToken, Task<JsonElement>>? PostHandler { get; set; }
        public Task<JsonElement> GetAccountAsync(string clientId, string accessToken, CancellationToken cancellationToken = default)
        {
            if (AccountHandler is not null) return AccountHandler(cancellationToken);
            if (BlockAccount) return Task.FromCanceled<JsonElement>(new CancellationToken(true));
            return Task.FromResult(Account);
        }
        public Task<JsonElement> GetResourceAsync(TraktResourceSpec resource, string clientId, string accessToken, CancellationToken cancellationToken = default)
            => ThrowOnRefresh ? throw new InvalidOperationException("refresh failed") : Task.FromResult(JsonDocument.Parse("[]").RootElement);
        public Task<JsonElement> AddMovieToListAsync(long? listId, long tmdbId, string clientId, string accessToken, CancellationToken cancellationToken = default)
        { Posts++; return PostHandler?.Invoke(cancellationToken) ?? Task.FromResult(PostResponse); }
    }

    private sealed class CacheFake : ITraktCacheRepository
    {
        public Dictionary<(string, string), TraktCacheEntry> Entries { get; } = new();
        public TraktCacheEntry? Get(string accountId, string resourceKey) => Entries.TryGetValue((accountId, resourceKey), out var e) ? e : null;
        public IReadOnlyList<TraktCacheSummary> List(string accountId) => Array.Empty<TraktCacheSummary>();
        public void SaveComplete(string accountId, string resourceKey, string payloadJson, DateTimeOffset now, CancellationToken cancellationToken = default) => Entries[(accountId, resourceKey)] = new(accountId, resourceKey, payloadJson, now, now, null);
        public void RecordFailure(string accountId, string resourceKey, TraktCacheErrorCode errorCode, DateTimeOffset now, CancellationToken cancellationToken = default) { }
        public void DeleteAccount(string accountId, CancellationToken cancellationToken = default) { }
    }
}
