using System.Security.Cryptography;
using DropSort.Application.External;
using DropSort.Application.Repositories;
using DropSort.Domain.Core.Operations;
using DropSort.Domain.Core.Safety;
using DropSort.FileSystem.Engine;
using DropSort.FileSystem.Safety;

namespace DropSort.FileSystem.Operations;

public sealed class FileOperationCoordinator : IFileOperationCoordinator, IDisposable
{
    private readonly IFileOperationStore _store;
    private readonly SafeTransferEngine _engine;
    private readonly Func<DateTimeOffset> _clock;

    public FileOperationCoordinator(IFileOperationStore store, Func<DateTimeOffset>? clock = null)
        : this(store, new SafeTransferEngine(), clock)
    {
    }

    internal FileOperationCoordinator(
        IFileOperationStore store,
        SafeTransferEngine engine,
        Func<DateTimeOffset>? clock = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public (FileOperationPlan Plan, SourceIdentity SourceIdentity) Plan(
        OperationType operationType,
        string source,
        string destination,
        IReadOnlyList<string> approvedRoots,
        int? mediaFileId = null,
        string? reversesOperationId = null,
        SourceIdentity? expectedSourceIdentity = null)
    {
        var preview = Preview(operationType, source, destination, approvedRoots, mediaFileId, reversesOperationId);
        var validatedIdentity = preview.SourceIdentity;
        if (expectedSourceIdentity is not null && expectedSourceIdentity != validatedIdentity)
            throw new SourceChangedException("Source identity changed after preview.");

        var plan = new FileOperationPlan(
            Guid.NewGuid().ToString("D"),
            operationType,
            preview.Plan.Source,
            preview.Plan.Destination,
            mediaFileId,
            reversesOperationId);
        var now = _clock();
        _store.Create(plan, now);
        _store.Transition(
            plan.OperationId,
            OperationState.Validated,
            new OperationUpdate(
                SourceSize: validatedIdentity.Size,
                SourceMTimeNs: validatedIdentity.MTimeNs,
                SourceDev: validatedIdentity.Dev,
                SourceIno: validatedIdentity.Ino));
        return (plan, validatedIdentity);
    }

    public (FileOperationPlan Plan, SourceIdentity SourceIdentity) Preview(
        OperationType operationType,
        string source,
        string destination,
        IReadOnlyList<string> approvedRoots,
        int? mediaFileId = null,
        string? reversesOperationId = null)
    {
        var validated = new PathPolicy(approvedRoots).ValidatePlan(source, destination, operationType);
        var plan = new FileOperationPlan(
            $"preview-{Guid.NewGuid():D}", operationType, validated.Source, validated.Destination,
            mediaFileId, reversesOperationId);
        return (plan, validated.Identity);
    }

    public FileOperationRecord Execute(string operationId, IReadOnlyList<string> approvedRoots)
    {
        var record = Require(operationId);
        if (record.State != OperationState.Validated)
            throw new InvalidOperationException($"Operation must be Validated, got {record.State}.");

        _store.Transition(record.Id, OperationState.Executing);
        try
        {
            record = Require(record.Id);
            var policy = new PathPolicy(approvedRoots);
            var identity = policy.Revalidate(record);
            var prepared = _engine.Prepare(record.Source, record.Destination, identity, record.Id);
            _store.Transition(
                record.Id,
                OperationState.FsVerified,
                new OperationUpdate(
                    DestinationSize: prepared.DestinationSize,
                    DestinationMTimeNs: prepared.DestinationMTimeNs,
                    DestinationDev: prepared.DestinationDev,
                    DestinationIno: prepared.DestinationIno,
                    DestinationSha256: prepared.DestinationSha256,
                    Strategy: prepared.Strategy));
            _engine.FinalizeSourceRemoval(record.Source, record.Destination, identity, prepared, record.Id);
            return _store.CommitVerified(record.Id);
        }
        catch (Exception exception)
        {
            _engine.Abandon(record.Id);
            RecordFailure(record.Id, exception);
            throw;
        }
    }

    public RecoveryInspection InspectRecovery(string operationId, IReadOnlyList<string> approvedRoots)
    {
        var record = Require(operationId);
        if (record.State is OperationState.Committed or OperationState.Failed)
            return Inspection(record, RecoverySituation.NotRequired, false, "Operation is terminal.");
        if (record.State is OperationState.Planned or OperationState.Validated)
            return Inspection(record, RecoverySituation.NotActionable, false, "Filesystem execution has not started.");

        var sourceExists = EntryExists(record.Source);
        var destinationExists = EntryExists(record.Destination);
        if (sourceExists && destinationExists)
            return new RecoveryInspection(record.Id, record.State, RecoverySituation.BothExist, true, true, false,
                "Both source and destination exist; DropSort will preserve both.");
        if (!sourceExists && !destinationExists)
            return new RecoveryInspection(record.Id, record.State, RecoverySituation.NeitherExists, false, false, false,
                "Neither source nor destination exists; automatic recovery is unsafe.");
        if (sourceExists)
        {
            var actionable = record.State == OperationState.Executing;
            return new RecoveryInspection(
                record.Id,
                record.State,
                actionable ? RecoverySituation.SourceOnlyExecuting : RecoverySituation.NotActionable,
                true,
                false,
                actionable,
                actionable
                    ? "The intact source can be retained and the interrupted operation marked failed."
                    : "Source-only state conflicts with the durable journal.");
        }

        try
        {
            var policy = new PathPolicy(approvedRoots);
            policy.ValidateExistingRecoveryPath(record.Destination);
            if (!DestinationMatches(record))
                return UnsafeDestination(record);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return UnsafeDestination(record);
        }

        var canReconcile = record.State is OperationState.Executing or OperationState.FsVerified or OperationState.RecoveryRequired;
        return new RecoveryInspection(
            record.Id,
            record.State,
            canReconcile ? RecoverySituation.DestinationOnlyVerified : RecoverySituation.NotActionable,
            false,
            true,
            canReconcile,
            canReconcile ? "Verified destination can be committed to the catalog." : "Journal state is not actionable.");
    }

    public FileOperationRecord Recover(string operationId, IReadOnlyList<string> approvedRoots)
    {
        var record = Require(operationId);
        var inspection = InspectRecovery(operationId, approvedRoots);
        switch (inspection.Situation)
        {
            case RecoverySituation.NotRequired:
            case RecoverySituation.NotActionable:
                return record;
            case RecoverySituation.SourceOnlyExecuting:
                return _store.Transition(
                    record.Id,
                    OperationState.Failed,
                    new OperationUpdate(
                        ErrorCode: "InterruptedBeforeDestination",
                        ErrorMessage: "Source is intact and destination is absent."));
            case RecoverySituation.DestinationOnlyVerified:
                if (record.State is OperationState.Executing or OperationState.RecoveryRequired)
                    _store.Transition(record.Id, OperationState.FsVerified);
                return _store.CommitVerified(record.Id);
            default:
                if (record.State != OperationState.RecoveryRequired)
                    return _store.Transition(
                        record.Id,
                        OperationState.RecoveryRequired,
                        new OperationUpdate(
                            ErrorCode: "AmbiguousFilesystemState",
                            ErrorMessage: inspection.Message));
                return record;
        }
    }

    public void Dispose() => _engine.Dispose();

    private FileOperationRecord Require(string operationId) =>
        _store.GetById(operationId) ?? throw new KeyNotFoundException($"Operation {operationId} was not found.");

    private void RecordFailure(string operationId, Exception exception)
    {
        var current = Require(operationId);
        if (current.State is OperationState.Committed or OperationState.Failed) return;
        var sourceExists = EntryExists(current.Source);
        var destinationExists = EntryExists(current.Destination);
        var target = current.State == OperationState.Executing && sourceExists && !destinationExists
            ? OperationState.Failed
            : OperationState.RecoveryRequired;
        try
        {
            _store.Transition(
                current.Id,
                target,
                new OperationUpdate(ErrorCode: exception.GetType().Name, ErrorMessage: exception.Message));
        }
        catch (InvalidOperationException) when (target == OperationState.RecoveryRequired)
        {
            // The original exception remains authoritative if journal persistence also failed.
        }
    }

    private static bool DestinationMatches(FileOperationRecord record)
    {
        if (record.DestinationSize is null || record.DestinationMTimeNs is null ||
            record.DestinationDev is null || record.DestinationIno is null)
            return false;
        var identity = PathPolicy.Identity(record.Destination);
        if (identity != new SourceIdentity(
                record.DestinationSize.Value,
                record.DestinationMTimeNs.Value,
                record.DestinationDev.Value,
                record.DestinationIno.Value))
            return false;
        if (record.DestinationSha256 is null) return true;
        using var stream = File.OpenRead(record.Destination);
        var actual = SHA256.HashData(stream);
        return CryptographicOperations.FixedTimeEquals(actual, Convert.FromHexString(record.DestinationSha256));
    }

    private static bool EntryExists(string path)
    {
        try
        {
            _ = File.GetAttributes(path);
            return true;
        }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    private static RecoveryInspection Inspection(
        FileOperationRecord record,
        RecoverySituation situation,
        bool canReconcile,
        string message) =>
        new(record.Id, record.State, situation, EntryExists(record.Source), EntryExists(record.Destination), canReconcile, message);

    private static RecoveryInspection UnsafeDestination(FileOperationRecord record) =>
        new(record.Id, record.State, RecoverySituation.DestinationUnsafeOrChanged, false, true, false,
            "Destination is unsafe or no longer matches recorded verification evidence.");
}
