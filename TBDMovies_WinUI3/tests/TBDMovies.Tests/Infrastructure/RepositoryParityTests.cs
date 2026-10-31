using System.Collections.Immutable;
using TBDMovies.Application.Services;
using TBDMovies.Tests.Application;
using TBDMovies.Domain.Core.Operations;
using TBDMovies.Domain.Library.Movies;
using TBDMovies.Domain.Library.Personal;
using TBDMovies.Domain.Library.Shows;
using TBDMovies.Infrastructure.Persistence.Migrations;
using TBDMovies.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Xunit;
using Xunit.Abstractions;

namespace TBDMovies.Tests.Infrastructure;

public sealed class RepositoryParityTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 1, 0, 0, TimeSpan.Zero);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"tbdmovies_repository_{Guid.NewGuid():N}");
    private readonly string _databasePath;
    private readonly string _connectionString;
    private readonly ITestOutputHelper _output;

    public RepositoryParityTests(ITestOutputHelper output)
    {
        _output = output;
        Directory.CreateDirectory(_directory);
        _databasePath = Path.Combine(_directory, "repository.db");
        _connectionString = $"Data Source={_databasePath}";
        new DatabaseMigrator(_connectionString).Migrate();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    [Fact]
    public void CatalogUnitOfWorkPersistsStableIdsGenresAndOriginalTimestamps()
    {
        int movieId;
        int mediaId;
        using (var unit = new CatalogUnitOfWork(_connectionString))
        {
            var movie = unit.Movies.Create(Data("1", "Original", "Drama"), Now);
            var media = unit.MediaFiles.Add(Facts("Original.mkv"), movie.Id);
            movieId = movie.Id;
            mediaId = media.Id;
            unit.Commit();
        }

        using var connection = Open();
        var movies = new MovieRepository(connection);
        var mediaFiles = new MediaFileRepository(connection);
        var before = movies.GetById(movieId)!;
        var updated = movies.UpdateMetadata(movieId, Data("1", "Updated", "Drama", "Thriller"), Now.AddHours(1));

        Assert.Equal(movieId, updated.Id);
        Assert.Equal(mediaId, mediaFiles.GetById(mediaId)!.Id);
        Assert.Equal(before.DateAdded, updated.DateAdded);
        Assert.Equal(before.CreatedAt, updated.CreatedAt);
        Assert.Equal(new[] { "Drama", "Thriller" }, updated.Genres.ToArray());
        Assert.Equal(movieId, mediaFiles.GetById(mediaId)!.MovieId);
    }

    [Fact]
    public void DisposedUncommittedUnitOfWorkRollsBackAllRows()
    {
        using (var unit = new CatalogUnitOfWork(_connectionString))
        {
            var movie = unit.Movies.Create(Data("rollback", "Rollback"), Now);
            unit.MediaFiles.Add(Facts("Rollback.mkv"), movie.Id);
        }
        using var connection = Open();
        Assert.Equal(0L, Scalar(connection, "SELECT COUNT(*) FROM movies;"));
        Assert.Equal(0L, Scalar(connection, "SELECT COUNT(*) FROM media_files;"));
    }

    [Fact]
    public void FileOperationStoreRoundTripsUnsignedFilesystemIdentity()
    {
        using var store = new FileOperationStore(_connectionString, () => Now);
        var plan = new FileOperationPlan(
            "large-identity",
            OperationType.Move,
            Path.Combine(_directory, "source.mkv"),
            Path.Combine(_directory, "destination.mkv"));
        store.Create(plan, Now);
        store.Transition(
            plan.OperationId,
            OperationState.Validated,
            new OperationUpdate(SourceSize: 1, SourceMTimeNs: 2, SourceDev: ulong.MaxValue, SourceIno: ulong.MaxValue - 1));

        var record = store.GetById(plan.OperationId)!;
        Assert.Equal(ulong.MaxValue, record.SourceDev);
        Assert.Equal(ulong.MaxValue - 1, record.SourceIno);
    }

    [Fact]
    public void PersonalHistoryDerivesRewatchAndClearPreservesWatchlist()
    {
        var movieId = SeedMovie("personal");
        var personal = new PersonalLibraryRepository(_connectionString);
        personal.AddToWatchlist(movieId, Now);
        personal.SetPreference(movieId, PersonalPreference.Liked, Now);
        var later = personal.AddWatchEvent(movieId, Now.AddDays(2), Now);
        var earlier = personal.AddWatchEvent(movieId, Now.AddDays(1), Now);

        var history = personal.GetWatchEvents(movieId);
        Assert.Equal([earlier.Id, later.Id], history.Select(item => item.Id));
        Assert.False(history[0].Rewatch);
        Assert.True(history[1].Rewatch);
        Assert.Equal(2, personal.GetState(movieId).WatchCount);
        Assert.True(personal.ClearPreference(movieId, Now).IsWatchlisted);

        personal.DeleteWatchEvent(later.Id);
        var state = personal.GetState(movieId);
        Assert.Equal(1, state.WatchCount);
        Assert.Equal(Now.AddDays(1), state.LastWatched);
    }

    [Fact]
    public void ClearCatalogBlocksNonterminalAndThenPreservesTerminalJournalAndMediaBytes()
    {
        var movieId = SeedMovie("clear");
        int mediaId;
        var path = Path.Combine(_directory, "Clear.mkv");
        File.WriteAllText(path, "immutable");
        using (var connection = Open())
        using (var transaction = connection.BeginTransaction())
        {
            mediaId = new MediaFileRepository(connection, transaction).Add(
                new VerifiedMediaFileFacts(path, new FileInfo(path).Length, ".mkv", null, null, null, Now),
                movieId).Id;
            transaction.Commit();
        }
        using var store = new FileOperationStore(_connectionString, () => Now);
        var plan = new FileOperationPlan(
            "clear-operation", OperationType.Move, path, Path.Combine(_directory, "After.mkv"), mediaId);
        store.Create(plan, Now);

        var maintenance = new LibraryMaintenanceRepository(_connectionString);
        Assert.Throws<InvalidOperationException>(() => maintenance.ClearCatalog());
        Assert.Equal("immutable", File.ReadAllText(path));

        store.Transition(plan.OperationId, OperationState.Failed);
        var counts = maintenance.ClearCatalog();
        Assert.Equal((1, 1), (counts.Movies, counts.MediaFiles));
        Assert.Equal("immutable", File.ReadAllText(path));
        Assert.Null(store.GetById(plan.OperationId)!.MediaFileId);
    }

    [Fact]
    public void ClearCatalogInvalidatesPersistedMetadataCache()
    {
        // A library reset must drop cached TMDB payloads so stale metadata cannot reappear later while
        // matching/selecting media after the reset.
        SeedMovie("cached");
        using (var connection = Open())
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "INSERT INTO metadata_cache (provider, cache_key, payload, fetched_at) VALUES ('tmdb', 'search:ghost', '{\"stale\":true}', $now);";
            command.Parameters.AddWithValue("$now", Now.ToString("o"));
            command.ExecuteNonQuery();
        }

        var counts = new LibraryMaintenanceRepository(_connectionString).ClearCatalog();

        Assert.True(counts.MetadataEntries >= 1);
        using var check = Open();
        using var countCommand = check.CreateCommand();
        countCommand.CommandText = "SELECT COUNT(*) FROM metadata_cache;";
        Assert.Equal(0, Convert.ToInt32(countCommand.ExecuteScalar()));
    }

    [Fact]
    public void ReviewMatchFlag_PersistsAndSurvivesMetadataUpdate()
    {
        var id = SeedMovie("rm");
        using (var uow = new CatalogUnitOfWork(_connectionString))
        {
            uow.Movies.SetReviewMatch(id, true);
            uow.Commit();
        }
        using (var uow = new CatalogUnitOfWork(_connectionString))
        {
            Assert.True(uow.Movies.GetById(id)!.ReviewMatch);
            // A metadata write must not silently reset the flag (it is not part of the metadata columns).
            uow.Movies.UpdateMetadata(id, uow.Movies.GetById(id)!.Data, Now);
            uow.Commit();
        }
        using (var uow = new CatalogUnitOfWork(_connectionString))
        {
            Assert.True(uow.Movies.GetById(id)!.ReviewMatch);
            uow.Movies.SetReviewMatch(id, false);
            uow.Commit();
        }
        using var check = new CatalogUnitOfWork(_connectionString);
        Assert.False(check.Movies.GetById(id)!.ReviewMatch);
    }

    [Fact]
    public void PersonalListProjection_PreservesMovieColumnsAndPersonalFlags()
    {
        var movieId = SeedMovie("personal-projection");
        using (var unit = new CatalogUnitOfWork(_connectionString))
        {
            unit.Movies.SetReviewMatch(movieId, true);
            unit.Movies.RecordOpened(movieId, Now.AddHours(2));
            unit.Commit();
        }

        var personal = new PersonalLibraryRepository(_connectionString);
        personal.AddToWatchlist(movieId, Now);
        personal.SetPreference(movieId, PersonalPreference.Liked, Now.AddMinutes(1));

        var summary = Assert.Single(personal.ListMovies(PersonalLibrarySection.Watchlist));

        Assert.Equal(movieId, summary.Movie.Id);
        Assert.Equal("personal-projection", summary.Movie.Title);
        Assert.Equal(Now, summary.Movie.DateAdded);
        Assert.Equal(Now.AddHours(2), summary.Movie.LastOpenedAt);
        Assert.True(summary.Movie.ReviewMatch);
        Assert.Equal(PersonalPreference.Liked, summary.Preference);
        Assert.True(summary.Watchlisted);
        Assert.Equal(0, summary.MediaFileCount);
        Assert.Equal(0, summary.MissingFileCount);
        Assert.Equal(0, summary.WatchCount);
        Assert.Null(summary.LastWatched);

        Assert.Single(personal.ListMovies(PersonalLibrarySection.Liked));
    }

    [Theory]
    [InlineData(PersonalLibrarySection.Watchlist)]
    [InlineData(PersonalLibrarySection.Liked)]
    [InlineData(PersonalLibrarySection.Blacklisted)]
    public void PersonalListProjection_CountsDistinctMissingFilesAcrossRepeatedViews(PersonalLibrarySection section)
    {
        var movieId = SeedMovie("repeated-view-counts");
        using (var unit = new CatalogUnitOfWork(_connectionString))
        {
            unit.MediaFiles.Add(Facts("present.mkv"), movieId);
            var missing = unit.MediaFiles.Add(Facts("missing.mkv"), movieId);
            unit.MediaFiles.MarkMissing(missing.Id, Now);
            unit.Commit();
        }
        var personal = new PersonalLibraryRepository(_connectionString);
        personal.AddToWatchlist(movieId, Now);
        personal.SetPreference(movieId, section == PersonalLibrarySection.Blacklisted
            ? PersonalPreference.Blacklisted : PersonalPreference.Liked, Now);
        for (var i = 0; i < 3; i++) personal.AddWatchEvent(movieId, Now.AddDays(i), Now.AddDays(i));

        var summary = Assert.Single(personal.ListMovies(section));
        Assert.Equal(2, summary.MediaFileCount);
        Assert.Equal(1, summary.MissingFileCount);
        Assert.Equal(3, summary.WatchCount);
        Assert.Equal(Now.AddDays(2), summary.LastWatched);
    }

    [Fact]
    public void RecordOpened_PersistsUtcForMovieAndShow_WithoutChangingCatalogOrPersonalState()
    {
        int movieId;
        int showId;
        using (var unit = new CatalogUnitOfWork(_connectionString))
        {
            movieId = unit.Movies.Create(Data("opened-movie", "Opened Movie"), Now).Id;
            showId = unit.TvShows.Create(new TvShowCatalogData(null, null, "Opened Show"), Now).Id;
            unit.Commit();
        }

        var openedMovie = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.FromHours(3));
        var openedShow = new DateTimeOffset(2026, 9, 1, 13, 0, 0, TimeSpan.FromHours(-5));
        using (var unit = new CatalogUnitOfWork(_connectionString))
        {
            unit.Movies.RecordOpened(movieId, openedMovie);
            unit.TvShows.RecordOpened(showId, openedShow);
            unit.Commit();
        }

        using (var check = new CatalogUnitOfWork(_connectionString))
        {
            var movie = check.Movies.GetById(movieId)!;
            var show = check.TvShows.GetById(showId)!;
            Assert.Equal(openedMovie.UtcDateTime, movie.LastOpenedAt!.Value.UtcDateTime);
            Assert.Equal(openedShow.UtcDateTime, show.LastOpenedAt!.Value.UtcDateTime);
            Assert.Equal(Now, movie.DateAdded);
            Assert.Equal(Now, movie.CreatedAt);
            Assert.Equal(Now, movie.UpdatedAt);
            Assert.Equal(Now, show.DateAdded);
            Assert.Equal(Now, show.CreatedAt);
            Assert.Equal(Now, show.UpdatedAt);
        }

        var personal = new PersonalLibraryRepository(_connectionString);
        personal.AddToWatchlist(movieId, Now);
        personal.SetPreference(movieId, PersonalPreference.Liked, Now.AddMinutes(1));
        var summary = Assert.Single(personal.ListMovies(PersonalLibrarySection.Watchlist));
        Assert.Equal(openedMovie.UtcDateTime, summary.Movie.LastOpenedAt!.Value.UtcDateTime);
        Assert.True(summary.Watchlisted);
        Assert.Equal(PersonalPreference.Liked, summary.Preference);
    }

    [Fact]
    public void RecordOpened_RollsBackWithoutCommit_AndMetadataUpdatePreservesActivity()
    {
        var movieId = SeedMovie("opened-rollback");
        var opened = Now.AddHours(4);
        using (var unit = new CatalogUnitOfWork(_connectionString))
        {
            unit.Movies.RecordOpened(movieId, opened);
        }

        using (var unit = new CatalogUnitOfWork(_connectionString))
            Assert.Null(unit.Movies.GetById(movieId)!.LastOpenedAt);

        using (var unit = new CatalogUnitOfWork(_connectionString))
        {
            unit.Movies.RecordOpened(movieId, opened);
            unit.Commit();
        }
        using (var unit = new CatalogUnitOfWork(_connectionString))
        {
            var movie = unit.Movies.GetById(movieId)!;
            unit.Movies.UpdateMetadata(movieId, movie.Data, Now.AddDays(1));
            unit.Commit();
        }
        using var check = new CatalogUnitOfWork(_connectionString);
        Assert.Equal(opened, check.Movies.GetById(movieId)!.LastOpenedAt);
    }

    [Fact]
    public void CatalogFactory_RevisionChangesOnlyAfterSuccessfulCommit()
    {
        var factory = new CatalogUnitOfWorkFactory(_connectionString);
        Assert.Equal(0, factory.Revision);
        using (var read = factory.Begin())
        {
            _ = read.Movies.ListAll();
        }
        Assert.Equal(0, factory.Revision);
        using (var rollback = factory.Begin())
        {
            rollback.Movies.Create(Data("revision-rollback", "Rollback"), Now);
        }
        Assert.Equal(0, factory.Revision);
        using (var committed = factory.Begin())
        {
            committed.Movies.Create(Data("revision-commit", "Commit"), Now);
            committed.Commit();
        }
        Assert.Equal(1, factory.Revision);
    }

    private int SeedMovie(string externalId)
    {
        using var unit = new CatalogUnitOfWork(_connectionString);
        var movie = unit.Movies.Create(Data(externalId, externalId), Now);
        unit.Commit();
        return movie.Id;
    }

    [Fact]
    public void TvSummaryQuery_MatchesHierarchyCountsIncludingEmptySeasonsAndMultipleCopies()
    {
        var factory = new CatalogUnitOfWorkFactory(_connectionString);
        int showId;
        using (var unit = factory.Begin())
        {
            unit.TvShows.Create(new TvShowCatalogData(null, null, "Empty Show"), Now);
            var show = unit.TvShows.Create(new TvShowCatalogData("tmdb", "55", "Canonical Show", year: 2005), Now.AddDays(1));
            showId = show.Id;
            unit.TvShows.SetCustomDisplayTitle(showId, "My Show");
            unit.TvShows.RecordOpened(showId, Now.AddDays(2));
            var season = unit.TvSeasons.Create(showId, 1, null, null, Now);
            unit.TvSeasons.Create(showId, 2, "Empty Season", null, Now);
            var mixed = unit.TvEpisodes.Create(season.Id, 1, "Mixed", null, null, null, Now);
            var missing = unit.TvEpisodes.Create(season.Id, 2, "Missing", null, null, null, Now);
            unit.TvEpisodes.Create(season.Id, 3, "No File", null, null, null, Now);
            foreach (var (episodeId, name, isMissing) in new[] {
                (mixed.Id, "mixed-present.mkv", false), (mixed.Id, "mixed-missing.mkv", true),
                (missing.Id, "missing-a.mkv", true), (missing.Id, "missing-b.mkv", true) })
            {
                var file = unit.MediaFiles.Add(Facts(name));
                unit.TvEpisodes.LinkMediaFile(episodeId, file.Id, Now);
                if (isMissing) unit.MediaFiles.MarkMissing(file.Id, Now);
            }
            unit.Commit();
        }
        var baseline = new TvLibraryService(new PerCallTvShowRepository(factory),
            new PerCallTvSeasonRepository(factory), new PerCallTvEpisodeRepository(factory)).ListShows();
        using var connection = Open();
        // Null hierarchy readers prove the optimized service never falls back to per-show queries.
        var optimized = new TvLibraryService(new TvShowRepository(connection), null!, null!).ListShows();
        Assert.Equal(baseline, optimized);
        var card = Assert.Single(optimized, row => row.Id == showId);
        Assert.Equal(2, card.SeasonCount);
        Assert.Equal(3, card.EpisodeCount);
        Assert.Equal(1, card.EpisodesWithLocalFile);
        Assert.Equal(1, card.MissingEpisodeCount);
        Assert.Equal("My Show", card.Title);
        Assert.Equal("Canonical Show", card.CanonicalTitle);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(250)]
    public void TvSummaryQuery_DoesNotReadPerShowHierarchyAtScale(int showCount)
    {
        using (var unit = new CatalogUnitOfWork(_connectionString))
        {
            for (var i = 0; i < showCount; i++)
                unit.TvShows.Create(new TvShowCatalogData(null, null, $"Show {i}"), Now);
            unit.Commit();
        }
        using var connection = Open();
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var rows = new TvLibraryService(new TvShowRepository(connection), null!, null!).ListShows();
        var summaryMilliseconds = timer.Elapsed.TotalMilliseconds;
        var factory = new CatalogUnitOfWorkFactory(_connectionString);
        timer.Restart();
        var baseline = new TvLibraryService(new PerCallTvShowRepository(factory),
            new PerCallTvSeasonRepository(factory), new PerCallTvEpisodeRepository(factory)).ListShows();
        _output.WriteLine($"TV summary fixture: shows={showCount}, bulk={summaryMilliseconds:F2}ms, hierarchy={timer.Elapsed.TotalMilliseconds:F2}ms");
        Assert.Equal(baseline, rows);
        Assert.Equal(showCount, rows.Count);
        Assert.All(rows, row => { Assert.Equal(0, row.SeasonCount); Assert.Equal(0, row.EpisodeCount); });
    }

    private MovieCatalogData Data(string externalId, string title, params string[] genres) => new(
        "tmdb", externalId, title, null, 2026, "Overview", genres.ToImmutableArray(), 100, 8.0, null, MetadataStatus.Ready);

    private VerifiedMediaFileFacts Facts(string name)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, "media");
        return new VerifiedMediaFileFacts(path, new FileInfo(path).Length, Path.GetExtension(path), null, null, null, Now);
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON;";
        command.ExecuteNonQuery();
        return connection;
    }

    private static long Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }
}
