using DropSort.Domain.Core.Operations;
using DropSort.FileSystem.Operations;
using DropSort.FileSystem.Safety;
using DropSort.Infrastructure.Persistence.Migrations;
using DropSort.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DropSort.Tests.FileSystem;

public sealed class RecoveryAndPathPolicyTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"dropsort_recovery_{Guid.NewGuid():N}");
    private readonly string _connectionString;

    public RecoveryAndPathPolicyTests()
    {
        Directory.CreateDirectory(_directory);
        _connectionString = $"Data Source={Path.Combine(_directory, "journal.db")}";
        new DatabaseMigrator(_connectionString).Migrate();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    [Fact]
    public void SourceOnlyExecutingRecoveryMarksFailedWithoutMutatingSource()
    {
        var source = Create("source.mkv", "source");
        var destination = Path.Combine(_directory, "destination.mkv");
        using var store = new FileOperationStore(_connectionString);
        using var coordinator = new FileOperationCoordinator(store);
        var plan = coordinator.Plan(OperationType.Move, source, destination, [_directory]).Plan;
        store.Transition(plan.OperationId, OperationState.Executing);

        var inspection = coordinator.InspectRecovery(plan.OperationId, [_directory]);
        Assert.Equal(RecoverySituation.SourceOnlyExecuting, inspection.Situation);
        Assert.True(inspection.CanReconcile);
        Assert.Equal(OperationState.Failed, coordinator.Recover(plan.OperationId, [_directory]).State);
        Assert.Equal("source", File.ReadAllText(source));
        Assert.False(File.Exists(destination));
    }

    [Fact]
    public void BothExistRecoveryPreservesBothAndRequiresManualRecovery()
    {
        var source = Create("source.mkv", "source");
        var destination = Path.Combine(_directory, "destination.mkv");
        using var store = new FileOperationStore(_connectionString);
        using var coordinator = new FileOperationCoordinator(store);
        var plan = coordinator.Plan(OperationType.Move, source, destination, [_directory]).Plan;
        store.Transition(plan.OperationId, OperationState.Executing);
        File.WriteAllText(destination, "destination");

        var recovered = coordinator.Recover(plan.OperationId, [_directory]);
        Assert.Equal(OperationState.RecoveryRequired, recovered.State);
        Assert.Equal("source", File.ReadAllText(source));
        Assert.Equal("destination", File.ReadAllText(destination));
    }

    [Fact]
    public void VerifiedDestinationOnlyRecoveryCommitsJournal()
    {
        var source = Create("source.mkv", "source");
        var destination = Path.Combine(_directory, "destination.mkv");
        using var store = new FileOperationStore(_connectionString);
        using var coordinator = new FileOperationCoordinator(store);
        var plan = coordinator.Plan(OperationType.Move, source, destination, [_directory]).Plan;
        store.Transition(plan.OperationId, OperationState.Executing);
        File.Move(source, destination);
        var identity = PathPolicy.Identity(destination);
        store.Transition(
            plan.OperationId,
            OperationState.FsVerified,
            new OperationUpdate(
                DestinationSize: identity.Size,
                DestinationMTimeNs: identity.MTimeNs,
                DestinationDev: identity.Dev,
                DestinationIno: identity.Ino,
                Strategy: "test-recovery"));

        var inspection = coordinator.InspectRecovery(plan.OperationId, [_directory]);
        Assert.Equal(RecoverySituation.DestinationOnlyVerified, inspection.Situation);
        Assert.Equal(OperationState.Committed, coordinator.Recover(plan.OperationId, [_directory]).State);
        Assert.Equal("source", File.ReadAllText(destination));
    }

    [Fact]
    public void CaseInsensitiveCollisionIsDistinctFromExactDestinationExistence()
    {
        var source = Create("source.mkv", "source");
        Create("MOVIE.mkv", "collision");
        var policy = new PathPolicy([_directory]);

        Assert.Throws<CaseInsensitiveCollisionException>(() =>
            policy.ValidatePlan(source, Path.Combine(_directory, "movie.mkv"), OperationType.Move));
        Assert.Throws<DestinationExistsException>(() =>
            policy.ValidatePlan(source, Path.Combine(_directory, "MOVIE.mkv"), OperationType.Move));
    }

    [Fact]
    public void ExistingHardlinkAliasIsDetectedAsSameFile()
    {
        var source = Create("source.mkv", "source");
        var alias = Path.Combine(_directory, "alias.mkv");
        Assert.True(WindowsNative.CreateHardLinkW(alias, source, IntPtr.Zero));
        var policy = new PathPolicy([_directory]);

        Assert.Throws<SameFileException>(() => policy.ValidatePlan(source, alias, OperationType.Move));
    }

    [Fact]
    public void SymbolicLinkTraversalIsRejectedWhenHostAllowsLinkCreation()
    {
        var real = Create("real.mkv", "real");
        var link = Path.Combine(_directory, "link.mkv");
        try
        {
            File.CreateSymbolicLink(link, real);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            return;
        }
        var policy = new PathPolicy([_directory]);

        Assert.Throws<LinkTraversalException>(() =>
            policy.ValidatePlan(link, Path.Combine(_directory, "destination.mkv"), OperationType.Move));
    }

    private string Create(string name, string contents)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, contents);
        return path;
    }
}
