using DropSort.Infrastructure.Persistence.Migrations;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DropSort.Tests.Infrastructure;

public sealed class MigrationUpgradeTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"dropsort_migrations_{Guid.NewGuid():N}");
    private readonly string _databasePath;
    private readonly string _connectionString;

    public MigrationUpgradeTests()
    {
        Directory.CreateDirectory(_directory);
        _databasePath = Path.Combine(_directory, "upgrade.db");
        _connectionString = $"Data Source={_databasePath}";
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    [Fact]
    public void UpgradeFrom0001To0005PreservesRowsRelationshipsAndUnsignedIdentityText()
    {
        var migrator = new DatabaseMigrator(_connectionString);
        migrator.Migrate(1);
        using (var connection = Open())
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO movies(id, provider, external_id, title, date_added, created_at, updated_at)
                VALUES (7, 'tmdb', '7', 'Seven', '2026-01-01T00:00:00+00:00', '2026-01-01T00:00:00+00:00', '2026-01-01T00:00:00+00:00');
                INSERT INTO media_files(id, movie_id, current_path, path_key, file_size, discovered_at, last_seen_at)
                VALUES (9, 7, 'C:\\media\\Seven.mkv', 'C:\\MEDIA\\SEVEN.MKV', 10, '2026-01-01T00:00:00+00:00', '2026-01-01T00:00:00+00:00');
                INSERT INTO file_operations(
                    id, operation_type, source_path, destination_path, state, media_file_id,
                    source_dev, source_ino, created_at, updated_at)
                VALUES (
                    'op-1', 'MOVE', 'C:\\media\\Seven.mkv', 'D:\\media\\Seven.mkv', 'COMMITTED', 9,
                    '18446744073709551615', '18446744073709551614',
                    '2026-01-01T00:00:00+00:00', '2026-01-01T00:00:00+00:00');
                """;
            command.ExecuteNonQuery();
        }

        migrator.Migrate();

        using var upgraded = Open();
        Assert.Equal(5L, Scalar(upgraded, "PRAGMA user_version;"));
        Assert.Equal("18446744073709551615", Scalar(upgraded, "SELECT source_dev FROM file_operations WHERE id = 'op-1';"));
        Assert.Equal(9L, Scalar(upgraded, "SELECT media_file_id FROM file_operations WHERE id = 'op-1';"));
        Assert.Equal("READY", Scalar(upgraded, "SELECT metadata_status FROM movies WHERE id = 7;"));
        Assert.Null(Scalar(upgraded, "PRAGMA foreign_key_check;"));
    }

    [Fact]
    public void StartupValidationRejectsMissingCriticalIndex()
    {
        var migrator = new DatabaseMigrator(_connectionString);
        migrator.Migrate();
        using (var connection = Open())
        {
            using var command = connection.CreateCommand();
            command.CommandText = "DROP INDEX idx_file_operations_state;";
            command.ExecuteNonQuery();
        }

        Assert.Throws<InvalidOperationException>(() => migrator.Migrate());
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

    private static object? Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }
}
