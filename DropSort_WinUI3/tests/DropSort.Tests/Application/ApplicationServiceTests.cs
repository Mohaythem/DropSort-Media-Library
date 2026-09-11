using System;
using System.Collections.Generic;
using DropSort.Application.Contracts;
using DropSort.Application.Dto;
using DropSort.Application.Repositories;
using DropSort.Application.External;
using DropSort.Application.Services;
using DropSort.Domain.Library.Movies;
using DropSort.Domain.Metadata.Contracts;
using DropSort.Domain.Media.Parser;
using Xunit;

namespace DropSort.Tests.Application;

public class ApplicationServiceTests
{
    private class FakeUnitOfWork : ICatalogUnitOfWork, ICatalogUnitOfWorkFactory
    {
        public FakeMovieRepo FakeMovies { get; } = new();
        public FakeMediaFileRepo FakeMediaFiles { get; } = new();

        public FakeTvShowRepo FakeTvShows { get; } = new();
        public FakeTvSeasonRepo FakeTvSeasons { get; } = new();
        public FakeTvEpisodeRepo FakeTvEpisodes { get; } = new();

        public IMovieRepository Movies => FakeMovies;
        public IMediaFileRepository MediaFiles => FakeMediaFiles;
        public ITvShowRepository TvShows => FakeTvShows;
        public ITvSeasonRepository TvSeasons => FakeTvSeasons;
        public ITvEpisodeRepository TvEpisodes => FakeTvEpisodes;

        public ICatalogUnitOfWork Begin() => this;
        public void Commit() { }
        public void Dispose() { }
    }

    private class FakeMovieRepo : IMovieRepository
    {
        private int _nextId = 1;
        public readonly Dictionary<int, Movie> Storage = new();

        public Movie Create(MovieCatalogData data, DateTimeOffset now)
        {
            var m = new Movie(_nextId++, data, now, now, now);
            Storage[m.Id] = m;
            return m;
        }

        public Movie UpdateMetadata(int id, MovieCatalogData data, DateTimeOffset now)
        {
            var m = Storage[id];
            var updated = new Movie(m.Id, data, m.DateAdded, m.CreatedAt, now);
            Storage[id] = updated;
            return updated;
        }

        public Movie? GetById(int id) => Storage.GetValueOrDefault(id);
        public Movie? GetByExternalId(string p, string e) => null;
        public IReadOnlyList<Movie> ListAll() => new List<Movie>(Storage.Values);
        public IReadOnlyList<Movie> ListPage(int a, int l) => ListAll();
        public int CountAll() => Storage.Count;
        public void Delete(int id) => Storage.Remove(id);
    }

    private class FakeMediaFileRepo : IMediaFileRepository
    {
        private int _nextId = 1;
        public readonly Dictionary<int, MediaFile> Storage = new();

        public MediaFile Add(VerifiedMediaFileFacts facts, int? movieId = null)
        {
            var mf = new MediaFile(_nextId++, movieId, facts.CurrentPath, facts.FileSize, facts.Extension, facts.Resolution, facts.Codec, facts.Source, MediaFileStatus.Present, facts.ObservedAt, facts.ObservedAt);
            Storage[mf.Id] = mf;
            return mf;
        }

        public MediaFile? GetById(int id) => Storage.GetValueOrDefault(id);
        public MediaFile? GetByPath(string path) 
        {
            foreach (var mf in Storage.Values) 
                if (mf.CurrentPath == path) return mf;
            return null;
        }
        public IReadOnlyList<MediaFile> GetByMovieId(int movieId)
        {
            var res = new List<MediaFile>();
            foreach (var mf in Storage.Values)
                if (mf.MovieId == movieId) res.Add(mf);
            return res;
        }
        public IReadOnlyList<MediaFile> ListAll() => new List<MediaFile>(Storage.Values);
        public IReadOnlyList<MediaFile> ListMissing() => new List<MediaFile>();
        public MediaFile LinkToMovie(int id, int movieId) => Storage[id];
        public MediaFile RefreshVerifiedFacts(int id, VerifiedMediaFileFacts facts) => Storage[id];
        public MediaFile MarkMissing(int id, DateTimeOffset observedAt) => Storage[id];
        public MediaFile Relink(int id, string expectedPath, VerifiedMediaFileFacts facts) => Storage[id];
        public void Delete(int id) => Storage.Remove(id);
    }

