using DropSort.Application.External;
using DropSort.Domain.Library.Availability;
using DropSort.FileSystem.Safety;

namespace DropSort.FileSystem.Inspection;

public sealed class AvailabilityInspector : IAvailabilityInspector
{
    public AvailabilityInspection Inspect(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new ArgumentException("Path must be absolute.", nameof(path));
        try
        {
            PathPolicy.AssertNoReparseComponents(path);

            // Probe with GetAttributes, not File.Exists: File.Exists hides the difference between a file
            // that is truly absent and one that cannot be inspected (access denied, other I/O). That
            // difference is exactly the access-problem vs missing distinction the contract requires.
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.Directory) != 0)
            {
                // A directory sits where a file is expected: the media file is not present here.
                return Absent(path);
            }

            var identity = PathPolicy.Identity(path);
            var creationNs = checked((File.GetCreationTimeUtc(path) - DateTime.UnixEpoch).Ticks * 100L);
            return new AvailabilityInspection(
                path,
                AvailabilityInspectionStatus.Present,
                new MediaFileIdentity(identity.Size, identity.MTimeNs, creationNs, identity.Dev, identity.Ino));
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            // The file (or a parent segment) does not exist. Fail closed: call it missing only when the
            // storage root is reachable, otherwise the storage is unavailable, not deleted.
            return Absent(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Access denied or another I/O condition. If the root is not reachable this is unavailable
            // storage; if the root is reachable this is a genuine access problem, never "missing".
            return IsStorageAvailable(path)
                ? new AvailabilityInspection(path, AvailabilityInspectionStatus.Error, ErrorCode: exception.GetType().Name)
                : new AvailabilityInspection(path, AvailabilityInspectionStatus.StorageUnavailable);
        }
    }

    private AvailabilityInspection Absent(string path) =>
        IsStorageAvailable(path)
            ? new AvailabilityInspection(path, AvailabilityInspectionStatus.Missing)
            : new AvailabilityInspection(path, AvailabilityInspectionStatus.StorageUnavailable);

    public bool IsStorageAvailable(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root))
                return false; // no identifiable root: cannot prove availability

            // A UNC share (\\server\share): the share root must be reachable.
            if (root.StartsWith(@"\\", StringComparison.Ordinal))
                return Directory.Exists(root);

            // A drive-letter root (C:\, E:\): the volume must be present and ready.
            return new DriveInfo(root).IsReady;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Cannot determine availability: fail closed so the caller never concludes "deleted".
            return false;
        }
    }
}
