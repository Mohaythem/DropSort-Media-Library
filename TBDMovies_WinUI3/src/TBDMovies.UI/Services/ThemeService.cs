using Microsoft.UI.Xaml;

namespace TBDMovies.UI.Services;

/// <summary>
/// The three application themes. Slate layers a navy/peach palette over native dark controls;
/// Dark and Light keep their respective native palettes.
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

    /// <summary>Applies the stored theme at startup without re-writing it to the store.</summary>
    public static void Restore(AppTheme theme)
    {
        CurrentTheme = theme;
    }

    public static void SetTheme(AppTheme theme)
    {
        if (CurrentTheme == theme)
        {
            return;
        }

        CurrentTheme = theme;
        AppServices.PersistTheme(theme);
        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }
}
