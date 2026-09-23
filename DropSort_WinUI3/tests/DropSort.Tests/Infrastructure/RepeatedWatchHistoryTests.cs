using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DropSort.Domain.Library.Personal;
using DropSort.Infrastructure.Persistence.Migrations;
using DropSort.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DropSort.Tests.Infrastructure;

/// <summary>
/// Guards the repeated-watch contract behind Movie Details -> Mark Watched. The v15 oracle is
/// unambiguous: every click performs an unconditional INSERT, there is no same-day dedupe/guard/toggle,
/// and Movie Details grows by exactly one history row per click (the earliest event stays "First watch",
/// every later one is a derived "Rewatch"). Marking the same movie watched three times on the SAME
/// calendar day must therefore leave three distinct, correctly ordered, correctly labelled events - the
/// exact state (three rows differing only by label and id) the details list must render.
///
/// These assertions run at the layer the UI render consumes: the repository persists and derives the
/// events, and the projection Movie Details uses is a newest-first ordering plus the label rule replayed
/// here verbatim. The render itself (an ItemsRepeater re-seeded on every reload) is validated manually.
/// This test fails if a future change ever collapses, dedupes or guards same-day repeats before they
/// reach the UI - the wrong way to "fix" the reported symptom.
/// </summary>
public sealed class RepeatedWatchHistoryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"dropsort_repeatwatch_{Guid.NewGuid():N}");
    private readonly string _connectionString;

    public RepeatedWatchHistoryTests()
    {
        Directory.CreateDirectory(_directory);
        _connectionString = $"Data Source={Path.Combine(_directory, "repeat.db")}";
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    [Fact]
    public void ThreeSameDayMarkWatched_ProduceThreeDistinctRows_NewestFirstWithDerivedRewatchLabels()
    {
        new DatabaseMigrator(_connectionString).Migrate();
        using (var connection = Open()) SeedMovie(connection);
        var repository = new PersonalLibraryRepository(_connectionString);

        // Three rapid clicks on the same day: identical displayed date, distinct sub-second timestamps,
        // exactly what MarkWatchedButton_Click -> RecordWatch(DateTimeOffset.Now) does per click.
        var day = new DateTimeOffset(2026, 3, 1, 20, 0, 0, TimeSpan.Zero);
        var first = repository.AddWatchEvent(1, day.AddMilliseconds(100), day.AddMilliseconds(100));
        var second = repository.AddWatchEvent(1, day.AddMilliseconds(200), day.AddMilliseconds(200));
        var third = repository.AddWatchEvent(1, day.AddMilliseconds(300), day.AddMilliseconds(300));

        // A: every click persists its own row - three distinct ids, nothing collapsed or replaced.
        Assert.Equal(3, new[] { first.Id, second.Id, third.Id }.Distinct().Count());
        Assert.All(new[] { first.Id, second.Id, third.Id }, id => Assert.True(id > 0));

        // B: the summary the store derives counts every occurrence and reports the latest as last watched.
        var state = repository.GetState(1);
        Assert.Equal(3, state.WatchCount);
        Assert.Equal(third.WatchedAt, state.LastWatched);

        // Ascending (as stored): earliest is the first watch, the two later ones are rewatches.
        var ascending = repository.GetWatchEvents(1);
        Assert.Equal(3, ascending.Count);
        Assert.Equal(new[] { first.Id, second.Id, third.Id }, ascending.Select(e => e.Id));
        Assert.Equal(new[] { false, true, true }, ascending.Select(e => e.Rewatch));

        // Newest-first, exactly as LibraryService.ListWatchEvents orders the details list.
        var listed = ascending.OrderByDescending(e => e.WatchedAt).ToList();
        Assert.Equal(new[] { third.Id, second.Id, first.Id }, listed.Select(e => e.Id));

        // Replay LibraryProjection.ToWatchHistory's label rule (Rewatch || index < count-1) verbatim.
        var labels = listed
            .Select((e, index) => e.Rewatch || index < listed.Count - 1 ? "Rewatch" : "FirstWatch")
            .ToList();
        Assert.Equal(new[] { "Rewatch", "Rewatch", "FirstWatch" }, labels);

        // The condition that must render as three rows: same displayed day, distinct event ids per row.
        var dates = listed.Select(e => e.WatchedAt.ToLocalTime().ToString("MMM d, yyyy")).Distinct().ToList();
        Assert.Single(dates);
        Assert.Equal(3, listed.Select(e => e.Id).Distinct().Count());
    }

    [Fact]
    public void RepeatedMarkWatched_PersistAcrossRepositoryRestart_AndKeepGrowing()
    {
        new DatabaseMigrator(_connectionString).Migrate();
        using (var connection = Open()) SeedMovie(connection);
        var day = new DateTimeOffset(2026, 5, 4, 9, 0, 0, TimeSpan.Zero);

        var first = new PersonalLibraryRepository(_connectionString);
        first.AddWatchEvent(1, day.AddMinutes(1), day.AddMinutes(1));
        first.AddWatchEvent(1, day.AddMinutes(2), day.AddMinutes(2));

        // Re-opening Movie Details (a fresh repository) still sees both, and another click makes three.
        var reopened = new PersonalLibraryRepository(_connectionString);
        Assert.Equal(2, reopened.GetWatchEvents(1).Count);
        reopened.AddWatchEvent(1, day.AddMinutes(3), day.AddMinutes(3));
        Assert.Equal(3, reopened.GetState(1).WatchCount);
        Assert.Equal(3, reopened.GetWatchEvents(1).Select(e => e.Id).Distinct().Count());
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

    private static void SeedMovie(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO movies(id, provider, external_id, title, date_added, created_at, updated_at, genres, metadata_status)
            VALUES (1, 'tmdb', '1', 'Movie', '2026-01-01', '2026-01-01', '2026-01-01', '[]', 'READY');
            """;
        command.ExecuteNonQuery();
    }
}
