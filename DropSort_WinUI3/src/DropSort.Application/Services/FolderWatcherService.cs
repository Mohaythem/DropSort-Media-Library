using System.Collections.Concurrent;
using System.Collections.Immutable;
using DropSort.Application.Dto;
using DropSort.Application.External;
using DropSort.Domain.Media.Discovery;

namespace DropSort.Application.Services;

/// <summary>Read-only folder observation. It never writes the media catalog or changes media files.</summary>
public sealed class FolderWatcherService : IDisposable
{
    private readonly IMediaDiscoveryService _discovery;
    private readonly IFolderWatcherStateStore _stateStore;
    private readonly IFolderWatcherIO _io;
    private readonly Func<IReadOnlyList<string>> _registeredPaths;
    private readonly TimeSpan _periodicInterval;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _startGate = new();
    private readonly object _scanCancellationGate = new();
    private readonly Dictionary<string, FolderState> _folders = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, WatcherRegistration> _watchers = new(StringComparer.OrdinalIgnoreCase);
    private long _watcherGeneration;
    private Task? _loadTask;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _scanCancellation;
    private Timer? _timer;
    private Timer? _dirtyTimer;
    private readonly object _dirtyGate = new();
    private readonly HashSet<string> _dirtyRoots = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset? _dirtyWindowStart;
    private bool _pumpRunning;
    private bool _rescanRequested;
    private bool _started;
    private volatile bool _disposed;
    public bool IsInitialized => _loadTask?.IsCompletedSuccessfully == true && InitializationError is null;
    public Exception? InitializationError { get; private set; }

    public FolderWatcherService(
        IMediaDiscoveryService discovery,
        IFolderWatcherStateStore stateStore,
        IFolderWatcherIO io,
        Func<IReadOnlyList<string>>? registeredPaths = null,
        TimeSpan? periodicInterval = null,
        TimeProvider? clock = null)
    {
        _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _io = io ?? throw new ArgumentNullException(nameof(io));
        _registeredPaths = registeredPaths ?? (() => []);
        _periodicInterval = periodicInterval ?? TimeSpan.FromSeconds(30);
        _clock = clock ?? TimeProvider.System;
    }

    public event EventHandler<FolderWatcherChangedEventArgs>? Changed;

    public IReadOnlyList<FolderWatcherSnapshot> Snapshot
    {
        get
        {
            lock (_folders)
                return _folders.Values.Select(folder => folder.Snapshot with
                {
                    Files = folder.Snapshot.Files.ToImmutableArray(),
                    Changes = folder.Snapshot.Changes.ToImmutableArray(),
                    Warnings = folder.Snapshot.Warnings.ToImmutableArray()
                }).ToImmutableArray();
        }
    }

    public IReadOnlyList<FolderWatcherSnapshot> GetSnapshot() => Snapshot;

    public void Start()
    {
        ThrowIfDisposed();
        lock (_startGate)
        {
            ThrowIfDisposed();
            if (_started) return;
            _started = true;
            _loadTask = Task.Run(() => InitializeSafeAsync(_lifetime.Token));
            _timer = new Timer(_ => QueueScan(), null, _periodicInterval, _periodicInterval);
        }
    }

    public async Task<FolderWatcherSnapshot> AddFolderAsync(string path, CancellationToken cancellationToken = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var linkedToken = linkedCts.Token;
        await EnsureStartedAsync(linkedToken).ConfigureAwait(false);
        var canonical = Canonical(path);
        await _gate.WaitAsync(linkedToken).ConfigureAwait(false);
        try
        {
            bool previousExisted;
            bool previousEnabled = false;
            FolderWatcherSnapshot? previousSnapshot = null;
            lock (_folders)
            {
                if (_folders.TryGetValue(canonical, out var existing))
                {
                    previousExisted = true;
                    previousEnabled = existing.Enabled;
                    previousSnapshot = existing.Snapshot;
                    existing.Enabled = true;
                    existing.Snapshot = existing.Snapshot with { Enabled = true };
                }
                else
                {
                    previousExisted = false;
                    _folders[canonical] = new FolderState(canonical, true, EmptySnapshot(canonical, true));
                }
            }
            try
            {
                await SaveStateAsync(linkedToken).ConfigureAwait(false);
            }
            catch
            {
                lock (_folders)
                {
                    if (previousExisted)
                    {
                        var folder = _folders[canonical];
                        folder.Enabled = previousEnabled;
                        folder.Snapshot = previousSnapshot!;
                    }
                    else
                    {
                        _folders.Remove(canonical);
                    }
                }
                throw;
            }
            AttachWatch(canonical);
            var scanned = (await ScanOneAsync(canonical, linkedToken).ConfigureAwait(false)).Snapshot;
            if (scanned.Status != FolderWatcherRootStatus.Cancelled)
                await SaveStateAsync(linkedToken).ConfigureAwait(false);
            return scanned;
        }
        finally { _gate.Release(); }
    }