    private class FakeMetadataProvider : IMetadataProvider
    {
        public string ProviderName => "tmdb";
        public MovieMetadata? GetMovie(string externalId)
        {
            if (externalId == "155")
            {
                return new MovieMetadata("tmdb", "155", "The Dark Knight", "The Dark Knight", 2008, "Batman", System.Collections.Immutable.ImmutableArray.Create("Action"), 152, 9.0, "/poster.jpg");
            }
            return null;
        }

        public IReadOnlyList<MovieCandidate> Search(MovieSearchQuery query)
        {
            return new List<MovieCandidate> { new MovieCandidate("tmdb", "155", "The Dark Knight", null, 2008, null, null, null) };
        }
    }

    private class FakeMaintenanceRepo : ILibraryMaintenanceRepository
    {
        public (int Movies, int MediaFiles, int MetadataEntries, int Shows, int Seasons, int Episodes) ClearCatalog()
        {
            return (10, 15, 10, 2, 3, 24);
        }
    }

    [Fact]
    public void ImportService_RegisterLocal_WorksOffline_WithPendingMetadata()
    {
        var uow = new FakeUnitOfWork();
        var provider = new FakeMetadataProvider();
        var service = new ImportService(uow, provider);

        var path = System.IO.Path.GetFullPath("Batman.mkv");
        var command = new ConfirmMovieImportCommand(
            path, 
            1024, 
            new ParsedMedia("Batman.mkv", MediaType.Movie, "Batman", null, null, null, null, ".mkv"), 
            DateTimeOffset.UtcNow, 
            null); // No candidate selected = offline registration

        var result = service.RegisterMovieImport(command);

        Assert.Equal("Batman", result.Movie.Title);
        Assert.Equal(MetadataStatus.Pending, result.Movie.MetadataStatus);
        Assert.Null(result.Movie.Provider);
        Assert.Equal(path, result.MediaFile.CurrentPath);
    }

    [Fact]
    public void ImportService_ConfirmImport_EnrichesIfCandidateProvided()
    {
        var uow = new FakeUnitOfWork();
        var provider = new FakeMetadataProvider();
        var service = new ImportService(uow, provider);

        var path = System.IO.Path.GetFullPath("DarkKnight.mkv");
        var candidate = new MovieCandidate("tmdb", "155", "The Dark Knight", null, 2008, null, null, null);
        var command = new ConfirmMovieImportCommand(
            path, 
            2048, 
            new ParsedMedia("DarkKnight.mkv", MediaType.Movie, "DarkKnight", null, null, null, null, ".mkv"), 
            DateTimeOffset.UtcNow, 
            candidate);

        var result = service.ConfirmMovieImport(command);

        Assert.Equal("tmdb", result.Movie.Provider);
        Assert.Equal("155", result.Movie.ExternalId);
        Assert.Equal("The Dark Knight", result.Movie.Title);
        Assert.Equal(MetadataStatus.Ready, result.Movie.MetadataStatus);
        Assert.Equal(152, result.Movie.RuntimeMinutes);
    }

    [Fact]
    public void SettingsService_ClearLibrary_PreservesV1Semantics()
    {
        var maintenance = new FakeMaintenanceRepo();
        var service = new SettingsService(maintenance);

        var result = service.ClearLibraryData();

        Assert.Equal(10, result.MoviesRemoved);
        Assert.Equal(15, result.MediaFilesRemoved);
    }
}
