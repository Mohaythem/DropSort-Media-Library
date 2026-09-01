using System.Globalization;
using DropSort.UI.Models;
using DropSort.UI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DropSort.UI.Views;

/// <summary>The states the Add Media review flow can be in.</summary>
public enum AddMediaScanState
{
    Idle,
    Scanning,
    Results,
}

public sealed partial class AddMediaPage : Page, ILocalizableView, IActivatableView
{
    private AddMediaScanState _state = AddMediaScanState.Idle;
    private string _mediaType = "movies";

    /// <summary>
    /// The tab strip raises Checked while the XAML is still being parsed, before the result sections
    /// exist; the constructor applies the initial media type instead.
    /// </summary>
    private readonly bool _isReady;

    public AddMediaPage()
    {
        this.InitializeComponent();
        _isReady = true;
        ApplyLocalization();
    }

    public void Activate() => Refresh();

    /// <summary>
    /// Entry point for a future scanner to report progress. The designed progress row is a real
    /// code path rather than a timed animation, so it stays honest once a backend exists.
    /// </summary>
    public void SetScanProgress(int completed, int total)
    {
        _state = AddMediaScanState.Scanning;
        ScanProgressBar.Maximum = total <= 0 ? 1 : total;
        ScanProgressBar.Value = completed;
        Refresh();
    }

    public void ApplyLocalization()
    {
        FlowDirection = LocalizationService.FlowDirection;
        PageTitleText.Text = LocalizationService.Text("AddMedia");
        MoviesTypeItem.Content = LocalizationService.Text("MoviesTab");
        EpisodesTypeItem.Content = LocalizationService.Text("TvEpisodesTab");
        MediaTypeSelector.SetValue(Microsoft.UI.Xaml.Automation.AutomationProperties.NameProperty,
            LocalizationService.Text("MediaTypeLabel"));
        FolderPathBox.PlaceholderText = LocalizationService.Text("FolderPathPlaceholder");
        FolderLabelText.Text = LocalizationService.Text("MediaFolderLabel");
        BrowseButton.Content = LocalizationService.Text("Browse");
        AddFilesButton.Content = LocalizationService.Text("AddFiles");
        ScanProgressText.Text = LocalizationService.Text("ScanningFolder");
        ColumnTitleText.Text = LocalizationService.Text("Title");
        ColumnYearText.Text = LocalizationService.Text("Year");
        ColumnQualityText.Text = LocalizationService.Text("Quality");
        ColumnActionText.Text = LocalizationService.Text("Action");
        MoviesResultHeadingText.Text = LocalizationService.Text("DetectedMovies");
        EpisodesResultHeadingText.Text = LocalizationService.Text("DetectedEpisodes");
        AddSelectedButton.Content = LocalizationService.Text("AddSelected");
        SearchTmdbHeadingText.Text = LocalizationService.Text("SearchTmdbHeading");
        SearchTmdbHelpText.Text = LocalizationService.Text("SearchTmdbHelp");
        TmdbSearchBox.PlaceholderText = LocalizationService.Text("SearchTmdbPlaceholder");
        TmdbSearchButton.Content = LocalizationService.Text("Search");
        EmptyState.Title = LocalizationService.Text("NothingDetected");
        EmptyState.Message = LocalizationService.Text("NothingDetectedHelp");
        Refresh();
    }