    public async Task<FolderWatcherSnapshot> SetEnabledAsync(string path, bool enabled, CancellationToken cancellationToken = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var linkedToken = linkedCts.Token;
        await EnsureStartedAsync(linkedToken).ConfigureAwait(false);
        var canonical = Canonical(path);
        await _gate.WaitAsync(linkedToken).ConfigureAwait(false);
        try
        {
            bool previousEnabled;
            FolderWatcherSnapshot previousSnapshot;
            lock (_folders)
            {
                if (!_folders.TryGetValue(canonical, out var folder))
                    throw new KeyNotFoundException($"Folder is not watched: {canonical}");
                previousEnabled = folder.Enabled;
                previousSnapshot = folder.Snapshot;
                folder.Enabled = enabled;
                folder.Snapshot = folder.Snapshot with { Enabled = enabled };
            }
            try
            {
                await SaveStateAsync(linkedToken).ConfigureAwait(false);
            }
            catch
            {
                lock (_folders)
                {
                    if (_folders.TryGetValue(canonical, out var folder))
                    {
                        folder.Enabled = previousEnabled;
                        folder.Snapshot = previousSnapshot;
                    }
                }
                throw;
            }
            if (enabled) AttachWatch(canonical); else DisposeWatcher(canonical);
        }
        finally { _gate.Release(); }
        return GetRequired(canonical).Snapshot;
    }

