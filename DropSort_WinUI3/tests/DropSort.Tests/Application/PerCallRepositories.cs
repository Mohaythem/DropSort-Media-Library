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
