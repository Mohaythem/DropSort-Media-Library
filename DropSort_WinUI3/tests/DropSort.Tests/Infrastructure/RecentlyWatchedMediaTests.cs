using System;
using System.Linq;
using System.IO;
using DropSort.Domain.Library.Personal;
using DropSort.Infrastructure.Persistence.Migrations;
using DropSort.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DropSort.Tests.Infrastructure;

/// <summary>
/// Guards the "Recently watched" read (Home): it must show each logical movie (by movie_id) and each
/// logical TV show (by its parent show_id) once, represented by that item's latest watch event and
/// ordered by it - not one card per raw replay, and never one card per episode. The raw watch_events
/// history is preserved untouched (ListRecent still returns every event); only ListRecentlyWatchedMedia
/// collapses the view. Watching different episodes or seasons of one show shares a single show card.
/// </summary>
public sealed class RecentlyWatchedMediaTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"dropsort_recentmedia_{Guid.NewGuid():N}");
    private readonly string _connectionString;

    public RecentlyWatchedMediaTests()
    {
        Directory.CreateDirectory(_directory);
        _connectionString = $"Data Source={Path.Combine(_directory, "recent.db")}";
        new DatabaseMigrator(_connectionString).Migrate();
        using var connection = Open();
        SeedCatalog(connection);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    [Fact]
    public void RepeatedMovie_CollapsesToOneCard_UsingNewestEvent()
    {
        var repository = new PersonalLibraryRepository(_connectionString);
        repository.AddWatchEvent(1, At(10), At(10));
        var newest = repository.AddWatchEvent(1, At(30), At(30));
        repository.AddWatchEvent(1, At(20), At(20));

        var card = Assert.Single(repository.ListRecentlyWatchedMedia());
        Assert.Equal(1, card.MovieId);
        Assert.Equal(newest.Id, card.Id);
        Assert.Equal(At(30), card.WatchedAt);

        // The raw history is preserved: every replay is still stored, only the read collapses.
        Assert.Equal(3, repository.ListRecent().Count);
    }

    [Fact]
    public void DifferentMovies_RemainSeparateCards_NewestFirst()
    {
        var repository = new PersonalLibraryRepository(_connectionString);
        repository.AddWatchEvent(1, At(10), At(10));
        repository.AddWatchEvent(2, At(20), At(20));

        Assert.Equal([2, 1], repository.ListRecentlyWatchedMedia().Select(item => item.MovieId));
    }

    [Fact]
    public void RepeatedSameEpisode_CollapsesToOneShowCard()
    {
        var repository = new PersonalLibraryRepository(_connectionString);
        repository.AddEpisodeWatchEvent(101, At(10), At(10));
        repository.AddEpisodeWatchEvent(101, At(30), At(30));

        var card = Assert.Single(repository.ListRecentlyWatchedMedia());
        Assert.Equal(WatchEventMediaKind.Episode, card.MediaKind);
        Assert.Equal(7, card.ShowId);

        // Raw episode history is intact - both replays remain stored.
        Assert.Equal(2, repository.ListRecent().Count);
    }

    [Fact]
    public void DifferentEpisodesOfSameSeason_CollapseToOneShowCard()
    {
        var repository = new PersonalLibraryRepository(_connectionString);
        repository.AddEpisodeWatchEvent(101, At(10), At(10));
        repository.AddEpisodeWatchEvent(102, At(20), At(20));

        var card = Assert.Single(repository.ListRecentlyWatchedMedia());
        Assert.Equal(7, card.ShowId);
        Assert.Equal(2, repository.ListRecent().Count);
    }

    [Fact]
    public void EpisodesAcrossSeasonsOfSameShow_CollapseToOneShowCard_UsingNewestState()
    {
        var repository = new PersonalLibraryRepository(_connectionString);
        repository.AddEpisodeWatchEvent(101, At(10), At(10)); // S1E1
        var newest = repository.AddEpisodeWatchEvent(111, At(30), At(30)); // S2E1, watched later

        var card = Assert.Single(repository.ListRecentlyWatchedMedia());
        Assert.Equal(7, card.ShowId);
        Assert.Equal(newest.Id, card.Id);
        Assert.Equal(At(30), card.WatchedAt);
        // The card reflects the most recently watched season/episode state (Season 2, Episode 1).
        Assert.Equal(2, card.SeasonNumberSnapshot);
        Assert.Equal(1, card.EpisodeNumberSnapshot);
    }

    [Fact]
    public void DifferentShows_RemainSeparateCards_NewestFirst()
    {
        var repository = new PersonalLibraryRepository(_connectionString);
        repository.AddEpisodeWatchEvent(101, At(10), At(10)); // Show 7
        repository.AddEpisodeWatchEvent(201, At(20), At(20)); // Show 8

        Assert.Equal([8, 7], repository.ListRecentlyWatchedMedia().Select(item => item.ShowId));
    }

    [Fact]
    public void MovieAndShow_OrderByEachItemsLatestWatchEvent()
    {
        var repository = new PersonalLibraryRepository(_connectionString);
        repository.AddWatchEvent(1, At(10), At(10));          // movie 1, first watch
        repository.AddEpisodeWatchEvent(101, At(20), At(20)); // show 7
        var latestMovie = repository.AddWatchEvent(1, At(30), At(30)); // movie 1 rewatched - newest overall

        var media = repository.ListRecentlyWatchedMedia();

        Assert.Equal(2, media.Count);
        Assert.Equal(1, media[0].MovieId);
        Assert.Equal(latestMovie.Id, media[0].Id);
        Assert.Equal(7, media[1].ShowId);
    }

    [Fact]
    public void RawEpisodeHistory_RemainsIntact_WhileHomeCollapsesToOneShowCard()
    {
        var repository = new PersonalLibraryRepository(_connectionString);
        repository.AddEpisodeWatchEvent(101, At(10), At(10));
        repository.AddEpisodeWatchEvent(101, At(20), At(20));
        repository.AddEpisodeWatchEvent(102, At(30), At(30));

        Assert.Single(repository.ListRecentlyWatchedMedia());
        // Every individual episode watch event survives for the TV details history.
        Assert.Equal(3, repository.ListRecent().Count);
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

    private static void SeedCatalog(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO movies(id, provider, external_id, title, date_added, created_at, updated_at, genres, metadata_status)
            VALUES (1, 'tmdb', '1', 'Movie A', '2026-01-01', '2026-01-01', '2026-01-01', '[]', 'READY'),
                   (2, 'tmdb', '2', 'Movie B', '2026-01-01', '2026-01-01', '2026-01-01', '[]', 'READY');
            INSERT INTO tv_shows(id, title, sort_title, year, date_added, created_at, updated_at, genres, metadata_status)
            VALUES (7, 'Show One', 'show one', 2020, '2026-01-01', '2026-01-01', '2026-01-01', '[]', 'PENDING'),
                   (8, 'Show Two', 'show two', 2021, '2026-01-01', '2026-01-01', '2026-01-01', '[]', 'PENDING');
            INSERT INTO tv_seasons(id, show_id, season_number, created_at, updated_at) VALUES
                (70, 7, 1, '2026-01-01', '2026-01-01'),
                (71, 7, 2, '2026-01-01', '2026-01-01'),
                (80, 8, 1, '2026-01-01', '2026-01-01');
            INSERT INTO tv_episodes(id, season_id, episode_number, title, created_at, updated_at) VALUES
                (101, 70, 1, 'Pilot', '2026-01-01', '2026-01-01'),
                (102, 70, 2, 'Episode 2', '2026-01-01', '2026-01-01'),
                (111, 71, 1, 'Premiere S2', '2026-01-01', '2026-01-01'),
                (201, 80, 1, 'Other Pilot', '2026-01-01', '2026-01-01');
            """;
        command.ExecuteNonQuery();
    }
}
