using DropSort.Application.Services;
using DropSort.Domain.Library.Personal;
using DropSort.Infrastructure.Persistence.Migrations;
using DropSort.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DropSort.Tests.Application;

public sealed class ViewingActivityTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"dropsort_activity_{Guid.NewGuid():N}");
    private readonly string _cs;
    private readonly DateTimeOffset _t0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private string MoviePath => Path.Combine(_dir, "movie.mkv");
    private string Movie4KPath => Path.Combine(_dir, "movie-4k.mkv");
    private string PilotPath => Path.Combine(_dir, "pilot.mkv");
    private string SecondPath => Path.Combine(_dir, "second.mkv");

    public ViewingActivityTests()
    {
        Directory.CreateDirectory(_dir);
        _cs = $"Data Source={Path.Combine(_dir, "library.db")}";
        new DatabaseMigrator(_cs).Migrate();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void MoviePlayIsIdempotentAndUsesLatestStartedAt()
    {
        SeedMovie(1, "Movie One", 2020, MoviePath);
        var service = Service();
        var first = service.RecordSuccessfulPlay(MoviePath);
        var second = new ViewingActivityService(new ViewingActivityRepository(_cs), () => _t0.AddHours(2))
            .RecordSuccessfulPlay(MoviePath);

        Assert.Equal(1, service.ListActive().Count);
        Assert.Equal(first.MovieId, second.MovieId);
        Assert.Equal(_t0.AddHours(2), second.StartedAt);
        Assert.Equal("Movie One", second.Title);
        Assert.Equal(2020, second.Year);
    }

    [Fact]
    public void EpisodePlayCarriesShowAndEpisodeMetadataAndOrderIsNewestFirst()
    {
        SeedShowAndEpisode(7, 70, 101, "Pilot", PilotPath);
        SeedShowAndEpisode(7, 70, 102, "Second", SecondPath, 2);
        var service = Service();
        var older = service.RecordSuccessfulPlay(PilotPath);
        var newer = new ViewingActivityService(new ViewingActivityRepository(_cs), () => _t0.AddMinutes(3))
            .RecordSuccessfulPlay(SecondPath);

        Assert.Equal(7, older.ShowId);
        Assert.Equal(1, older.EpisodeNumber);
        Assert.Equal("Pilot", older.EpisodeTitle);
        Assert.Equal(7, newer.ShowId);
        Assert.Equal(2, newer.EpisodeNumber);
        Assert.Equal([newer.EpisodeId, older.EpisodeId], service.ListActive().Select(x => x.EpisodeId));
    }

    [Fact]
    public void ReplayingOneCardUpdatesTheSameRowAndDoesNotCreateAnother()
    {
        SeedMovie(1, "Movie One", 2020, MoviePath);
        var repository = new ViewingActivityRepository(_cs);
        repository.RecordSuccessfulPlay(MoviePath, _t0);
        repository.RecordSuccessfulPlay(MoviePath, _t0.AddMinutes(9));
        Assert.Single(repository.ListActive(10));
        Assert.Equal(_t0.AddMinutes(9), repository.ListActive(10)[0].StartedAt);
    }

    [Fact]
    public void MultipleEditionsOfOneMovieStillProduceOneActiveCard()
    {
        SeedMovie(1, "Movie One", 2020, MoviePath);
        Exec($"INSERT INTO media_files(id,movie_id,current_path,path_key,file_size,status,discovered_at,last_seen_at) VALUES (99,1,'{Movie4KPath}','{Key(Movie4KPath)}',1,'PRESENT','2026-01-01','2026-01-01');");
        var activity = Service();
        activity.RecordSuccessfulPlay(MoviePath);
        var latest = new ViewingActivityService(new ViewingActivityRepository(_cs), () => _t0.AddMinutes(2))
            .RecordSuccessfulPlay(Movie4KPath);
        Assert.Single(activity.ListActive());
        Assert.Equal(Movie4KPath, latest.Path);
    }

    [Fact]
    public void MarkWatchedAppendsHistoryAndClearsActiveAtomically()
    {
        SeedMovie(1, "Movie One", 2020, MoviePath);
        var activity = Service();
        activity.RecordSuccessfulPlay(MoviePath);
        var history = new PersonalLibraryRepository(_cs);
        var eventRow = history.AddWatchEvent(1, _t0.AddHours(1), _t0.AddHours(1));

        Assert.Equal(1, eventRow.MovieId);
        Assert.Empty(activity.ListActive());
        Assert.Single(activity.ListRecentCompleted());
        history.AddWatchEvent(1, _t0.AddHours(2), _t0.AddHours(2));
        Assert.Equal(2, history.GetWatchEvents(1).Count);
    }

    [Fact]
    public void MarkWatchedEpisodeClearsOnlyThatEpisodeAndClearActiveIsIdempotent()
    {
        SeedShowAndEpisode(7, 70, 101, "Pilot", PilotPath);
        SeedShowAndEpisode(7, 70, 102, "Second", SecondPath, 2);
        var activity = Service();
        activity.RecordSuccessfulPlay(PilotPath);
        activity.RecordSuccessfulPlay(SecondPath);
        new PersonalLibraryRepository(_cs).AddEpisodeWatchEvent(101, _t0, _t0);
        Assert.Single(activity.ListActive());
        Assert.Equal(1, ClearActive());
        Assert.Equal(0, ClearActive());
    }

    [Fact]
    public void RecentCompletedDeduplicatesEachMovieAndEpisodeByLatestWatch()
    {
        SeedMovie(1, "Movie One", 2020, MoviePath);
        SeedShowAndEpisode(7, 70, 101, "Pilot", PilotPath);
        SeedShowAndEpisode(7, 70, 102, "Second", SecondPath, 2);
        var history = new PersonalLibraryRepository(_cs);
        history.AddWatchEvent(1, _t0, _t0);
        history.AddWatchEvent(1, _t0.AddMinutes(3), _t0.AddMinutes(3));
        history.AddEpisodeWatchEvent(101, _t0.AddMinutes(1), _t0.AddMinutes(1));
        history.AddEpisodeWatchEvent(102, _t0.AddMinutes(4), _t0.AddMinutes(4));

        var recent = Service().ListRecentCompleted();
        Assert.Equal(3, recent.Count);
        Assert.Equal(102, recent[0].EpisodeId);
        Assert.Equal(1, recent[1].MovieId);
        Assert.Equal(101, recent[2].EpisodeId);
        Assert.Equal(_t0.AddMinutes(4), recent[0].WatchedAt);
        Assert.Equal(_t0.AddMinutes(3), recent[1].WatchedAt);
        Assert.Equal(_t0.AddMinutes(1), recent[2].WatchedAt);
    }

    [Fact]
    public void RecentCompletedKeepsOrphansDistinctAndReportsAvailability()
    {
        SeedMovie(1, "Movie One", 2020, MoviePath);
        var history = new PersonalLibraryRepository(_cs);
        history.AddWatchEvent(1, _t0, _t0);
        Exec("UPDATE media_files SET status='MISSING';");
        var item = Assert.Single(Service().ListRecentCompleted());
        Assert.False(item.IsAvailable);
        Exec("DELETE FROM movies WHERE id=1;");
        var orphan = Assert.Single(Service().ListRecentCompleted());
        Assert.Null(orphan.MovieId);
        Assert.False(orphan.IsAvailable);
    }

    [Fact]
    public void RecentCompletedEmptyDatabaseReturnsEmptyForAnyPage()
    {
        var history = new PersonalLibraryRepository(_cs);
        Assert.Empty(Service().ListRecentCompleted());
        Assert.Empty(Service().ListRecentCompleted(8, 3));
    }

    [Fact]
    public void UnboundedActivityReadsIncludeMoreThanHomePreviewLimit()
    {
        var history = new PersonalLibraryRepository(_cs);
        for (var id = 1; id <= 12; id++)
        {
            var path = Path.Combine(_dir, $"movie-{id}.mkv");
            SeedMovie(id, $"Movie {id}", 2020 + id, path);
            Service().RecordSuccessfulPlay(path);
        }

        for (var id = 13; id <= 24; id++)
        {
            var path = Path.Combine(_dir, $"movie-{id}.mkv");
            SeedMovie(id, $"Movie {id}", 2020 + id, path);
            history.AddWatchEvent(id, _t0.AddMinutes(id), _t0.AddMinutes(id));
        }

        Assert.Equal(12, Service().ListActive().Count);
        Assert.Equal(12, Service().ListActive(int.MaxValue).Count);
        Assert.Equal(12, Service().ListRecentCompleted(int.MaxValue).Count);
    }

    [Fact]
    public void ActivityAndHistoryPersistAcrossRepositoryRestart()
    {
        SeedMovie(1, "Movie One", 2020, MoviePath);
        Service().RecordSuccessfulPlay(MoviePath);
        Assert.Single(new ViewingActivityRepository(_cs).ListActive(10));
        Assert.Single(new ViewingActivityService(new ViewingActivityRepository(_cs)).ListActive());
    }

    [Fact]
    public void ReplayAfterWatchedCreatesActivityWithoutChangingHistoryUntilMarkedAgain()
    {
        SeedMovie(1, "Movie One", 2020, MoviePath);
        var activity = Service();
        var history = new PersonalLibraryRepository(_cs);
        history.AddWatchEvent(1, _t0, _t0);
        activity.RecordSuccessfulPlay(MoviePath);
        Assert.Single(activity.ListActive());
        Assert.Single(history.GetWatchEvents(1));
        history.AddWatchEvent(1, _t0.AddMinutes(5), _t0.AddMinutes(5));
        Assert.Empty(activity.ListActive());
        Assert.Equal(2, history.GetWatchEvents(1).Count);
    }

    [Fact]
    public void ClearWatchHistoryReturnsCountClearsActiveAndPreservesPreferences()
    {
        SeedMovie(1, "Movie One", 2020, MoviePath);
        var activity = Service();
        activity.RecordSuccessfulPlay(MoviePath);
        var history = new PersonalLibraryRepository(_cs);
        history.SetPreference(1, PersonalPreference.Liked, _t0);
        history.AddWatchEvent(1, _t0, _t0);
        Assert.Equal(1, history.ClearWatchHistory());
        Assert.Empty(history.ListRecent());
        Assert.Empty(activity.ListActive());
        Assert.Equal(PersonalPreference.Liked, history.GetState(1).Preference);
    }

    [Fact]
    public void ClearCatalogRemovesActivityAndCatalogRows()
    {
        SeedMovie(1, "Movie One", 2020, MoviePath);
        Service().RecordSuccessfulPlay(MoviePath);
        new PersonalLibraryRepository(_cs).AddWatchEvent(1, _t0, _t0);
        new LibraryMaintenanceRepository(_cs).ClearCatalog();
        Assert.Empty(Service().ListActive());
        Assert.Empty(Service().ListRecentCompleted());
        Assert.Equal(0L, Scalar("SELECT COUNT(*) FROM media_files;"));
    }

    [Fact]
    public void Migration15To16PreservesCatalogHistoryAndAddsActivitySchema()
    {
        var path = Path.Combine(_dir, "legacy.db");
        var cs = $"Data Source={path}";
        new DatabaseMigrator(cs).Migrate(15);
        using (var db = Open(cs))
        {
            Exec(db, "INSERT INTO movies(id,provider,external_id,title,year,date_added,created_at,updated_at,genres,metadata_status) VALUES (1,'tmdb','1','Legacy',2020,'2026-01-01','2026-01-01','2026-01-01','[]','READY');");
            Exec(db, "INSERT INTO media_files(id,movie_id,current_path,path_key,file_size,status,discovered_at,last_seen_at) VALUES (1,1,'/media/legacy.mkv','/media/legacy.mkv',1,'PRESENT','2026-01-01','2026-01-01');");
            Exec(db, "INSERT INTO tv_shows(id,provider,external_id,title,sort_title,year,date_added,created_at,updated_at,genres,metadata_status) VALUES (7,'tmdb','s7','Legacy Show','legacy show',2020,'2026-01-01','2026-01-01','2026-01-01','[]','READY'); INSERT INTO tv_seasons(id,show_id,season_number,created_at,updated_at) VALUES (70,7,1,'2026-01-01','2026-01-01'); INSERT INTO tv_episodes(id,season_id,episode_number,title,created_at,updated_at) VALUES (101,70,1,'Pilot','2026-01-01','2026-01-01'); INSERT INTO custom_lists(id,display_name,created_at) VALUES (8,'Keep','2026-01-01'); INSERT INTO custom_list_members(id,list_id,media_kind,show_id,added_at) VALUES (80,8,'SHOW',7,'2026-01-01'); INSERT INTO watch_events(id,media_kind,movie_id,watched_at,created_at,title_snapshot) VALUES (4,'MOVIE',1,'2026-01-02','2026-01-02','Legacy');");
        }
        new DatabaseMigrator(cs).Migrate(16);
        Assert.Equal(16L, Scalar(cs, "PRAGMA user_version;"));
        Assert.Equal(1L, Scalar(cs, "SELECT COUNT(*) FROM watch_events WHERE id=4;"));
        Assert.Equal(1L, Scalar(cs, "SELECT COUNT(*) FROM tv_episodes WHERE id=101 AND season_id=70;"));
        Assert.Equal(1L, Scalar(cs, "SELECT COUNT(*) FROM custom_list_members WHERE id=80 AND show_id=7;"));
        Assert.Equal(0L, Scalar(cs, "SELECT COUNT(*) FROM active_viewings;"));
        Assert.Equal("ok", Scalar(cs, "PRAGMA integrity_check;"));
    }

    [Fact]
    public void FailedWatchInsertRollsBackHistoryAndActiveCleanup()
    {
        SeedMovie(1, "Movie One", 2020, MoviePath);
        Service().RecordSuccessfulPlay(MoviePath);
        Exec("CREATE TRIGGER fail_watch BEFORE INSERT ON watch_events BEGIN SELECT RAISE(ABORT, 'test failure'); END;");
        Assert.Throws<SqliteException>(() => new PersonalLibraryRepository(_cs).AddWatchEvent(1, _t0, _t0));
        Assert.Single(Service().ListActive());
        Assert.Empty(Service().ListRecentCompleted());
    }

    private ViewingActivityService Service() => new(new ViewingActivityRepository(_cs), () => _t0);
    private int ClearActive()
    {
        using var db = Open(_cs); using var command = db.CreateCommand(); command.CommandText = "DELETE FROM active_viewings;"; return command.ExecuteNonQuery();
    }

    private void SeedMovie(int id, string title, int year, string path)
    {
        Exec($"INSERT INTO movies(id,provider,external_id,title,year,date_added,created_at,updated_at,genres,metadata_status,backdrop_path,poster_path) VALUES ({id},'tmdb','m{id}','{title}',{year},'2026-01-01','2026-01-01','2026-01-01','[]','READY','/backdrop.jpg','/poster.jpg');");
        Exec($"INSERT INTO media_files(id,movie_id,current_path,path_key,file_size,status,discovered_at,last_seen_at) VALUES ({id},{id},'{path}','{Key(path)}',1,'PRESENT','2026-01-01','2026-01-01');");
    }

    private void SeedShowAndEpisode(int showId, int seasonId, int episodeId, string title, string path, int number = 1)
    {
        if (Scalar("SELECT COUNT(*) FROM tv_shows WHERE id=" + showId) is 0L)
            Exec($"INSERT INTO tv_shows(id,provider,external_id,title,sort_title,year,date_added,created_at,updated_at,genres,metadata_status,backdrop_path,poster_path) VALUES ({showId},'tmdb','s{showId}','Show','show',2020,'2026-01-01','2026-01-01','2026-01-01','[]','READY','/show-backdrop.jpg','/show-poster.jpg'); INSERT INTO tv_seasons(id,show_id,season_number,created_at,updated_at) VALUES ({seasonId},{showId},1,'2026-01-01','2026-01-01');");
        Exec($"INSERT INTO tv_episodes(id,season_id,episode_number,title,still_path,created_at,updated_at) VALUES ({episodeId},{seasonId},{number},'{title}','/still.jpg','2026-01-01','2026-01-01'); INSERT INTO media_files(id,current_path,path_key,file_size,status,discovered_at,last_seen_at) VALUES ({episodeId},'{path}','{Key(path)}',1,'PRESENT','2026-01-01','2026-01-01'); INSERT INTO episode_media_files(media_file_id,episode_id,linked_at) VALUES ({episodeId},{episodeId},'2026-01-01');");
    }

    private void Exec(string sql) { using var db = Open(_cs); Exec(db, sql); }
    private static string Key(string path) => Path.GetFullPath(path).ToUpperInvariant();
    private static void Exec(SqliteConnection db, string sql) { using var command = db.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery(); }
    private object? Scalar(string sql) { using var db = Open(_cs); return Scalar(db, sql); }
    private static object? Scalar(string cs, string sql) { using var db = Open(cs); return Scalar(db, sql); }
    private static object? Scalar(SqliteConnection db, string sql) { using var command = db.CreateCommand(); command.CommandText = sql; return command.ExecuteScalar(); }
    private static SqliteConnection Open(string cs) { var db = new SqliteConnection(cs); db.Open(); using var command = db.CreateCommand(); command.CommandText = "PRAGMA foreign_keys=ON;"; command.ExecuteNonQuery(); return db; }
}
