using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using DropSort.Application.Repositories;
using DropSort.Application.Services;
using DropSort.Domain.Library.Movies;
using Xunit;

namespace DropSort.Tests.Application;

/// <summary>
/// Scale-architecture invariant: Library must read presence in one bulk query, never a per-movie
/// GetByMovieId (the N+1 that blocked the UI thread for ~38s at 10K). This is a behavioural contract
/// test, not a wall-clock test.
/// </summary>
public class LibraryScaleContractTests
{
    [Fact]
    public void ListMovies_uses_one_bulk_presence_query_and_no_per_movie_lookup()
    {
        var movies = new CountingMovieRepository(500);
        var media = new CountingMediaFileRepository([1, 2, 3]);
        // ListMovies never touches the personal repository.
        var service = new LibraryService(movies, media, personal: null!);

        var items = service.ListMovies();

        Assert.Equal(500, items.Count);
        Assert.Equal(1, media.BulkPresenceCalls);   // one query for the whole grid
        Assert.Equal(0, media.GetByMovieIdCalls);    // no N+1
        Assert.False(items.Single(i => i.Id == 1).IsMissing);   // has a present file
        Assert.True(items.Single(i => i.Id == 400).IsMissing);  // no present file
    }

    private static Movie MakeMovie(int id) => new(
        id,
        new MovieCatalogData("tmdb", "m" + id, "Movie " + id, null, 2020, null,
            ImmutableArray<string>.Empty, null, null, null, MetadataStatus.Ready),
        DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    // APPEND_REPOS
    private sealed class CountingMovieRepository(int count) : IMovieRepository
    {
        public Movie? GetById(int id) => id >= 1 && id <= count ? MakeMovie(id) : null;
        public Movie? GetByExternalId(string provider, string externalId) => null;
        public IReadOnlyList<Movie> ListAll() => Enumerable.Range(1, count).Select(MakeMovie).ToList();
        public IReadOnlyList<Movie> ListPage(int afterId, int limit) => ListAll();
        public int CountAll() => count;
        public Movie Create(MovieCatalogData data, DateTimeOffset now) => throw new NotSupportedException();
        public Movie UpdateMetadata(int id, MovieCatalogData data, DateTimeOffset now) => throw new NotSupportedException();
        public void Delete(int id) { }
    }

    private sealed class CountingMediaFileRepository(IEnumerable<int> presentMovieIds) : IMediaFileRepository
    {
        private readonly IReadOnlySet<int> _present = presentMovieIds.ToHashSet();
        public int GetByMovieIdCalls { get; private set; }
        public int BulkPresenceCalls { get; private set; }

        public IReadOnlySet<int> ListMovieIdsWithPresentFile()
        {
            BulkPresenceCalls++;
            return _present;
        }

        public IReadOnlyList<MediaFile> GetByMovieId(int movieId)
        {
            GetByMovieIdCalls++;
            return [];
        }

        public MediaFile? GetById(int id) => null;
        public MediaFile? GetByPath(string path) => null;
        public IReadOnlyList<MediaFile> ListAll() => [];
        public IReadOnlyList<MediaFile> ListMissing() => [];
        public MediaFile Add(VerifiedMediaFileFacts facts, int? movieId = null) => throw new NotSupportedException();
        public MediaFile LinkToMovie(int id, int movieId) => throw new NotSupportedException();
        public MediaFile RefreshVerifiedFacts(int id, VerifiedMediaFileFacts facts) => throw new NotSupportedException();
        public MediaFile MarkMissing(int id, DateTimeOffset observedAt) => throw new NotSupportedException();
        public MediaFile Relink(int id, string expectedPath, VerifiedMediaFileFacts facts) => throw new NotSupportedException();
        public void Delete(int id) { }
    }
}
