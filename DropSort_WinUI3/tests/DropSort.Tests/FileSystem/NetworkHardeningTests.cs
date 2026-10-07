using System.Collections.Concurrent;
using System.ComponentModel;
using DropSort.Application.External;
using DropSort.Domain.Library.Availability;
using DropSort.FileSystem.Inspection;
using Xunit;

namespace DropSort.Tests.FileSystem;

public sealed class NetworkHardeningTests
{
    [Fact]
    public void Guard_times_out_a_hung_network_read_and_does_not_start_a_second_read_on_same_share()
    {
        using var release = new ManualResetEventSlim(false);
        var guard = new NetworkReadGuard(TimeSpan.FromMilliseconds(80));
        var started = 0;

        var result = guard.Execute(
            @"\\server\share\movie.mkv",
            () => { Interlocked.Increment(ref started); release.Wait(); return true; },
            () => false);

        Assert.False(result);
        Assert.Equal(1, started);

        var second = guard.Execute(
            @"\\server\share\other.mkv",
            () => { Interlocked.Increment(ref started); return true; },
            () => false);

        Assert.False(second);
        Assert.Equal(1, started);
        release.Set();
    }

    [Fact]
    public async Task Guard_uses_one_busy_slot_for_canonical_and_extended_unc_spellings()
    {
        using var release = new ManualResetEventSlim(false);
        var guard = new NetworkReadGuard(TimeSpan.FromMilliseconds(80));
        var started = 0;
        var first = Task.Run(() => guard.Execute(
            @"\\server\share\movie.mkv",
            () => { Interlocked.Increment(ref started); release.Wait(); return true; },
            () => false));

        SpinWait.SpinUntil(() => Volatile.Read(ref started) == 1, TimeSpan.FromSeconds(1));
        var second = guard.Execute(
            @"\\?\UNC\server\share\other.mkv",
            () => { Interlocked.Increment(ref started); return true; },
            () => false);

        Assert.False(second);
        Assert.Equal(1, started);
        release.Set();
        await first.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Guard_does_not_apply_network_timeout_to_local_paths_and_preserves_read_exception()
    {
        var guard = new NetworkReadGuard(TimeSpan.FromMilliseconds(20));
        var ran = false;
        Assert.Equal(42, guard.Execute(@"C:\media\movie.mkv", () => { ran = true; return 42; }, () => 0));
        Assert.True(ran);

        var expected = new InvalidOperationException("probe");
        var actual = Assert.Throws<InvalidOperationException>(() => guard.Execute(
            @"\\server\share\movie.mkv", () => throw expected, () => 0));
        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task Guard_recovers_after_timed_out_read_and_allows_sequential_reads()
    {
        using var release = new ManualResetEventSlim(false);
        var guard = new NetworkReadGuard(TimeSpan.FromMilliseconds(40));
        var holder = Task.Run(() => guard.Execute(@"\\server\share\hung.mkv", () => { release.Wait(); return true; }, () => false));
        Assert.False(await holder);
        release.Set();

        Assert.True(SpinWait.SpinUntil(() => guard.Execute(@"\\server\share\ok.mkv", () => true, () => false), TimeSpan.FromSeconds(1)));
        for (var i = 0; i < 4; i++)
            Assert.True(guard.Execute(@"\\server\share\ok.mkv", () => true, () => false));
    }

    [Fact]
    public void Availability_inspector_classifies_present_missing_and_unavailable_without_network_io()
    {
        var root = @"\\server\share\";
        var existing = root + "present.mkv";
        var absent = root + "absent.mkv";
        var attrs = new ConcurrentDictionary<string, FileAttributes>(StringComparer.OrdinalIgnoreCase)
        {
            [root] = FileAttributes.Directory,
            [root.TrimEnd('\\')] = FileAttributes.Directory,
            [existing] = FileAttributes.Normal
        };
        var inspector = new AvailabilityInspector(
            new NetworkReadGuard(TimeSpan.FromMilliseconds(100)),
            path => attrs.TryGetValue(path, out var value) ? value : throw new FileNotFoundException(path),
            _ => new MediaFileIdentity(100, 1, 1, 0, 0),
            _ => { });

        Assert.Equal(AvailabilityInspectionStatus.Present, inspector.Inspect(existing).Status);
        Assert.Equal(AvailabilityInspectionStatus.Missing, inspector.Inspect(absent).Status);

        var offlineInspector = new AvailabilityInspector(
            new NetworkReadGuard(TimeSpan.FromMilliseconds(100)),
            _ => throw new IOException("share offline"),
            _ => throw new InvalidOperationException(),
            _ => { });
        Assert.Equal(AvailabilityInspectionStatus.StorageUnavailable, offlineInspector.Inspect(existing).Status);
    }

    [Fact]
    public void Availability_inspector_preserves_a_present_share_as_error_when_identity_probe_fails()
    {
        var root = @"\\server\share\";
        var path = root + "present.mkv";
        var inspector = new AvailabilityInspector(
            new NetworkReadGuard(TimeSpan.FromMilliseconds(100)),
            candidate => candidate.EndsWith(@"share\", StringComparison.OrdinalIgnoreCase)
                || candidate.EndsWith(@"share", StringComparison.OrdinalIgnoreCase)
                ? FileAttributes.Directory
                : FileAttributes.Normal,
            _ => throw new Win32Exception(5),
            _ => { });

        var inspection = inspector.Inspect(path);

        Assert.Equal(AvailabilityInspectionStatus.Error, inspection.Status);
        Assert.Equal(nameof(Win32Exception), inspection.ErrorCode);
    }

    [Fact]
    public void Availability_inspector_treats_native_share_probe_failure_as_storage_unavailable()
    {
        var path = @"\\server\share\movie.mkv";
        var inspector = new AvailabilityInspector(
            new NetworkReadGuard(TimeSpan.FromMilliseconds(100)),
            _ => throw new Win32Exception(53),
            _ => throw new InvalidOperationException(),
            _ => { });

        Assert.Equal(AvailabilityInspectionStatus.StorageUnavailable, inspector.Inspect(path).Status);
        Assert.False(inspector.IsStorageAvailable(path));
    }

    [Fact]
    public async Task Media_file_actions_do_not_launch_for_missing_or_unavailable_storage()
    {
        var launched = 0;
        var inspector = new FakeInspector
        {
            Status = AvailabilityInspectionStatus.Missing
        };
        var actions = new MediaFileActions(inspector, (_, _) => Interlocked.Increment(ref launched));
        var path = @"\\server\share\movie.mkv";

        var missing = await actions.OpenFileAsync(path);
        Assert.Equal(AvailabilityInspectionStatus.Missing, missing.Status);

        inspector.Status = AvailabilityInspectionStatus.StorageUnavailable;
        var unavailable = await actions.OpenContainingFolderAsync(path);
        Assert.Equal(AvailabilityInspectionStatus.StorageUnavailable, unavailable.Status);
        Assert.Equal(0, launched);
    }

    [Fact]
    public async Task Media_file_actions_honor_cancellation_before_launch()
    {
        var launched = 0;
        var tokenSource = new CancellationTokenSource();
        tokenSource.Cancel();
        var actions = new MediaFileActions(new FakeInspector { Status = AvailabilityInspectionStatus.Present },
            (_, _) => Interlocked.Increment(ref launched));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => actions.OpenFileAsync(
            @"\\server\share\movie.mkv", tokenSource.Token));
        Assert.Equal(0, launched);
    }

    [Fact]
    public async Task Media_file_actions_classify_shell_failure_without_launching_again()
    {
        var attempts = 0;
        var actions = new MediaFileActions(new FakeInspector { Status = AvailabilityInspectionStatus.Present },
            (_, _) => { attempts++; throw new Win32Exception(5); });

        var result = await actions.OpenFileAsync(@"\\server\share\movie.mkv");

        Assert.Equal(AvailabilityInspectionStatus.Error, result.Status);
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task Media_file_actions_cancel_during_inspection_before_launch()
    {
        using var cancellation = new CancellationTokenSource();
        var launched = 0;
        var inspector = new FakeInspector { Status = AvailabilityInspectionStatus.Present };
        inspector.BeforeReturn = () => cancellation.Cancel();
        var actions = new MediaFileActions(inspector, (_, _) => Interlocked.Increment(ref launched));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => actions.OpenFileAsync(
            @"\\server\share\movie.mkv", cancellation.Token));
        Assert.Equal(0, launched);
    }

    private sealed class FakeInspector : IAvailabilityInspector
    {
        public AvailabilityInspectionStatus Status { get; set; }
        public Action? BeforeReturn { get; set; }
        public AvailabilityInspection Inspect(string path)
        {
            BeforeReturn?.Invoke();
            return new(path, Status, Status == AvailabilityInspectionStatus.Present
                ? new MediaFileIdentity(1, 1, 1, 0, 0) : null);
        }
        public bool IsStorageAvailable(string path) => Status != AvailabilityInspectionStatus.StorageUnavailable;
    }
}
