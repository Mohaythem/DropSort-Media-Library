namespace DropSort.Domain.Core.Operations;

public enum OperationType { Move, Rename }

public enum OperationState
{
    Planned,
    Validated,
    Executing,
    FsVerified,
    Committed,
    Failed,
    RecoveryRequired
}

public enum RecoverySituation
{
    NotRequired,
    NotActionable,
    SourceOnlyExecuting,
    DestinationOnlyVerified,
    BothExist,
    NeitherExists,
    DestinationUnsafeOrChanged
}

public sealed record FileOperationPlan
{
    public string OperationId { get; }
    public OperationType OperationType { get; }
    public string Source { get; }
    public string Destination { get; }
    public int? MediaFileId { get; }
    public string? ReversesOperationId { get; }

    public FileOperationPlan(
        string operationId,
        OperationType operationType,
        string source,
        string destination,
        int? mediaFileId = null,
        string? reversesOperationId = null)
    {
        OperationId = Required(operationId, nameof(operationId));
        if (!Enum.IsDefined(operationType)) throw new ArgumentOutOfRangeException(nameof(operationType));
        OperationType = operationType;
        Source = Absolute(source, nameof(source));
        Destination = Absolute(destination, nameof(destination));
        if (mediaFileId is <= 0) throw new ArgumentOutOfRangeException(nameof(mediaFileId));
        MediaFileId = mediaFileId;
        ReversesOperationId = reversesOperationId is null
            ? null
            : Required(reversesOperationId, nameof(reversesOperationId));
    }

    private static string Required(string value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value must be non-empty.", name)
            : value.Trim();

    private static string Absolute(string value, string name) =>
        !string.IsNullOrWhiteSpace(value) && Path.IsPathFullyQualified(value)
            ? Path.GetFullPath(value)
            : throw new ArgumentException("Path must be absolute.", name);
}

public sealed record OperationUpdate(
    long? SourceSize = null,
    long? SourceMTimeNs = null,
    ulong? SourceDev = null,
    ulong? SourceIno = null,
    long? DestinationSize = null,
    long? DestinationMTimeNs = null,
    ulong? DestinationDev = null,
    ulong? DestinationIno = null,
    string? DestinationSha256 = null,
    string? Strategy = null,
    string? ErrorCode = null,
    string? ErrorMessage = null);

public sealed record FileOperationRecord(
    string Id,
    OperationType OperationType,
    string Source,
    string Destination,
    OperationState State,
    int? MediaFileId,
    string? ReversesOperationId,
    long? SourceSize,
    long? SourceMTimeNs,
    ulong? SourceDev,
    ulong? SourceIno,
    long? DestinationSize,
    long? DestinationMTimeNs,
    ulong? DestinationDev,
    ulong? DestinationIno,
    string? DestinationSha256,
    string? Strategy,
    string? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record PreparedTransfer(
    string Strategy,
    long DestinationSize,
    long DestinationMTimeNs,
    ulong DestinationDev,
    ulong DestinationIno,
    string? DestinationSha256);

public sealed record RecoveryInspection(
    string OperationId,
    OperationState State,
    RecoverySituation Situation,
    bool SourceExists,
    bool DestinationExists,
    bool CanReconcile,
    string Message);
