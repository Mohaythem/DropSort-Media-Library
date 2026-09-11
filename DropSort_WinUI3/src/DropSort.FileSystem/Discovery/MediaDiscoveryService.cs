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

                // An episode is a candidate only when the name yielded a show title and both numbers.
                // Anything less stays skipped: the registration path refuses to guess, so discovery
                // must not promise more than it actually read.
                var classification = parsed.MediaType switch
                {
                    MediaType.Movie => DiscoveryClassification.MovieCandidate,
                    MediaType.TvEpisode when parsed.SeasonNumber is not null
                        && parsed.EpisodeNumber is not null
                        && !string.IsNullOrWhiteSpace(parsed.Title)
                        => DiscoveryClassification.TvEpisodeCandidate,
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

        // Two episode forms are recognized, both anchored so they cannot match part of a longer number:
        // S01E02 and 1x02. The show name is whatever precedes the marker; nothing after it is ever read
        // as a title, and no other numbering convention is guessed at.
        var episodeMatch = EpisodePattern().Match(stem);
        var marker = episodeMatch.Success ? episodeMatch : AlternateEpisodePattern().Match(stem);
        var isEpisode = marker.Success;
        var yearMatch = YearPattern().Match(stem);
        int? year = yearMatch.Success ? int.Parse(yearMatch.Value, System.Globalization.CultureInfo.InvariantCulture) : null;
        var titlePart = yearMatch.Success ? stem[..yearMatch.Index] : stem;
        var title = SeparatorPattern().Replace(titlePart, " ").Trim();

        // "Dune Part Two (2024) 1080p" leaves the opening bracket behind once the year is cut off, so
        // the bracket and punctuation characters that only ever wrap a year or a tag are trimmed too.
        title = title.Trim(' ', '(', ')', '[', ']', '{', '}', '-', '_', '.', ',');

        if (title.Length == 0) title = stem;

        // Resolution, source and codec are read from the release tags in the file name. They are what
        // the review list shows as Quality and what the catalog stores as the file's verified facts;
        // a tag that is not present stays null rather than being guessed.
        var resolution = FirstMatch(ResolutionPattern(), stem);
        var source = FirstMatch(SourcePattern(), stem);
        var codec = FirstMatch(CodecPattern(), stem);

        if (!isEpisode)
        {
            return new ParsedMedia(
                original,
                MediaType.Movie,
                title,
                year,
                resolution,
                source,
                codec,
                Path.GetExtension(path));
        }

        // The show name is the part before the marker, cleaned the way a movie title is. An empty result
        // stays null: the file is then an episode the parser could not resolve, not a show named after
        // its own file name.
        var showPart = SeparatorPattern().Replace(stem[..marker.Index], " ").Trim();
        showPart = showPart.Trim(' ', '(', ')', '[', ']', '{', '}', '-', '_', '.', ',');
        var numbers = ReadEpisodeNumbers(marker);

        return new ParsedMedia(
            original,
            MediaType.TvEpisode,
            showPart.Length == 0 ? null : showPart,
            null,
            resolution,
            source,
            codec,
            Path.GetExtension(path),
            numbers?.Season,
            numbers?.Episode);
    }

    /// <summary>
    /// The season and episode the marker claims, or null when either number is outside the range the
    /// catalog accepts (0 through 999). Out of range is a refusal, never a clamp.
    /// </summary>
    private static (int Season, int Episode)? ReadEpisodeNumbers(Match marker)
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        var styles = System.Globalization.NumberStyles.None;

        if (!int.TryParse(marker.Groups["season"].Value, styles, culture, out var season)
            || !int.TryParse(marker.Groups["episode"].Value, styles, culture, out var episode))
        {
            return null;
        }

        return season is < 0 or > 999 || episode is < 0 or > 999 ? null : (season, episode);
    }

    private static string? FirstMatch(Regex pattern, string value)
    {
        var match = pattern.Match(value);
        return match.Success ? match.Value : null;
    }

    [GeneratedRegex(@"(?i)\bS(?<season>\d{1,3})E(?<episode>\d{1,3})\b")]
    private static partial Regex EpisodePattern();

    /// <summary>
    /// The "1x02" form. The x has to sit between digits with no digit or x on either side, so a
    /// resolution like "1920x1080" never matches.
    /// </summary>
    [GeneratedRegex(@"(?i)(?<![\dx])(?<season>\d{1,3})x(?<episode>\d{1,3})(?![\dx])")]
    private static partial Regex AlternateEpisodePattern();

    [GeneratedRegex(@"(?<!\d)(?:19|20)\d{2}(?!\d)")]
    private static partial Regex YearPattern();

    [GeneratedRegex(@"[._\-]+")]
    private static partial Regex SeparatorPattern();

    [GeneratedRegex(@"(?i)\b(?:2160p|1440p|1080p|720p|576p|480p|4K|UHD)\b")]
    private static partial Regex ResolutionPattern();

    [GeneratedRegex(@"(?i)\b(?:BluRay|Blu-Ray|BDRip|BRRip|WEB-DL|WEBRip|WEB|HDTV|DVDRip|DVD|HDRip|CAM|TS)\b")]
    private static partial Regex SourcePattern();

    [GeneratedRegex(@"(?i)\b(?:x265|x264|H\.?265|H\.?264|HEVC|AV1|XviD|DivX)\b")]
    private static partial Regex CodecPattern();
}
