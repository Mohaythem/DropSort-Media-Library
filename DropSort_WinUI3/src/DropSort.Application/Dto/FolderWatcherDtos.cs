namespace DropSort.Application.Dto;

public enum FolderWatcherRootStatus
{
    Complete,
    Partial,
    Unavailable,
    Failed,
    Cancelled
}

public enum FolderWatcherChangeKind
{
    New,
    Modified,
    Missing,
    PotentialMove,
    Warning
}

public sealed record FolderWatcherFileFact(
    string Path,
    long Size,
    DateTimeOffset LastWriteUtc,
    bool IsReady,
    string? Identity = null);

public sealed record FolderWatcherChange(
    string Key,
    string Path,
    FolderWatcherChangeKind Kind,
    FolderWatcherFileFact? Current = null,
    FolderWatcherFileFact? Baseline = null,
    string? RelatedPath = null,
    string? Message = null);

public sealed record FolderWatcherSnapshot(
    string Path,
    bool Enabled,
    FolderWatcherRootStatus Status,
    DateTimeOffset? LastScanUtc,
    IReadOnlyList<FolderWatcherFileFact> Files,
    IReadOnlyList<FolderWatcherChange> Changes,
    IReadOnlyList<string> Warnings,
    bool IsScanning,
    bool MissedEvents);

public sealed record FolderWatcherWatchedFolder(string Path, bool Enabled);

public sealed record FolderWatcherState(
    int SchemaVersion,
    IReadOnlyList<FolderWatcherWatchedFolder> Folders,
    IReadOnlyDictionary<string, IReadOnlyList<FolderWatcherFileFact>> Baselines,
    IReadOnlyDictionary<string, IReadOnlyList<FolderWatcherChange>> PendingChanges,
    IReadOnlyDictionary<string, IReadOnlyList<string>> IgnoredKeys,
    IReadOnlyDictionary<string, bool> MissedEvents)
{
    public static FolderWatcherState Empty { get; } = new(
        1, [], new Dictionary<string, IReadOnlyList<FolderWatcherFileFact>>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, IReadOnlyList<FolderWatcherChange>>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));
}
