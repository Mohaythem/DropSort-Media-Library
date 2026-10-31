using System.Globalization;
using TBDMovies.Application.Dto;
using TBDMovies.Domain.Library.Availability;
using TBDMovies.Domain.Library.Personal;
using TBDMovies.UI.Models;
using TBDMovies.UI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace TBDMovies.UI.Views;

/// <summary>
/// The landing page: library totals, what the user can continue watching, and the newest additions.
/// <para>
/// The movie totals, TV progression and persisted recent watch history all come from the local catalog.
/// </para>
/// </summary>
public sealed partial class HomePage : Page, ILocalizableView, IActivatableView, IDeactivatableView
{
    private IReadOnlyList<MovieRecord> _movies = [];
    private IReadOnlyList<TVShowRecord> _shows = [];
    private IReadOnlyList<WatchHistoryItem> _history = [];
    private IReadOnlyList<HomeActivityCard> _continueSource = [];
    private IReadOnlyList<WatchHistoryCardRecord> _historyCards = [];
    private readonly PostHandoffRequestLifetime _playbackRequests = new();

    public HomePage()
    {
        this.InitializeComponent();
        ContinueRowGrid.SizeChanged += HomePage_SizeChanged;
        RecentRowGrid.SizeChanged += RecentRowGrid_SizeChanged;
        ContextRequested += Home_ContextRequested;
        ApplyLocalization();
        Activate();
    }

    public event EventHandler<MovieRecord>? MovieSelected;

    public event EventHandler<TVShowRecord>? ShowSelected;

    public event EventHandler? LibraryRequested;

    public event EventHandler<string>? ActivityListRequested;

    public void Activate()
    {
        _movies = LoadMovies();
        _shows = LoadShows();
        _history = LoadHistory();
        StatsRepeater.ItemsSource = BuildStats();
        _continueSource = BuildContinueWatching();
        ContinueRepeater.ItemsSource = _continueSource;
        _historyCards = BuildHistoryCards();
        UpdateContinueVisibility();
        UpdateHistoryVisibility();
        UpdateActivityShelves();
    }

    public void Deactivate() => _playbackRequests.Cancel();

    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        Deactivate();
        base.OnNavigatedFrom(e);
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
        ContinueSeeAllButton.Content = LocalizationService.Text("SeeAll");
        HistorySeeAllButton.Content = LocalizationService.Text("SeeAll");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(ContinueSeeAllButton,
            $"{LocalizationService.Text("SeeAll")}: {LocalizationService.Text("ContinueWatching")}");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(HistorySeeAllButton,
            $"{LocalizationService.Text("SeeAll")}: {LocalizationService.Text("RecentlyWatched")}");
        StatsRepeater.ItemsSource = BuildStats();
        _continueSource = BuildContinueWatching();
        ContinueRepeater.ItemsSource = _continueSource;
        _historyCards = BuildHistoryCards();
        UpdateContinueVisibility();
        UpdateHistoryVisibility();
        UpdateActivityShelves();
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
            return AppServices.ViewingActivity.ListRecentCompleted(20);
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>
    /// Continue Watching is supplied by the application service from successful playback activity.
    /// </summary>
    private IReadOnlyList<HomeActivityCard> BuildContinueWatching()
    {
        try
        {
            return [.. AppServices.ViewingActivity.ListActive(20).Select(item => new HomeActivityCard(item))];
        }
        catch { return []; }
    }

    public static string HistoryTypeLabel(WatchEventMediaKind mediaKind) =>
        LocalizationService.Text(mediaKind == WatchEventMediaKind.Movie ? "HomeMovieType" : "HomeEpisodeType");

    public static Visibility UnavailableVisibility(bool isAvailable) => isAvailable ? Visibility.Collapsed : Visibility.Visible;

    public static string HistoryAutomationId(int eventId) => $"HomeHistoryCard_{eventId}";

