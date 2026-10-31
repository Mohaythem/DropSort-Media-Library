using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TBDMovies.Application.Services;
using TBDMovies.Domain.Media.Discovery;
using TBDMovies.Domain.Media.Parser;

namespace TBDMovies.Application.Dto;

/// <summary>Source provenance for TV metadata evidence.</summary>
public enum TvEvidenceSource
{
    Filename,
    Folder,
    UserCorrection,
    Default,
    None
}

/// <summary>Warning codes for TV batch analysis.</summary>
public enum TvBatchWarningCode
{
    MissingTitle,
    FolderConflict,
    DuplicateEpisodePosition,
    UnsupportedMultiEpisode,
    UnresolvedSeasonOrEpisode,
    AmbiguousPathProvenance,
    DiscoveryError,
    Custom
}

/// <summary>Represents a warning identified during TV batch analysis.</summary>
public sealed record TvBatchWarning(TvBatchWarningCode Code, string Message)
{
    public override string ToString() => Message;
}

/// <summary>Candidate file within TV batch analysis.</summary>
public sealed record TvBatchFileCandidate
{
    public DiscoveredMedia OriginalItem { get; init; }
    public DiscoveredMedia CurrentItem { get; init; }
    public string? Title { get; init; }
    public int? SeasonNumber { get; init; }
    public int? EpisodeNumber { get; init; }
    public TvEvidenceSource TitleSource { get; init; }
    public TvEvidenceSource SeasonSource { get; init; }
    public TvEvidenceSource EpisodeSource { get; init; }
    public IReadOnlyList<TvBatchWarning> Warnings { get; init; }
    public bool IsResolved { get; init; }
    public bool RequiresReview { get; init; }
    public bool CanImport { get; init; }
    public string RelativePath { get; init; }
    public string? FolderShowTitle { get; init; }
    public int? FolderSeasonNumber { get; init; }
    public bool HasFilenameExplicitSeason { get; init; }
    public bool IsUserCorrected { get; init; }

    public TvBatchFileCandidate(
        DiscoveredMedia originalItem,
        DiscoveredMedia currentItem,
        string? title,
        int? seasonNumber,
        int? episodeNumber,
        TvEvidenceSource titleSource,
        TvEvidenceSource seasonSource,
        TvEvidenceSource episodeSource,
        IReadOnlyList<TvBatchWarning> warnings,
        bool isResolved,
        bool requiresReview,
        bool canImport,
        string relativePath,
        string? folderShowTitle = null,
        int? folderSeasonNumber = null,
        bool hasFilenameExplicitSeason = false,
        bool isUserCorrected = false)
    {
        OriginalItem = originalItem ?? throw new ArgumentNullException(nameof(originalItem));
        CurrentItem = currentItem ?? throw new ArgumentNullException(nameof(currentItem));
        Title = title;
        SeasonNumber = seasonNumber;
        EpisodeNumber = episodeNumber;
        TitleSource = titleSource;
        SeasonSource = seasonSource;
        EpisodeSource = episodeSource;
        Warnings = warnings ?? Array.Empty<TvBatchWarning>();
        IsResolved = isResolved;
        RequiresReview = requiresReview;
        CanImport = canImport;
        RelativePath = relativePath ?? string.Empty;
        FolderShowTitle = folderShowTitle;
        FolderSeasonNumber = folderSeasonNumber;
        HasFilenameExplicitSeason = hasFilenameExplicitSeason;
        IsUserCorrected = isUserCorrected;
    }

