using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DropSort.Application.Contracts;
using DropSort.Application.Dto;
using DropSort.Application.External;
using DropSort.Application.Services;
using DropSort.Application.Trakt;
using DropSort.Domain.Media.Parser;
using DropSort.Domain.Library.Availability;
using DropSort.Domain.Library.Personal;
using System;
using System.Collections.Immutable;
using System.IO;
using DropSort.Domain.Library.Movies;
using DropSort.Domain.Library.Shows;
using DropSort.Infrastructure.Persistence.Migrations;
using DropSort.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DropSort.Tests.Infrastructure;

public sealed class CustomDisplayTitlePersistenceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"dropsort_custom_title_{Guid.NewGuid():N}");
    private readonly string _databasePath;
    private readonly string _connectionString;

    public CustomDisplayTitlePersistenceTests()
    {
        Directory.CreateDirectory(_directory);
        _databasePath = Path.Combine(_directory, "test.db");
        _connectionString = $"Data Source={_databasePath}";
        new DatabaseMigrator(_connectionString).Migrate();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    [Fact]
    public void DatabaseMigrator_UpgradesToVersion19_WithCustomDisplayTitleColumns()
    {
        Assert.Equal(19, DatabaseMigrator.LatestVersion);

        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        Assert.Equal(19L, Convert.ToInt64(cmd.ExecuteScalar()));

        // Verify column exists on movies table
        cmd.CommandText = "SELECT custom_display_title FROM movies LIMIT 0;";
        using (var reader = cmd.ExecuteReader()) { }

        // Verify column exists on tv_shows table
        cmd.CommandText = "SELECT custom_display_title FROM tv_shows LIMIT 0;";
        using (var reader = cmd.ExecuteReader()) { }
    }

    [Fact]
    public void Movie_CustomDisplayTitle_Persists_AndPreservesAcrossMetadataRefresh()
    {
        int movieId;
        using (var unit = new CatalogUnitOfWork(_connectionString))
        {
            var created = unit.Movies.Create(
                new MovieCatalogData("tmdb", "550", "Fight Club", "Fight Club", 1999, "An insomniac office worker...", [], 139, 8.4, null, MetadataStatus.Ready),
                Now);
            movieId = created.Id;
            unit.Commit();
        }

        using (var connection = Open())
        {
            var movies = new MovieRepository(connection);
            var initial = movies.GetById(movieId)!;
            Assert.Null(initial.CustomDisplayTitle);
            Assert.Equal("Fight Club", initial.DisplayTitle);
            Assert.Equal("Fight Club", initial.Title);

            // Set custom title override
            movies.SetCustomDisplayTitle(movieId, "Fight Club (1999) - Fincher");
        }

        // Read back in a fresh connection to verify disk persistence
        using (var connection = Open())
        {
            var movies = new MovieRepository(connection);
            var withCustom = movies.GetById(movieId)!;
            Assert.Equal("Fight Club (1999) - Fincher", withCustom.CustomDisplayTitle);
            Assert.Equal("Fight Club (1999) - Fincher", withCustom.DisplayTitle);
            Assert.Equal("Fight Club", withCustom.Title); // Canonical remains Fight Club

            // Metadata Refresh from TMDB: should update overview/rating but KEEP custom display title
            var refreshed = movies.UpdateMetadata(movieId,
                new MovieCatalogData("tmdb", "550", "Fight Club", "Fight Club", 1999, "Updated overview from TMDB", ["Drama"], 139, 8.5, "poster.jpg", MetadataStatus.Ready),
                Now.AddDays(1));

            Assert.Equal("Updated overview from TMDB", refreshed.Overview);
            Assert.Equal("poster.jpg", refreshed.PosterReference);
            Assert.Equal("Fight Club (1999) - Fincher", refreshed.CustomDisplayTitle);
            Assert.Equal("Fight Club (1999) - Fincher", refreshed.DisplayTitle);
            Assert.Equal("Fight Club", refreshed.Title);
        }

        // Clearing override by passing null or whitespace reverts to canonical
        using (var connection = Open())
        {
            var movies = new MovieRepository(connection);
            movies.SetCustomDisplayTitle(movieId, "   ");
            var cleared = movies.GetById(movieId)!;
            Assert.Null(cleared.CustomDisplayTitle);
            Assert.Equal("Fight Club", cleared.DisplayTitle);
            Assert.Equal("Fight Club", cleared.Title);
        }
    }

    [Fact]
    public void TvShow_CustomDisplayTitle_Persists_AndPreservesAcrossMetadataRefresh()
    {
        int showId;
        using (var unit = new CatalogUnitOfWork(_connectionString))
        {
            var created = unit.TvShows.Create(
                new TvShowCatalogData("tmdb", "1396", "Breaking Bad", "Breaking Bad", 2008, "A chemistry teacher...", ["Crime", "Drama"], null, MetadataStatus.Ready),
                Now);
            showId = created.Id;
            unit.Commit();
        }

        using (var connection = Open())
        {
            var shows = new TvShowRepository(connection);
            var initial = shows.GetById(showId)!;
            Assert.Null(initial.CustomDisplayTitle);
            Assert.Equal("Breaking Bad", initial.DisplayTitle);
            Assert.Equal("Breaking Bad", initial.Title);

            // Set custom title override
            shows.SetCustomDisplayTitle(showId, "Breaking Bad: The Walter White Saga");
        }

        // Read back in a fresh connection
        using (var connection = Open())
        {
            var shows = new TvShowRepository(connection);
            var withCustom = shows.GetById(showId)!;
            Assert.Equal("Breaking Bad: The Walter White Saga", withCustom.CustomDisplayTitle);
            Assert.Equal("Breaking Bad: The Walter White Saga", withCustom.DisplayTitle);
            Assert.Equal("Breaking Bad", withCustom.Title); // Canonical remains Breaking Bad

            // Metadata refresh: updates metadata but preserves custom display title
            var refreshed = shows.UpdateMetadata(showId,
                new TvShowCatalogData("tmdb", "1396", "Breaking Bad", "Breaking Bad", 2008, "Updated overview from TMDB", ["Crime", "Drama", "Thriller"], "show_poster.jpg", MetadataStatus.Ready),
                Now.AddDays(2));

            Assert.Equal("Updated overview from TMDB", refreshed.Overview);
            Assert.Equal("Breaking Bad: The Walter White Saga", refreshed.CustomDisplayTitle);
            Assert.Equal("Breaking Bad: The Walter White Saga", refreshed.DisplayTitle);
            Assert.Equal("Breaking Bad", refreshed.Title);

            // Also verify GetBySortTitle loads custom title correctly
            var bySort = shows.GetBySortTitle("breaking bad")!;
            Assert.Equal("Breaking Bad: The Walter White Saga", bySort.DisplayTitle);
        }

        // Clearing override reverts to canonical title
        using (var connection = Open())
        {
            var shows = new TvShowRepository(connection);
            shows.SetCustomDisplayTitle(showId, null);
            var cleared = shows.GetById(showId)!;
            Assert.Null(cleared.CustomDisplayTitle);
            Assert.Equal("Breaking Bad", cleared.DisplayTitle);
            Assert.Equal("Breaking Bad", cleared.Title);
        }
    }

    [Fact]
    public void CustomDisplayTitle_DoesNotMutate_TraktResourceCache()
    {
        // Insert a sample Trakt cache row
        using (var connection = Open())
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO trakt_resource_cache (account_id, resource_key, payload_json, succeeded_at, attempted_at)
                VALUES ('acc_1', 'watched_movies', '{"title":"Fight Club"}', '2026-10-21T00:00:00Z', '2026-10-21T00:00:00Z');
                """;
            cmd.ExecuteNonQuery();
        }

        // Create movie and modify its custom display title
        using (var unit = new CatalogUnitOfWork(_connectionString))
        {
            var movie = unit.Movies.Create(
                new MovieCatalogData("tmdb", "550", "Fight Club", "Fight Club", 1999, "Overview", [], 139, 8.4, null, MetadataStatus.Ready),
                Now);
            unit.Commit();
        }

        using (var connection = Open())
        {
            var movies = new MovieRepository(connection);
            movies.SetCustomDisplayTitle(1, "My Custom Title");

            // Verify trakt_resource_cache payload is completely unchanged
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT payload_json FROM trakt_resource_cache WHERE resource_key = 'watched_movies';";
            var payload = (string)cmd.ExecuteScalar()!;
            Assert.Equal("{\"title\":\"Fight Club\"}", payload);
        }
    }

    [Fact]
    public void DatabaseRestart_PreservesCustomDisplayTitles()
    {
        int movieId;
        int showId;

        // 1. Session 1: Create and set custom titles
        using (var unit = new CatalogUnitOfWork(_connectionString))
        {
            var movie = unit.Movies.Create(
                new MovieCatalogData("tmdb", "603", "The Matrix", "The Matrix", 1999, "Neo...", [], 136, 8.7, null, MetadataStatus.Ready),
                Now);
            movieId = movie.Id;

            var show = unit.TvShows.Create(
                new TvShowCatalogData("tmdb", "1399", "Game of Thrones", "Game of Thrones", 2011, "Westeros...", [], null, MetadataStatus.Ready),
                Now);
            showId = show.Id;

            unit.Movies.SetCustomDisplayTitle(movieId, "The Matrix (Classic 1999)");
            unit.TvShows.SetCustomDisplayTitle(showId, "GoT (Seasons 1-8)");
            unit.Commit();
        }

        // 2. Complete shutdown: clear SQLite connection pools
        SqliteConnection.ClearAllPools();

        // 3. Session 2: Fresh connection to the SQLite database file
        using (var connection = Open())
        {
            var movies = new MovieRepository(connection);
            var shows = new TvShowRepository(connection);

            var loadedMovie = movies.GetById(movieId)!;
            Assert.Equal("The Matrix (Classic 1999)", loadedMovie.CustomDisplayTitle);
            Assert.Equal("The Matrix (Classic 1999)", loadedMovie.DisplayTitle);
            Assert.Equal("The Matrix", loadedMovie.Title);

            var loadedShow = shows.GetById(showId)!;
            Assert.Equal("GoT (Seasons 1-8)", loadedShow.CustomDisplayTitle);
            Assert.Equal("GoT (Seasons 1-8)", loadedShow.DisplayTitle);
            Assert.Equal("Game of Thrones", loadedShow.Title);
        }
    }

    [Fact]
    public void WatchHistory_LiveProjection_ShowsCustomDisplayTitle_WithoutRewritingHistoricalSnapshots()
    {
        int movieId;
        using (var unit = new CatalogUnitOfWork(_connectionString))
        {
            var movie = unit.Movies.Create(
                new MovieCatalogData("tmdb", "550", "Fight Club", "Fight Club", 1999, "Overview", [], 139, 8.4, null, MetadataStatus.Ready),
                Now);
            movieId = movie.Id;
            unit.MediaFiles.Add(new VerifiedMediaFileFacts(
                Path.Combine(_directory, "Fight.Club.1999.mkv"), 1024, ".mkv", "1080p", "H264", "WEB", Now), movieId);
            unit.Commit();
        }

        var personal = new PersonalLibraryRepository(_connectionString);
        var viewingRepo = new ViewingActivityRepository(_connectionString);

        // 1. Add watch event for movie
        var watchEvent = personal.AddWatchEvent(movieId, Now, Now);
        var initialHistory = personal.ListRecentlyWatchedMedia(10);
        Assert.Equal("Fight Club", Assert.Single(initialHistory).TitleSnapshot);

        // 2. Set custom display title
        using (var connection = Open())
        {
            new MovieRepository(connection).SetCustomDisplayTitle(movieId, "Fincher's Fight Club");
        }

        // 3. Live projection from PersonalLibraryRepository (Recently Watched / Recent)
        var recentPersonal = personal.ListRecentlyWatchedMedia(10);
        var movieEntry = Assert.Single(recentPersonal);
        Assert.Equal("Fincher's Fight Club", movieEntry.TitleSnapshot);

        var recentList = personal.ListRecent(10);
        var recentItem = Assert.Single(recentList);
        Assert.Equal("Fincher's Fight Club", recentItem.TitleSnapshot);

        // 4. Live projection from ViewingActivityRepository
        var recentCompleted = viewingRepo.ListRecentCompleted(10);
        var completedItem = Assert.Single(recentCompleted);
        Assert.Equal("Fincher's Fight Club", completedItem.TitleSnapshot);

        // 5. Active viewing query (Continue Watching live projection)
        viewingRepo.RecordSuccessfulPlay(Path.Combine(_directory, "Fight.Club.1999.mkv"), Now.AddHours(1));
        var activeItems = viewingRepo.ListActive(10);
        var activeItem = Assert.Single(activeItems);
        Assert.Equal("Fincher's Fight Club", activeItem.Title);

        // 6. Verify underlying watch_events table STILL holds the immutable canonical title snapshot!
        using (var connection = Open())
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT title_snapshot FROM watch_events WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", watchEvent.Id);
            var persistedHistoricalSnapshot = (string)cmd.ExecuteScalar()!;
            Assert.Equal("Fight Club", persistedHistoricalSnapshot);
        }

        // 7. Verify TV show watch event live projection
        int showId;
        int epId;
        using (var unit = new CatalogUnitOfWork(_connectionString))
        {
            var show = unit.TvShows.Create(
                new TvShowCatalogData("tmdb", "1396", "Breaking Bad", "Breaking Bad", 2008, "Overview", [], null, MetadataStatus.Ready),
                Now);
            showId = show.Id;
            var season = unit.TvSeasons.Create(showId, 1, "Season 1", null, Now);
            var episode = unit.TvEpisodes.Create(season.Id, 1, "Pilot", null, null, null, Now);
            epId = episode.Id;
            var file = unit.MediaFiles.Add(new VerifiedMediaFileFacts(
                Path.Combine(_directory, "BB.S01E01.mkv"), 1024, ".mkv", "1080p", "H264", "WEB", Now));
            unit.TvEpisodes.LinkMediaFile(epId, file.Id, Now);
            unit.Commit();
        }

        var epWatch = personal.AddEpisodeWatchEvent(epId, Now.AddHours(2), Now.AddHours(2));
        Assert.Equal("Breaking Bad", epWatch.ShowTitleSnapshot);

        using (var connection = Open())
        {
            new TvShowRepository(connection).SetCustomDisplayTitle(showId, "Breaking Bad: The Walter White Saga");
        }

        var recentWithShow = personal.ListRecentlyWatchedMedia(10);
        var showHistory = recentWithShow.First(r => r.ShowId == showId);
        Assert.Equal("Breaking Bad: The Walter White Saga", showHistory.ShowTitleSnapshot);

        // Historical snapshot in DB is preserved
        using (var connection = Open())
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT show_title_snapshot FROM watch_events WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", epWatch.Id);
            var persistedShowSnapshot = (string)cmd.ExecuteScalar()!;
            Assert.Equal("Breaking Bad", persistedShowSnapshot);
        }
    }

    [Fact]
    public void WatchHistory_DeletedMediaFallback_PreservesHistoricalSnapshot()
    {
        int movieId;
        using (var unit = new CatalogUnitOfWork(_connectionString))
        {
            var movie = unit.Movies.Create(
                new MovieCatalogData("tmdb", "550", "Fight Club", "Fight Club", 1999, "Overview", [], 139, 8.4, null, MetadataStatus.Ready),
                Now);
            movieId = movie.Id;
            unit.Commit();
        }

        var personal = new PersonalLibraryRepository(_connectionString);
        personal.AddWatchEvent(movieId, Now, Now);

        // Set custom title override
        using (var connection = Open())
        {
            new MovieRepository(connection).SetCustomDisplayTitle(movieId, "Custom Title");
        }

        // Delete the movie from catalog (simulating unavailable / removed media)
        using (var connection = Open())
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM movies WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", movieId);
            cmd.ExecuteNonQuery();
        }

        // Reading history gracefully falls back to historical snapshot and reports IsAvailable = false
        var recent = personal.ListRecentlyWatchedMedia(10);
        var item = Assert.Single(recent);
        Assert.False(item.IsAvailable);
        Assert.Equal("Fight Club", item.TitleSnapshot);
    }

    [Fact]
    public void CustomListMembers_ProjectsCustomDisplayTitle_AndRetainsCanonicalTitle()
    {
        int movieId;
        int showId;
        using (var unit = new CatalogUnitOfWork(_connectionString))
        {
            var movie = unit.Movies.Create(
                new MovieCatalogData("tmdb", "603", "The Matrix", "The Matrix", 1999, "Neo...", [], 136, 8.7, null, MetadataStatus.Ready),
                Now);
            movieId = movie.Id;
            var show = unit.TvShows.Create(
                new TvShowCatalogData("tmdb", "1399", "Game of Thrones", "Game of Thrones", 2011, "Westeros...", [], null, MetadataStatus.Ready),
                Now);
            showId = show.Id;

            unit.Movies.SetCustomDisplayTitle(movieId, "The Matrix (Classic)");
            unit.TvShows.SetCustomDisplayTitle(showId, "GoT (Complete)");
            unit.Commit();
        }

        var personal = new PersonalLibraryRepository(_connectionString);
        var listId = personal.CreateCustomList("Favorites", Now);
        personal.AddMovieToCustomList(listId, movieId, Now);
        personal.AddShowToCustomList(listId, showId, Now);

        var members = personal.ListCustomListItems(listId);
        Assert.Equal(2, members.Count);

        var movieMember = members.First(m => m.Kind == CustomListMediaKind.Movie);
        Assert.Equal("The Matrix (Classic)", movieMember.Title);
        Assert.Equal("The Matrix (Classic)", movieMember.CustomDisplayTitle);
        Assert.Equal("The Matrix", movieMember.CanonicalTitle);

        var showMember = members.First(m => m.Kind == CustomListMediaKind.Show);
        Assert.Equal("GoT (Complete)", showMember.Title);
        Assert.Equal("GoT (Complete)", showMember.CustomDisplayTitle);
        Assert.Equal("Game of Thrones", showMember.CanonicalTitle);
    }

    [Fact]
    public void TvContinueWatching_ProjectsCustomDisplayTitle()
    {
        int showId;
        using (var unit = new CatalogUnitOfWork(_connectionString))
        {
            var show = unit.TvShows.Create(
                new TvShowCatalogData("tmdb", "60059", "Better Call Saul", "Better Call Saul", 2015, "Overview", [], null, MetadataStatus.Ready),
                Now);
            showId = show.Id;
            unit.TvShows.SetCustomDisplayTitle(showId, "BCS (Pre-BB)");

            var season = unit.TvSeasons.Create(showId, 1, "Season 1", null, Now);
            var ep1 = unit.TvEpisodes.Create(season.Id, 1, "Uno", null, null, null, Now);
            var ep2 = unit.TvEpisodes.Create(season.Id, 2, "Mijo", null, null, null, Now);

            var f1 = unit.MediaFiles.Add(new VerifiedMediaFileFacts(
                Path.Combine(_directory, "BCS.S01E01.mkv"), 1024, ".mkv", "1080p", "H264", "WEB", Now));
            var f2 = unit.MediaFiles.Add(new VerifiedMediaFileFacts(
                Path.Combine(_directory, "BCS.S01E02.mkv"), 1024, ".mkv", "1080p", "H264", "WEB", Now));

            unit.TvEpisodes.LinkMediaFile(ep1.Id, f1.Id, Now);
            unit.TvEpisodes.LinkMediaFile(ep2.Id, f2.Id, Now);

            unit.Commit();
        }

        var personal = new PersonalLibraryRepository(_connectionString);
        // User watched episode 1
        using (var connection = Open())
        {
            var epRepo = new TvEpisodeRepository(connection);
            var allEps = epRepo.ListByShow(showId);
            var ep1 = allEps.First(e => e.Number == 1);
            personal.AddEpisodeWatchEvent(ep1.Id, Now, Now);
        }

        using (var connection = Open())
        {
            var tvService = new TvLibraryService(
                new TvShowRepository(connection),
                new TvSeasonRepository(connection),
                new TvEpisodeRepository(connection),
                personal);

            var continueItems = tvService.ListContinueWatching();
            var item = Assert.Single(continueItems);
            Assert.Equal("BCS (Pre-BB)", item.ShowTitle);
            Assert.Equal("BCS (Pre-BB)", item.CustomDisplayTitle);
            Assert.Equal("Better Call Saul", item.CanonicalTitle);
            Assert.Equal(2, item.EpisodeNumber); // Next episode to watch is ep 2
        }
    }

    [Fact]
    public void Rescan_PreservesCustomDisplayTitles_ForMoviesAndTvShows()
    {
        var factory = new CatalogUnitOfWorkFactory(_connectionString);
        var importService = new ImportService(factory, new NoProvider(), clock: () => Now);

        var moviePath = Path.Combine(_directory, "Interstellar.2014.mkv");
        File.WriteAllBytes(moviePath, [0x00]);

        var parsedMovie = new ParsedMedia(
            "Interstellar.2014.mkv",
            MediaType.Movie,
            "Interstellar",
            2014,
            "1080p",
            "BluRay",
            "x264",
            ".mkv");

        var movieReg = importService.ConfirmMovieImport(new ConfirmMovieImportCommand(
            moviePath, 1, parsedMovie, Now, null));
        var movieId = movieReg.Movie.Id;

        // Ingest TV show
        var tvPath = Path.Combine(_directory, "Chernobyl.S01E01.mkv");
        File.WriteAllBytes(tvPath, [0x00]);

        var parsedTv = new ParsedMedia(
            "Chernobyl.S01E01.mkv",
            MediaType.TvEpisode,
            "Chernobyl",
            2019,
            "1080p",
            "WEB",
            "x264",
            ".mkv",
            1,
            1);

        var epReg = importService.RegisterEpisodeImport(new ConfirmEpisodeImportCommand(
            tvPath, 1, parsedTv, Now, null));
        var showId = epReg.Show.Id;

        // Set custom display titles
        using (var connection = Open())
        {
            new MovieRepository(connection).SetCustomDisplayTitle(movieId, "Interstellar (Nolan 70mm IMAX)");
            new TvShowRepository(connection).SetCustomDisplayTitle(showId, "Chernobyl Mini-Series (2019)");
        }

        // SIMULATE RESCAN: scan the same files again
        var rescanMovie = importService.ConfirmMovieImport(new ConfirmMovieImportCommand(
            moviePath, 1, parsedMovie, Now.AddDays(1), null));
        Assert.True(rescanMovie.AlreadyRegistered);

        var rescanTv = importService.RegisterEpisodeImport(new ConfirmEpisodeImportCommand(
            tvPath, 1, parsedTv, Now.AddDays(1), null));
        Assert.True(rescanTv.AlreadyRegistered);

        // Verify that custom display titles survived the rescan without overwrite
        using (var connection = Open())
        {
            var movie = new MovieRepository(connection).GetById(movieId)!;
            Assert.Equal("Interstellar (Nolan 70mm IMAX)", movie.CustomDisplayTitle);
            Assert.Equal("Interstellar (Nolan 70mm IMAX)", movie.DisplayTitle);
            Assert.Equal("Interstellar", movie.Title);

            var show = new TvShowRepository(connection).GetById(showId)!;
            Assert.Equal("Chernobyl Mini-Series (2019)", show.CustomDisplayTitle);
            Assert.Equal("Chernobyl Mini-Series (2019)", show.DisplayTitle);
            Assert.Equal("Chernobyl", show.Title);
        }
    }

    [Fact]
    public async Task TraktLocalSnapshotBuilder_UsesCanonicalIdentity_UnaffectedByCustomDisplayTitle()
    {
        int movieId;
        using (var unit = new CatalogUnitOfWork(_connectionString))
        {
            var movie = unit.Movies.Create(
                new MovieCatalogData("tmdb", "550", "Fight Club", "Fight Club", 1999, "Overview", [], 139, 8.4, null, MetadataStatus.Ready),
                Now);
            movieId = movie.Id;
            unit.Movies.SetCustomDisplayTitle(movieId, "Fincher's Greatest Film");

            unit.MediaFiles.Add(new VerifiedMediaFileFacts(
                Path.Combine(_directory, "fc.mkv"), 1024, ".mkv", "1080p", "H264", "WEB", Now), movieId);
            unit.Commit();
        }

        using var connection = Open();
        var movieRepo = new MovieRepository(connection);
        var fileRepo = new MediaFileRepository(connection);
        var showRepo = new TvShowRepository(connection);
        var seasonRepo = new TvSeasonRepository(connection);
        var epRepo = new TvEpisodeRepository(connection);

        var builder = new TraktLocalSnapshotBuilder(movieRepo, fileRepo, showRepo, seasonRepo, epRepo, new TestAvailabilityInspector());
        var snapshot = await builder.BuildSnapshotAsync();

        var candidate = Assert.Single(snapshot.Candidates);
        Assert.Equal(movieId, candidate.LocalId);
        Assert.Equal(TraktMediaKind.Movie, candidate.Kind);
        // Canonical TMDB ID 550 is extracted
        Assert.Equal(550, candidate.Identity.TmdbId);

        // Custom display title has zero impact on identity matching
        var query = new TraktIdentity(TraktMediaKind.Movie, TmdbId: 550);
        var match = snapshot.Match(query);
        Assert.Equal(TraktMatchStatus.Matched, match.Status);
        Assert.Equal(movieId, match.Candidate?.LocalId);
    }

    private sealed class NoProvider : IMetadataProvider
    {
        public string ProviderName => "none";
        public IReadOnlyList<DropSort.Domain.Metadata.Contracts.MovieCandidate> Search(DropSort.Domain.Metadata.Contracts.MovieSearchQuery query) => [];
        public DropSort.Domain.Metadata.Contracts.MovieMetadata? GetMovie(string externalId) => null;
    }

    private sealed class TestAvailabilityInspector : IAvailabilityInspector
    {
        public AvailabilityInspection Inspect(string path) => new(path, AvailabilityInspectionStatus.Present);
        public bool IsStorageAvailable(string path) => true;
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
}
