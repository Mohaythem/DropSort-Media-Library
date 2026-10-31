using System;
using System.Linq;
using System.IO;
using TBDMovies.Domain.Library.Personal;
using TBDMovies.Infrastructure.Persistence.Migrations;
using TBDMovies.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Xunit;

namespace TBDMovies.Tests.Infrastructure;

/// <summary>
/// Custom lists and their movie membership on the multi-list model. A custom list has a stable id and a
/// display-only name; renaming keeps the id and the movies. A movie can belong to any number of lists,
/// cannot be duplicated inside one, survives a repository reload, and can be removed from one list without
/// affecting the others. Deleting a movie cascades out of every list; deleting a list removes only its
/// rows. Watchlist and Favorites (movie_personal_state) are independent and untouched.
/// </summary>
public sealed class CustomListMembershipTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"tbdmovies_lists_{Guid.NewGuid():N}");
    private readonly string _connectionString;

    public CustomListMembershipTests()
    {
        Directory.CreateDirectory(_directory);
        _connectionString = $"Data Source={Path.Combine(_directory, "library.db")}";
        new DatabaseMigrator(_connectionString).Migrate();
        using var connection = Open();
        Seed(connection, 1, "Movie A");
        Seed(connection, 2, "Movie B");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); } catch { }
    }

    private PersonalLibraryRepository Repo() => new(_connectionString);
    private static DateTimeOffset Now => DateTimeOffset.UtcNow;

    // APPEND_TESTS
    [Fact]
    public void CreateMultipleLists_HaveUniqueStableIds_AndSurviveReload()
    {
        var repo = Repo();
        var weekend = repo.CreateCustomList("Weekend", Now);
        var horror = repo.CreateCustomList("Horror", Now);

        Assert.NotEqual(weekend, horror);

        var lists = Repo().ListCustomLists(); // fresh repository (restart)
        Assert.Equal(2, lists.Count);
        Assert.Contains(lists, list => list.Id == weekend && list.Name == "Weekend");
        Assert.Contains(lists, list => list.Id == horror && list.Name == "Horror");
    }

    [Fact]
    public void Rename_PreservesIdAndMembership()
    {
        var repo = Repo();
        var listId = repo.CreateCustomList("Weekend", Now);
        repo.AddMovieToCustomList(listId, 1, Now);

        repo.RenameCustomList(listId, "Weekend Movies", Now);

        var lists = Repo().ListCustomLists();
        Assert.Contains(lists, list => list.Id == listId && list.Name == "Weekend Movies");
        Assert.Single(Repo().ListCustomListItems(listId)); // membership kept across the rename
    }

    [Fact]
    public void AddMovieToList_IsIdempotent_AndSurvivesReload()
    {
        var repo = Repo();
        var listId = repo.CreateCustomList("Weekend", Now);
        repo.AddMovieToCustomList(listId, 1, Now);
        repo.AddMovieToCustomList(listId, 1, Now); // duplicate: no-op

        Assert.Contains(listId, repo.GetCustomListIdsForMovie(1));
        Assert.Single(repo.ListCustomListItems(listId));
        Assert.Single(Repo().ListCustomListItems(listId)); // fresh repository (restart)
    }

    [Fact]
    public void MovieCanBelongToMultipleLists()
    {
        var repo = Repo();
        var weekend = repo.CreateCustomList("Weekend", Now);
        var classics = repo.CreateCustomList("Classics", Now);
        repo.AddMovieToCustomList(weekend, 1, Now);
        repo.AddMovieToCustomList(classics, 1, Now);

        var ids = repo.GetCustomListIdsForMovie(1);
        Assert.Contains(weekend, ids);
        Assert.Contains(classics, ids);
    }

    [Fact]
    public void RemoveMembership_LeavesOtherListsIntact()
    {
        var repo = Repo();
        var weekend = repo.CreateCustomList("Weekend", Now);
        var classics = repo.CreateCustomList("Classics", Now);
        repo.AddMovieToCustomList(weekend, 1, Now);
        repo.AddMovieToCustomList(classics, 1, Now);

        repo.RemoveMovieFromCustomList(weekend, 1);

        Assert.DoesNotContain(weekend, repo.GetCustomListIdsForMovie(1));
        Assert.Contains(classics, repo.GetCustomListIdsForMovie(1));
    }

    [Fact]
    public void DeleteList_RemovesOnlyThatListsMembership()
    {
        var repo = Repo();
        var weekend = repo.CreateCustomList("Weekend", Now);
        var classics = repo.CreateCustomList("Classics", Now);
        repo.AddMovieToCustomList(weekend, 1, Now);
        repo.AddMovieToCustomList(weekend, 2, Now);
        repo.AddMovieToCustomList(classics, 1, Now);

        repo.DeleteCustomList(weekend);

        Assert.DoesNotContain(repo.ListCustomLists(), list => list.Id == weekend);
        Assert.Single(repo.ListCustomListItems(classics)); // untouched
        Assert.Contains(classics, repo.GetCustomListIdsForMovie(1)); // the movie itself survives
    }

    [Fact]
    public void DeletingMovie_CascadesOutOfEveryList()
    {
        var repo = Repo();
        var weekend = repo.CreateCustomList("Weekend", Now);
        repo.AddMovieToCustomList(weekend, 1, Now);

        using (var connection = Open())
        {
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM movies WHERE id = 1;";
            command.ExecuteNonQuery();
        }

        Assert.Empty(repo.ListCustomListItems(weekend));
    }

    [Fact]
    public void CustomListMembership_IsIndependentOfWatchlistAndFavorites()
    {
        var repo = Repo();
        var weekend = repo.CreateCustomList("Weekend", Now);
        repo.AddToWatchlist(1, Now);                          // Watchlist
        repo.SetPreference(1, PersonalPreference.Liked, Now); // Favorites
        repo.AddMovieToCustomList(weekend, 1, Now);

        var state = repo.GetState(1);
        Assert.True(state.IsWatchlisted);
        Assert.Equal(PersonalPreference.Liked, state.Preference);
        Assert.Single(repo.ListCustomListItems(weekend));
        Assert.Contains(weekend, repo.GetCustomListIdsForMovie(1));
    }

    [Fact]
    public void ListCustomLists_ReportsItemCount()
    {
        var repo = Repo();
        var weekend = repo.CreateCustomList("Weekend", Now);
        repo.AddMovieToCustomList(weekend, 1, Now);
        repo.AddMovieToCustomList(weekend, 2, Now);

        var list = repo.ListCustomLists().Single(item => item.Id == weekend);
        Assert.Equal(2, list.ItemCount);
    }

    [Fact]
    public void CreateCustomList_RejectsDuplicateAndEquivalentNames()
    {
        var repo = Repo();
        repo.CreateCustomList("Weekend", Now);
        repo.CreateCustomList("Épica", Now);

        Assert.Throws<TBDMovies.Application.DuplicateCustomListNameException>(
            () => repo.CreateCustomList("Weekend", Now));
        Assert.Throws<TBDMovies.Application.DuplicateCustomListNameException>(
            () => repo.CreateCustomList("weekend", Now));
        Assert.Throws<TBDMovies.Application.DuplicateCustomListNameException>(
            () => repo.CreateCustomList("  Weekend  ", Now));
        // SQLite lower() would not fold É/é; the .NET OrdinalIgnoreCase comparison must:
        Assert.Throws<TBDMovies.Application.DuplicateCustomListNameException>(
            () => repo.CreateCustomList("épica", Now));
    }

    [Fact]
    public void RenameCustomList_RejectsDuplicateAndEquivalentOfAnotherList()
    {
        var repo = Repo();
        var weekend = repo.CreateCustomList("Weekend", Now);
        repo.CreateCustomList("Épica", Now);
        repo.CreateCustomList("Horror", Now);

        Assert.Throws<TBDMovies.Application.DuplicateCustomListNameException>(
            () => repo.RenameCustomList(weekend, "ÉPICA", Now));
        Assert.Throws<TBDMovies.Application.DuplicateCustomListNameException>(
            () => repo.RenameCustomList(weekend, "horror", Now));
    }

    [Fact]
    public void RenameCustomList_ToUniqueName_Succeeds_AndKeepsIdentityAndMembership()
    {
        var repo = Repo();
        var weekend = repo.CreateCustomList("Weekend", Now);
        repo.CreateCustomList("Horror", Now);
        repo.AddMovieToCustomList(weekend, 1, Now);

        repo.RenameCustomList(weekend, "Weekend Movies", Now);

        var reloaded = Repo();
        Assert.Contains(reloaded.ListCustomLists(), list => list.Id == weekend && list.Name == "Weekend Movies");
        Assert.Single(reloaded.ListCustomListItems(weekend)); // membership preserved across the rename
    }

    [Fact]
    public void RenameCustomList_ToItsOwnCurrentName_IsAllowed()
    {
        var repo = Repo();
        var weekend = repo.CreateCustomList("Weekend", Now);
        repo.RenameCustomList(weekend, "Weekend", Now); // not a duplicate of another list
        Assert.Contains(repo.ListCustomLists(), list => list.Id == weekend && list.Name == "Weekend");
    }

    [Fact]
    public void Show_CanBelongToMultipleLists_AndMixWithMovies()
    {
        var repo = Repo();
        using (var connection = Open()) SeedShow(connection, 10, "Show X");
        var weekend = repo.CreateCustomList("Weekend", Now);
        var classics = repo.CreateCustomList("Classics", Now);

        repo.AddShowToCustomList(weekend, 10, Now);
        repo.AddShowToCustomList(classics, 10, Now);
        repo.AddMovieToCustomList(weekend, 1, Now);

        Assert.Contains(weekend, repo.GetCustomListIdsForShow(10));
        Assert.Contains(classics, repo.GetCustomListIdsForShow(10));

        // Weekend now holds one movie and one show together.
        var weekendItems = repo.ListCustomListItems(weekend);
        Assert.Equal(2, weekendItems.Count);
        Assert.Contains(weekendItems, item => item.Kind == TBDMovies.Domain.Library.Personal.CustomListMediaKind.Movie && item.Id == 1);
        Assert.Contains(weekendItems, item => item.Kind == TBDMovies.Domain.Library.Personal.CustomListMediaKind.Show && item.Id == 10);
    }

    [Fact]
    public void AddShow_IsIdempotent()
    {
        var repo = Repo();
        using (var connection = Open()) SeedShow(connection, 10, "Show X");
        var weekend = repo.CreateCustomList("Weekend", Now);
        repo.AddShowToCustomList(weekend, 10, Now);
        repo.AddShowToCustomList(weekend, 10, Now); // duplicate: no-op

        Assert.Single(repo.ListCustomListItems(weekend));
    }

    [Fact]
    public void RemoveShow_AffectsOnlyThatList()
    {
        var repo = Repo();
        using (var connection = Open()) SeedShow(connection, 10, "Show X");
        var weekend = repo.CreateCustomList("Weekend", Now);
        var classics = repo.CreateCustomList("Classics", Now);
        repo.AddShowToCustomList(weekend, 10, Now);
        repo.AddShowToCustomList(classics, 10, Now);

        repo.RemoveShowFromCustomList(weekend, 10);

        Assert.DoesNotContain(weekend, repo.GetCustomListIdsForShow(10));
        Assert.Contains(classics, repo.GetCustomListIdsForShow(10));
    }

    [Fact]
    public void DeletingShow_CascadesOutOfEveryList()
    {
        var repo = Repo();
        using (var connection = Open()) SeedShow(connection, 10, "Show X");
        var weekend = repo.CreateCustomList("Weekend", Now);
        repo.AddShowToCustomList(weekend, 10, Now);

        using (var connection = Open())
        {
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM tv_shows WHERE id = 10;";
            command.ExecuteNonQuery();
        }

        Assert.Empty(repo.ListCustomListItems(weekend));
    }

    [Fact]
    public void Rename_PreservesMovieAndShowMemberships()
    {
        var repo = Repo();
        using (var connection = Open()) SeedShow(connection, 10, "Show X");
        var weekend = repo.CreateCustomList("Weekend", Now);
        repo.AddMovieToCustomList(weekend, 1, Now);
        repo.AddShowToCustomList(weekend, 10, Now);

        repo.RenameCustomList(weekend, "Weekend Picks", Now);

        Assert.Equal(2, Repo().ListCustomListItems(weekend).Count);
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

    private static void Seed(SqliteConnection connection, int id, string title)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO movies(id, provider, external_id, title, date_added, created_at, updated_at, genres, metadata_status)
            VALUES ($id, 'tmdb', $ext, $title, '2026-01-01', '2026-01-01', '2026-01-01', '[]', 'READY');
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$ext", id.ToString());
        command.Parameters.AddWithValue("$title", title);
        command.ExecuteNonQuery();
    }

    private static void SeedShow(SqliteConnection connection, int id, string title)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO tv_shows(id, provider, external_id, title, sort_title, date_added, created_at, updated_at, genres, metadata_status)
            VALUES ($id, 'tmdb', $ext, $title, $sort, '2026-01-01', '2026-01-01', '2026-01-01', '[]', 'READY');
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$ext", "s" + id);
        command.Parameters.AddWithValue("$title", title);
        command.Parameters.AddWithValue("$sort", title.ToLowerInvariant());
        command.ExecuteNonQuery();
    }
}