    /// <summary>Applies a user correction to title, season, or episode and regenerates current media.</summary>
    public TvBatchFileCandidate WithCorrection(string? newTitle = null, int? newSeason = null, int? newEpisode = null)
    {
        var updatedTitle = newTitle is not null ? newTitle.Trim() : Title;
        var updatedSeason = newSeason ?? SeasonNumber;
        var updatedEpisode = newEpisode ?? EpisodeNumber;

        var newTitleSource = newTitle is not null ? TvEvidenceSource.UserCorrection : TitleSource;
        var newSeasonSource = newSeason.HasValue ? TvEvidenceSource.UserCorrection : SeasonSource;
        var newEpisodeSource = newEpisode.HasValue ? TvEvidenceSource.UserCorrection : EpisodeSource;

        // Multi-episode files remain blocked. User correction cannot convert a multi-episode file to a single episode.
        if (OriginalItem.Classification == DiscoveryClassification.TvEpisodeUnsupported)
        {
            var multiWarnings = new List<TvBatchWarning>(Warnings);
            if (!multiWarnings.Any(w => w.Code == TvBatchWarningCode.UnsupportedMultiEpisode))
            {
                multiWarnings.Add(new TvBatchWarning(
                    TvBatchWarningCode.UnsupportedMultiEpisode,
                    "Multi-episode files cannot be imported as a single episode."));
            }

            return this with
            {
                Title = updatedTitle,
                SeasonNumber = updatedSeason,
                EpisodeNumber = updatedEpisode,
                TitleSource = newTitleSource,
                SeasonSource = newSeasonSource,
                EpisodeSource = newEpisodeSource,
                Warnings = multiWarnings,
                IsResolved = false,
                RequiresReview = true,
                CanImport = false,
                IsUserCorrected = true
            };
        }

        // Error items remain errors.
        if (OriginalItem.Classification == DiscoveryClassification.Error)
        {
            return this with
            {
                Title = updatedTitle,
                SeasonNumber = updatedSeason,
                EpisodeNumber = updatedEpisode,
                TitleSource = newTitleSource,
                SeasonSource = newSeasonSource,
                EpisodeSource = newEpisodeSource,
                IsResolved = false,
                RequiresReview = true,
                CanImport = false,
                IsUserCorrected = true
            };
        }

        var isResolved = !string.IsNullOrWhiteSpace(updatedTitle)
            && updatedSeason is >= 0 and <= 999
            && updatedEpisode is >= 0 and <= 999;

        var acknowledgedIdentity = newTitle is not null && newSeason.HasValue && newEpisode.HasValue;
        var remainingWarnings = Warnings.Where(w => w.Code switch
        {
            TvBatchWarningCode.MissingTitle => string.IsNullOrWhiteSpace(updatedTitle),
            TvBatchWarningCode.UnresolvedSeasonOrEpisode => !isResolved,
            TvBatchWarningCode.FolderConflict or TvBatchWarningCode.AmbiguousPathProvenance => !acknowledgedIdentity,
            _ => true
        }).ToList();

        var requiresReview = !isResolved || remainingWarnings.Count > 0;
        var canImport = isResolved && remainingWarnings.All(w => w.Code == TvBatchWarningCode.DuplicateEpisodePosition);

        DiscoveredMedia regeneratedMedia = CurrentItem;
        if (isResolved)
        {
            var originalParsed = OriginalItem.ParsedMedia;
            var newParsed = new ParsedMedia(
                originalName: originalParsed?.OriginalName ?? Path.GetFileName(OriginalItem.Path),
                mediaType: MediaType.TvEpisode,
                title: updatedTitle,
                year: originalParsed?.Year,
                resolution: originalParsed?.Resolution,
                source: originalParsed?.Source,
                codec: originalParsed?.Codec,
                extension: originalParsed?.Extension ?? Path.GetExtension(OriginalItem.Path),
                seasonNumber: updatedSeason,
                episodeNumber: updatedEpisode);

            regeneratedMedia = CreateOrCopyDiscoveredMedia(
                OriginalItem,
                OriginalItem.Path,
                OriginalItem.FileSize,
                newParsed,
                DiscoveryClassification.TvEpisodeCandidate,
                null);
        }

        return this with
        {
            CurrentItem = regeneratedMedia,
            Title = updatedTitle,
            SeasonNumber = updatedSeason,
            EpisodeNumber = updatedEpisode,
            TitleSource = newTitleSource,
            SeasonSource = newSeasonSource,
            EpisodeSource = newEpisodeSource,
            Warnings = remainingWarnings,
            IsResolved = isResolved,
            RequiresReview = requiresReview,
            CanImport = canImport,
            IsUserCorrected = true
        };
    }

    private static DiscoveredMedia CreateOrCopyDiscoveredMedia(
        DiscoveredMedia original,
        string path,
        long? fileSize,
        ParsedMedia? parsedMedia,
        DiscoveryClassification classification,
        DiscoveryIssue? issue)
    {
        var newItem = classification == DiscoveryClassification.Error
            ? DiscoveredMedia.Error(path, issue!)
            : new DiscoveredMedia(path, fileSize, parsedMedia, classification, issue, original.Observation);

        return newItem;
    }
}

/// <summary>Episode candidate with grouped files for a specific season and episode position.</summary>
public sealed record TvEpisodeCandidate(
    int SeasonNumber,
    int EpisodeNumber,
    IReadOnlyList<TvBatchFileCandidate> Files,
    IReadOnlyList<TvBatchWarning> Warnings)
{
    public bool HasDuplicateFiles => Files.Count > 1;
    public TvBatchFileCandidate? PrimaryFile => Files.FirstOrDefault(f => f.CanImport) ?? Files.FirstOrDefault();
}

/// <summary>Season candidate containing episodes and files.</summary>
public sealed record TvSeasonCandidate(
    int SeasonNumber,
    IReadOnlyList<TvEpisodeCandidate> Episodes,
    IReadOnlyList<TvBatchFileCandidate> Files,
    IReadOnlyList<TvBatchWarning> Warnings);

/// <summary>Series candidate containing seasons, files, and path provenance.</summary>
public sealed record TvSeriesCandidate(
    string Title,
    string NormalizedTitle,
    string? PathProvenance,
    IReadOnlyList<TvSeasonCandidate> Seasons,
    IReadOnlyList<TvBatchFileCandidate> Files,
    IReadOnlyList<TvBatchWarning> Warnings)
{
    public bool RequiresReview => Warnings.Count > 0 || Files.Any(f => f.RequiresReview);
    public bool CanImport => Files.Any(f => f.CanImport);
}

/// <summary>Result of TV batch analysis.</summary>
public sealed record TvBatchAnalysisResult(
    string RootPath,
    IReadOnlyList<TvSeriesCandidate> Series,
    IReadOnlyList<TvBatchFileCandidate> UnresolvedFiles,
    IReadOnlyList<DiscoveredMedia> RemainingMovies,
    IReadOnlyList<TvSeriesSuggestion> NumberedSeriesSuggestions,
    IReadOnlyList<TvBatchFileCandidate> AllFiles,
    IReadOnlyList<TvBatchWarning> Warnings)
{
    public int TotalFileCount => AllFiles.Count + RemainingMovies.Count;
    public int ResolvedCount => AllFiles.Count(f => f.IsResolved);
    public int ReviewRequiredCount => AllFiles.Count(f => f.RequiresReview);
    public int CanImportCount => AllFiles.Count(f => f.CanImport);
}
