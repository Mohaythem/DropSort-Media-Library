using System;
using System.Collections.Generic;
using DropSort.Application.Repositories;
using DropSort.Domain.Library.Movies;

namespace DropSort.Tests.Application;

/// <summary>
/// The shape the shell's catalog adapters use: one short-lived unit of work per call, reads rolled
/// back on dispose and writes committed. The shell's own adapters are internal to the WinUI assembly -
/// which cannot be referenced from a test project - so these mirror them, and
/// <c>UiSourceContractTests</c> asserts the shell's copies keep the same rule.
/// </summary>
internal sealed class PerCallMovieRepository(ICatalogUnitOfWorkFactory factory) : IMovieRepository
{
    private T Read<T>(Func<IMovieRepository, T> read)
    {
        using var work = factory.Begin();
        return read(work.Movies);
    }

    private T Write<T>(Func<IMovieRepository, T> write)
    {
        using var work = factory.Begin();
        var result = write(work.Movies);
        work.Commit();
        return result;
    }

    public Movie? GetById(int id) => Read(movies => movies.GetById(id));

    public Movie? GetByExternalId(string provider, string externalId) =>
        Read(movies => movies.GetByExternalId(provider, externalId));

    public IReadOnlyList<Movie> ListAll() => Read(movies => movies.ListAll());

    public IReadOnlyList<Movie> ListPage(int afterId, int limit) => Read(movies => movies.ListPage(afterId, limit));

    public int CountAll() => Read(movies => movies.CountAll());

    public Movie Create(MovieCatalogData data, DateTimeOffset now) => Write(movies => movies.Create(data, now));

    public Movie UpdateMetadata(int id, MovieCatalogData data, DateTimeOffset now) =>
        Write(movies => movies.UpdateMetadata(id, data, now));

    public void Delete(int id) => Write<object?>(movies =>
    {
        movies.Delete(id);
        return null;
    });
}

/// <summary>Media-file half of <see cref="PerCallMovieRepository" />.</summary>
internal sealed class PerCallMediaFileRepository(ICatalogUnitOfWorkFactory factory) : IMediaFileRepository
{
    private T Read<T>(Func<IMediaFileRepository, T> read)
    {
        using var work = factory.Begin();
        return read(work.MediaFiles);
    }

    private T Write<T>(Func<IMediaFileRepository, T> write)
    {
        using var work = factory.Begin();
        var result = write(work.MediaFiles);
        work.Commit();
        return result;
    }

    public MediaFile? GetById(int id) => Read(files => files.GetById(id));

    public MediaFile? GetByPath(string path) => Read(files => files.GetByPath(path));

    public IReadOnlyList<MediaFile> GetByMovieId(int movieId) => Read(files => files.GetByMovieId(movieId));

    public IReadOnlyList<MediaFile> ListAll() => Read(files => files.ListAll());

    public IReadOnlyList<MediaFile> ListMissing() => Read(files => files.ListMissing());

    public MediaFile Add(VerifiedMediaFileFacts facts, int? movieId = null) => Write(files => files.Add(facts, movieId));

    public MediaFile LinkToMovie(int id, int movieId) => Write(files => files.LinkToMovie(id, movieId));

    public MediaFile RefreshVerifiedFacts(int id, VerifiedMediaFileFacts facts) =>
        Write(files => files.RefreshVerifiedFacts(id, facts));

    public MediaFile MarkMissing(int id, DateTimeOffset observedAt) => Write(files => files.MarkMissing(id, observedAt));

    public MediaFile Relink(int id, string expectedPath, VerifiedMediaFileFacts facts) =>
        Write(files => files.Relink(id, expectedPath, facts));

    public void Delete(int id) => Write<object?>(files =>
    {
        files.Delete(id);
        return null;
    });
}

/// <summary>TV show half of the per-call adapters; mirrors the shell's own.</summary>
internal sealed class PerCallTvShowRepository(ICatalogUnitOfWorkFactory factory)
    : DropSort.Application.Repositories.ITvShowRepository
{
    private T Read<T>(Func<DropSort.Application.Repositories.ITvShowRepository, T> read)
    {
        using var work = factory.Begin();
        return read(work.TvShows);
    }

    private T Write<T>(Func<DropSort.Application.Repositories.ITvShowRepository, T> write)
    {
        using var work = factory.Begin();
        var result = write(work.TvShows);
        work.Commit();
        return result;
    }

    public DropSort.Domain.Library.Shows.TvShow? GetById(int id) => Read(shows => shows.GetById(id));

    public DropSort.Domain.Library.Shows.TvShow? GetBySortTitle(string sortTitle) =>
        Read(shows => shows.GetBySortTitle(sortTitle));

    public DropSort.Domain.Library.Shows.TvShow? GetByExternalId(string provider, string externalId) =>
        Read(shows => shows.GetByExternalId(provider, externalId));

    public IReadOnlyList<DropSort.Domain.Library.Shows.TvShow> ListAll() => Read(shows => shows.ListAll());

    public int CountAll() => Read(shows => shows.CountAll());

    public DropSort.Domain.Library.Shows.TvShow Create(
        DropSort.Domain.Library.Shows.TvShowCatalogData data, DateTimeOffset now) =>
        Write(shows => shows.Create(data, now));

    public DropSort.Domain.Library.Shows.TvShow UpdateMetadata(
        int id, DropSort.Domain.Library.Shows.TvShowCatalogData data, DateTimeOffset now) =>
        Write(shows => shows.UpdateMetadata(id, data, now));

    public void Delete(int id) => Write<object?>(shows =>
    {
        shows.Delete(id);
        return null;
    });
}

