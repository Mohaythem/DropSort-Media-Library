using TBDMovies.Application.Services;

namespace TBDMovies.UI.Services;

public sealed class WindowsClipboardService : IClipboardService
{
    private readonly Microsoft.UI.Dispatching.DispatcherQueue? _dispatcherQueue;

    public WindowsClipboardService(Microsoft.UI.Dispatching.DispatcherQueue? dispatcherQueue = null)
    {
        _dispatcherQueue = dispatcherQueue;
    }

    public bool TrySetText(string text)
    {
        try
        {
            var dq = _dispatcherQueue ?? AppServices.UiDispatcherQueue;
            if (dq != null && !dq.HasThreadAccess)
            {
                // Synchronous boolean contract: off-thread calls cannot guarantee immediate clipboard set
                return false;
            }

            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(text ?? string.Empty);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
