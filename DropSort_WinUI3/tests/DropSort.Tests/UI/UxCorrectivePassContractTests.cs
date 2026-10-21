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
        Assert.Contains("AppServices.UnavailableStoragePaths", page);
        Assert.Contains("file.AvailabilityKey", page);
        Assert.Contains("StorageUnavailable", page);
    }

    [Fact]
    public void Storage_availability_probe_is_exposed_off_the_ui_thread()
    {
        var services = File.ReadAllText(Path.Combine(UiRoot, "Services", "AppServices.cs"));
        Assert.Contains("public static bool IsStorageAvailable(string path, CancellationToken token = default)", services);
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
    public void Check_library_review_action_opens_the_movie_for_metadata_review()
    {
        // The metadata-issue row's Review button was a dead control: it carried a null media-file id and
        // the handler ignored it. It now carries the movie id and asks the shell to open that movie's
        // details, which is the only place a movie's metadata is reviewed and rematched.
        var page = File.ReadAllText(Path.Combine(UiRoot, "Views", "CheckLibraryPage.xaml.cs"));
        Assert.Contains("MovieReviewRequested", page);              // the page raises a review request
        Assert.Contains("MovieId: item.MovieId", page);            // metadata rows carry the movie id
        Assert.Contains("record.MovieId is int movieId", page);    // the action routes on the movie id

        var template = File.ReadAllText(Path.Combine(UiRoot, "Views", "CheckLibraryPage.xaml"));
        Assert.Contains("Tag=\"{x:Bind}\"", template);             // the action button carries its record

        var shell = File.ReadAllText(Path.Combine(UiRoot, "MainWindow.xaml.cs"));
        Assert.Contains("MovieReviewRequested += OpenMovieForReview", shell); // the shell handles it
        Assert.Contains("_movieDetailsPage.LoadMovie(movieId)", shell);       // by loading that movie
        Assert.Contains("SetReturnDestination(\"check\")", shell);            // Back returns to Check Library
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
    public void Select_ready_to_add_selects_only_rows_that_need_no_review()
    {
        // "Select ready to add" is the safe bulk action: it picks only rows in the clean initial state
        // or a confident match, leaving needs-review / no-match / failed / already-in-library rows for
        // the user to resolve by hand.
        var model = File.ReadAllText(Path.Combine(UiRoot, "Models", "AddMediaModels.cs"));
        Assert.Contains("public bool IsReadyToAdd", model);
        Assert.Contains("ImportItemStatus.ReadyToAdd or ImportItemStatus.Matched", model);

        var page = File.ReadAllText(Path.Combine(UiRoot, "Views", "AddMediaPage.xaml.cs"));
        Assert.Contains("SelectReadyButton_Click", page);
        Assert.Contains("row.IsReadyToAdd", page);

        var xaml = File.ReadAllText(Path.Combine(UiRoot, "Views", "AddMediaPage.xaml"));
        Assert.Contains("x:Name=\"SelectReadyButton\"", xaml);

        var loc = File.ReadAllText(Path.Combine(UiRoot, "Services", "LocalizationService.cs"));
        Assert.Equal(2, CountOccurrences(loc, "[\"SelectReady\"]")); // English + Arabic
    }

    [Fact]
    public void Adding_media_removes_only_the_registered_rows_from_the_queue()
    {
        // The active queue must answer "what still needs my attention?": successfully registered rows
        // leave it, while failed / unresolved / unselected rows stay visible instead of the whole list
        // being wiped and the page snapping back to idle.
        var page = File.ReadAllText(Path.Combine(UiRoot, "Views", "AddMediaPage.xaml.cs"));
        Assert.Contains("private void RemoveRows(ISet<string> paths)", page);
        Assert.Contains("if (item.Registered)", page);
        Assert.Contains("RemoveRows(addedPaths)", page);
        Assert.Contains("if (_rows.Count == 0)", page);   // idle only once nothing is left
    }

    [Fact]
    public void Review_match_dialog_has_manual_search_with_auto_candidates_and_result_focus()
    {
        // Match with TMDB keeps automatic candidates on open AND offers manual Title/Year + Search so the
        // user can look up a title when the automatic lookup finds nothing. Focus lands on the results
        // after each search, so the Search field never steals the caret after a result is available.
        var dialog = File.ReadAllText(Path.Combine(UiRoot, "Views", "MatchMediaDialog.cs"));
        Assert.Contains("_titleBox", dialog);             // editable title
        Assert.Contains("_yearBox", dialog);              // editable year
        Assert.Contains("_searchButton", dialog);         // manual Search button
        Assert.Contains("OnInputKeyDown", dialog);        // Enter triggers search
        Assert.Contains("Opened +=", dialog);             // automatic candidates on open
        Assert.Contains("await PerformSearchAsync()", dialog);
        Assert.Contains("_candidateList.Focus(", dialog); // focus lands on the results, not the search box
    }

    [Fact]
    public void Add_media_page_has_no_standalone_tmdb_search_block()
    {
        // The redundant standalone "Search TMDB" card is gone; manual search now lives in Match with TMDB.
        var xaml = File.ReadAllText(Path.Combine(UiRoot, "Views", "AddMediaPage.xaml"));
        Assert.DoesNotContain("SearchTmdbCard", xaml);
        Assert.DoesNotContain("TmdbSearchBox", xaml);
        Assert.DoesNotContain("TmdbSearchButton", xaml);

        var page = File.ReadAllText(Path.Combine(UiRoot, "Views", "AddMediaPage.xaml.cs"));
        Assert.DoesNotContain("TmdbSearchButton_Click", page);
    }

    [Fact]
    public void Import_matching_shows_progress_and_locks_actions_until_ready()
    {
        // While the metadata review pass runs, the status line shows real X/Total progress and every
        // action whose meaning depends on the final matched set is disabled until Ready.
        var page = File.ReadAllText(Path.Combine(UiRoot, "Views", "AddMediaPage.xaml.cs"));
        Assert.Contains("ImportReviewPhaseFormat", page);
        Assert.Contains("_matchProcessed", page);
        Assert.Contains("_matchTotal", page);
        Assert.Contains("var busy = _isAdding || matching;", page);
        // Select/Deselect/Select-ready/Add are all gated on !busy (busy includes matching).
        Assert.Contains("SelectAllButton.IsEnabled = selectable > 0 && !busy;", page);
        Assert.Contains("SelectReadyButton.IsEnabled = ready > 0 && !busy;", page);
        Assert.Contains("DeselectAllButton.IsEnabled = selected > 0 && !busy;", page);
        Assert.Contains("AddSelectedButton.IsEnabled = selected > 0 && !busy;", page);
    }

    [Fact]
    public void Bulk_add_reports_real_processed_of_total_progress()
    {
        // The add operation reports real registration progress; the UI shows "Adding to library… X / Total".
        var service = File.ReadAllText(Path.Combine(FindAppRoot(), "Services", "ImportService.cs"));
        Assert.Contains("Action<ImportProgress>? progress", service);
        Assert.Contains("new ImportProgress(", service);

        var page = File.ReadAllText(Path.Combine(UiRoot, "Views", "AddMediaPage.xaml.cs"));
        Assert.Contains("AddingProgressFormat", page);
        Assert.Contains("progress: update =>", page);   // real callback, marshalled to the UI thread
        Assert.Contains("_addProcessed = update.Processed;", page);
    }

    [Fact]
    public void Import_status_line_names_each_batch_phase_in_both_languages()
    {
        // Scanning → Matching → Ready → Adding → Done is communicated through the phase-aware status line.
        var page = File.ReadAllText(Path.Combine(UiRoot, "Views", "AddMediaPage.xaml.cs"));
        Assert.Contains("CurrentPhaseStatus", page);
        foreach (var key in new[] { "ImportReviewPhaseFormat", "AddingProgressFormat", "ReadyPhaseFormat", "ImportDoneFormat" })
        {
            Assert.Contains(key, page);
        }

        var loc = File.ReadAllText(Path.Combine(UiRoot, "Services", "LocalizationService.cs"));
        foreach (var key in new[] { "ImportReviewPhaseFormat", "AddingProgressFormat", "ReadyPhaseFormat", "ImportDoneFormat" })
        {
            Assert.Equal(2, CountOccurrences(loc, $"[\"{key}\"]")); // English + Arabic
        }
    }

    [Fact]
    public void Import_headers_sort_visually_without_touching_selection_or_match_state()
    {
        var xaml = File.ReadAllText(Path.Combine(UiRoot, "Views", "AddMediaPage.xaml"));
        Assert.Contains("x:Name=\"SortTitleButton\"", xaml);
        Assert.Contains("x:Name=\"SortTypeButton\"", xaml);
        Assert.Contains("x:Name=\"SortStatusButton\"", xaml);
        Assert.DoesNotContain("SortSelectButton", xaml);   // Select column is not sortable
        Assert.DoesNotContain("SortActionButton", xaml);   // Action column is not sortable

        var page = File.ReadAllText(Path.Combine(UiRoot, "Views", "AddMediaPage.xaml.cs"));
        Assert.Contains("private void ApplySort()", page);
        Assert.Contains("_rows.Move(current, targetIndex)", page); // reorders instances in place
    }

    [Fact]
    public void Sortable_headers_read_as_table_header_not_filled_buttons()
    {
        // The header buttons must blend into the header row: transparent background (no filled block),
        // only a subtle heavier stroke on hover/active, and the sort arrow at the far right of the cell.
        var xaml = File.ReadAllText(Path.Combine(UiRoot, "Views", "AddMediaPage.xaml"));
        Assert.Contains("TableHeaderSortButtonStyle", xaml);
        Assert.Contains("<Setter Property=\"Background\" Value=\"Transparent\" />", xaml);
        Assert.Contains("ControlStrongStrokeColorDefaultBrush", xaml);   // border on hover/active, not a fill
        // The arrow lives in its own right-hand column, not directly beside the word.
        Assert.Contains("x:Name=\"SortTitleIndicator\" Grid.Column=\"1\"", xaml);
        Assert.Contains("x:Name=\"SortStatusIndicator\" Grid.Column=\"1\"", xaml);
    }

    [Fact]
    public void Movie_cards_show_an_inset_titled_fallback_when_the_poster_is_missing()
    {
        // A movie with no poster/metadata shows its title in an inset bordered panel inside the poster
        // area (kept off the card edges by a margin), using theme brushes — not an empty/broken card.
        var templates = File.ReadAllText(Path.Combine(UiRoot, "Resources", "Templates.xaml"));
        Assert.Contains("Margin=\"12\"", templates);                 // inset: border does not touch edges
        Assert.Contains("TextAlignment=\"Center\"", templates);      // centered, readable title
        Assert.Contains("Visibility=\"{x:Bind IsPosterMissing, Mode=OneWay}\"", templates);
        Assert.Contains("LayerFillColorAltBrush", templates);        // theme brush (auto light/dark)
    }

    [Fact]
    public void Movie_card_shows_a_subtle_badge_when_a_movie_has_multiple_files()
    {
        // A movie backed by more than one local file gets a small "N files" badge on the card; the full
        // per-file detail stays in Movie Details.
        var model = File.ReadAllText(Path.Combine(UiRoot, "Models", "DemoModels.cs"));
        Assert.Contains("int FileCount = 1", model);
        Assert.Contains("MultiFileBadgeVisibility", model);
        Assert.Contains("FileCount > 1", model);

        var templates = File.ReadAllText(Path.Combine(UiRoot, "Resources", "Templates.xaml"));
        Assert.Contains("MultiFileBadgeVisibility", templates);
        Assert.Contains("MultiFileBadgeText", templates);

        var loc = File.ReadAllText(Path.Combine(UiRoot, "Services", "LocalizationService.cs"));
        Assert.Equal(2, CountOccurrences(loc, "[\"FilesCountFormat\"]")); // English + Arabic
    }

    [Fact]
    public void Movie_details_lists_every_associated_media_file()
    {
        // With one Movie owning multiple files, Movie Details lists all of them (filename, path,
        // availability), not just a single primary file.
        var xaml = File.ReadAllText(Path.Combine(UiRoot, "Views", "MovieDetailsPage.xaml"));
        Assert.Contains("x:Name=\"MediaFilesRepeater\"", xaml);
        Assert.Contains("x:DataType=\"models:MovieFileRecord\"", xaml);

        var projection = File.ReadAllText(Path.Combine(UiRoot, "Services", "LibraryProjection.cs"));
        Assert.Contains("MediaFiles: mediaFiles", projection);       // all files projected, not just one
    }

    [Fact]
    public void Review_match_is_persisted_state_with_a_card_badge()
    {
        // Review Match is real persisted state (a movies column), surfaced as a distinct card badge — not
        // a temporary UI-only flag and not the same as "missing metadata".
        var migrator = File.ReadAllText(Path.Combine(UiRoot, "../DropSort.Infrastructure/Persistence/Migrations/DatabaseMigrator.cs"));
        Assert.Contains("review_match", migrator);                    // persisted column via migration
        Assert.Contains("ALTER TABLE movies ADD COLUMN review_match", migrator);

        var movie = File.ReadAllText(Path.Combine(UiRoot, "../DropSort.Domain/Library/Movies/Models.cs"));
        Assert.Contains("public bool ReviewMatch", movie);

        var model = File.ReadAllText(Path.Combine(UiRoot, "Models", "DemoModels.cs"));
        Assert.Contains("ReviewMatchBadgeVisibility", model);
        Assert.Contains("MetadataGroupRank", model);

        var templates = File.ReadAllText(Path.Combine(UiRoot, "Resources", "Templates.xaml"));
        Assert.Contains("ReviewMatchBadgeVisibility", templates);
    }

    [Fact]
    public void Library_orders_metadata_first_and_offers_a_reversible_metadata_status_sort()
    {
        // Default order groups by metadata (confirmed → review-match → none) with the chosen criterion
        // secondary, and a dedicated reversible "Metadata Status" sort exists.
        var code = File.ReadAllText(Path.Combine(UiRoot, "Views", "LibraryPage.xaml.cs"));
        Assert.Contains("OrderBy(movie => movie.MetadataGroupRank)", code);          // metadata-first grouping
        Assert.Contains("_sort == \"metadata\"", code);                              // explicit metadata sort
        Assert.Contains("OrderByDescending(movie => movie.MetadataGroupRank)", code); // reversible

        var xaml = File.ReadAllText(Path.Combine(UiRoot, "Views", "LibraryPage.xaml"));
        Assert.Contains("Tag=\"metadata\"", xaml);
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
        Assert.Contains("RemoveFromList", File.ReadAllText(Path.Combine(UiRoot, "Services", "MediaCardContextMenu.cs")));                  // remove-from-list path
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

    [Fact]
    public void Library_and_my_lists_grids_own_a_working_vertical_scroll_and_stretch_full_width()
    {
        foreach (var page in new[] { "LibraryPage.xaml", "MyListsPage.xaml" })
        {
            var xaml = File.ReadAllText(Path.Combine(UiRoot, "Views", page));
            // The grid owns scrolling (fixes the clipped 10K library) while staying virtualized...
            Assert.Contains("ScrollViewer.VerticalScrollMode=\"Enabled\"", xaml);
            // ...and is not wrapped in the page ScrollViewer that would defeat virtualization.
            Assert.DoesNotContain("x:Name=\"Root\" Style=\"{StaticResource PageScrollViewerStyle}\"", xaml);
            Assert.Contains("HorizontalAlignment=\"Stretch\"", xaml);   // uses the full page width
            Assert.DoesNotContain("MaximumRowsOrColumns", xaml);        // no column cap
            Assert.DoesNotContain("MaxWidth=\"", xaml);                 // no fixed content width cap
        }
    }

    [Fact]
    public void Escape_accelerator_is_silent_no_visible_badge()
    {
        var xaml = File.ReadAllText(Path.Combine(UiRoot, "MainWindow.xaml"));
        Assert.Contains("KeyboardAcceleratorPlacementMode=\"Hidden\"", xaml); // no floating "Esc" label
        Assert.Contains("<KeyboardAccelerator Key=\"Escape\"", xaml);         // shortcut itself stays
    }

    [Fact]
    public void Media_card_templates_are_single_focus_units()
    {
        var templates = File.ReadAllText(Path.Combine(UiRoot, "Resources", "Templates.xaml"));
        // Card internals are decorative (Border/Image/FontIcon/TextBlock) - no nested interactive controls
        // or forced tab stops - so each GridView item is one keyboard-selectable unit.
        Assert.DoesNotContain("<Button", templates);
        Assert.DoesNotContain("IsTabStop=\"True\"", templates);
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

    private static string FindAppRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DropSort.slnx")))
            directory = directory.Parent;
        return Path.Combine(directory?.FullName ?? throw new InvalidOperationException("Solution root not found"), "src", "DropSort.Application");
    }
}
