using System.Text.Json;
using DropSort.Application.Dto;
using DropSort.Application.External;

namespace DropSort.Infrastructure.Watching;

public sealed class JsonFolderWatcherStateStore : IFolderWatcherStateStore
{
    private readonly string _path;
    private readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.General) { WriteIndented = true };

    public JsonFolderWatcherStateStore(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("State path is required.", nameof(path));
        _path = Path.GetFullPath(path);
    }

    public async Task<FolderWatcherState> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path)) return FolderWatcherState.Empty;
        await using var stream = File.OpenRead(_path);
        return await JsonSerializer.DeserializeAsync<FolderWatcherState>(stream, _options, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Folder watcher state is empty.");
    }

    public async Task SaveAsync(FolderWatcherState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        var directory = Path.GetDirectoryName(_path);
        if (string.IsNullOrWhiteSpace(directory)) throw new InvalidOperationException("State path has no directory.");
        Directory.CreateDirectory(directory);
        var temporary = _path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, state, _options, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, _path, true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { /* preserve the original state on cleanup failure */ }
        }
    }
}
