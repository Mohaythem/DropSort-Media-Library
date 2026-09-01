using System;
using System.IO;
using DropSort.Domain.Core.Operations;
using DropSort.Infrastructure.Persistence.Migrations;
using DropSort.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DropSort.Tests.Infrastructure;

public class PersistenceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly string _dbPath;
    
    public PersistenceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"dropsort_{Guid.NewGuid():N}.db");
        var migrator = new DatabaseMigrator($"Data Source={_dbPath}");
        migrator.Migrate();

        _connection = new SqliteConnection($"Data Source={_dbPath}");
        _connection.Open();
    }

    public void Dispose()
    {
        _connection.Dispose();
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    [Fact]
    public void Migrations_RunSuccessfully_AndCreateV1Schema()
    {
        // Verify schema versions
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        var version = Convert.ToInt32(cmd.ExecuteScalar());
        Assert.Equal(5, version); // We have 5 migrations
        
        // Verify tables exist
        cmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='movies';";
        Assert.Equal(1L, cmd.ExecuteScalar());

        cmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='file_operations';";
        Assert.Equal(1L, cmd.ExecuteScalar());
    }

    [Fact]
    public void JournalFSM_EnforcesValidTransitions()
    {
        var store = new FileOperationStore(_connection);
        var id = Guid.NewGuid().ToString("N");
        var plan = new FileOperationPlan(
            id,
            OperationType.Move,
            Path.Combine(Path.GetDirectoryName(_dbPath)!, "source.mkv"),
            Path.Combine(Path.GetDirectoryName(_dbPath)!, "destination.mkv"),
            1);

        // 1. Create a media file first due to FK constraints
        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO movies (title, date_added, created_at, updated_at, metadata_status) VALUES ('T', '2020', '2020', '2020', 'PENDING')";
            cmd.ExecuteNonQuery();
            cmd.CommandText = "INSERT INTO media_files (id, current_path, path_key, file_size, discovered_at, last_seen_at) VALUES (1, 'p', 'p', 0, '2020', '2020')";
            cmd.ExecuteNonQuery();
        }

        store.Create(plan, DateTimeOffset.UtcNow);

        // Valid transition
        store.Transition(id, OperationState.Validated);

        var updated = store.GetById(id);
        Assert.NotNull(updated);
        Assert.Equal(OperationState.Validated, updated.State);

        // Invalid transition (Validated -> FsVerified skips Executing)
        Assert.Throws<InvalidOperationException>(() => 
            store.Transition(id, OperationState.FsVerified));
            
        // Proceed correctly
        store.Transition(id, OperationState.Executing);
        store.Transition(id, OperationState.FsVerified);
        store.CommitVerified(id);
        
        updated = store.GetById(id);
        Assert.Equal(OperationState.Committed, updated!.State);
    }
}