    public async Task<bool> RemoveFolderAsync(string path, CancellationToken cancellationToken = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var linkedToken = linkedCts.Token;
        await EnsureStartedAsync(linkedToken).ConfigureAwait(false);
        var canonical = Canonical(path);
        await _gate.WaitAsync(linkedToken).ConfigureAwait(false);
        try
        {
            FolderState? removed;
            lock (_folders)
            {
                if (!_folders.Remove(canonical, out removed))
                    return false;
            }
            try
            {
                await SaveStateAsync(linkedToken).ConfigureAwait(false);
            }
            catch
            {
                lock (_folders)
                {
                    _folders[canonical] = removed;
                }
                throw;
            }
            DisposeWatcher(canonical);
            RaiseChanged(removed.Snapshot);
            return true;
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<FolderWatcherSnapshot>> ScanNowAsync(CancellationToken cancellationToken = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var linkedToken = linkedCts.Token;
        await EnsureStartedAsync(linkedToken).ConfigureAwait(false);
        await _gate.WaitAsync(linkedToken).ConfigureAwait(false);
        try
        {
            CancellationToken scanToken;
            lock (_scanCancellationGate)
            {
                _scanCancellation?.Cancel();
                _scanCancellation?.Dispose();
                _scanCancellation = CancellationTokenSource.CreateLinkedTokenSource(linkedToken);
                scanToken = _scanCancellation.Token;
            }
            FolderState[] folders;
            lock (_folders) folders = _folders.Values.Where(folder => folder.Enabled).ToArray();
            var results = new List<FolderWatcherSnapshot>(folders.Length);
            foreach (var folder in folders)
            {
                if (scanToken.IsCancellationRequested)
                {
                    results.Add(folder.Snapshot with { Status = FolderWatcherRootStatus.Cancelled, IsScanning = false });
                    continue;
                }
                results.Add((await ScanOneAsync(folder.Path, scanToken).ConfigureAwait(false)).Snapshot);
            }
            if (!scanToken.IsCancellationRequested && !results.Any(r => r.Status == FolderWatcherRootStatus.Cancelled))
                await SaveStateAsync(linkedToken).ConfigureAwait(false);
            return results.ToImmutableArray();
        }
        finally
        {
            lock (_scanCancellationGate)
            {
                _scanCancellation?.Dispose();
                _scanCancellation = null;
            }
            _gate.Release();
        }
    }

    public void CancelScan()
    {
        lock (_scanCancellationGate)
        {
            try { _scanCancellation?.Cancel(); }
            catch (ObjectDisposedException) { }
        }
    }

    public async Task IgnoreChangesAsync(IEnumerable<string> changeKeys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changeKeys);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var linkedToken = linkedCts.Token;
        await EnsureStartedAsync(linkedToken).ConfigureAwait(false);
        var keys = changeKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        await _gate.WaitAsync(linkedToken).ConfigureAwait(false);
        try
        {
            Dictionary<string, FolderWatcherSnapshot> previousSnapshots;
            lock (_folders)
            {
                previousSnapshots = _folders.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Snapshot, StringComparer.OrdinalIgnoreCase);
                foreach (var folder in _folders.Values)
                    folder.Snapshot = folder.Snapshot with { Changes = folder.Snapshot.Changes.Where(change => !keys.Contains(change.Key)).ToImmutableArray() };
            }
            try
            {
                await SaveStateAsync(linkedToken).ConfigureAwait(false);
            }
            catch
            {
                lock (_folders)
                {
                    foreach (var (folderPath, snapshot) in previousSnapshots)
                    {
                        if (_folders.TryGetValue(folderPath, out var folder))
                            folder.Snapshot = snapshot;
                    }
                }
                throw;
            }
        }
        finally { _gate.Release(); }
        foreach (var snapshot in Snapshot) RaiseChanged(snapshot);
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var state = await _stateStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (state.SchemaVersion != 1) throw new InvalidDataException($"Unsupported folder watcher state version: {state.SchemaVersion}");
        lock (_folders)
        {
            foreach (var item in state.Folders)
            {
                var path = Canonical(item.Path);
                var baseline = state.Baselines.TryGetValue(path, out var facts) ? facts : [];
                var changes = state.PendingChanges.TryGetValue(path, out var pending) ? pending : [];
                var missed = state.MissedEvents.TryGetValue(path, out var wasMissed) && wasMissed;
                _folders[path] = new FolderState(path, item.Enabled, new FolderWatcherSnapshot(path, item.Enabled,
                    baseline.Count == 0 ? FolderWatcherRootStatus.Unavailable : FolderWatcherRootStatus.Complete,
                    null, baseline, changes, [], false, missed));
            }
        }
        foreach (var folder in Snapshot.Where(snapshot => snapshot.Enabled)) AttachWatch(folder.Path);
    }

    private async Task InitializeSafeAsync(CancellationToken cancellationToken)
    {
        try { await InitializeAsync(cancellationToken).ConfigureAwait(false); }
        catch (Exception error)
        {
            InitializationError = error;
            lock (_folders)
                foreach (var folder in _folders.Values)
                    folder.Snapshot = folder.Snapshot with { Status = FolderWatcherRootStatus.Failed, Warnings = [error.Message] };
            var snapshots = Snapshot;
            if (snapshots.Count == 0)
                RaiseChanged(EmptySnapshot(string.Empty, false) with { Status = FolderWatcherRootStatus.Failed, Warnings = [error.Message] });
            else
                foreach (var snapshot in snapshots) RaiseChanged(snapshot);
        }
    }

