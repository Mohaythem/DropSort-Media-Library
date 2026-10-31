using System;

namespace TBDMovies.Domain.Library.Availability;

public enum AvailabilityInspectionStatus
{
    Present,
    Missing,

    /// <summary>
    /// The storage that holds the path is not currently reachable or ready (for example a disconnected
    /// or unmounted drive). This is not a deleted or missing file. TBDMovies must not mark the media
    /// missing or rewrite its path in this state.
    /// </summary>
    StorageUnavailable,
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
