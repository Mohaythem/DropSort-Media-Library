using System.Globalization;
using System.Threading.Tasks;
using DropSort.UI.Models;
using DropSort.UI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using XamlApplication = Microsoft.UI.Xaml.Application;

namespace DropSort.UI.Views;

/// <summary>The states the library grid can be in. The shell or a future backend drives these.</summary>
public enum LibraryViewState
{
    Ready,
    Loading,
    Error,
}

public sealed partial class LibraryPage : Page, ILocalizableView, IActivatableView
{
    private LibraryViewState _state = LibraryViewState.Ready;
    private string _mediaType = "movies";
    private string _availability = "all";
    private string _sort = "recent";

    /// <summary>
    /// The active sort direction. It is not a free-standing preference: changing the sort field resets it
    /// to that field's natural default (newest first for Recent, Date added and Year, A-Z for Title), and the
    /// direction button flips it until the field changes again.
    /// </summary>
    private bool _sortDescending = true;
    private string _query = string.Empty;
    private LibraryViewMode _viewMode;
    private int _viewSwitchGeneration;
    private bool _recentMovieOpened;
    private bool _recentShowOpened;
    private readonly Dictionary<int, ShowCardItem> _showCards = new();

    /// <summary>The catalog rows for this visit, or null when they still have to be read.</summary>
    private IReadOnlyList<MovieRecord>? _movies;
    private long _moviesRevision = -1;

    /// <summary>
    /// The tab strip raises Checked while the XAML is still being parsed (the selected tab is
    /// declared with IsChecked="True"), at which point the elements the refresh touches do not
    /// exist yet. Everything the strip drives is applied by the constructor instead.
    /// </summary>
    private readonly bool _isReady;

    public LibraryPage()
    {
        this.InitializeComponent();
        _viewMode = AppSettingsStore.ReadLibraryViewMode();
        _isReady = true;
        ApplyLocalization();
        ApplyViewMode(_viewMode, persist: false);
    }

    public event EventHandler<MovieRecord>? MovieSelected;

    public event EventHandler<TVShowRecord>? ShowSelected;

    /// <summary>
    /// Reuse catalog rows after an activity-only return; reload after another catalog commit.
    /// </summary>
    public void Activate()
    {
        if (_movies is not null && _moviesRevision != AppServices.CatalogRevision)
            _movies = null;
        if (_shows is not null && _showsRevision != AppServices.CatalogRevision)
            _shows = null;
        Refresh();
        var opened = _mediaType == "shows" ? _recentShowOpened : _recentMovieOpened;
        if (opened && _sort == "recent" && _sortDescending)
        {
            var grid = _mediaType == "shows" ? ShowsGrid : MoviesGrid;
            if (grid.Items.Count > 0)
                grid.ScrollIntoView(grid.Items[0], ScrollIntoViewAlignment.Leading);
        }
        _recentMovieOpened = false;
        _recentShowOpened = false;
    }

    public void InvalidateCatalog()
    {
        _movies = null;
        _shows = null;
        _showCards.Clear();
    }

    public void RecordMovieOpened(int movieId)
    {
        var before = AppServices.CatalogRevision;
        var openedAt = AppServices.Library.RecordMovieOpened(movieId);
        var after = AppServices.CatalogRevision;
        var movie = _movies?.FirstOrDefault(row => row.Id == movieId);
        if (movie is not null && _moviesRevision == before && after == before + 1)
        {
            movie.RecordOpened(openedAt);
            _moviesRevision = after;
            if (_showsRevision == before) _showsRevision = after;
        }
        else InvalidateCatalog();
        _recentMovieOpened = true;
    }

    public void RecordShowOpened(int showId)
    {
        var before = AppServices.CatalogRevision;
        var openedAt = AppServices.Tv.RecordShowOpened(showId);
        var after = AppServices.CatalogRevision;
        var show = _shows?.FirstOrDefault(row => row.Id == showId);
        if (show is not null && _showsRevision == before && after == before + 1)
        {
            show.RecordOpened(openedAt);
            _showsRevision = after;
            if (_moviesRevision == before) _moviesRevision = after;
        }
        else InvalidateCatalog();
        _recentShowOpened = true;
    }

    /// <summary>Entry point for a future library backend to report load progress or failure.</summary>
    public void SetState(LibraryViewState state)
    {
        _state = state;
        Refresh();
    }

