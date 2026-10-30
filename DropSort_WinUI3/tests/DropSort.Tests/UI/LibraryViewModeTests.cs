using System.Xml.Linq;
using DropSort.UI.Services;
using DropSort.Infrastructure.Persistence.Migrations;
using DropSort.Infrastructure.Persistence.Repositories;
using Xunit;

namespace DropSort.Tests.UI;

public sealed class LibraryViewModeTests
{
    private static readonly string UiRoot = FindUiRoot();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    [InlineData("GRID")]
    public void Invalid_or_missing_preferences_default_to_grid(string? value)
    {
        Assert.Equal(LibraryViewMode.Grid, LibraryViewModePreference.Parse(value));
    }

    [Theory]
    [InlineData(LibraryViewMode.Grid, "grid")]
    [InlineData(LibraryViewMode.Compact, "compact")]
    public void Preferences_serialize_and_parse_as_stable_values(LibraryViewMode mode, string persisted)
    {
        Assert.Equal(persisted, LibraryViewModePreference.Serialize(mode));
        Assert.Equal(mode, LibraryViewModePreference.Parse(persisted));
    }

    [Fact]
    public void Library_view_mode_setting_survives_database_reopen()
    {
        var root = Path.Combine(Path.GetTempPath(), "dropsort-library-mode-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var connectionString = $"Data Source={Path.Combine(root, "settings.db")};Pooling=False";
            new DatabaseMigrator(connectionString).Migrate();

            var first = new SettingsRepository(connectionString);
            first.Set("ui_library_view_mode", LibraryViewModePreference.Serialize(LibraryViewMode.Compact), DateTimeOffset.UtcNow);

            var reopened = new SettingsRepository(connectionString);
            Assert.Equal("compact", reopened.Get("ui_library_view_mode"));
            Assert.Equal(LibraryViewMode.Compact, LibraryViewModePreference.Parse(reopened.Get("ui_library_view_mode")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData(332, 320, 332, 1)]
    [InlineData(700, 338, 350, 2)]
    [InlineData(1040, 334.6666666667, 346.6666666667, 3)]
    public void Horizontal_layout_adapts_columns_and_card_width(double available, double cardWidth, double cellWidth, int columns)
    {
        var metrics = LibraryHorizontalLayout.Calculate(available);

        Assert.Equal(columns, metrics.Columns);
        Assert.Equal(cellWidth, metrics.CellWidth, precision: 6);
        Assert.Equal(cardWidth, metrics.CardWidth, precision: 6);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(0)]
    [InlineData(-1)]
    public void Horizontal_layout_uses_a_safe_single_card_fallback_for_invalid_width(double available)
    {
        var metrics = LibraryHorizontalLayout.Calculate(available);

        Assert.Equal(1, metrics.Columns);
        Assert.Equal(332, metrics.CellWidth);
        Assert.Equal(320, metrics.CardWidth);
    }

    [Fact]
    public void Library_switch_uses_selector_bar_and_keeps_grid_data_flow_unchanged()
    {
        var xaml = ReadUi("Views", "LibraryPage.xaml");
        var code = ReadUi("Views", "LibraryPage.xaml.cs");
        var handler = Slice(code, "private void ApplyViewMode", "private object? CaptureFirstVisibleItem");

        Assert.Contains("<SelectorBar", xaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"grid\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"compact\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectionChanged=\"ViewModeSelector_SelectionChanged\"", xaml, StringComparison.Ordinal);
        Assert.Contains("HorizontalMovieCardTemplate", handler, StringComparison.Ordinal);
        Assert.Contains("HorizontalShowCardTemplate", handler, StringComparison.Ordinal);
        Assert.Contains("ItemContainerStyle", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("ItemsSource =", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("Refresh(", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("_sort", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("_query", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("_availability", handler, StringComparison.Ordinal);
        Assert.Contains("ItemsPanel", handler, StringComparison.Ordinal);
        Assert.Contains("LibraryHorizontalItemsPanel", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("ContainerFromIndex", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("for (", handler, StringComparison.Ordinal);
    }

    [Fact]
    public void App_settings_store_wires_the_stable_view_mode_key_through_the_helper()
    {
        var store = ReadUi("Services", "AppSettingsStore.cs");
        var code = ReadUi("Views", "LibraryPage.xaml.cs");

        Assert.Contains("ui_library_view_mode", store, StringComparison.Ordinal);
        Assert.Contains("LibraryViewModePreference.Parse(Read(", store, StringComparison.Ordinal);
        Assert.Contains("LibraryViewModePreference.Serialize(mode)", store, StringComparison.Ordinal);
        Assert.Contains("_viewMode = AppSettingsStore.ReadLibraryViewMode()", code, StringComparison.Ordinal);
        Assert.Contains("ApplyViewMode(_viewMode, persist: false)", code, StringComparison.Ordinal);
        Assert.Contains("AppSettingsStore.WriteLibraryViewMode(mode)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Both_library_grids_remain_virtualized_and_keep_navigation_handlers()
    {
        var xaml = ReadUi("Views", "LibraryPage.xaml");

        foreach (var grid in new[] { "MoviesGrid", "ShowsGrid" })
        {
            var start = xaml.IndexOf($"x:Name=\"{grid}\"", StringComparison.Ordinal);
            var end = xaml.IndexOf("</GridView>", start, StringComparison.Ordinal);
            Assert.True(start >= 0 && end > start, $"Could not locate {grid}.");
            var block = xaml.Substring(start, end - start);
            Assert.Contains("ScrollViewer.VerticalScrollMode=\"Enabled\"", block, StringComparison.Ordinal);
            Assert.Contains(grid == "MoviesGrid"
                ? "LibraryMoviePosterItemsPanel"
                : "LibraryShowPosterItemsPanel", block, StringComparison.Ordinal);
            Assert.DoesNotContain("<ScrollViewer", block, StringComparison.Ordinal);
        }

        Assert.Contains("ItemClick=\"MoviesGrid_ItemClick\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemClick=\"ShowsGrid_ItemClick\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<RowDefinition Height=\"*\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<ScrollViewer", xaml, StringComparison.Ordinal);

        var code = ReadUi("Views", "LibraryPage.xaml.cs");
        var anchor = Slice(code, "private object? CaptureFirstVisibleItem", "private void Refresh");
        Assert.Contains("panel.FirstVisibleIndex", anchor, StringComparison.Ordinal);
        Assert.DoesNotContain("ContainerFromIndex", anchor, StringComparison.Ordinal);
        Assert.DoesNotContain("for (", anchor, StringComparison.Ordinal);
        Assert.Contains("Resources[\"LibraryMovieCardTemplate\"]", code, StringComparison.Ordinal);
        Assert.Contains("Resources[\"LibraryShowCardTemplate\"]", code, StringComparison.Ordinal);
        Assert.Contains("LibraryMoviePosterItemsPanel", code, StringComparison.Ordinal);
        Assert.Contains("LibraryShowPosterItemsPanel", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Library_panel_resources_preserve_grid_dimensions_and_define_horizontal_card_geometry()
    {
        var page = XDocument.Parse(ReadUi("Views", "LibraryPage.xaml"));
        var theme = XDocument.Parse(ReadUi("Resources", "ThemeResources.xaml"));
        var styles = XDocument.Parse(ReadUi("Resources", "Styles.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var movie = FindPanel(page, x, "LibraryMoviePosterItemsPanel");
        var show = FindPanel(page, x, "LibraryShowPosterItemsPanel");
        var horizontal = FindPanel(page, x, "LibraryHorizontalItemsPanel");
        Assert.Equal("{StaticResource MediaCardItemWidth}", movie.Attribute("ItemWidth")?.Value);
        Assert.Equal("{StaticResource MediaCardItemHeight}", movie.Attribute("ItemHeight")?.Value);
        Assert.Equal("{StaticResource MediaCardItemWidth}", show.Attribute("ItemWidth")?.Value);
        Assert.Equal("{StaticResource ShowCardItemHeight}", show.Attribute("ItemHeight")?.Value);
        Assert.Equal("{StaticResource LibraryHorizontalFallbackCellWidth}", horizontal.Attribute("ItemWidth")?.Value);
        Assert.Equal("{StaticResource LibraryHorizontalCellHeight}", horizontal.Attribute("ItemHeight")?.Value);

        Assert.Equal(96, ResourceDouble(theme, x, "LibraryHorizontalPosterWidth"));
        Assert.Equal(144, ResourceDouble(theme, x, "LibraryHorizontalPosterHeight"));
        Assert.Equal(200, ResourceDouble(theme, x, "LibraryHorizontalCardHeight"));
        Assert.Equal(212, ResourceDouble(theme, x, "LibraryHorizontalCellHeight"));
        Assert.Equal(12, ResourceDouble(theme, x, "LibraryHorizontalGap"));
        Assert.Equal(320, ResourceDouble(theme, x, "LibraryHorizontalCardMinWidth"));
        Assert.Equal(420, ResourceDouble(theme, x, "LibraryHorizontalCardMaxWidth"));
        Assert.Equal(332, ResourceDouble(theme, x, "LibraryHorizontalFallbackCellWidth"));
        Assert.Equal(200, ResourceDouble(theme, x, "LibraryHorizontalCardHeight"));

        var style = styles.Descendants().Single(e => e.Name.LocalName == "Style" && (string?)e.Attribute(x + "Key") == "MediaHorizontalGridViewItemStyle");
        Assert.Equal("{StaticResource LibraryHorizontalCardHeight}", Setter(style, "Height"));
        Assert.Equal("0", Setter(style, "MinWidth"));
        Assert.Equal("0", Setter(style, "MinHeight"));
        Assert.Equal("Stretch", Setter(style, "HorizontalContentAlignment"));
        Assert.DoesNotContain("MediaGridViewItemStyle", style.ToString(), StringComparison.Ordinal);
        Assert.Contains("CardBackgroundFillColorDefaultBrush", style.ToString(), StringComparison.Ordinal);
        Assert.Contains("CardStrokeColorDefaultBrush", style.ToString(), StringComparison.Ordinal);
        Assert.Contains("CornerRadius=\"8\"", style.ToString(), StringComparison.Ordinal);
        foreach (var state in new[] { "PointerOver", "Pressed" })
        {
            Assert.Contains($"x:Name=\"{state}\"", style.ToString(), StringComparison.Ordinal);
        }
        Assert.Contains("UseSystemFocusVisuals", style.ToString(), StringComparison.Ordinal);
        Assert.Contains("FocusVisualMargin", style.ToString(), StringComparison.Ordinal);
        Assert.Contains("Property=\"Padding\" Value=\"0\"", style.ToString(), StringComparison.Ordinal);
        Assert.Contains("Margin=\"{TemplateBinding Padding}\"", style.ToString(), StringComparison.Ordinal);

        var templates = XDocument.Parse(ReadUi("Resources", "Templates.xaml"));
        foreach (var key in new[] { "HorizontalMovieCardTemplate", "HorizontalShowCardTemplate" })
        {
            var template = FindTemplate(templates, x, key);
            Assert.Contains("HorizontalArtworkSource", template.ToString(), StringComparison.Ordinal);
            Assert.Contains("PosterSource", template.ToString(), StringComparison.Ordinal);
            Assert.Contains("MaxLines=\"2\"", template.ToString(), StringComparison.Ordinal);
            Assert.Contains("CornerRadius=\"{ThemeResource ControlCornerRadius}\"", template.ToString(), StringComparison.Ordinal);
            Assert.Contains("YearVisibility", template.ToString(), StringComparison.Ordinal);
            Assert.Contains(key.Contains("Movie", StringComparison.Ordinal) ? "HorizontalSummaryLine" : "SeasonLine", template.ToString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Horizontal_templates_use_real_posters_and_mode_specific_secondary_content()
    {
        var templates = XDocument.Parse(ReadUi("Resources", "Templates.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var compactMovie = FindTemplate(templates, x, "HorizontalMovieCardTemplate");
        var compactShow = FindTemplate(templates, x, "HorizontalShowCardTemplate");
        var originalMovie = FindTemplate(templates, x, "MovieCardTemplate");
        var originalShow = FindTemplate(templates, x, "ShowCardTemplate");

        Assert.Contains("HorizontalArtworkSource", compactMovie.ToString(), StringComparison.Ordinal);
        Assert.Contains("PosterSource", compactMovie.ToString(), StringComparison.Ordinal);
        Assert.Contains("ReviewMatchLabel", compactMovie.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("MultiFileBadgeText", compactMovie.ToString(), StringComparison.Ordinal);
        Assert.Contains("SeasonLine", compactShow.ToString(), StringComparison.Ordinal);
        Assert.Contains("YearText", compactShow.ToString(), StringComparison.Ordinal);
        foreach (var compact in new[] { compactMovie, compactShow })
        {
            Assert.Contains("LandscapeCardScrimBrush", compact.ToString(), StringComparison.Ordinal);
            Assert.Contains("YearText", compact.ToString(), StringComparison.Ordinal);
            Assert.Contains("MaxLines=\"2\"", compact.ToString(), StringComparison.Ordinal);
            if (ReferenceEquals(compact, compactShow))
            {
                Assert.Contains("TextWrapping=\"NoWrap\"", compact.ToString(), StringComparison.Ordinal);
            }
            Assert.DoesNotContain("FlowDirection=\"RightToLeft\"", compact.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("MultiFileBadgeText", compact.ToString(), StringComparison.Ordinal);
        }
        Assert.Contains("MediaCardPosterWidth", originalMovie.ToString(), StringComparison.Ordinal);
        Assert.Contains("MediaCardPosterHeight", originalMovie.ToString(), StringComparison.Ordinal);
        Assert.Contains("MediaCardPosterWidth", originalShow.ToString(), StringComparison.Ordinal);
        Assert.Contains("MediaCardPosterHeight", originalShow.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void View_mode_and_compact_labels_exist_in_english_and_arabic()
    {
        var localization = ReadUi("Services", "LocalizationService.cs");

        foreach (var key in new[] { "LibraryViewModeLabel", "LibraryViewGrid", "LibraryViewCompact" })
        {
            Assert.Equal(2, CountOccurrences(localization, $"[\"{key}\"]"));
        }

        Assert.Contains("[\"LibraryViewCompact\"] = \"Horizontal Cards\"", localization, StringComparison.Ordinal);
        Assert.Contains("[\"LibraryViewCompact\"] = \"بطاقات أفقية\"", localization, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1040, 5)]
    [InlineData(1616, 8)]
    [InlineData(1792, 9)]
    [InlineData(1856, 9)]
    public void Poster_layout_uses_desktop_width_and_preserves_artwork_ratio(double available, int columns)
    {
        var metrics = LibraryPosterLayout.Calculate(available);
        Assert.Equal(columns, metrics.Columns);
        Assert.Equal(available, metrics.CellWidth * columns, precision: 6);
        Assert.InRange(metrics.PosterWidth, 180, 220);
        Assert.Equal(metrics.PosterWidth * 1.5, metrics.PosterHeight, precision: 6);
        Assert.Equal(metrics.PosterHeight + 76, metrics.MovieCellHeight, precision: 6);
        Assert.Equal(metrics.PosterHeight + 94, metrics.ShowCellHeight, precision: 6);
    }

    [Theory]
    [InlineData(1616, 4)]
    [InlineData(1792, 5)]
    [InlineData(1856, 5)]
    public void Horizontal_layout_fills_desktop_rows_without_oversized_cards(double available, int columns)
    {
        var metrics = LibraryHorizontalLayout.Calculate(available);
        Assert.Equal(columns, metrics.Columns);
        Assert.Equal(available, metrics.CellWidth * columns, precision: 6);
        Assert.InRange(metrics.CardWidth, 320, 420);
    }

    [Fact]
    public void Minimum_window_content_width_fits_two_horizontal_or_four_poster_cards()
    {
        // 960 DIP minimum client width minus a 64 DIP rail, 64 DIP page padding and a 16 DIP scrollbar.
        const double usableWidth = 816;

        Assert.True(LibraryHorizontalLayout.Calculate(usableWidth).Columns >= 2);
        Assert.True(LibraryPosterLayout.Calculate(usableWidth).Columns >= 4);
    }

    [Theory]
    [InlineData(191)]
    [InlineData(192)]
    [InlineData(383)]
    [InlineData(384)]
    [InlineData(575)]
    [InlineData(576)]
    public void Poster_layout_fits_column_boundaries_and_keeps_growth_bounded(double available)
    {
        var metrics = LibraryPosterLayout.Calculate(available);
        Assert.True(metrics.Columns * metrics.CellWidth <= available + 0.000001);
        Assert.InRange(metrics.PosterWidth, 0, 220);
        Assert.Equal(metrics.PosterWidth + 12, metrics.CellWidth, precision: 6);
        if (available >= 192) Assert.True(metrics.PosterWidth >= 180);
        // A partial row at small widths keeps the maximum instead of stretching one poster excessively.
        if (available == 383) Assert.Equal(220, metrics.PosterWidth);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(0)]
    [InlineData(-1)]
    public void Poster_layout_has_a_finite_fallback(double available)
    {
        var metrics = LibraryPosterLayout.Calculate(available);
        Assert.Equal(1, metrics.Columns);
        Assert.Equal(180, metrics.PosterWidth);
        Assert.Equal(270, metrics.PosterHeight);
    }

    [Fact]
    public void View_mode_icons_keep_localized_accessible_names_and_tooltips()
    {
        var code = ReadUi("Views", "LibraryPage.xaml.cs");
        foreach (var item in new[] { "GridViewModeItem", "CompactViewModeItem" })
        {
            Assert.Contains($"{item}.Text = string.Empty;", code, StringComparison.Ordinal);
            Assert.Contains($"ToolTipService.SetToolTip({item},", code, StringComparison.Ordinal);
            Assert.Contains($"{item}.SetValue(Microsoft.UI.Xaml.Automation.AutomationProperties.NameProperty,", code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Adaptive_posters_are_scoped_to_library_and_keep_existing_visual_content()
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var page = XDocument.Parse(ReadUi("Views", "LibraryPage.xaml"));
        var shared = XDocument.Parse(ReadUi("Resources", "Templates.xaml"));
        foreach (var key in new[] { "MovieCardTemplate", "ShowCardTemplate" })
        {
            var local = FindTemplate(page, x, "Library" + key);
            Assert.Contains("SizeChanged=\"PosterSurface_SizeChanged\"", local.ToString(), StringComparison.Ordinal);
            Assert.Contains("HorizontalAlignment=\"Stretch\"", local.ToString(), StringComparison.Ordinal);
            Assert.Contains("UniformToFill", local.ToString(), StringComparison.Ordinal);
            Assert.Contains("IsPosterMissing", local.ToString(), StringComparison.Ordinal);
            Assert.Contains("MediaCardPosterWidth", FindTemplate(shared, x, key).ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("PosterSurface_SizeChanged", FindTemplate(shared, x, key).ToString(), StringComparison.Ordinal);
        }
        var movie = FindTemplate(page, x, "LibraryMovieCardTemplate").ToString();
        Assert.Contains("MultiFileBadgeText", movie, StringComparison.Ordinal);
        Assert.Contains("ReviewMatchLabel", movie, StringComparison.Ordinal);
        Assert.Contains("SecondaryLine", FindTemplate(page, x, "LibraryShowCardTemplate").ToString(), StringComparison.Ordinal);
    }

    private static XElement FindTemplate(XDocument document, XNamespace x, string key) =>
        document.Descendants().Single(element =>
            element.Name.LocalName == "DataTemplate" &&
            (string?)element.Attribute(x + "Key") == key);

    private static XElement FindPanel(XDocument document, XNamespace x, string key) =>
        document.Descendants().Single(element =>
            element.Name.LocalName == "ItemsPanelTemplate" &&
            (string?)element.Attribute(x + "Key") == key)
            .Descendants().Single(element => element.Name.LocalName == "ItemsWrapGrid");

    private static double ResourceDouble(XDocument document, XNamespace x, string key) =>
        double.Parse(document.Descendants().Single(element =>
            (string?)element.Attribute(x + "Key") == key).Value, System.Globalization.CultureInfo.InvariantCulture);

    private static string? Setter(XElement style, string property) =>
        style.Descendants().Single(element => element.Name.LocalName == "Setter" &&
            (string?)element.Attribute("Property") == property).Attribute("Value")?.Value;

    private static string Slice(string source, string startToken, string endToken)
    {
        var start = source.IndexOf(startToken, StringComparison.Ordinal);
        var end = source.IndexOf(endToken, start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return source[start..end];
    }

    private static int CountOccurrences(string text, string token) =>
        text.Split(token, StringSplitOptions.None).Length - 1;

    private static string ReadUi(params string[] segments) =>
        File.ReadAllText(segments.Aggregate(UiRoot, Path.Combine));

    private static string FindUiRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "DropSort.UI");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate src/DropSort.UI from the test output directory.");
    }
}
