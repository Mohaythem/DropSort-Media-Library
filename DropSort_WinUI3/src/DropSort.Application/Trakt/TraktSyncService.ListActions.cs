using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DropSort.Application.Trakt;

public sealed partial class TraktSyncService
{
    public async Task<TraktListRefreshResult> RefreshListAsync(
        long? listId,
        string expectedAccountId,
        CancellationToken cancellationToken = default)
    {
        if (listId is <= 0) throw new ArgumentOutOfRangeException(nameof(listId));
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedAccountId);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var auth = _authService.GetAuthState();
        if (!auth.IsConnected || auth.RequiresReconnect ||
            !string.Equals(_currentAccountId, expectedAccountId, StringComparison.Ordinal))
            return new(false, false, "Trakt account is not connected or no longer matches the selected account.");
        var generation = Interlocked.Read(ref _generation);
        if (!await _operationGate.WaitAsync(0, cancellationToken))
            return new(false, false, "Coordinator is busy with another operation.");

        CancellationTokenSource? linked = null;
        try
        {
            lock (_ctsLock)
            {
                if (_disposed || Interlocked.Read(ref _generation) != generation)
                    throw new OperationCanceledException("Trakt operation was superseded.");
                linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _activeNetworkCts = linked;
            }
            return await RefreshListCoreAsync(listId, expectedAccountId, generation, linked.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(false, false, "Trakt list refresh was cancelled.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new(false, false, "Trakt list refresh failed.");
        }
        finally
        {
            if (linked != null)
            {
                lock (_ctsLock)
                {
                    if (ReferenceEquals(_activeNetworkCts, linked)) _activeNetworkCts = null;
                }
                linked.Dispose();
            }
            _operationGate.Release();
        }
    }

    public async Task<TraktListWriteResult> AddMovieToListAsync(
        long? listId,
        long tmdbId,
        string expectedAccountId,
        CancellationToken cancellationToken = default)
    {
        if (listId is <= 0) throw new ArgumentOutOfRangeException(nameof(listId));
        if (tmdbId <= 0) throw new ArgumentOutOfRangeException(nameof(tmdbId));
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedAccountId);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var auth = _authService.GetAuthState();
        if (!auth.IsConnected || auth.RequiresReconnect ||
            !string.Equals(_currentAccountId, expectedAccountId, StringComparison.Ordinal))
            return new(TraktListWriteStatus.Failed, false, "Trakt account is not connected or no longer matches the selected account.");
        if (_dataClient is not ITraktListWriter writer)
            return new(TraktListWriteStatus.Failed, false, "Trakt list writing is unavailable.");

        var generation = Interlocked.Read(ref _generation);
        if (!await _operationGate.WaitAsync(0, cancellationToken))
            return new(TraktListWriteStatus.Failed, false, "Coordinator is busy with another operation.");
        CancellationTokenSource? linked = null;
        try
        {
            lock (_ctsLock)
            {
                if (_disposed || Interlocked.Read(ref _generation) != generation)
                    throw new OperationCanceledException("Trakt operation was superseded.");
                linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _activeNetworkCts = linked;
            }

            var (account, token) = await FetchWithAuthRetryAsync(
                t => _dataClient.GetAccountAsync(_config.ClientId, t, linked.Token), linked.Token);
            var verified = TraktDataProjections.ParseAccountProfile(account).Uuid;
            EnsureOperationValid(generation, expectedAccountId, linked.Token);
            if (!string.Equals(verified, expectedAccountId, StringComparison.Ordinal))
                return new(TraktListWriteStatus.Failed, false, "Trakt account verification failed.");
            // Re-read ownership after acquiring the gate and account verification. A stale
            // catalog must never authorize a mutation against another list.
            if (listId.HasValue)
            {
                var knownLists = GetActiveListIds(expectedAccountId);
                if (knownLists is null || !knownLists.Contains(listId.Value))
                    return new(TraktListWriteStatus.Failed, false, "The selected Trakt list is not in the current account catalog.");
            }
            var liveAuth = _authService.GetAuthState();
            if (!liveAuth.IsConnected || liveAuth.RequiresReconnect ||
                !string.Equals(liveAuth.AccountId, expectedAccountId, StringComparison.Ordinal))
                return new(TraktListWriteStatus.Failed, false, "Trakt account changed before list write.");
            linked.Token.ThrowIfCancellationRequested();

            JsonElement response;
            try
            {
                // The writer performs exactly one POST. Do not wrap this in auth retry.
                response = await writer.AddMovieToListAsync(listId, tmdbId, _config.ClientId, token, linked.Token);
            }
            catch (OperationCanceledException) when (linked.Token.IsCancellationRequested)
            {
                return new(TraktListWriteStatus.Failed, false,
                    "Trakt write outcome is unknown after cancellation. Sync the list before trying again; no retry was attempted.");
            }
            var status = ParseWriteStatus(response);
            if (status == TraktListWriteStatus.Failed)
                return new(status, false, "Trakt returned an unrecognized list write result.");
            if (status == TraktListWriteStatus.NotFound)
                return new(status, false, "Trakt could not find the selected movie. It was not added.");

            try
            {
                var refresh = await RefreshListCoreAsync(listId, expectedAccountId, generation, linked.Token);
                return new(status, refresh.Success, refresh.Success ? null : "Movie was confirmed by Trakt, but the local list cache could not be refreshed.");
            }
            catch (Exception) when (status is TraktListWriteStatus.Added or TraktListWriteStatus.AlreadyExists)
            {
                // The POST has a known remote outcome. A later GET/cache failure must not
                // convert that confirmation into an unknown remote write result.
                return new(status, false, "Movie was confirmed by Trakt, but the local list cache could not be refreshed.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(TraktListWriteStatus.Cancelled, false, "Trakt list write was cancelled.");
        }
        catch (Exception ex) when (ex is TraktClientException or TraktAuthException or TraktInvalidGrantException)
        {
            return new(TraktListWriteStatus.Failed, false, "Trakt list write outcome is unknown. No retry was attempted.");
        }
        finally
        {
            if (linked != null)
            {
                lock (_ctsLock)
                {
                    if (ReferenceEquals(_activeNetworkCts, linked)) _activeNetworkCts = null;
                }
                linked.Dispose();
            }
            _operationGate.Release();
        }
    }

    private async Task<TraktListRefreshResult> RefreshListCoreAsync(long? listId, string accountId, long generation, CancellationToken token)
    {
        var (account, accessToken) = await FetchWithAuthRetryAsync(
            t => _dataClient.GetAccountAsync(_config.ClientId, t, token), token);
        var verified = TraktDataProjections.ParseAccountProfile(account).Uuid;
        EnsureOperationValid(generation, accountId, token);
        if (!string.Equals(verified, accountId, StringComparison.Ordinal))
            return new(false, false, "Trakt account verification failed.");

        var spec = listId.HasValue ? TraktResourceSpec.ListItems(listId.Value) : TraktResourceSpec.Watchlist();
        // Bind the exact token used for account verification to this selected refresh.
        var payload = await _dataClient.GetResourceAsync(spec, _config.ClientId, accessToken, token);
        if (payload.ValueKind != JsonValueKind.Array)
            return new(false, false, "Trakt returned an invalid list payload.");
        EnsureOperationValid(generation, accountId, token);
        _cacheRepository.SaveComplete(accountId, spec.ToCacheResourceKey(), payload.GetRawText(), _timeProvider.GetUtcNow(), token);
        try { SyncCompleted?.Invoke(this, EventArgs.Empty); } catch { }
        return new(true, true);
    }

    private static TraktListWriteStatus ParseWriteStatus(JsonElement response)
    {
        var added = ReadMovieCount(response, "added");
        var existing = ReadMovieCount(response, "existing");
        var notFound = ReadNotFoundMovies(response);
        if (added is null || existing is null || notFound is null) return TraktListWriteStatus.Failed;
        var total = added.Value + existing.Value + notFound.Value;
        if (total != 1) return TraktListWriteStatus.Failed;
        if (added == 1) return TraktListWriteStatus.Added;
        if (existing == 1) return TraktListWriteStatus.AlreadyExists;
        return TraktListWriteStatus.NotFound;
    }

    private static int? ReadMovieCount(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object ||
            !value.TryGetProperty("movies", out value) || value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt32(out var count) || count is < 0 or > 1) return null;
        return count;
    }

    private static int? ReadNotFoundMovies(JsonElement root)
    {
        if (!root.TryGetProperty("not_found", out var value) || value.ValueKind != JsonValueKind.Object ||
            !value.TryGetProperty("movies", out value) || value.ValueKind != JsonValueKind.Array) return null;
        var count = 0;
        foreach (var item in value.EnumerateArray())
            if (item.ValueKind != JsonValueKind.Object) return null;
            else if (++count > 1) return null;
        return count;
    }
}
