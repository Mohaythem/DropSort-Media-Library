using DropSort.Application.Dto;
using DropSort.Application.External;
using DropSort.Application.Services;
using DropSort.Domain.Media.Discovery;
using DropSort.Domain.Media.Parser;
using DropSort.FileSystem.Watching;
using DropSort.Infrastructure.Watching;
using Xunit;

namespace DropSort.Tests.Application;

public sealed class FolderWatcherServiceTests
{
    [Fact]
    public async Task Complete_scan_reports_new_modified_and_missing_without_catalog_access()
    {
        using var temp = new TemporaryFolder();
        var file = Path.Combine(temp.Path, "Show.S01E01.mkv");
        File.WriteAllText(file, "one");
        var discovery = new FakeDiscovery(() => [Media(file)]);
        var state = new MemoryStateStore();
        using var service = new FolderWatcherService(discovery, state, new FakeIo(), () => []);

        var first = await service.AddFolderAsync(temp.Path);
        Assert.Contains(first.Changes, item => item.Kind == FolderWatcherChangeKind.New);

        File.WriteAllText(file, "two-updated");
        var second = (await service.ScanNowAsync()).Single();
        Assert.Contains(second.Changes, item => item.Kind == FolderWatcherChangeKind.New);

        File.Delete(file);
        discovery.UseResult = true;
        discovery.Result = [];
        var third = (await service.ScanNowAsync()).Single();
        Assert.Contains(third.Changes, item => item.Kind == FolderWatcherChangeKind.Missing);
    }

    [Fact]
    public async Task Partial_scan_preserves_last_complete_baseline_and_pending_changes()
    {
        using var temp = new TemporaryFolder();
        var file = Path.Combine(temp.Path, "Show.S01E01.mkv");
        File.WriteAllText(file, "one");
        var discovery = new FakeDiscovery(() => [Media(file)]);
        var state = new MemoryStateStore();
        using var service = new FolderWatcherService(discovery, state, new FakeIo(), () => []);
        var first = await service.AddFolderAsync(temp.Path);
        discovery.UseResult = true;
        discovery.Result = [DiscoveredMedia.Error(temp.Path, new DiscoveryIssue(DiscoveryErrorCode.DirectoryReadFailed, "partial"))];

        var second = (await service.ScanNowAsync()).Single();
        Assert.Equal(FolderWatcherRootStatus.Partial, second.Status);
        Assert.Equal(first.Files, second.Files);
        Assert.Equal(first.Changes, second.Changes);
    }

    [Fact]
    public async Task Disable_and_remove_stop_scanning_and_persist_configuration()
    {
        using var temp = new TemporaryFolder();
        var discovery = new FakeDiscovery(() => []);
        var state = new MemoryStateStore();
        using var service = new FolderWatcherService(discovery, state, new FakeIo(), () => []);
        await service.AddFolderAsync(temp.Path);
        await service.SetEnabledAsync(temp.Path, false);
        Assert.False(service.Snapshot.Single().Enabled);
        Assert.True(await service.RemoveFolderAsync(temp.Path));
        Assert.Empty(service.Snapshot);
        Assert.Empty(state.Value.Folders);
    }

    [Fact]
    public async Task IgnoreChanges_removes_only_requested_advisory_keys()
    {
        using var temp = new TemporaryFolder();
        var file = Path.Combine(temp.Path, "Show.S01E01.mkv");
        File.WriteAllText(file, "one");
        var discovery = new FakeDiscovery(() => [Media(file)]);
        using var service = new FolderWatcherService(discovery, new MemoryStateStore(), new FakeIo(), () => []);
        var snapshot = await service.AddFolderAsync(temp.Path);
        var key = snapshot.Changes.Single().Key;
        await service.IgnoreChangesAsync([key]);
        Assert.Empty(service.Snapshot.Single().Changes);
    }

