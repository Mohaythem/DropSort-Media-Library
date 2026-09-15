namespace DropSort.UI.Services;

/// <summary>Invalidates work handed off from a view when its record or visual lifetime changes.</summary>
public sealed class PostHandoffRequestLifetime : IDisposable
{
    private readonly object _gate = new();
    private CancellationTokenSource? _active;
    private long _generation;
    private bool _disposed;

    public Request Begin()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            _active?.Cancel();
            _active?.Dispose();
            _active = new CancellationTokenSource();
            return new Request(this, ++_generation, _active.Token);
        }
    }

    public void Cancel()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _generation++;
            _active?.Cancel();
            _active?.Dispose();
            _active = null;
        }
    }

    private bool IsCurrent(long generation, CancellationToken token)
    {
        lock (_gate)
        {
            return !_disposed && generation == _generation && _active?.Token == token && !token.IsCancellationRequested;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(PostHandoffRequestLifetime));
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _active?.Cancel();
            _active?.Dispose();
            _active = null;
        }
    }

    public sealed class Request
    {
        private readonly PostHandoffRequestLifetime _owner;
        private readonly long _generation;
        private readonly CancellationToken _token;

        internal Request(PostHandoffRequestLifetime owner, long generation, CancellationToken token)
        {
            _owner = owner;
            _generation = generation;
            _token = token;
        }

        public CancellationToken Token => _token;
        public bool IsCurrent => _owner.IsCurrent(_generation, _token);
    }
}
