using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DropSort.Application.Dto;
using DropSort.Domain.Media.Discovery;
using DropSort.Domain.Media.Parser;

namespace DropSort.Application.Services;

/// <summary>Contract for TV batch analysis service.</summary>
public interface ITvBatchAnalysisService
{
    TvBatchAnalysisResult Analyze(string rootPath, IReadOnlyList<DiscoveredMedia> items);
    TvBatchFileCandidate ApplyCorrection(TvBatchFileCandidate candidate, string? title = null, int? season = null, int? episode = null);
}

/// <summary>
/// Analyzes discovered media items for TV series batch ingestion without I/O or network operations.
/// Identifies series, season, episode hierarchy, tracks evidence provenance, and detects conflicts.
/// </summary>
public sealed class TvBatchAnalysisService : ITvBatchAnalysisService
{
    private static readonly Regex SeasonFolderPattern = new(
        @"^(?:season|series|s|(?:ال)?موسم)[\s._\-:]*(?<num>\d{1,3})$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex ExplicitSeasonMarkerPattern = new(
        @"(?i)(?:(?<![A-Za-z0-9])S(?<season>\d{1,3})E(?<episode>\d{1,3})(?![A-Za-z0-9])|(?<![\dx])(?<season>\d{1,3})x(?<episode>\d{1,3})(?![\dx]))",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> GenericFolderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "downloads",
        "download",
        "media",
        "season",
        "seasons",
        "specials",
        "special",
        "extras",
        "extra",
        "movies",
        "movie",
        "tv",
        "tv shows",
        "tv series",
        "series",
        "videos",
        "video",
        "incomplete",
        "completed"
    };

    public TvBatchAnalysisResult Analyze(string rootPath, IReadOnlyList<DiscoveredMedia> items)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(items);