    [Fact]
    public async Task Cancelled_scan_keeps_complete_baseline_and_pending_alerts()
    {
        using var temp = new TemporaryFolder();
        var file = Path.Combine(temp.Path, "Show.S01E01.mkv");
        File.WriteAllText(file, "one");
        var discovery = new BlockingDiscovery(() => [Media(file)]);
        using var service = new FolderWatcherService(discovery, new MemoryStateStore(), new FakeIo(), () => []);
        var first = await service.AddFolderAsync(temp.Path);
        File.WriteAllText(file, "two");
        discovery.Result = [Media(file)];
        discovery.BlockNextScan();
        var scan = service.ScanNowAsync();
        await discovery.Started.Task;
        service.CancelScan();
        var result = await scan;
        var snapshot = result.Single();

        Assert.Equal(FolderWatcherRootStatus.Cancelled, snapshot.Status);
        Assert.Equal(first.Files, snapshot.Files);
        Assert.Equal(first.Changes, snapshot.Changes);
    }

    [Fact]
    public async Task Case_only_duplicate_paths_are_one_change()
    {
        using var temp = new TemporaryFolder();
        var file = Path.Combine(temp.Path, "Show.S01E01.mkv");
        File.WriteAllText(file, "one");
        var discovery = new FakeDiscovery(() => [Media(file)]);
        using var service = new FolderWatcherService(discovery, new MemoryStateStore(), new FakeIo(), () => []);

        await service.AddFolderAsync(temp.Path);
        discovery.UseResult = true;
        discovery.Result = [Media(file), Media(file.ToUpperInvariant())];
        var snapshot = (await service.ScanNowAsync()).Single();

        Assert.Single(snapshot.Files);
        Assert.Single(snapshot.Changes);
        Assert.Equal(FolderWatcherChangeKind.New, snapshot.Changes[0].Kind);
    }

    [Fact]
    public async Task Unique_matching_new_and_missing_files_are_reported_as_potential_move()
    {
        using var temp = new TemporaryFolder();
        var oldPath = Path.Combine(temp.Path, "old.mkv");
        var newPath = Path.Combine(temp.Path, "new.mkv");
        File.WriteAllText(oldPath, "same");
        var discovery = new FakeDiscovery(() => [Media(oldPath)]);
        using var service = new FolderWatcherService(discovery, new MemoryStateStore(), new FakeIo(), () => []);
        await service.AddFolderAsync(temp.Path);
        File.Move(oldPath, newPath);
        discovery.UseResult = true;
        discovery.Result = [Media(newPath)];

        var snapshot = (await service.ScanNowAsync()).Single();

        var move = Assert.Single(snapshot.Changes);
        Assert.Equal(FolderWatcherChangeKind.PotentialMove, move.Kind);
        Assert.Equal(newPath, move.Path);
        Assert.Equal(oldPath, move.RelatedPath);
    }

    [Fact]
    public async Task Watcher_notification_burst_coalesces_to_one_scan()
    {
        using var temp = new TemporaryFolder();
        var discovery = new CountingDiscovery(() => []);
        var io = new ControllableIo();
        using var service = new FolderWatcherService(discovery, new MemoryStateStore(), io, () => []);
        await service.AddFolderAsync(temp.Path);
        var baseline = discovery.Count;
        var changed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Changed += (_, _) => changed.TrySetResult(true);

        for (var i = 0; i < 5; i++) io.Notify(temp.Path, WatcherChangeTypes.Changed);
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(baseline + 1, discovery.Count);
    }

