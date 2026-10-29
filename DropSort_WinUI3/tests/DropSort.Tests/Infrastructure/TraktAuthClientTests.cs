using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DropSort.Application.Trakt;
using DropSort.Infrastructure.Trakt;
using DropSort.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
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

    [Theory]
    [InlineData("device")]
    [InlineData("poll")]
    [InlineData("refresh")]
    public async Task OAuthRequests_IdentifyDropSortWithVersion_WithoutMutatingSharedClient(string operation)
    {
        using var http = new HttpClient(_mockHandler);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("SharedHost/2.0");
        using var client = new TraktAuthClient(http);
        _mockHandler.ResponseFactory = request =>
        {
            var product = Assert.Single(request.Headers.UserAgent).Product;
            Assert.NotNull(product);
            Assert.Equal("DropSort", product.Name);
            Assert.True(Version.TryParse(product.Version, out _));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"device_code":"fixture-device","user_code":"FIXTURE","verification_url":"https://auth.trakt.tv/activate","expires_in":600,"interval":5,"access_token":"fixture-access","refresh_token":"fixture-refresh","created_at":1700000000}""", Encoding.UTF8, "application/json")
            };
        };
        if (operation == "device") await client.GenerateDeviceCodeAsync("fixture-client");
        else if (operation == "poll") await client.PollDeviceTokenAsync("fixture-device", "fixture-client");
        else await client.RefreshTokenAsync("fixture-client", "https://localhost/callback", "fixture-refresh");
        Assert.Equal("SharedHost/2.0", http.DefaultRequestHeaders.UserAgent.ToString());
        Assert.Single(_mockHandler.Requests);
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
        Assert.Equal("client-id-789", request.ApiKey);
        Assert.Equal("2", request.ApiVersion);
        Assert.Equal("application/json", request.ContentType);
        Assert.Contains("\"client_id\":\"client-id-789\"", request.Body);
        Assert.DoesNotContain("client_secret", request.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("application/json", "{\"error\":\"invalid_client\",\"error_description\":\"echo-sensitive-value\",\"access_token\":\"echo-sensitive-value\"}", "JSON; error=invalid_client")]
    [InlineData("text/html", "<html>Cloudflare echo-sensitive-value</html>", "HTML/gateway")]
    [InlineData("application/json", "{bad-json echo-sensitive-value", "unreadable JSON")]
    [InlineData("text/plain", "echo-sensitive-value", "non-JSON")]
    public async Task DeviceCode_Forbidden_IsActionableSanitized_AndNeverRetried(string mediaType, string body, string expectedKind)
    {
        _mockHandler.ResponseFactory = _ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent(body, Encoding.UTF8, mediaType)
            };
            response.Headers.Add("X-Request-ID", "3d82a9d1-27f2-42eb-b41d-559fa23fbe8b");
            return response;
        };
        var error = await Assert.ThrowsAsync<TraktAuthException>(() => _client.GenerateDeviceCodeAsync("echo-sensitive-value"));
        Assert.Contains("HTTP status 403", error.Message);
        Assert.Contains("Client ID", error.Message);
        Assert.Contains("app approval/status", error.Message);
        Assert.Contains("POST https://auth.trakt.tv/oauth/device/code", error.Message);
        Assert.Contains(expectedKind, error.Message);
        Assert.Contains("3d82a9d1-27f2-42eb-b41d-559fa23fbe8b", error.Message);
        Assert.DoesNotContain("echo-sensitive-value", error.ToString());
        Assert.Null(error.InnerException);
        Assert.Single(_mockHandler.Requests);
    }

    [Fact]
    public async Task DeviceCode_RejectsUntrustedErrorAndCorrelationText()
    {
        _mockHandler.ResponseFactory = _ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("{\"error\":\"secret-access-token\",\"error_description\":\"secret-refresh-token\"}", Encoding.UTF8, "application/json")
            };
            response.Headers.TryAddWithoutValidation("X-Request-ID", "secret-access-token");
            response.Headers.TryAddWithoutValidation("CF-Ray", "secret-refresh-token");
            return response;
        };
        var error = await Assert.ThrowsAsync<TraktAuthException>(() => _client.GenerateDeviceCodeAsync("synthetic-client"));
        Assert.DoesNotContain("secret-", error.ToString());
    }

    [Fact]
    public async Task DeviceCode_CancelledRequest_PropagatesWithoutRetry()
    {
        using var cts = new CancellationTokenSource();
        _mockHandler.ResponseFactory = _ => { cts.Cancel(); throw new OperationCanceledException(cts.Token); };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _client.GenerateDeviceCodeAsync("synthetic-client", cts.Token));
        Assert.Single(_mockHandler.Requests);
    }

    [Fact]
    public async Task DeviceCode_PreCancelled_DoesNotSend()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _client.GenerateDeviceCodeAsync("synthetic-client", cts.Token));
        Assert.Empty(_mockHandler.Requests);
    }

    [Fact]
    public async Task SavedConfiguration_ReadByNewRepository_UsesTrimmedLatestClientInRealAuthService()
    {
        // Exercise the persisted wire shape and production repository/service/client with a disposable
        // settings-only database. This does not execute AppServices or a SettingsPage UI command.
        var directory = Path.Combine(Path.GetTempPath(), $"dropsort-trakt-config-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var connectionString = $"Data Source={Path.Combine(directory, "settings.db")};Pooling=False";
        try
        {
            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE settings (key TEXT PRIMARY KEY, value TEXT NOT NULL, updated_at TEXT NOT NULL);";
                command.ExecuteNonQuery();
            }
            var repository = new SettingsRepository(connectionString);
            repository.Set("trakt_api_config", "{\"ClientId\":\"old-synthetic-id\",\"RedirectUri\":\"https://localhost/old\"}", DateTimeOffset.UtcNow);
            var savedConfig = new TraktAuthConfig("  latest-synthetic-id  ", " https://localhost/trakt/callback ");
            repository.Set("trakt_api_config", JsonSerializer.Serialize(new { savedConfig.ClientId, savedConfig.RedirectUri }), DateTimeOffset.UtcNow);

            var reopened = new SettingsRepository(connectionString);
            using var document = JsonDocument.Parse(reopened.Get("trakt_api_config")!);
            var config = new TraktAuthConfig(document.RootElement.GetProperty("ClientId").GetString()!,
                document.RootElement.GetProperty("RedirectUri").GetString()!);
            _mockHandler.ResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"device_code\":\"synthetic-device\",\"user_code\":\"SYNTHETIC\",\"verification_url\":\"https://auth.trakt.tv/activate\",\"expires_in\":600,\"interval\":5}", Encoding.UTF8, "application/json")
            };
            var service = new TraktAuthService(_client, new EmptyCredentialStore(), config);
            var result = await service.BeginDeviceAuthAsync();
            Assert.Equal("SYNTHETIC", result.UserCode);
            var request = Assert.Single(_mockHandler.Requests);
            Assert.Equal("latest-synthetic-id", request.ApiKey);
            using var body = JsonDocument.Parse(request.Body);
            Assert.Equal("latest-synthetic-id", body.RootElement.GetProperty("client_id").GetString());
            Assert.Single(body.RootElement.EnumerateObject());
            Assert.Equal("https://localhost/trakt/callback", config.RedirectUri);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task DeviceCode_ErrorBodyReadIsBounded_AndCfRayIsSafe()
    {
        var stream = new CountingStream(Encoding.UTF8.GetBytes("{\"error_description\":\"" + new string('x', 100_000) + "\"}"));
        _mockHandler.ResponseFactory = _ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StreamContent(stream) };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            response.Headers.Add("CF-Ray", "0123456789abcdef-CAI");
            return response;
        };
        var error = await Assert.ThrowsAsync<TraktAuthException>(() => _client.GenerateDeviceCodeAsync("synthetic-client"));
        Assert.Equal(4096, stream.ReadBytes);
        Assert.Contains("cf_ray=0123456789abcdef-CAI", error.Message);
        Assert.Contains("unreadable JSON", error.Message);
        Assert.True(error.Message.Length < 700);
    }

    private sealed class CountingStream(byte[] bytes) : MemoryStream(bytes)
    {
        public int ReadBytes { get; private set; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var count = await base.ReadAsync(buffer, cancellationToken);
            ReadBytes += count;
            return count;
        }
    }

    [Fact]
    public async Task DeviceCode_UnreadableErrorStream_PreservesStatusWithoutLeakingTransportText()
    {
        _mockHandler.ResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StreamContent(new FailedReadStream())
        };
        var error = await Assert.ThrowsAsync<TraktAuthException>(() => _client.GenerateDeviceCodeAsync("synthetic-client"));
        Assert.Contains("HTTP status 403", error.Message);
        Assert.Contains("body unavailable", error.Message);
        Assert.DoesNotContain("sensitive-transport-text", error.ToString());
        Assert.Null(error.InnerException);
    }

    private sealed class FailedReadStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(new IOException("sensitive-transport-text"));
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task DeviceCode_HttpTimeoutStillCoversResponseBody(HttpStatusCode status)
    {
        _mockHandler.ResponseFactory = _ => new HttpResponseMessage(status) { Content = new StreamContent(new WaitingStream()) };
        using var http = new HttpClient(_mockHandler) { Timeout = TimeSpan.FromMilliseconds(100) };
        using var client = new TraktAuthClient(http);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GenerateDeviceCodeAsync("synthetic-client"));
        Assert.Single(_mockHandler.Requests);
    }

    private sealed class WaitingStream : MemoryStream
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken) =>
            Task.Delay(Timeout.Infinite, cancellationToken);
    }

    private sealed class EmptyCredentialStore : ITraktCredentialStore
    {
        public TraktCredentialPayload? Read() => null;
        public void Write(TraktCredentialPayload payload) => throw new InvalidOperationException("Device-code generation must not write credentials.");
        public void Delete() => throw new InvalidOperationException("Device-code generation must not delete credentials.");
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
            Requests.Add(new RecordedRequest(request.Method, request.RequestUri, body, request.Headers.Accept.ToString(),
                request.Headers.TryGetValues("trakt-api-key", out var keys) ? string.Join("", keys) : null,
                request.Headers.TryGetValues("trakt-api-version", out var versions) ? string.Join("", versions) : null,
                request.Content?.Headers.ContentType?.MediaType));
            return ResponseFactory(request);
        }
    }

    private sealed record RecordedRequest(HttpMethod Method, Uri? Uri, string Body, string AcceptHeader,
        string? ApiKey, string? ApiVersion, string? ContentType);
}
