using TBDMovies.Domain.Media.Discovery;
using TBDMovies.FileSystem.Discovery;
using TBDMovies.FileSystem.Inspection;
using Xunit;

namespace TBDMovies.Tests.FileSystem;

public sealed class ImportFileValidatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tbdmovies-import-validator-" + Guid.NewGuid().ToString("N"));
    private DiscoveredMedia Media()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllBytes(Path.Combine(_root, "Show.S01E01.mkv"), new byte[64]);
        return Assert.Single(new MediaDiscoveryService().Discover(_root, false));
    }
    [Fact]
    public void Unchanged_file_validates_without_modification()
    {
        var item = Media();
        new ImportFileValidator().Validate(item);
        Assert.Equal(item.Observation, Assert.Single(new MediaDiscoveryService().Discover(_root, false)).Observation);
    }
    [Fact]
    public void Size_change_is_refused()
    {
        var item = Media(); File.WriteAllBytes(item.Path, new byte[128]);
        Assert.Throws<IOException>(() => new ImportFileValidator().Validate(item));
    }
    [Fact]
    public void Cancellation_prevents_any_availability_check()
    {
        var item = Media(); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => new ImportFileValidator().Validate(item, cancellation.Token));
    }
    [Fact]
    public void Unavailable_drive_fails_closed()
    {
        var item = Media();
        var unavailable = new DiscoveredMedia(@"Z:\tbdmovies-isolated-unavailable-" + Guid.NewGuid().ToString("N") + @"\Show.S01E01.mkv",
            item.FileSize, item.ParsedMedia, item.Classification, null, item.Observation);
        Assert.ThrowsAny<IOException>(() => new ImportFileValidator().Validate(unavailable));
    }
    public void Dispose()
    {
        var path = Path.GetFullPath(_root);
        Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), path, StringComparison.OrdinalIgnoreCase);
        if (Directory.Exists(path)) Directory.Delete(path, true);
    }
}
