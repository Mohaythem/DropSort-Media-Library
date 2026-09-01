using DropSort.Domain.Core.Operations;
using DropSort.Domain.Core.Safety;

namespace DropSort.FileSystem.Safety;

public sealed record ValidatedPathPlan(string Source, string Destination, SourceIdentity Identity);

public sealed class PathPolicy
{
    private readonly string[] _approvedRoots;

    public PathPolicy(IEnumerable<string> approvedRoots)
    {
        ArgumentNullException.ThrowIfNull(approvedRoots);
        _approvedRoots = approvedRoots.Select(Canonical).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (_approvedRoots.Length == 0) throw new ArgumentException("At least one approved root is required.");
        foreach (var root in _approvedRoots)
        {
            AssertNoReparseComponents(root);
            if (!Directory.Exists(root)) throw new UnsafePathException($"Approved root is unavailable: {root}");
        }
    }

    public IReadOnlyList<string> ApprovedRoots => _approvedRoots;

    public ValidatedPathPlan ValidatePlan(string source, string destination, OperationType operationType)
    {
        if (!Enum.IsDefined(operationType)) throw new ArgumentOutOfRangeException(nameof(operationType));
        var sourcePath = Canonical(source);
        var destinationPath = Canonical(destination);
        AssertNoReparseComponents(sourcePath);
        AssertNoReparseComponents(Path.GetDirectoryName(destinationPath)!);
        AssertApproved(sourcePath);
        AssertApproved(destinationPath);

        if (!File.Exists(sourcePath)) throw new SourceMissingException($"Source is unavailable: {sourcePath}");
        var destinationParent = Path.GetDirectoryName(destinationPath)!;
        if (!Directory.Exists(destinationParent))
            throw new UnsafePathException($"Destination parent is unavailable: {destinationParent}");

        if (operationType == OperationType.Rename &&
            !StringComparer.OrdinalIgnoreCase.Equals(Path.GetDirectoryName(sourcePath), destinationParent))
            throw new InvalidRenameException("Rename operations must stay in the same directory.");

        if (StringComparer.OrdinalIgnoreCase.Equals(sourcePath, destinationPath))
            throw new SameFileException("Source and destination are the same Windows path.");

        var desiredName = Path.GetFileName(destinationPath);
        foreach (var entry in Directory.EnumerateFileSystemEntries(destinationParent))
        {
            var existingName = Path.GetFileName(entry);
            if (!StringComparer.OrdinalIgnoreCase.Equals(existingName, desiredName)) continue;
            if (!StringComparer.Ordinal.Equals(existingName, desiredName))
                throw new CaseInsensitiveCollisionException($"Case-insensitive destination collision: {existingName}");
            if (File.Exists(entry) && WindowsNative.Identity(entry) == WindowsNative.Identity(sourcePath))
                throw new SameFileException("Source and destination identify the same file.");
            throw new DestinationExistsException($"Destination already exists: {destinationPath}");
        }

        return new ValidatedPathPlan(sourcePath, destinationPath, WindowsNative.Identity(sourcePath));
    }

    public SourceIdentity Revalidate(FileOperationRecord record)
    {
        var validated = ValidatePlan(record.Source, record.Destination, record.OperationType);
        var expected = new SourceIdentity(
            record.SourceSize ?? throw new SourceChangedException("Journal lacks source size."),
            record.SourceMTimeNs ?? throw new SourceChangedException("Journal lacks source timestamp."),
            record.SourceDev ?? throw new SourceChangedException("Journal lacks source volume identity."),
            record.SourceIno ?? throw new SourceChangedException("Journal lacks source file identity."));
        if (validated.Identity != expected) throw new SourceChangedException("Source identity changed after planning.");
        return validated.Identity;
    }

    public string ValidateExistingRecoveryPath(string path)
    {
        var canonical = Canonical(path);
        AssertNoReparseComponents(canonical);
        AssertApproved(canonical);
        if (!File.Exists(canonical)) throw new UnsafePathException($"Recovery path is unavailable: {canonical}");
        return canonical;
    }

    public static SourceIdentity Identity(string path) => WindowsNative.Identity(Canonical(path));

    internal static void AssertNoReparseComponents(string path)
    {
        var full = Canonical(path);
        var root = Path.GetPathRoot(full)!;
        var current = root;
        foreach (var part in full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            if (!File.Exists(current) && !Directory.Exists(current)) break;
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new LinkTraversalException($"Link/reparse traversal is not allowed: {current}");
        }
    }

    private void AssertApproved(string candidate)
    {
        foreach (var root in _approvedRoots)
        {
            var relative = Path.GetRelativePath(root, candidate);
            if (relative == "." ||
                (!relative.Equals("..", StringComparison.Ordinal) &&
                 !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                 !Path.IsPathRooted(relative)))
                return;
        }
        throw new UnsafePathException($"Path is outside approved roots: {candidate}");
    }

    private static string Canonical(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new UnsafePathException("Path must be absolute.");
        return Path.GetFullPath(path);
    }
}