    private void Refresh()
    {
        var isEpisodes = _mediaType == "episodes";
        SourceHeadingText.Text = LocalizationService.Text(isEpisodes ? "AddEpisodesFromFiles" : "AddMoviesFromFolder");
        SourceHelpText.Text = LocalizationService.Text(isEpisodes ? "AddEpisodesFromFilesHelp" : "AddMoviesFromFolderHelp");
        ScanButton.Content = LocalizationService.Text(
            _state == AddMediaScanState.Results ? "ResetScan" : "ScanFolder");

        // Episodes are added as individual files, movies as a folder scan: Add Files therefore only
        // belongs to the episode flow. The control is not removed, it simply is not part of the
        // movies source row in the design.
        AddFilesButton.Visibility = isEpisodes ? Visibility.Visible : Visibility.Collapsed;

        ScanProgressPanel.Visibility = _state == AddMediaScanState.Scanning
            ? Visibility.Visible
            : Visibility.Collapsed;
        EmptyState.Visibility = _state == AddMediaScanState.Idle ? Visibility.Visible : Visibility.Collapsed;

        var showResults = _state == AddMediaScanState.Results;
        MoviesResultSection.Visibility = showResults && !isEpisodes ? Visibility.Visible : Visibility.Collapsed;
        EpisodesResultSection.Visibility = showResults && isEpisodes ? Visibility.Visible : Visibility.Collapsed;

        if (!showResults)
        {
            return;
        }

        var movies = BuildDetectedMovies();
        DetectedMoviesRepeater.ItemsSource = movies;
        MoviesResultCountText.Text = LocalizationService.Format("ItemsFoundFormat", movies.Count);

        var episodes = BuildDetectedEpisodes();
        DetectedEpisodesRepeater.ItemsSource = episodes;
        EpisodesResultCountText.Text = LocalizationService.Format(
            "DetectedEpisodesFormat", SeasonNumber(), episodes.Count);
        AddEpisodesButton.Content = LocalizationService.Format("AddEpisodesFormat", episodes.Count);
    }

    private static IReadOnlyList<DetectedMovieDisplayRecord> BuildDetectedMovies() =>
        DemoData.DetectedMovies
            .Select(movie => new DetectedMovieDisplayRecord(
                movie.Title,
                movie.Year,
                movie.Quality,
                System.IO.Path.GetFileName(movie.Path),
                LocalizationService.Text(movie.Matched ? "Review" : "FindMatch"),
                movie.Matched))
            .ToArray();

    private static IReadOnlyList<DetectedEpisodeDisplayRecord> BuildDetectedEpisodes() =>
        DemoData.DetectedEpisodes
            .Select(episode => new DetectedEpisodeDisplayRecord(
                episode.Code,
                episode.Title,
                episode.FileName,
                LocalizationService.Text(episode.IsReady ? "Ready" : "NeedsReview"),
                episode.IsReady))
            .ToArray();

    /// <summary>Reads the season number out of the first detected episode code (for example S02E05).</summary>
    private static int SeasonNumber()
    {
        var code = DemoData.DetectedEpisodes.FirstOrDefault()?.Code;
        if (code is null)
        {
            return 1;
        }

        var separator = code.IndexOf('E', StringComparison.OrdinalIgnoreCase);
        var digits = separator > 1 ? code[1..separator] : code[1..];
        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var season)
            ? season
            : 1;
    }

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

    /// <summary>
    /// Picking a folder and enumerating loose files both need the V2 scanner and a window-handle
    /// aware picker. The controls stay visible and native; they are intentionally inert for now.
    /// </summary>
    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
    }

    private void AddFilesButton_Click(object sender, RoutedEventArgs e)
    {
    }

    private void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        _state = _state == AddMediaScanState.Results ? AddMediaScanState.Idle : AddMediaScanState.Results;
        Refresh();
    }

    /// <summary>Confirming an add needs the organize pipeline, so the button is inert but real.</summary>
    private void AddSelectedButton_Click(object sender, RoutedEventArgs e)
    {
    }

    private void AddEpisodesButton_Click(object sender, RoutedEventArgs e)
    {
    }

    /// <summary>
    /// The per-row action seeds the manual TMDB lookup with the detected title instead of opening a
    /// placeholder dialog, so the row stays useful without a metadata backend.
    /// </summary>
    private void RowAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string title })
        {
            TmdbSearchBox.Text = title;
            TmdbSearchBox.Focus(FocusState.Programmatic);
        }
    }
}