    public void ApplyLocalization()
    {
        FlowDirection = LocalizationService.FlowDirection;
        PageTitleText.Text = LocalizationService.Text("Library");
        MoviesTypeItem.Content = LocalizationService.Text("MoviesTab");
        ShowsTypeItem.Content = LocalizationService.Text("TvShowsTab");
        MediaTypeSelector.SetValue(Microsoft.UI.Xaml.Automation.AutomationProperties.NameProperty,
            LocalizationService.Text("MediaTypeLabel"));
        FilterAllItem.Text = LocalizationService.Text("All");
        FilterLocalItem.Text = LocalizationService.Text("Local");
        FilterRemoteItem.Text = LocalizationService.Text("OnlineOnly");
        SortAddedItem.Text = LocalizationService.Text("SortAdded");
        SortRecentItem.Text = LocalizationService.Text("SortRecent");
        SortTitleItem.Text = LocalizationService.Text("Title");
        SortYearItem.Text = LocalizationService.Text("Year");
        SortMetadataItem.Text = LocalizationService.Text("MetadataStatus");
        ViewModeSelector.SetValue(Microsoft.UI.Xaml.Automation.AutomationProperties.NameProperty,
            LocalizationService.Text("LibraryViewModeLabel"));
        var gridLabel = LocalizationService.Text("LibraryViewGrid");
        var compactLabel = LocalizationService.Text("LibraryViewCompact");
        GridViewModeItem.Text = string.Empty;
        CompactViewModeItem.Text = string.Empty;
        GridViewModeItem.SetValue(Microsoft.UI.Xaml.Automation.AutomationProperties.NameProperty, gridLabel);
        CompactViewModeItem.SetValue(Microsoft.UI.Xaml.Automation.AutomationProperties.NameProperty, compactLabel);
        ToolTipService.SetToolTip(GridViewModeItem, gridLabel);
        ToolTipService.SetToolTip(CompactViewModeItem, compactLabel);
        LoadingText.Text = LocalizationService.Text("LoadingLibrary");
        ErrorState.Title = LocalizationService.Text("LibraryError");
        ErrorState.Message = LocalizationService.Text("LibraryErrorHelp");
        ErrorState.ActionText = LocalizationService.Text("Retry");
        Refresh();
    }

    private void ViewModeSelector_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (!_isReady || sender.SelectedItem is not SelectorBarItem { Tag: string tag })
        {
            return;
        }

