using System.Globalization;
using DropSort.Domain.Library.Personal;
using DropSort.UI.Models;
using DropSort.UI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DropSort.UI.Views;

/// <summary>
/// My Lists.
/// <para>
/// The three system lists are the real personal-library sections: Watchlist, Favorites is Liked, and
/// Watch Later is Ready to Watch (watchlisted with a file on disk). The fourth tab is the user's own
/// list - its name is created, renamed and deleted for real and persists in the settings table - but
/// the catalog has no membership table for custom lists, so it stays empty and says so.
/// </para>
/// </summary>
public sealed partial class MyListsPage : Page, ILocalizableView, IActivatableView
{
    private const string CustomListKey = "thrillers";

    private string _selectedKey = "watchlist";
    private string _query = string.Empty;

    /// <summary>
    /// The tab strip raises Checked while the XAML is still being parsed (the selected tab is
    /// declared with IsChecked="True"), before the grid and the empty state exist. The constructor
    /// applies the initial list instead.
    /// </summary>
    private readonly bool _isReady;

    public MyListsPage()
    {
        this.InitializeComponent();
        _isReady = true;
        ApplyLocalization();
    }

    public event EventHandler<MovieRecord>? MovieSelected;

    public void Activate() => Refresh();

    public void ApplyLocalization()
    {
        FlowDirection = LocalizationService.FlowDirection;
        PageTitleText.Text = LocalizationService.Text("MyLists");
        NewListButtonText.Text = LocalizationService.Text("NewList");
        RenameListButton.Content = LocalizationService.Text("Rename");
        DeleteListButton.Content = LocalizationService.Text("Delete");
        SearchBox.PlaceholderText = LocalizationService.Text("SearchLists");
        ListsCountText.Text = LocalizationService.Format("ListsCountFormat", Definitions.Count);
        LocalizeTabs();
        Refresh();
    }

    /// <summary>
    /// A tab's content is a UIElement (icon + label + count), so it is declared per instance in the
    /// markup and code only fills in the two text blocks. The tuple pairs each tab with its own
    /// labels, which keeps the tabs and the filters in <see cref="BuildItems" /> in one place
    /// without walking a live visual tree.
    /// </summary>
    private (RadioButton Tab, TextBlock Label, TextBlock Count)[] Tabs =>
    [
        (WatchlistItem, WatchlistLabel, WatchlistCount),
        (FavoritesItem, FavoritesLabel, FavoritesCount),
        (WatchLaterItem, WatchLaterLabel, WatchLaterCount),
        (ThrillersItem, ThrillersLabel, ThrillersCount),
    ];

    /// <summary>
    /// The lists on the strip: the three system sections, plus the user's own list when one exists.
    /// The custom list's name is the stored one, which is why the tabs are labelled from here rather
    /// than from a fixed table.
    /// </summary>
    private IReadOnlyList<MediaListDefinition> Definitions
    {
        get
        {
            List<MediaListDefinition> definitions =
            [
                new("watchlist", "Watchlist", true),
                new("favorites", "Favorites", true),
                new("watch-later", "WatchLater", true),
            ];

            if (CustomListName is not null)
            {
                definitions.Add(new(CustomListKey, CustomListName, false));
            }

            return definitions;
        }
    }

    /// <summary>The user's list name, or null when they have not created one (or deleted it).</summary>
    private static string? CustomListName => AppSettingsStore.CustomLists.FirstOrDefault();

    private void LocalizeTabs()
    {
        var custom = CustomListName;
        ThrillersItem.Visibility = custom is null ? Visibility.Collapsed : Visibility.Visible;

        foreach (var (tab, label, _) in Tabs)
        {
            if (tab.Tag is not string key)
            {
                continue;
            }

            // A system list is labelled from the localization table; the user's list is labelled with
            // the name they typed, which is not a translatable key.
            label.Text = key == CustomListKey
                ? custom ?? string.Empty
                : LocalizationService.Text(Definitions.First(list => list.Key == key).LabelKey);
        }
    }

