using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using TBDMovies.Application.Trakt;
using TBDMovies.Infrastructure.Persistence;
using TBDMovies.Infrastructure.Persistence.Migrations;
using TBDMovies.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Xunit;

namespace TBDMovies.Tests.Infrastructure;

/// <summary>
/// Disposable temp DB fixtures for TraktCacheRepository and migration 18 verification.
/// </summary>
public sealed class TraktCacheRepositoryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"tbdmovies_trakt_cache_{Guid.NewGuid():N}");
    private readonly string _path;
    private readonly string _cs;

    public TraktCacheRepositoryTests()
    {
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, "library.db");
        _cs = $"Data Source={_path}";
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); } catch { }
    }

    private void MigrateTo18() => new DatabaseMigrator(_cs).Migrate();

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_cs);
        connection.Open();
        return connection;
    }

    private object? Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private void Exec(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    [Fact]
    public void Account_keys_are_case_sensitive_and_summary_does_not_include_payload()
    {
        MigrateTo18();
        var repository = new TraktCacheRepository(_cs);
        var payload = System.Text.Json.JsonSerializer.Serialize(new string('x', 16 * 1024));
        repository.SaveComplete("Account_A", "history", payload, DateTimeOffset.UtcNow);
        Assert.Empty(repository.List("account_a"));
        var summary = Assert.Single(repository.List("Account_A"));
        Assert.Equal("history", summary.ResourceKey);
        Assert.Equal(payload, repository.Get("Account_A", summary.ResourceKey)!.PayloadJson);
        repository.DeleteAccount("account_a");
        Assert.Single(repository.List("Account_A"));
    }

    [Fact]
    public void SaveComplete_InsertsNewEntry_AtomicallyWithNullError()
    {
        MigrateTo18();
        var repository = new TraktCacheRepository(_cs);
        var now = DateTimeOffset.UtcNow;
        var payload = """[{"id":101,"title":"The Matrix","rank":1,"custom_field":"raw_unmatched"}]""";

        repository.SaveComplete("acc_123", "watchlist:movies", payload, now);

        var entry = repository.Get("acc_123", "watchlist:movies");
        Assert.NotNull(entry);
        Assert.Equal("acc_123", entry.AccountId);
        Assert.Equal("watchlist:movies", entry.ResourceKey);
        Assert.Equal(payload, entry.PayloadJson);
        Assert.Equal(now, entry.SucceededAt);
        Assert.Equal(now, entry.AttemptedAt);
        Assert.Null(entry.ErrorCode);
        Assert.Null(entry.ParsedErrorCode);
    }

    [Fact]
    public void SaveComplete_IsIdempotent_ReplacesSnapshotOrderAndUnknownFields()
    {
        MigrateTo18();
        var repository = new TraktCacheRepository(_cs);
        var t1 = DateTimeOffset.UtcNow.AddMinutes(-10);
        var payload1 = """[{"id":1,"order":1,"extra":"foo"},{"id":2,"order":2,"extra":"bar"}]""";
        repository.SaveComplete("acc_123", "lists", payload1, t1);

        var t2 = DateTimeOffset.UtcNow;
        // Invert order and alter extra field:
        var payload2 = """[{"id":2,"order":1,"extra":"bar_v2"},{"id":1,"order":2,"extra":"foo_v2"}]""";
        repository.SaveComplete("acc_123", "lists", payload2, t2);

        var entry = repository.Get("acc_123", "lists");
        Assert.NotNull(entry);
        Assert.Equal(payload2, entry.PayloadJson);
        Assert.Equal(t2, entry.SucceededAt);
        Assert.Equal(t2, entry.AttemptedAt);
        Assert.Null(entry.ErrorCode);

        // Verify only 1 row exists
        using var db = Open();
        Assert.Equal(1L, Scalar(db, "SELECT COUNT(*) FROM trakt_resource_cache WHERE account_id='acc_123' AND resource_key='lists'"));
    }

    [Fact]
    public void SaveComplete_ClearsPreviousErrorCode_WhenSavingAfterFailure()
    {
        MigrateTo18();
        var repository = new TraktCacheRepository(_cs);
        var t1 = DateTimeOffset.UtcNow.AddHours(-1);
        repository.RecordFailure("acc_123", "history:movies", TraktCacheErrorCode.RateLimited, t1);

        var failedEntry = repository.Get("acc_123", "history:movies");
        Assert.NotNull(failedEntry);
        Assert.Equal("RateLimited", failedEntry.ErrorCode);
        Assert.Equal(TraktCacheErrorCode.RateLimited, failedEntry.ParsedErrorCode);

        var t2 = DateTimeOffset.UtcNow;
        var goodPayload = """[{"action":"watch","id":999}]""";
        repository.SaveComplete("acc_123", "history:movies", goodPayload, t2);

        var recoveredEntry = repository.Get("acc_123", "history:movies");
        Assert.NotNull(recoveredEntry);
        Assert.Equal(goodPayload, recoveredEntry.PayloadJson);
        Assert.Equal(t2, recoveredEntry.SucceededAt);
        Assert.Equal(t2, recoveredEntry.AttemptedAt);
        Assert.Null(recoveredEntry.ErrorCode);
        Assert.Null(recoveredEntry.ParsedErrorCode);
    }

    [Fact]
    public void SaveComplete_ThrowsAndDoesNotOverwriteGoodCache_WhenJsonIsInvalid()
    {
        MigrateTo18();
        var repository = new TraktCacheRepository(_cs);
        var t1 = DateTimeOffset.UtcNow.AddMinutes(-5);
        var goodPayload = """{"status":"ok","items":[1,2,3]}""";
        repository.SaveComplete("acc_123", "lists", goodPayload, t1);

        var t2 = DateTimeOffset.UtcNow;
        var invalidPayload = "{malformed json: true, ";

        var ex = Assert.Throws<ArgumentException>(() =>
            repository.SaveComplete("acc_123", "lists", invalidPayload, t2));
        Assert.Contains("valid JSON", ex.Message);

        // Verify database is untouched and still contains good payload
        var entry = repository.Get("acc_123", "lists");
        Assert.NotNull(entry);
        Assert.Equal(goodPayload, entry.PayloadJson);
        Assert.Equal(t1, entry.SucceededAt);
    }

    [Fact]
    public void RecordFailure_OnFirstFailure_SetsNullPayloadAndNullSucceededAt()
    {
        MigrateTo18();
        var repository = new TraktCacheRepository(_cs);
        var now = DateTimeOffset.UtcNow;

        repository.RecordFailure("acc_123", "watched:movies", TraktCacheErrorCode.HttpError, now);

        var entry = repository.Get("acc_123", "watched:movies");
        Assert.NotNull(entry);
        Assert.Equal("acc_123", entry.AccountId);
        Assert.Equal("watched:movies", entry.ResourceKey);
        Assert.Null(entry.PayloadJson);
        Assert.Null(entry.SucceededAt);
        Assert.Equal(now, entry.AttemptedAt);
        Assert.Equal("HttpError", entry.ErrorCode);
        Assert.Equal(TraktCacheErrorCode.HttpError, entry.ParsedErrorCode);
    }

    [Fact]
    public void RecordFailure_OnSubsequentFailure_RetainsPriorPayloadAndSucceededAt()
    {
        MigrateTo18();
        var repository = new TraktCacheRepository(_cs);
        var successTime = DateTimeOffset.UtcNow.AddHours(-2);
        var priorPayload = """[{"id":55,"title":"Fight Club"}]""";
        repository.SaveComplete("acc_123", "watched:movies", priorPayload, successTime);

        var failureTime = DateTimeOffset.UtcNow;
        repository.RecordFailure("acc_123", "watched:movies", TraktCacheErrorCode.NetworkError, failureTime);

        var entry = repository.Get("acc_123", "watched:movies");
        Assert.NotNull(entry);
        // Prior payload and succeeded_at MUST be retained:
        Assert.Equal(priorPayload, entry.PayloadJson);
        Assert.Equal(successTime, entry.SucceededAt);
        // Attempted_at and error_code MUST be updated:
        Assert.Equal(failureTime, entry.AttemptedAt);
        Assert.Equal("NetworkError", entry.ErrorCode);
        Assert.Equal(TraktCacheErrorCode.NetworkError, entry.ParsedErrorCode);
    }

    [Fact]
    public void List_ReturnsAllEntriesForTargetAccount_SortedByKey()
    {
        MigrateTo18();
        var repository = new TraktCacheRepository(_cs);
        var now = DateTimeOffset.UtcNow;

        repository.SaveComplete("acc_A", "watchlist:shows", """{"shows":[]}""", now);
        repository.SaveComplete("acc_A", "lists", """{"lists":[]}""", now);
        repository.SaveComplete("acc_B", "lists", """{"lists":["other"]}""", now);

        var listA = repository.List("acc_A");
        Assert.Equal(2, listA.Count);
        Assert.Equal("lists", listA[0].ResourceKey);
        Assert.Equal("watchlist:shows", listA[1].ResourceKey);
        Assert.All(listA, item => Assert.Equal("acc_A", item.AccountId));

        var listB = repository.List("acc_B");
        Assert.Single(listB);
        Assert.Equal("acc_B", listB[0].AccountId);
        Assert.Equal("lists", listB[0].ResourceKey);
    }

    [Fact]
    public void DeleteAccount_DeletesOnlyTargetAccount_PreservesOtherAccounts()
    {
        MigrateTo18();
        var repository = new TraktCacheRepository(_cs);
        var now = DateTimeOffset.UtcNow;

        repository.SaveComplete("acc_1", "lists", """{"id":1}""", now);
        repository.SaveComplete("acc_1", "watched:movies", """{"id":2}""", now);
        repository.SaveComplete("acc_2", "lists", """{"id":3}""", now);

        repository.DeleteAccount("acc_1");

        Assert.Empty(repository.List("acc_1"));
        Assert.Null(repository.Get("acc_1", "lists"));

        var acc2List = repository.List("acc_2");
        Assert.Single(acc2List);
        Assert.Equal("acc_2", acc2List[0].AccountId);
        Assert.Equal("""{"id":3}""", repository.Get("acc_2", "lists")!.PayloadJson);
    }

    [Fact]
    public void Cancellation_ThrowsBeforeWrite_WhenTokenIsAlreadyCancelled()
    {
        MigrateTo18();
        var repository = new TraktCacheRepository(_cs);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            repository.SaveComplete("acc_1", "lists", "{}", DateTimeOffset.UtcNow, cts.Token));

        Assert.Throws<OperationCanceledException>(() =>
            repository.RecordFailure("acc_1", "lists", TraktCacheErrorCode.Timeout, DateTimeOffset.UtcNow, cts.Token));

        Assert.Throws<OperationCanceledException>(() =>
            repository.DeleteAccount("acc_1", cts.Token));

        // Confirm nothing was written
        Assert.Null(repository.Get("acc_1", "lists"));
    }

    [Fact]
    public void Validation_Throws_WhenAccountOrKeyIsBlankOrInvalidEnum()
    {
        MigrateTo18();
        var repository = new TraktCacheRepository(_cs);
        var now = DateTimeOffset.UtcNow;

        Assert.Throws<ArgumentException>(() => repository.Get("", "lists"));
        Assert.Throws<ArgumentException>(() => repository.Get("  ", "lists"));
        Assert.Throws<ArgumentException>(() => repository.Get("acc", ""));
        Assert.Throws<ArgumentException>(() => repository.Get("acc", "   "));

        Assert.Throws<ArgumentException>(() => repository.SaveComplete("", "lists", "{}", now));
        Assert.Throws<ArgumentException>(() => repository.SaveComplete("acc", "", "{}", now));
        Assert.Throws<ArgumentException>(() => repository.SaveComplete("acc", "lists", "", now));

        Assert.Throws<ArgumentException>(() => repository.RecordFailure("acc", "lists", TraktCacheErrorCode.None, now));
        Assert.Throws<ArgumentException>(() => repository.RecordFailure("acc", "lists", (TraktCacheErrorCode)999, now));

        Assert.Throws<ArgumentException>(() => repository.DeleteAccount(""));
        Assert.Throws<ArgumentException>(() => repository.DeleteAccount("   "));
    }

    [Fact]
    public void CacheOperations_DoNotMutateLocalCatalog_OrWatchedState()
    {
        MigrateTo18();
        using (var db = Open())
        {
            // Seed a local movie
            Exec(db, """
                INSERT INTO movies (id, provider, external_id, title, date_added, created_at, updated_at, genres, metadata_status)
                VALUES (1, 'tmdb', '100', 'Inception', '2026-01-01', '2026-01-01', '2026-01-01', '[]', 'READY');
                """);
        }

        var repository = new TraktCacheRepository(_cs);
        var now = DateTimeOffset.UtcNow;

        repository.SaveComplete("acc_1", "history:movies", """[{"movie":{"title":"Inception","ids":{"tmdb":100}}}]""", now);
        repository.RecordFailure("acc_1", "watchlist:movies", TraktCacheErrorCode.RateLimited, now);

        using (var db = Open())
        {
            // Verify no extra movies, no media files, no watch events, no active viewings
            Assert.Equal(1L, Scalar(db, "SELECT COUNT(*) FROM movies"));
            Assert.Equal(0L, Scalar(db, "SELECT COUNT(*) FROM media_files"));
            Assert.Equal(0L, Scalar(db, "SELECT COUNT(*) FROM watch_events"));
            Assert.Equal(0L, Scalar(db, "SELECT COUNT(*) FROM active_viewings"));
        }
    }

    [Fact]
    public void ClearCatalog_DoesNotMutateTraktCache()
    {
        MigrateTo18();
        using (var db = Open())
        {
            Exec(db, """
                INSERT INTO movies (id, provider, external_id, title, date_added, created_at, updated_at, genres, metadata_status)
                VALUES (1, 'tmdb', '100', 'Inception', '2026-01-01', '2026-01-01', '2026-01-01', '[]', 'READY');
                INSERT INTO settings (key, value, updated_at) VALUES ('test_key', 'test_val', '2026-01-01');
                """);
        }

        var repository = new TraktCacheRepository(_cs);
        var now = DateTimeOffset.UtcNow;
        repository.SaveComplete("acc_1", "lists", """[{"name":"Custom List 1"}]""", now);

        // Run existing local ClearCatalog
        var maintenance = new LibraryMaintenanceRepository(_cs);
        var cleared = maintenance.ClearCatalog();

        Assert.Equal(1, cleared.Movies);

        // Trakt cache MUST remain intact:
        var cacheEntry = repository.Get("acc_1", "lists");
        Assert.NotNull(cacheEntry);
        Assert.Equal("""[{"name":"Custom List 1"}]""", cacheEntry.PayloadJson);
    }

    [Fact]
    public void Upgrade17To18_PreservesExistingCatalogAndSettings_AndVerifiesBackup()
    {
        // 1. Establish v17 database
        new DatabaseMigrator(_cs).Migrate(17);
        using (var db = Open())
        {
            Exec(db, """
                INSERT INTO movies (id, provider, external_id, title, date_added, created_at, updated_at, genres, metadata_status)
                VALUES (1, 'tmdb', '550', 'Fight Club', '2026-01-01', '2026-01-01', '2026-01-01', '[]', 'READY');
                INSERT INTO settings (key, value, updated_at) VALUES ('ui_theme', 'dark', '2026-01-01');
                INSERT INTO custom_lists (id, display_name, created_at) VALUES (1, 'Favorites', '2026-01-01');
                INSERT INTO custom_list_members (id, list_id, media_kind, movie_id, show_id, added_at)
                VALUES (1, 1, 'MOVIE', 1, NULL, '2026-01-01');
                """);
        }

        // 2. Perform forward migration to 18
        new DatabaseMigrator(_cs).Migrate();

        var backup = Assert.Single(Directory.GetFiles(DatabaseSafetyBackup.BackupDirectoryFor(_cs), "library.*.db"));
        Assert.True(DatabaseSafetyBackup.VerifyBackup(backup, out var backupVersion));
        Assert.Equal(17, backupVersion);
        using (var saved = new SqliteConnection($"Data Source={backup};Mode=ReadOnly;Pooling=False"))
        {
            saved.Open();
            Assert.Equal("Fight Club", Scalar(saved, "SELECT title FROM movies WHERE id=1"));
            Assert.Equal(1L, Scalar(saved, "SELECT COUNT(*) FROM custom_list_members"));
        }

        // 3. Verify user_version is at latest version (>= 18)
        using (var db = Open())
        {
            Assert.Equal((long)DatabaseMigrator.LatestVersion, Scalar(db, "PRAGMA user_version"));
            Assert.Equal("Fight Club", Scalar(db, "SELECT title FROM movies WHERE id=1"));
            Assert.Equal("dark", Scalar(db, "SELECT value FROM settings WHERE key='ui_theme'"));
            Assert.Equal("Favorites", Scalar(db, "SELECT display_name FROM custom_lists WHERE id=1"));
            Assert.Equal(1L, Scalar(db, "SELECT COUNT(*) FROM custom_list_members WHERE id=1"));

            // trakt_resource_cache exists and is empty
            Assert.Equal(0L, Scalar(db, "SELECT COUNT(*) FROM trakt_resource_cache"));

            // Schema validation helper checks
            new DatabaseMigrator(_cs).Migrate();
        }
    }

    [Fact]
    public void SchemaValidation_CatchesMissingTable()
    {
        MigrateTo18();
        using (var db = Open())
        {
            // Drop trakt_resource_cache to simulate schema corruption / missing table
            Exec(db, "DROP TABLE trakt_resource_cache;");

            var ex = Assert.Throws<InvalidOperationException>(() => new DatabaseMigrator(_cs).Migrate());
            Assert.Contains("trakt_resource_cache", ex.Message);
            Assert.Contains("missing", ex.Message);
        }
    }

}
