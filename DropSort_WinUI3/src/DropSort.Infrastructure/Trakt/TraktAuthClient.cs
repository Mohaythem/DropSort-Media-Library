using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.IO;
using System.Linq;
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
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        clientId = clientId.Trim();

        var requestUri = new Uri($"{FixedAuthBaseUrl}/oauth/device/code");
        var requestDto = new DeviceCodeRequestDto(clientId);
        var json = JsonSerializer.Serialize(requestDto);

        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        TraktRequestHeaders.IdentifyApplication(request);
        request.Headers.Add("trakt-api-key", clientId);
        request.Headers.Add("trakt-api-version", "2");

        // ResponseHeadersRead permits a bounded failure-body read. Keep the existing HTTP timeout
        // covering both headers and content, including successful device-code responses.
        using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        requestCts.CancelAfter(_httpClient.Timeout);
        var requestToken = requestCts.Token;
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, requestToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await DeviceCodeFailureAsync(response, clientId, requestToken);
        }

        var responseJson = await response.Content.ReadAsStringAsync(requestToken);
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

    private static async Task<TraktAuthException> DeviceCodeFailureAsync(
        HttpResponseMessage response, string clientId, CancellationToken cancellationToken)
    {
        // Never display raw response text: gateways and OAuth errors can echo credentials.
        // Bound the diagnostic read even for chunked responses, preserving caller cancellation.
        var buffer = new byte[4096];
        var count = 0;
        var bodyAvailable = true;
        using var readCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        readCts.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            using var stream = await response.Content.ReadAsStreamAsync(readCts.Token);
            while (count < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(count), readCts.Token);
                if (read == 0) break;
                count += read;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { bodyAvailable = false; }
        catch (IOException) { bodyAvailable = false; }
        catch (HttpRequestException) { bodyAvailable = false; }
        cancellationToken.ThrowIfCancellationRequested();

        var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        var text = Encoding.UTF8.GetString(buffer, 0, count).TrimStart();
        var kind = bodyAvailable ? "non-JSON" : "body unavailable";
        string? errorCode = null;
        if (bodyAvailable && (mediaType.Contains("html", StringComparison.OrdinalIgnoreCase) || text.StartsWith('<')))
            kind = "HTML/gateway";
        else if (bodyAvailable && (mediaType.Contains("json", StringComparison.OrdinalIgnoreCase) || text.StartsWith('{')))
        {
            kind = "unreadable JSON";
            try
            {
                using var json = JsonDocument.Parse(buffer.AsMemory(0, count), new JsonDocumentOptions { MaxDepth = 8 });
                kind = "JSON";
                if (json.RootElement.ValueKind == JsonValueKind.Object &&
                    json.RootElement.TryGetProperty("error", out var value) && value.ValueKind == JsonValueKind.String)
                {
                    // Only known protocol codes are safe. Descriptions and arbitrary codes are omitted.
                    var code = value.GetString();
                    if (code is "invalid_client" or "invalid_api_key" or "unauthorized_client" or
                        "access_denied" or "unapproved_app" or "invalid_request")
                        errorCode = string.Equals(code, clientId, StringComparison.OrdinalIgnoreCase) ? null : code;
                }
            }
            catch (JsonException) { }
        }

        var correlation = string.Empty;
        if (response.Headers.TryGetValues("X-Request-ID", out var ids))
        {
            var id = ids.FirstOrDefault();
            if (id is { Length: <= 36 } && Guid.TryParseExact(id, "D", out var parsed) &&
                !id.Contains(clientId, StringComparison.OrdinalIgnoreCase))
                correlation = $"; request_id={parsed:D}";
        }
        if (response.Headers.TryGetValues("CF-Ray", out var rays))
        {
            var ray = rays.FirstOrDefault();
            if (ray is { Length: >= 20 and <= 22 } && ray[16] == '-' &&
                ray[..16].All(Uri.IsHexDigit) && ray[17..].All(c => c is >= 'A' and <= 'Z') &&
                !ray.Contains(clientId, StringComparison.OrdinalIgnoreCase))
                correlation += $"; cf_ray={ray}";
        }

        var advice = response.StatusCode == HttpStatusCode.Forbidden
            ? " Verify the Trakt API Client ID and app approval/status in the developer dashboard. A 403 alone does not identify the cause."
            : string.Empty;
        return new TraktAuthException(
            $"Trakt device code request failed with HTTP status {(int)response.StatusCode}.{advice} " +
            $"[POST {FixedAuthBaseUrl}/oauth/device/code; response={kind}" +
            (errorCode is null ? string.Empty : $"; error={errorCode}") + correlation + "]");
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
        TraktRequestHeaders.IdentifyApplication(request);
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
        TraktRequestHeaders.IdentifyApplication(request);
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
