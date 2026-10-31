using TBDMovies.Application.External;
using TBDMovies.Domain.Media.Discovery;
using TBDMovies.FileSystem.Safety;

namespace TBDMovies.FileSystem.Inspection;

/// <summary>Read-only availability and scan-fact check. Called before acquiring a catalog transaction.</summary>
public sealed class ImportFileValidator(NetworkReadGuard? networkReads = null) : IImportFileValidator
{
    private readonly NetworkReadGuard _reads = networkReads ?? NetworkReadGuard.Shared;

    public void Validate(DiscoveredMedia item, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Path.IsPathFullyQualified(item.Path)) throw new IOException("The media path must be absolute.");
        var path = Path.GetFullPath(item.Path);
        _reads.Execute(path, () =>
        {
            PathPolicy.AssertNoReparseComponents(path);
            var info = new FileInfo(path);
            if (!info.Exists || (info.Attributes & FileAttributes.Directory) != 0)
                throw new IOException("The media file is missing or storage is unavailable.");
            var size = info.Length;
            var modified = info.LastWriteTimeUtc;
            if (size != item.FileSize || item.Observation is { } observed
                && (size != observed.FileSize || modified != observed.LastWriteTimeUtc))
                throw new IOException("The media file changed after scanning. Scan it again before importing.");
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length != size) throw new IOException("The media file changed after scanning.");
            return true;
        }, () => throw new NetworkStorageUnavailableException(path), () => cancellationToken.IsCancellationRequested);
    }
}
