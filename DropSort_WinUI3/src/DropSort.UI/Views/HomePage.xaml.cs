using System.Globalization;
using DropSort.Application.Dto;
using DropSort.Domain.Library.Personal;
using DropSort.UI.Models;
using DropSort.UI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DropSort.UI.Views;

/// <summary>
/// The landing page: library totals, what the user can continue watching, and the newest additions.
/// <para>
/// The movie totals, TV progression and persisted recent watch history all come from the local catalog.
/// </para>
/// </summary>
public sealed partial class HomePage : Page, ILocalizableView, IActivatableView
{
    private IReadOnlyList<MovieRecord> _movies = [];
    private IReadOnlyList<TVShowRecord> _shows = [];
    private IReadOnlyList<WatchHistoryItem> _history = [];

    public HomePage()
    {
        this.InitializeComponent();
        ApplyLocalization();
        Activate();
    }

    public event EventHandler<MovieRecord>? MovieSelected;

    public event EventHandler<TVShowRecord>? ShowSelected;

    public event EventHandler? LibraryRequested;

    public void Activate()
    {
        _movies = LoadMovies();
        _shows = LoadShows();
        _history = LoadHistory();
        StatsRepeater.ItemsSource = BuildStats();
        ContinueRepeater.ItemsSource = BuildContinueWatching();
        HistoryRepeater.ItemsSource = BuildHistoryCards();
        RecentGrid.ItemsSource = _movies.Take(8).ToArray();
        UpdateContinueVisibility();
        UpdateHistoryVisibility();
    }

    public void ApplyLocalization()
    {
        FlowDirection = LocalizationService.FlowDirection;
        PageTitleText.Text = LocalizationService.Text("WelcomeBack");
        PageSubtitleText.Text = LocalizationService.Text("YourLibrary");
        ContinueHeadingText.Text = LocalizationService.Text("ContinueWatching");
        HistoryHeadingText.Text = LocalizationService.Text("RecentlyWatched");
        RecentHeadingText.Text = LocalizationService.Text("RecentlyAdded");
        SeeAllButton.Content = LocalizationService.Text("SeeAll");
        StatsRepeater.ItemsSource = BuildStats();
        ContinueRepeater.ItemsSource = BuildContinueWatching();
        HistoryRepeater.ItemsSource = BuildHistoryCards();
        UpdateContinueVisibility();
        UpdateHistoryVisibility();
    }

