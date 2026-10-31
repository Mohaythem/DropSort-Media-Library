using System;
using System.IO;
using System.Linq;
using TBDMovies.Infrastructure.Persistence.Migrations;
using TBDMovies.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Xunit;

namespace TBDMovies.Tests.Infrastructure;

/// <summary>
/// The v11 -> v12 forward migration promotes custom lists from the single fixed settings slot to real,
/// independently identified lists. It must preserve the previously stored list name and any membership the
/// old single-slot implementation created, create a default list when membership exists without a name,
/// reach the latest version on a clean database, and be safe to run again.
/// </summary>
public sealed class CustomListMigrationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"tbdmovies_migr_{Guid.NewGuid():N}");
    private readonly string _connectionString;

    public CustomListMigrationTests()
    {
        Directory.CreateDirectory(_directory);
        _connectionString = $"Data Source={Path.Combine(_directory, "library.db")}";
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); } catch { }
    }

    // APPEND_MIGRATION_TESTS
    [Fact]
    public void CleanDatabase_ReachesLatestVersion_WithNewTables()
    {
        new DatabaseMigrator(_connectionString).Migrate();

        Assert.Equal(DatabaseMigrator.LatestVersion, UserVersion());
        Assert.True(TableExists("custom_lists"));
        Assert.True(TableExists("custom_list_members"));
        Assert.False(TableExists("custom_list_movies")); // the old single-slot table is gone
    }

    [Fact]
    public void Migrating_v11_to_v12_PreservesTheStoredListNameAndMembership()
    {
        new DatabaseMigrator(_connectionString).Migrate(11);
        using (var connection = Open())
        {
            SeedMovie(connection, 1);
            SetSetting(connection, "ui_custom_lists", "[\"Thrillers\"]");
            SeedOldMembership(connection, "thrillers", 1);
        }

        new DatabaseMigrator(_connectionString).Migrate();

        var repo = new PersonalLibraryRepository(_connectionString);
        var lists = repo.ListCustomLists();
        var thrillers = Assert.Single(lists);
        Assert.Equal("Thrillers", thrillers.Name);
        Assert.Single(repo.ListCustomListItems(thrillers.Id)); // the old membership carried over
    }

    [Fact]
    public void Migrating_v11_to_v12_ImportsEveryStoredNameInOrder()
    {
        new DatabaseMigrator(_connectionString).Migrate(11);
        using (var connection = Open())
        {
            SeedMovie(connection, 1);
            SetSetting(connection, "ui_custom_lists", "[\"Weekend\",\"Horror\"]");
            SeedOldMembership(connection, "thrillers", 1);
        }

        new DatabaseMigrator(_connectionString).Migrate();

        var repo = new PersonalLibraryRepository(_connectionString);
        var lists = repo.ListCustomLists();
        Assert.Equal(2, lists.Count);
        Assert.Equal("Weekend", lists[0].Name);
        Assert.Equal("Horror", lists[1].Name);
        // The single old membership slot attaches to the first imported list.
        Assert.Single(repo.ListCustomListItems(lists[0].Id));
        Assert.Empty(repo.ListCustomListItems(lists[1].Id));
    }

    [Fact]
    public void Migrating_v11_to_v12_CreatesDefaultList_WhenMembershipHasNoStoredName()
    {
        new DatabaseMigrator(_connectionString).Migrate(11);
        using (var connection = Open())
        {
            SeedMovie(connection, 1);
            SeedOldMembership(connection, "thrillers", 1); // membership but no ui_custom_lists setting
        }

        new DatabaseMigrator(_connectionString).Migrate();

        var repo = new PersonalLibraryRepository(_connectionString);
        var list = Assert.Single(repo.ListCustomLists());
        Assert.Single(repo.ListCustomListItems(list.Id)); // the movie is not lost
    }

    [Fact]
    public void Migrating_v11_to_v12_IgnoresMalformedSettingsJson()
    {
        new DatabaseMigrator(_connectionString).Migrate(11);
        using (var connection = Open())
        {
            SetSetting(connection, "ui_custom_lists", "{not valid json");
        }

        new DatabaseMigrator(_connectionString).Migrate(); // must not throw

        Assert.Empty(new PersonalLibraryRepository(_connectionString).ListCustomLists());
    }

    [Fact]
    public void Migrating_IsIdempotent_OnceAtLatestVersion()
    {
        new DatabaseMigrator(_connectionString).Migrate(11);
        using (var connection = Open())
        {
            SeedMovie(connection, 1);
            SetSetting(connection, "ui_custom_lists", "[\"Thrillers\"]");
            SeedOldMembership(connection, "thrillers", 1);
        }

        new DatabaseMigrator(_connectionString).Migrate();
        new DatabaseMigrator(_connectionString).Migrate(); // no-op: already at latest

        var repo = new PersonalLibraryRepository(_connectionString);
        Assert.Single(repo.ListCustomLists());
        Assert.Single(repo.ListCustomListItems(repo.ListCustomLists()[0].Id));
    }

    [Fact]
    public void Migrating_v12_to_v13_PreservesMovieMembership_AndAllowsShows()
    {
        new DatabaseMigrator(_connectionString).Migrate(12);
        int listId;
        using (var connection = Open())
        {
            SeedMovie(connection, 1);
            SeedShow(connection, 10);
            listId = SeedV12List(connection, "Weekend");
            SeedV12MovieMembership(connection, listId, 1);
        }

        new DatabaseMigrator(_connectionString).Migrate(); // -> 13 (mixed media)

        var repo = new PersonalLibraryRepository(_connectionString);
        var items = repo.ListCustomListItems(listId);
        var movie = Assert.Single(items); // the v12 movie membership carried over
        Assert.Equal(1, movie.Id);
        Assert.Equal(TBDMovies.Domain.Library.Personal.CustomListMediaKind.Movie, movie.Kind);

        // The v13 capability: a TV show can now join the same list.
        repo.AddShowToCustomList(listId, 10, DateTimeOffset.UtcNow);
        Assert.Equal(2, repo.ListCustomListItems(listId).Count);
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    private int UserVersion()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private bool TableExists(string name)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
        command.Parameters.AddWithValue("$name", name);
        return Convert.ToInt32(command.ExecuteScalar()) == 1;
    }

    private static void SeedMovie(SqliteConnection connection, int id)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO movies(id, provider, external_id, title, date_added, created_at, updated_at, genres, metadata_status)
            VALUES ($id, 'tmdb', $ext, $title, '2026-01-01', '2026-01-01', '2026-01-01', '[]', 'READY');
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$ext", id.ToString());
        command.Parameters.AddWithValue("$title", "Movie " + id);
        command.ExecuteNonQuery();
    }

    private static void SetSetting(SqliteConnection connection, string key, string value)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO settings(key, value, updated_at) VALUES ($k, $v, '2026-01-01');";
        command.Parameters.AddWithValue("$k", key);
        command.Parameters.AddWithValue("$v", value);
        command.ExecuteNonQuery();
    }

    private static void SeedOldMembership(SqliteConnection connection, string listKey, int movieId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO custom_list_movies(list_key, movie_id, added_at) VALUES ($key, $movie, '2026-01-02');
            """;
        command.Parameters.AddWithValue("$key", listKey);
        command.Parameters.AddWithValue("$movie", movieId);
        command.ExecuteNonQuery();
    }

    private static void SeedShow(SqliteConnection connection, int id)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO tv_shows(id, provider, external_id, title, sort_title, date_added, created_at, updated_at, genres, metadata_status)
            VALUES ($id, 'tmdb', $ext, $title, $sort, '2026-01-01', '2026-01-01', '2026-01-01', '[]', 'READY');
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$ext", "s" + id);
        command.Parameters.AddWithValue("$title", "Show " + id);
        command.Parameters.AddWithValue("$sort", "show " + id);
        command.ExecuteNonQuery();
    }

    private static int SeedV12List(SqliteConnection connection, string name)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO custom_lists(display_name, created_at) VALUES ($name, '2026-01-01T00:00:00.000Z');
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$name", name);
        return Convert.ToInt32((long)command.ExecuteScalar()!);
    }

    private static void SeedV12MovieMembership(SqliteConnection connection, int listId, int movieId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO custom_list_members(list_id, movie_id, added_at) VALUES ($list, $movie, '2026-01-02');
            """;
        command.Parameters.AddWithValue("$list", listId);
        command.Parameters.AddWithValue("$movie", movieId);
        command.ExecuteNonQuery();
    }
}
