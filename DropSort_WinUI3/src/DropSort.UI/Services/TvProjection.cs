using System.Globalization;
using DropSort.Application.Dto;
using DropSort.Domain.Library.Movies;
using DropSort.Domain.Library.Shows;
using DropSort.UI.Models;

namespace DropSort.UI.Services;

/// <summary>
/// Maps the TV catalog's DTOs onto the records the TV pages already bind to.
/// <para>
/// The pages keep their templates; only the source changes, from the bundled sample to the SQLite
/// hierarchy. Nothing is invented: an episode registered from a file name has no title of its own, so
/// the row falls back to its episode code, and runtime, overview and genres stay empty until a metadata
/// backend exists.
/// </para>
/// </summary>
internal static class TvProjection
{
    /// <summary>
    /// A show as the library grid needs it: identity, title, and the counts the card line reports. The
    /// season list stays empty - a card does not read episodes, and filling it with stand-ins would be
    /// inventing data.
    /// </summary>
    public static TVShowRecord ToCard(TvShowListItem item) => new(
        item.Id,
        item.Title,
        item.Year ?? 0,
        [],
        string.Empty,
        [],
        new ShowCounts(
            item.SeasonCount,
            item.EpisodeCount,
            item.EpisodesWithLocalFile,
            item.MissingEpisodeCount),
        PosterReference: item.PosterReference);

    /// <summary>The full hierarchy of one show: seasons in order, each with its episodes in order.</summary>
    public static TVShowRecord ToDetails(TvShowDetails details) => new(
        details.Id,
        details.Title,
        details.Year ?? 0,
        details.Genres,
        details.Overview ?? string.Empty,
        [
            .. details.Seasons.Select(season => new SeasonRecord(
                season.Id,
                season.Number,
                [.. season.Episodes.Select(episode => ToEpisode(season.Number, episode))])),
        ],
        PosterReference: details.PosterReference,
        ExternalId: details.ExternalId,
        MetadataStatus: details.MetadataStatus);

    private static EpisodeRecord ToEpisode(int seasonNumber, TvEpisodeDetails episode)
    {
        var file = episode.MediaFiles
            .OrderByDescending(candidate => candidate.Status == MediaFileStatus.Present)
            .FirstOrDefault();

        return new EpisodeRecord(
            episode.Id,
            episode.Number,
            string.IsNullOrWhiteSpace(episode.Title)
                ? ShowFormatting.EpisodeCode(seasonNumber, episode.Number)
                : episode.Title,
            episode.RuntimeMinutes is { } minutes
                ? string.Create(CultureInfo.InvariantCulture, $"{minutes} min")
                : string.Empty,
            Watched: false,
            HasLocalFile: episode.Availability == EpisodeAvailability.Present,
            IsFileMissing: episode.Availability == EpisodeAvailability.Missing,
            FilePath: file?.CurrentPath);
    }
}
