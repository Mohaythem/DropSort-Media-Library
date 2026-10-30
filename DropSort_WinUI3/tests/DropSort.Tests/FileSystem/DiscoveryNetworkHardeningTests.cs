using DropSort.Domain.Media.Discovery;
using DropSort.FileSystem.Discovery;
using DropSort.FileSystem.Inspection;
using Xunit;

namespace DropSort.Tests.FileSystem;

public sealed class DiscoveryNetworkHardeningTests
{
    [Fact]
    public void Local_discovery_keeps_healthy_sibling_when_one_child_disappears()
    {
        var root = Path.Combine(Path.GetTempPath(), "dropsort-discovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var healthy = Path.Combine(root, "Healthy Movie.mkv");
        File.WriteAllBytes(healthy, new byte[7]);
        var vanished = Path.Combine(root, "Vanished Movie.mkv");
        try
        {
            var service = new MediaDiscoveryService(
                new NetworkReadGuard(TimeSpan.FromMilliseconds(100)),
                _ => new[] { vanished, healthy },
                path => path == root ? FileAttributes.Directory :
                    path == vanished ? throw new FileNotFoundException(path) : FileAttributes.Normal);

            var results = service.Discover(root, recursive: false);

            Assert.Contains(results, item => item.Path == healthy && item.Classification == DiscoveryClassification.MovieCandidate);
            Assert.Contains(results, item => item.Path == vanished && item.Classification == DiscoveryClassification.Error
                && item.Issue?.Code == DiscoveryErrorCode.Disappeared);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task Busy_unc_share_returns_storage_unavailable_without_starting_discovery_io()
    {
        using var release = new ManualResetEventSlim(false);
        var guard = new NetworkReadGuard(TimeSpan.FromMilliseconds(100));
        var started = 0;
        var holder = Task.Run(() => guard.Execute(
            @"\\server\share\held.mkv",
            () => { Interlocked.Increment(ref started); release.Wait(); return true; },
            () => false));
        SpinWait.SpinUntil(() => Volatile.Read(ref started) == 1, TimeSpan.FromSeconds(1));
        var enumerateCalled = false;
        var service = new MediaDiscoveryService(guard,
            _ => { enumerateCalled = true; return []; },
            _ => throw new InvalidOperationException("network I/O must not run while share is busy"));

        var result = service.Discover(@"\\server\share\", recursive: true);

        Assert.Contains(result, item => item.Classification == DiscoveryClassification.Error
            && item.Issue?.Code == DiscoveryErrorCode.StorageUnavailable);
        Assert.False(enumerateCalled);
        release.Set();
        await holder.WaitAsync(TimeSpan.FromSeconds(1));
    }
}
