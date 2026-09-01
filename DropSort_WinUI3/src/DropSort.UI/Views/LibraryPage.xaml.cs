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
    private string _query = string.Empty;

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

    public void Activate() => Refresh();

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
            _ => LocalizationService.Text("SortAdded"),
        };

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
        var items = DemoData.Movies
            .Where(movie => _availability switch
            {
                "local" => movie.HasLocalFile,
                "remote" => !movie.HasLocalFile,
                _ => true,
            })
            .Where(MatchesMovie);

        items = _sort switch
        {
            "title" => items.OrderBy(movie => movie.Title, StringComparer.CurrentCultureIgnoreCase),
            "year" => items.OrderByDescending(movie => movie.Year),
            _ => items,
        };

        var result = items.ToArray();
        MoviesGrid.ItemsSource = result;
        return result.Length;
    }

    private int PopulateShows()
    {
        var items = DemoData.Shows
            .Where(show => _availability switch
            {
                "local" => show.Seasons.Any(season => season.LocalCount > 0),
                "remote" => show.Seasons.All(season => season.LocalCount == 0),
                _ => true,
            })
            .Where(MatchesShow);

        items = _sort switch
        {
            "title" => items.OrderBy(show => show.Title, StringComparer.CurrentCultureIgnoreCase),
            "year" => items.OrderByDescending(show => show.Year),
            _ => items,
        };

        var result = items
            .Select(show => new ShowCardItem(show, show.Seasons.Count == 1
                ? LocalizationService.Format(
                    "SeasonWatchedFormat",
                    show.WatchedEpisodeCount,
                    show.EpisodeCount)
                : LocalizationService.Format(
                    "SeasonsWatchedFormat",
                    show.Seasons.Count,
                    show.WatchedEpisodeCount,
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
            Refresh();
        }
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
