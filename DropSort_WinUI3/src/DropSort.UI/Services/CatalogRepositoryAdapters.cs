using DropSort.Application.Repositories;
using DropSort.Domain.Library.Movies;

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
