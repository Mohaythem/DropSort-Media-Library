using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DropSort.Application.Trakt;
using DropSort.Infrastructure.Trakt;
using Xunit;

namespace DropSort.Tests.Infrastructure;

public sealed class TraktAuthClientTests : IDisposable
{
    private readonly MockHttpMessageHandler _mockHandler = new();
    private readonly TraktAuthClient _client;

    public TraktAuthClientTests()
    {
        var httpClient = new HttpClient(_mockHandler);
        _client = new TraktAuthClient(httpClient);
    }

    public void Dispose()
    {
        _client.Dispose();
        _mockHandler.Dispose();
    }

    [Fact]
    public async Task GenerateDeviceCode_SendsCorrectHostPathHeaders_AndNoSecret()
    {
        _mockHandler.ResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"device_code\":\"dev-123\",\"user_code\":\"USER-456\",\"verification_url\":\"https://trakt.tv/activate\",\"expires_in\":600,\"interval\":5}",
                Encoding.UTF8,
                "application/json")
        };

        var result = await _client.GenerateDeviceCodeAsync("client-id-789");

        Assert.NotNull(result);
        Assert.Equal("dev-123", result.DeviceCode);
        Assert.Equal("USER-456", result.UserCode);
        Assert.Equal("https://trakt.tv/activate", result.VerificationUrl);
        Assert.Equal(600, result.ExpiresIn);
        Assert.Equal(5, result.Interval);

        var request = Assert.Single(_mockHandler.Requests);
        Assert.Equal("https://auth.trakt.tv/oauth/device/code", request.Uri!.AbsoluteUri);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Contains("\"client_id\":\"client-id-789\"", request.Body);
        Assert.DoesNotContain("client_secret", request.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PollDeviceToken_SendsCorrectHostPathBody_WithCodeAndClientId_AndNoSecret()
    {
        _mockHandler.ResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"access_token\":\"acc-tok\",\"token_type\":\"bearer\",\"expires_in\":7200,\"refresh_token\":\"ref-tok\",\"scope\":\"public\",\"created_at\":1700000000}",
                Encoding.UTF8,
                "application/json")
        };

        var result = await _client.PollDeviceTokenAsync("dev-code-xyz", "client-id-789");

        Assert.Equal(TraktPollStatus.Success, result.Status);
        Assert.NotNull(result.Tokens);
        Assert.Equal("acc-tok", result.Tokens.AccessToken);
        Assert.Equal("ref-tok", result.Tokens.RefreshToken);
        Assert.Equal(7200, result.Tokens.ExpiresIn);

        var request = Assert.Single(_mockHandler.Requests);
        Assert.Equal("https://auth.trakt.tv/oauth/device/token", request.Uri!.AbsoluteUri);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Contains("\"code\":\"dev-code-xyz\"", request.Body);
        Assert.Contains("\"client_id\":\"client-id-789\"", request.Body);
        Assert.DoesNotContain("client_secret", request.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, TraktPollStatus.Pending)]
    [InlineData(HttpStatusCode.NotFound, TraktPollStatus.Invalid)]
    [InlineData(HttpStatusCode.Conflict, TraktPollStatus.AlreadyUsed)]
    [InlineData(HttpStatusCode.Gone, TraktPollStatus.Expired)]
    [InlineData((HttpStatusCode)418, TraktPollStatus.Denied)]
    public async Task PollDeviceToken_MapsStatusCodes_Correctly(HttpStatusCode statusCode, TraktPollStatus expectedStatus)
    {
        _mockHandler.ResponseFactory = _ => new HttpResponseMessage(statusCode);

        var result = await _client.PollDeviceTokenAsync("dev-code", "client-id");

        Assert.Equal(expectedStatus, result.Status);
        Assert.Null(result.Tokens);
    }

    [Fact]
    public async Task PollDeviceToken_Slowdown_ParsesRetryAfterHeader()
    {
        _mockHandler.ResponseFactory = _ =>
        {
            var response = new HttpResponseMessage((HttpStatusCode)429);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(15));
            return response;
        };

        var result = await _client.PollDeviceTokenAsync("dev-code", "client-id");

        Assert.Equal(TraktPollStatus.Slowdown, result.Status);
        Assert.Equal(15, result.RetryAfterSeconds);
    }

    [Fact]
    public async Task RefreshToken_SendsCorrectHostPathFields_AndNoSecret()
    {
        _mockHandler.ResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"access_token\":\"new-acc\",\"token_type\":\"bearer\",\"expires_in\":7200,\"refresh_token\":\"new-ref\",\"scope\":\"public\",\"created_at\":1700005000}",
                Encoding.UTF8,
                "application/json")
        };

        var tokens = await _client.RefreshTokenAsync(
            "client-id-789",
            "https://localhost/callback",
            "old-ref-token");

        Assert.NotNull(tokens);
        Assert.Equal("new-acc", tokens.AccessToken);
        Assert.Equal("new-ref", tokens.RefreshToken);

        var request = Assert.Single(_mockHandler.Requests);
        Assert.Equal("https://auth.trakt.tv/oauth/token", request.Uri!.AbsoluteUri);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Contains("\"client_id\":\"client-id-789\"", request.Body);
        Assert.Contains("\"redirect_uri\":\"https://localhost/callback\"", request.Body);
        Assert.Contains("\"refresh_token\":\"old-ref-token\"", request.Body);
        Assert.Contains("\"grant_type\":\"refresh_token\"", request.Body);
        Assert.DoesNotContain("client_secret", request.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RefreshToken_InvalidGrant_ThrowsTraktInvalidGrantException()
    {
        _mockHandler.ResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(
                "{\"error\":\"invalid_grant\",\"error_description\":\"The provided authorization grant is invalid, expired, revoked, does not match the redirection URI used in the authorization request, or was issued to another client.\"}",
                Encoding.UTF8,
                "application/json")
        };

        await Assert.ThrowsAsync<TraktInvalidGrantException>(() =>
            _client.RefreshTokenAsync("client-id", "https://localhost/callback", "bad-token"));
    }

    [Fact]
    public async Task RefreshToken_SanitizesJsonException_DoesNotLeakRawTokensInExceptionMessage()
    {
        _mockHandler.ResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"access_token\":\"super-secret-token-123\", bad_json...}",
                Encoding.UTF8,
                "application/json")
        };

        var ex = await Assert.ThrowsAsync<TraktAuthException>(() =>
            _client.RefreshTokenAsync("client-id", "https://localhost/callback", "ref-token"));

        Assert.DoesNotContain("super-secret-token-123", ex.Message);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public void TraktAuthConfig_RejectsNonHttpsRedirectUri_AndEnforcesFixedAuthHost()
    {
        Assert.Throws<ArgumentException>(() => new TraktAuthConfig("client-id", "http://insecure/callback"));
        Assert.Throws<ArgumentException>(() => new TraktAuthConfig("client-id", "ftp://invalid/callback"));

        var valid = new TraktAuthConfig("client-id", "https://localhost/callback");
        Assert.Equal("https://auth.trakt.tv", TraktAuthConfig.FixedAuthBaseUrl);
        Assert.Equal("client-id", valid.ClientId);
        Assert.Equal("https://localhost/callback", valid.RedirectUri);
    }

    [Fact]
    public void TraktModels_RedactAllSecretToString_IncludingDeviceCode()
    {
        var deviceCode = new TraktDeviceCode("secret-device-code", "USER-CODE", "https://trakt.tv/activate", 600, 5, DateTimeOffset.UtcNow, 42);
        var tokens = new TraktTokenPair("secret-access-token", "secret-refresh-token", "bearer", 3600, "public", 1700000000);
        var creds = new TraktCredentialPayload("client-id", "secret-access-token", "secret-refresh-token", "bearer", 3600, "public", 1700000000);

        Assert.DoesNotContain("secret-device-code", deviceCode.ToString());
        Assert.Contains("USER-CODE", deviceCode.ToString());
        Assert.Equal("[REDACTED_TRAKT_TOKENS]", tokens.ToString());
        Assert.Equal("[REDACTED_TRAKT_CREDENTIAL]", creds.ToString());
        Assert.DoesNotContain("secret-access-token", tokens.ToString());
        Assert.DoesNotContain("secret-refresh-token", creds.ToString());
    }

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> ResponseFactory { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.OK);
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content != null ? await request.Content.ReadAsStringAsync(cancellationToken) : string.Empty;
            Requests.Add(new RecordedRequest(request.Method, request.RequestUri, body, request.Headers.Accept.ToString()));
            return ResponseFactory(request);
        }
    }

    private sealed record RecordedRequest(HttpMethod Method, Uri? Uri, string Body, string AcceptHeader);
}
