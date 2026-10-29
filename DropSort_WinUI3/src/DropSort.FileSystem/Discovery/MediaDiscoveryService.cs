using System.Text.RegularExpressions;
using DropSort.Application.External;
using DropSort.Domain.Media.Discovery;
using DropSort.Domain.Media.Parser;
using DropSort.FileSystem.Safety;
using DropSort.FileSystem.Inspection;

namespace DropSort.FileSystem.Discovery;

public sealed partial class MediaDiscoveryService : IMediaDiscoveryService
{
    private readonly NetworkReadGuard _networkReads;
    private readonly Func<string, IEnumerable<string>> _enumerateEntries;
    private readonly Func<string, FileAttributes> _getAttributes;
    private static readonly HashSet<string> MediaExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".mkv", ".mp4", ".avi", ".mov", ".m4v", ".wmv" };

    public MediaDiscoveryService(NetworkReadGuard? networkReads = null)
        : this(networkReads, Directory.EnumerateFileSystemEntries, File.GetAttributes) { }

    internal MediaDiscoveryService(NetworkReadGuard? networkReads,
        Func<string, IEnumerable<string>> enumerateEntries,
        Func<string, FileAttributes> getAttributes)
    {
        _networkReads = networkReads ?? NetworkReadGuard.Shared;
        _enumerateEntries = enumerateEntries;
        _getAttributes = getAttributes;
    }

    public IReadOnlyList<DiscoveredMedia> Discover(
        string rootPath,
        bool recursive,
        Action<int, int, string?>? progress = null,
        Func<bool>? isCancelled = null)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Path.IsPathFullyQualified(rootPath))
            throw new ArgumentException("Discovery root must be absolute.", nameof(rootPath));
        var root = Path.GetFullPath(rootPath);
        bool rootReady;
        try
        {
            rootReady = _networkReads.Execute(root, () =>
            {
                PathPolicy.AssertNoReparseComponents(root);
                var attributes = _getAttributes(root);
                if ((attributes & FileAttributes.Directory) == 0) throw new DirectoryNotFoundException(root);
                return true;
            }, () => false, isCancelled);
        }
        catch (Exception error) when (NetworkReadGuard.IsNetworkPath(root)
            && error is IOException or UnauthorizedAccessException)
        {
            return [DiscoveredMedia.Error(root, new DiscoveryIssue(DiscoveryErrorCode.StorageUnavailable, "Storage is unavailable."))];
        }
        if (!rootReady)
            return [DiscoveredMedia.Error(root, new DiscoveryIssue(DiscoveryErrorCode.StorageUnavailable, "Storage is unavailable."))];

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
                var abandoned = 0;
                DirectorySnapshot? snapshot;
                try
                {
                    snapshot = _networkReads.Execute<DirectorySnapshot?>(directory, () => ReadDirectorySnapshot(directory, _enumerateEntries, _getAttributes, () =>
                        Volatile.Read(ref abandoned) != 0 || isCancelled?.Invoke() == true),
                        () => { Volatile.Write(ref abandoned, 1); return null; }, isCancelled);
                }
                catch
                {
                    Volatile.Write(ref abandoned, 1);
                    throw;
                }
                if (snapshot is null)
                {
                    if (NetworkReadGuard.IsNetworkPath(root))
                    {
                        results.Add(DiscoveredMedia.Error(directory, new DiscoveryIssue(DiscoveryErrorCode.StorageUnavailable, "Storage is unavailable.")));
                        return results;
                    }
                    results.Add(DiscoveredMedia.Error(directory, new DiscoveryIssue(DiscoveryErrorCode.DirectoryReadFailed, "Storage is unavailable.")));
                    continue;
                }
                foreach (var issue in snapshot.Issues)
                    results.Add(DiscoveredMedia.Error(issue.Path, new DiscoveryIssue(issue.Code, issue.Message)));
                if (snapshot.FatalIssue is not null)
                {
                    results.Add(DiscoveredMedia.Error(directory, new DiscoveryIssue(
                        NetworkReadGuard.IsNetworkPath(root) ? DiscoveryErrorCode.StorageUnavailable : DiscoveryErrorCode.DirectoryReadFailed,
                        snapshot.FatalIssue.Message)));
                    if (NetworkReadGuard.IsNetworkPath(root)) pending.Clear();
                }
                foreach (var item in snapshot.Items)
                {
                    var entry = item.Path;
                    if ((item.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    if ((item.Attributes & FileAttributes.Directory) != 0)
                    {
                        if (recursive && !(snapshot.FatalIssue is not null && NetworkReadGuard.IsNetworkPath(root))) pending.Push(entry);
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
                if (NetworkReadGuard.IsNetworkPath(root))
                {
                    results.Add(DiscoveredMedia.Error(directory, new DiscoveryIssue(DiscoveryErrorCode.StorageUnavailable, "Storage is unavailable.")));
                    return results;
                }
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
                var observation = _networkReads.Execute<FileObservation?>(file, () =>
                {
                    var attributes = _getAttributes(file);
                    if ((attributes & FileAttributes.Directory) != 0) throw new FileNotFoundException(file);
                    var info = new FileInfo(file);
                    return new FileObservation(info.Length, info.LastWriteTimeUtc);
                }, () => null, isCancelled);
                if (observation is null)
                {
                    if (NetworkReadGuard.IsNetworkPath(root))
                    {
                        results.Add(DiscoveredMedia.Error(file, new DiscoveryIssue(DiscoveryErrorCode.StorageUnavailable, "Storage is unavailable.")));
                        return results;
                    }
                    results.Add(DiscoveredMedia.Error(file, new DiscoveryIssue(DiscoveryErrorCode.StatFailed, "Storage is unavailable.")));
                    processed++;
                    progress?.Invoke(files.Count, processed, Path.GetDirectoryName(file));
                    continue;
                }
                if (isCancelled?.Invoke() == true) throw new OperationCanceledException();
                var parsed = Parse(file);

                // A multi-episode or range marker is TV, but not a single episode the registration path
                // can represent. It must be surfaced for review, never allowed to fall through to a movie
                // (its name has no single SxxExx) or be registered as one arbitrary episode of the set.
                var classification =
                    MultiEpisodePattern().IsMatch(Path.GetFileNameWithoutExtension(file))
                        ? DiscoveryClassification.TvEpisodeUnsupported
                        : parsed.MediaType switch
                        {
                            MediaType.Movie => DiscoveryClassification.MovieCandidate,
                            MediaType.TvEpisode when parsed.SeasonNumber is not null
                                && parsed.EpisodeNumber is not null
                                && !string.IsNullOrWhiteSpace(parsed.Title)
                                => DiscoveryClassification.TvEpisodeCandidate,
                            MediaType.TvEpisode => DiscoveryClassification.TvEpisodeSkipped,
                            _ => DiscoveryClassification.UnknownMedia
                        };
                results.Add(new DiscoveredMedia(file, observation.FileSize, parsed, classification, null, observation));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                if (NetworkReadGuard.IsNetworkPath(root))
                {
                    var rootStillAvailable = _networkReads.Execute(root, () =>
                    {
                        try { return (_getAttributes(Path.GetPathRoot(root)!) & FileAttributes.Directory) != 0; }
                        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
                    }, () => false, isCancelled);
                    if (!rootStillAvailable)
                    {
                        results.Add(DiscoveredMedia.Error(file, new DiscoveryIssue(DiscoveryErrorCode.StorageUnavailable, "Storage is unavailable.")));
                        return results;
                    }
                }
                results.Add(DiscoveredMedia.Error(
                    file,
                    new DiscoveryIssue(DiscoveryErrorCode.StatFailed, exception.Message)));
            }
            processed++;
            progress?.Invoke(files.Count, processed, Path.GetDirectoryName(file));
        }
        return results;
    }

    private static DirectorySnapshot ReadDirectorySnapshot(string directory,
        Func<string, IEnumerable<string>> enumerateEntries,
        Func<string, FileAttributes> getAttributes,
        Func<bool> isAbandoned)
    {
        var entries = new List<(string Path, FileAttributes Attributes)>();
        var issues = new List<DirectoryIssue>();
        // Enumeration failure itself is handled by the caller. A child that disappears while the
        // snapshot is being materialized is isolated so healthy siblings remain discoverable.
        try
        {
            foreach (var entry in enumerateEntries(directory))
            {
                if (isAbandoned()) throw new OperationCanceledException();
                try
                {
                    entries.Add((entry, getAttributes(entry)));
                }
                catch (UnauthorizedAccessException exception)
                {
                    issues.Add(new DirectoryIssue(entry, DiscoveryErrorCode.PermissionDenied, exception.Message));
                }
                catch (IOException exception)
                {
                    issues.Add(new DirectoryIssue(entry, DiscoveryErrorCode.Disappeared, exception.Message));
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new DirectorySnapshot(entries.ToArray(), issues, exception);
        }
        return new DirectorySnapshot(entries.ToArray(), issues, null);
    }

    private sealed record DirectorySnapshot(
        (string Path, FileAttributes Attributes)[] Entries,
        IReadOnlyList<DirectoryIssue> Issues,
        Exception? FatalIssue)
    {
        public IReadOnlyList<(string Path, FileAttributes Attributes)> Items => Entries;
        public IEnumerator<(string Path, FileAttributes Attributes)> GetEnumerator() => ((IEnumerable<(string Path, FileAttributes Attributes)>)Entries).GetEnumerator();
    }

    private sealed record DirectoryIssue(string Path, DiscoveryErrorCode Code, string Message);

    /// <summary>
    /// Best-effort season/episode inference for a known file name, used by manual repair (Add / Move to
    /// series). It reuses the discovery parser — SxxEyy, 1x02, "Episode N", "الحلقة N", Arabic-Indic digits —
    /// and, for a name that is only a base title plus a trailing number ("الرجل العناب 29") that the parser
    /// leaves as a movie, treats it as a flat single-season show (season 1 + that number). Returns null when
    /// nothing can be inferred confidently (a plain or year-only movie name), so the caller asks the user.
    /// Read-only: it never scans or touches disk, and creates no second, divergent parser.
    /// </summary>
    public static (int Season, int Episode)? InferSeasonEpisode(string? fileNameOrPath)
    {
        if (string.IsNullOrWhiteSpace(fileNameOrPath))
        {
            return null;
        }

        var dir = Path.GetDirectoryName(fileNameOrPath);
        int? folderSeason = null;
        if (!string.IsNullOrEmpty(dir))
        {
            var folderName = Path.GetFileName(dir);
            if (DropSort.Application.Services.TvBatchAnalysisService.TryExtractSeasonFolder(folderName, out var s))
            {
                folderSeason = s;
            }
        }

        var stem = Path.GetFileNameWithoutExtension(fileNameOrPath);
        var parsed = Parse(fileNameOrPath);

        var hasExplicitSeason = EpisodePattern().IsMatch(stem) || AlternateEpisodePattern().IsMatch(stem);

        if (parsed.MediaType == MediaType.TvEpisode
            && parsed.EpisodeNumber is int episode)
        {
            // Explicit season from filename marker (e.g. S03E05, 3x05) must take precedence
            // over conflicting season folder. Only fallback to folderSeason if filename carries no explicit season (e.g. Episode 05).
            var season = hasExplicitSeason && parsed.SeasonNumber.HasValue
                ? parsed.SeasonNumber.Value
                : (folderSeason ?? parsed.SeasonNumber ?? 1);
            return (season, episode);
        }

        // Under an explicit season folder, extract episode only if stem is a genuine bare episode number
        // or explicit episode prefix - NEVER for movie sequels, resolution, or ambiguous titles.
        if (folderSeason.HasValue
            && DropSort.Application.Services.TvBatchAnalysisService.TryExtractBareNumericEpisode(stem, out var seasonFolderEp))
        {
            return (folderSeason.Value, seasonFolderEp);
        }

        // For flat standalone filenames, check TvSeriesGrouping trailing episode, but conservatively
        // exclude movie sequels ("Part 2", "Vol 2", "Toy Story 2"), known resolutions (720, 1080),
        // and ambiguous 3-digit numbers ("BB - 105").
        if (DropSort.Application.Services.TvSeriesGrouping.TryDeriveEpisode(
                stem, out var baseTitle, out var trailingEpisode))
        {
            if (IsAmbiguousOrMovieSequel(baseTitle, trailingEpisode, stem))
            {
                return null;
            }

            return (folderSeason ?? 1, trailingEpisode);
        }

        return null;
    }

    private static bool IsAmbiguousOrMovieSequel(string baseTitle, int trailingNumber, string fullStem)
    {
        // 1. Video resolutions are never episodes
        if (trailingNumber is 480 or 576 or 720 or 1080 or 2160)
        {
            return true;
        }

        // If stem contains an explicit episode keyword (Episode, Ep, الحلقة, حلقة), it is not a sequel
        var hasEpisodeKeyword = fullStem.Contains("episode", StringComparison.OrdinalIgnoreCase)
            || fullStem.Contains("ep", StringComparison.OrdinalIgnoreCase)
            || fullStem.Contains("الحلقة", StringComparison.Ordinal)
            || fullStem.Contains("حلقة", StringComparison.Ordinal);

        if (hasEpisodeKeyword)
        {
            return false;
        }

        // 2. Part / Volume sequel markers (e.g. "The Godfather Part 2", "Kill Bill Vol 2")
        var trimmedBase = baseTitle.TrimEnd(' ', '.', '_', '-', ':');
        if (trimmedBase.EndsWith("Part", StringComparison.OrdinalIgnoreCase)
            || trimmedBase.EndsWith("Vol", StringComparison.OrdinalIgnoreCase)
            || trimmedBase.EndsWith("Volume", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 3. 3-digit numbers without episode keyword (e.g. "BB - 105") are ambiguous between S01E05 and anime E105
        if (trailingNumber is >= 100 and <= 999)
        {
            return true;
        }

        // 4. Single-digit numbers (< 10) on multi-character titles without episode keyword are typical movie sequels
        // (e.g. "Toy Story 2", "Avatar 2", "Iron Man 3", "Deadpool 2", "Spider-Man 3")
        if (trailingNumber < 10)
        {
            return true;
        }

        return false;
    }

    private static ParsedMedia Parse(string path)
    {
        var original = Path.GetFileName(path);
        var stem = Path.GetFileNameWithoutExtension(path);

        // Two episode forms are recognized, both anchored so they cannot match part of a longer number:
        // S01E02 and 1x02. The show name is whatever precedes the marker; nothing after it is ever read
        // as a title, and no other numbering convention is guessed at.
        // Episode markers, in priority order. SxxExx and 1x02 carry an explicit season. A word marker —
        // "Episode 3", "Ep.3", or the Arabic "الحلقة ٣" (ASCII or Arabic-Indic digits) — carries only an
        // episode number, so it is read as a flat single-season show (season 1). Season 0 is never invented
        // here; it is reserved for real TMDB Specials.
        var episodeMatch = EpisodePattern().Match(stem);
        var altMatch = AlternateEpisodePattern().Match(stem);
        var wordMatch = WordEpisodePattern().Match(stem);
        var marker = episodeMatch.Success ? episodeMatch
            : altMatch.Success ? altMatch
            : wordMatch.Success ? wordMatch
            : Match.Empty;
        var isEpisode = marker.Success;
        // Episode titles/tags after the marker cannot supply the show's release year.
        var titleStem = isEpisode ? stem[..marker.Index] : stem;
        var yearMatch = FindReleaseYear(titleStem);
        int? year = yearMatch.Success ? int.Parse(yearMatch.Value, System.Globalization.CultureInfo.InvariantCulture) : null;
        var titlePart = yearMatch.Success ? titleStem[..yearMatch.Index] : titleStem;
        var title = CleanReleaseTitle(titlePart);

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
        var showPart = yearMatch.Success ? title : CleanReleaseTitle(titleStem);
        var numbers = episodeMatch.Success || altMatch.Success
            ? ReadEpisodeNumbers(marker)
            : ReadWordEpisodeNumber(marker);

        return new ParsedMedia(
            original,
            MediaType.TvEpisode,
            showPart.Length == 0 ? null : showPart,
            year,
            resolution,
            source,
            codec,
            Path.GetExtension(path),
            numbers?.Season,
            numbers?.Episode);
    }

    private static Match FindReleaseYear(string titleStem)
    {
        Match fallback = Match.Empty;
        foreach (Match candidate in YearPattern().Matches(titleStem))
        {
            // A year-shaped title at the beginning (1917, 1984, 2001...) is not a release year.
            var prefix = CleanReleaseTitle(titleStem[..candidate.Index])
                .Trim(' ', '(', ')', '[', ']', '{', '}', '-', '_', '.', ',');
            if (prefix.Length == 0) continue;
            if (!fallback.Success) fallback = candidate;
            var end = candidate.Index + candidate.Length;
            if (candidate.Index > 0 && end < titleStem.Length
                && ((titleStem[candidate.Index - 1] == '(' && titleStem[end] == ')')
                    || (titleStem[candidate.Index - 1] == '[' && titleStem[end] == ']')))
                return candidate;
        }
        return fallback;
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

    /// <summary>
    /// The episode a word marker ("Episode 3", "Ep.3", "الحلقة ٣") claims. It carries no season, so a flat
    /// single-season show is assumed (season 1) — never season 0, which is reserved for real TMDB Specials.
    /// Arabic-Indic digits are folded to ASCII before parsing; an out-of-range number is a refusal.
    /// </summary>
    private static (int Season, int Episode)? ReadWordEpisodeNumber(Match marker)
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        var styles = System.Globalization.NumberStyles.None;
        var episodeText = FoldDigits(marker.Groups["episode"].Value);

        if (!int.TryParse(episodeText, styles, culture, out var episode) || episode is < 0 or > 999)
        {
            return null;
        }

        return (1, episode);
    }

    /// <summary>Folds Arabic-Indic (٠-٩) and extended Arabic-Indic (۰-۹) digits to their ASCII value.</summary>
    private static string FoldDigits(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var chars = value.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            if (c is >= '٠' and <= '٩')
            {
                chars[i] = (char)('0' + (c - '٠'));
            }
            else if (c is >= '۰' and <= '۹')
            {
                chars[i] = (char)('0' + (c - '۰'));
            }
        }

        return new string(chars);
    }

    private static string? FirstMatch(Regex pattern, string value)
    {
        var match = pattern.Match(value);
        return match.Success ? match.Value : null;
    }

    private static string CleanReleaseTitle(string value)
    {
        var title = LeadingReleaseGroupPattern().Replace(value, " ");
        title = SeparatorPattern().Replace(title, " ");
        // Release groups and source labels are commonly wrapped in brackets. Remove only groups
        // that are clearly release metadata, preserving meaningful parenthesized titles.
        title = MetadataBracketPattern().Replace(title, " ");
        title = LanguageBracketPattern().Replace(title, " ");
        title = StandaloneWebReleasePattern().Replace(title, " ");
        title = ReleaseTagPattern().Replace(title, " ");
        title = title.Replace('[', ' ').Replace(']', ' ');
        title = SeparatorPattern().Replace(title, " ").Trim(' ', '(', ')', '[', ']', '{', '}', '-', '_', '.', ',');
        return Regex.Replace(title, @"\s+", " ").Trim();
    }

    // A single SxxExx marker, allowed to sit against ordinary separators (space, dot, underscore, dash)
    // or a string edge but never against a letter or digit, so "Show_S01E02" resolves and "1920x1080"
    // or a word containing the letters does not. A second episode token immediately after (S01E01E02)
    // fails the trailing guard on purpose: that is a multi-episode file, handled separately.
    [GeneratedRegex(@"(?i)(?<![A-Za-z0-9])S(?<season>\d{1,3})E(?<episode>\d{1,3})(?![A-Za-z0-9])")]
    private static partial Regex EpisodePattern();

    /// <summary>
    /// A multi-episode or episode-range marker the single-episode path cannot represent. It is detected
    /// before single-episode parsing so the file is surfaced for review, never registered as a movie or
    /// as one arbitrary episode of the set. Recognized forms, with dot, underscore, space or dash
    /// separators: repeated episodes after one season (S01E01E02, S01E01-E03), two full markers
    /// (S01E01_S01E02), a numeric range (S01E01-03), and the x-form (1x01x02, 1x01-1x03). The trailing
    /// boundaries stop a resolution tag such as 1x01-1080p from being read as a range.
    /// </summary>
    [GeneratedRegex(@"(?i)(?:S\d{1,3}(?:[.\-_ ]*E\d{1,3}){2,}|S\d{1,3}E\d{1,3}[.\-_ ]*S\d{1,3}E\d{1,3}|S\d{1,3}E\d{1,3}\s*-\s*\d{1,3}(?![\dxp])|(?<![\dx])\d{1,3}x\d{1,3}(?:[x-]\d{1,3})+(?![\dxp]))")]
    private static partial Regex MultiEpisodePattern();

    /// <summary>
    /// The "1x02" form. The x has to sit between digits with no digit or x on either side, so a
    /// resolution like "1920x1080" never matches.
    /// </summary>
    [GeneratedRegex(@"(?i)(?<![\dx])(?<season>\d{1,3})x(?<episode>\d{1,3})(?![\dx])")]
    private static partial Regex AlternateEpisodePattern();

    /// <summary>
    /// A word-form episode marker with no season: English "Episode 3" / "Ep 3" (any separator or none) and
    /// the Arabic "الحلقة ٣" / "حلقة 3" (teh-marbuta or heh spelling). The episode number may be ASCII or
    /// Arabic-Indic digits. Series named this way are flat single-season shows, so the caller assigns
    /// season 1. The trailing guard stops it from reading only part of a longer number.
    /// </summary>
    [GeneratedRegex(@"(?i)(?:\bepisode|\bep|(?:ال)?حلق[ةه])[\s._\-:]*(?<episode>[0-9٠-٩۰-۹]{1,3})(?![0-9٠-٩۰-۹])")]
    private static partial Regex WordEpisodePattern();

    [GeneratedRegex(@"(?<!\d)(?:19|20)\d{2}(?!\d)")]
    private static partial Regex YearPattern();

    [GeneratedRegex(@"[._\-]+")]
    private static partial Regex SeparatorPattern();

    [GeneratedRegex(@"(?i)\[(?:[^\]]*(?:bluray|blu-ray|bdrip|brrip|web[- ]?dl|webrip|hdtv|dvdrip|2160p|1440p|1080p|720p|576p|480p|x26[45]|h\.?26[45]|hevc|av1|xvid|divx)[^\]]*)\]")]
    private static partial Regex MetadataBracketPattern();

    [GeneratedRegex(@"(?i)\[(?:ar|en|eng|ja|jpn|fr|de|ger)\]")]
    private static partial Regex LanguageBracketPattern();

    [GeneratedRegex(@"^\s*\[\s*(?:www\.)?(?:[A-Za-z0-9-]+\.)+(?:com|net|org)\s*\]\s*", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingReleaseGroupPattern();

    [GeneratedRegex(@"(?i)\b(?:2160p|1440p|1080p|720p|576p|480p|4K|UHD|BluRay|Blu-Ray|BDRip|BRRip|WEB-DL|WEBRip|HDTV|DVDRip|HDRip|x265|x264|H\.?265|H\.?264|HEVC|AV1|XviD|DivX)\b.*$")]
    private static partial Regex ReleaseTagPattern();

    [GeneratedRegex(@"(?i)\bWEB(?=\s*(?:[- ]?(?:DL|Rip)\b|(?:2160p|1440p|1080p|720p|576p|480p|4K|UHD|BluRay|Blu-Ray|BDRip|BRRip|HDTV|DVDRip|DVD|HDRip|CAM|TS|x265|x264|H\.?265|H\.?264|HEVC|AV1|XviD|DivX)\b))")]
    private static partial Regex StandaloneWebReleasePattern();

    [GeneratedRegex(@"(?i)\b(?:2160p|1440p|1080p|720p|576p|480p|4K|UHD)\b")]
    private static partial Regex ResolutionPattern();

    [GeneratedRegex(@"(?i)\b(?:BluRay|Blu-Ray|BDRip|BRRip|WEB-DL|WEBRip|WEB|HDTV|DVDRip|DVD|HDRip|CAM|TS)\b")]
    private static partial Regex SourcePattern();

    [GeneratedRegex(@"(?i)\b(?:x265|x264|H\.?265|H\.?264|HEVC|AV1|XviD|DivX)\b")]
    private static partial Regex CodecPattern();
}
