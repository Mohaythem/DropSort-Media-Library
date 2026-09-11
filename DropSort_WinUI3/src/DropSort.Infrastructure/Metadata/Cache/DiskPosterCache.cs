using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DropSort.Application.External;

namespace DropSort.Infrastructure.Metadata.Cache;

public sealed class DiskPosterCache : IPosterService, IPosterCacheMaintenance, IDisposable
{
    private readonly ConcurrentDictionary<string, Task<string?>> _inFlight = new();
    private readonly string _cacheDirectory;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly string _imageBaseUrl;
    private readonly string _defaultPosterSize;

    public DiskPosterCache(
        string cacheDirectory,
        HttpClient? httpClient = null,
        string imageBaseUrl = "https://image.tmdb.org/t/p/",
        string defaultPosterSize = "w342")
    {
        if (string.IsNullOrWhiteSpace(cacheDirectory))
        {
            throw new ArgumentException("cacheDirectory must be provided", nameof(cacheDirectory));
        }

        _cacheDirectory = cacheDirectory;
        _imageBaseUrl = imageBaseUrl ?? "https://image.tmdb.org/t/p/";
        _defaultPosterSize = defaultPosterSize ?? "w342";

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
            _httpClient.Timeout = TimeSpan.FromSeconds(15);
        }
        catch
        {
            // Ignore if timeout cannot be configured on shared client
        }

        Directory.CreateDirectory(_cacheDirectory);
    }

    public string? GetCachedPosterPath(string provider, string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        var path = GetTargetPath(reference);
        if (path != null && File.Exists(path) && new FileInfo(path).Length > 0)
        {
            return path;
        }

        return null;
    }

    public Task<string?> EnsurePosterCachedAsync(string provider, string reference, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return Task.FromResult<string?>(null);
        }

        var cached = GetCachedPosterPath(provider, reference);
        if (cached != null)
        {
            return Task.FromResult<string?>(cached);
        }

        var cacheKey = string.Concat(provider, ":", reference).ToLowerInvariant();
        lock (_inFlight)
        {
            if (_inFlight.TryGetValue(cacheKey, out var existing))
            {
                return existing;
            }

            var task = DownloadPosterInternalAsync(provider, reference, cacheKey, cancellationToken);
            _inFlight[cacheKey] = task;
            return task;
        }
    }

    private async Task<string?> DownloadPosterInternalAsync(string provider, string reference, string cacheKey, CancellationToken cancellationToken)
    {
        try
        {
            var cached = GetCachedPosterPath(provider, reference);
            if (cached != null)
            {
                return cached;
            }

            var targetPath = GetTargetPath(reference);
            if (targetPath == null)
            {
                return null;
            }

            var cleanRef = reference.Trim().TrimStart('/', '\\');
            var url = $"{_imageBaseUrl.TrimEnd('/')}/{_defaultPosterSize.Trim('/')}/{cleanRef}";
            var tempPath = Path.Combine(_cacheDirectory, $".tmp_{Guid.NewGuid():N}.tmp");

            try
            {
                using (var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        return null;
                    }

                    using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken))
                    using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await stream.CopyToAsync(fileStream, cancellationToken);
                    }
                }

                if (IsValidImage(tempPath, out _))
                {
                    File.Move(tempPath, targetPath, overwrite: true);
                    return targetPath;
                }

                TryDeleteFile(tempPath);
                return null;
            }
            catch
            {
                TryDeleteFile(tempPath);
                return null;
            }
        }
        finally
        {
            _inFlight.TryRemove(cacheKey, out _);
        }
    }

    public PosterAsset? LoadPoster(PosterRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Reference))
        {
            return null;
        }

        try
        {
            var localPath = EnsurePosterCachedAsync(request.Provider, request.Reference).GetAwaiter().GetResult();
            if (localPath != null && File.Exists(localPath) && IsValidImage(localPath, out var mimeType))
            {
                var bytes = File.ReadAllBytes(localPath);
                return new PosterAsset(mimeType, bytes);
            }
        }
        catch
        {
            // Safe fallback
        }

        return null;
    }

    public long GetCacheSizeBytes()
    {
        try
        {
            if (!Directory.Exists(_cacheDirectory))
            {
                return 0;
            }

            var dir = new DirectoryInfo(_cacheDirectory);
            return dir.EnumerateFiles("*", SearchOption.TopDirectoryOnly)
                .Where(f => !f.Name.StartsWith('.'))
                .Sum(f => f.Length);
        }
        catch
        {
            return 0;
        }
    }

    public int Clear()
    {
        try
        {
            if (!Directory.Exists(_cacheDirectory))
            {
                return 0;
            }

            var dir = new DirectoryInfo(_cacheDirectory);
            int deleted = 0;
            foreach (var file in dir.EnumerateFiles("*", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    file.Delete();
                    deleted++;
                }
                catch
                {
                    // Continue clearing remaining files
                }
            }

            return deleted;
        }
        catch
        {
            return 0;
        }
    }

    private string? GetTargetPath(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        var cleanRef = reference.Trim().TrimStart('/', '\\');
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(cleanRef.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray());
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return null;
        }

        return Path.Combine(_cacheDirectory, $"tmdb_{sanitized}");
    }

    private static bool IsValidImage(string filePath, out string mimeType)
    {
        mimeType = "image/jpeg";
        try
        {
            var info = new FileInfo(filePath);
            if (!info.Exists || info.Length < 100)
            {
                return false;
            }

            using var stream = File.OpenRead(filePath);
            Span<byte> header = stackalloc byte[12];
            int read = stream.Read(header);
            if (read < 12)
            {
                return false;
            }

            // JPEG: FF D8
            if (header[0] == 0xFF && header[1] == 0xD8)
            {
                mimeType = "image/jpeg";
                return true;
            }

            // PNG: 89 50 4E 47
            if (header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47)
            {
                mimeType = "image/png";
                return true;
            }

            // WebP: RIFF .... WEBP
            if (header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46 &&
                header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50)
            {
                mimeType = "image/webp";
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Ignore failure to delete temp file
        }
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}
