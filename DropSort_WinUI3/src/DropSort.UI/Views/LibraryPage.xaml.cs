using System.Globalization;
using DropSort.UI.Models;
using DropSort.UI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

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
    private string _sort = "added";

    /// <summary>
    /// The active sort direction. It is not a free-standing preference: changing the sort field resets it
    /// to that field's natural default (newest first for Date added and Year, A-Z for Title), and the
    /// direction button flips it until the field changes again.
    /// </summary>
    private bool _sortDescending = true;
    private string _query = string.Empty;

    /// <summary>The catalog rows for this visit, or null when they still have to be read.</summary>
    private IReadOnlyList<MovieRecord>? _movies;

    /// <summary>
    /// The tab strip raises Checked while the XAML is still being parsed (the selected tab is
    /// declared with IsChecked="True"), at which point the elements the refresh touches do not
    /// exist yet. Everything the strip drives is applied by the constructor instead.
    /// </summary>
    private readonly bool _isReady;

    public LibraryPage()
    {
        this.InitializeComponent();
        _isReady = true;
        ApplyLocalization();
    }

    public event EventHandler<MovieRecord>? MovieSelected;

    public event EventHandler<TVShowRecord>? ShowSelected;

    /// <summary>
    /// Every arrival re-reads the catalog: media may have been registered, or the library cleared,
    /// since the page was last shown.
    /// </summary>
    public void Activate()
    {
        _movies = null;
        _shows = null;
        Refresh();
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
        SortTitleItem.Text = LocalizationService.Text("Title");
        SortYearItem.Text = LocalizationService.Text("Year");
        SortMetadataItem.Text = LocalizationService.Text("MetadataStatus");
        LoadingText.Text = LocalizationService.Text("LoadingLibrary");
        ErrorState.Title = LocalizationService.Text("LibraryError");
        ErrorState.Message = LocalizationService.Text("LibraryErrorHelp");
        ErrorState.ActionText = LocalizationService.Text("Retry");
        Refresh();
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
            // Every other sort is metadata-first: confirmed metadata, then Review Match, then no metadata,
            // so blank fallback cards never break up rows of posters. The chosen criterion orders WITHIN
            // each metadata group (secondary), honouring its own direction.
            var grouped = items.OrderBy(movie => movie.MetadataGroupRank);
            items = _sort switch
            {
                "title" => _sortDescending
                    ? grouped.ThenByDescending(movie => movie.Title, StringComparer.CurrentCultureIgnoreCase)
                    : grouped.ThenBy(movie => movie.Title, StringComparer.CurrentCultureIgnoreCase),
                "year" => _sortDescending
                    ? grouped.ThenByDescending(movie => movie.Year)
                    : grouped.ThenBy(movie => movie.Year),
                // "Date added": the catalog id is the monotonic registration order, so descending is newest
                // first and ascending is oldest first.
                _ => _sortDescending
                    ? grouped.ThenByDescending(movie => movie.Id)
                    : grouped.ThenBy(movie => movie.Id),
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
    /// dropped by <see cref="Activate" />, so navigating back, registering media or clearing the
    /// library all pick up fresh rows. A failure here is the page's error state, not a crash.
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
            _movies = [.. AppServices.Library.ListMovies().Select(LibraryProjection.ToCard)];
            return _movies;
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
    /// cache: read once per arrival, filter in memory.
    /// </summary>
    private IReadOnlyList<TVShowRecord>? _shows;

    /// <summary>Reads the shows grid from the catalog; a failure is the page's error state.</summary>
    private IReadOnlyList<TVShowRecord> LoadShows()
    {
        if (_shows is not null)
        {
            return _shows;
        }

        try
        {
            _shows = [.. AppServices.Tv.ListShows().Select(TvProjection.ToCard)];
            return _shows;
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
            _ => _sortDescending
                ? items.OrderByDescending(show => show.Id)
                : items.OrderBy(show => show.Id),
        };

        var result = items
            .Select(show => new ShowCardItem(show, show.SeasonCount == 1
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
}
