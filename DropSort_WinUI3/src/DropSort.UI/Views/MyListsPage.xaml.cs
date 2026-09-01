using System.Globalization;
using DropSort.UI.Models;
using DropSort.UI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DropSort.UI.Views;

public sealed partial class MyListsPage : Page, ILocalizableView, IActivatableView
{
    private string _selectedKey = DemoData.Lists[0].Key;
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
        ListsCountText.Text = LocalizationService.Format("ListsCountFormat", DemoData.Lists.Count);
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

    private void LocalizeTabs()
    {
        foreach (var (tab, label, _) in Tabs)
        {
            if (tab.Tag is not string key)
            {
                continue;
            }

            var definition = DemoData.Lists.FirstOrDefault(list => list.Key == key);
            if (definition is not null)
            {
                label.Text = LocalizationService.Text(definition.LabelKey);
            }
        }
    }

    private void Refresh()
    {
        var definition = DemoData.Lists.FirstOrDefault(list => list.Key == _selectedKey) ?? DemoData.Lists[0];
        ListNameText.Text = LocalizationService.Text(definition.LabelKey);
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
        EmptyState.Title = LocalizationService.Text(searching ? "NoResults" : "ListEmpty");
        EmptyState.Message = LocalizationService.Text(searching ? "NoResultsHelp" : "ListEmptyHelp");
        EmptyState.ActionText = LocalizationService.Text("ClearSearch");
        EmptyState.IsActionVisible = searching;
    }

    /// <summary>
    /// The four designed lists. Watch Later is the one that carries the Local / Online only badge on
    /// its cards, so it is also the one that mixes local and remote entries.
    /// </summary>
    private IReadOnlyList<ListMediaItem> BuildItems(MediaListDefinition definition)
    {
        var showBadge = definition.Key == "watch-later";
        return DemoData.Movies
            .Where(movie => definition.Key switch
            {
                "watchlist" => movie.InWatchlist,
                "favorites" => movie.IsLiked,
                "watch-later" => movie.InWatchlist && !movie.IsBlacklisted,
                "thrillers" => movie.Genres.Contains("Thriller"),
                _ => false,
            })
            .Where(movie => _query.Length == 0
                || movie.Title.Contains(_query, StringComparison.CurrentCultureIgnoreCase)
                || movie.Year.ToString(CultureInfo.InvariantCulture).Contains(_query, StringComparison.Ordinal))
            .Select(movie => new ListMediaItem(
                movie,
                showBadge,
                movie.HasLocalFile,
                LocalizationService.Text(movie.HasLocalFile ? "Local" : "OnlineOnly")))
            .ToArray();
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
    /// Creating and renaming lists needs a persistence layer that does not exist yet, so the
    /// dialog is real, native and correctly styled but confirming it currently changes nothing.
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

        await dialog.ShowAsync();
    }

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

        await dialog.ShowAsync();
    }
}
