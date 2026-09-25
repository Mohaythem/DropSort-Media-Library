using Xunit;

namespace DropSort.Tests.UI;

/// <summary>
/// UX corrective pass contracts. The UI must present current backend truth: a file whose storage is
/// offline is shown as "storage unavailable" (not affirmatively available), and Check Library reports
/// unavailable storage instead of a false-clean result. These are source-contract checks because the
/// behavior is UI-thread runtime code the backend test suite cannot execute.
/// </summary>
public sealed class UxCorrectivePassContractTests
{
    private static readonly string UiRoot = FindUiRoot();

    [Fact]
    public void Movie_details_probes_storage_and_shows_unavailable_instead_of_available()
    {
        var page = File.ReadAllText(Path.Combine(UiRoot, "Views", "MovieDetailsPage.xaml.cs"));
        Assert.Contains("AppServices.IsStorageAvailable", page);
        Assert.Contains("StorageUnavailable", page);
    }

    [Fact]
    public void Storage_availability_probe_is_exposed_off_the_ui_thread()
    {
        var services = File.ReadAllText(Path.Combine(UiRoot, "Services", "AppServices.cs"));
        Assert.Contains("public static bool IsStorageAvailable(string path)", services);
        Assert.Contains("UnavailableStoragePaths", services); // shared batched primitive for list surfaces
    }

    [Fact]
    public void Tv_details_probes_episode_storage_and_gates_file_actions()
    {
        var page = File.ReadAllText(Path.Combine(UiRoot, "Views", "TVShowDetailsPage.xaml.cs"));
        Assert.Contains("AppServices.UnavailableStoragePaths", page);
        Assert.Contains("ProbeEpisodeStorageAvailability", page);

        var xaml = File.ReadAllText(Path.Combine(UiRoot, "Views", "TVShowDetailsPage.xaml"));
        Assert.Contains("CanOpenFile", xaml);                 // Play/Open gated by storage availability
        Assert.Contains("Labels.StorageUnavailable", xaml);   // storage-unavailable pill
    }

    [Fact]
    public void Check_library_reports_unavailable_storage_honestly()
    {
        var page = File.ReadAllText(Path.Combine(UiRoot, "Views", "CheckLibraryPage.xaml.cs"));
        Assert.Contains("StorageUnavailableFiles", page);
        Assert.Contains("FilesStorageUnavailableFormat", page);
    }

    [Fact]
    public void New_visible_states_have_localization_keys_in_both_languages()
    {
        var localization = File.ReadAllText(Path.Combine(UiRoot, "Services", "LocalizationService.cs"));
        // English and Arabic tables each define the key, so it must appear twice.
        Assert.Equal(2, CountOccurrences(localization, "[\"StorageUnavailable\"]"));
        Assert.Equal(2, CountOccurrences(localization, "[\"FilesStorageUnavailableFormat\"]"));
    }

    [Fact]
    public void Multi_list_and_sort_states_have_localization_keys_in_both_languages()
    {
        var localization = File.ReadAllText(Path.Combine(UiRoot, "Services", "LocalizationService.cs"));
        foreach (var key in new[]
                 {
                     "SortAscending", "SortDescending", "AddMedia", "SelectMedia", "RemoveFromList",
                     "AlreadyInList", "DuplicateListName", "NoCustomLists",
                 })
        {
            // English and Arabic tables each define the key, so it must appear twice.
            Assert.Equal(2, CountOccurrences(localization, $"[\"{key}\"]"));
        }
    }

    [Fact]
    public void Library_sort_is_one_integrated_split_control_with_accurate_filter_terminology()
    {
        var library = File.ReadAllText(Path.Combine(UiRoot, "Views", "LibraryPage.xaml"));
        Assert.Contains("<SplitButton", library);             // sort reads as one integrated control
        Assert.DoesNotContain("SortDirectionButton", library); // no separate isolated direction button
        Assert.DoesNotContain("Glyph=\"&#xE8CB;\"", library);

        var loc = File.ReadAllText(Path.Combine(UiRoot, "Services", "LocalizationService.cs"));
        Assert.DoesNotContain("Online only", loc);
        Assert.Contains("\"No local file\"", loc);
        Assert.Contains("\"Date added\"", loc);
    }

    [Fact]
    public void Library_sort_direction_toggles_within_the_split_control()
    {
        var code = File.ReadAllText(Path.Combine(UiRoot, "Views", "LibraryPage.xaml.cs"));
        Assert.Contains("_sortDescending", code);
        Assert.Contains("SortSplit_Click", code);              // the split face reverses direction
        Assert.DoesNotContain("SortDirectionButton", code);    // the standalone square button is gone
    }

    [Fact]
    public void Movie_details_add_to_list_contains_custom_lists_only()
    {
        var xaml = File.ReadAllText(Path.Combine(UiRoot, "Views", "MovieDetailsPage.xaml"));
        Assert.Contains("AddToListButton", xaml);
        Assert.Contains("AddToListFlyout", xaml);

        var code = File.ReadAllText(Path.Combine(UiRoot, "Views", "MovieDetailsPage.xaml.cs"));
        Assert.Contains("ListCustomLists", code);                 // enumerates every custom list
        Assert.Contains("GetCustomListIdsForMovie", code);        // by stable id, not a key
        Assert.Contains("AddMovieToCustomList", code);
        Assert.Contains("RemoveMovieFromCustomList", code);
        // Watchlist and Favorites are no longer duplicated inside the Add to list menu.
        Assert.DoesNotContain("ToggleFavoriteFromMenu", code);
    }