/// <summary>Season half of the per-call adapters.</summary>
internal sealed class PerCallTvSeasonRepository(ICatalogUnitOfWorkFactory factory)
    : DropSort.Application.Repositories.ITvSeasonRepository
{
    private T Read<T>(Func<DropSort.Application.Repositories.ITvSeasonRepository, T> read)
    {
        using var work = factory.Begin();
        return read(work.TvSeasons);
    }

    public DropSort.Domain.Library.Shows.TvSeason? GetById(int id) => Read(seasons => seasons.GetById(id));

    public DropSort.Domain.Library.Shows.TvSeason? GetByNumber(int showId, int seasonNumber) =>
        Read(seasons => seasons.GetByNumber(showId, seasonNumber));

    public IReadOnlyList<DropSort.Domain.Library.Shows.TvSeason> ListByShow(int showId) =>
        Read(seasons => seasons.ListByShow(showId));

    public DropSort.Domain.Library.Shows.TvSeason Create(
        int showId, int seasonNumber, string? title, string? overview, DateTimeOffset now)
    {
        using var work = factory.Begin();
        var season = work.TvSeasons.Create(showId, seasonNumber, title, overview, now);
        work.Commit();
        return season;
    }

    public void Delete(int id)
    {
        using var work = factory.Begin();
        work.TvSeasons.Delete(id);
        work.Commit();
    }
}

/// <summary>Episode half of the per-call adapters, including the media-file links.</summary>
internal sealed class PerCallTvEpisodeRepository(ICatalogUnitOfWorkFactory factory)
    : DropSort.Application.Repositories.ITvEpisodeRepository
{
    private T Read<T>(Func<DropSort.Application.Repositories.ITvEpisodeRepository, T> read)
    {
        using var work = factory.Begin();
        return read(work.TvEpisodes);
    }

    public DropSort.Domain.Library.Shows.TvEpisode? GetById(int id) => Read(episodes => episodes.GetById(id));

    public DropSort.Domain.Library.Shows.TvEpisode? GetByNumber(int seasonId, int episodeNumber) =>
        Read(episodes => episodes.GetByNumber(seasonId, episodeNumber));

    public IReadOnlyList<DropSort.Domain.Library.Shows.TvEpisode> ListBySeason(int seasonId) =>
        Read(episodes => episodes.ListBySeason(seasonId));

    public IReadOnlyList<DropSort.Domain.Library.Shows.TvEpisode> ListByShow(int showId) =>
        Read(episodes => episodes.ListByShow(showId));

    public DropSort.Domain.Library.Shows.TvEpisode Create(
        int seasonId,
        int episodeNumber,
        string? title,
        string? overview,
        int? runtimeMinutes,
        DateTimeOffset? airDate,
        DateTimeOffset now)
    {
        using var work = factory.Begin();
        var episode = work.TvEpisodes.Create(seasonId, episodeNumber, title, overview, runtimeMinutes, airDate, now);
        work.Commit();
        return episode;
    }

    public void Delete(int id)
    {
        using var work = factory.Begin();
        work.TvEpisodes.Delete(id);
        work.Commit();
    }

    public void LinkMediaFile(int episodeId, int mediaFileId, DateTimeOffset now)
    {
        using var work = factory.Begin();
        work.TvEpisodes.LinkMediaFile(episodeId, mediaFileId, now);
        work.Commit();
    }

    public void UnlinkMediaFile(int mediaFileId)
    {
        using var work = factory.Begin();
        work.TvEpisodes.UnlinkMediaFile(mediaFileId);
        work.Commit();
    }

    public int? GetEpisodeIdForMediaFile(int mediaFileId) =>
        Read(episodes => episodes.GetEpisodeIdForMediaFile(mediaFileId));

    public IReadOnlyList<DropSort.Domain.Library.Movies.MediaFile> ListMediaFiles(int episodeId) =>
        Read(episodes => episodes.ListMediaFiles(episodeId));

    public IReadOnlyDictionary<int, IReadOnlyList<DropSort.Domain.Library.Movies.MediaFile>> ListMediaFilesByShow(int showId) =>
        Read(episodes => episodes.ListMediaFilesByShow(showId));
}