    private void Refresh()
    {
        var definitions = Definitions;
        var definition = definitions.FirstOrDefault(list => list.Key == _selectedKey) ?? definitions[0];
        _selectedKey = definition.Key;

        ListNameText.Text = definition.IsSystem
            ? LocalizationService.Text(definition.LabelKey)
            : definition.LabelKey;
        RenameListButton.Visibility = definition.IsSystem ? Visibility.Collapsed : Visibility.Visible;
        DeleteListButton.Visibility = definition.IsSystem ? Visibility.Collapsed : Visibility.Visible;

        var items = BuildItems(definition);
        ListGrid.ItemsSource = items;
        ListCountText.Text = items.Count == 1
            ? $"{LocalizationService.Number(items.Count)} {LocalizationService.Text("ItemSingular")}"
            : $"{LocalizationService.Number(items.Count)} {LocalizationService.Text("Items")}";

        // The design puts the item count on the active tab only.
        foreach (var (tab, _, count) in Tabs)
        {
            var isActive = tab.Tag as string == definition.Key;
            count.Text = isActive ? LocalizationService.Number(items.Count) : string.Empty;
        }

        var isEmpty = items.Count == 0;
        ListGrid.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;
        EmptyState.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;

        if (!isEmpty)
        {
            return;
        }

        var searching = _query.Length > 0;
        var isCustom = _selectedKey == CustomListKey;
        EmptyState.Title = LocalizationService.Text(searching ? "NoResults" : "ListEmpty");
        EmptyState.Message = LocalizationService.Text(
            searching ? "NoResultsHelp" : isCustom ? "CustomListEmptyHelp" : "ListEmptyHelp");
        EmptyState.ActionText = LocalizationService.Text("ClearSearch");
        EmptyState.IsActionVisible = searching;
    }

    /// <summary>
    /// The three system lists read the real personal-library sections. The user's own list has no
    /// membership store yet, so it is genuinely empty rather than being filled with a guess.
    /// </summary>
    private IReadOnlyList<ListMediaItem> BuildItems(MediaListDefinition definition)
    {
        var showBadge = definition.Key == "watch-later";

        var movies = definition.Key switch
        {
            "watchlist" => LoadSection(PersonalLibrarySection.Watchlist),
            "favorites" => LoadSection(PersonalLibrarySection.Liked),
            "watch-later" => LoadSection(PersonalLibrarySection.ReadyToWatch),
            _ => [],
        };

        return
        [
            .. movies
                .Where(movie => _query.Length == 0
                    || movie.Title.Contains(_query, StringComparison.CurrentCultureIgnoreCase)
                    || movie.Year.ToString(CultureInfo.InvariantCulture).Contains(_query, StringComparison.Ordinal))
                .Select(movie => new ListMediaItem(
                    movie,
                    showBadge,
                    movie.HasLocalFile,
                    LocalizationService.Text(movie.HasLocalFile ? "Local" : "OnlineOnly"))),
        ];
    }

    private static IReadOnlyList<MovieRecord> LoadSection(PersonalLibrarySection section)
    {
        try
        {
            return [.. AppServices.Personal.ListPersonalMovies(section).Select(LibraryProjection.ToCard)];
        }
        catch (Exception)
        {
            return [];
        }
    }

    private void ListTab_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isReady)
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
        SearchBox.Text = string.Empty;
        _query = string.Empty;
        Refresh();
    }

    private void ListGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ListMediaItem item)
        {
            MovieSelected?.Invoke(this, item.Movie);
        }
    }

    private void NewListButton_Click(object sender, RoutedEventArgs e) =>
        _ = ShowListNameDialogAsync(LocalizationService.Text("NewList"), string.Empty);

    private void RenameListButton_Click(object sender, RoutedEventArgs e) =>
        _ = ShowListNameDialogAsync(LocalizationService.Text("RenameList"), ListNameText.Text);

    private void DeleteListButton_Click(object sender, RoutedEventArgs e) =>
        _ = ShowDeleteListDialogAsync();

    /// <summary>
    /// Creates or renames the user's list. The name is written to the settings table, so it survives a
    /// restart; an empty name is rejected rather than silently creating an unnamed list.
    /// </summary>
    private async Task ShowListNameDialogAsync(string title, string value)
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

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var name = input.Text?.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        AppSettingsStore.CustomLists = [name];
        _selectedKey = CustomListKey;
        ThrillersItem.IsChecked = true;
        ApplyLocalization();
    }

    /// <summary>
    /// Deletes the user's list. Only the list definition goes away - no movie, no file and no personal
    /// state is touched by this.
    /// </summary>
    private async Task ShowDeleteListDialogAsync()
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            FlowDirection = LocalizationService.FlowDirection,
            RequestedTheme = ThemeService.ElementTheme,
            Title = LocalizationService.Text("DeleteList"),
            Content = ListNameText.Text,
            PrimaryButtonText = LocalizationService.Text("Delete"),
            CloseButtonText = LocalizationService.Text("Cancel"),
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        AppSettingsStore.CustomLists = [];
        _selectedKey = "watchlist";
        WatchlistItem.IsChecked = true;
        ApplyLocalization();
    }
}