    private async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        Start();
        if (_loadTask is not null) await _loadTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (InitializationError is not null)
            throw new InvalidDataException(InitializationError.Message, InitializationError);
    }

    private async Task<FolderState> ScanOneAsync(string path, CancellationToken cancellationToken)
    {
        var folder = GetRequired(path);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_folders) folder.Snapshot = folder.Snapshot with { IsScanning = true };
        try
        {
            var discovered = await Task.Run(() => _discovery.Discover(path, true, isCancelled: () => cancellationToken.IsCancellationRequested), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var warnings = discovered.Where(item => item.Issue is not null).Select(item => item.Issue!.Message).Distinct().ToImmutableArray();
            var unavailable = discovered.Any(item => item.Issue?.Code == DiscoveryErrorCode.StorageUnavailable);
            var hasErrors = discovered.Any(item => item.Issue is not null);
            var status = cancellationToken.IsCancellationRequested ? FolderWatcherRootStatus.Cancelled : unavailable ? FolderWatcherRootStatus.Unavailable : hasErrors ? FolderWatcherRootStatus.Partial : FolderWatcherRootStatus.Complete;
            var distinctDiscovered = discovered
                .Where(item => item.FileSize is not null && item.Observation is not null && item.Issue is null)
                .DistinctBy(item => item.Path, StringComparer.OrdinalIgnoreCase);
            var currentBuilder = new List<FolderWatcherFileFact>();
            foreach (var item in distinctDiscovered)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var ioFact = _io.ReadFact(item.Path);
                if (ioFact is not null && ioFact.Size >= 0 && ioFact.LastWriteUtc != DateTimeOffset.MinValue)
                    currentBuilder.Add(new FolderWatcherFileFact(item.Path, ioFact.Size, ioFact.LastWriteUtc, ioFact.IsReady));
                else
                    currentBuilder.Add(new FolderWatcherFileFact(item.Path, item.FileSize!.Value, new DateTimeOffset(item.Observation!.LastWriteTimeUtc, TimeSpan.Zero), false));
            }
            var current = currentBuilder.ToImmutableArray();
            var changes = status == FolderWatcherRootStatus.Complete
                ? FilterRegisteredNew(folder.Snapshot.Changes, MergePending(folder.Snapshot.Changes, Diff(folder.Snapshot.Files, current)))
                : folder.Snapshot.Changes;
            cancellationToken.ThrowIfCancellationRequested();
            lock (_folders)
            {
                // Incomplete or cancelled scans preserve the last complete baseline and pending alerts.
                folder.Snapshot = folder.Snapshot with { Status = status, LastScanUtc = _clock.GetUtcNow(), Files = status == FolderWatcherRootStatus.Complete ? current : folder.Snapshot.Files, Changes = changes, Warnings = warnings, IsScanning = false };
            }
            if (status == FolderWatcherRootStatus.Complete) TryReattachAndClearMissedEvents(folder);
            RaiseChanged(folder.Snapshot);
            return folder;
        }
        catch (OperationCanceledException)
        {
            lock (_folders) folder.Snapshot = folder.Snapshot with { Status = FolderWatcherRootStatus.Cancelled, IsScanning = false };
            RaiseChanged(folder.Snapshot);
            return folder;
        }
        catch (Exception error)
        {
            lock (_folders) folder.Snapshot = folder.Snapshot with { Status = FolderWatcherRootStatus.Failed, Warnings = [error.Message], IsScanning = false };
            RaiseChanged(folder.Snapshot);
            return folder;
        }
    }

    private static IReadOnlyList<FolderWatcherChange> Diff(IReadOnlyList<FolderWatcherFileFact> baseline, IReadOnlyList<FolderWatcherFileFact> current)
    {
        var oldByPath = baseline.ToDictionary(item => item.Path, StringComparer.OrdinalIgnoreCase);
        var newByPath = current.ToDictionary(item => item.Path, StringComparer.OrdinalIgnoreCase);
        var changes = new List<FolderWatcherChange>();
        foreach (var item in current)
        {
            if (!oldByPath.TryGetValue(item.Path, out var old)) changes.Add(new(ChangeKey(item.Path, FolderWatcherChangeKind.New), item.Path, FolderWatcherChangeKind.New, item));
            else if (old.Size != item.Size || old.LastWriteUtc != item.LastWriteUtc || old.IsReady != item.IsReady)
                changes.Add(new(ChangeKey(item.Path, FolderWatcherChangeKind.Modified), item.Path, FolderWatcherChangeKind.Modified, item, old));
        }
        foreach (var item in baseline.Where(item => !newByPath.ContainsKey(item.Path)))
            changes.Add(new(ChangeKey(item.Path, FolderWatcherChangeKind.Missing), item.Path, FolderWatcherChangeKind.Missing, null, item));
        var moveCandidates = changes.Where(change => change.Kind == FolderWatcherChangeKind.New && change.Current is { IsReady: true })
            .Select(change => (Change: change, Matches: baseline.Where(old => !newByPath.ContainsKey(old.Path)
                && old.Size == change.Current!.Size && old.LastWriteUtc == change.Current.LastWriteUtc).ToArray()))
            .Where(item => item.Matches.Length == 1)
            .Where(item => changes.Count(other => other.Kind == FolderWatcherChangeKind.New && other.Current!.Size == item.Change.Current!.Size && other.Current.LastWriteUtc == item.Change.Current.LastWriteUtc) == 1)
            .ToArray();
        foreach (var move in moveCandidates)
        {
            var missing = changes.FirstOrDefault(change => change.Kind == FolderWatcherChangeKind.Missing && change.Baseline?.Path.Equals(move.Matches[0].Path, StringComparison.OrdinalIgnoreCase) == true);
            if (missing is null) continue;
            changes.Remove(move.Change);
            changes.Remove(missing);
            changes.Add(new(ChangeKey(move.Change.Path, FolderWatcherChangeKind.PotentialMove), move.Change.Path,
                FolderWatcherChangeKind.PotentialMove, move.Change.Current, missing.Baseline, missing.Path,
                "A unique size/timestamp match suggests a move. Review before relinking."));
        }
        return changes;
    }

    private static IReadOnlyList<FolderWatcherChange> MergePending(IReadOnlyList<FolderWatcherChange> pending, IReadOnlyList<FolderWatcherChange> detected)
    {
        var result = pending.ToDictionary(change => change.Key, StringComparer.OrdinalIgnoreCase);
        foreach (var change in detected)
        {
            if (change.Kind == FolderWatcherChangeKind.PotentialMove)
            {
                foreach (var stale in result.Values.Where(item =>
                    item.Path.Equals(change.Path, StringComparison.OrdinalIgnoreCase) ||
                    item.Path.Equals(change.RelatedPath, StringComparison.OrdinalIgnoreCase)).ToArray())
                    result.Remove(stale.Key);
                result[change.Key] = change;
                continue;
            }
            var existingNew = result.Values.FirstOrDefault(item => item.Path.Equals(change.Path, StringComparison.OrdinalIgnoreCase) && item.Kind == FolderWatcherChangeKind.New);
            var existingMissing = result.Values.FirstOrDefault(item => item.Path.Equals(change.Path, StringComparison.OrdinalIgnoreCase) && item.Kind == FolderWatcherChangeKind.Missing);
            if (existingMissing is not null && change.Kind != FolderWatcherChangeKind.Missing)
            {
                result.Remove(existingMissing.Key);
                if (change.Kind == FolderWatcherChangeKind.New &&
                    existingNew is null &&
                    existingMissing.Baseline is not null &&
                    change.Current is not null &&
                    existingMissing.Baseline.Size == change.Current.Size &&
                    existingMissing.Baseline.LastWriteUtc == change.Current.LastWriteUtc)
                {
                    continue;
                }
            }
            if (existingNew is not null && change.Kind == FolderWatcherChangeKind.Modified)
            {
                result[existingNew.Key] = existingNew with { Current = change.Current };
                continue;
            }
            if (existingNew is not null && change.Kind == FolderWatcherChangeKind.Missing)
                result.Remove(existingNew.Key);
            result[change.Key] = change;
        }
        return result.Values.ToImmutableArray();
    }

    private IReadOnlyList<FolderWatcherChange> FilterRegisteredNew(
        IReadOnlyList<FolderWatcherChange> pending,
        IReadOnlyList<FolderWatcherChange> changes)
    {
        var registered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in _registeredPaths())
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            try { registered.Add(Canonical(path)); } catch (ArgumentException) { }
        }
        if (registered.Count == 0) return changes;
        return changes.Where(change => change.Kind != FolderWatcherChangeKind.New || !registered.Contains(Canonical(change.Path))).ToImmutableArray();
    }

    private static string ChangeKey(string path, FolderWatcherChangeKind kind) => $"{kind}:{path}";

    private void AttachWatch(string path)
    {
        if (_disposed) return;
        if (IsWatcherActive(path)) return;
        DisposeWatcher(path);

        var generation = Interlocked.Increment(ref _watcherGeneration);
        var registration = new WatcherRegistration(generation);
        if (!_watchers.TryAdd(path, registration)) return;

        try
        {
            var subscription = _io.Watch(
                path,
                _ => OnWatcherNotification(path, generation),
                error => OnWatcherError(path, generation, error));

            if (!registration.SetSubscription(subscription))
            {
                _watchers.TryRemove(new KeyValuePair<string, WatcherRegistration>(path, registration));
            }
            if (_disposed) DisposeWatcher(path);
        }
        catch (Exception error)
        {
            _watchers.TryRemove(new KeyValuePair<string, WatcherRegistration>(path, registration));
            registration.Dispose();
            MarkError(path, error);
        }
    }

    private bool IsWatcherActive(string path) =>
        _watchers.TryGetValue(path, out var registration) && registration.IsActive;

    private void TryReattachAndClearMissedEvents(FolderState folder)
    {
        if (folder.Enabled && !IsWatcherActive(folder.Path))
        {
            AttachWatch(folder.Path);
        }

        lock (_folders)
        {
            if (IsWatcherActive(folder.Path))
            {
                folder.Snapshot = folder.Snapshot with { MissedEvents = false };
            }
        }
    }

    private void OnWatcherNotification(string path, long generation)
    {
        if (_disposed) return;
        if (!_watchers.TryGetValue(path, out var reg) || reg.Generation != generation) return;
        MarkDirty(path);
    }

    private void OnWatcherError(string path, long generation, Exception error)
    {
        if (_disposed) return;
        if (!_watchers.TryGetValue(path, out var reg) || reg.Generation != generation) return;
        if (!_watchers.TryRemove(new KeyValuePair<string, WatcherRegistration>(path, reg))) return;
        reg.Dispose();
        MarkError(path, error, detach: false);
    }

    private void MarkDirty(string path)
    {
        if (_disposed) return;
        lock (_folders)
        {
            if (!_folders.TryGetValue(path, out var folder) || !folder.Enabled)
                return;
        }

        lock (_dirtyGate)
        {
            if (_disposed) return;
            _dirtyRoots.Add(path);

            var now = _clock.GetUtcNow();
            _dirtyWindowStart ??= now;
            var elapsed = now - _dirtyWindowStart.Value;
            var maxWait = TimeSpan.FromSeconds(2);
            var trailing = TimeSpan.FromMilliseconds(500);
            var delay = elapsed >= maxWait ? TimeSpan.Zero : (trailing < maxWait - elapsed ? trailing : maxWait - elapsed);

            _dirtyTimer ??= new Timer(_ => RunDirtyScan(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _dirtyTimer.Change(delay, Timeout.InfiniteTimeSpan);
        }
    }

    private void RunDirtyScan()
    {
        lock (_dirtyGate)
        {
            _dirtyRoots.Clear();
            _dirtyWindowStart = null;
        }
        QueueScan();
    }

    private void QueueScan()
    {
        if (_disposed || InitializationError is not null) return;
        lock (_dirtyGate)
        {
            if (_disposed) return;
            if (_pumpRunning)
            {
                _rescanRequested = true;
                return;
            }
            _pumpRunning = true;
            _rescanRequested = false;
            Task.Run(RunDirtyPumpAsync);
        }
    }

    private async Task RunDirtyPumpAsync()
    {
        while (!_disposed && !_lifetime.IsCancellationRequested)
        {
            try { await ScanNowAsync(_lifetime.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (Exception error)
            {
                foreach (var snapshot in Snapshot)
                {
                    FolderWatcherSnapshot? updated = null;
                    lock (_folders)
                        if (_folders.TryGetValue(snapshot.Path, out var folder))
                        {
                            folder.Snapshot = folder.Snapshot with { Status = FolderWatcherRootStatus.Failed, Warnings = [error.Message] };
                            updated = folder.Snapshot;
                        }
                    if (updated is not null) RaiseChanged(updated);
                }
            }

            lock (_dirtyGate)
            {
                if (!_rescanRequested || _disposed || _lifetime.IsCancellationRequested)
                {
                    _pumpRunning = false;
                    _rescanRequested = false;
                    return;
                }
                _rescanRequested = false;
            }
        }

        lock (_dirtyGate)
        {
            _pumpRunning = false;
            _rescanRequested = false;
        }
    }

    private void MarkError(string path, Exception error, bool detach = true)
    {
        if (_disposed) return;
        if (detach) DisposeWatcher(path);
        FolderWatcherSnapshot? updatedSnapshot = null;
        lock (_folders)
        {
            if (_folders.TryGetValue(path, out var folder))
            {
                folder.Snapshot = folder.Snapshot with
                {
                    MissedEvents = true,
                    Warnings = folder.Snapshot.Warnings.Append(error.Message).Distinct().ToImmutableArray()
                };
                updatedSnapshot = folder.Snapshot;
            }
        }
        if (updatedSnapshot is not null) RaiseChanged(updatedSnapshot);
    }

    private void DisposeWatcher(string path)
    {
        if (_watchers.TryRemove(path, out var watcher)) watcher.Dispose();
    }

    private async Task SaveStateAsync(CancellationToken cancellationToken)
    {
        if (_disposed || InitializationError is not null) return;
        cancellationToken.ThrowIfCancellationRequested();
        FolderWatcherState state;
        lock (_folders)
        {
            state = new FolderWatcherState(1, _folders.Values.Select(folder => new FolderWatcherWatchedFolder(folder.Path, folder.Enabled)).ToImmutableArray(),
                _folders.Values.ToDictionary(folder => folder.Path, folder => folder.Snapshot.Files, StringComparer.OrdinalIgnoreCase),
                _folders.Values.ToDictionary(folder => folder.Path, folder => folder.Snapshot.Changes, StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase),
                _folders.Values.ToDictionary(folder => folder.Path, folder => folder.Snapshot.MissedEvents, StringComparer.OrdinalIgnoreCase));
        }
        await _stateStore.SaveAsync(state, cancellationToken).ConfigureAwait(false);
    }

    private FolderState GetRequired(string path)
    {
        lock (_folders) return _folders.TryGetValue(path, out var folder) ? folder : throw new KeyNotFoundException(path);
    }

    private static string Canonical(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) throw new ArgumentException("Folder path must be absolute.", nameof(path));
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full);
        return string.Equals(full, root, StringComparison.OrdinalIgnoreCase)
            ? full
            : full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static FolderWatcherSnapshot EmptySnapshot(string path, bool enabled) => new(path, enabled, FolderWatcherRootStatus.Unavailable, null, [], [], [], false, false);
    private static bool IsSameOrChild(string candidate, string root)
    {
        var relative = Path.GetRelativePath(root, candidate);
        return relative == "." || (!relative.Equals("..", StringComparison.Ordinal) && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !Path.IsPathRooted(relative));
    }
    private void RaiseChanged(FolderWatcherSnapshot snapshot) => Changed?.Invoke(this, new FolderWatcherChangedEventArgs(snapshot));
    private void ThrowIfDisposed() { if (_disposed) throw new ObjectDisposedException(nameof(FolderWatcherService)); }

    public void Dispose()
    {
        lock (_startGate)
        {
            if (_disposed) return;
            _disposed = true;
            _lifetime.Cancel();
            CancelScan();
            _timer?.Dispose();
        }
        lock (_dirtyGate) _dirtyTimer?.Dispose();
        var registrations = _watchers.Values.ToArray();
        _watchers.Clear();
        foreach (var watcher in registrations) watcher.Dispose();
        // Keep _lifetime undisposed so in-flight and unwinding tasks can observe cancellation without ObjectDisposedException.
    }

    private sealed class WatcherRegistration
    {
        private readonly object _gate = new();
        private IDisposable? _subscription;
        private bool _disposed;

        public long Generation { get; }
        public bool IsActive
        {
            get
            {
                lock (_gate) return _subscription is not null && !_disposed;
            }
        }

        public WatcherRegistration(long generation)
        {
            Generation = generation;
        }

        public bool SetSubscription(IDisposable subscription)
        {
            lock (_gate)
            {
                if (!_disposed) { _subscription = subscription; return true; }
            }
            subscription.Dispose();
            return false;
        }

        public void Dispose()
        {
            IDisposable? toDispose;
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                toDispose = _subscription;
                _subscription = null;
            }
            toDispose?.Dispose();
        }
    }

    private sealed class FolderState(string path, bool enabled, FolderWatcherSnapshot snapshot)
    {
        public string Path { get; } = path;
        public bool Enabled { get; set; } = enabled;
        public FolderWatcherSnapshot Snapshot { get; set; } = snapshot;
    }
}

public sealed class FolderWatcherChangedEventArgs(FolderWatcherSnapshot snapshot) : EventArgs
{
    public FolderWatcherSnapshot Snapshot { get; } = snapshot;
}
