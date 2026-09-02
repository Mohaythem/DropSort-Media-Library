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
        foreach (var page in AllPages)
        {
            var xaml = Read("Views", page);
            Assert.Contains(
                "<ScrollViewer x:Name=\"Root\" Style=\"{StaticResource PageScrollViewerStyle}\">",
                xaml,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Layout_is_driven_by_visual_states_not_by_measuring_in_code()
    {
        foreach (var page in AllPages)
        {
            var xaml = Read("Views", page);
            Assert.DoesNotContain("Width=\"{Binding ActualWidth", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain("SizeChanged=\"", xaml, StringComparison.Ordinal);

            var codeBehind = Read("Views", page + ".cs");
            Assert.DoesNotContain("ActualWidth", codeBehind, StringComparison.Ordinal);
            Assert.DoesNotContain("CompactLayoutBreakpoint", codeBehind, StringComparison.Ordinal);
        }

        foreach (var responsive in new[] { "CheckLibraryPage.xaml", "MovieDetailsPage.xaml", "TVShowDetailsPage.xaml" })
        {
            Assert.Contains("<AdaptiveTrigger", Read("Views", responsive), StringComparison.Ordinal);
        }
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

    /// <summary>
    /// The x:Class assertion is a regression guard: the XAML compiler fails - silently, with no
    /// diagnostic - on {x:Bind} inside a ResourceDictionary that has no code-behind.
    /// </summary>
    [Fact]
    public void Shared_cards_are_poster_and_title_only()
    {
        var templates = Read("Resources", "Templates.xaml");

        Assert.Contains("x:Class=\"DropSort.UI.Resources.MediaTemplates\"", templates, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"MovieCardTemplate\"", templates, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"ShowCardTemplate\"", templates, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"ListMovieCardTemplate\"", templates, StringComparison.Ordinal);
        Assert.Contains("Text=\"{x:Bind Title}\"", templates, StringComparison.Ordinal);
        Assert.DoesNotContain("{x:Bind Year}", templates, StringComparison.Ordinal);
        Assert.DoesNotContain("{x:Bind Rating}", templates, StringComparison.Ordinal);
        Assert.DoesNotContain("{x:Bind MetaLine}", templates, StringComparison.Ordinal);
        Assert.DoesNotContain("&#xE735;", templates, StringComparison.Ordinal);
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
        foreach (var page in new[] { "MyListsPage.xaml", "LibraryPage.xaml", "AddMediaPage.xaml", "OperationsLogPage.xaml" })
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

    [Fact]
    public void Movie_details_matches_the_refined_design()
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
        Assert.DoesNotContain("Media Files", xaml, StringComparison.OrdinalIgnoreCase);
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
        var data = Read("Models", "DemoData.cs");
        var details = Read("Views", "TVShowDetailsPage.xaml.cs");
        var detailsXaml = Read("Views", "TVShowDetailsPage.xaml");

        Assert.Contains("EpisodeSelectionRecord", models, StringComparison.Ordinal);
        Assert.Contains("int ShowId", models, StringComparison.Ordinal);
        Assert.Contains("int SeasonNumber", models, StringComparison.Ordinal);
        Assert.Contains("int EpisodeNumber", models, StringComparison.Ordinal);
        Assert.Contains("show.NextPlayableEpisode", data, StringComparison.Ordinal);
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

        Assert.Contains("[\"SeasonWatchedFormat\"] = \"1 season", localization, StringComparison.Ordinal);
        Assert.Contains("[\"SeasonWatchedFormat\"] = \"موسم واحد", localization, StringComparison.Ordinal);
        Assert.Contains("show.Seasons.Count == 1", library, StringComparison.Ordinal);
        Assert.Contains("\"SeasonWatchedFormat\"", library, StringComparison.Ordinal);
        Assert.Contains("\"SeasonsWatchedFormat\"", library, StringComparison.Ordinal);
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
        Assert.Contains("OrganizeConfirmFormat", page, StringComparison.Ordinal);
        Assert.Contains("await Task.Run(() => AppServices.Organization.PrepareOrganization", page, StringComparison.Ordinal);
        Assert.Contains("await Task.Run(() => AppServices.Organization.ConfirmOrganization", page, StringComparison.Ordinal);
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
        Assert.Contains("AttentionMovieCount", page, StringComparison.Ordinal);
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
    /// The review list has no per-row selection control, so the action is named after what it does.
    /// </summary>
    [Fact]
    public void Add_media_names_its_action_after_what_it_registers()
    {
        var page = Read("Views", "AddMediaPage.xaml.cs");
        var localization = Read("Services", "LocalizationService.cs");

        Assert.Contains("AddAllFormat", page, StringComparison.Ordinal);
        Assert.DoesNotContain("LocalizationService.Text(\"AddSelected\")", page, StringComparison.Ordinal);
        Assert.Contains("[\"AddAllFormat\"] = \"Add All ({0})\"", localization, StringComparison.Ordinal);
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
        Assert.Contains("_sectionMovies", lists, StringComparison.Ordinal);
        Assert.Contains("private void Invalidate()", lists, StringComparison.Ordinal);
        Assert.Contains("_missing = MissingFiles();", check, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var missing in MissingFiles())", check, StringComparison.Ordinal);
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

    private static IEnumerable<string> EnumerateUiSource() =>
        Directory.EnumerateFiles(UiRoot, "*.*", SearchOption.AllDirectories)
            .Where(path => (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    private static string Read(params string[] relativeSegments) =>
        File.ReadAllText(relativeSegments.Aggregate(UiRoot, Path.Combine));

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
