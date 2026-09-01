using System.Text.RegularExpressions;
using DropSort.Application.External;
using DropSort.Domain.Media.Discovery;
using DropSort.Domain.Media.Parser;
using DropSort.FileSystem.Safety;

namespace DropSort.FileSystem.Discovery;

public sealed partial class MediaDiscoveryService : IMediaDiscoveryService
{
    private static readonly HashSet<string> MediaExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".mkv", ".mp4", ".avi", ".mov", ".m4v", ".wmv" };

    public IReadOnlyList<DiscoveredMedia> Discover(
        string rootPath,
        bool recursive,
        Action<int, int, string?>? progress = null,
        Func<bool>? isCancelled = null)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Path.IsPathFullyQualified(rootPath))
            throw new ArgumentException("Discovery root must be absolute.", nameof(rootPath));
        var root = Path.GetFullPath(rootPath);
        PathPolicy.AssertNoReparseComponents(root);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);

        var files = new List<string>();
        var results = new List<DiscoveredMedia>();
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count != 0)
        {
            if (isCancelled?.Invoke() == true) throw new OperationCanceledException();
            var directory = pending.Pop();
            try
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0) continue;
                    if (Directory.Exists(entry))
                    {
                        if (recursive) pending.Push(entry);
                    }
                    else if (MediaExtensions.Contains(Path.GetExtension(entry))) files.Add(entry);
                }
            }
            catch (UnauthorizedAccessException exception)
            {
                results.Add(DiscoveredMedia.Error(
                    directory,
                    new DiscoveryIssue(DiscoveryErrorCode.PermissionDenied, exception.Message)));
            }
            catch (IOException exception)
            {
                results.Add(DiscoveredMedia.Error(
                    directory,
                    new DiscoveryIssue(DiscoveryErrorCode.DirectoryReadFailed, exception.Message)));
            }
        }

        var processed = 0;
        foreach (var file in files.Order(StringComparer.OrdinalIgnoreCase))
        {
            if (isCancelled?.Invoke() == true) throw new OperationCanceledException();
            try
            {
                var parsed = Parse(file);
                var classification = parsed.MediaType switch
                {
                    MediaType.Movie => DiscoveryClassification.MovieCandidate,
                    MediaType.TvEpisode => DiscoveryClassification.TvEpisodeSkipped,
                    _ => DiscoveryClassification.UnknownMedia
                };
                results.Add(new DiscoveredMedia(file, new FileInfo(file).Length, parsed, classification, null));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                results.Add(DiscoveredMedia.Error(
                    file,
                    new DiscoveryIssue(DiscoveryErrorCode.StatFailed, exception.Message)));
            }
            processed++;
            progress?.Invoke(files.Count, processed, Path.GetDirectoryName(file));
        }
        return results;
    }

    private static ParsedMedia Parse(string path)
    {
        var original = Path.GetFileName(path);
        var stem = Path.GetFileNameWithoutExtension(path);
        var isEpisode = EpisodePattern().IsMatch(stem);
        var yearMatch = YearPattern().Match(stem);
        int? year = yearMatch.Success ? int.Parse(yearMatch.Value, System.Globalization.CultureInfo.InvariantCulture) : null;
        var titlePart = yearMatch.Success ? stem[..yearMatch.Index] : stem;
        var title = SeparatorPattern().Replace(titlePart, " ").Trim();
        if (title.Length == 0) title = stem;
        return new ParsedMedia(
            original,
            isEpisode ? MediaType.TvEpisode : MediaType.Movie,
            isEpisode ? null : title,
            isEpisode ? null : year,
            null,
            null,
            null,
            Path.GetExtension(path));
    }

    [GeneratedRegex(@"(?i)\bS\d{1,2}E\d{1,3}\b")]
    private static partial Regex EpisodePattern();

    [GeneratedRegex(@"(?<!\d)(?:19|20)\d{2}(?!\d)")]
    private static partial Regex YearPattern();

    [GeneratedRegex(@"[._\-]+")]
    private static partial Regex SeparatorPattern();
}
