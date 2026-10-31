using System;

namespace TBDMovies.Application.Trakt;

/// <summary>
/// Configuration for Trakt OAuth device code flow.
/// Redirect URI must use HTTPS and the authentication host is fixed to https://auth.trakt.tv.
/// </summary>
public sealed record TraktAuthConfig
{
    public const string FixedAuthBaseUrl = "https://auth.trakt.tv";

    public string ClientId { get; }
    public string RedirectUri { get; }

    public TraktAuthConfig(string clientId, string redirectUri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(redirectUri);

        if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("The redirect URI must be an absolute HTTPS URL.", nameof(redirectUri));
        }

        ClientId = clientId.Trim();
        RedirectUri = redirectUri.Trim();
    }
}

/// <summary>
/// Device code grant authorization metadata returned by Trakt.
/// ToString() redacts the sensitive DeviceCode value.
/// </summary>
public sealed record TraktDeviceCode(
    string DeviceCode,
    string UserCode,
    string VerificationUrl,
    int ExpiresIn,
    int Interval,
    DateTimeOffset CreatedAt,
    long SessionId = 0)
{
    public DateTimeOffset ExpiresAt => CreatedAt.AddSeconds(ExpiresIn);

    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;

    public override string ToString() =>
        $"[TraktDeviceCode UserCode={UserCode}, VerificationUrl={VerificationUrl}, ExpiresIn={ExpiresIn}, Interval={Interval}, SessionId={SessionId}]";
}

/// <summary>
/// Redacted OAuth token pair. ToString() never outputs raw tokens.
/// </summary>
public sealed record TraktTokenPair(
    string AccessToken,
    string RefreshToken,
    string TokenType,
    int ExpiresIn,
    string Scope,
    long CreatedAtUnixSeconds)
{
    public DateTimeOffset CreatedAt => DateTimeOffset.FromUnixTimeSeconds(CreatedAtUnixSeconds);

    public DateTimeOffset ExpiresAt => CreatedAt.AddSeconds(ExpiresIn);

    public bool IsExpired(DateTimeOffset now, TimeSpan? buffer = null) =>
        now >= ExpiresAt - (buffer ?? TimeSpan.FromMinutes(5));

    public override string ToString() => "[REDACTED_TRAKT_TOKENS]";
}

/// <summary>
/// Secure credential payload stored in protected storage.
/// Binds tokens to the client ID and optional verified account ID.
/// Supports a durable RequiresReconnect fail-closed recovery marker.
/// </summary>
public sealed record TraktCredentialPayload(
    string ClientId,
    string AccessToken,
    string RefreshToken,
    string TokenType,
    int ExpiresIn,
    string Scope,
    long CreatedAtUnixSeconds,
    string? AccountId = null,
    bool RequiresReconnect = false)
{
    public bool IsValidForClient(string expectedClientId)
    {
        if (!string.Equals(ClientId, expectedClientId, StringComparison.Ordinal))
        {
            return false;
        }

        if (RequiresReconnect)
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(AccessToken) &&
               !string.IsNullOrWhiteSpace(RefreshToken) &&
               CreatedAtUnixSeconds > 0 &&
               ExpiresIn >= 0;
    }

    public override string ToString() => "[REDACTED_TRAKT_CREDENTIAL]";
}

/// <summary>
/// Status returned by a device token poll request.
/// </summary>
public enum TraktPollStatus
{
    Success,
    Pending,
    Invalid,
    AlreadyUsed,
    Expired,
    Denied,
    Slowdown
}

/// <summary>
/// Response from polling the device token endpoint.
/// </summary>
public sealed record TraktPollResponse(
    TraktPollStatus Status,
    TraktTokenPair? Tokens = null,
    int? RetryAfterSeconds = null)
{
    public override string ToString() => $"[TraktPollResponse Status={Status}]";
}

/// <summary>
/// Connection state for Trakt authentication.
/// </summary>
public enum TraktAuthConnectionStatus
{
    NotConfigured,
    Connected,
    RequiresReconnect
}

/// <summary>
/// Public immutable snapshot of Trakt auth state.
/// </summary>
public sealed record TraktAuthState(
    TraktAuthConnectionStatus Status,
    bool IsConnected,
    bool RequiresReconnect,
    string? AccountId,
    DateTimeOffset? ExpiresAt)
{
    public static TraktAuthState NotConfigured { get; } =
        new(TraktAuthConnectionStatus.NotConfigured, false, false, null, null);

    public static TraktAuthState RequiresReconnection { get; } =
        new(TraktAuthConnectionStatus.RequiresReconnect, false, true, null, null);

    public static TraktAuthState ConnectedState(string? accountId, DateTimeOffset? expiresAt) =>
        new(TraktAuthConnectionStatus.Connected, true, false, accountId, expiresAt);
}

/// <summary>
/// General Trakt authentication exception. Never exposes raw tokens or confidential bodies.
/// </summary>
public class TraktAuthException : Exception
{
    public TraktAuthException(string message) : base(message) { }
    public TraktAuthException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Thrown when Trakt rejects a refresh token grant (e.g. invalid_grant).
/// </summary>
public sealed class TraktInvalidGrantException : TraktAuthException
{
    public TraktInvalidGrantException(string message) : base(message) { }
    public TraktInvalidGrantException(string message, Exception innerException) : base(message, innerException) { }
}
