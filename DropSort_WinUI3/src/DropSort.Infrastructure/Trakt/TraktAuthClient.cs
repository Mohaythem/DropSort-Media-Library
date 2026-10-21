using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DropSort.Application.Trakt;

namespace DropSort.Infrastructure.Trakt;

/// <summary>
/// OAuth HTTP client for Trakt authentication (strictly fixed to https://auth.trakt.tv).
/// </summary>
public sealed class TraktAuthClient : ITraktAuthClient, IDisposable
{
    public const string FixedAuthBaseUrl = "https://auth.trakt.tv";

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public TraktAuthClient(HttpClient? httpClient = null)
    {
        if (httpClient is null)
        {
            _httpClient = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false });
            _ownsHttpClient = true;
        }
        else
        {
            _httpClient = httpClient;
            _ownsHttpClient = false;
        }
    }

    public async Task<TraktDeviceCode> GenerateDeviceCodeAsync(string clientId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);

        var requestUri = new Uri($"{FixedAuthBaseUrl}/oauth/device/code");
        var requestDto = new DeviceCodeRequestDto(clientId);
        var json = JsonSerializer.Serialize(requestDto);

        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("trakt-api-key", clientId);
        request.Headers.Add("trakt-api-version", "2");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new TraktAuthException($"Trakt device code request failed with HTTP status {(int)response.StatusCode}.");
        }

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        DeviceCodeResponseDto? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<DeviceCodeResponseDto>(responseJson);
        }
        catch (JsonException)
        {
            throw new TraktAuthException("Trakt returned an unparseable device code response payload.");
        }

        if (parsed is null || string.IsNullOrWhiteSpace(parsed.DeviceCode) || string.IsNullOrWhiteSpace(parsed.UserCode))
        {
            throw new TraktAuthException("Trakt returned an empty or invalid device code payload.");
        }

        return new TraktDeviceCode(
            parsed.DeviceCode,
            parsed.UserCode,
            parsed.VerificationUrl,
            parsed.ExpiresIn,
            parsed.Interval,
            DateTimeOffset.UtcNow);
    }

    public async Task<TraktPollResponse> PollDeviceTokenAsync(string deviceCode, string clientId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);

        var requestUri = new Uri($"{FixedAuthBaseUrl}/oauth/device/token");
        var requestDto = new DeviceTokenRequestDto(deviceCode, clientId);
        var json = JsonSerializer.Serialize(requestDto);

        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("trakt-api-key", clientId);
        request.Headers.Add("trakt-api-version", "2");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var statusCode = (int)response.StatusCode;

        return statusCode switch
        {
            200 => await ParseTokenSuccessAsync(response, cancellationToken),
            400 => new TraktPollResponse(TraktPollStatus.Pending),
            404 => new TraktPollResponse(TraktPollStatus.Invalid),
            409 => new TraktPollResponse(TraktPollStatus.AlreadyUsed),
            410 => new TraktPollResponse(TraktPollStatus.Expired),
            418 => new TraktPollResponse(TraktPollStatus.Denied),
            429 => ParseSlowdown(response),
            _ => throw new TraktAuthException($"Trakt device token polling failed with HTTP status {statusCode}.")
        };
    }

    public async Task<TraktTokenPair> RefreshTokenAsync(
        string clientId,
        string redirectUri,
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(redirectUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);

        var requestUri = new Uri($"{FixedAuthBaseUrl}/oauth/token");
        var requestDto = new TokenRefreshRequestDto(clientId, redirectUri, refreshToken);
        var json = JsonSerializer.Serialize(requestDto);

        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("trakt-api-key", clientId);
        request.Headers.Add("trakt-api-version", "2");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var statusCode = (int)response.StatusCode;

        if (statusCode == 200)
        {
            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            TokenResponseDto? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<TokenResponseDto>(responseJson);
            }
            catch (JsonException)
            {
                throw new TraktAuthException("Trakt returned an unparseable refresh token response payload.");
            }

            if (parsed is null || string.IsNullOrWhiteSpace(parsed.AccessToken) || string.IsNullOrWhiteSpace(parsed.RefreshToken))
            {
                throw new TraktAuthException("Trakt returned an empty or invalid refresh token payload.");
            }

            return new TraktTokenPair(
                parsed.AccessToken,
                parsed.RefreshToken,
                parsed.TokenType ?? "bearer",
                parsed.ExpiresIn,
                parsed.Scope ?? "public",
                parsed.CreatedAt > 0 ? parsed.CreatedAt : DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        }

        if (statusCode is 400 or 401)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (errorBody.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase))
            {
                throw new TraktInvalidGrantException("The Trakt refresh token is invalid, expired, or revoked.");
            }

            throw new TraktAuthException($"Trakt token refresh failed with HTTP status {statusCode}.");
        }

        throw new TraktAuthException($"Trakt token refresh failed with HTTP status {statusCode}.");
    }

    private static async Task<TraktPollResponse> ParseTokenSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        TokenResponseDto? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<TokenResponseDto>(responseJson);
        }
        catch (JsonException)
        {
            throw new TraktAuthException("Trakt returned an unparseable token response payload.");
        }

        if (parsed is null || string.IsNullOrWhiteSpace(parsed.AccessToken) || string.IsNullOrWhiteSpace(parsed.RefreshToken))
        {
            throw new TraktAuthException("Trakt returned an empty or invalid token payload.");
        }

        var tokens = new TraktTokenPair(
            parsed.AccessToken,
            parsed.RefreshToken,
            parsed.TokenType ?? "bearer",
            parsed.ExpiresIn,
            parsed.Scope ?? "public",
            parsed.CreatedAt > 0 ? parsed.CreatedAt : DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        return new TraktPollResponse(TraktPollStatus.Success, Tokens: tokens);
    }

    private static TraktPollResponse ParseSlowdown(HttpResponseMessage response)
    {
        int? retryAfterSeconds = null;
        if (response.Headers.RetryAfter != null)
        {
            if (response.Headers.RetryAfter.Delta.HasValue && response.Headers.RetryAfter.Delta.Value.TotalSeconds > 0)
            {
                retryAfterSeconds = (int)Math.Min(int.MaxValue, Math.Ceiling(response.Headers.RetryAfter.Delta.Value.TotalSeconds));
            }
            else if (response.Headers.RetryAfter.Date is { } retryAt)
                retryAfterSeconds = (int)Math.Clamp(Math.Ceiling((retryAt - DateTimeOffset.UtcNow).TotalSeconds), 0, int.MaxValue);
        }

        return new TraktPollResponse(TraktPollStatus.Slowdown, RetryAfterSeconds: retryAfterSeconds);
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private sealed record DeviceCodeRequestDto(
        [property: JsonPropertyName("client_id")] string ClientId)
    {
        public override string ToString() => "[REDACTED_DEVICE_CODE_REQUEST]";
    }

    private sealed record DeviceCodeResponseDto(
        [property: JsonPropertyName("device_code")] string DeviceCode,
        [property: JsonPropertyName("user_code")] string UserCode,
        [property: JsonPropertyName("verification_url")] string VerificationUrl,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        [property: JsonPropertyName("interval")] int Interval)
    {
        public override string ToString() => "[REDACTED_DEVICE_CODE_DTO]";
    }

    private sealed record DeviceTokenRequestDto(
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("client_id")] string ClientId)
    {
        public override string ToString() => "[REDACTED_DEVICE_TOKEN_REQUEST]";
    }

    private sealed record TokenResponseDto(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("token_type")] string? TokenType,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        [property: JsonPropertyName("refresh_token")] string RefreshToken,
        [property: JsonPropertyName("scope")] string? Scope,
        [property: JsonPropertyName("created_at")] long CreatedAt)
    {
        public override string ToString() => "[REDACTED_TOKEN_DTO]";
    }

    private sealed record TokenRefreshRequestDto(
        [property: JsonPropertyName("client_id")] string ClientId,
        [property: JsonPropertyName("redirect_uri")] string RedirectUri,
        [property: JsonPropertyName("refresh_token")] string RefreshToken,
        [property: JsonPropertyName("grant_type")] string GrantType = "refresh_token")
    {
        public override string ToString() => "[REDACTED_TOKEN_REFRESH_REQUEST]";
    }
}
