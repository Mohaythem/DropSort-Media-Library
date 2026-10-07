using DropSort.Domain.Core.Operations;
using DropSort.FileSystem.Safety;

namespace DropSort.FileSystem.Inspection;

public static class CrashTempFileInspector
{
    private const string TempFilePattern = "*.dropsort-*.tmp";

    /// <summary>
    /// Detects and reports stale DropSort temporary files (*.dropsort-*.tmp) in the specified
    /// directories. DropSort NEVER automatically deletes these files on startup or scan;
    /// they are reported so that explicit approval or review can occur.
    /// </summary>
    public static IReadOnlyList<StaleTempFileInfo> FindStaleTempFiles(IEnumerable<string> directoryRoots, Func<bool>? isCancelled = null)
    {
        ArgumentNullException.ThrowIfNull(directoryRoots);
        var results = new List<StaleTempFileInfo>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in directoryRoots)
        {
            if (isCancelled?.Invoke() == true) throw new OperationCanceledException();
            if (string.IsNullOrWhiteSpace(root)) continue;

            if (NetworkReadGuard.IsNetworkPath(root))
            {
                // Read-only diagnostics must not hold up a library check on an unavailable share.
                var abandoned = 0;
                var networkFiles = NetworkReadGuard.Shared.Execute<IReadOnlyList<StaleTempFileInfo>>(
                    root, () => ReadNetworkTemps(root, () => Volatile.Read(ref abandoned) != 0 || isCancelled?.Invoke() == true),
                    () => { Volatile.Write(ref abandoned, 1); return []; }, isCancelled);
                foreach (var file in networkFiles)
                    if (seenPaths.Add(file.FilePath)) results.Add(file);
                continue;
            }
            if (!Directory.Exists(root)) continue;

            try
            {
                var directoryInfo = new DirectoryInfo(root);
                var files = directoryInfo.EnumerateFiles(TempFilePattern, new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint
                });

                foreach (var file in files)
                {
                    if (isCancelled?.Invoke() == true) throw new OperationCanceledException();
                    if (!seenPaths.Add(file.FullName)) continue;
                    results.Add(new StaleTempFileInfo(
                        file.FullName,
                        file.Name,
                        file.Length,
                        file.LastWriteTimeUtc));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Unreadable directories are skipped safely without throwing
            }
        }

        return results;
    }

    private static IReadOnlyList<StaleTempFileInfo> ReadNetworkTemps(string root, Func<bool> isAbandoned)
    {
        try
        {
            return new DirectoryInfo(root).EnumerateFiles(TempFilePattern, new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            }).TakeWhile(_ => !isAbandoned())
                .Select(file => new StaleTempFileInfo(file.FullName, file.Name, file.Length, file.LastWriteTimeUtc)).ToList();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return []; }
    }
}
