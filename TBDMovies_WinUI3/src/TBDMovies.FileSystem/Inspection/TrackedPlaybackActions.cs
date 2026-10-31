using TBDMovies.Domain.Library.Availability;

namespace TBDMovies.FileSystem.Inspection;

/// <summary>
/// Opens media files and records playback only after the shell action reports a successful launch.
/// </summary>
public sealed class TrackedPlaybackActions
{
    private readonly MediaFileActions _actions;
    private readonly Action<string> _recordSuccessfulLaunch;

    public TrackedPlaybackActions(MediaFileActions actions, Action<string> recordSuccessfulLaunch)
    {
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _recordSuccessfulLaunch = recordSuccessfulLaunch ?? throw new ArgumentNullException(nameof(recordSuccessfulLaunch));
    }

    public async Task<FileActionResult> OpenFileAsync(string path, CancellationToken token = default)
    {
        // Do not check cancellation after this await. Once the shell accepts the launch, playback
        // has started and cannot be undone by a late cancellation request.
        var result = await _actions.OpenFileAsync(path, token).ConfigureAwait(false);
        if (result.Status != AvailabilityInspectionStatus.Present || result.ErrorCode is not null)
            return result;

        try
        {
            _recordSuccessfulLaunch(path);
            return result;
        }
        catch (Exception)
        {
            return new FileActionResult(
                AvailabilityInspectionStatus.Error,
                "PlaybackActivityNotSaved");
        }
    }
}
