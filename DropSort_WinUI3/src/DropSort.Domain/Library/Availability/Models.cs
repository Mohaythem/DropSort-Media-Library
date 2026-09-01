using System;

namespace DropSort.Domain.Library.Availability;

public enum AvailabilityInspectionStatus
{
    Present,
    Missing,
    Error
}

public record MediaFileIdentity(long Size, long MTimeNs, long CTimeNs, ulong Dev, ulong Ino);

public record AvailabilityInspection(
    string Path,
    AvailabilityInspectionStatus Status,
    MediaFileIdentity? Identity = null,
    string? ErrorCode = null)
{
    public long? Size => Identity?.Size;
}