    [Fact]
    public async Task Watcher_error_marks_root_as_having_missed_events()
    {
        using var temp = new TemporaryFolder();
        var io = new ControllableIo();
        using var service = new FolderWatcherService(new FakeDiscovery(() => []), new MemoryStateStore(), io, () => []);
        await service.AddFolderAsync(temp.Path);

        io.Fail(new IOException("watch root unavailable"));

        var snapshot = service.Snapshot.Single();
        Assert.True(snapshot.MissedEvents);
        Assert.Contains(snapshot.Warnings, warning => warning.Contains("watch root unavailable", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Dispose_prevents_new_operations_and_does_not_save_after_dispose()
    {
        using var temp = new TemporaryFolder();
        var store = new MemoryStateStore();
        var service = new FolderWatcherService(new FakeDiscovery(() => []), store, new FakeIo(), () => []);
        await service.AddFolderAsync(temp.Path);
        var saves = store.SaveCount;
        service.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => service.AddFolderAsync(temp.Path));
        Assert.Equal(saves, store.SaveCount);
    }

    [Fact]
    public async Task State_store_round_trips_schema_and_pending_change_data()
    {
        using var temp = new TemporaryFolder();
        var path = Path.Combine(temp.Path, "watcher.json");
        var fact = new FolderWatcherFileFact(Path.Combine(temp.Path, "a.mkv"), 3, DateTimeOffset.UtcNow, true);
        var change = new FolderWatcherChange("New:" + fact.Path, fact.Path, FolderWatcherChangeKind.New, fact);
        var expected = new FolderWatcherState(1, [new FolderWatcherWatchedFolder(temp.Path, true)],
            new Dictionary<string, IReadOnlyList<FolderWatcherFileFact>>(StringComparer.OrdinalIgnoreCase) { [temp.Path] = [fact] },
            new Dictionary<string, IReadOnlyList<FolderWatcherChange>>(StringComparer.OrdinalIgnoreCase) { [temp.Path] = [change] },
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase) { [temp.Path] = [change.Key] },
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) { [temp.Path] = true });
        var store = new JsonFolderWatcherStateStore(path);

        await store.SaveAsync(expected);
        var actual = await store.LoadAsync();

        Assert.Equal(expected.SchemaVersion, actual.SchemaVersion);
        Assert.Equal(expected.Folders, actual.Folders);
        Assert.Equal(expected.Baselines[temp.Path], actual.Baselines[temp.Path]);
        Assert.Equal(expected.PendingChanges[temp.Path], actual.PendingChanges[temp.Path]);
        Assert.Equal(expected.IgnoredKeys[temp.Path], actual.IgnoredKeys[temp.Path]);
        Assert.True(actual.MissedEvents[temp.Path]);
        Assert.DoesNotContain(".tmp-", string.Join("\n", Directory.GetFiles(temp.Path)));
    }

    [Fact]
    public void File_system_adapter_reads_quiet_file_and_missing_file_without_writing()
    {
        using var temp = new TemporaryFolder();
        var file = Path.Combine(temp.Path, "quiet.mkv");
        File.WriteAllText(file, "payload");
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddSeconds(-3));
        var before = File.GetLastWriteTimeUtc(file);
        var adapter = new FileSystemWatcherIO();

        var fact = adapter.ReadFact(file);
        var missing = adapter.ReadFact(Path.Combine(temp.Path, "missing.mkv"));

