using Microsoft.UI.Xaml;

namespace DropSort.UI.Services;

/// <summary>
/// The three themes the design source exposes. Slate and Dark share the native dark palette;
/// Slate only re-tints the shell surface, exactly like the design's <c>[data-theme='slate']</c>
/// override. Light maps straight onto the native light palette.
/// </summary>
public enum AppTheme
{
    Slate,
    Dark,
    Light,
}

public static class ThemeService
{
    public static AppTheme CurrentTheme { get; private set; } = AppTheme.Slate;

    public static event EventHandler? ThemeChanged;

    /// <summary>
    /// Slate and Dark both resolve to the native dark element theme so every WinUI control keeps
    /// its own hover/pressed/disabled brushes instead of a bespoke palette.
    /// </summary>
    public static ElementTheme ElementTheme =>
        CurrentTheme == AppTheme.Light ? ElementTheme.Light : ElementTheme.Dark;

    public static bool UsesSlateSurface => CurrentTheme == AppTheme.Slate;

    public static void SetTheme(AppTheme theme)
    {
        if (CurrentTheme == theme)
        {
            return;
        }

        CurrentTheme = theme;
        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }
}
