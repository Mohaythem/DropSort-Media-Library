using System;
using System.Linq;
using DropSort.Application.Contracts;
using DropSort.Application.Dto;
using DropSort.Application.Repositories;
using DropSort.Domain.Library.Shows;

namespace DropSort.Application.Services;

/// <summary>
/// Manual catalog repair the automatic classifier does not cover: move a movie's file(s) into an existing
/// TV show as an episode, and remove a catalog item without deleting its file. Each operation runs in one
/// transaction, preserves the media files on disk, and leaves unrelated shows/episodes untouched.
/// </summary>
public sealed class LibraryRepairService(
    ICatalogUnitOfWorkFactory uowFactory,
    Func<DateTimeOffset>? clock = null) : ILibraryRepairUiActions
{
    private readonly ICatalogUnitOfWorkFactory _uowFactory =
        uowFactory ?? throw new ArgumentNullException(nameof(uowFactory));
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);

    public MoveToSeriesResult MoveMovieToSeries(int movieId, int showId, int seasonNumber, int episodeNumber)
    {
        // Reject an out-of-range season/episode up front, so a bad manual entry never reaches the catalog.
        TvSeason.ValidateSeasonNumber(seasonNumber);
        TvEpisode.ValidateEpisodeNumber(episodeNumber);

        using var uow = _uowFactory.Begin();
        _ = uow.Movies.GetById(movieId)
            ?? throw new InvalidOperationException($"Movie {movieId} was not found.");
        _ = uow.TvShows.GetById(showId)
            ?? throw new InvalidOperationException($"TV show {showId} was not found.");

        var now = _clock();
        var fileIds = uow.MediaFiles.GetByMovieId(movieId).Select(file => file.Id).ToList();

        // Reuse the season/episode if the show already has them, so no duplicate rows are created.
        var season = uow.TvSeasons.GetByNumber(showId, seasonNumber)
            ?? uow.TvSeasons.Create(showId, seasonNumber, title: null, overview: null, now);
        var episode = uow.TvEpisodes.GetByNumber(season.Id, episodeNumber)
            ?? uow.TvEpisodes.Create(season.Id, episodeNumber, title: null, overview: null, runtimeMinutes: null, airDate: null, now);

        // Detach the files from the movie, then delete the now-empty movie. media_files.movie_id is
        // ON DELETE SET NULL, so the files are kept (never deleted from disk), then re-linked to the
        // episode. LinkToMovie/LinkMediaFile only re-point an unlinked file, which is why the movie is
        // removed first — the same delete-then-relink pattern the duplicate-movie merge uses.
        uow.Movies.Delete(movieId);
        foreach (var fileId in fileIds)
        {
            // A file already attached to a different episode (a re-run, or a wrong-show relink) is
            // detached first so it moves cleanly to the chosen episode.
            if (uow.TvEpisodes.GetEpisodeIdForMediaFile(fileId) is int owner && owner != episode.Id)
            {
                uow.TvEpisodes.UnlinkMediaFile(fileId);
            }

            uow.TvEpisodes.LinkMediaFile(episode.Id, fileId, now);
        }

        uow.Commit();
        return new MoveToSeriesResult(showId, seasonNumber, episodeNumber);
    }

    public void RemoveMovieFromLibrary(int movieId)
    {
        using var uow = _uowFactory.Begin();
        _ = uow.Movies.GetById(movieId)
            ?? throw new InvalidOperationException($"Movie {movieId} was not found.");

        // Remove the catalog rows only. Deleting a media-file row never touches the file on disk; deleting
        // the movie cascades its personal state and list membership and orphans its watch history.
        foreach (var file in uow.MediaFiles.GetByMovieId(movieId))
        {
            uow.MediaFiles.Delete(file.Id);
        }

        uow.Movies.Delete(movieId);
        uow.Commit();
    }
}
