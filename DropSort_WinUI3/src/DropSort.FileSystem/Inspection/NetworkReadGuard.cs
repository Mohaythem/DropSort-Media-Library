namespace DropSort.FileSystem.Inspection;

/// <summary>Bounds synchronous filesystem calls against a network share.</summary>
public sealed class NetworkStorageUnavailableException : IOException
{
    public NetworkStorageUnavailableException(string path)
        : base($"Network storage is unavailable: {path}") { }
}

public sealed class NetworkReadGuard
{
    public static NetworkReadGuard Shared { get; } = new();

    private readonly TimeSpan _timeout;
    private readonly object _gate = new();
    private readonly Dictionary<string, Task> _active = new(StringComparer.OrdinalIgnoreCase);

    public NetworkReadGuard(TimeSpan? timeout = null)
    {
        _timeout = timeout ?? TimeSpan.FromSeconds(2);
        if (_timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
    }

    public T Execute<T>(string path, Func<T> read, Func<T> unavailable, Func<bool>? isCancelled = null)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(unavailable);
        if (isCancelled?.Invoke() == true) throw new OperationCanceledException();
        if (!IsNetworkPath(path))
        {
            var local = read();
            if (isCancelled?.Invoke() == true) throw new OperationCanceledException();
            return local;
        }

        var share = ShareKey(path);
        Task<T> task;
        lock (_gate)
        {
            if (_active.TryGetValue(share, out var active))
            {
                if (!active.IsCompleted) return unavailable();
                _ = active.Exception;
                _active.Remove(share);
            }
            task = Task.Run(read);
            _active[share] = task;
            _ = task.ContinueWith(t =>
            {
                _ = t.Exception;
                lock (_gate)
                {
                    if (_active.TryGetValue(share, out var current) && ReferenceEquals(current, t))
                        _active.Remove(share);
                }
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var timeoutTicks = (long)(_timeout.TotalSeconds * System.Diagnostics.Stopwatch.Frequency);
        while (!task.IsCompleted)
        {
            var elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - started;
            if (elapsed >= timeoutTicks)
                return unavailable();
            if (isCancelled?.Invoke() == true) throw new OperationCanceledException();
            var remainingMs = Math.Max(1, Math.Min(50, (int)((timeoutTicks - elapsed) * 1000L / System.Diagnostics.Stopwatch.Frequency)));
            _ = Task.WhenAny(task, Task.Delay(remainingMs)).GetAwaiter().GetResult();
        }

        RemoveActive(share, task);
        if (isCancelled?.Invoke() == true) throw new OperationCanceledException();
        return task.GetAwaiter().GetResult();
    }

    private void RemoveActive(string share, Task task)
    {
        lock (_gate)
        {
            if (_active.TryGetValue(share, out var current) && ReferenceEquals(current, task))
                _active.Remove(share);
        }
    }

    public static bool IsNetworkPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        return (path.StartsWith(@"\\", StringComparison.Ordinal) &&
            !path.StartsWith(@"\\?\", StringComparison.Ordinal) &&
            !path.StartsWith(@"\\.\", StringComparison.Ordinal)) ||
            path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase);
    }

    private static string ShareKey(string path)
    {
        var normalized = path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)
            ? @"\\" + path[8..]
            : path;
        var parts = normalized.TrimStart('\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? $@"\\{parts[0]}\{parts[1]}" : normalized;
    }
}
