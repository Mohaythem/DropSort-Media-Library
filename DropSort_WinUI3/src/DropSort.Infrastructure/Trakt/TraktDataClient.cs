using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DropSort.Application.Trakt;

namespace DropSort.Infrastructure.Trakt;

/// <summary>
/// Production HTTP GET client for Trakt API v2.
/// Implements whitelisted endpoints, strict pagination, rate limiting, and memory bounds.
/// </summary>
public sealed class TraktDataClient : ITraktDataClient, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly TimeProvider _timeProvider;
    private readonly TraktDataClientOptions _options;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;

    public TraktDataClient(
        HttpClient? httpClient = null,
        TimeProvider? timeProvider = null,
        TraktDataClientOptions? options = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _options = options ?? new TraktDataClientOptions();
        _delayAsync = delayAsync ?? ((delay, ct) => Task.Delay(delay, ct));

        if (httpClient is null)
        {
            var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false
            };
            _httpClient = new HttpClient(handler, disposeHandler: true);
            _ownsHttpClient = true;
        }
        else
        {
            _httpClient = httpClient;
            _ownsHttpClient = false;
        }
    }

    /// <inheritdoc />
    public async Task<JsonElement> GetAccountAsync(
        string clientId,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        const string relativePath = "users/settings";
        var uri = new Uri($"{TraktDataConfig.ApiBaseUrl}/{relativePath}");

        using var response = await SendWithRetryAsync(
            uri,
            relativePath,
            clientId,
            accessToken,
            cancellationToken);

        var payloadBytes = await ReadBoundedStreamAsync(
            response.Content,
            _options.MaxResponseBytesPerPage,
            relativePath,
            cancellationToken);

        try
        {
            using var doc = JsonDocument.Parse(payloadBytes);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new TraktClientException(
                    TraktFailureKind.InvalidResponse,
                    "Expected JSON object payload for user settings.",
                    relativePath,
                    response.StatusCode);
            }

            // Verify immutable user.ids.uuid presence
            if (!doc.RootElement.TryGetProperty("user", out var userEl) ||
                !userEl.TryGetProperty("ids", out var idsEl) ||
                !idsEl.TryGetProperty("uuid", out var uuidEl) ||
                string.IsNullOrWhiteSpace(uuidEl.GetString()))
            {
                throw new TraktClientException(
                    TraktFailureKind.InvalidResponse,
                    "Response payload missing immutable 'user.ids.uuid' identifier.",
                    relativePath,
                    response.StatusCode);
            }

            return doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new TraktClientException(
                TraktFailureKind.InvalidResponse,
                "Malformed JSON received for user settings.",
                relativePath,
                response.StatusCode,
                ex);
        }
    }

    /// <inheritdoc />
    public async Task<JsonElement> GetResourceAsync(
        TraktResourceSpec resource,
        string clientId,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        var relativePath = resource.GetRelativePath();
        long cumulativeBytes = 0;
        var snapshotBuffer = new ArrayBufferWriter<byte>();
        using var snapshotWriter = new Utf8JsonWriter(snapshotBuffer);
        snapshotWriter.WriteStartArray();
        var accumulatedCount = 0;

        int currentPage = 1;
        int expectedPageCount = 1;
        int expectedItemCount = 0;
        int expectedLimit = TraktDataConfig.DefaultPaginationLimit;
        bool isPaginationActive = true;

        while (currentPage <= expectedPageCount)
        {
            var query = isPaginationActive
                ? $"?page={currentPage}&limit={TraktDataConfig.DefaultPaginationLimit}"
                : "";
            if (resource.ResourceType == TraktResourceType.WatchedShows)
                query += (query.Length == 0 ? "?" : "&") + "extended=progress";

            var uri = new Uri($"{TraktDataConfig.ApiBaseUrl}/{relativePath}{query}");

            using var response = await SendWithRetryAsync(
                uri,
                relativePath,
                clientId,
                accessToken,
                cancellationToken);

            var pageBytes = await ReadBoundedStreamAsync(
                response.Content,
                _options.MaxResponseBytesPerPage,
                relativePath,
                cancellationToken);

            cumulativeBytes += pageBytes.Length;
            if (cumulativeBytes > _options.MaxCumulativeResponseBytes)
            {
                throw new TraktClientException(
                    TraktFailureKind.InvalidResponse,
                    $"Cumulative response exceeded limit of {_options.MaxCumulativeResponseBytes} bytes.",
                    relativePath,
                    response.StatusCode);
            }

            // Inspect pagination headers
            var hasPage = TryGetHeaderInt(response.Headers, "X-Pagination-Page", out var headerPage);
            var hasLimit = TryGetHeaderInt(response.Headers, "X-Pagination-Limit", out var headerLimit);
            var hasPageCount = TryGetHeaderInt(response.Headers, "X-Pagination-Page-Count", out var headerPageCount);
            var hasItemCount = TryGetHeaderInt(response.Headers, "X-Pagination-Item-Count", out var headerItemCount);

            var hasAnyPagination = response.Headers.Any(h => h.Key.StartsWith("X-Pagination-", StringComparison.OrdinalIgnoreCase));
            if ((hasAnyPagination && !(hasPage && hasLimit && hasPageCount && hasItemCount)) ||
                (hasPage && (headerPage != currentPage || headerLimit <= 0 || headerPageCount < 0 || headerItemCount < 0 ||
                    (headerItemCount > 0 && headerPageCount <= 0) ||
                    (headerItemCount == 0 && headerPageCount > 1))))
                throw new TraktClientException(TraktFailureKind.InvalidResponse,
                    "Invalid or incomplete pagination metadata.", relativePath, response.StatusCode);

            if (currentPage == 1)
            {
                if (!hasPage || !hasLimit || !hasPageCount || !hasItemCount)
                {
                    if (resource.RequiresPaginationHeaders())
                    {
                        throw new TraktClientException(
                            TraktFailureKind.InvalidResponse,
                            "Declared paginated endpoint missing required X-Pagination headers.",
                            relativePath,
                            response.StatusCode);
                    }

                    // Optional list catalog returned unpaginated response
                    isPaginationActive = false;
                }
                else
                {
                    if (headerPageCount > _options.MaxAllowedPages)
                    {
                        throw new TraktClientException(
                            TraktFailureKind.InvalidResponse,
                            $"Advertised page count {headerPageCount} exceeds maximum allowed {_options.MaxAllowedPages}.",
                            relativePath,
                            response.StatusCode);
                    }

                    expectedPageCount = headerPageCount;
                    expectedItemCount = headerItemCount;
                    expectedLimit = headerLimit;
                }
            }
            else
            {
                // Verify cross-page header consistency
                if (headerPage != currentPage ||
                    headerLimit != expectedLimit ||
                    headerPageCount != expectedPageCount ||
                    headerItemCount != expectedItemCount)
                {
                    throw new TraktClientException(
                        TraktFailureKind.InvalidResponse,
                        $"Pagination headers inconsistent on page {currentPage}.",
                        relativePath,
                        response.StatusCode);
                }
            }

            // Parse and clone page items
            try
            {
                using var pageDoc = JsonDocument.Parse(pageBytes);
                if (pageDoc.RootElement.ValueKind != JsonValueKind.Array)
                {
                    throw new TraktClientException(
                        TraktFailureKind.InvalidResponse,
                        "Expected JSON array for resource page payload.",
                        relativePath,
                        response.StatusCode);
                }

                var pageItems = pageDoc.RootElement.EnumerateArray().ToList();

                // Tolerate validated empty dataset
                if (currentPage == 1 && isPaginationActive && expectedItemCount == 0)
                {
                    if (pageItems.Count != 0)
                    {
                        throw new TraktClientException(
                            TraktFailureKind.InvalidResponse,
                            "Dataset reported zero items but returned non-empty payload.",
                            relativePath,
                            response.StatusCode);
                    }

                    return CreateClonedArraySnapshot(Array.Empty<JsonElement>());
                }

                // Never stop on empty before advertised pages
                if (isPaginationActive && currentPage < expectedPageCount && pageItems.Count == 0)
                {
                    throw new TraktClientException(
                        TraktFailureKind.InvalidResponse,
                        $"Empty page received at page {currentPage} before advertised {expectedPageCount} pages.",
                        relativePath,
                        response.StatusCode);
                }

                foreach (var item in pageItems)
                {
                    item.WriteTo(snapshotWriter);
                    accumulatedCount++;
                }
            }
            catch (JsonException ex)
            {
                throw new TraktClientException(
                    TraktFailureKind.InvalidResponse,
                    $"Malformed JSON on page {currentPage}.",
                    relativePath,
                    response.StatusCode,
                    ex);
            }

            if (!isPaginationActive)
            {
                break;
            }

            currentPage++;
        }

        // Verify total items count against header specification
        if (isPaginationActive && accumulatedCount != expectedItemCount)
        {
            throw new TraktClientException(
                TraktFailureKind.InvalidResponse,
                $"Accumulated item count {accumulatedCount} does not match advertised {expectedItemCount}.",
                relativePath);
        }

        snapshotWriter.WriteEndArray();
        snapshotWriter.Flush();
        using var snapshot = JsonDocument.Parse(snapshotBuffer.WrittenMemory);
        return snapshot.RootElement.Clone();
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(
        Uri uri,
        string endpoint,
        string clientId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        int attempt = 0;

        while (true)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Add("trakt-api-key", clientId);
            request.Headers.Add("trakt-api-version", TraktDataConfig.ApiVersion);

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new TraktClientException(
                    TraktFailureKind.Network,
                    "Network error occurred while calling Trakt endpoint.",
                    endpoint,
                    innerException: ex);
            }

            // Immediately fail closed on redirects
            if ((int)response.StatusCode is >= 300 and <= 399)
            {
                var code = response.StatusCode;
                response.Dispose();
                throw new TraktClientException(
                    TraktFailureKind.HttpError,
                    $"HTTP redirect not permitted (status {(int)code}).",
                    endpoint,
                    code);
            }

            // 401 Unauthorized fails immediately without retry
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                response.Dispose();
                throw new TraktClientException(
                    TraktFailureKind.Unauthorized,
                    "Trakt rejected authentication credentials (HTTP 401).",
                    endpoint,
                    HttpStatusCode.Unauthorized);
            }

            // 429 Rate Limited handles Retry-After
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                if (attempt >= _options.MaxRateLimitRetries)
                {
                    response.Dispose();
                    throw new TraktClientException(
                        TraktFailureKind.RateLimited,
                        $"Rate limit retry attempts exhausted ({attempt} retries).",
                        endpoint,
                        HttpStatusCode.TooManyRequests);
                }

                var delay = CalculateRetryDelay(response.Headers.RetryAfter, attempt);
                response.Dispose();

                if (delay > _options.MaxRateLimitDelayOrDefault)
                {
                    throw new TraktClientException(
                        TraktFailureKind.RateLimited,
                        $"Retry delay {delay.TotalSeconds:F1}s exceeded maximum allowable {_options.MaxRateLimitDelayOrDefault.TotalSeconds:F1}s.",
                        endpoint,
                        HttpStatusCode.TooManyRequests);
                }

                await _delayAsync(delay, cancellationToken);
                attempt++;
                continue;
            }

            // Other non-success HTTP codes
            if (!response.IsSuccessStatusCode)
            {
                var code = response.StatusCode;
                response.Dispose();
                throw new TraktClientException(
                    TraktFailureKind.HttpError,
                    $"Trakt server returned HTTP status {(int)code}.",
                    endpoint,
                    code);
            }

            return response;
        }
    }

    private TimeSpan CalculateRetryDelay(RetryConditionHeaderValue? retryAfter, int attempt)
    {
        TimeSpan delay;

        if (retryAfter?.Delta is not null)
        {
            delay = retryAfter.Delta.Value;
        }
        else if (retryAfter?.Date is not null)
        {
            delay = retryAfter.Date.Value - _timeProvider.GetUtcNow();
        }
        else
        {
            // Exponential fallback: 2s, 4s, 8s
            delay = TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
        }

        if (delay < TimeSpan.Zero)
        {
            delay = TimeSpan.Zero;
        }

        // Add randomized jitter (50ms - 250ms)
        var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(50, 250));
        return delay + jitter;
    }

    private static async Task<byte[]> ReadBoundedStreamAsync(
        HttpContent content,
        int maxBytes,
        string endpoint,
        CancellationToken cancellationToken)
    {
        using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var memoryStream = new MemoryStream();
        var buffer = new byte[8192];
        long totalBytesRead = 0;
        int read;

        while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
        {
            totalBytesRead += read;
            if (totalBytesRead > maxBytes)
            {
                throw new TraktClientException(
                    TraktFailureKind.InvalidResponse,
                    $"Single response payload exceeded safe limit of {maxBytes} bytes.",
                    endpoint);
            }

            memoryStream.Write(buffer, 0, read);
        }

        return memoryStream.ToArray();
    }

    private static JsonElement CreateClonedArraySnapshot(IReadOnlyList<JsonElement> items)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray();
            foreach (var item in items)
            {
                item.WriteTo(writer);
            }
            writer.WriteEndArray();
        }

        using var doc = JsonDocument.Parse(buffer.WrittenMemory);
        return doc.RootElement.Clone();
    }

    private static bool TryGetHeaderInt(HttpResponseHeaders headers, string name, out int value)
    {
        value = 0;
        if (headers.TryGetValues(name, out var values))
        {
            var str = values.FirstOrDefault();
            return str is not null && int.TryParse(str, out value);
        }

        return false;
    }
}
