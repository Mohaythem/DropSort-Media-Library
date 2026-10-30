namespace DropSort.UI.Services;

internal enum NavigationZone
{
    Narrow,
    Medium,
    Wide,
}

/// <summary>Separates the preferred wide pane state from temporary responsive overlays.</summary>
internal sealed class NavigationPaneState(bool widePaneOpen)
{
    internal const double WideBreakpoint = 1200;
    internal const double MediumBreakpoint = 760;

    internal NavigationZone? Zone { get; private set; }
    internal bool WidePaneOpen { get; private set; } = widePaneOpen;

    internal static bool ParseWidePreference(string? value) =>
        !bool.TryParse(value, out var isOpen) || isOpen;

    internal static NavigationZone ResolveZone(double clientWidth) => clientWidth >= WideBreakpoint
        ? NavigationZone.Wide
        : clientWidth >= MediumBreakpoint ? NavigationZone.Medium : NavigationZone.Narrow;

    internal bool TryResize(double clientWidth, out bool isPaneOpen)
    {
        isPaneOpen = false;
        if (!double.IsFinite(clientWidth) || clientWidth <= 0)
        {
            return false;
        }

        var zone = ResolveZone(clientWidth);
        if (Zone == zone)
        {
            return false;
        }

        Zone = zone;
        isPaneOpen = zone == NavigationZone.Wide && WidePaneOpen;
        return true;
    }

    internal bool RememberWidePreference(double clientWidth, bool isPaneOpen, bool isApplyingChange)
    {
        // ActualWidth can change before SizeChanged arrives. Check both width and the last zone.
        if (isApplyingChange || Zone != NavigationZone.Wide || clientWidth < WideBreakpoint
            || !double.IsFinite(clientWidth) || WidePaneOpen == isPaneOpen)
        {
            return false;
        }

        WidePaneOpen = isPaneOpen;
        return true;
    }

    internal bool ShouldDismissOverlay(bool isPaneOpen) =>
        isPaneOpen && Zone is NavigationZone.Medium or NavigationZone.Narrow;
}
