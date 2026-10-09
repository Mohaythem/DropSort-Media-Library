using System.Xml.Linq;
using DropSort.UI.Services;
using Xunit;

namespace DropSort.Tests.UI;

public sealed class NavigationPaneStateTests
{
    [Theory]
    [InlineData(759.99, 0)]
    [InlineData(760, 1)]
    [InlineData(1199.99, 1)]
    [InlineData(1200, 2)]
    [InlineData(1600, 2)]
    public void Client_DIP_boundaries_select_the_expected_zone(double width, int expected)
    {
        var state = new NavigationPaneState(true);
        Assert.True(state.TryResize(width, out var open));
        Assert.Equal((NavigationZone)expected, state.Zone);
        Assert.Equal(expected == 2, open);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Wide_user_choice_survives_resizing_and_temporary_overlays(bool preference)
    {
        var state = new NavigationPaneState(!preference);
        Assert.True(state.TryResize(1300, out _));
        Assert.True(state.RememberWidePreference(1300, preference, false));

        foreach (var width in new[] { 1000.0, 700.0 })
        {
            Assert.True(state.TryResize(width, out var open));
            Assert.False(open);
            Assert.False(state.RememberWidePreference(width, true, false));
            Assert.False(state.RememberWidePreference(width, false, false));
            Assert.Equal(preference, state.WidePaneOpen);
        }

        Assert.True(state.TryResize(1200, out var restored));
        Assert.Equal(preference, restored);
    }

    [Theory]
    [InlineData(600, 700)]
    [InlineData(900, 1100)]
    [InlineData(1300, 1400)]
    public void Same_zone_resize_leaves_native_open_state_untouched(double first, double second)
    {
        var state = new NavigationPaneState(true);
        Assert.True(state.TryResize(first, out _));
        Assert.False(state.TryResize(second, out _));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Small_startup_preserves_stored_wide_preference(bool preference)
    {
        var state = new NavigationPaneState(preference);
        Assert.False(state.RememberWidePreference(1300, !preference, false));
        Assert.True(state.TryResize(700, out var open));
        Assert.False(open);
        Assert.False(state.RememberWidePreference(700, !preference, false));
        Assert.True(state.TryResize(1300, out open));
        Assert.Equal(preference, open);
    }

    [Fact]
    public void Automatic_changes_and_stale_resize_callbacks_do_not_save_user_preferences()
    {
        var state = new NavigationPaneState(false);
        state.TryResize(1300, out _);
        Assert.False(state.RememberWidePreference(1300, true, true));
        Assert.False(state.RememberWidePreference(1199, true, false));
        state.TryResize(900, out _);
        Assert.False(state.RememberWidePreference(1300, true, false));
        Assert.True(state.TryResize(1300, out var open));
        Assert.False(open);
        Assert.False(state.RememberWidePreference(1300, false, false));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Invalid_layout_measurements_do_not_change_state(double width)
    {
        var state = new NavigationPaneState(false);
        state.TryResize(1300, out _);
        Assert.False(state.TryResize(width, out _));
        Assert.Equal(NavigationZone.Wide, state.Zone);
        Assert.False(state.RememberWidePreference(width, true, false));
        Assert.False(state.WidePaneOpen);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("invalid", true)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void Stored_preference_defaults_safely(string? value, bool expected)
    {
        Assert.Equal(expected, NavigationPaneState.ParseWidePreference(value));
    }

    [Theory]
    [InlineData(700, true)]
    [InlineData(900, true)]
    [InlineData(1300, false)]
    public void Escape_dismisses_only_open_temporary_navigation(double width, bool dismiss)
    {
        var state = new NavigationPaneState(true);
        state.TryResize(width, out _);
        Assert.Equal(dismiss, state.ShouldDismissOverlay(true));
        Assert.False(state.ShouldDismissOverlay(false));
    }

    [Fact]
    public void Native_shell_dimensions_and_footer_destinations_are_preserved()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "DropSort.UI")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var shell = XDocument.Load(Path.Combine(directory!.FullName, "src", "DropSort.UI", "MainWindow.xaml"));
        var navigation = Assert.Single(shell.Descendants(), e => e.Name.LocalName == "NavigationView");
        Assert.Equal("240", (string?)navigation.Attribute("OpenPaneLength"));
        Assert.Equal("64", (string?)navigation.Attribute("CompactPaneLength"));
        Assert.Equal("Left", (string?)navigation.Attribute("PaneDisplayMode"));
        Assert.Equal(new[] { "home", "library", "lists", "add", "check", "settings" },
            navigation.Descendants().Where(e => e.Name.LocalName == "NavigationViewItem")
                .Select(e => (string?)e.Attribute("Tag")).ToArray());
        Assert.Single(navigation.Descendants().Where(e => e.Name.LocalName == "NavigationView.FooterMenuItems")
            .Single().Elements());
        Assert.DoesNotContain(shell.Descendants().Where(e => e.Name.LocalName == "Setter"),
            e => ((string?)e.Attribute("Target"))?.StartsWith("ShellNavigation.", StringComparison.Ordinal) == true);

        var themes = XDocument.Load(Path.Combine(directory.FullName, "src", "DropSort.UI", "Resources", "ThemeResources.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var paneBrushes = themes.Descendants().Where(e => (string?)e.Attribute(x + "Key") == "NavigationViewDefaultPaneBackground").ToArray();
        Assert.Equal(3, paneBrushes.Length);
        Assert.All(paneBrushes, brush => Assert.Contains("{ThemeResource ", (string?)brush.Attribute("Color")));
    }

    [Fact]
    public void Scoped_pane_surfaces_preserve_native_light_and_high_contrast_resources()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "DropSort.UI")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var shell = XDocument.Load(Path.Combine(directory!.FullName, "src", "DropSort.UI", "MainWindow.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var navigation = Assert.Single(shell.Descendants(), e => e.Name.LocalName == "NavigationView");
        var dictionaries = navigation.Descendants().Where(e => e.Name.LocalName == "ResourceDictionary"
            && e.Attribute(x + "Key") is not null).ToDictionary(e => (string)e.Attribute(x + "Key")!);
        Assert.Equal(new[] { "Default", "HighContrast", "Light" }, dictionaries.Keys.OrderBy(key => key).ToArray());
        foreach (var theme in new[] { "Default", "Light", "HighContrast" })
        {
            var brushes = dictionaries[theme].Elements().ToDictionary(e => (string)e.Attribute(x + "Key")!);
            Assert.Equal("{ThemeResource " + (theme == "HighContrast" ? "SystemColorWindowColor" : "SolidBackgroundFillColorBase") + "}",
                (string?)brushes["NavigationViewDefaultPaneBackground"].Attribute("Color"));
            Assert.Equal(theme == "HighContrast" ? "{ThemeResource SystemColorWindowColor}" : "Transparent",
                (string?)brushes["NavigationViewExpandedPaneBackground"].Attribute("Color"));
            Assert.All(brushes.Values, brush => Assert.Null(brush.Attribute("Opacity")));
        }
    }

    [Fact]
    public void Slate_and_dark_switches_update_both_pane_brushes_from_existing_element_resources()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "DropSort.UI")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var code = File.ReadAllText(Path.Combine(directory!.FullName, "src", "DropSort.UI", "MainWindow.xaml.cs"));
        var applyTheme = code[code.IndexOf("private void ApplyTheme()", StringComparison.Ordinal)..];
        applyTheme = applyTheme[..applyTheme.IndexOf("private void UpdateTitleBarColors()", StringComparison.Ordinal)];
        Assert.Contains("UpdateNavigationPaneBackgrounds();", applyTheme);
        Assert.Contains("ShellNavigation.Resources.ThemeDictionaries[\"Default\"]", applyTheme);
        Assert.Contains("ThemeService.UsesSlateSurface ? (FrameworkElement)SlateSurface : RootGrid", applyTheme);
        Assert.Contains("BindingOperations.SetBinding(overlay, SolidColorBrush.ColorProperty", applyTheme);
        Assert.Contains("BindingOperations.SetBinding(inline, SolidColorBrush.ColorProperty", applyTheme);
        Assert.Contains("Source = SlateSurface", applyTheme);
        Assert.Contains("new PropertyPath(\"Background.Color\")", applyTheme);
        Assert.Contains("inline.ClearValue(SolidColorBrush.ColorProperty)", applyTheme);
        Assert.DoesNotContain("ColorHelper.FromArgb", applyTheme);
        Assert.DoesNotContain("Application.Current.Resources", applyTheme);
    }
}
