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
            if (!File.Exists(path))
                return new AvailabilityInspection(path, AvailabilityInspectionStatus.Missing);
            var identity = PathPolicy.Identity(path);
            var creationNs = checked((File.GetCreationTimeUtc(path) - DateTime.UnixEpoch).Ticks * 100L);
            return new AvailabilityInspection(
                path,
                AvailabilityInspectionStatus.Present,
                new MediaFileIdentity(identity.Size, identity.MTimeNs, creationNs, identity.Dev, identity.Ino));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new AvailabilityInspection(path, AvailabilityInspectionStatus.Error, ErrorCode: exception.GetType().Name);
        }
    }
}
