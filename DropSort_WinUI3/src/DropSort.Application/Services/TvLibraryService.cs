using DropSort.Application.Contracts;
using DropSort.Application.Dto;
using DropSort.Application.Repositories;
using DropSort.Domain.Library.Movies;
using DropSort.Domain.Library.Shows;

namespace DropSort.Application.Services;

/// <summary>
/// The read side of the TV catalog.
/// <para>
/// A show's hierarchy is loaded with four reads - the show, its seasons, its episodes and the media
/// files of those episodes - and assembled in memory, so opening a show is not one query per episode.
/// Availability is computed from the registered files alone: no file means NoFile, at least one file
/// present means Present, and every file missing means Missing. Nothing here deletes catalog state
/// because a file disappeared.
/// </para>
/// </summary>
public sealed class TvLibraryService(
    ITvShowRepository shows,
    ITvSeasonRepository seasons,
    ITvEpisodeRepository episodes) : ITvLibraryUiActions
{
    public IReadOnlyList<TvShowListItem> ListShows()
    {
        var result = new List<TvShowListItem>();

        foreach (var show in shows.ListAll())
        {
            var showSeasons = seasons.ListByShow(show.Id);
            var showEpisodes = episodes.ListByShow(show.Id);
            var files = episodes.ListMediaFilesByShow(show.Id);

            var withLocalFile = 0;
            var missing = 0;

            foreach (var episode in showEpisodes)
            {
                switch (Availability(files, episode.Id))
                {
                    case EpisodeAvailability.Present:
                        withLocalFile++;
                        break;
                    case EpisodeAvailability.Missing:
                        missing++;
                        break;
                }
            }

            result.Add(new TvShowListItem(
                show.Id,
                show.Title,
                show.Year,
                show.PosterReference,
                showSeasons.Count,
                showEpisodes.Count,
                withLocalFile,
                missing));
        }

        return result;
    }

    public TvShowDetails GetShowDetails(int showId)
    {
        var show = shows.GetById(showId)
            ?? throw new KeyNotFoundException($"TV show {showId} was not found.");

        var files = episodes.ListMediaFilesByShow(showId);
        var episodesBySeason = episodes.ListByShow(showId)
            .GroupBy(episode => episode.SeasonId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<TvEpisode>)[.. group.OrderBy(episode => episode.Number)]);

        var seasonDetails = new List<TvSeasonDetails>();

        foreach (var season in seasons.ListByShow(showId))
        {
            var seasonEpisodes = episodesBySeason.TryGetValue(season.Id, out var found)
                ? found
                : [];

            seasonDetails.Add(new TvSeasonDetails(
                season.Id,
                season.Number,
                season.Title,
                [
                    .. seasonEpisodes.Select(episode => new TvEpisodeDetails(
                        episode.Id,
                        episode.Number,
                        episode.Title,
                        episode.RuntimeMinutes,
                        Availability(files, episode.Id),
                        files.TryGetValue(episode.Id, out var episodeFiles) ? episodeFiles : [])),
                ]));
        }

        return new TvShowDetails(
            show.Id,
            show.Title,
            show.Data.OriginalTitle,
            show.Year,
            show.Overview,
            show.Genres,
            show.PosterReference,
            seasonDetails,
            show.ExternalId,
            show.MetadataStatus);
    }

    public IReadOnlyList<MediaFile> ListEpisodeFiles(int episodeId) => episodes.ListMediaFiles(episodeId);

    public int? FindEpisodeForMediaFile(int mediaFileId) => episodes.GetEpisodeIdForMediaFile(mediaFileId);

    /// <summary>
    /// An episode is Present when any registered file is on disk, Missing when it has files and none of
    /// them are, and NoFile when nothing has been registered for it yet.
    /// </summary>
    private static EpisodeAvailability Availability(
        IReadOnlyDictionary<int, IReadOnlyList<MediaFile>> files,
        int episodeId)
    {
        if (!files.TryGetValue(episodeId, out var episodeFiles) || episodeFiles.Count == 0)
        {
            return EpisodeAvailability.NoFile;
        }

        return episodeFiles.Any(file => file.Status == MediaFileStatus.Present)
            ? EpisodeAvailability.Present
            : EpisodeAvailability.Missing;
    }
}
