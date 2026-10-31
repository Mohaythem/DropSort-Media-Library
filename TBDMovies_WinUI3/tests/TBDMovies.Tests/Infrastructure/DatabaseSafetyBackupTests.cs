using System;
using System.IO;
using System.Linq;
using System.Threading;
using TBDMovies.Infrastructure.Persistence;
using TBDMovies.Infrastructure.Persistence.Migrations;
using TBDMovies.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Xunit;

namespace TBDMovies.Tests.Infrastructure;

/// <summary>
/// Early Backup Protection. Before a forward schema migration of an existing database, TBDMovies creates
/// and verifies a consistent snapshot. If the backup cannot be created or verified, the migration must
/// not begin and the database is left untouched. A brand-new database has no data and is not backed up.
/// Backups are bounded by retention and can be restored.
/// </summary>
public sealed class DatabaseSafetyBackupTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"tbdmovies_backup_{Guid.NewGuid():N}");
    private readonly string _dbPath;
    private readonly string _connectionString;

    public DatabaseSafetyBackupTests()
    {
        Directory.CreateDirectory(_directory);
        _dbPath = Path.Combine(_directory, "library.db");
        _connectionString = $"Data Source={_dbPath}";
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); } catch { }
    }

    private string BackupDir => Path.Combine(_directory, "backups");
    private int UserVersion()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private void SeedMovie(int id)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO movies(id, provider, external_id, title, date_added, created_at, updated_at, genres, metadata_status)
            VALUES ($id, 'tmdb', $id, 'Seed', '2026-01-01', '2026-01-01', '2026-01-01', '[]', 'READY');
            """;
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    [Fact]
    public void FreshDatabase_Migration_TakesNoBackup()
    {
        new DatabaseMigrator(_connectionString).Migrate(); // 0 -> latest, no existing data
        Assert.False(Directory.Exists(BackupDir) && Directory.GetFiles(BackupDir, "library.*.db").Length > 0);

        new DatabaseMigrator(_connectionString).Migrate(); // already current -> still nothing
        Assert.False(Directory.Exists(BackupDir) && Directory.GetFiles(BackupDir, "library.*.db").Length > 0);
    }

    [Fact]
    public void PendingMigration_CreatesVerifiedBackupOfPreMigrationState()
    {
        new DatabaseMigrator(_connectionString).Migrate(9); // fresh -> 9 (no backup)
        SeedMovie(42);

        new DatabaseMigrator(_connectionString).Migrate(); // 9 -> latest, existing data -> backup

        var backups = Directory.GetFiles(BackupDir, "library.*.db");
        var backup = Assert.Single(backups);
        Assert.True(DatabaseSafetyBackup.VerifyBackup(backup, out var version));
        Assert.Equal(9, version); // the pre-migration schema version
        Assert.Equal(1, CountMovies(backup)); // the pre-migration data
        Assert.Equal(DatabaseMigrator.LatestVersion, UserVersion()); // migration completed
    }

    [Fact]
    public void BackupCreationFailure_PreventsMigration()
    {
        new DatabaseMigrator(_connectionString).Migrate(9);
        SeedMovie(1);

        // Put a file where the backup directory must be created, so the backup cannot be written.
        File.WriteAllText(BackupDir, "blocker");

        Assert.Throws<DatabaseBackupException>(() => new DatabaseMigrator(_connectionString).Migrate());
        Assert.Equal(9, UserVersion()); // migration did not begin
    }

    [Fact]
    public void VerifyBackup_AcceptsValid_AndRejectsCorrupt()
    {
        new DatabaseMigrator(_connectionString).Migrate();
        var backup = CreateBackupDirectly(out _);
        Assert.True(DatabaseSafetyBackup.VerifyBackup(backup, out var version));
        Assert.Equal(DatabaseMigrator.LatestVersion, version);

        var corrupt = Path.Combine(_directory, "corrupt.db");
        File.WriteAllText(corrupt, "not a sqlite database");
        Assert.False(DatabaseSafetyBackup.VerifyBackup(corrupt, out _));
    }

    [Fact]
    public void Retention_KeepsBoundedNumberOfKnownGoodBackups()
    {
        new DatabaseMigrator(_connectionString).Migrate();
        using var source = new SqliteConnection(_connectionString);
        source.Open();
        var version = DatabaseMigrator.LatestVersion;

        string last = string.Empty;
        for (var i = 0; i < DatabaseSafetyBackup.RetentionCount + 2; i++)
        {
            last = DatabaseSafetyBackup.CreateVerifiedBackup(source, _connectionString, version, "retention");
            Thread.Sleep(4); // distinct millisecond-stamped names
            Assert.True(DatabaseSafetyBackup.VerifyBackup(last, out _)); // the newest always survives pruning
        }

        Assert.Equal(DatabaseSafetyBackup.RetentionCount, Directory.GetFiles(BackupDir, "library.*.db").Length);
        Assert.True(File.Exists(last));
    }

    [Fact]
    public void Restore_RecoversState_AndRejectsCorruptBackup()
    {
        new DatabaseMigrator(_connectionString).Migrate();
        SeedMovie(7);
        var backup = CreateBackupDirectly(out _);

        // Mutate the live database after the backup.
        using (var connection = new SqliteConnection(_connectionString))
        {
            connection.Open();
            using var delete = connection.CreateCommand();
            delete.CommandText = "DELETE FROM movies;";
            delete.ExecuteNonQuery();
        }
        Assert.Equal(0, CountMovies(_dbPath));

        DatabaseSafetyBackup.RestoreFromBackup(backup, _connectionString);
        Assert.Equal(1, CountMovies(_dbPath)); // state recovered

        // A corrupt backup is rejected before it can replace the database.
        var corrupt = Path.Combine(_directory, "corrupt.db");
        File.WriteAllText(corrupt, "not sqlite");
        Assert.Throws<DatabaseBackupException>(() => DatabaseSafetyBackup.RestoreFromBackup(corrupt, _connectionString));
        Assert.Equal(1, CountMovies(_dbPath)); // database untouched by the rejected restore
    }

    [Fact]
    public void ClearCatalog_CreatesVerifiedBackupOfPreClearState()
    {
        new DatabaseMigrator(_connectionString).Migrate();
        SeedMovie(5);

        new LibraryMaintenanceRepository(_connectionString).ClearCatalog();

        Assert.Equal(0, CountMovies(_dbPath)); // catalog cleared
        var backup = Assert.Single(Directory.GetFiles(BackupDir, "library.*.db"));
        Assert.True(DatabaseSafetyBackup.VerifyBackup(backup, out _));
        Assert.Equal(1, CountMovies(backup)); // committed pre-clear data captured (incl. WAL)
    }

    [Fact]
    public void ClearCatalog_BackupFailure_PreventsAnyDelete()
    {
        new DatabaseMigrator(_connectionString).Migrate();
        SeedMovie(6);

        // Block the backup directory so the safety backup cannot be written.
        File.WriteAllText(BackupDir, "blocker");

        Assert.Throws<DatabaseBackupException>(() => new LibraryMaintenanceRepository(_connectionString).ClearCatalog());
        Assert.Equal(1, CountMovies(_dbPath)); // nothing was cleared
    }

    private string CreateBackupDirectly(out int version)
    {
        using var source = new SqliteConnection(_connectionString);
        source.Open();
        version = DatabaseMigrator.LatestVersion;
        return DatabaseSafetyBackup.CreateVerifiedBackup(source, _connectionString, version, "direct");
    }

    private static int CountMovies(string dbPath)
    {
        using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM movies;";
        return Convert.ToInt32(command.ExecuteScalar());
    }
}
