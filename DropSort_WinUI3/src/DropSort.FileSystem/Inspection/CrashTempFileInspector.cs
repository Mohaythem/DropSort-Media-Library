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
    public static IReadOnlyList<StaleTempFileInfo> FindStaleTempFiles(IEnumerable<string> directoryRoots)
    {
        ArgumentNullException.ThrowIfNull(directoryRoots);
        var results = new List<StaleTempFileInfo>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in directoryRoots)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;

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
}
