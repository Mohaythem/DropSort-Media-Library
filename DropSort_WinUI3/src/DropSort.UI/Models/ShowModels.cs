using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using DropSort.Domain.Library.Movies;
using DropSort.UI.Services;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace DropSort.UI.Models;

/// <summary>
/// One episode as the UI needs it. <see cref="Id" /> is the catalog's episode id - the identity every
/// action resolves through - and <see cref="FilePath" /> is the registered file behind it, if any.
/// <see cref="IsFileMissing" /> is the case that matters for the product contract: the episode has a
/// registered file that is not on disk, so it stays in the catalog and is reported, never removed.
/// </summary>
public sealed record EpisodeRecord(
    int Id,
    int Number,
    string Title,
    string Runtime,
    bool Watched,
    bool HasLocalFile,
    bool IsFileMissing = false,
    string? FilePath = null);

public sealed record SeasonRecord(int Id, int Number, IReadOnlyList<EpisodeRecord> Episodes)
{
    public int WatchedCount => Episodes.Count(episode => episode.Watched);

    public int LocalCount => Episodes.Count(episode => episode.HasLocalFile);

    public int MissingCount => Episodes.Count(episode => episode.IsFileMissing);
}

/// <summary>
/// Lossless UI identity for an episode. Keeping the show, season and episode numbers together
/// prevents a future action handler from confusing equal episode numbers in different seasons.
/// </summary>
public sealed record EpisodeSelectionRecord(
    int ShowId,
    int SeasonNumber,
    int EpisodeNumber,
    EpisodeRecord Episode);

/// <summary>
/// The counts of a show whose hierarchy has not been loaded. A library card needs the totals but not the
/// episodes themselves, so the catalog answers with these instead of the grid having to read - or invent -
/// a season list to count.
/// </summary>
public sealed record ShowCounts(int Seasons, int Episodes, int Local, int Missing);

public sealed record TVShowRecord(
    int Id,
    string Title,
    int Year,
    IReadOnlyList<string> Genres,
    string Overview,
    IReadOnlyList<SeasonRecord> Seasons,
    ShowCounts? Counts = null,
    string? PosterReference = null,
    string? ExternalId = null,
    MetadataStatus MetadataStatus = MetadataStatus.Pending)
{
    public int SeasonCount => Counts?.Seasons ?? Seasons.Count;

    public int EpisodeCount => Counts?.Episodes ?? Seasons.Sum(season => season.Episodes.Count);

    public int WatchedEpisodeCount => Seasons.Sum(season => season.WatchedCount);

    public int LocalEpisodeCount => Counts?.Local ?? Seasons.Sum(season => season.LocalCount);

    public int MissingEpisodeCount => Counts?.Missing ?? Seasons.Sum(season => season.MissingCount);

    public string GenreLine => string.Join(" · ", Genres);

    /// <summary>Year and genres, with the parts the catalog does not know yet left out.</summary>
    public string MetaLine
    {
        get
        {
            var parts = new List<string>(2);

            if (Year > 0)
            {
                parts.Add(Year.ToString(CultureInfo.InvariantCulture));
            }

            if (Genres.Count > 0)
            {
                parts.Add(GenreLine);
            }

            return LocalizationService.Digits(string.Join(" · ", parts));
        }
    }

    /// <summary>
    /// How much of the show is on disk. There is no episode watch state in this build, so reporting a
    /// watched count would be reporting a zero that means nothing.
    /// </summary>
    public string ProgressLine => LocalizationService.Digits($"{LocalEpisodeCount} / {EpisodeCount}");

    public EpisodeSelectionRecord? NextPlayableEpisode => Seasons
        .SelectMany(season => season.Episodes.Select(episode => new EpisodeSelectionRecord(
            Id,
            season.Number,
            episode.Number,
            episode)))
        .FirstOrDefault(selection => selection.Episode.HasLocalFile);
}

/// <summary>A TV show as it appears inside a media grid: poster, title and one progress line.</summary>
public sealed record ShowCardItem(TVShowRecord Show, string SecondaryLine) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private ImageSource? _posterSource;
    private bool _posterRequested;

    public string Title => Show.Title;

    /// <summary>Show artwork, null until a backend supplies it. See MovieRecord.PosterSource.</summary>
    public ImageSource? PosterSource
    {
        get
        {
            if (_posterSource != null)
            {
                return _posterSource;
            }

            if (string.IsNullOrWhiteSpace(Show.PosterReference) || !AppServices.IsAvailable)
            {
                return null;
            }

            var cachedPath = AppServices.Poster.GetCachedPosterPath("TMDB", Show.PosterReference);
            if (cachedPath != null && File.Exists(cachedPath))
            {
                _posterSource = new BitmapImage(new Uri(cachedPath));
                return _posterSource;
            }

            if (!_posterRequested)
            {
                _posterRequested = true;
                var posterRef = Show.PosterReference;
                _ = Task.Run(async () =>
                {
                    var downloaded = await AppServices.Poster.EnsurePosterCachedAsync("TMDB", posterRef);
                    if (downloaded != null && File.Exists(downloaded))
                    {
                        AppServices.UiDispatcherQueue?.TryEnqueue(() =>
                        {
                            if (Show.PosterReference == posterRef)
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

    public bool IsPosterMissing => _posterSource is null && (string.IsNullOrWhiteSpace(Show.PosterReference) || !AppServices.IsAvailable || AppServices.Poster.GetCachedPosterPath("TMDB", Show.PosterReference) is null);

    public override string ToString() => Title;
}

/// <summary>One season card plus the episode thread it expands.</summary>
public sealed record SeasonDisplayRecord(
    int ShowId,
    int Number,
    string ArtLabel,
    string Title,
    string Meta,
    double ProgressValue,
    bool IsExpanded,
    IReadOnlyList<EpisodeDisplayRecord> Episodes);

public sealed record EpisodeDisplayRecord(
    int ShowId,
    int SeasonNumber,
    int EpisodeNumber,
    int EpisodeId,
    string ThumbnailLabel,
    string Title,
    string Meta,
    bool Watched,
    bool HasLocalFile,
    bool IsFileMissing,
    EpisodeActionLabels Labels)
{
    /// <summary>
    /// The caution pill is for a registered file that is gone from disk. An episode that was never
    /// given a file is not an error, so it shows no pill at all.
    /// </summary>
    public bool IsMissing => IsFileMissing;
}

/// <summary>Localized labels shared by every episode row, so templates never call the service.</summary>
public sealed record EpisodeActionLabels(
    string Watched,
    string Missing,
    string Play,
    string OpenFolder,
    string More);

public sealed record StatCardRecord(string Label, string Value);

public sealed record ContinueWatchingRecord(int ShowId, string ShowTitle, string EpisodeLine);

public static class ShowFormatting
{
    public static string SeasonArtLabel(int number) =>
        "S" + number.ToString("00", CultureInfo.InvariantCulture);

    public static string EpisodeThumbnailLabel(int number) =>
        "E" + number.ToString("00", CultureInfo.InvariantCulture);

    public static string EpisodeCode(int season, int episode) =>
        "S" + season.ToString("00", CultureInfo.InvariantCulture) +
        "E" + episode.ToString("00", CultureInfo.InvariantCulture);
}
