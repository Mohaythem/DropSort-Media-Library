using System.Globalization;
using DropSort.UI.Services;
using Microsoft.UI.Xaml.Media;

namespace DropSort.UI.Models;

public sealed record EpisodeRecord(
    int Number,
    string Title,
    string Runtime,
    bool Watched,
    bool HasLocalFile);

public sealed record SeasonRecord(int Number, IReadOnlyList<EpisodeRecord> Episodes)
{
    public int WatchedCount => Episodes.Count(episode => episode.Watched);

    public int LocalCount => Episodes.Count(episode => episode.HasLocalFile);
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

public sealed record TVShowRecord(
    int Id,
    string Title,
    int Year,
    IReadOnlyList<string> Genres,
    string Overview,
    IReadOnlyList<SeasonRecord> Seasons)
{
    public int EpisodeCount => Seasons.Sum(season => season.Episodes.Count);

    public int WatchedEpisodeCount => Seasons.Sum(season => season.WatchedCount);

    public string GenreLine => string.Join(" · ", Genres);

    public string MetaLine => LocalizationService.Digits($"{Year} · {GenreLine}");

    public string ProgressLine => LocalizationService.Digits($"{WatchedEpisodeCount} / {EpisodeCount}");

    public EpisodeSelectionRecord? NextPlayableEpisode => Seasons
        .SelectMany(season => season.Episodes.Select(episode => new EpisodeSelectionRecord(
            Id,
            season.Number,
            episode.Number,
            episode)))
        .FirstOrDefault(selection => !selection.Episode.Watched && selection.Episode.HasLocalFile);
}

/// <summary>A TV show as it appears inside a media grid: poster, title and one progress line.</summary>
public sealed record ShowCardItem(TVShowRecord Show, string SecondaryLine)
{
    public string Title => Show.Title;

    /// <summary>Show artwork, null until a backend supplies it. See MovieRecord.PosterSource.</summary>
    public ImageSource? PosterSource => null;

    public bool IsPosterMissing => PosterSource is null;

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
    string ThumbnailLabel,
    string Title,
    string Meta,
    bool Watched,
    bool HasLocalFile,
    EpisodeActionLabels Labels)
{
    public bool IsMissing => !HasLocalFile;
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
