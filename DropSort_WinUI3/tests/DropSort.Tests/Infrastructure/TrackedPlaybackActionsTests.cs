using DropSort.Application.External;
using DropSort.Domain.Library.Availability;
using DropSort.FileSystem.Inspection;
using Xunit;

namespace DropSort.Tests.Infrastructure;

public sealed class TrackedPlaybackActionsTests
{
    private const string Path = @"C:\media\episode.mkv";

    [Fact]
    public async Task Records_present_success_once_with_exact_path()
    {
        var recorded = new List<string>();
        var sut = Create(AvailabilityInspectionStatus.Present, recorded.Add);

        var result = await sut.OpenFileAsync(Path);

        Assert.Equal(AvailabilityInspectionStatus.Present, result.Status);
        Assert.Null(result.ErrorCode);
        Assert.Equal(new[] { Path }, recorded);
    }

    [Fact]
    public async Task Does_not_record_failed_launch()
    {
        var recorded = new List<string>();
        var actions = new MediaFileActions(
            new FakeAvailabilityInspector(AvailabilityInspectionStatus.Present),
            (_, _) => throw new IOException("player unavailable"));
        var sut = new TrackedPlaybackActions(actions, recorded.Add);

        var result = await sut.OpenFileAsync(Path);

        Assert.Equal(AvailabilityInspectionStatus.Error, result.Status);
        Assert.Equal(nameof(IOException), result.ErrorCode);
        Assert.Empty(recorded);
    }

    [Theory]
    [InlineData(AvailabilityInspectionStatus.Missing)]
    [InlineData(AvailabilityInspectionStatus.StorageUnavailable)]
    [InlineData(AvailabilityInspectionStatus.Error)]
    public async Task Does_not_record_non_present_result(AvailabilityInspectionStatus status)
    {
        var recorded = new List<string>();
        var sut = Create(status, recorded.Add);

        var result = await sut.OpenFileAsync(Path);

        Assert.Equal(status, result.Status);
        Assert.Empty(recorded);
    }

    [Fact]
    public async Task Does_not_record_when_cancelled_before_launch()
    {
        var recorded = new List<string>();
        var sut = Create(AvailabilityInspectionStatus.Present, recorded.Add);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.OpenFileAsync(Path, cancellation.Token));

        Assert.Empty(recorded);
    }

    [Fact]
    public async Task Records_when_cancellation_arrives_after_launch_is_accepted()
    {
        var recorded = new List<string>();
        using var cancellation = new CancellationTokenSource();
        var actions = new MediaFileActions(
            new FakeAvailabilityInspector(AvailabilityInspectionStatus.Present),
            (_, _) => cancellation.Cancel());
        var sut = new TrackedPlaybackActions(actions, recorded.Add);

        var result = await sut.OpenFileAsync(Path, cancellation.Token);

        Assert.Equal(AvailabilityInspectionStatus.Present, result.Status);
        Assert.Equal(new[] { Path }, recorded);
    }

    [Fact]
    public async Task Returns_activity_error_when_recording_fails()
    {
        var sut = Create(
            AvailabilityInspectionStatus.Present,
            _ => throw new InvalidOperationException("database unavailable"));

        var result = await sut.OpenFileAsync(Path);

        Assert.Equal(AvailabilityInspectionStatus.Error, result.Status);
        Assert.Equal("PlaybackActivityNotSaved", result.ErrorCode);
    }

    private static TrackedPlaybackActions Create(
        AvailabilityInspectionStatus status,
        Action<string> record)
    {
        var actions = new MediaFileActions(
            new FakeAvailabilityInspector(status),
            (_, _) => { });
        return new TrackedPlaybackActions(actions, record);
    }

    private sealed class FakeAvailabilityInspector : IAvailabilityInspector
    {
        private readonly AvailabilityInspectionStatus _status;
        private readonly string? _errorCode;

        public FakeAvailabilityInspector(AvailabilityInspectionStatus status, string? errorCode = null)
        {
            _status = status;
            _errorCode = errorCode;
        }

        public AvailabilityInspection Inspect(string path) => new(path, _status, ErrorCode: _errorCode);

        public bool IsStorageAvailable(string path) => true;
    }
}
