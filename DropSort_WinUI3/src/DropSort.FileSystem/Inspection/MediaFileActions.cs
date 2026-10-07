using System.Diagnostics;
using DropSort.Application.External;
using DropSort.Domain.Library.Availability;

namespace DropSort.FileSystem.Inspection;

public sealed record FileActionResult(AvailabilityInspectionStatus Status, string? ErrorCode = null);

/// <summary>Read-only shell actions. Availability checks and shell activation run away from the UI.</summary>
public sealed class MediaFileActions
{
    private readonly IAvailabilityInspector _inspector;
    private readonly Action<string, bool> _launch;

    public MediaFileActions(IAvailabilityInspector inspector, Action<string, bool>? launch = null)
    {
        _inspector = inspector;
        _launch = launch ?? Launch;
    }

    public Task<FileActionResult> OpenFileAsync(string path, CancellationToken token = default) =>
        OpenAsync(path, selectFile: false, directory: false, token);

    public Task<FileActionResult> OpenContainingFolderAsync(string path, CancellationToken token = default) =>
        OpenAsync(path, selectFile: true, directory: false, token);

    public Task<FileActionResult> OpenFolderAsync(string path, CancellationToken token = default) =>
        OpenAsync(path, selectFile: false, directory: true, token);

    private Task<FileActionResult> OpenAsync(string path, bool selectFile, bool directory, CancellationToken token) =>
        Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
                    return new FileActionResult(AvailabilityInspectionStatus.Error, "InvalidPath");

                var status = directory ? InspectFolder(path, token) : _inspector.Inspect(path).Status;
                token.ThrowIfCancellationRequested();
                if (status != AvailabilityInspectionStatus.Present) return new FileActionResult(status);

                _launch(path, selectFile);
                return new FileActionResult(AvailabilityInspectionStatus.Present);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException
                or System.ComponentModel.Win32Exception or ArgumentException or InvalidOperationException)
            {
                token.ThrowIfCancellationRequested();
                // The share can disconnect between verification and shell activation.
                return new FileActionResult(_inspector.IsStorageAvailable(path)
                    ? AvailabilityInspectionStatus.Error : AvailabilityInspectionStatus.StorageUnavailable,
                    error.GetType().Name);
            }
        }, token);

    private AvailabilityInspectionStatus InspectFolder(string path, CancellationToken token) => NetworkReadGuard.Shared.Execute(
        path,
        () =>
        {
            try
            {
                var attributes = File.GetAttributes(path);
                return (attributes & FileAttributes.Directory) != 0
                    ? AvailabilityInspectionStatus.Present : AvailabilityInspectionStatus.Missing;
            }
            catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
            {
                // Use a raw share-root probe here: nesting the shared guard would report its own work busy.
                var root = Path.GetPathRoot(Path.GetFullPath(path))!;
                if (NetworkReadGuard.IsNetworkPath(path))
                {
                    try { return (File.GetAttributes(root) & FileAttributes.Directory) != 0
                        ? AvailabilityInspectionStatus.Missing : AvailabilityInspectionStatus.StorageUnavailable; }
                    catch (Exception rootError) when (rootError is IOException or UnauthorizedAccessException)
                    { return AvailabilityInspectionStatus.StorageUnavailable; }
                }
                return _inspector.IsStorageAvailable(path)
                    ? AvailabilityInspectionStatus.Missing : AvailabilityInspectionStatus.StorageUnavailable;
            }
        },
        () => AvailabilityInspectionStatus.StorageUnavailable,
        () => token.IsCancellationRequested);

    private static void Launch(string path, bool selectFile)
    {
        var start = selectFile
            ? new ProcessStartInfo("explorer.exe") { Arguments = "/select,\"" + path + "\"", UseShellExecute = true }
            : new ProcessStartInfo(path) { UseShellExecute = true };
        using var process = Process.Start(start);
    }
}
