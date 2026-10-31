using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.IO;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TBDMovies.Application.Trakt;

namespace TBDMovies.Infrastructure.Trakt;

public sealed partial class TraktDataClient
{
    public async Task<JsonElement> AddMovieToListAsync(
        long? listId,
        long tmdbId,
        string clientId,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        if (listId is <= 0) throw new ArgumentOutOfRangeException(nameof(listId));
        if (tmdbId <= 0) throw new ArgumentOutOfRangeException(nameof(tmdbId));
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        var relativePath = listId.HasValue
            ? $"users/me/lists/{listId.Value.ToString(CultureInfo.InvariantCulture)}/items"
            : "sync/watchlist";
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri($"{TraktDataConfig.ApiBaseUrl}/{relativePath}"));
        TraktRequestHeaders.IdentifyApplication(request);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("trakt-api-key", clientId);
        request.Headers.Add("trakt-api-version", TraktDataConfig.ApiVersion);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { movies = new[] { new { ids = new { tmdb = tmdbId } } } }),
            Encoding.UTF8,
            "application/json");

        HttpResponseMessage response;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_httpClient.Timeout);
        try
        {
            // Mutations are deliberately sent exactly once. In particular, do not retry 401,
            // 429, 5xx, transport failures, or timeouts because the remote outcome is unknown.
            response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            throw new TraktClientException(TraktFailureKind.Network,
                    "Network error occurred while writing to Trakt.", relativePath);
        }

        using (response)
        {
            if ((int)response.StatusCode is >= 300 and <= 399)
                throw new TraktClientException(TraktFailureKind.HttpError,
                    "HTTP redirect not permitted.", relativePath, response.StatusCode);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new TraktClientException(TraktFailureKind.Unauthorized,
                    "Trakt rejected authentication credentials.", relativePath, response.StatusCode);
            if (response.StatusCode != HttpStatusCode.Created)
                throw new TraktClientException(TraktFailureKind.HttpError,
                    $"Trakt server returned HTTP status {(int)response.StatusCode}.", relativePath, response.StatusCode);

            byte[] bytes;
            try
            {
                bytes = await ReadBoundedStreamAsync(
                    response.Content, _options.MaxResponseBytesPerPage, relativePath, timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TraktClientException(TraktFailureKind.Network, "Trakt write response timed out; outcome is unknown.", relativePath);
            }
            catch (IOException)
            {
                throw new TraktClientException(TraktFailureKind.Network, "Trakt write response could not be read; outcome is unknown.", relativePath);
            }
            try
            {
                using var document = JsonDocument.Parse(bytes);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    throw new TraktClientException(TraktFailureKind.InvalidResponse,
                        "Expected JSON object payload for list write.", relativePath, response.StatusCode);
                return document.RootElement.Clone();
            }
            catch (JsonException)
            {
                throw new TraktClientException(TraktFailureKind.InvalidResponse,
                    "Malformed JSON received for list write.", relativePath, response.StatusCode);
            }
        }
    }
}
