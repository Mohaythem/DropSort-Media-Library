using System.Globalization;
using DropSort.Application.Dto;
using DropSort.Domain.Library.Personal;
using DropSort.UI.Models;
using DropSort.UI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace DropSort.UI.Views;

/// <summary>
/// My Lists.
/// <para>
/// The two system lists are Watch Later (the persisted Watchlist membership) and Favorites (the persisted
/// Liked preference). Every further tab is a user-created custom list. A custom list is a real catalog row
/// (custom_lists) with a stable id; its display name is
/// only a label, so a rename keeps the list's movies. Membership (custom_list_members) is keyed on that
/// id, so a movie can belong to any number of lists and appears here right after it is added.
/// </para>
/// </summary>
public sealed partial class MyListsPage : Page, ILocalizableView, IActivatableView
{
    private string _selectedKey = "watch-later";
    private string _query = string.Empty;

    /// <summary>The user's custom lists for this visit. Reloaded on arrival and after every mutation.</summary>
    private IReadOnlyList<CustomList> _customLists = [];

    /// <summary>The members of the selected list for this visit, so search filters in memory.</summary>
    private IReadOnlyList<ListMediaItem>? _sectionItems;
    private string? _sectionKey;

    /// <summary>The custom-list tabs created in code, so a rebuild can remove exactly its own tabs.</summary>
    private readonly List<(RadioButton Tab, TextBlock Label, TextBlock Count, int ListId)> _dynamicTabs = [];

    /// <summary>The system tabs raise Checked during XAML parse; the constructor applies the first list.</summary>
    private readonly bool _isReady;

    /// <summary>Guards so rebuilding tabs and syncing the selection do not re-enter the Checked handler.</summary>
    private bool _rebuilding;
    private bool _syncing;

    public MyListsPage()
    {
        this.InitializeComponent();
        _isReady = true;
        Invalidate();
        ApplyLocalization();
    }

    // APPEND_MARKER
    public event EventHandler<MovieRecord>? MovieSelected;

    public event EventHandler<TVShowRecord>? ShowSelected;

    /// <summary>Every arrival re-reads the custom lists and the selected section from the catalog.</summary>
    public void Activate()
    {
        Invalidate();
        RebuildTabs();
        Refresh();
    }

    /// <summary>Drops the per-visit caches: the custom lists and the loaded section.</summary>
    private void Invalidate()
    {
        _customLists = LoadCustomLists();
        _sectionItems = null;
        _sectionKey = null;
    }

    private static IReadOnlyList<CustomList> LoadCustomLists()
    {
        try { return AppServices.Personal.ListCustomLists(); }
        catch (Exception) { return []; }
    }

    public void ApplyLocalization()
    {
        Invalidate();
        FlowDirection = LocalizationService.FlowDirection;
        PageTitleText.Text = LocalizationService.Text("MyLists");
        NewListButtonText.Text = LocalizationService.Text("NewList");
        AddMoviesButtonText.Text = LocalizationService.Text("AddMedia");
        RenameListButton.Content = LocalizationService.Text("Rename");
        DeleteListButton.Content = LocalizationService.Text("Delete");
        SearchBox.PlaceholderText = LocalizationService.Text("SearchLists");
        ListsCountText.Text = LocalizationService.Format("ListsCountFormat", Definitions.Count);
        LocalizeSystemTabs();
        RebuildTabs();
        Refresh();
    }

    /// <summary>The lists on the strip: the two system sections, then one per user-created custom list.</summary>
    private IReadOnlyList<MediaListDefinition> Definitions
    {
        get
        {
            List<MediaListDefinition> definitions =
            [
                new("watch-later", "WatchLater", true),
                new("favorites", "Favorites", true),
            ];

            foreach (var list in _customLists)
            {
                definitions.Add(new(CustomKey(list.Id), list.Name, false));
            }

            return definitions;
        }
    }

    private static string CustomKey(int id) => "custom:" + id.ToString(CultureInfo.InvariantCulture);

    private static int? ParseCustomKey(string key) =>
        key.StartsWith("custom:", StringComparison.Ordinal) && int.TryParse(key.AsSpan(7), out var id)
            ? id
            : null;

    private (RadioButton Tab, TextBlock Label, TextBlock Count, string Key)[] SystemTabs =>
    [
        (WatchLaterItem, WatchLaterLabel, WatchLaterCount, "watch-later"),
        (FavoritesItem, FavoritesLabel, FavoritesCount, "favorites"),
    ];

