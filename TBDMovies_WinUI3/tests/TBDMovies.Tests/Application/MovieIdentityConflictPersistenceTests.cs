using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TBDMovies.Application.External;
using TBDMovies.Application.Services;
using TBDMovies.Domain.Library.Movies;
using TBDMovies.Domain.Library.Personal;
using TBDMovies.Domain.Metadata.Contracts;
using TBDMovies.Infrastructure.Persistence.Migrations;
using TBDMovies.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Xunit;

namespace TBDMovies.Tests.Application;

public sealed class MovieIdentityConflictPersistenceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tbdmovies_movie_conflict_" + Guid.NewGuid().ToString("N"));
    private readonly string _connectionString;
    private readonly CatalogUnitOfWorkFactory _factory;

    public MovieIdentityConflictPersistenceTests()
    {
        Directory.CreateDirectory(_root);
        _connectionString = "Data Source=" + Path.Combine(_root, "catalog.db");
        new DatabaseMigrator(_connectionString).Migrate();
        _factory = new CatalogUnitOfWorkFactory(_connectionString);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [Fact]
    public async Task ConflictingOfflineMovieMatch_PreservesFilesAndPersonalState()
    {
        var now = DateTimeOffset.Parse("2026-10-02T12:00:00+00:00");
        int ownerId;
        int offlineId;
        using (var uow = _factory.Begin())
        {
            ownerId = uow.Movies.Create(
                new MovieCatalogData("tmdb", "owner", "Canonical Movie", null, 2020, "Owner overview",
                    ImmutableArray.Create("Drama"), null, null, null, MetadataStatus.Ready), now).Id;
            uow.MediaFiles.Add(new VerifiedMediaFileFacts(Path.Combine(_root, "owner.mkv"), 100, ".mkv", null, null, null, now), ownerId);
            offlineId = uow.Movies.Create(
                new MovieCatalogData(null, null, "Offline Movie", null, 2020, null,
                    ImmutableArray<string>.Empty, null, null, null, MetadataStatus.Pending), now).Id;
            uow.MediaFiles.Add(new VerifiedMediaFileFacts(Path.Combine(_root, "offline.mkv"), 200, ".mkv", null, null, null, now), offlineId);
            uow.Commit();
        }

        var personal = new PersonalLibraryRepository(_connectionString);
        personal.SetPreference(offlineId, PersonalPreference.Liked, now);
        personal.AddToWatchlist(offlineId, now);
        var listId = personal.CreateCustomList("Offline Favorites", now);
        personal.AddMovieToCustomList(listId, offlineId, now);
        personal.AddWatchEvent(offlineId, now, now);

        var before = SnapshotTables();
        var matching = new MetadataMatchingService(_factory, new StubMetadataProvider());
        var error = await Assert.ThrowsAsync<MetadataServiceException>(() => matching.MatchMovieAsync(
            offlineId,
            new MovieCandidate("tmdb", "owner", "Canonical Movie", null, 2020, null, null, null)));

        Assert.Equal(MetadataFailureKind.IdentityConflict, error.Kind);
        Assert.Equal(before, SnapshotTables());

        using var check = _factory.Begin();
        Assert.Equal("owner", check.Movies.GetById(ownerId)!.ExternalId);
        Assert.Null(check.Movies.GetById(offlineId)!.ExternalId);
        Assert.Equal(1, check.MediaFiles.GetByMovieId(ownerId).Count);
        Assert.Equal(1, check.MediaFiles.GetByMovieId(offlineId).Count);
    }

    private Dictionary<string, string> SnapshotTables() =>
        new[] { "movies", "media_files", "movie_personal_state", "watch_events", "custom_lists", "custom_list_members" }
            .ToDictionary(table => table, SnapshotTable);

    private string SnapshotTable(string table)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT * FROM {table} ORDER BY 1";
        using var reader = command.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read())
        {
            var values = new object[reader.FieldCount];
            reader.GetValues(values);
            rows.Add(string.Join("|", values.Select(value => value is DBNull ? "<NULL>" : value.ToString())));
        }
        return string.Join("\n", rows);
    }

    private sealed class StubMetadataProvider : IMetadataProvider
    {
        public string ProviderName => "TMDB";
        public bool IsConfigured => true;
        public Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConnectionTestResult(true, "ok", 200));
        public IReadOnlyList<MovieCandidate> Search(MovieSearchQuery query) => [];
        public MovieMetadata? GetMovie(string externalId) => new MovieMetadata(
            "tmdb", externalId, "Canonical Movie", null, 2020, "Provider overview",
            ImmutableArray.Create("Drama"), null, null, null);
        public Task<IReadOnlyList<MovieCandidate>> SearchMoviesAsync(MovieSearchQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MovieCandidate>>([]);
        public Task<MovieMetadata?> GetMovieAsync(string externalId, CancellationToken cancellationToken = default) =>
            Task.FromResult<MovieMetadata?>(new MovieMetadata(
                "tmdb", externalId, "Canonical Movie", null, 2020, "Provider overview",
                ImmutableArray.Create("Drama"), null, null, null));
        public Task<IReadOnlyList<TvCandidate>> SearchTvAsync(TvSearchQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TvCandidate>>([]);
        public Task<TvShowMetadata?> GetTvShowAsync(string externalId, CancellationToken cancellationToken = default) =>
            Task.FromResult<TvShowMetadata?>(null);
        public Task<TvSeasonMetadata?> GetTvSeasonAsync(string showExternalId, int seasonNumber, CancellationToken cancellationToken = default) =>
            Task.FromResult<TvSeasonMetadata?>(null);
    }
}
