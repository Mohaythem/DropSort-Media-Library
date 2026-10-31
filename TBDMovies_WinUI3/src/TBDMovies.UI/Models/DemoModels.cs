using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using TBDMovies.UI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace TBDMovies.UI.Models;

/// <summary>One associated local file shown in the Movie Details media-files list.</summary>
public sealed record MovieFileRecord(string FileName, string FilePath, string Extension, string Availability,
    string Facts = "", string AvailabilityKey = "UnknownMedia");

/// <summary>One configured media root in Settings, with its per-row Replace / Remove actions.</summary>
public sealed record MediaFolderRow(string Path)
{
    public string ReplaceLabel => LocalizationService.Text("ReplaceFolder");
    public string RemoveLabel => LocalizationService.Text("RemoveFolder");
}

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
    int? MediaFileId = null,
    string? PosterReference = null,
    int FileCount = 1,
    IReadOnlyList<MovieFileRecord>? MediaFiles = null,
    bool ReviewMatch = false,
    bool HasPendingMetadata = false,
    DateTimeOffset DateAdded = default,
    DateTimeOffset? LastOpenedAt = null,
    string? BackdropReference = null,
    string? CustomDisplayTitle = null,
    string? CanonicalTitle = null) : INotifyPropertyChanged
{
    public DateTimeOffset? LastOpenedAt { get; private set; } = LastOpenedAt;

    internal void RecordOpened(DateTimeOffset openedAt) => LastOpenedAt = openedAt;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// A subtle "N files" badge shown on the card only when a movie owns more than one local file
    /// (multiple versions/copies). The full per-file detail lives in Movie Details, so the card stays
    /// uncluttered.
    /// </summary>
    public bool HasMultipleFiles => FileCount > 1;

    public Visibility MultiFileBadgeVisibility => FileCount > 1 ? Visibility.Visible : Visibility.Collapsed;

    public string MultiFileBadgeText => LocalizationService.Format("FilesCountFormat", FileCount);

    /// <summary>Every local file associated with this movie, for the Movie Details media-files list.</summary>
    public IReadOnlyList<MovieFileRecord> Files => MediaFiles ?? [];

    public bool HasMultipleFilesListed => (MediaFiles?.Count ?? 0) > 1;

    public Visibility MultiFileListVisibility => (MediaFiles?.Count ?? 0) > 1 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// "Review match" indicator: the movie carries metadata that was auto-picked from a close match, so a
    /// glance is recommended. It is NOT "missing metadata" — the poster/details are present and usable.
    /// </summary>
    public Visibility ReviewMatchBadgeVisibility => ReviewMatch ? Visibility.Visible : Visibility.Collapsed;

    public string ReviewMatchLabel => LocalizationService.Text("ReviewMatch");

    /// <summary>
    /// Metadata grouping rank for the metadata-first Library order: 0 = confirmed metadata, 1 = metadata
    /// present but Review Match recommended, 2 = no usable metadata. Title and Year retain grouping;
    /// Recent and Date Added order by timestamp independently of metadata.
    /// </summary>
    public int MetadataGroupRank => HasPendingMetadata ? 2 : ReviewMatch ? 1 : 0;

    private ImageSource? _posterSource;
    private bool _posterRequested;
    private ArtworkLoader? _horizontalArtwork;

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

    public string YearText => Year > 0 ? LocalizationService.Number(Year) : string.Empty;

    public Visibility YearVisibility => Year > 0 ? Visibility.Visible : Visibility.Collapsed;

    public string HorizontalSummaryLine => FileCount > 1
        ? LocalizationService.Format("FilesCountFormat", FileCount)
        : string.Empty;

    public bool IsLiked => string.Equals(Preference, "liked", StringComparison.OrdinalIgnoreCase);

    public bool IsBlacklisted => string.Equals(Preference, "blacklisted", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Artwork for the card grids and the details hero. Fetching metadata images is a backend
    /// concern that does not exist yet, so this is null today and every surface draws the
    /// placeholder instead. Both occupy the same fixed 2:3 rectangle and the image is drawn with
    /// UniformToFill, so a real poster later changes the pixels inside the frame and nothing else.
    /// </summary>
    public ImageSource? PosterSource
    {
        get
        {
            if (_posterSource != null)
            {
                return _posterSource;
            }

            if (string.IsNullOrWhiteSpace(PosterReference) || !AppServices.IsAvailable)
            {
                return null;
            }

            var cachedPath = AppServices.Poster.GetCachedPosterPath("TMDB", PosterReference);
            if (cachedPath != null && File.Exists(cachedPath))
            {
                _posterSource = new BitmapImage(new Uri(cachedPath));
                return _posterSource;
            }

            if (!_posterRequested)
            {
                _posterRequested = true;
                var posterRef = PosterReference;
                _ = Task.Run(async () =>
                {
                    var downloaded = await AppServices.Poster.EnsurePosterCachedAsync("TMDB", posterRef);
                    if (downloaded != null && File.Exists(downloaded))
                    {
                        AppServices.UiDispatcherQueue?.TryEnqueue(() =>
                        {
                            if (PosterReference == posterRef)
                            {
                                _posterSource = new BitmapImage(new Uri(downloaded));
                                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PosterSource)));
                                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPosterMissing)));
                            }
                        });
                    }
                });
            }

            return null;
        }
    }

    public bool IsPosterMissing => _posterSource is null && (string.IsNullOrWhiteSpace(PosterReference) || !AppServices.IsAvailable || AppServices.Poster.GetCachedPosterPath("TMDB", PosterReference) is null);

    /// <summary>Landscape artwork for horizontal Library cards, with the existing poster as view fallback.</summary>
    public ImageSource? HorizontalArtworkSource => HorizontalArtwork.Source;

    public Visibility HorizontalPosterVisibility => HorizontalArtworkSource is null
        ? Visibility.Visible
        : Visibility.Collapsed;

    private ArtworkLoader HorizontalArtwork => _horizontalArtwork ??= new ArtworkLoader(
        [BackdropReference],
        () =>
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HorizontalArtworkSource)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HorizontalPosterVisibility)));
        });

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