    private void LocalizeSystemTabs()
    {
        FavoritesLabel.Text = LocalizationService.Text("Favorites");
        WatchLaterLabel.Text = LocalizationService.Text("WatchLater");
    }

    /// <summary>
    /// Rebuilds the custom-list tabs from <see cref="_customLists" />. Each rebuild removes only the tabs
    /// it added last time and attaches fresh handlers, so no tab or subscription is duplicated.
    /// </summary>
    private void RebuildTabs()
    {
        _rebuilding = true;
        foreach (var (tab, _, _, _) in _dynamicTabs)
        {
            ListSelector.Children.Remove(tab);
        }

        _dynamicTabs.Clear();

        foreach (var list in _customLists)
        {
            var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Text = list.Name };
            var count = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                Style = (Style)App.Current.Resources["SecondaryCaptionTextStyle"],
            };
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            content.Children.Add(new FontIcon
            {
                VerticalAlignment = VerticalAlignment.Center,
                FontFamily = (FontFamily)App.Current.Resources["SymbolThemeFontFamily"],
                FontSize = 14,
                Glyph = "",
            });
            content.Children.Add(label);
            content.Children.Add(count);

            var tab = new RadioButton
            {
                GroupName = "MyListsSelector",
                Style = (Style)App.Current.Resources["TabStripItemStyle"],
                Tag = CustomKey(list.Id),
                Content = content,
            };
            tab.Checked += ListTab_Checked;
            ListSelector.Children.Add(tab);
            _dynamicTabs.Add((tab, label, count, list.Id));
        }

        _rebuilding = false;
    }

    // APPEND_MARKER2
    private void Refresh()
    {
        var definitions = Definitions;
        var definition = definitions.FirstOrDefault(list => list.Key == _selectedKey) ?? definitions[0];
        _selectedKey = definition.Key;
        SyncSelectedTab();

        var isCustom = !definition.IsSystem;
        ListNameText.Text = isCustom ? definition.LabelKey : LocalizationService.Text(definition.LabelKey);
        AddMoviesButton.Visibility = Visibility.Visible;
        RenameListButton.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
        DeleteListButton.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;

        var items = BuildItems(definition);
        ListGrid.ItemsSource = items;
        ListCountText.Text = items.Count == 1
            ? $"{LocalizationService.Number(items.Count)} {LocalizationService.Text("ItemSingular")}"
            : $"{LocalizationService.Number(items.Count)} {LocalizationService.Text("Items")}";

        // The design shows the item count on the active tab only.
        foreach (var (_, _, count, key) in SystemTabs)
        {
            count.Text = key == definition.Key ? LocalizationService.Number(items.Count) : string.Empty;
        }

        foreach (var (_, _, count, id) in _dynamicTabs)
        {
            count.Text = CustomKey(id) == definition.Key ? LocalizationService.Number(items.Count) : string.Empty;
        }

        var isEmpty = items.Count == 0;
        ListGrid.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;
        EmptyState.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;

        if (!isEmpty)
        {
            return;
        }

        var searching = _query.Length > 0;
        EmptyState.Title = LocalizationService.Text(searching ? "NoResults" : "ListEmpty");
        EmptyState.Message = LocalizationService.Text(
            searching ? "NoResultsHelp" : isCustom ? "CustomListEmptyHelp" : "ListEmptyHelp");

        // The empty-state action clears the search while searching, and otherwise opens the picker for
        // every list type.
        if (searching)
        {
            EmptyState.ActionText = LocalizationService.Text("ClearSearch");
            EmptyState.IsActionVisible = true;
        }
        else
        {
            EmptyState.ActionText = LocalizationService.Text("AddMovies");
            EmptyState.IsActionVisible = true;
        }
    }

    /// <summary>Checks the tab that matches the selected key without re-entering the Checked handler.</summary>
    private void SyncSelectedTab()
    {
        _syncing = true;
        foreach (var (tab, _, _, key) in SystemTabs)
        {
            if (key == _selectedKey && tab.IsChecked != true) tab.IsChecked = true;
        }

        foreach (var (tab, _, _, id) in _dynamicTabs)
        {
            if (CustomKey(id) == _selectedKey && tab.IsChecked != true) tab.IsChecked = true;
        }

        _syncing = false;
    }

    private IReadOnlyList<ListMediaItem> BuildItems(MediaListDefinition definition)
    {
        var all = SectionItems(definition);
        if (_query.Length == 0)
        {
            return all;
        }

        return
        [
            .. all.Where(item =>
                item.Title.Contains(_query, StringComparison.CurrentCultureIgnoreCase)
                || item.Year.ToString(CultureInfo.InvariantCulture).Contains(_query, StringComparison.Ordinal)),
        ];
    }

    /// <summary>The members of the selected list, read on first use for this visit and reused afterwards.</summary>
    private IReadOnlyList<ListMediaItem> SectionItems(MediaListDefinition definition)
    {
        if (_sectionKey == definition.Key && _sectionItems is not null)
        {
            return _sectionItems;
        }

        // A read that failed is not cached: the next refresh tries again instead of showing empty.
        if (LoadItems(definition) is not { } items)
        {
            return [];
        }

        _sectionKey = definition.Key;
        _sectionItems = items;
        return items;
    }

    /// <summary>
    /// Reads a list's members. System lists contain movies and TV shows; a custom list holds both kinds
    /// together. Returns null when the catalog could not be read.
    /// </summary>
    private static IReadOnlyList<ListMediaItem>? LoadItems(MediaListDefinition definition)
    {
        try
        {
            if (definition.IsSystem)
            {
                var section = definition.Key switch
                {
                    "favorites" => PersonalLibrarySection.Liked,
                    "watch-later" => PersonalLibrarySection.Watchlist,
                    _ => (PersonalLibrarySection?)null,
                };
                if (section is null) return [];
                return
                [
                    .. AppServices.Personal.ListPersonalMovies(section.Value)
                        .Select(LibraryProjection.ToCard)
                        .Select(movie => new ListMediaItem(
                            movie,
                            true,
                            movie.HasLocalFile,
                            LocalizationService.Text(movie.HasLocalFile ? "Local" : "Unavailable"))),
                    .. AppServices.ShowPersonal.ListShows(section.Value)
                        .Select(TvProjection.ToCard)
                        .Select(show => new ListMediaItem(
                            show,
                            true,
                            show.LocalEpisodeCount > 0,
                            LocalizationService.Text(show.LocalEpisodeCount > 0 ? "Local" : "Unavailable"))),
                ];
            }

            if (ParseCustomKey(definition.Key) is int id)
            {
                return [.. AppServices.Personal.ListCustomListItems(id).Select(ToListItem)];
            }

            return [];
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Projects a mixed custom-list member (movie or TV show) onto a card item.</summary>
    private static ListMediaItem ToListItem(CustomListItem item)
    {
        var badgeText = LocalizationService.Text(!item.IsMissing ? "Local" : "OnlineOnly");
        if (item.Kind == CustomListMediaKind.Show)
        {
            var show = new TVShowRecord(
                item.Id, item.Title, item.Year ?? 0, [], string.Empty, [], PosterReference: item.PosterReference);
            return new ListMediaItem(show, false, !item.IsMissing, badgeText);
        }

        var movie = new MovieRecord(
            item.Id, item.Title, item.Year ?? 0, string.Empty, 0, [], string.Empty,
            HasLocalFile: !item.IsMissing, PosterReference: item.PosterReference);
        return new ListMediaItem(movie, false, !item.IsMissing, badgeText);
    }

    // APPEND_MARKER3
    private int? SelectedListId() => ParseCustomKey(_selectedKey);

    private CustomList? SelectedList() =>
        SelectedListId() is int id ? _customLists.FirstOrDefault(list => list.Id == id) : null;

    private void ListTab_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isReady || _rebuilding || _syncing)
        {
            return;
        }

        if (sender is not RadioButton { Tag: string key } || key == _selectedKey)
        {
            return;
        }

        _selectedKey = key;
        Refresh();
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        _query = sender.Text?.Trim() ?? string.Empty;
        Refresh();
    }

    private void EmptyStateAction_Click(object sender, RoutedEventArgs e)
    {
        if (_query.Length > 0)
        {
            SearchBox.Text = string.Empty;
            _query = string.Empty;
            Refresh();
            return;
        }

        // Every list accepts explicit movie/show membership through the same picker.
        _ = ShowAddMediaAsync();
    }

    private void ListGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not ListMediaItem item)
        {
            return;
        }

        if (item.Show is { } show)
        {
            ShowSelected?.Invoke(this, show);
        }
        else if (item.Movie is { } movie)
        {
            MovieSelected?.Invoke(this, movie);
        }
    }

    /// <summary>
    /// A right-tap (or press-and-hold) offers removal from the selected list. The item is resolved from
    /// the tapped element's data context, so the menu targets the exact movie or show.
    /// </summary>
    private void ListGrid_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is not ListMediaItem item)
        {
            return;
        }

        var remove = new MenuFlyoutItem
        {
            Text = LocalizationService.Text("RemoveFromList"),
            FlowDirection = LocalizationService.FlowDirection,
        };
        var destinationKey = _selectedKey;
        remove.Click += (_, _) => RemoveFromSelectedList(item, destinationKey);
        var menu = new MenuFlyout();
        menu.Items.Add(remove);
        menu.ShowAt(ListGrid, new FlyoutShowOptions { Position = e.GetPosition(ListGrid) });
    }

    private void RemoveFromSelectedList(ListMediaItem item, string destinationKey)
    {
        try
        {
            if (ParseCustomKey(destinationKey) is int listId)
            {
                if (item.Show is { } show)
                {
                    AppServices.Personal.RemoveShowFromCustomList(listId, show.Id);
                }
                else if (item.Movie is { } movie)
                {
                    AppServices.Personal.RemoveMovieFromCustomList(listId, movie.Id);
                }
            }
            else if (destinationKey == "watch-later")
            {
                if (item.Show is { } show) AppServices.ShowPersonal.SetWatchLater(show.Id, false);
                else if (item.Movie is { } movie) AppServices.Personal.RemoveFromWatchlist(movie.Id);
            }
            else if (destinationKey == "favorites")
            {
                if (item.Show is { } show) AppServices.ShowPersonal.SetFavorite(show.Id, false);
                else if (item.Movie is { } movie) AppServices.Personal.ClearPersonalPreference(movie.Id);
            }
        }
        catch (Exception error)
        {
            _ = ReportAsync(error);
            return;
        }

        // The current list's contents changed; reload it and redraw. Other lists and the media are untouched.
        Invalidate();
        Refresh();
    }

    // APPEND_MARKER4
    private void NewListButton_Click(object sender, RoutedEventArgs e) => _ = ShowCreateListDialogAsync();

    private void RenameListButton_Click(object sender, RoutedEventArgs e) => _ = ShowRenameListDialogAsync();

    private void DeleteListButton_Click(object sender, RoutedEventArgs e) => _ = ShowDeleteListDialogAsync();

    private void AddMoviesButton_Click(object sender, RoutedEventArgs e) => _ = ShowAddMediaAsync();

    /// <summary>Creates a new custom list and selects it. It never replaces an existing list.</summary>
    private async Task ShowCreateListDialogAsync()
    {
        var name = await PromptListNameAsync(LocalizationService.Text("NewList"), string.Empty);
        if (name is null)
        {
            return;
        }

        try
        {
            var created = AppServices.Personal.CreateCustomList(name);
            _selectedKey = CustomKey(created.Id);
        }
        catch (DropSort.Application.DuplicateCustomListNameException)
        {
            await ShowMessageAsync(LocalizationService.Text("NewList"), LocalizationService.Text("DuplicateListName"));
            return;
        }
        catch (Exception error)
        {
            await ReportAsync(error);
            return;
        }

        Invalidate();
        RebuildTabs();
        Refresh();
    }

    /// <summary>Renames the selected custom list. Its id and its members are unchanged.</summary>
    private async Task ShowRenameListDialogAsync()
    {
        if (SelectedList() is not { } list)
        {
            return;
        }

        var name = await PromptListNameAsync(LocalizationService.Text("RenameList"), list.Name);
        if (name is null)
        {
            return;
        }

        try
        {
            AppServices.Personal.RenameCustomList(list.Id, name);
        }
        catch (DropSort.Application.DuplicateCustomListNameException)
        {
            await ShowMessageAsync(LocalizationService.Text("RenameList"), LocalizationService.Text("DuplicateListName"));
            return;
        }
        catch (Exception error)
        {
            await ReportAsync(error);
            return;
        }

        // The selection key is the id, so it survives the rename.
        Invalidate();
        RebuildTabs();
        Refresh();
    }

    /// <summary>Deletes the selected custom list and its membership rows. No movie or file is touched.</summary>
    private async Task ShowDeleteListDialogAsync()
    {
        if (SelectedList() is not { } list)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            FlowDirection = LocalizationService.FlowDirection,
            RequestedTheme = ThemeService.ElementTheme,
            Title = LocalizationService.Text("DeleteList"),
            Content = list.Name,
            PrimaryButtonText = LocalizationService.Text("Delete"),
            CloseButtonText = LocalizationService.Text("Cancel"),
            DefaultButton = ContentDialogButton.Close,
        };

        if (await ContentDialogCoordinator.ShowAsync(dialog) != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            AppServices.Personal.DeleteCustomList(list.Id);
        }
        catch (Exception error)
        {
            await ReportAsync(error);
            return;
        }

        _selectedKey = "watch-later";
        Invalidate();
        RebuildTabs();
        Refresh();
    }

    // APPEND_MARKER5
    /// <summary>
    /// Adds existing library items - movies and TV shows - to the selected list. The picker lists
    /// items already registered in DropSort; it never imports files, scans folders or fetches metadata.
    /// Items already in the list are shown disabled.
    /// </summary>
    private async Task ShowAddMediaAsync()
    {
        var destinationKey = _selectedKey;
        var destinationListId = ParseCustomKey(destinationKey);
        var definition = Definitions.FirstOrDefault(item => item.Key == destinationKey);
        if (definition is null) return;
        List<MediaPickerEntry> entries;
        try
        {
            var members = LoadItems(definition)
                ?? throw new InvalidOperationException(LocalizationService.Text("Failed"));
            var movieMembers = members.Where(item => item.Movie is not null).Select(item => item.Movie!.Id).ToHashSet();
            var showMembers = members.Where(item => item.Show is not null).Select(item => item.Show!.Id).ToHashSet();

            entries =
            [
                .. AppServices.Library.ListMovies().Select(movie => new MediaPickerEntry(
                    CustomListMediaKind.Movie, movie.Id, movie.Title, movie.Year, movieMembers.Contains(movie.Id))),
                .. AppServices.Tv.ListShows().Select(show => new MediaPickerEntry(
                    CustomListMediaKind.Show, show.Id, show.Title, show.Year, showMembers.Contains(show.Id))),
            ];
        }
        catch (Exception error)
        {
            await ReportAsync(error);
            return;
        }

        var picks = await AddMediaToListDialog.ShowAsync(XamlRoot, entries);
        if (picks.Count == 0)
        {
            return;
        }

        try
        {
            foreach (var pick in picks)
            {
                if (destinationListId is int listId)
                {
                    if (pick.Kind == CustomListMediaKind.Show) AppServices.Personal.AddShowToCustomList(listId, pick.Id);
                    else AppServices.Personal.AddMovieToCustomList(listId, pick.Id);
                }
                else if (destinationKey == "watch-later")
                {
                    if (pick.Kind == CustomListMediaKind.Show) AppServices.ShowPersonal.SetWatchLater(pick.Id, true);
                    else AppServices.Personal.AddToWatchlist(pick.Id);
                }
                else if (destinationKey == "favorites")
                {
                    if (pick.Kind == CustomListMediaKind.Show) AppServices.ShowPersonal.SetFavorite(pick.Id, true);
                    else AppServices.Personal.SetPersonalPreference(pick.Id, PersonalPreference.Liked);
                }
            }
        }
        catch (Exception error)
        {
            await ReportAsync(error);
        }

        Invalidate();
        RebuildTabs();
        Refresh();
    }

    /// <summary>
    /// Prompts for a list name and returns the trimmed value, or null when cancelled or left empty. An
    /// empty name is rejected rather than silently creating an unnamed list.
    /// </summary>
    private async Task<string?> PromptListNameAsync(string title, string value)
    {
        var input = new TextBox
        {
            Text = value,
            PlaceholderText = LocalizationService.Text("ListNamePlaceholder"),
            Style = (Style)App.Current.Resources["FormInputTextBoxStyle"],
        };

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            FlowDirection = LocalizationService.FlowDirection,
            RequestedTheme = ThemeService.ElementTheme,
            Title = title,
            Content = input,
            PrimaryButtonText = LocalizationService.Text("Save"),
            CloseButtonText = LocalizationService.Text("Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await ContentDialogCoordinator.ShowAsync(dialog) != ContentDialogResult.Primary)
        {
            return null;
        }

        var name = input.Text?.Trim();
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    private Task ReportAsync(Exception error) =>
        ShowMessageAsync(LocalizationService.Text("Failed"), MetadataErrorText.For(error));

    private async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.ElementTheme,
            FlowDirection = LocalizationService.FlowDirection,
            Title = title,
            Content = message,
            CloseButtonText = LocalizationService.Text("Close"),
        };

        await ContentDialogCoordinator.ShowAsync(dialog);
    }
}