    /// <summary>A catalog read failure leaves Home with zero movies rather than taking the page down.</summary>
    private static IReadOnlyList<MovieRecord> LoadMovies()
    {
        try
        {
            return [.. AppServices.Library.ListMovies().Select(LibraryProjection.ToCard)];
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>A catalog read failure leaves Home with no shows rather than taking the page down.</summary>
    private static IReadOnlyList<TVShowRecord> LoadShows()
    {
        try
        {
            return [.. AppServices.Tv.ListShows().Select(TvProjection.ToCard)];
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static IReadOnlyList<WatchHistoryItem> LoadHistory()
    {
        try
        {
            return AppServices.WatchHistory.ListRecentWatchHistory(8);
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>
    /// Continue Watching is supplied by the application service: only shows with persisted episode
    /// history and a next available unwatched episode are shown.
    /// </summary>
    private IReadOnlyList<ContinueWatchingRecord> BuildContinueWatching()
    {
        try
        {
            return [.. AppServices.Tv.ListContinueWatching(6).Select(item => new ContinueWatchingRecord(
                item.ShowId,
                item.ShowTitle,
                LocalizationService.Digits(string.Format(LocalizationService.Culture, LocalizationService.Text("ContinueEpisodeFormat"), item.SeasonNumber, item.EpisodeNumber, item.EpisodeTitle ?? string.Empty)),
                [item.ArtworkReference, item.ShowBackdropReference, item.ShowPosterReference]))];
        }
        catch { return []; }
    }

    private IReadOnlyList<WatchHistoryCardRecord> BuildHistoryCards() =>
        [.. _history.Select(item => new WatchHistoryCardRecord(
            item.Id,
            item.MediaKind,
            item.MovieId,
            item.ShowId,
            HistoryTitle(item),
            HistoryContext(item),
            LocalizationService.Digits(item.WatchedAt.ToLocalTime().ToString("g", LocalizationService.Culture)),
            item.IsAvailable,
            item.IsAvailable ? string.Empty : LocalizationService.Text("Unavailable"),
            HistoryArtwork(item)))];

    // A Recently Watched TV entry represents the whole show, so its card leads with the show's own
    // artwork (backdrop, then poster) and only falls back to the latest episode still. A movie leads
    // with its own backdrop/poster.
    private static IEnumerable<string?> HistoryArtwork(WatchHistoryItem item) =>
        item.MediaKind == WatchEventMediaKind.Episode
            ? [item.BackdropReference, item.PosterReference, item.ArtworkReference]
            : [item.ArtworkReference, item.BackdropReference, item.PosterReference];

    private static string HistoryTitle(WatchHistoryItem item)
    {
        // A TV entry is grouped by show, so the card's primary identity is the show, not the episode.
        if (item.MediaKind == WatchEventMediaKind.Episode)
        {
            return string.IsNullOrWhiteSpace(item.ShowTitle) ? item.Title : item.ShowTitle;
        }

        return item.Title;
    }

    private static string HistoryContext(WatchHistoryItem item)
    {
        switch (item.MediaKind)
        {
            case WatchEventMediaKind.Movie when item.Year is int year:
                return LocalizationService.Digits(year.ToString(CultureInfo.InvariantCulture));

            // Season-oriented secondary line for a show card, surfacing the latest watched episode:
            // "Season 2 · Last watched S02E05".
            case WatchEventMediaKind.Episode when item.SeasonNumber is int season && item.EpisodeNumber is int episode:
                return LocalizationService.Format(
                    "HistoryShowContextFormat",
                    ShowFormatting.SeasonLabel(season),
                    season,
                    episode);

            case WatchEventMediaKind.Episode:
                return item.ShowTitle ?? string.Empty;

            default:
                return string.Empty;
        }
    }

    private void UpdateContinueVisibility()
    {
        var hasAny = ContinueRepeater.ItemsSource is IEnumerable<ContinueWatchingRecord> records && records.Any();
        ContinueRepeater.Visibility = hasAny
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;
        ContinueEmptyState.Visibility = hasAny
            ? Microsoft.UI.Xaml.Visibility.Collapsed
            : Microsoft.UI.Xaml.Visibility.Visible;
        ContinueEmptyState.Title = LocalizationService.Text("NothingToContinue");
        ContinueEmptyState.Message = LocalizationService.Text("NothingToContinueHelp");
    }

    private void UpdateHistoryVisibility()
    {
        var hasAny = _history.Count > 0;
        HistoryRepeater.Visibility = hasAny ? Visibility.Visible : Visibility.Collapsed;
        HistoryEmptyState.Visibility = hasAny ? Visibility.Collapsed : Visibility.Visible;
        HistoryEmptyState.Title = LocalizationService.Text("NothingWatched");
        HistoryEmptyState.Message = LocalizationService.Text("NothingWatchedHelp");
    }

    private IReadOnlyList<StatCardRecord> BuildStats()
    {
        var episodes = _shows.Sum(show => show.EpisodeCount);
        var onDisk = _shows.Sum(show => show.LocalEpisodeCount);
        return
        [
            new(LocalizationService.Text("StatMovies"), Count(_movies.Count)),
            new(LocalizationService.Text("StatShows"), Count(_shows.Count)),
            new(LocalizationService.Text("StatEpisodes"), Count(episodes)),
            new(LocalizationService.Text("StatEpisodesOnDisk"), Count(onDisk)),
        ];
    }

    private static string Count(int value) => LocalizationService.Number(value);

    private void ContinueCard_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is Button { Tag: int showId } &&
            _shows.FirstOrDefault(show => show.Id == showId) is TVShowRecord show)
        {
            ShowSelected?.Invoke(this, show);
        }
    }

    private async void HistoryCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: WatchHistoryCardRecord card })
        {
            return;
        }

        if (!card.IsAvailable)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                RequestedTheme = ThemeService.ElementTheme,
                FlowDirection = LocalizationService.FlowDirection,
                Title = LocalizationService.Text("Unavailable"),
                Content = LocalizationService.Text("HistoryUnavailableHelp"),
                CloseButtonText = LocalizationService.Text("Close"),
            };
            await ContentDialogCoordinator.ShowAsync(dialog);
            return;
        }

        if (card.MediaKind == WatchEventMediaKind.Movie &&
            card.MovieId is int movieId &&
            _movies.FirstOrDefault(movie => movie.Id == movieId) is MovieRecord movie)
        {
            MovieSelected?.Invoke(this, movie);
        }
        else if (card.MediaKind == WatchEventMediaKind.Episode &&
                 card.ShowId is int showId &&
                 _shows.FirstOrDefault(show => show.Id == showId) is TVShowRecord show)
        {
            ShowSelected?.Invoke(this, show);
        }
    }

    private void RecentGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is MovieRecord movie)
        {
            MovieSelected?.Invoke(this, movie);
        }
    }

    private void SeeAllButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
        LibraryRequested?.Invoke(this, EventArgs.Empty);
}
