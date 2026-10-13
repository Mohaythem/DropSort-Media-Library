using DropSort.Application.Dto;

namespace DropSort.Application.External;

public interface IFolderWatcherStateStore
{
    Task<FolderWatcherState> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(FolderWatcherState state, CancellationToken cancellationToken = default);
}

public interface IFolderWatcherIO
{
    FolderWatcherFileFact? ReadFact(string path);
    IDisposable Watch(string rootPath, Action<FolderWatcherNotification> onNotification, Action<Exception> onError);
}

public sealed record FolderWatcherNotification(string FullPath, WatcherChangeTypes ChangeType, string? OldFullPath = null);
