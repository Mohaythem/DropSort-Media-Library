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
    public void StartupRecoverySweepRecoversVerifiedDestinationAndUpdatesMediaFile()
    {
        var source = Create("original_movie.mkv", "movie data");
        var destination = Path.Combine(_directory, "organized_movie.mkv");

        var catalogFactory = new CatalogUnitOfWorkFactory(_connectionString);
        using var uow = catalogFactory.Begin();
        var facts = new DropSort.Domain.Library.Movies.VerifiedMediaFileFacts(
            source,
            new FileInfo(source).Length,
            ".mkv",
            "1080p",
            "x264",
            "test",
            DateTimeOffset.UtcNow);
        var mediaFile = uow.MediaFiles.Add(facts);
        uow.Commit();

        using var store = new FileOperationStore(_connectionString);
        using var coordinator = new FileOperationCoordinator(store);
        var (plan, _) = coordinator.Plan(OperationType.Move, source, destination, [_directory], mediaFileId: mediaFile.Id);
        store.Transition(plan.OperationId, OperationState.Executing);

        // Simulate crash right after file move and verification
        File.Move(source, destination);
        var destIdentity = PathPolicy.Identity(destination);
        store.Transition(
            plan.OperationId,
            OperationState.FsVerified,
            new OperationUpdate(
                DestinationSize: destIdentity.Size,
                DestinationMTimeNs: destIdentity.MTimeNs,
                DestinationDev: destIdentity.Dev,
                DestinationIno: destIdentity.Ino,
                Strategy: "test-recovery"));

        // Verify non-terminal state before startup recovery
        var nonterminalBefore = store.ListNonterminal();
        Assert.Single(nonterminalBefore);
        Assert.Equal(plan.OperationId, nonterminalBefore[0].Id);

        // Simulate startup recovery sweep
        foreach (var op in store.ListNonterminal())
        {
            var roots = new[] { Path.GetDirectoryName(op.Source)!, Path.GetDirectoryName(op.Destination)! };
            coordinator.Recover(op.Id, roots);
        }

        // Verify clean terminal state
        Assert.Empty(store.ListNonterminal());
        var finishedOp = store.GetById(plan.OperationId);
        Assert.NotNull(finishedOp);
        Assert.Equal(OperationState.Committed, finishedOp.State);

        // Verify media file record was updated to destination and is PRESENT
        using var checkUow = catalogFactory.Begin();
        var updatedMedia = checkUow.MediaFiles.GetById(mediaFile.Id);
        Assert.NotNull(updatedMedia);
        Assert.Equal(destination, updatedMedia.CurrentPath);
        Assert.Equal(DropSort.Domain.Library.Movies.MediaFileStatus.Present, updatedMedia.Status);
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

    [Fact]
    public void PlannedOrValidatedRecoveryMarksFailedWhenSourceIntact()
    {
        var source = Create("source_planned.mkv", "source_planned");
        var destination = Path.Combine(_directory, "dest_planned.mkv");
        using var store = new FileOperationStore(_connectionString);
        using var coordinator = new FileOperationCoordinator(store);
        var (plan, _) = coordinator.Plan(OperationType.Move, source, destination, [_directory]);

        var inspection = coordinator.InspectRecovery(plan.OperationId, [_directory]);
        Assert.Equal(RecoverySituation.SourceOnlyExecuting, inspection.Situation);
        Assert.True(inspection.CanReconcile);

        var recovered = coordinator.Recover(plan.OperationId, [_directory]);
        Assert.Equal(OperationState.Failed, recovered.State);
        Assert.True(File.Exists(source));
        Assert.False(File.Exists(destination));
    }

    [Fact]
    public void NeitherExistsRecoveryTransitionsToRecoveryRequired()
    {
        var source = Create("source_neither.mkv", "source_neither");
        var destination = Path.Combine(_directory, "dest_neither.mkv");
        using var store = new FileOperationStore(_connectionString);
        using var coordinator = new FileOperationCoordinator(store);
        var (plan, _) = coordinator.Plan(OperationType.Move, source, destination, [_directory]);
        store.Transition(plan.OperationId, OperationState.Executing);
        File.Delete(source);

        var inspection = coordinator.InspectRecovery(plan.OperationId, [_directory]);
        Assert.Equal(RecoverySituation.NeitherExists, inspection.Situation);
        Assert.False(inspection.CanReconcile);

        var recovered = coordinator.Recover(plan.OperationId, [_directory]);
        Assert.Equal(OperationState.RecoveryRequired, recovered.State);
    }

    [Fact]
    public void DestinationMismatchRecoveryPreservesFilesAndRequiresManualRecovery()
    {
        var source = Create("source_mismatch.mkv", "source_mismatch");
        var destination = Path.Combine(_directory, "dest_mismatch.mkv");
        using var store = new FileOperationStore(_connectionString);
        using var coordinator = new FileOperationCoordinator(store);
        var (plan, _) = coordinator.Plan(OperationType.Move, source, destination, [_directory]);
        store.Transition(plan.OperationId, OperationState.Executing);
        File.WriteAllText(destination, "completely different destination content");

        var recovered = coordinator.Recover(plan.OperationId, [_directory]);
        Assert.Equal(OperationState.RecoveryRequired, recovered.State);
        Assert.True(File.Exists(source));
        Assert.True(File.Exists(destination));
    }

    [Fact]
    public void ExtendedPathFormattingHandlesLocalUncAndPrefixedPaths()
    {
        Assert.Equal(@"\\?\C:\test\movie.mkv", WindowsNative.EnsureExtendedPath(@"C:\test\movie.mkv"));
        Assert.Equal(@"\\?\C:\test\movie.mkv", WindowsNative.EnsureExtendedPath(@"\\?\C:\test\movie.mkv"));
        Assert.Equal(@"\\?\UNC\server\share\movie.mkv", WindowsNative.EnsureExtendedPath(@"\\server\share\movie.mkv"));
    }

    [Fact]
    public void StaleTempFileInspectorFindsDropSortTempFilesWithoutDeleting()
    {
        var targetFile = Create("movie.mkv", "movie content");
        var regularTemp = Create("movie.tmp", "temp content");
        var dropsortTemp = Create(".movie.mkv.dropsort-op12345.tmp", "partial copy content");

        var stale = DropSort.FileSystem.Inspection.CrashTempFileInspector.FindStaleTempFiles([_directory]);

        Assert.Single(stale);
        Assert.Equal(".movie.mkv.dropsort-op12345.tmp", stale[0].FileName);
        Assert.Equal(Path.GetFullPath(dropsortTemp), stale[0].FilePath);

        // Verification: NEVER automatically deletes files!
        Assert.True(File.Exists(targetFile));
        Assert.True(File.Exists(regularTemp));
        Assert.True(File.Exists(dropsortTemp));
    }

    [Fact]
    public void CoordinatorDetectStaleTempFilesDiscoversFilesSafely()
    {
        Create(".other.mkv.dropsort-test999.tmp", "test");
        using var store = new FileOperationStore(_connectionString);
        using var coordinator = new FileOperationCoordinator(store);

        var stale = coordinator.DetectStaleTempFiles([_directory]);
        Assert.Contains(stale, item => item.FileName == ".other.mkv.dropsort-test999.tmp");
    }

    private string Create(string name, string contents)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, contents);
        return path;
    }
}
