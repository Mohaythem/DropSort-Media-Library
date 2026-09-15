using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DropSort.Application.External;
using DropSort.Domain.Metadata.Contracts;

namespace DropSort.Infrastructure.Metadata.Tmdb;

public sealed class TmdbClient : IMetadataProvider, IDisposable
{
    private readonly Func<string?> _tokenProvider;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly string _baseUrl;
    private readonly string _imageBaseUrl;

    public string ProviderName => "TMDB";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_tokenProvider());

    public TmdbClient(
        Func<string?> tokenProvider,
        HttpClient? httpClient = null,
        string baseUrl = "https://api.themoviedb.org/",
        string imageBaseUrl = "https://image.tmdb.org/t/p/")
    {
        _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
        _baseUrl = baseUrl ?? "https://api.themoviedb.org/";
        _imageBaseUrl = imageBaseUrl ?? "https://image.tmdb.org/t/p/";

        if (httpClient == null)
        {
            _httpClient = new HttpClient();
            _ownsHttpClient = true;
        }
        else
        {
            _httpClient = httpClient;
            _ownsHttpClient = false;
        }

        try
        {
            _httpClient.Timeout = TimeSpan.FromSeconds(10);
        }
        catch
        {
            // Ignore if timeout cannot be changed on shared client
        }
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return new ConnectionTestResult(false, "TMDB is not configured.", null, MetadataFailureKind.NotConfigured);
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            using var response = await SendWithRetryAsync(HttpMethod.Get, "3/authentication", cts.Token);

            int statusCode = (int)response.StatusCode;
            if (response.IsSuccessStatusCode)
            {
                return new ConnectionTestResult(true, "Successfully connected to TMDB.", 200);
            }

            return new ConnectionTestResult(false, "TMDB connection failed.", statusCode, FailureKindForStatus(response.StatusCode));
        }
        catch (HttpRequestException)
        {
            return new ConnectionTestResult(false, "Cannot reach TMDB.", null, MetadataFailureKind.Network);
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            return new ConnectionTestResult(false, "TMDB request timed out.", null, MetadataFailureKind.Timeout);
        }
        catch (MetadataServiceException ex)
        {
            return new ConnectionTestResult(false, "TMDB connection failed.", null, ex.Kind);
        }
    }

    public async Task<IReadOnlyList<MovieCandidate>> SearchMoviesAsync(MovieSearchQuery query, CancellationToken cancellationToken = default)
    {
        if (query == null || string.IsNullOrWhiteSpace(query.Title))
        {
            return [];
        }
        if (!IsConfigured) throw new MetadataServiceException(MetadataFailureKind.NotConfigured);

        try
        {
            var path = $"3/search/movie?query={Uri.EscapeDataString(query.Title.Trim())}&include_adult=false&language=en-US";
            if (query.Year.HasValue)
            {
                path += $"&year={query.Year.Value}";
            }

            using var response = await SendWithRetryAsync(HttpMethod.Get, path, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.NotFound) return [];
                ThrowForStatus(response.StatusCode);
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("results", out var resultsElem) || resultsElem.ValueKind != JsonValueKind.Array)
            {
                throw new MetadataServiceException(MetadataFailureKind.InvalidResponse);
            }

            var list = new List<MovieCandidate>();
            foreach (var item in resultsElem.EnumerateArray())
            {
                var id = item.TryGetProperty("id", out var idProp) ? idProp.ToString() : null;
                var title = item.TryGetProperty("title", out var titleProp) ? titleProp.GetString() : null;
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title))
                {
                    continue;
                }

                var origTitle = item.TryGetProperty("original_title", out var origProp) ? origProp.GetString() : null;
                int? year = null;
                if (item.TryGetProperty("release_date", out var relProp) && relProp.ValueKind == JsonValueKind.String)
                {
                    var relDate = relProp.GetString();
                    if (!string.IsNullOrWhiteSpace(relDate) && relDate.Length >= 4 && int.TryParse(relDate[..4], out var y))
                    {
                        year = y;
                    }
                }

                var overview = item.TryGetProperty("overview", out var ovProp) ? ovProp.GetString() : null;
                double? rating = null;
                if (item.TryGetProperty("vote_average", out var voteProp) && voteProp.TryGetDouble(out var d))
                {
                    rating = Math.Round(d, 1);
                }

                var poster = item.TryGetProperty("poster_path", out var pProp) ? pProp.GetString() : null;
                var backdrop = item.TryGetProperty("backdrop_path", out var bProp) ? bProp.GetString() : null;

                list.Add(new MovieCandidate(
                    provider: ProviderName,
                    externalId: id,
                    title: title,
                    originalTitle: origTitle,
                    year: year,
                    overview: overview,
                    rating: rating,
                    posterReference: poster,
                    backdropReference: backdrop));
            }

            return list;
        }
        catch (OperationCanceledException) { if (cancellationToken.IsCancellationRequested) throw; throw new MetadataServiceException(MetadataFailureKind.Timeout); }
        catch (MetadataServiceException) { throw; }
        catch (HttpRequestException)
        {
            throw new MetadataServiceException(MetadataFailureKind.Network);
        }
        catch (JsonException)
        {
            throw new MetadataServiceException(MetadataFailureKind.InvalidResponse);
        }
        catch
        {
            throw new MetadataServiceException(MetadataFailureKind.Api);
        }
    }

    public async Task<MovieMetadata?> GetMovieAsync(string externalId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(externalId))
        {
            return null;
        }
        if (!IsConfigured) throw new MetadataServiceException(MetadataFailureKind.NotConfigured);

        try
        {
            var path = $"3/movie/{Uri.EscapeDataString(externalId.Trim())}?language=en-US";
            using var response = await SendWithRetryAsync(HttpMethod.Get, path, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.NotFound) return null;
                ThrowForStatus(response.StatusCode);
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var id = root.TryGetProperty("id", out var idProp) ? idProp.ToString() : externalId;
            var title = root.TryGetProperty("title", out var titleProp) ? titleProp.GetString() : null;
            if (string.IsNullOrWhiteSpace(title))
            {
                return null;
            }

            var origTitle = root.TryGetProperty("original_title", out var origProp) ? origProp.GetString() : null;
            int? year = null;
            if (root.TryGetProperty("release_date", out var relProp) && relProp.ValueKind == JsonValueKind.String)
            {
                var relDate = relProp.GetString();
                if (!string.IsNullOrWhiteSpace(relDate) && relDate.Length >= 4 && int.TryParse(relDate[..4], out var y))
                {
                    year = y;
                }
            }

            var overview = root.TryGetProperty("overview", out var ovProp) ? ovProp.GetString() : null;

            var genresBuilder = ImmutableArray.CreateBuilder<string>();
            if (root.TryGetProperty("genres", out var genresElem) && genresElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var g in genresElem.EnumerateArray())
                {
                    if (g.TryGetProperty("name", out var gName) && !string.IsNullOrWhiteSpace(gName.GetString()))
                    {
                        genresBuilder.Add(gName.GetString()!.Trim());
                    }
                }
            }

            int? runtime = null;
            if (root.TryGetProperty("runtime", out var rtProp) && rtProp.ValueKind == JsonValueKind.Number && rtProp.TryGetInt32(out var rt) && rt > 0)
            {
                runtime = rt;
            }

            double? rating = null;
            if (root.TryGetProperty("vote_average", out var voteProp) && voteProp.TryGetDouble(out var d))
            {
                rating = Math.Round(d, 1);
            }

            var poster = root.TryGetProperty("poster_path", out var pProp) ? pProp.GetString() : null;
            var backdrop = root.TryGetProperty("backdrop_path", out var bProp) ? bProp.GetString() : null;
            var tagline = root.TryGetProperty("tagline", out var tagProp) ? tagProp.GetString() : null;

            return new MovieMetadata(
                provider: ProviderName,
                externalId: id,
                title: title,
                originalTitle: origTitle,
                year: year,
                overview: overview,
                genres: genresBuilder.ToImmutable(),
                runtimeMinutes: runtime,
                rating: rating,
                posterReference: poster,
                backdropReference: backdrop,
                tagline: string.IsNullOrWhiteSpace(tagline) ? null : tagline);
        }
        catch (OperationCanceledException) { if (cancellationToken.IsCancellationRequested) throw; throw new MetadataServiceException(MetadataFailureKind.Timeout); }
        catch (MetadataServiceException) { throw; }
        catch (HttpRequestException) { throw new MetadataServiceException(MetadataFailureKind.Network); }
        catch (JsonException) { throw new MetadataServiceException(MetadataFailureKind.InvalidResponse); }
        catch
        {
            throw new MetadataServiceException(MetadataFailureKind.Api);
        }
    }

    public async Task<IReadOnlyList<TvCandidate>> SearchTvAsync(TvSearchQuery query, CancellationToken cancellationToken = default)
    {
        if (query == null || string.IsNullOrWhiteSpace(query.Title))
        {
            return [];
        }
        if (!IsConfigured) throw new MetadataServiceException(MetadataFailureKind.NotConfigured);

        try
        {
            var path = $"3/search/tv?query={Uri.EscapeDataString(query.Title.Trim())}&include_adult=false&language=en-US";
            if (query.FirstAirYear.HasValue)
            {
                path += $"&first_air_date_year={query.FirstAirYear.Value}";
            }

            using var response = await SendWithRetryAsync(HttpMethod.Get, path, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.NotFound) return [];
                ThrowForStatus(response.StatusCode);
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("results", out var resultsElem) || resultsElem.ValueKind != JsonValueKind.Array)
            {
                throw new MetadataServiceException(MetadataFailureKind.InvalidResponse);
            }

            var list = new List<TvCandidate>();
            foreach (var item in resultsElem.EnumerateArray())
            {
                var id = item.TryGetProperty("id", out var idProp) ? idProp.ToString() : null;
                var name = item.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var origName = item.TryGetProperty("original_name", out var origProp) ? origProp.GetString() : null;
                int? firstAirYear = null;
                if (item.TryGetProperty("first_air_date", out var airProp) && airProp.ValueKind == JsonValueKind.String)
                {
                    var airDate = airProp.GetString();
                    if (!string.IsNullOrWhiteSpace(airDate) && airDate.Length >= 4 && int.TryParse(airDate[..4], out var y))
                    {
                        firstAirYear = y;
                    }
                }

                var overview = item.TryGetProperty("overview", out var ovProp) ? ovProp.GetString() : null;
                double? rating = null;
                if (item.TryGetProperty("vote_average", out var voteProp) && voteProp.TryGetDouble(out var d))
                {
                    rating = Math.Round(d, 1);
                }

                var poster = item.TryGetProperty("poster_path", out var pProp) ? pProp.GetString() : null;
                var backdrop = item.TryGetProperty("backdrop_path", out var bProp) ? bProp.GetString() : null;

                list.Add(new TvCandidate(
                    Provider: ProviderName,
                    ExternalId: id,
                    Title: name,
                    OriginalTitle: origName,
                    FirstAirYear: firstAirYear,
                    Overview: overview,
                    Rating: rating,
                    PosterReference: poster,
                    BackdropReference: backdrop));
            }

            return list;
        }
        catch (OperationCanceledException) { if (cancellationToken.IsCancellationRequested) throw; throw new MetadataServiceException(MetadataFailureKind.Timeout); }
        catch (MetadataServiceException) { throw; }
        catch (HttpRequestException) { throw new MetadataServiceException(MetadataFailureKind.Network); }
        catch (JsonException) { throw new MetadataServiceException(MetadataFailureKind.InvalidResponse); }
        catch
        {
            throw new MetadataServiceException(MetadataFailureKind.Api);
        }
    }

    public async Task<TvShowMetadata?> GetTvShowAsync(string externalId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(externalId))
        {
            return null;
        }
        if (!IsConfigured) throw new MetadataServiceException(MetadataFailureKind.NotConfigured);

        try
        {
            var path = $"3/tv/{Uri.EscapeDataString(externalId.Trim())}?language=en-US";
            using var response = await SendWithRetryAsync(HttpMethod.Get, path, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.NotFound) return null;
                ThrowForStatus(response.StatusCode);
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var id = root.TryGetProperty("id", out var idProp) ? idProp.ToString() : externalId;
            var name = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            var origName = root.TryGetProperty("original_name", out var origProp) ? origProp.GetString() : null;
            int? firstAirYear = null;
            if (root.TryGetProperty("first_air_date", out var airProp) && airProp.ValueKind == JsonValueKind.String)
            {
                var airDate = airProp.GetString();
                if (!string.IsNullOrWhiteSpace(airDate) && airDate.Length >= 4 && int.TryParse(airDate[..4], out var y))
                {
                    firstAirYear = y;
                }
            }

            var overview = root.TryGetProperty("overview", out var ovProp) ? ovProp.GetString() : null;

            var genresBuilder = ImmutableArray.CreateBuilder<string>();
            if (root.TryGetProperty("genres", out var genresElem) && genresElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var g in genresElem.EnumerateArray())
                {
                    if (g.TryGetProperty("name", out var gName) && !string.IsNullOrWhiteSpace(gName.GetString()))
                    {
                        genresBuilder.Add(gName.GetString()!.Trim());
                    }
                }
            }

            double? rating = null;
            if (root.TryGetProperty("vote_average", out var voteProp) && voteProp.TryGetDouble(out var d))
            {
                rating = Math.Round(d, 1);
            }

            var poster = root.TryGetProperty("poster_path", out var pProp) ? pProp.GetString() : null;
            var backdrop = root.TryGetProperty("backdrop_path", out var bProp) ? bProp.GetString() : null;
            var tagline = root.TryGetProperty("tagline", out var tagProp) ? tagProp.GetString() : null;

            var seasonsBuilder = ImmutableArray.CreateBuilder<TvSeasonMetadata>();
            if (root.TryGetProperty("seasons", out var seasonsElem) && seasonsElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in seasonsElem.EnumerateArray())
                {
                    int seasonNum = s.TryGetProperty("season_number", out var snProp) ? snProp.GetInt32() : 0;
                    var sName = s.TryGetProperty("name", out var snmProp) ? snmProp.GetString() : null;
                    var sOverview = s.TryGetProperty("overview", out var soProp) ? soProp.GetString() : null;
                    var sPoster = s.TryGetProperty("poster_path", out var spProp) ? spProp.GetString() : null;
                    var sAirDate = s.TryGetProperty("air_date", out var saProp) ? saProp.GetString() : null;
                    var sId = s.TryGetProperty("id", out var sidProp) ? sidProp.ToString() : null;

                    seasonsBuilder.Add(new TvSeasonMetadata(
                        SeasonNumber: seasonNum,
                        Title: sName,
                        Overview: sOverview,
                        PosterReference: sPoster,
                        AirDate: sAirDate,
                        Episodes: ImmutableArray<TvEpisodeMetadata>.Empty,
                        ExternalId: sId));
                }
            }

            return new TvShowMetadata(
                Provider: ProviderName,
                ExternalId: id,
                Title: name,
                OriginalTitle: origName,
                FirstAirYear: firstAirYear,
                Overview: overview,
                Genres: genresBuilder.ToImmutable(),
                Rating: rating,
                PosterReference: poster,
                BackdropReference: backdrop,
                Seasons: seasonsBuilder.ToImmutable(),
                Tagline: string.IsNullOrWhiteSpace(tagline) ? null : tagline);
        }
        catch (OperationCanceledException) { if (cancellationToken.IsCancellationRequested) throw; throw new MetadataServiceException(MetadataFailureKind.Timeout); }
        catch (MetadataServiceException) { throw; }
        catch (HttpRequestException) { throw new MetadataServiceException(MetadataFailureKind.Network); }
        catch (JsonException) { throw new MetadataServiceException(MetadataFailureKind.InvalidResponse); }
        catch
        {
            throw new MetadataServiceException(MetadataFailureKind.Api);
        }
    }

    public async Task<TvSeasonMetadata?> GetTvSeasonAsync(string showExternalId, int seasonNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(showExternalId))
        {
            return null;
        }
        if (!IsConfigured) throw new MetadataServiceException(MetadataFailureKind.NotConfigured);

        try
        {
            var path = $"3/tv/{Uri.EscapeDataString(showExternalId.Trim())}/season/{seasonNumber}?language=en-US";
            using var response = await SendWithRetryAsync(HttpMethod.Get, path, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.NotFound) return null;
                ThrowForStatus(response.StatusCode);
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var seasonNum = root.TryGetProperty("season_number", out var snProp) ? snProp.GetInt32() : seasonNumber;
            var sName = root.TryGetProperty("name", out var snmProp) ? snmProp.GetString() : null;
            var sOverview = root.TryGetProperty("overview", out var soProp) ? soProp.GetString() : null;
            var sPoster = root.TryGetProperty("poster_path", out var spProp) ? spProp.GetString() : null;
            var sAirDate = root.TryGetProperty("air_date", out var saProp) ? saProp.GetString() : null;
            var sId = root.TryGetProperty("id", out var sidProp) ? sidProp.ToString() : null;

            var episodesBuilder = ImmutableArray.CreateBuilder<TvEpisodeMetadata>();
            if (root.TryGetProperty("episodes", out var episodesElem) && episodesElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var ep in episodesElem.EnumerateArray())
                {
                    int epNum = ep.TryGetProperty("episode_number", out var epnProp) ? epnProp.GetInt32() : 0;
                    var epTitle = ep.TryGetProperty("name", out var eptProp) ? eptProp.GetString() : null;
                    var epOverview = ep.TryGetProperty("overview", out var epoProp) ? epoProp.GetString() : null;
                    int? runtime = null;
                    if (ep.TryGetProperty("runtime", out var eprProp) && eprProp.ValueKind == JsonValueKind.Number && eprProp.TryGetInt32(out var rtVal) && rtVal > 0)
                    {
                        runtime = rtVal;
                    }
                    var epAirDate = ep.TryGetProperty("air_date", out var epaProp) ? epaProp.GetString() : null;
                    double? epRating = null;
                    if (ep.TryGetProperty("vote_average", out var epvProp) && epvProp.TryGetDouble(out var dVal))
                    {
                        epRating = Math.Round(dVal, 1);
                    }
                    var stillPath = ep.TryGetProperty("still_path", out var epsProp) ? epsProp.GetString() : null;
                    var epId = ep.TryGetProperty("id", out var epidProp) ? epidProp.ToString() : null;

                    episodesBuilder.Add(new TvEpisodeMetadata(
                        EpisodeNumber: epNum,
                        Title: epTitle,
                        Overview: epOverview,
                        RuntimeMinutes: runtime,
                        AirDate: epAirDate,
                        Rating: epRating,
                        StillReference: stillPath,
                        ExternalId: epId));
                }
            }

            return new TvSeasonMetadata(
                SeasonNumber: seasonNum,
                Title: sName,
                Overview: sOverview,
                PosterReference: sPoster,
                AirDate: sAirDate,
                Episodes: episodesBuilder.ToImmutable(),
                ExternalId: sId);
        }
        catch (OperationCanceledException) { if (cancellationToken.IsCancellationRequested) throw; throw new MetadataServiceException(MetadataFailureKind.Timeout); }
        catch (MetadataServiceException) { throw; }
        catch (HttpRequestException) { throw new MetadataServiceException(MetadataFailureKind.Network); }
        catch (JsonException) { throw new MetadataServiceException(MetadataFailureKind.InvalidResponse); }
        catch
        {
            throw new MetadataServiceException(MetadataFailureKind.Api);
        }
    }

    public IReadOnlyList<MovieCandidate> Search(MovieSearchQuery query) =>
        Task.Run(() => SearchMoviesAsync(query)).GetAwaiter().GetResult();

    public MovieMetadata? GetMovie(string externalId) =>
        Task.Run(() => GetMovieAsync(externalId)).GetAwaiter().GetResult();

    private static void ThrowForStatus(HttpStatusCode statusCode)
    {
        var kind = FailureKindForStatus(statusCode);
        throw new MetadataServiceException(kind);
    }

    private static MetadataFailureKind FailureKindForStatus(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => MetadataFailureKind.Authentication,
        HttpStatusCode.TooManyRequests => MetadataFailureKind.RateLimited,
        HttpStatusCode.NotFound => MetadataFailureKind.NotFound,
        _ => MetadataFailureKind.Api
    };

    private async Task<HttpResponseMessage> SendWithRetryAsync(HttpMethod method, string relativePath, CancellationToken cancellationToken)
    {
        var request = CreateRequest(method, relativePath);
        var response = await _httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var delay = TimeSpan.FromSeconds(1);
            if (response.Headers.RetryAfter?.Delta is { } delta)
            {
                delay = delta;
            }
            else if (response.Headers.RetryAfter?.Date is { } date)
            {
                var diff = date - DateTimeOffset.UtcNow;
                delay = diff > TimeSpan.Zero ? diff : TimeSpan.Zero;
            }

            if (delay > TimeSpan.FromSeconds(2))
            {
                delay = TimeSpan.FromSeconds(2);
            }
            else if (delay < TimeSpan.Zero)
            {
                delay = TimeSpan.Zero;
            }

            response.Dispose();
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }

            using var retryRequest = CreateRequest(method, relativePath);
            response = await _httpClient.SendAsync(retryRequest, cancellationToken);
        }

        return response;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativePath)
    {
        var token = _tokenProvider()?.Trim();
        var baseUri = new Uri(_baseUrl.TrimEnd('/') + "/");
        var cleanPath = relativePath.TrimStart('/');

        bool is32Hex = Is32CharHex(token);

        string requestPath;
        if (is32Hex)
        {
            var sep = cleanPath.Contains('?') ? "&" : "?";
            requestPath = $"{cleanPath}{sep}api_key={Uri.EscapeDataString(token!)}";
        }
        else
        {
            requestPath = cleanPath;
        }

        var fullUri = new Uri(baseUri, requestPath);
        var request = new HttpRequestMessage(method, fullUri);

        if (!string.IsNullOrEmpty(token) && !is32Hex)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private static bool Is32CharHex(string? token) =>
        !string.IsNullOrEmpty(token) &&
        token.Length == 32 &&
        token.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'));

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}
