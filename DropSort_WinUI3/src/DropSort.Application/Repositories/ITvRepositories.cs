using System;
using System.Collections.Generic;
using DropSort.Domain.Library.Movies;
using DropSort.Domain.Library.Shows;

namespace DropSort.Application.Repositories;

public interface ITvShowRepository
{
    TvShow? GetById(int id);

    /// <summary>
    /// The show whose normalized title matches, or null. Registration uses this to decide whether a
    /// file belongs to a show that already exists instead of creating a near-duplicate.
    /// </summary>
    TvShow? GetBySortTitle(string sortTitle);

    TvShow? GetByExternalId(string provider, string externalId);

    IReadOnlyList<TvShow> ListAll();

    int CountAll();

    TvShow Create(TvShowCatalogData data, DateTimeOffset now);

    TvShow UpdateMetadata(int id, TvShowCatalogData data, DateTimeOffset now);

    void Delete(int id);
}

public interface ITvSeasonRepository
{
    TvSeason? GetById(int id);

    TvSeason? GetByNumber(int showId, int seasonNumber);

    IReadOnlyList<TvSeason> ListByShow(int showId);

    TvSeason Create(int showId, int seasonNumber, string? title, string? overview, DateTimeOffset now);

    void Delete(int id);
}

public interface ITvEpisodeRepository
{
    TvEpisode? GetById(int id);

    TvEpisode? GetByNumber(int seasonId, int episodeNumber);

    IReadOnlyList<TvEpisode> ListBySeason(int seasonId);

    /// <summary>Every episode of a show, in season then episode order, for one hierarchy read.</summary>
    IReadOnlyList<TvEpisode> ListByShow(int showId);

    TvEpisode Create(
        int seasonId,
        int episodeNumber,
        string? title,
        string? overview,
        int? runtimeMinutes,
        DateTimeOffset? airDate,
        DateTimeOffset now);

    void Delete(int id);

    /// <summary>
    /// Registers an already-stored media file against an episode. A file belongs to at most one
    /// episode; linking one that another episode owns is refused rather than silently re-pointed.
    /// </summary>
    void LinkMediaFile(int episodeId, int mediaFileId, DateTimeOffset now);

    void UnlinkMediaFile(int mediaFileId);

    /// <summary>The episode a media file belongs to, or null when it is not an episode file.</summary>
    int? GetEpisodeIdForMediaFile(int mediaFileId);

    IReadOnlyList<MediaFile> ListMediaFiles(int episodeId);

    /// <summary>
    /// The media files of every episode of one show, keyed by episode id. One query feeds the whole
    /// hierarchy instead of one query per episode.
    /// </summary>
    IReadOnlyDictionary<int, IReadOnlyList<MediaFile>> ListMediaFilesByShow(int showId);
}
