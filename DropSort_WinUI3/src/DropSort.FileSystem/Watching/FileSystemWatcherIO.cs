using DropSort.Application.Dto;
using DropSort.Application.External;
using DropSort.FileSystem.Inspection;

namespace DropSort.FileSystem.Watching;

/// <summary>Bounded, read-only filesystem adapter used by the application watcher.</summary>
public sealed class FileSystemWatcherIO : IFolderWatcherIO
{
    private readonly NetworkReadGuard _reads;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _quietAge;
    public FileSystemWatcherIO(NetworkReadGuard? reads = null, TimeProvider? clock = null, TimeSpan? quietAge = null)
    {
        _reads = reads ?? NetworkReadGuard.Shared;
        _clock = clock ?? TimeProvider.System;
        _quietAge = quietAge ?? TimeSpan.FromSeconds(2);
        if (_quietAge < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(quietAge));
    }

    public FolderWatcherFileFact? ReadFact(string path)
    {
        var canonical = Path.GetFullPath(path);
        try
        {
            return _reads.Execute(canonical, () =>
            {
                AssertNoReparseComponents(canonical);
                return ReadLocal(canonical);
            }, () => new FolderWatcherFileFact(canonical, 0, DateTimeOffset.MinValue, false));
        }
        catch (IOException) { return new FolderWatcherFileFact(canonical, 0, DateTimeOffset.MinValue, false); }
        catch (UnauthorizedAccessException) { return new FolderWatcherFileFact(canonical, 0, DateTimeOffset.MinValue, false); }
    }

    private FolderWatcherFileFact? ReadLocal(string path)
    {
        var before = new FileInfo(path);
        if (!before.Exists) return null;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var size = stream.Length;
        var timestamp = before.LastWriteTimeUtc;
        var after = new FileInfo(path);
        var ready = before.Length == size && after.Exists && after.Length == size && after.LastWriteTimeUtc == timestamp
            && _clock.GetUtcNow() - new DateTimeOffset(timestamp, TimeSpan.Zero) >= _quietAge;
        return new FolderWatcherFileFact(path, size, new DateTimeOffset(timestamp, TimeSpan.Zero), ready);
    }

    public IDisposable Watch(string rootPath, Action<FolderWatcherNotification> onNotification, Action<Exception> onError)
    {
        var canonicalRoot = Path.GetFullPath(rootPath);
        AssertNoReparseComponents(canonicalRoot);
        var watcher = new FileSystemWatcher(canonicalRoot)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
            EnableRaisingEvents = false
        };
        watcher.Created += (_, args) => onNotification(new(args.FullPath, WatcherChangeTypes.Created));
        watcher.Changed += (_, args) => onNotification(new(args.FullPath, WatcherChangeTypes.Changed));
        watcher.Deleted += (_, args) => onNotification(new(args.FullPath, WatcherChangeTypes.Deleted));
        watcher.Renamed += (_, args) => onNotification(new(args.FullPath, WatcherChangeTypes.Renamed, args.OldFullPath));
        watcher.Error += (_, args) => onError(args.GetException());
        watcher.EnableRaisingEvents = true;
        return watcher;
    }

    private static void AssertNoReparseComponents(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full) ?? throw new IOException("Path has no root.");
        var current = root;
        foreach (var part in full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            if (File.Exists(current) || Directory.Exists(current))
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException($"Reparse traversal is not allowed: {current}");
            }
        }
    }
}