    private IReadOnlyList<WatchHistoryCardRecord> BuildHistoryCards() =>
        [.. _history.Select(item => new WatchHistoryCardRecord(
            item.Id,
            item.MediaKind,
            item.MovieId,
            item.ShowId,
            HistoryTitle(item),
            HistoryContext(item),
            LocalizationService.Digits(item.WatchedAt.ToLocalTime().ToString("d", LocalizationService.Culture)),
            item.IsAvailable,
            item.IsAvailable ? string.Empty : LocalizationService.Text("Unavailable"),
            HistoryArtwork(item)))];

    // Completed-viewing cards use the same landscape artwork priority as Continue Watching.
    private static IEnumerable<string?> HistoryArtwork(WatchHistoryItem item) =>
        [item.BackdropReference, item.ArtworkReference, item.PosterReference];

    public static string HistoryMetaLine(string watchedAt, WatchEventMediaKind mediaKind) =>
        $"{watchedAt} · {HistoryTypeLabel(mediaKind)}";

    private static string HistoryTitle(WatchHistoryItem item)
    {
        // The context line below identifies the individual episode.
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

            // Keep the episode identity visible in the compact history card.
            case WatchEventMediaKind.Episode when item.SeasonNumber is int season && item.EpisodeNumber is int episode:
                return LocalizationService.Digits(LocalizationService.Format(
                    "HomeContinueEpisodeFormat", season, episode, item.Title));

            case WatchEventMediaKind.Episode:
                return item.ShowTitle ?? string.Empty;

            default:
                return string.Empty;
        }
    }

    private void UpdateContinueVisibility()
    {
        var hasAny = ContinueRepeater.ItemsSource is IEnumerable<HomeActivityCard> records && records.Any();
        ContinueRowGrid.Visibility = hasAny
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;
        ContinueEmptyState.Visibility = hasAny
            ? Microsoft.UI.Xaml.Visibility.Collapsed
            : Microsoft.UI.Xaml.Visibility.Visible;
        ContinueEmptyState.Title = LocalizationService.Text("NothingToContinue");
        ContinueEmptyState.Message = LocalizationService.Text("NothingToContinueHelp");
        if (!hasAny)
        {
            ContinueSeeAllButton.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateHistoryVisibility()
    {
        var hasAny = _history.Count > 0;
        HistoryRowGrid.Visibility = hasAny ? Visibility.Visible : Visibility.Collapsed;
        HistoryEmptyState.Visibility = hasAny ? Visibility.Collapsed : Visibility.Visible;
        HistoryEmptyState.Title = LocalizationService.Text("NothingWatched");
        HistoryEmptyState.Message = LocalizationService.Text("NothingWatchedHelp");
        if (!hasAny)
        {
            HistorySeeAllButton.Visibility = Visibility.Collapsed;
        }
    }

    private void Home_ContextRequested(UIElement sender, Microsoft.UI.Xaml.Input.ContextRequestedEventArgs e)
        => MediaCardContextMenu.ShowForContext(this, e, onChanged: Activate);

    private void HomePage_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateActivityShelves();

    private void RecentRowGrid_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateRecentShelf();

    private void UpdateRecentShelf()
    {
        var width = RecentRowGrid.ActualWidth;
        if (!double.IsFinite(width) || width <= 0) return;
        var cellWidth = (double)Microsoft.UI.Xaml.Application.Current.Resources["MediaCardItemWidth"];
        RecentRowGrid.Clip = new RectangleGeometry
            { Rect = new Windows.Foundation.Rect(0, 0, width, RecentRowGrid.ActualHeight) };
        RecentGrid.ItemsSource = _movies.Take(HomeActivityLayout.CalculateItemsToRender(width, _movies.Count, cellWidth, 0)).ToArray();
        SeeAllButton.Visibility = HomeActivityLayout.ShouldShowSeeAll(width, _movies.Count, cellWidth, 0)
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateActivityShelves()
    {
        var availableWidth = ContinueRowGrid.ActualWidth;
        if (!double.IsFinite(availableWidth) || availableWidth <= 0)
            availableWidth = ActualWidth;

        if (double.IsFinite(availableWidth) && availableWidth > 0)
        {
            var clipRect = new Windows.Foundation.Rect(0, 0, availableWidth, 196);
            ContinueRowGrid.Clip = new RectangleGeometry { Rect = clipRect };
            HistoryRowGrid.Clip = new RectangleGeometry { Rect = clipRect };
        }

        var continueRender = HomeActivityLayout.CalculateItemsToRender(availableWidth, _continueSource.Count);
        var historyRender = HomeActivityLayout.CalculateItemsToRender(availableWidth, _historyCards.Count);

        ContinueRepeater.ItemsSource = _continueSource.Take(continueRender).ToArray();
        HistoryRepeater.ItemsSource = _historyCards.Take(historyRender).ToArray();

        ContinueSeeAllButton.Visibility = HomeActivityLayout.ShouldShowSeeAll(availableWidth, _continueSource.Count)
            ? Visibility.Visible
            : Visibility.Collapsed;
        HistorySeeAllButton.Visibility = HomeActivityLayout.ShouldShowSeeAll(availableWidth, _history.Count)
            ? Visibility.Visible
            : Visibility.Collapsed;
        UpdateRecentShelf();
    }

    private IReadOnlyList<StatCardRecord> BuildStats()
    {
        var episodes = _shows.Sum(show => show.EpisodeCount);
        var onDisk = _shows.Sum(show => show.LocalEpisodeCount);
        return
        [
            new(LocalizationService.Text("StatMovies"), Count(_movies.Count), "\uE8B2"),
            new(LocalizationService.Text("StatShows"), Count(_shows.Count), "\uE7F4"),
            new(LocalizationService.Text("StatEpisodes"), Count(episodes), "\uE8FD"),
            new(LocalizationService.Text("StatEpisodesOnDisk"), Count(onDisk), "\uEDA2"),
        ];
    }

    private static string Count(int value) => LocalizationService.Number(value);

    private void ContinueDetails_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is Button { Tag: HomeActivityCard card })
        {
            if (card.MovieId is int movieId && _movies.FirstOrDefault(movie => movie.Id == movieId) is MovieRecord movie)
            {
                MovieSelected?.Invoke(this, movie);
            }
            else if (card.ShowId is int showId && _shows.FirstOrDefault(show => show.Id == showId) is TVShowRecord show)
            {
                ShowSelected?.Invoke(this, show);
            }
        }
    }

    private async void ContinuePlay_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: HomeActivityCard card } || !card.CanPlay)
        {
            return;
        }

        var request = _playbackRequests.Begin();
        card.SetPlaying(true);
        try
        {
            var result = await AppServices.PlayMediaFileAsync(card.Path!, request.Token);
            if (!request.IsCurrent)
            {
                return;
            }

            if (result.ErrorCode == "PlaybackActivityNotSaved")
            {
                await ShowPlayMessageAsync(LocalizationService.Text("Failed"), LocalizationService.Text("PlaybackActivityNotSaved"));
                return;
            }

            if (result.Status == AvailabilityInspectionStatus.Present)
            {
                Activate();
                return;
            }

            if (result.Status == AvailabilityInspectionStatus.StorageUnavailable)
            {
                await ShowPlayMessageAsync(LocalizationService.Text("StorageUnavailable"), LocalizationService.Text("DetailsOfflineHelp"));
            }
            else if (result.Status == AvailabilityInspectionStatus.Missing)
            {
                await ShowPlayMessageAsync(LocalizationService.Text("Missing"), LocalizationService.Text("DetailsMissingHelp"));
            }
            else
            {
                await ShowPlayMessageAsync(LocalizationService.Text("Failed"), LocalizationService.Text("Failed"));
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            if (request.IsCurrent)
            {
                await ShowPlayMessageAsync(LocalizationService.Text("Failed"), LocalizationService.Text("Failed"));
            }
        }
        finally
        {
            card.SetPlaying(false);
        }
    }

    private async Task ShowPlayMessageAsync(string title, string message)
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

    private void ContinueSeeAllButton_Click(object sender, RoutedEventArgs e) =>
        ActivityListRequested?.Invoke(this, "continue-watching");

    private void HistorySeeAllButton_Click(object sender, RoutedEventArgs e) =>
        ActivityListRequested?.Invoke(this, "recently-watched");
}
