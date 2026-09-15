using DropSort.Application.Repositories;
using DropSort.Application.Services;
using Xunit;
using DropSort.Infrastructure.Persistence.Migrations;
using DropSort.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;

namespace DropSort.Tests.Application;

public sealed class SessionCredentialRegressionTests
{
    [Fact]
    public void Sqlite_restart_removes_only_legacy_key_and_session_use_never_inserts_it()
    {
        var directory = Directory.CreateTempSubdirectory("dropsort_session_");
        var connectionString = $"Data Source={Path.Combine(directory.FullName, "session.db")};Pooling=False";
        try
        {
            new DatabaseMigrator(connectionString).Migrate();
            var repository = new SettingsRepository(connectionString);
            repository.Set("tmdb_read_access_token", "synthetic-legacy", DateTimeOffset.UtcNow);
            repository.Set("ui_language", "ar", DateTimeOffset.UtcNow);
            var first = new SettingsService(new Maintenance(), settings: repository);
            Assert.False(first.IsTmdbConfigured());
            first.ApplyTmdbSessionToken("synthetic-session");
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT count(*) FROM settings WHERE key = 'tmdb_read_access_token'";
            Assert.Equal(0L, command.ExecuteScalar());
            command.CommandText = "PRAGMA user_version";
            Assert.Equal((long)DatabaseMigrator.LatestVersion, command.ExecuteScalar());
            var restarted = new SettingsService(new Maintenance(), settings: new SettingsRepository(connectionString));
            Assert.False(restarted.IsTmdbConfigured());
            Assert.Equal("ar", repository.Get("ui_language"));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Session_token_is_not_saved_or_restored_after_restart()
    {
        var settings = new MemorySettings();
        var first = new SettingsService(new Maintenance(), settings: settings);
        Assert.True(first.ApplyTmdbSessionToken(" synthetic-session-token "));
        Assert.Equal("synthetic-session-token", first.GetTmdbToken());
        Assert.Empty(settings.Values);
        var restarted = new SettingsService(new Maintenance(), settings: settings);
        Assert.False(restarted.IsTmdbConfigured());
        Assert.Null(restarted.GetTmdbToken());
        Assert.True(first.ClearTmdbSessionToken());
        Assert.False(first.IsTmdbConfigured());
    }

    [Fact]
    public void Legacy_accidental_plaintext_is_removed_without_restoring_it_or_other_settings()
    {
        var settings = new MemorySettings();
        settings.Values["tmdb_read_access_token"] = "synthetic-legacy-token";
        settings.Values["ui_language"] = "ar";
        settings.Values["ui_movie_folder"] = "D:\\fixture";
        var service = new SettingsService(new Maintenance(), settings: settings);
        Assert.False(service.IsTmdbConfigured());
        Assert.False(settings.Values.ContainsKey("tmdb_read_access_token"));
        Assert.Equal("ar", settings.Values["ui_language"]);
        Assert.Equal("D:\\fixture", settings.Values["ui_movie_folder"]);
    }

    [Fact]
    public void Explicit_initial_token_and_clear_are_session_only()
    {
        var settings = new MemorySettings();
        var service = new SettingsService(new Maintenance(), settings: settings, initialTmdbToken: " explicit ");
        Assert.Equal("explicit", service.GetTmdbToken());
        Assert.False(service.ApplyTmdbSessionToken("  "));
        Assert.Equal("explicit", service.GetTmdbToken());
        Assert.True(service.ClearTmdbSessionToken());
        Assert.False(service.ClearTmdbSessionToken());
        Assert.Empty(settings.Values);
    }

    private sealed class MemorySettings : ISettingsRepository
    {
        public Dictionary<string, string> Values { get; } = [];
        public string? Get(string key) => Values.GetValueOrDefault(key);
        public void Set(string key, string value, DateTimeOffset updatedAt) => Values[key] = value;
        public void Delete(string key) => Values.Remove(key);
    }

    private sealed class Maintenance : ILibraryMaintenanceRepository
    {
        public (int Movies, int MediaFiles, int MetadataEntries, int Shows, int Seasons, int Episodes) ClearCatalog() => default;
    }
}