/// <summary>
/// A media item rendered inside one of the My Lists collections. It is a movie or a TV show: system lists
/// only ever hold movies, but a custom list can hold either, so the card exposes one set of
/// poster/title/badge properties backed by whichever record it wraps.
/// </summary>
public sealed record ListMediaItem : INotifyPropertyChanged
{
    public MovieRecord? Movie { get; }
    public TVShowRecord? Show { get; }
    private readonly ShowCardItem? _showCard;

    public bool ShowLocalBadge { get; }
    public bool IsLocal { get; }
    public string LocalBadgeText { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ListMediaItem(
        MovieRecord movie,
        bool showLocalBadge,
        bool isLocal,
        string localBadgeText)
    {
        Movie = movie;
        ShowLocalBadge = showLocalBadge;
        IsLocal = isLocal;
        LocalBadgeText = localBadgeText;
        movie.PropertyChanged += OnSourceChanged;
    }

    public ListMediaItem(
        TVShowRecord show,
        bool showLocalBadge,
        bool isLocal,
        string localBadgeText)
    {
        Show = show;
        _showCard = new ShowCardItem(show, string.Empty);
        ShowLocalBadge = showLocalBadge;
        IsLocal = isLocal;
        LocalBadgeText = localBadgeText;
        _showCard.PropertyChanged += OnSourceChanged;
    }

    private void OnSourceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PosterSource) or nameof(IsPosterMissing))
        {
            PropertyChanged?.Invoke(this, e);
        }
    }

    public bool IsShow => Show is not null;

    public string TypeLabel => LocalizationService.Text(IsShow ? "TvShow" : "HomeMovieType");

    public string Title => Movie?.Title ?? Show?.Title ?? string.Empty;

    public string CanonicalTitle => Movie?.CanonicalTitle ?? Show?.CanonicalTitle ?? Title;

    public int Year => Movie?.Year ?? Show?.Year ?? 0;

    public string YearText => Year > 0 ? LocalizationService.Number(Year) : string.Empty;

    public Visibility YearVisibility => Year > 0 ? Visibility.Visible : Visibility.Collapsed;

    public ImageSource? PosterSource => Movie is not null ? Movie.PosterSource : _showCard?.PosterSource;

    public bool IsPosterMissing => Movie is not null ? Movie.IsPosterMissing : (_showCard?.IsPosterMissing ?? true);

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
/// two folders can share; a metadata row has no file and leaves it null. <paramref name="MovieId" /> is
/// set only on a metadata-review row and identifies the movie whose metadata the Review action opens; a
/// missing-file row leaves it null. The two id kinds are mutually exclusive: a row is either about a
/// missing file (MediaFileId) or about a movie's metadata (MovieId).
/// </summary>
public sealed record LibraryIssueDisplayRecord(
    string Item,
    string Issue,
    string ActionLabel,
    int? MediaFileId = null,
    string? RelinkLabel = null,
    int? MovieId = null)
{
    public bool HasMediaFile => MediaFileId.HasValue;
    public Visibility HasMediaFileVisibility => MediaFileId.HasValue ? Visibility.Visible : Visibility.Collapsed;
    public string EffectiveRelinkLabel => RelinkLabel ?? LocalizationService.Text("Relink");
}

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
