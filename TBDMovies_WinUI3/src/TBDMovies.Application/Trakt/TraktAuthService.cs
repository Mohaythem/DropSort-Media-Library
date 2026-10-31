using System;
using System.Threading;
using System.Threading.Tasks;

namespace TBDMovies.Application.Trakt;

/// <summary>
/// Service orchestrating Trakt device authentication, token refreshing,
/// credential persistence, thread-safe snapshot publishing, and disconnect operations.
/// </summary>
public sealed class TraktAuthService : ITraktAuthService
{
    private readonly ITraktAuthClient _authClient;
    private readonly ITraktCredentialStore _credentialStore;
    private readonly TraktAuthConfig _config;
    private readonly TimeProvider _timeProvider;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    private readonly SemaphoreSlim _authGate = new(1, 1);
    private long _generation;
    private long _sessionSequence;

    private string? _activeDeviceCode;
    private long _activeSessionId;

    private TraktTokenPair? _tokens;
    private string? _accountId;
    private bool _requiresReconnect;
    private volatile TraktAuthState _state;

    public TraktAuthService(
        ITraktAuthClient authClient,
        ITraktCredentialStore credentialStore,
        TraktAuthConfig config,
        TimeProvider? timeProvider = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _authClient = authClient ?? throw new ArgumentNullException(nameof(authClient));
        _credentialStore = credentialStore ?? throw new ArgumentNullException(nameof(credentialStore));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _delay = delay ?? ((duration, ct) => Task.Delay(duration, _timeProvider, ct));

        // Read stored credentials during initialization without making startup network requests.
        try
        {
            var stored = _credentialStore.Read();
            if (stored != null && stored.IsValidForClient(_config.ClientId))
            {
                if (stored.RequiresReconnect)
                {
                    _tokens = null;
                    _accountId = null;
                    _requiresReconnect = true;
                    _state = TraktAuthState.RequiresReconnection;
                }
                else
                {
                    _tokens = new TraktTokenPair(
                        stored.AccessToken,
                        stored.RefreshToken,
                        stored.TokenType,
                        stored.ExpiresIn,
                        stored.Scope,
                        stored.CreatedAtUnixSeconds);
                    _accountId = stored.AccountId;
                    _requiresReconnect = false;
                    _state = TraktAuthState.ConnectedState(_accountId, _tokens.ExpiresAt);
                }
            }
            else
            {
                _tokens = null;
                _accountId = null;
                _requiresReconnect = false;
                _state = TraktAuthState.NotConfigured;
            }
        }
        catch
        {
            _tokens = null;
            _accountId = null;
            _requiresReconnect = true;
            _state = TraktAuthState.RequiresReconnection;
        }
    }

    public TraktAuthState GetAuthState() => _state;

    public async Task<TraktDeviceCode> BeginDeviceAuthAsync(CancellationToken cancellationToken = default)
    {
        await _authGate.WaitAsync(cancellationToken);
        try
        {
            Interlocked.Increment(ref _generation);
            var sessionId = Interlocked.Increment(ref _sessionSequence);

            var rawCode = await _authClient.GenerateDeviceCodeAsync(_config.ClientId, cancellationToken);

            var boundCode = new TraktDeviceCode(
                rawCode.DeviceCode,
                rawCode.UserCode,
                rawCode.VerificationUrl,
                rawCode.ExpiresIn,
                rawCode.Interval,
                rawCode.CreatedAt,
                sessionId);

            _activeDeviceCode = boundCode.DeviceCode;
            _activeSessionId = sessionId;

            return boundCode;
        }
        finally
        {
            _authGate.Release();
        }
    }

    public async Task<TraktAuthState> CompleteDeviceAuthAsync(TraktDeviceCode deviceCode, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deviceCode);

        long boundGen;
        long boundSessionId;

        await _authGate.WaitAsync(cancellationToken);
        try
        {
            if (string.IsNullOrWhiteSpace(_activeDeviceCode) ||
                !string.Equals(_activeDeviceCode, deviceCode.DeviceCode, StringComparison.Ordinal) ||
                (deviceCode.SessionId > 0 && deviceCode.SessionId != _activeSessionId))
            {
                throw new TraktAuthException("Authentication flow superseded or invalid device code.");
            }

            boundGen = Interlocked.Read(ref _generation);
            boundSessionId = _activeSessionId;
        }
        finally
        {
            _authGate.Release();
        }

