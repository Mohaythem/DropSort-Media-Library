using DropSort.Application.External;
using DropSort.Domain.Library.Availability;
using DropSort.FileSystem.Safety;

namespace DropSort.FileSystem.Inspection;

public sealed class AvailabilityInspector : IAvailabilityInspector
{
    private readonly NetworkReadGuard _networkReads;
    private readonly Func<string, FileAttributes> _attributes;
    private readonly Func<string, MediaFileIdentity> _identity;
    private readonly Action<string> _validate;

    public AvailabilityInspector(NetworkReadGuard? networkReads = null)
        : this(networkReads, File.GetAttributes, path =>
        {
            var identity = PathPolicy.Identity(path);
            var creationNs = checked((File.GetCreationTimeUtc(path) - DateTime.UnixEpoch).Ticks * 100L);
            return new MediaFileIdentity(identity.Size, identity.MTimeNs, creationNs, identity.Dev, identity.Ino);
        }, PathPolicy.AssertNoReparseComponents) { }

    internal AvailabilityInspector(NetworkReadGuard? networkReads, Func<string, FileAttributes> attributes,
        Func<string, MediaFileIdentity> identity, Action<string> validate)
    {
        _networkReads = networkReads ?? NetworkReadGuard.Shared;
        _attributes = attributes;
        _identity = identity;
        _validate = validate;
    }

    public AvailabilityInspection Inspect(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) throw new ArgumentException("Path must be absolute.", nameof(path));
        return _networkReads.Execute(path, () => InspectRaw(path), () => new AvailabilityInspection(path, AvailabilityInspectionStatus.StorageUnavailable));
    }

    private AvailabilityInspection InspectRaw(string path)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(path));
        if (string.IsNullOrEmpty(root)) return new AvailabilityInspection(path, AvailabilityInspectionStatus.Error, ErrorCode: "NoRoot");
        if (!IsStorageAvailableRaw(root)) return new AvailabilityInspection(path, AvailabilityInspectionStatus.StorageUnavailable);
        try
        {
            _validate(path);
            var attributes = _attributes(path);
            if ((attributes & FileAttributes.Directory) != 0) return new AvailabilityInspection(path, AvailabilityInspectionStatus.Missing);
            return new AvailabilityInspection(path, AvailabilityInspectionStatus.Present, _identity(path));
        }
        catch (FileNotFoundException) { return IsStorageAvailableRaw(root) ? Missing(path) : Unavailable(path); }
        catch (DirectoryNotFoundException) { return IsStorageAvailableRaw(root) ? Missing(path) : Unavailable(path); }
        catch (NetworkStorageUnavailableException) { return Unavailable(path); }
        catch (IOException exception) { return IsStorageAvailableRaw(root) ? Error(path, exception) : Unavailable(path); }
        catch (UnauthorizedAccessException exception) { return IsStorageAvailableRaw(root) ? Error(path, exception) : Unavailable(path); }
        catch (System.ComponentModel.Win32Exception exception) { return IsStorageAvailableRaw(root) ? Error(path, exception) : Unavailable(path); }
    }

    public bool IsStorageAvailable(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return false;
        return _networkReads.Execute(path, () => IsStorageAvailableRaw(Path.GetPathRoot(Path.GetFullPath(path))), () => false);
    }

    private bool IsStorageAvailableRaw(string? root)
    {
        if (string.IsNullOrEmpty(root)) return false;
        try
        {
            if (NetworkReadGuard.IsNetworkPath(root))
            {
                var attributes = _attributes(root);
                return (attributes & FileAttributes.Directory) != 0;
            }
            return new DriveInfo(root).IsReady;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException
            or System.ComponentModel.Win32Exception) { return false; }
    }

    private static AvailabilityInspection Missing(string path) => new(path, AvailabilityInspectionStatus.Missing);
    private static AvailabilityInspection Unavailable(string path) => new(path, AvailabilityInspectionStatus.StorageUnavailable);
    private static AvailabilityInspection Error(string path, Exception exception) => new(path, AvailabilityInspectionStatus.Error, ErrorCode: exception.GetType().Name);
}
