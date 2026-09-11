using System;
using System.Collections.Generic;
using System.Linq;
using DropSort.Application.Repositories;
using DropSort.Domain.Library.Movies;
using DropSort.Domain.Library.Shows;

namespace DropSort.Tests.Application;

/// <summary>
/// In-memory TV repositories for the service tests that must not touch SQLite. They enforce the two
/// rules the schema enforces - one season per (show, number), one episode per (season, number) - and the
/// rule the episode repository enforces: a media file belongs to at most one episode.
/// </summary>
internal sealed class FakeTvShowRepo : ITvShowRepository
{
    private readonly List<TvShow> _shows = [];
    private int _nextId = 1;

    public TvShow? GetById(int id) => _shows.FirstOrDefault(show => show.Id == id);

    public TvShow? GetBySortTitle(string sortTitle) =>
        _shows.FirstOrDefault(show => string.Equals(show.SortTitle, sortTitle, StringComparison.Ordinal));

    public TvShow? GetByExternalId(string provider, string externalId) => _shows.FirstOrDefault(show =>
        show.Data.Provider == provider && show.Data.ExternalId == externalId);

    public IReadOnlyList<TvShow> ListAll() => _shows;

    public int CountAll() => _shows.Count;

    public TvShow Create(TvShowCatalogData data, DateTimeOffset now)
    {
        if (GetBySortTitle(data.SortTitle) is not null)
        {
            throw new InvalidOperationException("A show with that normalized title already exists.");
        }

        var show = new TvShow(_nextId++, data, now, now, now);
        _shows.Add(show);
        return show;
    }

    public TvShow UpdateMetadata(int id, TvShowCatalogData data, DateTimeOffset now)
    {
        var index = _shows.FindIndex(show => show.Id == id);

        if (index < 0)
        {
            throw new KeyNotFoundException($"TV show {id} was not found.");
        }

        var updated = new TvShow(id, data, _shows[index].DateAdded, _shows[index].CreatedAt, now);
        _shows[index] = updated;
        return updated;
    }

    public void Delete(int id) => _shows.RemoveAll(show => show.Id == id);
}

internal sealed class FakeTvSeasonRepo : ITvSeasonRepository
{
    private readonly List<TvSeason> _seasons = [];
    private int _nextId = 1;

    public TvSeason? GetById(int id) => _seasons.FirstOrDefault(season => season.Id == id);

    public TvSeason? GetByNumber(int showId, int seasonNumber) =>
        _seasons.FirstOrDefault(season => season.ShowId == showId && season.Number == seasonNumber);

    public IReadOnlyList<TvSeason> ListByShow(int showId) =>
        [.. _seasons.Where(season => season.ShowId == showId).OrderBy(season => season.Number)];

    public TvSeason Create(int showId, int seasonNumber, string? title, string? overview, DateTimeOffset now)
    {
        if (GetByNumber(showId, seasonNumber) is not null)
        {
            throw new InvalidOperationException("That season already exists for the show.");
        }

        var season = new TvSeason(_nextId++, showId, seasonNumber, title, overview, now, now);
        _seasons.Add(season);
        return season;
    }

    public void Delete(int id) => _seasons.RemoveAll(season => season.Id == id);
}

internal sealed class FakeTvEpisodeRepo : ITvEpisodeRepository
{
    private readonly List<TvEpisode> _episodes = [];
    private readonly Dictionary<int, int> _links = [];
    private int _nextId = 1;

    /// <summary>Set by a test that wants <see cref="ListMediaFiles" /> to answer with real files.</summary>
    public Func<int, IReadOnlyList<MediaFile>>? FilesForEpisode { get; set; }

    /// <summary>The seasons the fake season repository holds, so ListByShow can walk the hierarchy.</summary>
    public FakeTvSeasonRepo? Seasons { get; set; }

    public TvEpisode? GetById(int id) => _episodes.FirstOrDefault(episode => episode.Id == id);

    public TvEpisode? GetByNumber(int seasonId, int episodeNumber) =>
        _episodes.FirstOrDefault(episode => episode.SeasonId == seasonId && episode.Number == episodeNumber);

    public IReadOnlyList<TvEpisode> ListBySeason(int seasonId) =>
        [.. _episodes.Where(episode => episode.SeasonId == seasonId).OrderBy(episode => episode.Number)];

    public IReadOnlyList<TvEpisode> ListByShow(int showId)
    {
        if (Seasons is null)
        {
            return [];
        }

        var seasonIds = Seasons.ListByShow(showId).Select(season => season.Id).ToHashSet();
        return [.. _episodes.Where(episode => seasonIds.Contains(episode.SeasonId)).OrderBy(episode => episode.Number)];
    }

    public TvEpisode Create(
        int seasonId,
        int episodeNumber,
        string? title,
        string? overview,
        int? runtimeMinutes,
        DateTimeOffset? airDate,
        DateTimeOffset now)
    {
        if (GetByNumber(seasonId, episodeNumber) is not null)
        {
            throw new InvalidOperationException("That episode already exists for the season.");
        }

        var episode = new TvEpisode(
            _nextId++, seasonId, episodeNumber, title, overview, runtimeMinutes, airDate, now, now);
        _episodes.Add(episode);
        return episode;
    }

    public void Delete(int id) => _episodes.RemoveAll(episode => episode.Id == id);

    public void LinkMediaFile(int episodeId, int mediaFileId, DateTimeOffset now)
    {
        if (_links.TryGetValue(mediaFileId, out var owner))
        {
            if (owner == episodeId)
            {
                return;
            }

            throw new InvalidOperationException($"Media file {mediaFileId} already belongs to episode {owner}.");
        }

        _links[mediaFileId] = episodeId;
    }

    public void UnlinkMediaFile(int mediaFileId) => _links.Remove(mediaFileId);

    public int? GetEpisodeIdForMediaFile(int mediaFileId) =>
        _links.TryGetValue(mediaFileId, out var episodeId) ? episodeId : null;

    public IReadOnlyList<MediaFile> ListMediaFiles(int episodeId) => FilesForEpisode?.Invoke(episodeId) ?? [];

    public IReadOnlyDictionary<int, IReadOnlyList<MediaFile>> ListMediaFilesByShow(int showId)
    {
        var result = new Dictionary<int, IReadOnlyList<MediaFile>>();

        foreach (var episode in ListByShow(showId))
        {
            var files = ListMediaFiles(episode.Id);

            if (files.Count > 0)
            {
                result[episode.Id] = files;
            }
        }

        return result;
    }
}
