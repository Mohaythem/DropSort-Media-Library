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
    IReadOnlyList<WatchHistoryRecord>? WatchHistory = null,
    int? MediaFileId = null)
{
    /// <summary>
    /// Year · runtime · rating, with the parts the catalog does not know yet left out: a movie
    /// registered from a file name has no runtime or rating until metadata enrichment exists, and a
    /// line reading "2024 · · 0.0" would be a fake fact. Digits() keeps the numbers Western when the
    /// line is read inside the right-to-left shell; see LocalizationService.Digits.
    /// </summary>
    public string MetaLine
    {
        get
        {
            var parts = new List<string>(3);

            if (Year > 0)
            {
                parts.Add(Year.ToString(CultureInfo.InvariantCulture));
            }

            if (!string.IsNullOrWhiteSpace(Runtime))
            {
                parts.Add(Runtime);
            }

            if (Rating > 0)
            {
                parts.Add(string.Create(CultureInfo.InvariantCulture, $"{Rating:0.0}"));
            }

            return LocalizationService.Digits(string.Join(" · ", parts));
        }
    }

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

/// <summary>
/// One stored watch event. <paramref name="EventId" /> is the catalog's id: the remove button needs
/// it to call RemoveWatchEvent, so a row without one cannot be removed.
/// </summary>
public sealed record WatchHistoryRecord(string Date, string Label, int? EventId = null);

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

/// <summary>
/// One row in the Check Library issue list. <paramref name="MediaFileId" /> is the registered file the
/// row is about, so the row action resolves the exact record instead of matching on a file name that
/// two folders can share; a metadata row has no file and leaves it null.
/// </summary>
public sealed record LibraryIssueDisplayRecord(string Item, string Issue, string ActionLabel, int? MediaFileId = null);

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