    [Fact]
    public void Tv_details_has_the_custom_list_add_action()
    {
        var xaml = File.ReadAllText(Path.Combine(UiRoot, "Views", "TVShowDetailsPage.xaml"));
        Assert.Contains("AddToListFlyout", xaml);
        Assert.Contains("Opening=\"AddToListFlyout_Opening\"", xaml);

        var code = File.ReadAllText(Path.Combine(UiRoot, "Views", "TVShowDetailsPage.xaml.cs"));
        Assert.Contains("ListCustomLists", code);
        Assert.Contains("GetCustomListIdsForShow", code);
        Assert.Contains("AddShowToCustomList", code);
        Assert.Contains("RemoveShowFromCustomList", code);
    }

    [Fact]
    public void My_lists_is_multi_list_with_no_fixed_single_slot()
    {
        var code = File.ReadAllText(Path.Combine(UiRoot, "Views", "MyListsPage.xaml.cs"));
        Assert.Contains("ListCustomLists", code);                 // enumerates all custom lists
        Assert.Contains("CreateCustomList", code);                // New List creates, does not replace
        Assert.Contains("RenameCustomList", code);
        Assert.Contains("DeleteCustomList", code);
        Assert.DoesNotContain("thrillers", code);                 // the fixed single slot is gone
        Assert.DoesNotContain("CustomListKey", code);

        var xaml = File.ReadAllText(Path.Combine(UiRoot, "Views", "MyListsPage.xaml"));
        Assert.DoesNotContain("ThrillersItem", xaml);             // no hard-coded custom-list tab

        var store = File.ReadAllText(Path.Combine(UiRoot, "Services", "AppSettingsStore.cs"));
        Assert.DoesNotContain("CustomListKey", store);            // list identity left the settings slot
    }

    [Fact]
    public void My_lists_supports_mixed_movie_and_tv_membership()
    {
        var code = File.ReadAllText(Path.Combine(UiRoot, "Views", "MyListsPage.xaml.cs"));
        Assert.Contains("ListCustomListItems", code);             // reads movies and shows together
        Assert.Contains("AddShowToCustomList", code);
        Assert.Contains("RemoveShowFromCustomList", code);
        Assert.Contains("ShowSelected", code);                    // a show card navigates to TV details
    }

    [Fact]
    public void Custom_list_exposes_add_media_and_remove_from_list()
    {
        var code = File.ReadAllText(Path.Combine(UiRoot, "Views", "MyListsPage.xaml.cs"));
        Assert.Contains("ShowAddMediaAsync", code);               // Add media action (movies + shows)
        Assert.Contains("AddMediaToListDialog", code);            // library picker
        Assert.Contains("RemoveFromList", code);                  // remove-from-list path
        Assert.Contains("RemoveMovieFromCustomList", code);
        Assert.Contains("RemoveShowFromCustomList", code);

        var picker = File.ReadAllText(Path.Combine(UiRoot, "Views", "AddMediaToListDialog.cs"));
        Assert.Contains("ListViewSelectionMode.Multiple", picker);    // multi-select picker
        Assert.Contains("CustomListMediaKind", picker);               // distinguishes movie vs show
    }

    [Fact]
    public void Sidebar_navigation_uses_item_invoked_so_home_works_from_a_detail_page()
    {
        // Root cause of the "Home does nothing from a detail page" bug: a detail page keeps its origin
        // nav item selected, and SelectionChanged does not fire when the already-selected item is clicked.
        // ItemInvoked fires on every click, so Home always navigates.
        var xaml = File.ReadAllText(Path.Combine(UiRoot, "MainWindow.xaml"));
        Assert.Contains("ItemInvoked=\"ShellNavigation_ItemInvoked\"", xaml);
        Assert.DoesNotContain("SelectionChanged=\"ShellNavigation_SelectionChanged\"", xaml);

        var code = File.ReadAllText(Path.Combine(UiRoot, "MainWindow.xaml.cs"));
        Assert.Contains("ShellNavigation_ItemInvoked", code);
        Assert.Contains("InvokedItemContainer", code);
    }

    [Fact]
    public void Escape_is_a_shell_back_accelerator_sharing_the_back_path_without_closing_the_app()
    {
        var xaml = File.ReadAllText(Path.Combine(UiRoot, "MainWindow.xaml"));
        Assert.Contains("<KeyboardAccelerator Key=\"Escape\"", xaml);
        Assert.Contains("Invoked=\"EscAccelerator_Invoked\"", xaml);

        var code = File.ReadAllText(Path.Combine(UiRoot, "MainWindow.xaml.cs"));
        Assert.Contains("private bool GoBack()", code);
        // Esc runs the same navigation as the visible Back action (the detail return destination),
        Assert.Contains("Navigate(_detailsReturnDestination)", code);
        // and leaves the key unhandled on a root page (GoBack returns false), so the window never closes.
        Assert.Contains("args.Handled = GoBack();", code);
    }

    private static int CountOccurrences(string text, string token)
    {
        var count = 0;
        for (var index = text.IndexOf(token, StringComparison.Ordinal); index >= 0;
             index = text.IndexOf(token, index + token.Length, StringComparison.Ordinal))
        {
            count++;
        }
        return count;
    }

    private static string FindUiRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DropSort.slnx")))
            directory = directory.Parent;
        return Path.Combine(directory?.FullName ?? throw new InvalidOperationException("Solution root not found"), "src", "DropSort.UI");
    }
}
