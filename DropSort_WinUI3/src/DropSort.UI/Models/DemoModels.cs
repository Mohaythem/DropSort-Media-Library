using System.Globalization;
using DropSort.UI.Services;
using Microsoft.UI.Xaml.Media;

namespace DropSort.UI.Models;

public sealed record MovieRecord(
    int Id,
    string Title,
    int Year,
    string Runtime,
    double Rating,
    IReadOnlyList<string> Genres,
    string Overview,
    string? OriginalTitle = null,
    string? Preference = null,
    bool InWatchlist = false,
    bool HasLocalFile = false,
    string? FileName = null,
    string? FilePath = null,
    string? FileFacts = null,
    IReadOnlyList<WatchHistoryRecord>? WatchHistory = null)
{
    // Digits() keeps the year and the rating Western when this line is read inside the
    // right-to-left shell; see LocalizationService.Digits.
    public string MetaLine => LocalizationService.Digits(
        string.Create(CultureInfo.InvariantCulture, $"{Year} · {Runtime} · {Rating:0.0}"));

    public bool IsLiked => string.Equals(Preference, "liked", StringComparison.OrdinalIgnoreCase);

    public bool IsBlacklisted => string.Equals(Preference, "blacklisted", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Artwork for the card grids and the details hero. Fetching metadata images is a backend
    /// concern that does not exist yet, so this is null today and every surface draws the
    /// placeholder instead. Both occupy the same fixed 2:3 rectangle and the image is drawn with
    /// UniformToFill, so a real poster later changes the pixels inside the frame and nothing else.
    /// </summary>
    public ImageSource? PosterSource => null;

    public bool IsPosterMissing => PosterSource is null;

    /// <summary>
    /// A GridView item container takes its accessible name from the item's ToString(), so the
    /// record default would make Narrator read the whole property list of the card.
    /// </summary>
    public override string ToString() => Title;
}

public sealed record WatchHistoryRecord(string Date, string Label);

/// <summary>One watch-history row: the entry plus the localized label of its remove button.</summary>
public sealed record WatchHistoryDisplayRecord(WatchHistoryRecord Entry, string RemoveLabel)
{
    public string Date => Entry.Date;

    public string Label => Entry.Label;
}

/// <summary>A movie rendered inside one of the My Lists collections.</summary>
public sealed record ListMediaItem(
    MovieRecord Movie,
    bool ShowLocalBadge,
    bool IsLocal,
    string LocalBadgeText)
{
    public string Title => Movie.Title;

    public ImageSource? PosterSource => Movie.PosterSource;

    public bool IsPosterMissing => Movie.IsPosterMissing;

    public bool IsRemote => !IsLocal;

    public override string ToString() => Title;
}

/// <summary>
/// A list on the My Lists page. The tab icons live in the page markup next to their tab, so a
/// definition only carries what the page has to look up: the filter key, the label and whether the
/// list is a built-in one (system lists cannot be renamed or deleted).
/// </summary>
public sealed record MediaListDefinition(string Key, string LabelKey, bool IsSystem);

public sealed record DetectedMovieRecord(
    string Title,
    int Year,
    string Quality,
    string Path,
    bool Matched);

public sealed record DetectedMovieDisplayRecord(
    string Title,
    int Year,
    string Quality,
    string FileName,
    string ActionLabel,
    bool IsReady)
{
    public bool ShowReadyGlyph => IsReady;

    public string YearText => LocalizationService.Number(Year);
}

public sealed record DetectedEpisodeRecord(string Code, string Title, string FileName, bool IsReady);

public sealed record DetectedEpisodeDisplayRecord(
    string Code,
    string Title,
    string FileName,
    string StatusLabel,
    bool IsReady)
{
    public bool NeedsReview => !IsReady;
}

public sealed record LibraryIssueRecord(string Item, string Issue);

public sealed record LibraryIssueDisplayRecord(string Item, string Issue, string ActionLabel);

public sealed record OperationLogRecord(
    string Timestamp,
    string Time,
    string Operation,
    string Target,
    string Status,
    string Detail)
{
    public bool IsSuccess => string.Equals(Status, "success", StringComparison.OrdinalIgnoreCase);

    public bool NeedsAttention => !IsSuccess;
}
