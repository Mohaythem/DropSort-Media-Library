using System;
using System.IO;
using System.Linq;
using DropSort.Application.Dto;
using DropSort.Application.Services;
using DropSort.Domain.Library.Personal;
using DropSort.Infrastructure.Persistence.Migrations;
using DropSort.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DropSort.Tests.Infrastructure;

public sealed class Migration8WatchHistoryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"dropsort_watch8_{Guid.NewGuid():N}");
    private readonly string _databasePath;
    private readonly string _connectionString;

    public Migration8WatchHistoryTests()
    {
        Directory.CreateDirectory(_directory);
        _databasePath = Path.Combine(_directory, "history.db");
        _connectionString = $"Data Source={_databasePath}";
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    [Fact]
    public void Migration8_PreservesPopulatedMovieEvents_AndIsIdempotent()
    {
        new DatabaseMigrator(_connectionString).Migrate(7);
        using (var connection = Open())
        {
            Execute(connection, """
                INSERT INTO movies(id, provider, external_id, title, year, poster_path, backdrop_path, date_added, created_at, updated_at, genres, metadata_status)
                VALUES (11, 'tmdb', '11', 'Legacy Movie', 1999, '/poster.jpg', '/backdrop.jpg', '2026-01-01', '2026-01-01', '2026-01-01', '[]', 'READY');
                INSERT INTO watch_events(id, movie_id, watched_at, created_at) VALUES (42, 11, '2026-02-03T04:05:06.0000000+00:00', '2026-02-03T04:06:06.0000000+00:00');
                """);
        }

        new DatabaseMigrator(_connectionString).Migrate();
        new DatabaseMigrator(_connectionString).Migrate();
        var repository = new PersonalLibraryRepository(_connectionString);
        var item = Assert.Single(repository.ListRecent());
        Assert.Equal(42, item.Id);
        Assert.Equal("Legacy Movie", item.TitleSnapshot);
        Assert.Equal(1999, item.YearSnapshot);
        Assert.Equal("/backdrop.jpg", item.ArtworkReference);
        Assert.Equal("/poster.jpg", item.PosterReference);
        Assert.Equal(new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.Zero), item.WatchedAt);
        Assert.Equal(new DateTimeOffset(2026, 2, 3, 4, 6, 6, TimeSpan.Zero), item.CreatedAt);
        using var verified = Open();
        using var foreignKeyCheck = verified.CreateCommand();
        foreignKeyCheck.CommandText = "PRAGMA foreign_key_check;";
        Assert.Null(foreignKeyCheck.ExecuteScalar());
    }

    [Fact]
    public void MovieAndEpisodeHistory_PersistsAcrossRepositoryRestart_AndSurvivesDeletion()
    {
        new DatabaseMigrator(_connectionString).Migrate();
        using (var connection = Open()) SeedCatalog(connection);
        var first = new PersonalLibraryRepository(_connectionString);
        var movieEvent = first.AddWatchEvent(1, At(10), At(10));
        var episodeEvent = first.AddEpisodeWatchEvent(101, At(20), At(20));
        Assert.Equal("Pilot", episodeEvent.TitleSnapshot);
        Assert.Equal(2020, episodeEvent.YearSnapshot);
        Assert.Equal("/still1.jpg", episodeEvent.ArtworkReference);
        Assert.Equal("/showbackdrop.jpg", episodeEvent.BackdropReference);
        Assert.Equal("/showposter.jpg", episodeEvent.PosterReference);
        Assert.Contains(101, first.GetWatchedEpisodeIds(7));

        var restarted = new PersonalLibraryRepository(_connectionString);
        Assert.Equal(2, restarted.ListRecent().Count);
        Assert.Equal(episodeEvent.Id, restarted.ListRecent().First().Id);

        using (var connection = Open())
        {
            Execute(connection, "UPDATE media_files SET status = 'MISSING';");
        }
        Assert.All(restarted.ListRecent(), item => Assert.False(item.IsAvailable));

        using (var connection = Open())
        {
            Execute(connection, "DELETE FROM movies WHERE id = 1; DELETE FROM tv_shows WHERE id = 7;");
        }

        var afterRemoval = restarted.ListRecent();
        Assert.All(afterRemoval, item => Assert.False(item.IsAvailable));
        Assert.Contains(afterRemoval, item => item.Id == movieEvent.Id && item.MovieId is null && item.TitleSnapshot == "Movie");
        var removedEpisode = Assert.Single(afterRemoval, item => item.Id == episodeEvent.Id && item.EpisodeId is null && item.TitleSnapshot == "Pilot");
        Assert.Equal("/still1.jpg", removedEpisode.ArtworkReference);
        Assert.Equal("/showbackdrop.jpg", removedEpisode.BackdropReference);
        Assert.Equal("/showposter.jpg", removedEpisode.PosterReference);
    }

    [Fact]
    public void ContinueWatching_RequiresWatchedEpisode_AndChoosesNextPresentUnwatchedEpisode()
    {
        new DatabaseMigrator(_connectionString).Migrate();
        using (var connection = Open()) SeedCatalog(connection);
        var history = new PersonalLibraryRepository(_connectionString);
        using var connectionForReads = Open();
        var service = new TvLibraryService(
            new TvShowRepository(connectionForReads),
            new TvSeasonRepository(connectionForReads),
            new TvEpisodeRepository(connectionForReads), history);

        Assert.Empty(service.ListContinueWatching());
        history.AddEpisodeWatchEvent(101, At(10), At(10));
        var item = Assert.Single(service.ListContinueWatching());
        Assert.Equal(102, item.EpisodeId);
        Assert.Equal("Episode 2", item.EpisodeTitle);
        Assert.Equal("/still2.jpg", item.ArtworkReference);
        Assert.True(item.IsAvailable);
        Assert.Single(history.ListRecent());
    }

    [Fact]
    public void NeedsAttentionCount_OnlyCountsActionableStatuses()
    {
        var statuses = new[]
        {
            ImportItemStatus.Matched, ImportItemStatus.NeedsMatch, ImportItemStatus.NoMatchFound,
            ImportItemStatus.MetadataUnavailable, ImportItemStatus.MetadataError,
            ImportItemStatus.MetadataCancelled, ImportItemStatus.RegistrationFailed,
            ImportItemStatus.AlreadyInLibrary
        };
        var result = new ImportBatchResult(statuses.Select((status, index) => new ImportItemResult(
            $"file{index}", DropSort.Domain.Media.Discovery.DiscoveryClassification.MovieCandidate,
            false, false, Status: status)).ToList());
        Assert.Equal(6, result.NeedsAttentionCount);
    }

    [Fact]
    public void RecentHistory_OrdersFractionalTimestampsAcrossOffsets_NotInsertionIds()
    {
        new DatabaseMigrator(_connectionString).Migrate();
        using (var connection = Open()) SeedCatalog(connection);
        var history = new PersonalLibraryRepository(_connectionString);
        var later = history.AddWatchEvent(
            1,
            new DateTimeOffset(2025, 12, 31, 19, 0, 0, 900, TimeSpan.FromHours(-5)),
            At(1));
        var earlier = history.AddWatchEvent(
            1,
            new DateTimeOffset(2026, 1, 1, 2, 0, 0, 100, TimeSpan.FromHours(2)),
            At(2));

        Assert.True(later.Id < earlier.Id);
        Assert.Equal([later.Id, earlier.Id], history.ListRecent().Select(item => item.Id));
        Assert.Equal([earlier.Id, later.Id], history.GetWatchEvents(1).Select(item => item.Id));
        Assert.Equal(later.WatchedAt, history.GetState(1).LastWatched);
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var foreignKeys = connection.CreateCommand();
        foreignKeys.CommandText = "PRAGMA foreign_keys = ON;";
        foreignKeys.ExecuteNonQuery();
        return connection;
    }

    private static DateTimeOffset At(int minute) => new(2026, 1, 1, 0, minute, 0, TimeSpan.Zero);

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void SeedCatalog(SqliteConnection connection)
    {
        Execute(connection, """
            INSERT INTO movies(id, provider, external_id, title, date_added, created_at, updated_at, genres, metadata_status)
            VALUES (1, 'tmdb', '1', 'Movie', '2026-01-01', '2026-01-01', '2026-01-01', '[]', 'READY');
            INSERT INTO media_files(id, movie_id, current_path, path_key, file_size, status, discovered_at, last_seen_at)
            VALUES (1, 1, '/tmp/movie.mkv', '/tmp/movie.mkv', 10, 'PRESENT', '2026-01-01', '2026-01-01');
            INSERT INTO tv_shows(id, title, sort_title, year, date_added, created_at, updated_at, genres, metadata_status, backdrop_path, poster_path)
            VALUES (7, 'Show', 'show', 2020, '2026-01-01', '2026-01-01', '2026-01-01', '[]', 'PENDING', '/showbackdrop.jpg', '/showposter.jpg');
            INSERT INTO tv_seasons(id, show_id, season_number, created_at, updated_at) VALUES (70, 7, 1, '2026-01-01', '2026-01-01');
            INSERT INTO tv_episodes(id, season_id, episode_number, title, still_path, created_at, updated_at) VALUES
                (101, 70, 1, 'Pilot', '/still1.jpg', '2026-01-01', '2026-01-01'),
                (102, 70, 2, 'Episode 2', '/still2.jpg', '2026-01-01', '2026-01-01'),
                (103, 70, 3, 'Episode 3', '/still3.jpg', '2026-01-01', '2026-01-01');
            INSERT INTO media_files(id, current_path, path_key, file_size, status, discovered_at, last_seen_at) VALUES
                (2, '/tmp/episode2.mkv', '/tmp/episode2.mkv', 10, 'PRESENT', '2026-01-01', '2026-01-01'),
                (3, '/tmp/episode3.mkv', '/tmp/episode3.mkv', 10, 'MISSING', '2026-01-01', '2026-01-01');
            INSERT INTO episode_media_files(media_file_id, episode_id, linked_at) VALUES (2, 102, '2026-01-01'), (3, 103, '2026-01-01');
            """);
    }
}
