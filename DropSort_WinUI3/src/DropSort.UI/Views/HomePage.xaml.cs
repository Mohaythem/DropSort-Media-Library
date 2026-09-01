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
        StatsRepeater.ItemsSource = BuildStats();
        ContinueRepeater.ItemsSource = DemoData.ContinueWatching;
        RecentGrid.ItemsSource = _movies.Take(8).ToArray();
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

    private IReadOnlyList<StatCardRecord> BuildStats()
    {
        var episodes = DemoData.Shows.Sum(show => show.EpisodeCount);
        var watched = DemoData.Shows.Sum(show => show.WatchedEpisodeCount);
        return
        [
            new(LocalizationService.Text("StatMovies"), Count(_movies.Count)),
            new(LocalizationService.Text("StatShows"), Count(DemoData.Shows.Count)),
            new(LocalizationService.Text("StatEpisodes"), Count(episodes)),
            new(LocalizationService.Text("StatWatched"), Count(watched)),
        ];
    }

    private static string Count(int value) => LocalizationService.Number(value);

    private void ContinueCard_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is Button { Tag: int showId } &&
            DemoData.Shows.FirstOrDefault(show => show.Id == showId) is TVShowRecord show)
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
