using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;

namespace DropSort.UI.Services;

/// <summary>
/// Serializes the app's ContentDialog instances. WinUI permits only one ContentDialog per XamlRoot;
/// awaiting the gate keeps a late failure or a double-click from starting a competing native dialog.
/// </summary>
internal static class ContentDialogCoordinator
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static async Task<ContentDialogResult> ShowAsync(ContentDialog dialog)
    {
        await Gate.WaitAsync();
        try
        {
            return await dialog.ShowAsync();
        }
        finally
        {
            Gate.Release();
        }
    }
}