        var mode = LibraryViewModePreference.Parse(tag);
        if (mode != _viewMode)
        {
            ApplyViewMode(mode, persist: true);
        }
    }

    private void ApplyViewMode(LibraryViewMode mode, bool persist)
    {
        _viewMode = mode;
        var selected = mode == LibraryViewMode.Compact ? CompactViewModeItem : GridViewModeItem;
        if (!ReferenceEquals(ViewModeSelector.SelectedItem, selected))
        {
            ViewModeSelector.SelectedItem = selected;
        }

        if (persist)
        {
            AppSettingsStore.WriteLibraryViewMode(mode);
        }

        var grid = _mediaType == "shows" ? ShowsGrid : MoviesGrid;
        var anchor = CaptureFirstVisibleItem(grid);
        var generation = ++_viewSwitchGeneration;
        var source = grid.ItemsSource;
        var resources = XamlApplication.Current.Resources;
        MoviesGrid.ItemTemplate = mode == LibraryViewMode.Compact
            ? (DataTemplate)resources["HorizontalMovieCardTemplate"]
            : (DataTemplate)Resources["LibraryMovieCardTemplate"];
        ShowsGrid.ItemTemplate = mode == LibraryViewMode.Compact
            ? (DataTemplate)resources["HorizontalShowCardTemplate"]
            : (DataTemplate)Resources["LibraryShowCardTemplate"];
        MoviesGrid.ItemContainerStyle = mode == LibraryViewMode.Compact
            ? (Style)resources["MediaHorizontalGridViewItemStyle"]
            : (Style)resources["MediaGridViewItemStyle"];
        ShowsGrid.ItemContainerStyle = mode == LibraryViewMode.Compact
            ? (Style)resources["MediaHorizontalGridViewItemStyle"]
            : (Style)resources["MediaGridViewItemStyle"];
        MoviesGrid.ItemsPanel = (ItemsPanelTemplate)Resources[mode == LibraryViewMode.Compact
            ? "LibraryHorizontalItemsPanel"
            : "LibraryMoviePosterItemsPanel"];
        ShowsGrid.ItemsPanel = (ItemsPanelTemplate)Resources[mode == LibraryViewMode.Compact
            ? "LibraryHorizontalItemsPanel"
            : "LibraryShowPosterItemsPanel"];

        DispatcherQueue.TryEnqueue(() =>
        {
            if (generation == _viewSwitchGeneration
                && ReferenceEquals(grid.ItemsSource, source)
                && (grid.Visibility == Visibility.Visible || anchor is null))
            {
                grid.UpdateLayout();
                ApplyLibraryLayout(MoviesGrid);
                ApplyLibraryLayout(ShowsGrid);
                if (anchor is not null)
                {
                    grid.ScrollIntoView(anchor, ScrollIntoViewAlignment.Leading);
                }
            }
        });
    }

    private void LibraryGrid_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is GridView grid)
        {
            ApplyLibraryLayout(grid);
        }
    }

    private void LibraryGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is GridView grid)
        {
            ApplyLibraryLayout(grid);
        }
    }

    private void HorizontalPanel_EffectiveViewportChanged(FrameworkElement sender, EffectiveViewportChangedEventArgs args)
    {
        if (sender is ItemsWrapGrid panel && double.IsFinite(args.EffectiveViewport.Width) && args.EffectiveViewport.Width > 0)
        {
            if (ReferenceEquals(MoviesGrid.ItemsPanelRoot, panel))
            {
                ApplyLibraryLayout(MoviesGrid, args.EffectiveViewport.Width);
            }
            else if (ReferenceEquals(ShowsGrid.ItemsPanelRoot, panel))
            {
                ApplyLibraryLayout(ShowsGrid, args.EffectiveViewport.Width);
            }
        }
    }

    private void ApplyLibraryLayout(GridView grid, double viewportWidth = double.NaN)
    {
        if (grid.Visibility != Visibility.Visible || grid.ActualWidth <= 0
            || grid.ItemsPanelRoot is not ItemsWrapGrid panel)
        {
            return;
        }

        var available = grid.ActualWidth - grid.Padding.Left - grid.Padding.Right;
        // The effective viewport also changes when a scrollbar consumes space without resizing the grid.
        if (double.IsFinite(viewportWidth) && viewportWidth > 0)
        {
            available = Math.Min(available, viewportWidth);
        }
        double cellWidth;
        double cellHeight;
        if (_viewMode == LibraryViewMode.Compact)
        {
            cellWidth = LibraryHorizontalLayout.Calculate(available).CellWidth;
            cellHeight = LibraryHorizontalLayout.CellHeight;
        }
        else
        {
            var metrics = LibraryPosterLayout.Calculate(available);
            cellWidth = metrics.CellWidth;
            cellHeight = ReferenceEquals(grid, ShowsGrid) ? metrics.ShowCellHeight : metrics.MovieCellHeight;
        }
        if (!double.IsFinite(panel.ItemWidth) || Math.Abs(panel.ItemWidth - cellWidth) > 0.5)
        {
            panel.ItemWidth = cellWidth;
        }
        if (!double.IsFinite(panel.ItemHeight) || Math.Abs(panel.ItemHeight - cellHeight) > 0.5)
        {
            panel.ItemHeight = cellHeight;
        }
    }

    private void PosterSurface_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is FrameworkElement surface && double.IsFinite(e.NewSize.Width) && e.NewSize.Width > 0)
        {
            var height = e.NewSize.Width * LibraryPosterLayout.AspectRatio;
            if (!double.IsFinite(surface.Height) || Math.Abs(surface.Height - height) > 0.5)
            {
                surface.Height = height;
            }
        }
    }

    private object? CaptureFirstVisibleItem(GridView grid)
    {
        if (grid.Items.Count == 0)
        {
            return null;
        }

        if (grid.ItemsPanelRoot is ItemsWrapGrid panel && panel.FirstVisibleIndex >= 0 && panel.FirstVisibleIndex < grid.Items.Count)
        {
            return grid.Items[panel.FirstVisibleIndex];
        }

        return null;
    }

    private void Refresh()
    {
        var isShows = _mediaType == "shows";
        SearchBox.PlaceholderText = LocalizationService.Text(isShows ? "SearchTvShows" : "SearchLibrary");
        FilterButtonText.Text = _availability switch
        {
            "local" => LocalizationService.Text("Local"),
            "remote" => LocalizationService.Text("OnlineOnly"),
            _ => LocalizationService.Text("Filter"),
        };
        SortButtonText.Text = _sort switch
        {
            "recent" => LocalizationService.Text("SortRecent"),
            "title" => LocalizationService.Text("Title"),
            "year" => LocalizationService.Text("Year"),
            "metadata" => LocalizationService.Text("MetadataStatus"),
            _ => LocalizationService.Text("SortAdded"),
        };
        UpdateSortDirectionVisuals();

        if (_state == LibraryViewState.Ready && !AppServices.IsAvailable)
        {
            _state = LibraryViewState.Error;
        }

        LoadingPanel.Visibility = _state == LibraryViewState.Loading ? Visibility.Visible : Visibility.Collapsed;
        ErrorState.Visibility = _state == LibraryViewState.Error ? Visibility.Visible : Visibility.Collapsed;

        if (_state != LibraryViewState.Ready)
        {
            MoviesGrid.Visibility = Visibility.Collapsed;
            ShowsGrid.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Collapsed;
            CountText.Text = string.Empty;
            return;
        }

        var count = isShows ? PopulateShows() : PopulateMovies();

        // The catalog read can fail after the visibility above was applied; show the error instead
        // of an empty grid that would read as "your library is empty".
        if (_state == LibraryViewState.Error)
        {
            ErrorState.Visibility = Visibility.Visible;
            MoviesGrid.Visibility = Visibility.Collapsed;
            ShowsGrid.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Collapsed;
            CountText.Text = string.Empty;
            return;
        }

        var noun = isShows
            ? LocalizationService.Text(count == 1 ? "ShowSingular" : "Shows")
            : LocalizationService.Text(count == 1 ? "MovieSingular" : "Movies");
        CountText.Text = count == 0
            ? string.Empty
            : $"{LocalizationService.Number(count)} {noun}";

        var isEmpty = count == 0;
        MoviesGrid.Visibility = !isEmpty && !isShows ? Visibility.Visible : Visibility.Collapsed;
        ShowsGrid.Visibility = !isEmpty && isShows ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;

        if (!isEmpty)
        {
            return;
        }

        var filtered = _query.Length > 0 || _availability != "all";
        EmptyState.Title = LocalizationService.Text(filtered ? "NoResults" : "EmptyLibrary");
        EmptyState.Message = LocalizationService.Text(filtered ? "NoResultsHelp" : "EmptyLibraryHelp");
        EmptyState.ActionText = LocalizationService.Text("ClearSearch");
        EmptyState.IsActionVisible = filtered;
    }

    private int PopulateMovies()
    {
        var items = LoadMovies()
            .Where(movie => _availability switch
            {
                "local" => movie.HasLocalFile,
                "remote" => !movie.HasLocalFile,
                _ => true,
            })
            .Where(MatchesMovie);

        // "Metadata Status" is an explicit sort: its direction chooses whether clean or problematic items
        // lead (ascending = confirmed → review → none; descending = none → review → confirmed for cleanup).
        if (_sort == "metadata")
        {
            var byMeta = _sortDescending
                ? items.OrderByDescending(movie => movie.MetadataGroupRank)
                : items.OrderBy(movie => movie.MetadataGroupRank);
            items = byMeta.ThenBy(movie => movie.Title, StringComparer.CurrentCultureIgnoreCase);
        }
        else
        {
            // Title and Year retain their existing metadata groups. Recent and Date Added use
            // the complete filtered sequence so an incomplete item can lead by its timestamp.
            var grouped = items.OrderBy(movie => movie.MetadataGroupRank);
            items = _sort switch
            {
                "title" => _sortDescending
                    ? grouped.ThenByDescending(movie => movie.Title, StringComparer.CurrentCultureIgnoreCase)
                    : grouped.ThenBy(movie => movie.Title, StringComparer.CurrentCultureIgnoreCase),
                "year" => _sortDescending
                    ? grouped.ThenByDescending(movie => movie.Year)
                    : grouped.ThenBy(movie => movie.Year),
                "added" => LibraryActivityOrdering.ByDateAdded(items, movie => movie.DateAdded, movie => movie.Id, _sortDescending),
                _ => LibraryActivityOrdering.ByRecent(items, movie => movie.LastOpenedAt, movie => movie.DateAdded, movie => movie.Id, _sortDescending),
            };
        }

        var result = items.ToArray();
        MoviesGrid.ItemsSource = result;
        return result.Length;
    }

    /// <summary>
    /// The catalog rows for this visit, read once and then filtered in memory.
    /// <para>
    /// Refresh runs on every keystroke in the search box, every filter change and every navigation;
    /// reading the whole table each time put a database query behind each keystroke. The cache is
    /// invalidated when the catalog revision changes or the library is cleared. Activity-only returns
    /// reuse the rows and their artwork. A failure here is the page's error state, not a crash.
    /// </para>
    /// </summary>
    private IReadOnlyList<MovieRecord> LoadMovies()
    {
        if (_movies is not null)
        {
            return _movies;
        }

        try
        {
            var revision = AppServices.CatalogRevision;
            var rows = AppServices.Library.ListMovies().Select(LibraryProjection.ToCard).ToArray();
            _movies = rows;
            _moviesRevision = revision;
            if (_moviesRevision != AppServices.CatalogRevision)
                _movies = null;
            return rows;
        }
        catch (Exception)
        {
            _movies = null;
            _state = LibraryViewState.Error;
            return [];
        }
    }

    /// <summary>
    /// The TV catalog for this visit, or null when it still has to be read. Same rule as the movie
    /// cache: read once per catalog revision, filter in memory.
    /// </summary>
    private IReadOnlyList<TVShowRecord>? _shows;
    private long _showsRevision = -1;

    /// <summary>Reads the shows grid from the catalog; a failure is the page's error state.</summary>
    private IReadOnlyList<TVShowRecord> LoadShows()
    {
        if (_shows is not null)
        {
            return _shows;
        }

        try
        {
            var revision = AppServices.CatalogRevision;
            var rows = AppServices.Tv.ListShows().Select(TvProjection.ToCard).ToArray();
            _shows = rows;
            _showCards.Clear();
            _showsRevision = revision;
            if (_showsRevision != AppServices.CatalogRevision)
                _shows = null;
            return rows;
        }
        catch (Exception)
        {
            _shows = null;
            _state = LibraryViewState.Error;
            return [];
        }
    }

    private int PopulateShows()
    {
        var items = LoadShows()
            .Where(show => _availability switch
            {
                "local" => show.LocalEpisodeCount > 0,
                "remote" => show.LocalEpisodeCount == 0,
                _ => true,
            })
            .Where(MatchesShow);

        items = _sort switch
        {
            "title" => _sortDescending
                ? items.OrderByDescending(show => show.Title, StringComparer.CurrentCultureIgnoreCase)
                : items.OrderBy(show => show.Title, StringComparer.CurrentCultureIgnoreCase),
            "year" => _sortDescending
                ? items.OrderByDescending(show => show.Year)
                : items.OrderBy(show => show.Year),
            "added" => LibraryActivityOrdering.ByDateAdded(items, show => show.DateAdded, show => show.Id, _sortDescending),
            "metadata" => _sortDescending
                ? items.OrderByDescending(show => show.MetadataStatus == DropSort.Domain.Library.Movies.MetadataStatus.Ready ? 0 : 1).ThenBy(show => show.Title, StringComparer.CurrentCultureIgnoreCase).ThenBy(show => show.Id)
                : items.OrderBy(show => show.MetadataStatus == DropSort.Domain.Library.Movies.MetadataStatus.Ready ? 0 : 1).ThenBy(show => show.Title, StringComparer.CurrentCultureIgnoreCase).ThenBy(show => show.Id),
            _ => LibraryActivityOrdering.ByRecent(items, show => show.LastOpenedAt, show => show.DateAdded, show => show.Id, _sortDescending),
        };

        var result = items
            .Select(show => GetShowCard(show, show.SeasonCount == 1
                ? LocalizationService.Format(
                    "SeasonAvailableFormat",
                    show.LocalEpisodeCount,
                    show.EpisodeCount)
                : LocalizationService.Format(
                    "SeasonsAvailableFormat",
                    show.SeasonCount,
                    show.LocalEpisodeCount,
                    show.EpisodeCount)))
            .ToArray();
        ShowsGrid.ItemsSource = result;
        return result.Length;
    }

    private ShowCardItem GetShowCard(TVShowRecord show, string secondaryLine)
    {
        if (!_showCards.TryGetValue(show.Id, out var card)
            || !ReferenceEquals(card.Show, show) || card.SecondaryLine != secondaryLine)
        {
            card = new ShowCardItem(show, secondaryLine);
            _showCards[show.Id] = card;
        }
        return card;
    }

    private bool MatchesMovie(MovieRecord movie) =>
        _query.Length == 0
        || Contains(movie.Title)
        || Contains(movie.OriginalTitle)
        || Contains(movie.Year.ToString(CultureInfo.InvariantCulture))
        || movie.Genres.Any(Contains);

    private bool MatchesShow(TVShowRecord show) =>
        _query.Length == 0
        || Contains(show.Title)
        || Contains(show.Year.ToString(CultureInfo.InvariantCulture))
        || show.Genres.Any(Contains);

    private bool Contains(string? value) =>
        value is not null && value.Contains(_query, StringComparison.CurrentCultureIgnoreCase);

    private void MediaTypeItem_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isReady)
        {
            return;
        }

        if (sender is RadioButton { Tag: string mediaType } && mediaType != _mediaType)
        {
            _mediaType = mediaType;
            Refresh();
        }
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        _query = sender.Text?.Trim() ?? string.Empty;
        Refresh();
    }

    private void FilterItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioMenuFlyoutItem { Tag: string availability })
        {
            _availability = availability;
            Refresh();
        }
    }

    private void SortItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioMenuFlyoutItem { Tag: string sort })
        {
            _sort = sort;
            // Changing the field resets direction to that field's natural default: A-Z for Title,
            // newest first for Date added and Year.
            _sortDescending = DefaultDescendingFor(sort);
            Refresh();
        }
    }

    /// <summary>The natural starting direction for a sort field. Title reads A-Z and Metadata Status leads
    /// with confirmed items; the rest read newest first.</summary>
    private static bool DefaultDescendingFor(string sort) => sort is not ("title" or "metadata");

    private void SortSplit_Click(SplitButton sender, SplitButtonClickEventArgs args)
    {
        // Pressing the split button's face reverses the current criterion's direction.
        _sortDescending = !_sortDescending;
        Refresh();
    }

    /// <summary>
    /// Draws the sort control from state as one surface: the criterion label plus a direction chevron
    /// (down for descending, up for ascending). The accessible name and tooltip state both the criterion
    /// and the direction, so the arrow is not the only cue.
    /// </summary>
    private void UpdateSortDirectionVisuals()
    {
        SortDirectionGlyph.Glyph = _sortDescending ? "" : "";
        var criterion = _sort switch
        {
            "recent" => LocalizationService.Text("SortRecent"),
            "title" => LocalizationService.Text("Title"),
            "year" => LocalizationService.Text("Year"),
            "metadata" => LocalizationService.Text("MetadataStatus"),
            _ => LocalizationService.Text("SortAdded"),
        };
        var direction = LocalizationService.Text(_sortDescending ? "SortDescending" : "SortAscending");
        var label = $"{criterion} · {direction}";
        ToolTipService.SetToolTip(SortButton, label);
        SortButton.SetValue(Microsoft.UI.Xaml.Automation.AutomationProperties.NameProperty, label);
    }

    private void EmptyStateAction_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = string.Empty;
        _query = string.Empty;
        _availability = "all";
        FilterAllItem.IsChecked = true;
        Refresh();
    }

    private void ErrorRetry_Click(object sender, RoutedEventArgs e) => SetState(LibraryViewState.Ready);

    private void MoviesGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is MovieRecord movie)
        {
            MovieSelected?.Invoke(this, movie);
        }
    }

    private void ShowsGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ShowCardItem item)
        {
            ShowSelected?.Invoke(this, item.Show);
        }
    }

    private void LibraryGrid_ContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        if (sender is GridView grid)
            MediaCardContextMenu.ShowForContext(grid, e, _viewMode == LibraryViewMode.Compact,
                onChanged: () => { InvalidateCatalog(); Refresh(); });
    }

}
