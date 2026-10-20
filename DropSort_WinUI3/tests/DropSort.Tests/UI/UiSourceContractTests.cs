using System.Xml.Linq;
using Xunit;

namespace DropSort.Tests.UI;

/// <summary>
/// Source-level contract for the WinUI shell. These tests encode the rules the UI pass established:
/// one shared scroll shell per page, responsiveness through visual states instead of code-behind
/// measuring, shared card templates, native selection controls, and no prototype surfaces.
/// </summary>
public sealed class UiSourceContractTests
{
    private static readonly string UiRoot = FindUiRoot();

    private static readonly string[] AllPages =
    [
        "HomePage.xaml",
        "LibraryPage.xaml",
        "MyListsPage.xaml",
        "AddMediaPage.xaml",
        "CheckLibraryPage.xaml",
        "OperationsLogPage.xaml",
        "SettingsPage.xaml",
        "MovieDetailsPage.xaml",
        "TVShowDetailsPage.xaml",
    ];

    [Fact]
    public void All_pages_exist()
    {
        Assert.All(AllPages, page => Assert.True(File.Exists(Path.Combine(UiRoot, "Views", page)), page));
    }

    [Fact]
    public void Shell_keeps_search_in_the_pages_and_uses_the_final_navigation_names()
    {
        var shell = Read("MainWindow.xaml");
        var localization = Read("Services", "LocalizationService.cs");

        Assert.Contains("<NavigationView", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("AutoSuggestBox", shell, StringComparison.Ordinal);
        Assert.Contains("LibraryNavItem", shell, StringComparison.Ordinal);
        Assert.Contains("MyListsNavItem", shell, StringComparison.Ordinal);
        Assert.Contains("[\"Library\"] = \"Library\"", localization, StringComparison.Ordinal);
        Assert.Contains("[\"MyLists\"] = \"My Lists\"", localization, StringComparison.Ordinal);
        Assert.Contains("[\"Library\"] = \"المكتبة\"", localization, StringComparison.Ordinal);
        Assert.Contains("[\"MyLists\"] = \"قوائمي\"", localization, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_page_hosts_its_content_in_the_shared_scroll_shell()
    {
        // The collection/grid pages are the exception: their virtualizing GridView must own its own
        // scroll (a GridView nested in the page ScrollViewer realizes every card and does not virtualize,
        // which was the large-library hang). They use a fixed header row + a scrolling grid row instead.
        var gridPages = new[] { "LibraryPage.xaml", "MyListsPage.xaml" };
        foreach (var page in AllPages)
        {
            var xaml = Read("Views", page);
            if (page == "AddMediaPage.xaml")
            {
                // A native ListView owns the finite review viewport. Its header and footer scroll
                // with the queue; nesting it in a page ScrollViewer would defeat virtualization.
                Assert.Contains("<ListView x:Name=\"Root\"", xaml, StringComparison.Ordinal);
                Assert.Contains("<ListView.Header>", xaml, StringComparison.Ordinal);
                Assert.Contains("<ListView.Footer>", xaml, StringComparison.Ordinal);
                Assert.DoesNotContain("<ScrollViewer", xaml, StringComparison.Ordinal);
            }
            else if (gridPages.Contains(page))
            {
                Assert.DoesNotContain("x:Name=\"Root\" Style=\"{StaticResource PageScrollViewerStyle}\"", xaml, StringComparison.Ordinal);
                Assert.Contains("<Grid.RowDefinitions>", xaml, StringComparison.Ordinal);
                Assert.Contains("<GridView", xaml, StringComparison.Ordinal);
            }
            else
            {
                Assert.Contains(
                    "<ScrollViewer x:Name=\"Root\" Style=\"{StaticResource PageScrollViewerStyle}\"",
                    xaml,
                    StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void Layout_is_driven_by_visual_states_not_by_measuring_in_code()
    {
        foreach (var page in AllPages)
        {
            var xaml = Read("Views", page);
            if (page is not ("LibraryPage.xaml" or "AddMediaPage.xaml"))
            {
                Assert.DoesNotContain("Width=\"{Binding ActualWidth", xaml, StringComparison.Ordinal);
                Assert.DoesNotContain("SizeChanged=\"", xaml, StringComparison.Ordinal);
            }

            var codeBehind = Read("Views", page + ".cs");
            if (page is not ("LibraryPage.xaml" or "MovieDetailsPage.xaml" or "HomePage.xaml"))
            {
                Assert.DoesNotContain("ActualWidth", codeBehind, StringComparison.Ordinal);
            }
            Assert.DoesNotContain("CompactLayoutBreakpoint", codeBehind, StringComparison.Ordinal);
        }

        var library = Read("Views", "LibraryPage.xaml");
        var libraryCode = Read("Views", "LibraryPage.xaml.cs");
        Assert.Contains("SizeChanged=\"LibraryGrid_SizeChanged\"", library, StringComparison.Ordinal);
        Assert.Contains("LibraryGrid_SizeChanged", libraryCode, StringComparison.Ordinal);
        Assert.Contains("LibraryHorizontalLayout.Calculate", libraryCode, StringComparison.Ordinal);
        Assert.Contains("ItemsPanelRoot", libraryCode, StringComparison.Ordinal);
        var layout = Read("Services", "LibraryViewMode.cs");
        Assert.Contains("double.IsNaN", layout, StringComparison.Ordinal);
        Assert.Contains("double.IsInfinity", layout, StringComparison.Ordinal);
        Assert.Contains("FallbackCellWidth", layout, StringComparison.Ordinal);
        Assert.DoesNotContain("MediaCardPosterWidth", libraryCode, StringComparison.Ordinal);
        Assert.DoesNotContain("MediaCardPosterHeight", libraryCode, StringComparison.Ordinal);
        Assert.DoesNotContain("ItemsSource =", Slice(libraryCode, "private void LibraryGrid_SizeChanged", "private void Refresh"), StringComparison.Ordinal);
        Assert.DoesNotContain("ItemTemplate", Slice(libraryCode, "private void LibraryGrid_SizeChanged", "private void Refresh"), StringComparison.Ordinal);

        foreach (var responsive in new[] { "TVShowDetailsPage.xaml" })
        {
            Assert.Contains("<AdaptiveTrigger", Read("Views", responsive), StringComparison.Ordinal);
        }
        Assert.Contains("Root.ActualWidth - Root.Padding.Left - Root.Padding.Right", Read("Views", "MovieDetailsPage.xaml.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void Collection_pages_search_in_page_and_render_the_shared_card_templates()
    {
        var library = Read("Views", "LibraryPage.xaml");
        var lists = Read("Views", "MyListsPage.xaml");

        foreach (var xaml in new[] { library, lists })
        {
            Assert.Contains("AutoSuggestBox", xaml, StringComparison.Ordinal);
            Assert.Contains("x:Name=\"SearchBox\"", xaml, StringComparison.Ordinal);
        }

        Assert.Contains("MovieCardTemplate", library, StringComparison.Ordinal);
        Assert.Contains("ShowCardTemplate", library, StringComparison.Ordinal);
        Assert.Contains("ListMovieCardTemplate", lists, StringComparison.Ordinal);
        Assert.Contains("MovieCardTemplate", Read("Views", "HomePage.xaml"), StringComparison.Ordinal);
    }

    [Fact]
    public void Library_horizontal_cards_are_isolated_from_the_poster_grid()
    {
        var page = Read("Views", "LibraryPage.xaml");
        var styles = Read("Resources", "Styles.xaml");
        var templates = Read("Resources", "Templates.xaml");

        Assert.Contains("x:Key=\"LibraryHorizontalItemsPanel\"", page, StringComparison.Ordinal);
        Assert.Contains("LibraryHorizontalFallbackCellWidth", page, StringComparison.Ordinal);
        Assert.Contains("LibraryHorizontalCellHeight", page, StringComparison.Ordinal);
        Assert.Contains("HorizontalMovieCardTemplate", templates, StringComparison.Ordinal);
        Assert.Contains("HorizontalShowCardTemplate", templates, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"MediaHorizontalGridViewItemStyle\"", styles, StringComparison.Ordinal);
        Assert.Contains("CardBackgroundFillColorDefaultBrush", styles, StringComparison.Ordinal);
        Assert.Contains("CardStrokeColorDefaultBrush", styles, StringComparison.Ordinal);
        var horizontalStyle = Slice(styles, "x:Key=\"MediaHorizontalGridViewItemStyle\"", "</Style>");
        Assert.DoesNotContain("BasedOn=\"{StaticResource MediaGridViewItemStyle}\"", horizontalStyle, StringComparison.Ordinal);
        Assert.DoesNotContain("Property=\"Width\"", horizontalStyle, StringComparison.Ordinal);
        Assert.Contains("Background=\"{ThemeResource CardBackgroundFillColorDefaultBrush}\"", horizontalStyle, StringComparison.Ordinal);
        Assert.Contains("BorderThickness=\"1\"", horizontalStyle, StringComparison.Ordinal);
        Assert.Contains("Property=\"Padding\" Value=\"0\"", horizontalStyle, StringComparison.Ordinal);
        Assert.Contains("UseSystemFocusVisuals", horizontalStyle, StringComparison.Ordinal);
        Assert.Contains("FocusVisualMargin", horizontalStyle, StringComparison.Ordinal);
        Assert.Contains("Margin=\"{TemplateBinding Padding}\"", horizontalStyle, StringComparison.Ordinal);

        var horizontalTemplates = new[]
        {
            Slice(templates, "x:Key=\"HorizontalMovieCardTemplate\"", "</DataTemplate>"),
            Slice(templates, "x:Key=\"HorizontalShowCardTemplate\"", "</DataTemplate>"),
        };
        foreach (var template in horizontalTemplates)
        {
            Assert.Contains("HorizontalArtworkSource", template, StringComparison.Ordinal);
            Assert.Contains("PosterSource", template, StringComparison.Ordinal);
            Assert.Contains("MaxLines=\"2\"", template, StringComparison.Ordinal);
            Assert.DoesNotContain("CardBackgroundFillColorDefaultBrush", template, StringComparison.Ordinal);
            Assert.DoesNotContain("FlowDirection=\"RightToLeft\"", template, StringComparison.Ordinal);
        }

        Assert.Contains("x:Key=\"MediaCardItemWidth\">192", Read("Resources", "ThemeResources.xaml"), StringComparison.Ordinal);
        Assert.Contains("x:Key=\"MediaCardItemHeight\">346", Read("Resources", "ThemeResources.xaml"), StringComparison.Ordinal);
        Assert.Contains("x:Key=\"ShowCardItemHeight\">364", Read("Resources", "ThemeResources.xaml"), StringComparison.Ordinal);
        Assert.DoesNotContain("HorizontalMovieCardTemplate", Slice(templates, "x:Key=\"MovieCardTemplate\"", "x:Key=\"ShowCardTemplate\""), StringComparison.Ordinal);
    }

    [Fact]
    public void Library_horizontal_templates_keep_local_catalog_rules_and_rtl_artwork_orientation()
    {
        var templates = Read("Resources", "Templates.xaml");
        var models = Read("Models", "DemoModels.cs");
        var shows = Read("Models", "ShowModels.cs");
        var localization = Read("Services", "LocalizationService.cs");

        var movie = Slice(templates, "x:Key=\"HorizontalMovieCardTemplate\"", "</DataTemplate>");
        var show = Slice(templates, "x:Key=\"HorizontalShowCardTemplate\"", "</DataTemplate>");
        Assert.Contains("YearVisibility", movie, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{x:Bind YearVisibility}\"", movie, StringComparison.Ordinal);
        Assert.Contains("HorizontalSummaryLine", movie, StringComparison.Ordinal);
        Assert.Contains("SeasonLine", show, StringComparison.Ordinal);
        Assert.Contains("LocalEpisodesLine", show, StringComparison.Ordinal);
        Assert.Contains("HorizontalEpisodesOnDiskFormat", localization, StringComparison.Ordinal);
        Assert.Contains("LocalEpisodeCount", shows, StringComparison.Ordinal);
        Assert.DoesNotContain("MultiFileBadgeText", movie, StringComparison.Ordinal);
        Assert.DoesNotContain("MultiFileBadgeText", show, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Bind Rating", movie, StringComparison.Ordinal);
        Assert.Contains("بطاقات أفقية", localization, StringComparison.Ordinal);
        Assert.Contains("Horizontal", models, StringComparison.Ordinal);
    }

    /// <summary>
    /// The x:Class assertion is a regression guard: the XAML compiler fails - silently, with no
    /// diagnostic - on {x:Bind} inside a ResourceDictionary that has no code-behind.
    /// </summary>
    [Fact]
    public void Shared_poster_grid_cards_keep_their_original_content()
    {
        var templates = Read("Resources", "Templates.xaml");
        var document = XDocument.Parse(templates);
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var originalTemplates = document.Descendants()
            .Where(element => element.Name.LocalName == "DataTemplate"
                && ((string?)element.Attribute(x + "Key") is "MovieCardTemplate" or "ShowCardTemplate" or "ListMovieCardTemplate"))
            .Select(element => element.ToString())
            .ToArray();

        Assert.Contains("x:Class=\"DropSort.UI.Resources.MediaTemplates\"", templates, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"ListMovieCardTemplate\"", templates, StringComparison.Ordinal);
        Assert.Equal(3, originalTemplates.Length);
        foreach (var original in originalTemplates)
        {
            Assert.Contains("Text=\"{x:Bind Title}\"", original, StringComparison.Ordinal);
            Assert.DoesNotContain("{x:Bind Year}", original, StringComparison.Ordinal);
            Assert.DoesNotContain("{x:Bind Rating}", original, StringComparison.Ordinal);
            Assert.DoesNotContain("{x:Bind MetaLine}", original, StringComparison.Ordinal);
            Assert.DoesNotContain("\uE735", original, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Every page-level filter surface is the one shared tab strip: grouped RadioButtons carrying
    /// TabStripItemStyle, whose template supplies the selection visual (subtle hover fill plus an
    /// accent underline) from the framework's own visual states. The negative assertions keep the
    /// tabs from regressing into a document-tab control or a hand-drawn underline.
    /// </summary>
    [Fact]
    public void Selection_surfaces_use_the_shared_native_tab_strip()
    {
        foreach (var page in new[] { "MyListsPage.xaml", "LibraryPage.xaml", "OperationsLogPage.xaml" })
        {
            var xaml = Read("Views", page);
            Assert.Contains("Style=\"{StaticResource TabStripStyle}\"", xaml, StringComparison.Ordinal);
            Assert.Contains("Style=\"{StaticResource TabStripItemStyle}\"", xaml, StringComparison.Ordinal);
            Assert.Contains("GroupName=", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain("<TabView", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain("Underline", xaml, StringComparison.Ordinal);
        }

        var styles = Read("Resources", "Styles.xaml");
        Assert.Contains("x:Key=\"TabStripStyle\"", styles, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"TabStripItemStyle\"", styles, StringComparison.Ordinal);
        Assert.Contains("<VisualState x:Name=\"Checked\">", styles, StringComparison.Ordinal);
        Assert.Contains("<VisualState x:Name=\"PointerOver\">", styles, StringComparison.Ordinal);
    }

    [Fact]
    public void Add_media_uses_one_mixed_review_with_native_selection_controls()
    {
        var xaml = Read("Views", "AddMediaPage.xaml");
        var code = Read("Views", "AddMediaPage.xaml.cs");
        var model = Read("Models", "AddMediaModels.cs");

        Assert.Contains("<ListView x:Name=\"Root\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CheckBox", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectAllButton", xaml, StringComparison.Ordinal);
        Assert.Contains("DeselectAllButton", xaml, StringComparison.Ordinal);
        Assert.Contains("CancelButton", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MediaTypeSelector", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("RadioButton", xaml, StringComparison.Ordinal);
        Assert.Contains("ImportSelectedAsync", code, StringComparison.Ordinal);
        Assert.Contains("DiscoveryClassification.MovieCandidate", model, StringComparison.Ordinal);
        Assert.Contains("DiscoveryClassification.TvEpisodeCandidate", model, StringComparison.Ordinal);
    }

    /// <summary>
    /// A Setter value inside a shared Style is instantiated once and shared by every control the
    /// style is applied to. When that value is a UIElement the first control parents it and the
    /// second one to load dies with "Element is already the child of another element", which the
    /// native XAML core turns into a process kill with no managed stack. That is exactly what took
    /// the app down when two search boxes were loaded at the same time, so element-valued
    /// properties must be declared per instance in each element's own markup.
    /// </summary>
    [Fact]
    public void Shared_styles_never_carry_element_valued_setters()
    {
        var styles = Read("Resources", "Styles.xaml");

        foreach (var elementProperty in new[] { "QueryIcon", "Icon", "Content", "Header" })
        {
            Assert.DoesNotContain($"Property=\"{elementProperty}\"", styles, StringComparison.Ordinal);
        }

        foreach (var page in new[] { "LibraryPage.xaml", "MyListsPage.xaml", "AddMediaPage.xaml" })
        {
            var xaml = Read("Views", page);
            var searchBoxes = Occurrences(xaml, "<AutoSuggestBox");
            Assert.Equal(searchBoxes, Occurrences(xaml, "QueryIcon=\""));
        }
    }

    private static int Occurrences(string haystack, string needle)
    {
        var count = 0;
        var index = haystack.IndexOf(needle, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = haystack.IndexOf(needle, index + needle.Length, StringComparison.Ordinal);
        }

        return count;
    }

    private static string Slice(string source, string startToken, string endToken)
    {
        var start = source.IndexOf(startToken, StringComparison.Ordinal);
        var end = source.IndexOf(endToken, start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return source[start..end];
    }

    [Fact]
    public void Movie_details_preserves_core_behavior_controls()
    {
        var xaml = Read("Views", "MovieDetailsPage.xaml");

        Assert.Contains("x:Name=\"PlayButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"OpenFolderButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"OrganizeButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<Flyout", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ActivityTitleText\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"WatchlistButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"WatchedDatePicker\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"HistoryRepeater\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MaxWidth=\"{StaticResource PageContentMaxWidth}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Movie_details_keeps_the_approved_reading_pane_hierarchy()
    {
        var xaml = Read("Views", "MovieDetailsPage.xaml");
        var document = XDocument.Parse(xaml);
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        XElement Named(string name) => document.Descendants()
            .Single(element => (string?)element.Attribute(x + "Name") == name);

        var gallery = Named("GalleryPanel");
        var readingPane = Named("ReadingPane");
        var dailyCommands = Named("DailyCommandsGrid");
        var recordsGrid = Named("RecordsGrid");
        var watchPane = Named("WatchPane");
        var filesPane = Named("FilesPane");
        var metadataFooter = Named("MetadataFooter");

        Assert.Same(readingPane, Named("WatchControlsGrid").Ancestors().First(ancestor => ancestor == readingPane));
        Assert.Same(watchPane, Named("WatchControlsGrid").Ancestors().First(ancestor => ancestor == watchPane));
        Assert.Same(filesPane, Named("FileActionsPanel").Ancestors().First(ancestor => ancestor == filesPane));
        Assert.Same(recordsGrid, watchPane.Ancestors().First(ancestor => ancestor == recordsGrid));
        Assert.Same(recordsGrid, filesPane.Ancestors().First(ancestor => ancestor == recordsGrid));
        var matchButton = Named("MatchButton");
        Assert.Same(dailyCommands, matchButton.Ancestors().First(ancestor => ancestor == dailyCommands));
        Assert.Contains(matchButton.Descendants(), element => (string?)element.Attribute(x + "Name") == "ChangeMatchButton");
        Assert.Contains(matchButton.Descendants(), element => (string?)element.Attribute(x + "Name") == "RefreshMetadataMenuItem");
        var moreButton = Named("MoreButton");
        Assert.Contains(moreButton.Descendants(), element => (string?)element.Attribute(x + "Name") == "MoveToSeriesButton");
        Assert.Contains(moreButton.Descendants(), element => (string?)element.Attribute(x + "Name") == "RemoveFromLibraryButton");
        var preferenceGroup = Named("PreferenceGroup");
        Assert.Same(preferenceGroup, Named("WatchlistButton").Ancestors().First(ancestor => ancestor == preferenceGroup));
        Assert.Same(preferenceGroup, Named("FavoriteButton").Ancestors().First(ancestor => ancestor == preferenceGroup));
        Assert.Same(metadataFooter, Named("MetadataStatusText").Ancestors().First(ancestor => ancestor == metadataFooter));
        Assert.Same(metadataFooter, Named("MatchedIdentityText").Ancestors().First(ancestor => ancestor == metadataFooter));
        Assert.Same(HeroCopyPanel(document, x), preferenceGroup.Ancestors().First(ancestor => ancestor == HeroCopyPanel(document, x)));
        Assert.Same(HeroCopyPanel(document, x), dailyCommands.Ancestors().First(ancestor => ancestor == HeroCopyPanel(document, x)));
        Assert.Same(readingPane, recordsGrid.Ancestors().First(ancestor => ancestor == readingPane));
        Assert.Same(gallery, Named("PosterSurface").Ancestors().First(ancestor => ancestor == gallery));
        Assert.Contains("Stretch=\"Uniform\"", gallery.ToString(SaveOptions.DisableFormatting), StringComparison.Ordinal);

        var pageResources = document.Root!.Elements().Single(element => element.Name.LocalName == "Page.Resources");
        Assert.Equal("{ThemeResource FigmaSurfaceSecondaryBrush}", (string?)gallery.Attribute("Background"));
        Assert.Contains("FigmaTextPrimaryBrush", pageResources.ToString(SaveOptions.DisableFormatting), StringComparison.Ordinal);
    }

    private static XElement HeroCopyPanel(XDocument document, XNamespace x) => document.Descendants()
        .Single(element => (string?)element.Attribute(x + "Name") == "HeroCopyPanel");

    [Fact]
    public void Movie_details_metadata_and_organization_actions_keep_busy_guards()
    {
        var page = Read("Views", "MovieDetailsPage.xaml.cs");
        var changeMatch = Slice(page, "private async void ChangeMatchButton_Click", "private async Task RefreshMovieMetadataAsync");
        var matchingState = Slice(page, "private void SetMatchingState", "private async void MatchButton_Click");
        var organizingState = Slice(page, "private void SetOrganizing", "private async Task<ContentDialogResult> ConfirmOrganizeAsync");
        var moveToSeries = Slice(page, "private async void MoveToSeriesButton_Click", "private async void RemoveFromLibraryButton_Click");

        Assert.Contains("if (_isOrganizing || _isMatching) return;", changeMatch, StringComparison.Ordinal);
        Assert.Contains("ChangeMatchButton.IsEnabled = !isMatching && !_isOrganizing;", matchingState, StringComparison.Ordinal);
        Assert.Contains("ChangeMatchButton.IsEnabled = !isOrganizing && !_isMatching;", organizingState, StringComparison.Ordinal);
        Assert.Contains("MoveToSeriesButton.IsEnabled = !isOrganizing;", organizingState, StringComparison.Ordinal);
        Assert.Contains("if (_isOrganizing) return;", moveToSeries, StringComparison.Ordinal);
    }

    [Fact]
    public void Movie_details_history_header_and_visibility_follow_current_history_count()
    {
        var page = Read("Views", "MovieDetailsPage.xaml.cs");
        var history = Slice(page, "private void UpdateHistoryVisibility()", "private void UpdateDetailsIcons()");

        Assert.Contains("LocalizationService.Format(\"DetailsViewings\", LocalizationService.Number(_history.Count))", history, StringComparison.Ordinal);
        Assert.Contains("_history.Count == 0 ? \"DetailsNotWatched\" : \"DetailsWatched\"", history, StringComparison.Ordinal);
        Assert.Contains("HistoryRepeater.Visibility = _history.Count == 0 ? Visibility.Collapsed : Visibility.Visible", history, StringComparison.Ordinal);
        Assert.Contains("NoHistoryText.Visibility = _history.Count == 0 ? Visibility.Visible : Visibility.Collapsed", history, StringComparison.Ordinal);
    }

    [Fact]
    public void Movie_poster_is_static_and_preserves_loading_and_missing_artwork()
    {
        var xaml = Read("Views", "MovieDetailsPage.xaml");
        var page = Read("Views", "MovieDetailsPage.xaml.cs");
        var document = XDocument.Parse(xaml);
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var poster = document.Descendants().Single(e => (string?)e.Attribute(x + "Name") == "PosterImage");
        Assert.Equal("Uniform", (string?)poster.Attribute("Stretch"));
        Assert.DoesNotContain(poster.Ancestors(), e => e.Name.LocalName == "Button");
        Assert.DoesNotContain("PosterButton", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("PosterViewLink", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("PosterLightbox", page, StringComparison.Ordinal);
        Assert.DoesNotContain("PreferenceTitleText", xaml, StringComparison.Ordinal);
        Assert.Contains("GetCachedPosterPath", page, StringComparison.Ordinal);
        Assert.Contains("EnsurePosterCachedAsync", page, StringComparison.Ordinal);
        Assert.Contains("PosterImage.ImageFailed", page, StringComparison.Ordinal);
        Assert.Contains("PosterGlyph.Visibility = Visibility.Visible", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Tv_show_details_uses_native_expanders_and_a_real_menu_flyout()
    {
        var xaml = Read("Views", "TVShowDetailsPage.xaml");

        Assert.Contains("<Expander", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SeasonsRepeater\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PlayNextButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<MenuFlyout", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Shell_wires_home_library_lists_and_contextual_detail_back_navigation()
    {
        var shell = Read("MainWindow.xaml.cs");
        var movieDetails = Read("Views", "MovieDetailsPage.xaml.cs");
        var showDetails = Read("Views", "TVShowDetailsPage.xaml.cs");

        Assert.Contains("_homePage.MovieSelected += OpenMovie", shell, StringComparison.Ordinal);
        Assert.Contains("_homePage.ShowSelected += OpenShow", shell, StringComparison.Ordinal);
        Assert.Contains("_libraryPage.MovieSelected += OpenMovie", shell, StringComparison.Ordinal);
        Assert.Contains("_libraryPage.ShowSelected += OpenShow", shell, StringComparison.Ordinal);
        Assert.Contains("_myListsPage.MovieSelected += OpenMovie", shell, StringComparison.Ordinal);
        Assert.Contains("ReturnDestinationFor(sender)", shell, StringComparison.Ordinal);
        Assert.Contains("\"home\" => HomeNavItem", shell, StringComparison.Ordinal);
        Assert.Contains("SetReturnDestination", movieDetails, StringComparison.Ordinal);
        Assert.Contains("SetReturnDestination", showDetails, StringComparison.Ordinal);
        Assert.Contains("BackToHome", movieDetails, StringComparison.Ordinal);
        Assert.Contains("BackToMyLists", movieDetails, StringComparison.Ordinal);
    }

    [Fact]
    public void Tv_hierarchy_keeps_lossless_show_season_episode_identity()
    {
        var models = Read("Models", "ShowModels.cs");
        var projection = Read("Services", "TvProjection.cs");
        var details = Read("Views", "TVShowDetailsPage.xaml.cs");
        var detailsXaml = Read("Views", "TVShowDetailsPage.xaml");

        Assert.Contains("EpisodeSelectionRecord", models, StringComparison.Ordinal);
        Assert.Contains("int ShowId", models, StringComparison.Ordinal);
        Assert.Contains("int SeasonNumber", models, StringComparison.Ordinal);
        Assert.Contains("int EpisodeNumber", models, StringComparison.Ordinal);

        // The row carries the catalog's episode id, and the page resolves an action through it rather
        // than by matching numbers.
        Assert.Contains("int EpisodeId", models, StringComparison.Ordinal);
        Assert.Contains("episode.Id == row.EpisodeId", details, StringComparison.Ordinal);
        Assert.Contains("season.Id,", projection, StringComparison.Ordinal);
        Assert.Contains("episode.Id,", projection, StringComparison.Ordinal);
        Assert.Contains("show.Id,", details, StringComparison.Ordinal);
        Assert.Contains("season.Number,", details, StringComparison.Ordinal);
        Assert.Contains("episode.Number,", details, StringComparison.Ordinal);
        Assert.Contains("Tag=\"{x:Bind}\"", detailsXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Show_cards_localize_singular_and_plural_season_counts()
    {
        var localization = Read("Services", "LocalizationService.cs");
        var library = Read("Views", "LibraryPage.xaml.cs");

        Assert.Contains("[\"SeasonAvailableFormat\"] = \"1 season", localization, StringComparison.Ordinal);
        Assert.Contains("[\"SeasonAvailableFormat\"] = \"موسم واحد", localization, StringComparison.Ordinal);
        Assert.Contains("show.SeasonCount == 1", library, StringComparison.Ordinal);
        Assert.Contains("\"SeasonAvailableFormat\"", library, StringComparison.Ordinal);
        Assert.Contains("\"SeasonsAvailableFormat\"", library, StringComparison.Ordinal);
    }

    [Fact]
    public void My_lists_localizes_singular_and_plural_item_counts()
    {
        var localization = Read("Services", "LocalizationService.cs");
        var lists = Read("Views", "MyListsPage.xaml.cs");

        Assert.Contains("[\"ItemSingular\"] = \"item\"", localization, StringComparison.Ordinal);
        Assert.Contains("[\"ItemSingular\"] = \"عنصر واحد\"", localization, StringComparison.Ordinal);
        Assert.Contains("items.Count == 1", lists, StringComparison.Ordinal);
        Assert.Contains("\"ItemSingular\"", lists, StringComparison.Ordinal);
        Assert.Contains("\"Items\"", lists, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_theme_help_tracks_the_selected_theme()
    {
        var localization = Read("Services", "LocalizationService.cs");
        var settings = Read("Views", "SettingsPage.xaml.cs");

        Assert.Contains("ThemeHelpSlate", localization, StringComparison.Ordinal);
        Assert.Contains("ThemeHelpDark", localization, StringComparison.Ordinal);
        Assert.Contains("ThemeHelpLight", localization, StringComparison.Ordinal);
        Assert.Contains("RefreshThemeHelp();", settings, StringComparison.Ordinal);
        Assert.Contains("ThemeService.CurrentTheme switch", settings, StringComparison.Ordinal);
    }

    [Fact]
    public void Operations_log_localizes_singular_and_plural_entry_counts()
    {
        var localization = Read("Services", "LocalizationService.cs");
        var operations = Read("Views", "OperationsLogPage.xaml.cs");

        Assert.Contains("[\"EntryFormat\"] = \"{0} entry\"", localization, StringComparison.Ordinal);
        Assert.Contains("[\"EntryFormat\"] = \"سجل واحد\"", localization, StringComparison.Ordinal);
        Assert.Contains("entries.Count == 1", operations, StringComparison.Ordinal);
        Assert.Contains("\"EntryFormat\"", operations, StringComparison.Ordinal);
        Assert.Contains("\"EntriesFormat\"", operations, StringComparison.Ordinal);
    }

    [Fact]
    public void Movie_surfaces_read_the_catalog_instead_of_a_demo_table()
    {
        // Every movie-facing page goes through AppServices now. DemoData is the bundled TV sample and
        // nothing else, so a page that reads a movie out of it would be sample data pretending to be
        // the user's library.
        string[] pages =
        [
            "LibraryPage",
            "HomePage",
            "MyListsPage",
            "MovieDetailsPage",
            "AddMediaPage",
            "CheckLibraryPage",
            "OperationsLogPage",
        ];

        foreach (var page in pages)
        {
            var source = Read("Views", page + ".xaml.cs");

            Assert.DoesNotContain("DemoData.Movies", source, StringComparison.Ordinal);
            Assert.DoesNotContain("DemoData.Operations", source, StringComparison.Ordinal);
            Assert.DoesNotContain("DemoData.Issues", source, StringComparison.Ordinal);
            Assert.DoesNotContain("DemoData.Detected", source, StringComparison.Ordinal);
            Assert.DoesNotContain("DemoData.Check", source, StringComparison.Ordinal);
            Assert.DoesNotContain("DemoData.Lists", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Every_page_that_changes_stored_state_reports_a_failure_instead_of_swallowing_it()
    {
        // A catch that neither reports nor recovers is how a dead button looks from the outside: the
        // click appears to work and nothing was written.
        string[] pages = ["MovieDetailsPage", "AddMediaPage", "CheckLibraryPage", "SettingsPage"];

        foreach (var page in pages)
        {
            var source = Read("Views", page + ".xaml.cs");

            Assert.Contains("AppServices", source, StringComparison.Ordinal);
            Assert.Contains("ShowMessageAsync", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Backendless_controls_stay_native_instead_of_opening_prototype_popups()
    {
        var source = string.Join(Environment.NewLine, EnumerateUiSource().Select(File.ReadAllText));

        Assert.DoesNotContain("InertNotice", source, StringComparison.Ordinal);
        Assert.DoesNotContain("safe UI preview", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Preview only", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Coming soon", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Not implemented", source, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A real move can take minutes on another volume, so it is confirmed with the actual source and
    /// destination and then run off the UI thread, with the file actions locked while it runs.
    /// </summary>
    [Fact]
    public void Organize_confirms_the_move_and_runs_it_off_the_ui_thread()
    {
        var page = Read("Views", "MovieDetailsPage.xaml.cs");

        Assert.Contains("ConfirmOrganizeAsync", page, StringComparison.Ordinal);
        Assert.Contains("OrganizationConfirmRename", page, StringComparison.Ordinal);
        Assert.Contains("preview.Plan.Source", page, StringComparison.Ordinal);
        Assert.Contains("preview.Plan.Destination", page, StringComparison.Ordinal);
        var editor = Read("Views", "MovieOrganizationDialog.cs");
        Assert.Contains("await Task.Run(() => _organization.PrepareOrganization", editor, StringComparison.Ordinal);
        Assert.Contains("MovieRenameStays", editor, StringComparison.Ordinal);
        Assert.DoesNotContain("RadioButton", editor, StringComparison.Ordinal);
        Assert.Contains("MoveMovieButton_Click", page, StringComparison.Ordinal);
        Assert.Contains("SecondaryButtonText", page, StringComparison.Ordinal);
        Assert.Contains("organization.DiscardOrganizationPreview(preview.PreviewId)", page, StringComparison.Ordinal);
        Assert.DoesNotContain("AppSettingsStore.MovieFolder", page, StringComparison.Ordinal);
        Assert.Contains("await Task.Run(() => organization.ConfirmOrganization", page, StringComparison.Ordinal);
        Assert.Contains("DiscardOrganizationPreview", page, StringComparison.Ordinal);
        Assert.Contains("SetOrganizing", page, StringComparison.Ordinal);
        Assert.Contains("OrganizeProgress", Read("Views", "MovieDetailsPage.xaml"), StringComparison.Ordinal);
    }

    /// <summary>
    /// A stop signal handed to a worker thread goes through a CancellationTokenSource; a plain bool
    /// field is not guaranteed to be observed on the other thread.
    /// </summary>
    [Fact]
    public void The_long_running_flows_cancel_through_a_token_source()
    {
        foreach (var page in new[] { "AddMediaPage.xaml.cs", "CheckLibraryPage.xaml.cs" })
        {
            var source = Read("Views", page);
            Assert.Contains("CancellationTokenSource", source, StringComparison.Ordinal);
            Assert.Contains("IsCancellationRequested", source, StringComparison.Ordinal);
            Assert.DoesNotContain("_cancelRequested", source, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The composition root publishes services only after every one of them is built, retries after a
    /// failure instead of latching it for the session, and closes the journal store it owns so SQLite
    /// can check-point the write-ahead log.
    /// </summary>
    [Fact]
    public void The_composition_root_retries_and_closes_what_it_owns()
    {
        var services = Read("Services", "AppServices.cs");
        var shell = Read("MainWindow.xaml.cs");

        var initialize = services[services.IndexOf("public static void Initialize()", StringComparison.Ordinal)..];
        var tryIndex = initialize.IndexOf("try", StringComparison.Ordinal);
        var latchIndex = initialize.IndexOf("_initialized = true", StringComparison.Ordinal);
        Assert.True(tryIndex > 0 && latchIndex > tryIndex, "the initialized flag must not be set before the attempt");

        Assert.Contains("public static void Shutdown()", services, StringComparison.Ordinal);
        Assert.Contains("_operationStore?.Dispose()", services, StringComparison.Ordinal);
        Assert.Contains("_coordinator?.Dispose()", services, StringComparison.Ordinal);
        Assert.Contains("ReleasePooledConnections()", services, StringComparison.Ordinal);
        Assert.Contains("AppServices.Shutdown()", shell, StringComparison.Ordinal);
    }

    /// <summary>
    /// Check Library counts movies, not movies plus their files: the old total reported a four-movie
    /// library as eight items, and the summary line claimed most media was healthy at any issue count.
    /// </summary>
    [Fact]
    public void Check_library_counts_movies_rather_than_movies_plus_files()
    {
        var page = Read("Views", "CheckLibraryPage.xaml.cs");

        Assert.DoesNotContain("FileProgress.CheckedFiles + result.TotalMovies", page, StringComparison.Ordinal);
        Assert.Contains("AttentionItemCount", page, StringComparison.Ordinal);
        Assert.Contains("CheckIssuesFormat", page, StringComparison.Ordinal);
        Assert.Contains("FilesCheckedFormat", page, StringComparison.Ordinal);
        Assert.DoesNotContain("\"CheckCompleteHelp\"", page, StringComparison.Ordinal);

        // The row action resolves the registered file by id, not by a file name two folders can share.
        Assert.Contains("Tag=\"{x:Bind MediaFileId}\"", Read("Views", "CheckLibraryPage.xaml"), StringComparison.Ordinal);
        Assert.Contains("Tag: int mediaFileId", page, StringComparison.Ordinal);
    }

    /// <summary>Copy and Save write the rows on screen, and a failed save is reported, not swallowed.</summary>
    [Fact]
    public void The_operations_log_copies_and_saves_the_same_rows()
    {
        var page = Read("Views", "OperationsLogPage.xaml.cs");

        Assert.Contains("SaveOperationHistory(_visible", page, StringComparison.Ordinal);
        Assert.Contains("_visible.Select(ToRecord)", page, StringComparison.Ordinal);
        Assert.Contains("LogSaveFailed", page, StringComparison.Ordinal);
        Assert.Contains("InfoBarSeverity.Error", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The review list exposes selective row selection, so the primary action names the selected set.
    /// </summary>
    [Fact]
    public void Add_media_names_its_action_after_what_it_registers()
    {
        var page = Read("Views", "AddMediaPage.xaml.cs");
        var localization = Read("Services", "LocalizationService.cs");

        Assert.Contains("AddSelectedButton", page, StringComparison.Ordinal);
        Assert.Contains("AddSelectedFormat", page, StringComparison.Ordinal);
        Assert.DoesNotContain("AddAllFormat", page, StringComparison.Ordinal);
        Assert.Contains("[\"AddSelectedFormat\"] = \"Add selected ({0})\"", localization, StringComparison.Ordinal);
    }

    [Fact]
    public void Add_media_separates_local_readiness_from_metadata_outcomes()
    {
        var model = Read("Models", "AddMediaModels.cs");
        var page = Read("Views", "AddMediaPage.xaml.cs");
        var localization = Read("Services", "LocalizationService.cs");

        Assert.Contains("ImportItemStatus.ReadyToAdd", model, StringComparison.Ordinal);
        Assert.Contains("ImportItemStatus.Matching", model, StringComparison.Ordinal);
        Assert.Contains("ImportItemStatus.Matched", model, StringComparison.Ordinal);
        Assert.Contains("ImportItemStatus.NeedsMatch", model, StringComparison.Ordinal);
        Assert.Contains("ImportItemStatus.MetadataUnavailable", model, StringComparison.Ordinal);
        Assert.Contains("ImportItemStatus.MetadataError", model, StringComparison.Ordinal);
        Assert.Contains("ImportItemStatus.AlreadyInLibrary", model, StringComparison.Ordinal);
        Assert.Contains("private ImportItemStatus _status = ImportItemStatus.ReadyToAdd;", model, StringComparison.Ordinal);
        Assert.Contains("ImportItemStatus.ReadyToAdd => LocalizationService.Text(\"ReadyToAdd\")", model, StringComparison.Ordinal);
        Assert.Contains("ImportItemStatus.AlreadyInLibrary => LocalizationService.Text(\"AlreadyInLibrary\")", model, StringComparison.Ordinal);
        Assert.Contains("public bool CanAdd", model, StringComparison.Ordinal);
        Assert.Contains("ImportAddedFormat", page, StringComparison.Ordinal);
        Assert.Contains("ImportMatchedFormat", page, StringComparison.Ordinal);
        Assert.Contains("ImportNeedsAttentionFormat", page, StringComparison.Ordinal);
        Assert.DoesNotContain("LocalizationService.Format(\"ImportNoMatchFoundFormat\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("LocalizationService.Format(\"ImportMetadataUnavailableFormat\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("LocalizationService.Format(\"ImportMetadataErrorsFormat\"", page, StringComparison.Ordinal);
        Assert.Contains("if (result.RegistrationFailuresCount > 0)", page, StringComparison.Ordinal);
        Assert.Contains("if (result.ExistingCount > 0)", page, StringComparison.Ordinal);
        Assert.Contains("[\"ImportNeedsAttentionFormat\"] = \"Needs attention: {0}\"", localization, StringComparison.Ordinal);
        Assert.Contains("[\"ImportNeedsAttentionFormat\"] = \"يحتاج إلى الانتباه: {0}\"", localization, StringComparison.Ordinal);
        Assert.Contains("[\"ReadyToAdd\"] = \"Ready to add\"", localization, StringComparison.Ordinal);
        Assert.Contains("[\"MetadataUnavailable\"] = \"Metadata unavailable\"", localization, StringComparison.Ordinal);
        Assert.Contains("[\"NeedsMatch\"] = \"Needs match\"", localization, StringComparison.Ordinal);
        Assert.Contains("[\"AlreadyInLibrary\"] = \"Already in Library\"", localization, StringComparison.Ordinal);
        Assert.Contains("[\"ReadyToAdd\"] = \"جاهز للإضافة\"", localization, StringComparison.Ordinal);
        Assert.Contains("[\"AlreadyInLibrary\"] = \"موجود بالفعل في المكتبة\"", localization, StringComparison.Ordinal);
        Assert.Contains("[\"MetadataUnavailable\"] = \"البيانات الوصفية غير متاحة\"", localization, StringComparison.Ordinal);
    }

    [Fact]
    public void Add_media_checks_exact_registered_paths_before_metadata_and_excludes_them_from_add()
    {
        var contract = ReadApplicationSource("Contracts", "IUiActions.cs");
        var service = ReadApplicationSource("Services", "ImportService.cs");
        var model = Read("Models", "AddMediaModels.cs");
        var page = Read("Views", "AddMediaPage.xaml.cs");

        Assert.Contains("bool IsMediaFileRegistered(string filePath)", contract, StringComparison.Ordinal);
        Assert.Contains("public bool IsMediaFileRegistered(string filePath)", service, StringComparison.Ordinal);
        Assert.Contains("uow.MediaFiles.GetByPath(filePath)", service, StringComparison.Ordinal);
        Assert.Contains("if (IsMediaFileRegistered(item.Path))", service, StringComparison.Ordinal);
        Assert.Contains("Status: ImportItemStatus.AlreadyInLibrary", service, StringComparison.Ordinal);
        Assert.Contains("public void SetAlreadyInLibrary()", model, StringComparison.Ordinal);
        Assert.Contains("AppServices.Import.IsMediaFileRegistered(row.Item.Path)", page, StringComparison.Ordinal);
        Assert.Contains("row.SetAlreadyInLibrary()", page, StringComparison.Ordinal);
        Assert.Contains("_reviewCounts.Eligible", page, StringComparison.Ordinal);
        Assert.Contains("!row.CanAdd || !row.IsSelected", page, StringComparison.Ordinal);
        Assert.Contains("row.Status != ImportItemStatus.AlreadyInLibrary", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Add_media_reviews_metadata_before_registration_and_keeps_row_recovery_actions()
    {
        var dto = ReadApplicationSource("Dto", "CatalogDtos.cs");
        var contract = ReadApplicationSource("Contracts", "IUiActions.cs");
        var service = ReadApplicationSource("Services", "ImportService.cs");
        var model = Read("Models", "AddMediaModels.cs");
        var xaml = Read("Views", "AddMediaPage.xaml");
        var page = Read("Views", "AddMediaPage.xaml.cs");

        Assert.Contains("record ImportMetadataPreview", dto, StringComparison.Ordinal);
        Assert.Contains("ImportSelection(DiscoveredMedia Item, ImportMetadataPreview? MetadataPreview", dto, StringComparison.Ordinal);
        Assert.Contains("PreviewMetadataAsync", contract, StringComparison.Ordinal);
        Assert.Contains("RetryMetadataAsync", contract, StringComparison.Ordinal);
        Assert.Contains("SearchMovieCandidatesAsync", service, StringComparison.Ordinal);
        Assert.Contains("SearchTvCandidatesAsync", service, StringComparison.Ordinal);
        Assert.Contains("BeginMetadataReview", page, StringComparison.Ordinal);
        Assert.Contains("PreviewMetadataAsync(row.Item", page, StringComparison.Ordinal);
        Assert.Contains("RetryMetadataAsync(row.Item", page, StringComparison.Ordinal);
        Assert.Contains("MatchMediaDialog.ShowForMovieAsync", page, StringComparison.Ordinal);
        Assert.Contains("MatchMediaDialog.ShowForTvShowAsync", page, StringComparison.Ordinal);
        Assert.Contains("ImportSelection(row.Item, row.MetadataPreview, row.ExistingShowId", page, StringComparison.Ordinal);
        Assert.Contains("ManualMatch_Click", xaml, StringComparison.Ordinal);
        Assert.Contains("RetryMetadata_Click", xaml, StringComparison.Ordinal);
        Assert.Contains("ImportItemStatus.NoMatchFound", model, StringComparison.Ordinal);
        Assert.Contains("IDeactivatableView", page, StringComparison.Ordinal);
        Assert.Contains("public void Deactivate()", page, StringComparison.Ordinal);
        Assert.Contains("row.SetMetadataCancelled()", page, StringComparison.Ordinal);
        Assert.Contains("SetMetadataCancelled", model, StringComparison.Ordinal);
        Assert.Contains("IDeactivatableView deactivatable", Read("MainWindow.xaml.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("AppSettingsStore.MovieFolder = folder.Path", page, StringComparison.Ordinal);
        Assert.DoesNotContain("AppSettingsStore.ShowFolder = folder.Path", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Add_media_row_information_uses_selectable_static_text_not_input_fields()
    {
        var xaml = Read("Views", "AddMediaPage.xaml");
        var rowStart = xaml.IndexOf("<DataTemplate x:Key=\"FileReviewTemplate\"", StringComparison.Ordinal);
        var rowEnd = xaml.IndexOf("</DataTemplate>", rowStart, StringComparison.Ordinal);

        Assert.True(rowStart >= 0 && rowEnd > rowStart);
        var row = xaml.Substring(rowStart, rowEnd - rowStart);
        Assert.DoesNotContain("<TextBox", row, StringComparison.Ordinal);
        Assert.DoesNotContain("IsReadOnly=", row, StringComparison.Ordinal);
        Assert.True(Occurrences(row, "IsTextSelectionEnabled=\"True\"") >= 4);
        Assert.Contains("RowDetails_Click", row, StringComparison.Ordinal);
        // The redundant standalone "Search TMDB" card was removed; manual search now lives in the
        // Match with TMDB dialog reached from a row's manual-match action.
        Assert.DoesNotContain("TmdbSearchButton", xaml, StringComparison.Ordinal);
        Assert.Contains("ManualMatch_Click", row, StringComparison.Ordinal);
    }

    [Fact]
    public void Manual_match_uses_one_selectable_ranked_candidate_surface()
    {
        var dialog = Read("Views", "MatchMediaDialog.cs");
        var addMedia = Read("Views", "AddMediaPage.xaml.cs");

        Assert.Contains("ListView", dialog, StringComparison.Ordinal);
        Assert.Contains("ApplyMatch", dialog, StringComparison.Ordinal);
        Assert.Contains("IsTextSelectionEnabled = true", dialog, StringComparison.Ordinal);
        Assert.Contains("MatchConfidenceFormat", dialog, StringComparison.Ordinal);
        Assert.Contains("TmdbPopularityFormat", dialog, StringComparison.Ordinal);
        Assert.Contains("CachedPosterPath", dialog, StringComparison.Ordinal);
        // The manual-match surface is reached per row (detected title + year), not from a standalone card.
        Assert.Contains("MatchMediaDialog.ShowForMovieAsync(this, row.Title, year)", addMedia, StringComparison.Ordinal);
    }

    /// <summary>
    /// Only the composition root knows about Infrastructure and FileSystem, and only it knows where the
    /// data folder is. A view that built either for itself would be a second source of truth.
    /// </summary>
    [Fact]
    public void Only_the_composition_root_knows_the_infrastructure_and_the_data_folder()
    {
        foreach (var path in EnumerateUiSource())
        {
            if (Path.GetFileName(path) == "AppServices.cs")
            {
                continue;
            }

            var source = File.ReadAllText(path);
            Assert.DoesNotContain("using DropSort.Infrastructure", source, StringComparison.Ordinal);
            Assert.DoesNotContain("using DropSort.FileSystem", source, StringComparison.Ordinal);
            Assert.DoesNotContain("SpecialFolder.LocalApplicationData", source, StringComparison.Ordinal);
        }

        Assert.Contains("AppServices.DataFolder", Read("Views", "SettingsPage.xaml.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_media_folders_support_add_replace_remove_without_a_two_slot_limit()
    {
        var store = Read("Services", "AppSettingsStore.cs");
        var xaml = Read("Views", "SettingsPage.xaml");
        var code = Read("Views", "SettingsPage.xaml.cs");

        // A real list of roots with explicit Add / Replace / Remove, not the old fixed movie/show slots.
        Assert.Contains("ConfiguredMediaFolders", store, StringComparison.Ordinal);
        Assert.Contains("Distinct(StringComparer.OrdinalIgnoreCase)", store, StringComparison.Ordinal);
        Assert.Contains("public static bool AddMediaFolder(", store, StringComparison.Ordinal);
        Assert.Contains("public static bool ReplaceMediaFolder(", store, StringComparison.Ordinal);
        Assert.Contains("public static bool RemoveMediaFolder(", store, StringComparison.Ordinal);
        // The old two-slot limit and its "no additional folder could be stored" behaviour are gone.
        Assert.DoesNotContain("TryAddMediaFolder", store, StringComparison.Ordinal);

        Assert.Contains("MediaFoldersLabelText", xaml, StringComparison.Ordinal);
        Assert.Contains("StorageAdvancedHeadingText", xaml, StringComparison.Ordinal);
        Assert.Contains("AddMediaFolder_Click", code, StringComparison.Ordinal);
        Assert.Contains("ReplaceMediaFolder_Click", code, StringComparison.Ordinal);
        Assert.Contains("RemoveMediaFolder_Click", code, StringComparison.Ordinal);
        Assert.DoesNotContain("MovieFoldersLabelText", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("TvFoldersLabelText", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_no_longer_expose_import_or_export_data()
    {
        // The non-functional Import/Export Data stubs are removed; the real backup/restore internals stay.
        var xaml = Read("Views", "SettingsPage.xaml");
        var code = Read("Views", "SettingsPage.xaml.cs");
        Assert.DoesNotContain("ExportButton", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ImportButton", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ExportButton_Click", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ImportButton_Click", code, StringComparison.Ordinal);

        // Backup/restore infrastructure is untouched and still wired to migrations and clear-catalog.
        var migrator = Read("../DropSort.Infrastructure/Persistence/Migrations/DatabaseMigrator.cs");
        var maintenance = Read("../DropSort.Infrastructure/Persistence/Repositories/MaintenanceRepository.cs");
        Assert.Contains("DatabaseSafetyBackup", migrator, StringComparison.Ordinal);
        Assert.Contains("DatabaseSafetyBackup.CreateVerifiedBackup", maintenance, StringComparison.Ordinal);
    }

    /// <summary>
    /// Typing in a search box filters rows already in memory. Reading the whole catalog, or the settings
    /// table, on every refresh put a database query behind every keystroke.
    /// </summary>
    [Fact]
    public void The_collection_pages_do_not_query_on_every_keystroke()
    {
        var library = Read("Views", "LibraryPage.xaml.cs");
        var lists = Read("Views", "MyListsPage.xaml.cs");
        var check = Read("Views", "CheckLibraryPage.xaml.cs");

        Assert.Contains("if (_movies is not null)", library, StringComparison.Ordinal);
        Assert.Contains("_movies = null;", library, StringComparison.Ordinal);
        Assert.Contains("_sectionItems", lists, StringComparison.Ordinal);
        Assert.Contains("private void Invalidate()", lists, StringComparison.Ordinal);
        Assert.Contains("_missing = MissingFiles();", check, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var missing in MissingFiles())", check, StringComparison.Ordinal);
    }

    [Fact]
    public void Library_open_actions_record_only_successful_movie_or_show_navigation()
    {
        var library = Read("Views", "LibraryPage.xaml.cs");
        var shell = Read("MainWindow.xaml.cs");
        Assert.Contains("RecordMovieOpened", library, StringComparison.Ordinal);
        Assert.Contains("RecordShowOpened", library, StringComparison.Ordinal);
        Assert.Contains("InvalidateCatalog", library, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(sender, _libraryPage)", shell, StringComparison.Ordinal);
        Assert.Contains("IsLoadedSuccessfully", shell, StringComparison.Ordinal);
    }

    /// <summary>One helper labels the watch rows, so the first watch cannot move when a row is added.</summary>
    [Fact]
    public void The_watch_history_labels_come_from_one_helper()
    {
        var projection = Read("Services", "LibraryProjection.cs");
        var details = Read("Views", "MovieDetailsPage.xaml.cs");

        Assert.Contains("public static IReadOnlyList<WatchHistoryRecord> ToWatchHistory(", projection, StringComparison.Ordinal);
        Assert.Contains("index < events.Count - 1", projection, StringComparison.Ordinal);
        Assert.Contains("LibraryProjection.ToWatchHistory(events)", details, StringComparison.Ordinal);
        Assert.DoesNotContain("\"FirstWatch\"", details, StringComparison.Ordinal);
    }

    [Fact]
    public void Home_uses_real_persisted_history_progression_and_cached_artwork()
    {
        var home = Read("Views", "HomePage.xaml.cs");
        var xaml = Read("Views", "HomePage.xaml");
        var models = Read("Models", "ShowModels.cs");
        var services = Read("Services", "AppServices.cs");
        var tvDetails = Read("Views", "TVShowDetailsPage.xaml.cs");
        var tvXaml = Read("Views", "TVShowDetailsPage.xaml");

        Assert.Contains("AppServices.ViewingActivity.ListRecentCompleted(20)", home, StringComparison.Ordinal);
        Assert.Contains("AppServices.ViewingActivity.ListActive(20)", home, StringComparison.Ordinal);
        Assert.DoesNotContain("RecordEpisodeWatch", home, StringComparison.Ordinal);
        Assert.Contains("if (!card.IsAvailable)", home, StringComparison.Ordinal);
        Assert.Contains("HistoryEmptyState", xaml, StringComparison.Ordinal);
        Assert.Contains("ArtworkSource, Mode=OneWay", xaml, StringComparison.Ordinal);
        Assert.Contains("GetCachedPosterPath", models, StringComparison.Ordinal);
        Assert.Contains("EnsurePosterCachedAsync", models, StringComparison.Ordinal);
        Assert.Contains("foreach (var reference in candidates)", models, StringComparison.Ordinal);
        Assert.Contains("selection.Episode.HasLocalFile && !selection.Episode.Watched", models, StringComparison.Ordinal);
        Assert.Contains("new ViewingActivityService", services, StringComparison.Ordinal);
        Assert.Contains("EpisodeMarkWatched_Click", tvDetails, StringComparison.Ordinal);
        Assert.Contains("AppServices.WatchHistory.RecordEpisodeWatch(episode.Id)", tvDetails, StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"{x:Bind CanMarkWatched}\"", tvXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Tv_missing_episodes_render_quieter_and_hide_local_file_actions()
    {
        var models = Read("Models", "ShowModels.cs");
        var projection = Read("Services", "TvProjection.cs");
        var tvDetails = Read("Views", "TVShowDetailsPage.xaml.cs");
        var tvXaml = Read("Views", "TVShowDetailsPage.xaml");

        Assert.Contains("public bool HasActions => HasLocalFile;", models, StringComparison.Ordinal);
        Assert.Contains("public double RowOpacity => HasLocalFile ? 1.0 : 0.8;", models, StringComparison.Ordinal);
        Assert.Contains("episode.Availability is EpisodeAvailability.Missing or EpisodeAvailability.NoFile || episode.Id == 0", projection, StringComparison.Ordinal);
        Assert.Contains("Opacity=\"{x:Bind RowOpacity}\"", tvXaml, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{x:Bind HasActions}\"", tvXaml, StringComparison.Ordinal);
        Assert.Contains("episode.Id <= 0", tvDetails, StringComparison.Ordinal);
    }

    private static IEnumerable<string> EnumerateUiSource() =>
        Directory.EnumerateFiles(UiRoot, "*.*", SearchOption.AllDirectories)
            .Where(path => (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    private static string Read(params string[] relativeSegments) =>
        File.ReadAllText(relativeSegments.Aggregate(UiRoot, Path.Combine));

    private static string ReadApplicationSource(params string[] relativeSegments) =>
        File.ReadAllText(relativeSegments.Aggregate(FindApplicationRoot(), Path.Combine));

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

    private static string FindApplicationRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "DropSort.Application");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate src/DropSort.Application from the test output directory.");
    }
}
