using DropSort.Application.Repositories;
using DropSort.Domain.Library.Movies;
using DropSort.Domain.Library.Shows;

namespace DropSort.UI.Services;

/// <summary>
/// Adapts the transactional catalog unit of work onto the flat repository contracts the application
/// services take.
/// <para>
/// Every call opens its own short-lived unit of work: reads roll back on dispose, writes commit. No
/// connection is held open across a user interaction, and a background scan can therefore run on a
/// worker thread without sharing a SQLite connection with the UI thread - Microsoft.Data.Sqlite
/// connections are not thread safe, so one long-lived connection would be a latent crash.
/// </para>
/// </summary>
internal sealed class UnitOfWorkMovieRepository(ICatalogUnitOfWorkFactory factory) : IMovieRepository
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

    public IReadOnlyList<Movie> ListPage(int afterId, int limit) =>
        Read(movies => movies.ListPage(afterId, limit));

    public int CountAll() => Read(movies => movies.CountAll());

    public Movie Create(MovieCatalogData data, DateTimeOffset now) =>
        Write(movies => movies.Create(data, now));

    public Movie UpdateMetadata(int id, MovieCatalogData data, DateTimeOffset now) =>
        Write(movies => movies.UpdateMetadata(id, data, now));

    public void Delete(int id) => Write<object?>(movies =>
    {
        movies.Delete(id);
        return null;
    });
}

/// <summary>Media-file half of <see cref="UnitOfWorkMovieRepository" />; same one-call-one-transaction rule.</summary>
internal sealed class UnitOfWorkMediaFileRepository(ICatalogUnitOfWorkFactory factory) : IMediaFileRepository
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

    public MediaFile Add(VerifiedMediaFileFacts facts, int? movieId = null) =>
        Write(files => files.Add(facts, movieId));

    public MediaFile LinkToMovie(int id, int movieId) => Write(files => files.LinkToMovie(id, movieId));

    public MediaFile RefreshVerifiedFacts(int id, VerifiedMediaFileFacts facts) =>
        Write(files => files.RefreshVerifiedFacts(id, facts));

    public MediaFile MarkMissing(int id, DateTimeOffset observedAt) =>
        Write(files => files.MarkMissing(id, observedAt));

    public MediaFile Relink(int id, string expectedPath, VerifiedMediaFileFacts facts) =>
        Write(files => files.Relink(id, expectedPath, facts));

    public void Delete(int id) => Write<object?>(files =>
    {
        files.Delete(id);
        return null;
    });
}

/// <summary>Show half of the TV catalog; same one-call-one-transaction rule as the movie adapters.</summary>
internal sealed class UnitOfWorkTvShowRepository(ICatalogUnitOfWorkFactory factory) : ITvShowRepository
{
    private T Read<T>(Func<ITvShowRepository, T> read)
    {
        using var work = factory.Begin();
        return read(work.TvShows);
    }

    private T Write<T>(Func<ITvShowRepository, T> write)
    {
        using var work = factory.Begin();
        var result = write(work.TvShows);
        work.Commit();
        return result;
    }

    public TvShow? GetById(int id) => Read(shows => shows.GetById(id));

    public TvShow? GetBySortTitle(string sortTitle) => Read(shows => shows.GetBySortTitle(sortTitle));

    public TvShow? GetByExternalId(string provider, string externalId) =>
        Read(shows => shows.GetByExternalId(provider, externalId));

    public IReadOnlyList<TvShow> ListAll() => Read(shows => shows.ListAll());

    public int CountAll() => Read(shows => shows.CountAll());

    public TvShow Create(TvShowCatalogData data, DateTimeOffset now) => Write(shows => shows.Create(data, now));

    public TvShow UpdateMetadata(int id, TvShowCatalogData data, DateTimeOffset now) =>
        Write(shows => shows.UpdateMetadata(id, data, now));

    public void Delete(int id) => Write<object?>(shows =>
    {
        shows.Delete(id);
        return null;
    });
}

/// <summary>Season half of the TV catalog.</summary>
internal sealed class UnitOfWorkTvSeasonRepository(ICatalogUnitOfWorkFactory factory) : ITvSeasonRepository
{
    private T Read<T>(Func<ITvSeasonRepository, T> read)
    {
        using var work = factory.Begin();
        return read(work.TvSeasons);
    }

    public TvSeason? GetById(int id) => Read(seasons => seasons.GetById(id));

    public TvSeason? GetByNumber(int showId, int seasonNumber) =>
        Read(seasons => seasons.GetByNumber(showId, seasonNumber));

    public IReadOnlyList<TvSeason> ListByShow(int showId) => Read(seasons => seasons.ListByShow(showId));

    public TvSeason Create(int showId, int seasonNumber, string? title, string? overview, DateTimeOffset now)
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

/// <summary>Episode half of the TV catalog, including the episode-to-media-file links.</summary>
internal sealed class UnitOfWorkTvEpisodeRepository(ICatalogUnitOfWorkFactory factory) : ITvEpisodeRepository
{
    private T Read<T>(Func<ITvEpisodeRepository, T> read)
    {
        using var work = factory.Begin();
        return read(work.TvEpisodes);
    }

    public TvEpisode? GetById(int id) => Read(episodes => episodes.GetById(id));

    public TvEpisode? GetByNumber(int seasonId, int episodeNumber) =>
        Read(episodes => episodes.GetByNumber(seasonId, episodeNumber));

    public IReadOnlyList<TvEpisode> ListBySeason(int seasonId) => Read(episodes => episodes.ListBySeason(seasonId));

    public IReadOnlyList<TvEpisode> ListByShow(int showId) => Read(episodes => episodes.ListByShow(showId));

    public TvEpisode Create(
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

    public IReadOnlyList<MediaFile> ListMediaFiles(int episodeId) =>
        Read(episodes => episodes.ListMediaFiles(episodeId));

    public IReadOnlyDictionary<int, IReadOnlyList<MediaFile>> ListMediaFilesByShow(int showId) =>
        Read(episodes => episodes.ListMediaFilesByShow(showId));
}