        Assert.NotNull(fact);
        Assert.Equal(7, fact!.Size);
        Assert.True(fact.IsReady);
        Assert.Equal(before, fact.LastWriteUtc.UtcDateTime);
        Assert.Null(missing);
    }

    [Fact]
    public async Task Pending_new_alert_survives_unchanged_scan_and_restart_until_ignored()
    {
        using var temp = new TemporaryFolder();
        var file = Path.Combine(temp.Path, "new.mkv");
        File.WriteAllText(file, "one");
        var store = new MemoryStateStore();
        var discovery = new FakeDiscovery(() => [Media(file)]);
        string key;
        using (var first = new FolderWatcherService(discovery, store, new FakeIo(), () => []))
        {
            var initial = await first.AddFolderAsync(temp.Path);
            key = Assert.Single(initial.Changes).Key;
            var unchanged = (await first.ScanNowAsync()).Single();
            Assert.Contains(unchanged.Changes, item => item.Key == key);
        }

        using var restarted = new FolderWatcherService(discovery, store, new FakeIo(), () => []);
        var restored = await restarted.AddFolderAsync(temp.Path);
        Assert.Contains(restored.Changes, item => item.Key == key);
        await restarted.IgnoreChangesAsync([key]);
        Assert.DoesNotContain(restarted.Snapshot.Single().Changes, item => item.Key == key);
    }

    [Fact]
    public async Task Registered_catalog_path_is_allowed_without_new_advisory()
    {
        using var temp = new TemporaryFolder();
        var file = Path.Combine(temp.Path, "registered.mkv");
        File.WriteAllText(file, "one");
        var discovery = new FakeDiscovery(() => [Media(file)]);
        using var service = new FolderWatcherService(discovery, new MemoryStateStore(), new FakeIo(), () => [file]);

        var snapshot = await service.AddFolderAsync(temp.Path);

        Assert.Single(snapshot.Files);
        Assert.Empty(snapshot.Changes);
    }

    [Fact]
    public async Task Unavailable_scan_then_recovery_preserves_baseline_and_avoids_false_missing()
    {
        using var temp = new TemporaryFolder();
        var file = Path.Combine(temp.Path, "existing.mkv");
        File.WriteAllText(file, "one");
        var discovery = new FakeDiscovery(() => [Media(file)]);
        using var service = new FolderWatcherService(discovery, new MemoryStateStore(), new FakeIo(), () => []);
        var baseline = await service.AddFolderAsync(temp.Path);
        discovery.UseResult = true;
        discovery.Result = [DiscoveredMedia.Error(temp.Path, new DiscoveryIssue(DiscoveryErrorCode.StorageUnavailable, "offline"))];

        var unavailable = (await service.ScanNowAsync()).Single();
        Assert.Equal(FolderWatcherRootStatus.Unavailable, unavailable.Status);
        Assert.Equal(baseline.Files, unavailable.Files);
        Assert.DoesNotContain(unavailable.Changes, item => item.Kind == FolderWatcherChangeKind.Missing);

        discovery.Result = [Media(file)];
        var recovered = (await service.ScanNowAsync()).Single();
        Assert.Equal(FolderWatcherRootStatus.Complete, recovered.Status);
        Assert.Equal(baseline.Files, recovered.Files);
        Assert.DoesNotContain(recovered.Changes, item => item.Kind == FolderWatcherChangeKind.Missing);
    }

    [Fact]
    public async Task Reappeared_file_clears_pending_missing_advisory()
    {
        using var temp = new TemporaryFolder();
        var file = Path.Combine(temp.Path, "reappearing.mkv");
        File.WriteAllText(file, "one");
        var discovery = new FakeDiscovery(() => [Media(file)]);
        using var service = new FolderWatcherService(discovery, new MemoryStateStore(), new FakeIo(), () => []);

        await service.AddFolderAsync(temp.Path);
        File.Delete(file);
        discovery.UseResult = true;
        discovery.Result = [];
        var missing = (await service.ScanNowAsync()).Single();
        Assert.Contains(missing.Changes, change => change.Kind == FolderWatcherChangeKind.Missing);

        File.WriteAllText(file, "one");
        discovery.Result = [Media(file)];
        var recovered = (await service.ScanNowAsync()).Single();

        Assert.DoesNotContain(recovered.Changes, change =>
            change.Kind == FolderWatcherChangeKind.Missing &&
            change.Path.Equals(file, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Initialization_state_failure_does_not_allow_add_to_overwrite_state()
    {
        using var temp = new TemporaryFolder();
        var file = Path.Combine(temp.Path, "should-not-be-added.mkv");
        File.WriteAllText(file, "one");
        var store = new FailingLoadStateStore();
        using var service = new FolderWatcherService(new FakeDiscovery(() => [Media(file)]), store, new FakeIo(), () => []);

        service.Start();
        await Assert.ThrowsAsync<InvalidDataException>(() => service.AddFolderAsync(temp.Path));

        Assert.NotNull(service.InitializationError);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task Corrupted_json_state_is_preserved_when_initialization_fails()
    {
        using var temp = new TemporaryFolder();
        var statePath = Path.Combine(temp.Path, "folder-watcher.json");
        const string corrupted = "{ this is not valid watcher state";
        await File.WriteAllTextAsync(statePath, corrupted);
        var file = Path.Combine(temp.Path, "should-not-be-added.mkv");
        File.WriteAllText(file, "one");
        using var service = new FolderWatcherService(
            new FakeDiscovery(() => [Media(file)]),
            new JsonFolderWatcherStateStore(statePath),
            new FakeIo(),
            () => []);

        service.Start();
        await Assert.ThrowsAsync<InvalidDataException>(() => service.AddFolderAsync(temp.Path));

        Assert.Equal(corrupted, await File.ReadAllTextAsync(statePath));
    }

    [Fact]
    public async Task Unsupported_state_schema_is_reported_without_replacing_state()
    {
        using var temp = new TemporaryFolder();
        var store = new FixedStateStore(new FolderWatcherState(
            99,
            [],
            new Dictionary<string, IReadOnlyList<FolderWatcherFileFact>>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, IReadOnlyList<FolderWatcherChange>>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)));
        var file = Path.Combine(temp.Path, "should-not-be-added.mkv");
        File.WriteAllText(file, "one");
        using var service = new FolderWatcherService(new FakeDiscovery(() => [Media(file)]), store, new FakeIo(), () => []);

        service.Start();
        await Assert.ThrowsAsync<InvalidDataException>(() => service.AddFolderAsync(temp.Path));

        Assert.Equal(0, store.SaveCount);
        Assert.Equal(99, store.Value.SchemaVersion);
    }

    [Fact]
    public async Task Save_failure_is_reported_without_retaining_unsaved_folder()
    {
        using var temp = new TemporaryFolder();
        var file = Path.Combine(temp.Path, "file.mkv");
        File.WriteAllText(file, "one");
        using var service = new FolderWatcherService(new FakeDiscovery(() => [Media(file)]), new FailingStateStore(), new FakeIo(), () => []);

        await Assert.ThrowsAsync<IOException>(() => service.AddFolderAsync(temp.Path));
        Assert.Empty(service.Snapshot);
    }

    [Fact]
    public async Task Add_folder_save_failure_rolls_back_in_memory_configuration()
    {
        using var temp = new TemporaryFolder();
        using var service = new FolderWatcherService(new FakeDiscovery(() => []), new FailingStateStore(), new FakeIo(), () => []);

        await Assert.ThrowsAsync<IOException>(() => service.AddFolderAsync(temp.Path));

        Assert.Empty(service.Snapshot);
    }

    [Fact]
    public void Locked_file_is_reported_as_not_ready_without_writing()
    {
        using var temp = new TemporaryFolder();
        var file = Path.Combine(temp.Path, "locked.mkv");
        File.WriteAllText(file, "payload");
        using var lockHandle = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None);
        var adapter = new FileSystemWatcherIO();

        var fact = adapter.ReadFact(file);

        Assert.NotNull(fact);
        Assert.False(fact!.IsReady);
        Assert.Equal(0, fact.Size);
    }

    [Fact]
    public async Task Scan_handles_one_thousand_file_facts_without_dropping_records()
    {
        using var temp = new TemporaryFolder();
        var facts = Enumerable.Range(0, 1000).Select(index =>
        {
            var path = Path.Combine(temp.Path, $"Show.S01E{index + 1:00}.mkv");
            File.WriteAllText(path, index.ToString());
            return Media(path);
        }).ToArray();
        using var service = new FolderWatcherService(new FakeDiscovery(() => facts), new MemoryStateStore(), new FakeIo(), () => []);

        var snapshot = await service.AddFolderAsync(temp.Path);

        Assert.Equal(1000, snapshot.Files.Count);
        Assert.Equal(1000, snapshot.Changes.Count);
    }

    [Fact]
    public async Task Corrupt_json_is_preserved_across_manual_and_periodic_scans()
    {
        using var temp = new TemporaryFolder();
        var path = Path.Combine(temp.Path, "state.json");
        const string corrupt = "{invalid-json";
        await File.WriteAllTextAsync(path, corrupt);
        using var service = new FolderWatcherService(new FakeDiscovery(() => []),
            new JsonFolderWatcherStateStore(path), new FakeIo(), periodicInterval: TimeSpan.FromMilliseconds(25));
        service.Start();
        await Assert.ThrowsAsync<InvalidDataException>(() => service.ScanNowAsync());
        await Task.Delay(100);
        Assert.NotNull(service.InitializationError);
        Assert.Equal(corrupt, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task Cancellation_during_fact_read_preserves_complete_baseline()
    {
        using var temp = new TemporaryFolder();
        var first = Path.Combine(temp.Path, "first.mkv");
        var second = Path.Combine(temp.Path, "second.mkv");
        File.WriteAllText(first, "one");
        File.WriteAllText(second, "two");
        var discovery = new FakeDiscovery(() => [Media(first)]);
        var io = new ControllableIo();
        using var service = new FolderWatcherService(discovery, new MemoryStateStore(), io);
        var baseline = await service.AddFolderAsync(temp.Path);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        io.OnReadFact = path =>
        {
            if (path != second) return;
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
        };
        discovery.UseResult = true;
        discovery.Result = [Media(first), Media(second)];
        var scan = Task.Run(() => service.ScanNowAsync());
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            service.CancelScan();
        }
        finally { release.Set(); }
        var cancelled = Assert.Single(await scan);
        Assert.Equal(FolderWatcherRootStatus.Cancelled, cancelled.Status);
        Assert.Equal(baseline.Files, cancelled.Files);
        Assert.Equal(baseline.Changes, cancelled.Changes);
    }

    [Fact]
    public async Task Ready_fact_uses_one_coherent_size_and_timestamp_sample()
    {
        using var temp = new TemporaryFolder();
        var file = Path.Combine(temp.Path, "growing.mkv");
        File.WriteAllText(file, "one");
        var discoveryFact = Media(file);
        var timestamp = DateTimeOffset.UtcNow.AddMinutes(-5);
        var io = new ControllableIo { FactFactory = path => new(path, 1234, timestamp, true) };
        using var service = new FolderWatcherService(new FakeDiscovery(() => [discoveryFact]), new MemoryStateStore(), io);
        var fact = Assert.Single((await service.AddFolderAsync(temp.Path)).Files);
        Assert.Equal(1234, fact.Size);
        Assert.Equal(timestamp, fact.LastWriteUtc);
        Assert.True(fact.IsReady);
    }

    [Fact]
    public async Task Watcher_error_recovers_registration_and_clears_missed_flag()
    {
        using var temp = new TemporaryFolder();
        var io = new ControllableIo();
        using var service = new FolderWatcherService(new FakeDiscovery(() => []), new MemoryStateStore(), io);
        await service.AddFolderAsync(temp.Path);
        io.Fail(new InternalBufferOverflowException("overflow"));
        Assert.True(Assert.Single(service.Snapshot).MissedEvents);
        var recovered = Assert.Single(await service.ScanNowAsync());
        Assert.False(recovered.MissedEvents);
        Assert.Equal(2, io.WatchCount);
    }

    [Fact]
    public async Task Continuous_notifications_do_not_starve_scan()
    {
        using var temp = new TemporaryFolder();
        var discovery = new CountingDiscovery(() => []);
        var io = new ControllableIo();
        using var service = new FolderWatcherService(discovery, new MemoryStateStore(), io,
            periodicInterval: TimeSpan.FromSeconds(30));
        await service.AddFolderAsync(temp.Path);
        var initial = discovery.Count;
        var scanned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Changed += (_, _) => { if (discovery.Count > initial) scanned.TrySetResult(); };
        using var stop = new CancellationTokenSource();
        var events = Task.Run(async () =>
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    io.Notify(temp.Path, WatcherChangeTypes.Changed);
                    await Task.Delay(80, stop.Token);
                }
            }
            catch (OperationCanceledException) { }
        });
        try { await scanned.Task.WaitAsync(TimeSpan.FromSeconds(3)); }
        finally { stop.Cancel(); await events; }
        Assert.True(discovery.Count > initial);
    }

    [Fact]
    public async Task Ambiguous_identical_facts_do_not_infer_a_move()
    {
        using var temp = new TemporaryFolder();
        var old1 = Path.Combine(temp.Path, "old1.mkv");
        var old2 = Path.Combine(temp.Path, "old2.mkv");
        var new1 = Path.Combine(temp.Path, "new1.mkv");
        var new2 = Path.Combine(temp.Path, "new2.mkv");
        var fixedTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        foreach (var file in new[] { old1, old2 }) { File.WriteAllText(file, "same"); File.SetLastWriteTimeUtc(file, fixedTime); }
        var discovery = new FakeDiscovery(() => [Media(old1), Media(old2)]);
        using var service = new FolderWatcherService(discovery, new MemoryStateStore(), new FakeIo());
        await service.AddFolderAsync(temp.Path);
        File.Move(old1, new1);
        File.Move(old2, new2);
        discovery.UseResult = true;
        discovery.Result = [Media(new1), Media(new2)];
        var changes = Assert.Single(await service.ScanNowAsync()).Changes;
        Assert.DoesNotContain(changes, change => change.Kind == FolderWatcherChangeKind.PotentialMove);
        Assert.Equal(2, changes.Count(change => change.Kind == FolderWatcherChangeKind.Missing));
        Assert.Equal(2, changes.Count(change => change.Kind == FolderWatcherChangeKind.New));
    }

    [Fact]
    public async Task Missing_read_fact_is_never_ready_for_review()
    {
        using var temp = new TemporaryFolder();
        var file = Path.Combine(temp.Path, "pending-copy.mkv");
        File.WriteAllText(file, "one");
        var io = new ControllableIo { FactFactory = _ => null };
        using var service = new FolderWatcherService(new FakeDiscovery(() => [Media(file)]), new MemoryStateStore(), io);
        var snapshot = await service.AddFolderAsync(temp.Path);
        Assert.False(Assert.Single(snapshot.Files).IsReady);
        Assert.False(Assert.Single(snapshot.Changes).Current!.IsReady);
    }

    [Fact]
    public void Active_writer_is_not_ready_even_with_old_timestamp()
    {
        using var temp = new TemporaryFolder();
        var file = Path.Combine(temp.Path, "copying.mkv");
        File.WriteAllText(file, "payload");
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(-5));
        using var writer = new FileStream(file, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
        Assert.False(new FileSystemWatcherIO().ReadFact(file)!.IsReady);
    }

    [Fact]
    public async Task Cancelled_state_replace_preserves_original_and_removes_temporary_file()
    {
        using var temp = new TemporaryFolder();
        var path = Path.Combine(temp.Path, "state.json");
        var store = new JsonFolderWatcherStateStore(path);
        await store.SaveAsync(FolderWatcherState.Empty);
        var original = await File.ReadAllBytesAsync(path);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(FolderWatcherState.Empty with { SchemaVersion = 99 }, cancellation.Token));
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
        Assert.Empty(Directory.GetFiles(temp.Path, "*.tmp-*"));
    }

    [Fact]
    public async Task Initial_scan_persists_baseline_and_pending_without_waiting_for_timer()
    {
        using var temp = new TemporaryFolder();
        var file = Path.Combine(temp.Path, "new.mkv");
        File.WriteAllText(file, "one");
        var store = new MemoryStateStore();
        using var service = new FolderWatcherService(new FakeDiscovery(() => [Media(file)]), store, new FakeIo());
        var snapshot = await service.AddFolderAsync(temp.Path);
        Assert.Equal(snapshot.Files, store.Value.Baselines[temp.Path]);
        Assert.Equal(snapshot.Changes, store.Value.PendingChanges[temp.Path]);
    }

    [Fact]
    public async Task Error_and_disposal_during_scan_preserve_baseline_and_do_not_save()
    {
        using var temp = new TemporaryFolder();
        var store = new MemoryStateStore();
        var io = new ControllableIo();
        var discovery = new BlockingDiscovery(() => []);
        using var service = new FolderWatcherService(discovery, store, io);
        await service.AddFolderAsync(temp.Path);
        var saves = store.SaveCount;
        discovery.BlockNextScan();
        var scan = service.ScanNowAsync();
        await discovery.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        io.Fail(new InternalBufferOverflowException("overflow"));
        service.Dispose();
        var result = await scan.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(FolderWatcherRootStatus.Cancelled, Assert.Single(result).Status);
        Assert.Equal(saves, store.SaveCount);
    }

    private static DiscoveredMedia Media(string path) => new(path, new FileInfo(path).Length,
        new ParsedMedia(Path.GetFileName(path), MediaType.TvEpisode, "Show", null, null, null, null, ".mkv", 1, 1),
        DiscoveryClassification.TvEpisodeCandidate, null,
        new FileObservation(new FileInfo(path).Length, File.GetLastWriteTimeUtc(path)));

    private sealed class FakeDiscovery(Func<IReadOnlyList<DiscoveredMedia>> factory) : IMediaDiscoveryService
    {
        public IReadOnlyList<DiscoveredMedia> Result { get; set; } = [];
        public bool UseResult { get; set; }
        public IReadOnlyList<DiscoveredMedia> Discover(string rootPath, bool recursive, Action<int, int, string?>? progress = null, Func<bool>? isCancelled = null) => UseResult ? Result : factory();
    }

    private sealed class CountingDiscovery(Func<IReadOnlyList<DiscoveredMedia>> factory) : IMediaDiscoveryService
    {
        private readonly Func<IReadOnlyList<DiscoveredMedia>> _factory = factory;
        public int Count { get; private set; }
        public IReadOnlyList<DiscoveredMedia> Discover(string rootPath, bool recursive, Action<int, int, string?>? progress = null, Func<bool>? isCancelled = null)
        {
            Count++;
            return _factory();
        }
    }

    private sealed class BlockingDiscovery(Func<IReadOnlyList<DiscoveredMedia>> factory) : IMediaDiscoveryService
    {
        private readonly Func<IReadOnlyList<DiscoveredMedia>> _factory = factory;
        private TaskCompletionSource<bool> _block = Completed();
        public TaskCompletionSource<bool> Started { get; private set; } = Completed();
        public IReadOnlyList<DiscoveredMedia> Result { get; set; } = [];
        public void BlockNextScan()
        {
            Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _block = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        public IReadOnlyList<DiscoveredMedia> Discover(string rootPath, bool recursive, Action<int, int, string?>? progress = null, Func<bool>? isCancelled = null)
        {
            Started.TrySetResult(true);
            while (!_block.Task.IsCompleted)
            {
                if (isCancelled?.Invoke() == true) throw new OperationCanceledException();
                Thread.Yield();
            }
            return Result.Count > 0 ? Result : _factory();
        }
        private static TaskCompletionSource<bool> Completed() { var source = new TaskCompletionSource<bool>(); source.SetResult(true); return source; }
    }

    private sealed class FakeIo : IFolderWatcherIO
    {
        public FolderWatcherFileFact? ReadFact(string path) => File.Exists(path) ? new(path, new FileInfo(path).Length, new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero), true) : null;
        public IDisposable Watch(string rootPath, Action<FolderWatcherNotification> onNotification, Action<Exception> onError) => new NoopDisposable();
        private sealed class NoopDisposable : IDisposable { public void Dispose() { } }
    }

    private sealed class ControllableIo : IFolderWatcherIO
    {
        private Action<FolderWatcherNotification>? _notify;
        private Action<Exception>? _error;
        public Func<string, FolderWatcherFileFact?>? FactFactory { get; set; }
        public Action<string>? OnReadFact { get; set; }
        public int WatchCount { get; private set; }
        public FolderWatcherFileFact? ReadFact(string path)
        {
            OnReadFact?.Invoke(path);
            if (FactFactory is not null) return FactFactory(path);
            return File.Exists(path) ? new(path, new FileInfo(path).Length, new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero), true) : null;
        }
        public IDisposable Watch(string rootPath, Action<FolderWatcherNotification> onNotification, Action<Exception> onError) { WatchCount++; _notify = onNotification; _error = onError; return new Noop(); }
        public void Notify(string path, WatcherChangeTypes type) => _notify?.Invoke(new(path, type));
        public void Fail(Exception error) => _error?.Invoke(error);
        private sealed class Noop : IDisposable { public void Dispose() { } }
    }

    private sealed class MemoryStateStore : IFolderWatcherStateStore
    {
        public FolderWatcherState Value { get; private set; } = FolderWatcherState.Empty;
        public int SaveCount { get; private set; }
        public Task<FolderWatcherState> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Value);
        public Task SaveAsync(FolderWatcherState state, CancellationToken cancellationToken = default) { SaveCount++; Value = state; return Task.CompletedTask; }
    }

    private sealed class FailingStateStore : IFolderWatcherStateStore
    {
        public Task<FolderWatcherState> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(FolderWatcherState.Empty);
        public Task SaveAsync(FolderWatcherState state, CancellationToken cancellationToken = default) => Task.FromException(new IOException("state write failed"));
    }

    private sealed class FailingLoadStateStore : IFolderWatcherStateStore
    {
        public int SaveCount { get; private set; }
        public Task<FolderWatcherState> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<FolderWatcherState>(new InvalidDataException("corrupted watcher state"));
        public Task SaveAsync(FolderWatcherState state, CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedStateStore(FolderWatcherState value) : IFolderWatcherStateStore
    {
        public FolderWatcherState Value { get; } = value;
        public int SaveCount { get; private set; }
        public Task<FolderWatcherState> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Value);
        public Task SaveAsync(FolderWatcherState state, CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class TemporaryFolder : IDisposable
    {
        public string Path { get; } = System.IO.Directory.CreateTempSubdirectory("dropsort-watcher-").FullName;
        public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
    }
}