        if (!Path.IsPathFullyQualified(rootPath)) throw new ArgumentException("An absolute scan root is required.", nameof(rootPath));
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));

        var allCandidateFiles = new List<TvBatchFileCandidate>();
        var unresolvedFiles = new List<TvBatchFileCandidate>();
        var remainingMovies = new List<DiscoveredMedia>();
        var tvFiles = new List<(TvBatchFileCandidate Candidate, string? ShowFolderPath)>();

        foreach (var item in items)
        {
            if (item.Classification == DiscoveryClassification.Error)
            {
                var errorWarning = new TvBatchWarning(
                    TvBatchWarningCode.DiscoveryError,
                    item.Issue?.Message ?? "Discovery error");
                var errorCandidate = new TvBatchFileCandidate(
                    originalItem: item,
                    currentItem: item,
                    title: null,
                    seasonNumber: null,
                    episodeNumber: null,
                    titleSource: TvEvidenceSource.None,
                    seasonSource: TvEvidenceSource.None,
                    episodeSource: TvEvidenceSource.None,
                    warnings: [errorWarning],
                    isResolved: false,
                    requiresReview: true,
                    canImport: false,
                    relativePath: GetRelativePathSafe(normalizedRoot, item.Path));
                allCandidateFiles.Add(errorCandidate);
                unresolvedFiles.Add(errorCandidate);
                continue;
            }

            if (item.Classification == DiscoveryClassification.TvEpisodeUnsupported)
            {
                var multiWarning = new TvBatchWarning(
                    TvBatchWarningCode.UnsupportedMultiEpisode,
                    "Multi-episode files cannot be imported as a single episode.");
                ExtractFolderEvidence(normalizedRoot, item.Path, out var fTitle, out var fSeason, out _, out var relPath);
                var multiCandidate = new TvBatchFileCandidate(
                    originalItem: item,
                    currentItem: item,
                    title: item.ParsedMedia?.Title ?? fTitle,
                    seasonNumber: item.ParsedMedia?.SeasonNumber ?? fSeason,
                    episodeNumber: item.ParsedMedia?.EpisodeNumber,
                    titleSource: !string.IsNullOrWhiteSpace(item.ParsedMedia?.Title) ? TvEvidenceSource.Filename : (fTitle is not null ? TvEvidenceSource.Folder : TvEvidenceSource.None),
                    seasonSource: item.ParsedMedia?.SeasonNumber is not null ? TvEvidenceSource.Filename : (fSeason is not null ? TvEvidenceSource.Folder : TvEvidenceSource.None),
                    episodeSource: item.ParsedMedia?.EpisodeNumber is not null ? TvEvidenceSource.Filename : TvEvidenceSource.None,
                    warnings: [multiWarning],
                    isResolved: false,
                    requiresReview: true,
                    canImport: false,
                    relativePath: relPath,
                    folderShowTitle: fTitle,
                    folderSeasonNumber: fSeason);
                allCandidateFiles.Add(multiCandidate);
                unresolvedFiles.Add(multiCandidate);
                continue;
            }

            ExtractFolderEvidence(
                normalizedRoot,
                item.Path,
                out var folderShowTitle,
                out var folderSeason,
                out var showFolderPath,
                out var relativePath);

            var isMovieClassification = item.Classification == DiscoveryClassification.MovieCandidate;
            var hasFolderSeason = folderSeason.HasValue;
            var parsed = item.ParsedMedia;
            var hasTvMarker = parsed?.MediaType == MediaType.TvEpisode;

            // An ordinary movie candidate with no credible TV evidence remains a movie.
            if (isMovieClassification && !hasFolderSeason && !hasTvMarker)
            {
                remainingMovies.Add(item);
                continue;
            }

            var stem = Path.GetFileNameWithoutExtension(item.Path);
            var hasExplicitSeason = ExplicitSeasonMarkerPattern.IsMatch(FoldArabicDigits(stem));

            var warnings = new List<TvBatchWarning>();

            // Determine Season and SeasonSource
            int? effectiveSeason;
            TvEvidenceSource seasonSource;

            if (hasExplicitSeason && parsed?.SeasonNumber is not null)
            {
                effectiveSeason = parsed.SeasonNumber.Value;
                seasonSource = TvEvidenceSource.Filename;

                if (folderSeason.HasValue && folderSeason.Value != effectiveSeason.Value)
                {
                    warnings.Add(new TvBatchWarning(
                        TvBatchWarningCode.FolderConflict,
                        $"Filename season {effectiveSeason.Value} contradicts folder season {folderSeason.Value}."));
                }
            }
            else if (folderSeason.HasValue)
            {
                effectiveSeason = folderSeason.Value;
                seasonSource = TvEvidenceSource.Folder;
            }
            else if (parsed?.SeasonNumber is not null)
            {
                effectiveSeason = parsed.SeasonNumber.Value;
                seasonSource = TvEvidenceSource.Default;
            }
            else
            {
                effectiveSeason = null;
                seasonSource = TvEvidenceSource.None;
            }

            // Determine Episode and EpisodeSource
            int? effectiveEpisode = parsed?.EpisodeNumber;
            TvEvidenceSource episodeSource = effectiveEpisode.HasValue
                ? TvEvidenceSource.Filename
                : TvEvidenceSource.None;

            // Determine Title and TitleSource
            string? effectiveTitle;
            TvEvidenceSource titleSource;

            if (!string.IsNullOrWhiteSpace(parsed?.Title))
            {
                effectiveTitle = parsed.Title.Trim();
                titleSource = TvEvidenceSource.Filename;
            }
            else if (!string.IsNullOrWhiteSpace(folderShowTitle))
            {
                effectiveTitle = folderShowTitle.Trim();
                titleSource = TvEvidenceSource.Folder;
            }
            else
            {
                effectiveTitle = null;
                titleSource = TvEvidenceSource.None;
                warnings.Add(new TvBatchWarning(
                    TvBatchWarningCode.MissingTitle,
                    "Show title is missing and could not be determined from filename or folder."));
            }

            if (!effectiveSeason.HasValue || !effectiveEpisode.HasValue)
            {
                if (!warnings.Any(w => w.Code == TvBatchWarningCode.FolderConflict || w.Code == TvBatchWarningCode.MissingTitle))
                {
                    warnings.Add(new TvBatchWarning(
                        TvBatchWarningCode.UnresolvedSeasonOrEpisode,
                        "Season or episode number is unresolved."));
                }
            }

            if (titleSource == TvEvidenceSource.Filename && !string.IsNullOrWhiteSpace(folderShowTitle)
                && NormalizeKey(effectiveTitle!) != NormalizeKey(folderShowTitle))
            {
                warnings.Add(new TvBatchWarning(TvBatchWarningCode.FolderConflict,
                    "Filename and folder name identify different series. Confirm the series identity."));
            }

            var isResolved = !string.IsNullOrWhiteSpace(effectiveTitle)
                && effectiveSeason is >= 0 and <= 999
                && effectiveEpisode is >= 0 and <= 999;

            var requiresReview = !isResolved || warnings.Count > 0;
            var canImport = isResolved && !requiresReview;

            // Regenerate CurrentItem if resolved and missing title was filled or folder season applied
            DiscoveredMedia currentItem = item;
            if (isResolved && (item.Classification == DiscoveryClassification.TvEpisodeSkipped || item.Classification == DiscoveryClassification.TvEpisodeCandidate))
            {
                var newParsed = new ParsedMedia(
                    originalName: parsed?.OriginalName ?? Path.GetFileName(item.Path),
                    mediaType: MediaType.TvEpisode,
                    title: effectiveTitle,
                    year: parsed?.Year,
                    resolution: parsed?.Resolution,
                    source: parsed?.Source,
                    codec: parsed?.Codec,
                    extension: parsed?.Extension ?? Path.GetExtension(item.Path),
                    seasonNumber: effectiveSeason,
                    episodeNumber: effectiveEpisode);

                currentItem = new DiscoveredMedia(
                    item.Path,
                    item.FileSize,
                    newParsed,
                    DiscoveryClassification.TvEpisodeCandidate,
                    null, item.Observation);
            }

            var candidate = new TvBatchFileCandidate(
                originalItem: item,
                currentItem: currentItem,
                title: effectiveTitle,
                seasonNumber: effectiveSeason,
                episodeNumber: effectiveEpisode,
                titleSource: titleSource,
                seasonSource: seasonSource,
                episodeSource: episodeSource,
                warnings: warnings,
                isResolved: isResolved,
                requiresReview: requiresReview,
                canImport: canImport,
                relativePath: relativePath,
                folderShowTitle: folderShowTitle,
                folderSeasonNumber: folderSeason,
                hasFilenameExplicitSeason: hasExplicitSeason);

            allCandidateFiles.Add(candidate);

            if (!isResolved || string.IsNullOrWhiteSpace(effectiveTitle))
            {
                unresolvedFiles.Add(candidate);
            }
            else
            {
                tvFiles.Add((candidate, showFolderPath));
            }
        }

        // Group titled files deterministically with path provenance
        var (seriesList, batchWarnings) = GroupSeriesAndCheckDuplicates(normalizedRoot, tvFiles);
        // Grouping can add conflicts. Return those same reviewed candidates in every projection.
        allCandidateFiles = seriesList.SelectMany(s => s.Files).Concat(unresolvedFiles).ToList();

        // Optional numbered-series suggestions from TvSeriesGrouping for remaining movies
        var suggestions = remainingMovies.Count >= TvSeriesGrouping.MinGroupSize
            ? TvSeriesGrouping.Detect(remainingMovies)
            : Array.Empty<TvSeriesSuggestion>();

        return new TvBatchAnalysisResult(
            RootPath: normalizedRoot,
            Series: seriesList,
            UnresolvedFiles: unresolvedFiles,
            RemainingMovies: remainingMovies,
            NumberedSeriesSuggestions: suggestions,
            AllFiles: allCandidateFiles,
            Warnings: batchWarnings);
    }

    public TvBatchFileCandidate ApplyCorrection(
        TvBatchFileCandidate candidate,
        string? title = null,
        int? season = null,
        int? episode = null)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return candidate.WithCorrection(title, season, episode);
    }

    private static (IReadOnlyList<TvSeriesCandidate> Series, IReadOnlyList<TvBatchWarning> Warnings)
        GroupSeriesAndCheckDuplicates(
            string rootPath,
            List<(TvBatchFileCandidate Candidate, string? ShowFolderPath)> tvFiles)
    {
        var batchWarnings = new List<TvBatchWarning>();

        // Group files by (NormalizedTitle, PathProvenanceKey)
        var groups = tvFiles
            .GroupBy(t => (
                NormalizedTitle: NormalizeKey(t.Candidate.Title!),
                ProvenanceKey: t.ShowFolderPath ?? Path.GetDirectoryName(Path.GetFullPath(t.Candidate.OriginalItem.Path)) ?? rootPath))
            .ToList();

        // Check if the same normalized series title appears in distinct folder paths
        var normalizedTitleGroups = groups
            .GroupBy(g => g.Key.NormalizedTitle)
            .ToList();

        var ambiguousTitleKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var titleGroup in normalizedTitleGroups)
        {
            if (titleGroup.Count() > 1)
            {
                ambiguousTitleKeys.Add(titleGroup.Key);
                var paths = string.Join(", ", titleGroup.Select(g => $"'{g.Key.ProvenanceKey}'"));
                var ambiguousWarning = new TvBatchWarning(
                    TvBatchWarningCode.AmbiguousPathProvenance,
                    $"Same normalized series name '{titleGroup.Key}' found in separate folders: {paths}.");
                batchWarnings.Add(ambiguousWarning);
            }
        }

        var resultSeries = new List<TvSeriesCandidate>();

        foreach (var group in groups.OrderBy(g => g.First().Candidate.Title, StringComparer.OrdinalIgnoreCase).ThenBy(g => g.Key.ProvenanceKey, StringComparer.OrdinalIgnoreCase))
        {
            var displayTitle = group.First().Candidate.Title!;
            var normalizedTitle = group.Key.NormalizedTitle;
            var pathProvenance = group.Key.ProvenanceKey;

            var seriesWarnings = new List<TvBatchWarning>();
            var isAmbiguous = ambiguousTitleKeys.Contains(normalizedTitle);
            if (isAmbiguous)
            {
                seriesWarnings.Add(new TvBatchWarning(
                    TvBatchWarningCode.AmbiguousPathProvenance,
                    $"Series '{displayTitle}' is present in multiple distinct folder paths."));
            }

            var filesInGroup = group.Select(t => t.Candidate).ToList();

            // If ambiguous path provenance, attach warning to files and gate import
            if (isAmbiguous)
            {
                for (var i = 0; i < filesInGroup.Count; i++)
                {
                    var file = filesInGroup[i];
                    var updatedWarnings = new List<TvBatchWarning>(file.Warnings)
                    {
                        new(TvBatchWarningCode.AmbiguousPathProvenance, $"Ambiguous folder location for series '{displayTitle}'.")
                    };
                    filesInGroup[i] = file with
                    {
                        Warnings = updatedWarnings,
                        RequiresReview = true,
                        CanImport = false
                    };
                }
            }

            // Group files into seasons
            var seasonGroups = filesInGroup
                .GroupBy(f => f.SeasonNumber ?? 0)
                .OrderBy(sg => sg.Key);

            var seasonCandidates = new List<TvSeasonCandidate>();

            foreach (var seasonGroup in seasonGroups)
            {
                var seasonNum = seasonGroup.Key;
                var seasonFiles = seasonGroup.ToList();
                var seasonWarnings = new List<TvBatchWarning>();

                // Group files into episodes
                var episodeGroups = seasonFiles
                    .GroupBy(f => f.EpisodeNumber ?? 0)
                    .OrderBy(eg => eg.Key);

                var episodeCandidates = new List<TvEpisodeCandidate>();

                foreach (var episodeGroup in episodeGroups)
                {
                    var episodeNum = episodeGroup.Key;
                    var episodeFiles = episodeGroup.ToList();
                    var episodeWarnings = new List<TvBatchWarning>();

                    var distinctPaths = episodeFiles
                        .Select(f => f.OriginalItem.Path)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (distinctPaths.Count > 1)
                    {
                        var pathList = string.Join(" and ", distinctPaths.Select(p => $"'{p}'"));
                        var dupWarning = new TvBatchWarning(
                            TvBatchWarningCode.DuplicateEpisodePosition,
                            $"Multiple distinct files found for Season {seasonNum}, Episode {episodeNum}: {pathList}.");

                        episodeWarnings.Add(dupWarning);
                        seasonWarnings.Add(dupWarning);
                        seriesWarnings.Add(dupWarning);
                        batchWarnings.Add(dupWarning);

                        // Attach warning to each duplicate file
                        for (var i = 0; i < episodeFiles.Count; i++)
                        {
                            var f = episodeFiles[i];
                            var updatedWarnings = new List<TvBatchWarning>(f.Warnings) { dupWarning };
                            var updatedFile = f with
                            {
                                Warnings = updatedWarnings,
                                RequiresReview = true,
                                CanImport = f.CanImport
                            };
                            episodeFiles[i] = updatedFile;

                            // Update corresponding entry in seasonFiles and filesInGroup
                            var seasonIndex = seasonFiles.FindIndex(x => x.OriginalItem.Path.Equals(f.OriginalItem.Path, StringComparison.OrdinalIgnoreCase));
                            if (seasonIndex >= 0) seasonFiles[seasonIndex] = updatedFile;

                            var groupIndex = filesInGroup.FindIndex(x => x.OriginalItem.Path.Equals(f.OriginalItem.Path, StringComparison.OrdinalIgnoreCase));
                            if (groupIndex >= 0) filesInGroup[groupIndex] = updatedFile;
                        }
                    }

                    episodeCandidates.Add(new TvEpisodeCandidate(
                        SeasonNumber: seasonNum,
                        EpisodeNumber: episodeNum,
                        Files: episodeFiles,
                        Warnings: episodeWarnings));
                }

                seasonCandidates.Add(new TvSeasonCandidate(
                    SeasonNumber: seasonNum,
                    Episodes: episodeCandidates,
                    Files: seasonFiles,
                    Warnings: seasonWarnings));
            }

            resultSeries.Add(new TvSeriesCandidate(
                Title: displayTitle,
                NormalizedTitle: normalizedTitle,
                PathProvenance: pathProvenance,
                Seasons: seasonCandidates,
                Files: filesInGroup,
                Warnings: seriesWarnings));
        }

        return (resultSeries, batchWarnings);
    }

    private static void ExtractFolderEvidence(
        string rootPath,
        string filePath,
        out string? folderShowTitle,
        out int? folderSeason,
        out string? showFolderPath,
        out string relativePath)
    {
        folderShowTitle = null;
        folderSeason = null;
        showFolderPath = null;
        relativePath = GetRelativePathSafe(rootPath, filePath);

        var fileDir = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (string.IsNullOrEmpty(fileDir))
        {
            return;
        }

        var relativeDir = Path.GetRelativePath(rootPath, fileDir);

        // Never traverse above rootPath
        if (relativeDir == ".." || relativeDir.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || Path.IsPathRooted(relativeDir))
        {
            return;
        }

        // The selected root itself is valid title evidence. Do not walk above it.
        var rootName = Path.GetFileName(Path.TrimEndingDirectorySeparator(rootPath));
        var childSegments = relativeDir == "." ? Array.Empty<string>() : relativeDir.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);
        var segments = new[] { rootName }.Concat(childSegments).ToArray();

        if (segments.Length == 0)
        {
            return;
        }

        var lastSegment = segments[^1].Trim();
        if (TryExtractSeasonFolder(lastSegment, out var season))
        {
            folderSeason = season;

            // Search backwards for the closest non-generic folder under rootPath
            for (var i = segments.Length - 2; i >= 0; i--)
            {
                var candidate = segments[i].Trim();
                if (!IsGenericFolder(candidate))
                {
                    folderShowTitle = candidate;
                    showFolderPath = i == 0 ? rootPath : Path.Combine(rootPath, Path.Combine(segments[1..(i + 1)]));
                    break;
                }
            }
        }
        else
        {
            // Immediate parent is not a season folder
            if (!IsGenericFolder(lastSegment))
            {
                folderShowTitle = lastSegment;
                showFolderPath = childSegments.Length == 0 ? rootPath : Path.Combine(rootPath, Path.Combine(childSegments));
            }
        }
    }

    public static bool TryExtractSeasonFolder(string folderName, out int season)
    {
        season = 0;
        if (string.IsNullOrWhiteSpace(folderName))
        {
            return false;
        }

        var folded = FoldArabicDigits(folderName.Trim());

        // Check Specials folder
        if (folded.Equals("specials", StringComparison.OrdinalIgnoreCase) || folded.Equals("special", StringComparison.OrdinalIgnoreCase))
        {
            season = 0;
            return true;
        }

        var match = SeasonFolderPattern.Match(folded);
        if (!match.Success)
        {
            return false;
        }

        var numText = match.Groups["num"].Value;
        if (int.TryParse(numText, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedSeason)
            && parsedSeason is >= 0 and <= 999)
        {
            season = parsedSeason;
            return true;
        }

        return false;
    }

    public static bool IsGenericFolder(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return true;
        }

        var trimmed = name.Trim();
        if (GenericFolderNames.Contains(trimmed))
        {
            return true;
        }

        if (TryExtractSeasonFolder(trimmed, out _))
        {
            return true;
        }

        return false;
    }

    public static string FoldArabicDigits(string value)
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

    private static string GetRelativePathSafe(string rootPath, string filePath)
    {
        try
        {
            var rel = Path.GetRelativePath(rootPath, filePath);
            return rel.StartsWith("..", StringComparison.Ordinal) ? Path.GetFileName(filePath) : rel;
        }
        catch
        {
            return Path.GetFileName(filePath);
        }
    }

    private static string NormalizeKey(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }
}