        var deadline = deviceCode.ExpiresAt;
        var currentInterval = TimeSpan.FromSeconds(Math.Max(1, deviceCode.Interval));

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Interlocked.Read(ref _generation) != boundGen || _activeSessionId != boundSessionId)
            {
                throw new TraktAuthException("Authentication flow superseded or disconnected.");
            }

            var now = _timeProvider.GetUtcNow();
            if (now >= deadline)
            {
                throw new TraktAuthException("Trakt device code authorization expired.");
            }

            // Wait at least the server interval before polling, bounded to remaining lifetime.
            var remaining = deadline - now;
            var waitDuration = currentInterval > remaining ? remaining : currentInterval;
            await _delay(waitDuration, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            if (Interlocked.Read(ref _generation) != boundGen || _activeSessionId != boundSessionId)
            {
                throw new TraktAuthException("Authentication flow superseded or disconnected.");
            }

            if (_timeProvider.GetUtcNow() >= deadline)
            {
                throw new TraktAuthException("Trakt device code authorization expired.");
            }

            var pollResult = await _authClient.PollDeviceTokenAsync(deviceCode.DeviceCode, _config.ClientId, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            if (Interlocked.Read(ref _generation) != boundGen || _activeSessionId != boundSessionId)
            {
                throw new TraktAuthException("Authentication flow superseded or disconnected.");
            }

            switch (pollResult.Status)
            {
                case TraktPollStatus.Success:
                    if (pollResult.Tokens is null)
                    {
                        throw new TraktAuthException("Authorization succeeded but no tokens were returned.");
                    }

                    if (_timeProvider.GetUtcNow() >= deadline)
                    {
                        throw new TraktAuthException("Trakt device code authorization expired.");
                    }

                    await _authGate.WaitAsync(cancellationToken);
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (Interlocked.Read(ref _generation) != boundGen || _activeSessionId != boundSessionId)
                        {
                            throw new TraktAuthException("Authentication flow superseded or disconnected.");
                        }

                        if (_timeProvider.GetUtcNow() >= deadline)
                        {
                            throw new TraktAuthException("Trakt device code authorization expired.");
                        }

                        var tokens = pollResult.Tokens;
                        var payload = new TraktCredentialPayload(
                            _config.ClientId,
                            tokens.AccessToken,
                            tokens.RefreshToken,
                            tokens.TokenType,
                            tokens.ExpiresIn,
                            tokens.Scope,
                            tokens.CreatedAtUnixSeconds,
                            AccountId: null,
                            RequiresReconnect: false);

                        try
                        {
                            _credentialStore.Write(payload);

                            var readback = _credentialStore.Read();
                            if (readback is null || readback.RequiresReconnect ||
                                !string.Equals(readback.AccessToken, tokens.AccessToken, StringComparison.Ordinal))
                            {
                                throw new InvalidOperationException("Failed to verify persisted credentials after device completion.");
                            }

                            _tokens = tokens;
                            _accountId = null;
                            _requiresReconnect = false;
                            _activeDeviceCode = null;
                            _activeSessionId = 0;
                            Interlocked.Increment(ref _generation);
                            _state = TraktAuthState.ConnectedState(_accountId, _tokens.ExpiresAt);
                            return _state;
                        }
                        catch (Exception storeEx)
                        {
                            try { _credentialStore.Delete(); } catch { }
                            _requiresReconnect = true;
                            _tokens = null;
                            _state = TraktAuthState.RequiresReconnection;
                            throw new TraktAuthException("Failed to persist and verify Trakt credentials. Reconnect required.", storeEx);
                        }
                    }
                    finally
                    {
                        _authGate.Release();
                    }

                case TraktPollStatus.Pending:
                    break;

                case TraktPollStatus.Slowdown:
                    var retryAfter = pollResult.RetryAfterSeconds.HasValue && pollResult.RetryAfterSeconds.Value > 0
                        ? TimeSpan.FromSeconds(pollResult.RetryAfterSeconds.Value)
                        : TimeSpan.Zero;
                    var candidate = currentInterval + TimeSpan.FromSeconds(5);
                    currentInterval = retryAfter > candidate ? retryAfter : candidate;
                    break;

                case TraktPollStatus.Invalid:
                    throw new TraktAuthException("The device code was invalid or not found.");

                case TraktPollStatus.AlreadyUsed:
                    throw new TraktAuthException("The device code has already been used.");

                case TraktPollStatus.Expired:
                    throw new TraktAuthException("The device code expired.");

                case TraktPollStatus.Denied:
                    throw new TraktAuthException("Trakt authorization was denied by the user.");

                default:
                    throw new TraktAuthException($"Unexpected polling status: {pollResult.Status}.");
            }
        }
    }

    public async Task<string> GetAccessTokenAsync(
        bool forceRefresh = false,
        string? rejectedAccessToken = null,
        CancellationToken cancellationToken = default)
    {
        if (_requiresReconnect)
        {
            throw new TraktAuthException("Trakt authentication requires reconnection.");
        }

        var currentTokens = _tokens;
        if (currentTokens is null)
        {
            throw new TraktAuthException("Trakt is not connected.");
        }

        var now = _timeProvider.GetUtcNow();
        if (!forceRefresh && !currentTokens.IsExpired(now, TimeSpan.FromMinutes(5)))
        {
            return currentTokens.AccessToken;
        }

        // Fast coalesce: if a specific rejected token was provided and the active token already differs,
        // another concurrent thread has already refreshed the token.
        if (rejectedAccessToken != null && !string.Equals(currentTokens.AccessToken, rejectedAccessToken, StringComparison.Ordinal))
        {
            return currentTokens.AccessToken;
        }

        var tokenBeforeGate = currentTokens.AccessToken;

        await _authGate.WaitAsync(cancellationToken);
        try
        {
            if (_requiresReconnect)
            {
                throw new TraktAuthException("Trakt authentication requires reconnection.");
            }

            if (_tokens is null)
            {
                throw new TraktAuthException("Trakt is not connected.");
            }

            now = _timeProvider.GetUtcNow();
            if (!forceRefresh && !_tokens.IsExpired(now, TimeSpan.FromMinutes(5)))
            {
                return _tokens.AccessToken;
            }

            // Coalesce concurrent forced refresh calls under lock.
            if (rejectedAccessToken != null)
            {
                if (!string.Equals(_tokens.AccessToken, rejectedAccessToken, StringComparison.Ordinal))
                {
                    return _tokens.AccessToken;
                }
            }
            else if (forceRefresh && !string.Equals(_tokens.AccessToken, tokenBeforeGate, StringComparison.Ordinal))
            {
                return _tokens.AccessToken;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var oldRefreshToken = _tokens.RefreshToken;
            var currentGen = Interlocked.Read(ref _generation);

            // Persist fail-closed recovery marker before sending refresh POST.
            // Tokens are kept only in memory for this attempt.
            var inFlightMarker = new TraktCredentialPayload(
                _config.ClientId,
                AccessToken: string.Empty,
                RefreshToken: string.Empty,
                TokenType: "bearer",
                ExpiresIn: 0,
                Scope: "public",
                CreatedAtUnixSeconds: _timeProvider.GetUtcNow().ToUnixTimeSeconds(),
                AccountId: _accountId,
                RequiresReconnect: true);

            try
            {
                _credentialStore.Write(inFlightMarker);
                var markerVerify = _credentialStore.Read();
                if (markerVerify is null || !markerVerify.RequiresReconnect)
                {
                    _requiresReconnect = true;
                    _tokens = null;
                    _state = TraktAuthState.RequiresReconnection;
                    throw new TraktAuthException("Failed to verify persistent fail-closed marker before refresh.");
                }
            }
            catch (Exception ex) when (ex is not TraktAuthException)
            {
                _requiresReconnect = true;
                _tokens = null;
                _state = TraktAuthState.RequiresReconnection;
                throw new TraktAuthException("Failed to write persistent fail-closed marker before refresh. Reconnect required.", ex);
            }

            TraktTokenPair newTokens;
            try
            {
                newTokens = await _authClient.RefreshTokenAsync(
                    _config.ClientId,
                    _config.RedirectUri,
                    oldRefreshToken,
                    cancellationToken);
            }
            catch (TraktInvalidGrantException ex)
            {
                try { _credentialStore.Delete(); } catch { }
                _requiresReconnect = true;
                _tokens = null;
                _state = TraktAuthState.RequiresReconnection;
                throw new TraktAuthException("Trakt authorization grant is invalid or expired. Reconnect required.", ex);
            }
            catch (Exception ex)
            {
                // Single-use refresh token: network outcome is uncertain and token may be consumed.
                // Do not retry. Retain fail-closed marker so restart blocks replay.
                _requiresReconnect = true;
                _tokens = null;
                _state = TraktAuthState.RequiresReconnection;
                throw new TraktAuthException("Token refresh encountered an uncertain network failure. Reconnect required.", ex);
            }

            if (Interlocked.Read(ref _generation) != currentGen)
            {
                _requiresReconnect = true;
                _tokens = null;
                _state = TraktAuthState.RequiresReconnection;
                throw new TraktAuthException("Authentication flow superseded or disconnected.");
            }

            var newPayload = new TraktCredentialPayload(
                _config.ClientId,
                newTokens.AccessToken,
                newTokens.RefreshToken,
                newTokens.TokenType,
                newTokens.ExpiresIn,
                newTokens.Scope,
                newTokens.CreatedAtUnixSeconds,
                _accountId,
                RequiresReconnect: false);

            try
            {
                _credentialStore.Write(newPayload);
                var verifySaved = _credentialStore.Read();
                if (verifySaved is null || verifySaved.RequiresReconnect ||
                    !string.Equals(verifySaved.AccessToken, newTokens.AccessToken, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Stored credential verification readback failed.");
                }

                _tokens = newTokens;
                _requiresReconnect = false;
                _state = TraktAuthState.ConnectedState(_accountId, _tokens.ExpiresAt);
                return newTokens.AccessToken;
            }
            catch (Exception storeEx)
            {
                // Do not delete the fail-closed marker! The persistent marker in the store
                // already has RequiresReconnect=true, which ensures any restart will block reuse.
                _requiresReconnect = true;
                _tokens = null;
                _state = TraktAuthState.RequiresReconnection;
                throw new TraktAuthException("Failed to persist and verify refreshed Trakt credentials. Reconnect required.", storeEx);
            }
        }
        finally
        {
            _authGate.Release();
        }
    }

    public async Task RejectAccessTokenAsync(string rejectedAccessToken, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rejectedAccessToken);
        await _authGate.WaitAsync(cancellationToken);
        try
        {
            // A late rejection must not invalidate a replacement authorization.
            if (_tokens is null || !string.Equals(_tokens.AccessToken, rejectedAccessToken, StringComparison.Ordinal)) return;
            try
            {
                _credentialStore.Write(new TraktCredentialPayload(_config.ClientId, "", "", "bearer", 0,
                    "public", _timeProvider.GetUtcNow().ToUnixTimeSeconds(), RequiresReconnect: true));
                if (_credentialStore.Read()?.RequiresReconnect != true)
                    throw new TraktAuthException("Failed to verify rejected credential state.");
            }
            catch (Exception ex)
            {
                // If the marker cannot be saved, try to remove the rejected pair.
                try { _credentialStore.Delete(); } catch { }
                throw new TraktAuthException("Failed to persist rejected credential state. Reconnect required.", ex);
            }
            finally
            {
                Interlocked.Increment(ref _generation);
                _activeDeviceCode = null;
                _activeSessionId = 0;
                _tokens = null;
                _accountId = null;
                _requiresReconnect = true;
                _state = TraktAuthState.RequiresReconnection;
            }
        }
        finally
        {
            _authGate.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await _authGate.WaitAsync(cancellationToken);
        try
        {
            Interlocked.Increment(ref _generation);
            _activeDeviceCode = null;
            _activeSessionId = 0;
            try
            {
                // A durable marker prevents a failed deletion from restoring a disconnected session.
                _credentialStore.Write(new TraktCredentialPayload(_config.ClientId, "", "", "bearer", 0,
                    "public", _timeProvider.GetUtcNow().ToUnixTimeSeconds(), RequiresReconnect: true));
                if (_credentialStore.Read()?.RequiresReconnect != true)
                    throw new TraktAuthException("Failed to verify disconnected credential state.");
                _credentialStore.Delete();
                if (_credentialStore.Read() is not null)
                    throw new TraktAuthException("Failed to verify credential removal.");
                _tokens = null;
                _accountId = null;
                _requiresReconnect = false;
                _state = TraktAuthState.NotConfigured;
            }
            catch
            {
                _tokens = null;
                _accountId = null;
                // Removal can still succeed when writing the protective marker was unavailable.
                try
                {
                    _credentialStore.Delete();
                    if (_credentialStore.Read() is null)
                    {
                        _requiresReconnect = false;
                        _state = TraktAuthState.NotConfigured;
                        return;
                    }
                }
                catch { }
                _requiresReconnect = true;
                _state = TraktAuthState.RequiresReconnection;
                throw new TraktAuthException("Credential removal failed. Reconnect required.");
            }
        }
        finally
        {
            _authGate.Release();
        }
    }

    public async Task<bool> RecordVerifiedAccountAsync(
        string accountId,
        string expectedAccessToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedAccessToken);

        await _authGate.WaitAsync(cancellationToken);
        try
        {
            if (_tokens is null || _requiresReconnect)
            {
                return false;
            }

            if (!string.Equals(_tokens.AccessToken, expectedAccessToken, StringComparison.Ordinal))
            {
                return false;
            }

            var payload = new TraktCredentialPayload(
                _config.ClientId,
                _tokens.AccessToken,
                _tokens.RefreshToken,
                _tokens.TokenType,
                _tokens.ExpiresIn,
                _tokens.Scope,
                _tokens.CreatedAtUnixSeconds,
                accountId,
                RequiresReconnect: false);

            _credentialStore.Write(payload);

            var verified = _credentialStore.Read();
            if (verified is null || !string.Equals(verified.AccountId, accountId, StringComparison.Ordinal))
            {
                throw new TraktAuthException("Failed to verify persisted account ID in credential store.");
            }

            _accountId = accountId;
            _state = TraktAuthState.ConnectedState(_accountId, _tokens.ExpiresAt);
            return true;
        }
        finally
        {
            _authGate.Release();
        }
    }

}
