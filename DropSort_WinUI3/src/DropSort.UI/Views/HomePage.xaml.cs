using System.Globalization;
using DropSort.UI.Models;
using DropSort.UI.Services;
using Microsoft.UI.Xaml.Controls;

namespace DropSort.UI.Views;

/// <summary>
/// The landing page: library totals, what the user can continue watching, and the newest additions.
/// <para>
/// The movie totals and the recent grid come from the catalog. The TV numbers and Continue watching
/// still come from the bundled sample shows: the catalog has no season or episode tables, so there is
/// nothing real to count there yet.
/// </para>
/// </summary>
public sealed partial class HomePage : Page, ILocalizableView, IActivatableView
{
    private IReadOnlyList<MovieRecord> _movies = [];
    private IReadOnlyList<TVShowRecord> _shows = [];

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
        StatsRepeater.ItemsSource = BuildStats();
        ContinueRepeater.ItemsSource = BuildContinueWatching();
        RecentGrid.ItemsSource = _movies.Take(8).ToArray();
        UpdateContinueVisibility();
    }

    public void ApplyLocalization()
    {
        FlowDirection = LocalizationService.FlowDirection;
        PageTitleText.Text = LocalizationService.Text("WelcomeBack");
        PageSubtitleText.Text = LocalizationService.Text("YourLibrary");
        ContinueHeadingText.Text = LocalizationService.Text("ContinueWatching");
        RecentHeadingText.Text = LocalizationService.Text("RecentlyAdded");
        SeeAllButton.Content = LocalizationService.Text("SeeAll");
        StatsRepeater.ItemsSource = BuildStats();
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

    /// <summary>
    /// The shows with an episode on disk, each pointing at the first episode that can be played. There
    /// is no episode watch state in this build, so "continue" means the first available episode rather
    /// than a resume point that would have to be invented.
    /// </summary>
    private IReadOnlyList<ContinueWatchingRecord> BuildContinueWatching() =>
    [
        .. _shows
            .Where(show => show.LocalEpisodeCount > 0)
            .Take(6)
            .Select(show => new ContinueWatchingRecord(
                show.Id,
                show.Title,
                LocalizationService.Format("EpisodesOnDiskFormat", show.LocalEpisodeCount, show.EpisodeCount))),
    ];

    private void UpdateContinueVisibility()
    {
        var hasAny = _shows.Any(show => show.LocalEpisodeCount > 0);
        ContinueRepeater.Visibility = hasAny
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;
        ContinueEmptyState.Visibility = hasAny
            ? Microsoft.UI.Xaml.Visibility.Collapsed
            : Microsoft.UI.Xaml.Visibility.Visible;
        ContinueEmptyState.Title = LocalizationService.Text("NothingToContinue");
        ContinueEmptyState.Message = LocalizationService.Text("NothingToContinueHelp");
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
