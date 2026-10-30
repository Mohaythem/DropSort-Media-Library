using DropSort.Domain.Core.Operations;
using DropSort.FileSystem.Engine;
using DropSort.FileSystem.Operations;
using DropSort.FileSystem.Safety;
using DropSort.Infrastructure.Persistence.Migrations;
using DropSort.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DropSort.Tests.FileSystem;

public sealed class EngineTests : IDisposable
{
    private readonly string _directory;
    private readonly string _databasePath;
    private readonly string _connectionString;

    public EngineTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"dropsort_fs_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        _databasePath = Path.Combine(_directory, "journal.db");
        _connectionString = $"Data Source={_databasePath}";
        new DatabaseMigrator(_connectionString).Migrate();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    [Fact]
    public void JournaledTransfer_UsesHardlinkAndCommits()
    {
        var source = CreateSource();
        var destination = Path.Combine(_directory, "destination.mkv");
        using var store = new FileOperationStore(_connectionString);
        using var coordinator = new FileOperationCoordinator(store);

        var (plan, _) = coordinator.Plan(OperationType.Move, source, destination, [_directory]);
        Assert.Equal(OperationState.Validated, store.GetById(plan.OperationId)!.State);
        var record = coordinator.Execute(plan.OperationId, [_directory]);

        Assert.Equal(OperationState.Committed, record.State);
        Assert.Equal("hardlink-unlink", record.Strategy);
        Assert.False(File.Exists(source));
        Assert.Equal("hello world", File.ReadAllText(destination));
    }

    [Fact]
    public void JournaledTransfer_ForcedCopyFlushesHashesAndCommits()
    {
        var source = CreateSource();
        var destination = Path.Combine(_directory, "destination.mkv");
        using var store = new FileOperationStore(_connectionString);
        using var engine = new SafeTransferEngine(forceCopy: true);
        using var coordinator = new FileOperationCoordinator(store, engine);

        var (plan, _) = coordinator.Plan(OperationType.Move, source, destination, [_directory]);
        var record = coordinator.Execute(plan.OperationId, [_directory]);

        Assert.Equal(OperationState.Committed, record.State);
        Assert.Equal("copy-sha256-flush-finalize-unlink", record.Strategy);
        Assert.Matches("^[0-9a-f]{64}$", record.DestinationSha256!);
        Assert.False(File.Exists(source));
        Assert.Equal("hello world", File.ReadAllText(destination));
    }

    [Fact]
    public void ExistingDestinationIsRejectedBeforeJournalCreation()
    {
        var source = CreateSource();
        var destination = Path.Combine(_directory, "destination.mkv");
        File.WriteAllText(destination, "existing");
        using var store = new FileOperationStore(_connectionString);
        using var coordinator = new FileOperationCoordinator(store);

        Assert.Throws<DestinationExistsException>(() =>
            coordinator.Plan(OperationType.Move, source, destination, [_directory]));
        Assert.True(File.Exists(source));
        Assert.Empty(store.ListHistory());
    }

    [Fact]
    public void SourceChangeAfterPreviewIsRejectedWithoutJournal()
    {
        var source = CreateSource();
        var destination = Path.Combine(_directory, "destination.mkv");
        using var store = new FileOperationStore(_connectionString);
        using var coordinator = new FileOperationCoordinator(store);
        var preview = coordinator.Preview(OperationType.Move, source, destination, [_directory]);
        File.AppendAllText(source, "changed");

        Assert.Throws<SourceChangedException>(() =>
            coordinator.Plan(
                OperationType.Move, source, destination, [_directory],
                expectedSourceIdentity: preview.SourceIdentity));
        Assert.Empty(store.ListHistory());
    }

    [Fact]
    public void PathOutsideApprovedRootIsRejected()
    {
        var source = CreateSource();
        var other = Path.Combine(Path.GetTempPath(), $"dropsort_outside_{Guid.NewGuid():N}");
        Directory.CreateDirectory(other);
        try
        {
            using var store = new FileOperationStore(_connectionString);
            using var coordinator = new FileOperationCoordinator(store);
            Assert.Throws<UnsafePathException>(() =>
                coordinator.Plan(OperationType.Move, source, Path.Combine(other, "destination.mkv"), [_directory]));
        }
        finally
        {
            Directory.Delete(other, true);
        }
    }

    private string CreateSource()
    {
        var source = Path.Combine(_directory, "source.mkv");
        File.WriteAllText(source, "hello world");
        return source;
    }
}
