using System.Globalization;
using DropSort.Application.Dto;
using DropSort.Domain.Media.Discovery;
using DropSort.UI.Models;
using DropSort.UI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace DropSort.UI.Views;

/// <summary>The states the Add Media review flow can be in.</summary>
public enum AddMediaScanState
{
    Idle,
    Scanning,
    Results,
}

/// <summary>
/// Add Media.
/// <para>
/// The movies flow is real end to end: the folder picker chooses a root, the discovery service walks
/// it on a worker thread while the designed progress row reports live counts and Cancel stops it, and
/// Add Selected registers every detected candidate in the catalog through
/// <see cref="IImportUiActions.RegisterMovieImport" /> - which is the offline path, so no metadata
/// provider is needed. Registration is idempotent per path, so scanning the same folder twice does
/// not duplicate a movie.
/// </para>
/// <para>
/// The episodes flow can pick files and list them, but the catalog has no episode tables yet, so
/// adding them is refused with a plain statement instead of a fake success.
/// </para>
/// </summary>
public sealed partial class AddMediaPage : Page, ILocalizableView, IActivatableView
{
    private AddMediaScanState _state = AddMediaScanState.Idle;
    private string _mediaType = "movies";
    private IReadOnlyList<DiscoveredMedia> _detected = [];
    private bool _isScanning;

    /// <summary>
    /// Cancellation for the running scan. A CancellationTokenSource is the safe way to hand a stop
    /// signal to a worker thread; a plain bool field is not guaranteed to be observed there.
    /// </summary>
    private CancellationTokenSource? _cancellation;

    /// <summary>
    /// The tab strip raises Checked while the XAML is still being parsed, before the result sections
    /// exist; the constructor applies the initial media type instead.
    /// </summary>
    private readonly bool _isReady;

    public AddMediaPage()
    {
        this.InitializeComponent();
        _isReady = true;
        FolderPathBox.Text = AppSettingsStore.MovieFolder ?? string.Empty;
        ApplyLocalization();
    }

    /// <summary>Raised after media is registered so the shell can refresh the library.</summary>
    public event EventHandler? MediaRegistered;

    public void Activate() => Refresh();

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
        ColumnTitleText.Text = LocalizationService.Text("Title");
        ColumnYearText.Text = LocalizationService.Text("Year");
        ColumnQualityText.Text = LocalizationService.Text("Quality");
        ColumnActionText.Text = LocalizationService.Text("Action");
        MoviesResultHeadingText.Text = LocalizationService.Text("DetectedMovies");
        EpisodesResultHeadingText.Text = LocalizationService.Text("DetectedEpisodes");
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
            _isScanning ? "Cancel" : _state == AddMediaScanState.Results ? "ResetScan" : "ScanFolder");
        ScanProgressText.Text = LocalizationService.Text("ScanningFolder");

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
        AddSelectedButton.IsEnabled = movies.Count > 0;

        // There is no per-row selection control in this list, so the action cannot honestly be called
        // "Add Selected": it registers every detected movie. The label states the count it will add.
        AddSelectedButton.Content = LocalizationService.Format("AddAllFormat", movies.Count);

        var episodes = BuildDetectedEpisodes();
        DetectedEpisodesRepeater.ItemsSource = episodes;
        EpisodesResultCountText.Text = LocalizationService.Format(
            "DetectedEpisodesFormat", DetectedSeasonNumber(), episodes.Count);
        AddEpisodesButton.Content = LocalizationService.Format("AddEpisodesFormat", episodes.Count);
        AddEpisodesButton.IsEnabled = episodes.Count > 0;
    }

    /// <summary>
    /// The detected movie rows. Quality is whatever the parser could read out of the file name, and a
    /// row the parser could not name at all is still listed - as its file name - so nothing found on
    /// disk disappears silently from the review.
    /// </summary>
    private IReadOnlyList<DetectedMovieDisplayRecord> BuildDetectedMovies() =>
    [
        .. _detected
            .Where(item => item.Classification == DiscoveryClassification.MovieCandidate)
            .Select(item => new DetectedMovieDisplayRecord(
                item.ParsedMedia?.Title is { Length: > 0 } title
                    ? title
                    : Path.GetFileNameWithoutExtension(item.Path),
                item.ParsedMedia?.Year ?? 0,
                item.ParsedMedia?.Resolution ?? string.Empty,
                Path.GetFileName(item.Path),
                LocalizationService.Text(item.ParsedMedia?.Year is null ? "FindMatch" : "Review"),
                item.ParsedMedia?.Year is not null)),
    ];

    /// <summary>
    /// The detected episode rows. A file the parser resolved shows its show name and code and is ready
    /// to register; one it could not resolve is still listed - as its file name, marked for review - so
    /// nothing found on disk disappears silently from the review.
    /// </summary>
    private IReadOnlyList<DetectedEpisodeDisplayRecord> BuildDetectedEpisodes() =>
    [
        .. EpisodeItems().Select(item =>
        {
            var isReady = item.Classification == DiscoveryClassification.TvEpisodeCandidate;

            return new DetectedEpisodeDisplayRecord(
                isReady
                    ? ShowFormatting.EpisodeCode(
                        item.ParsedMedia!.SeasonNumber!.Value,
                        item.ParsedMedia!.EpisodeNumber!.Value)
                    : string.Empty,
                isReady ? item.ParsedMedia!.Title! : Path.GetFileNameWithoutExtension(item.Path),
                Path.GetFileName(item.Path),
                LocalizationService.Text(isReady ? "Review" : "NeedsReview"),
                isReady);
        }),
    ];

    /// <summary>Every discovered file that is an episode, resolved or not.</summary>
    private IEnumerable<DiscoveredMedia> EpisodeItems() => _detected.Where(item =>
        item.Classification is DiscoveryClassification.TvEpisodeCandidate
            or DiscoveryClassification.TvEpisodeSkipped);

    /// <summary>
    /// The season the detected episodes belong to, for the results heading: the lowest season any
    /// resolved row claims, or 1 when nothing resolved.
    /// </summary>
    private int DetectedSeasonNumber()
    {
        var seasons = EpisodeItems()
            .Where(item => item.Classification == DiscoveryClassification.TvEpisodeCandidate)
            .Select(item => item.ParsedMedia!.SeasonNumber!.Value)
            .ToArray();

        return seasons.Length == 0 ? 1 : seasons.Min();
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
            FolderPathBox.Text = (_mediaType == "episodes"
                ? AppSettingsStore.ShowFolder
                : AppSettingsStore.MovieFolder) ?? string.Empty;
            Refresh();
        }
    }

    /// <summary>
    /// Picks the root to scan and remembers it: the same folder is the approved destination root that
    /// Organize File moves into, so choosing it here also configures that.
    /// </summary>
    private async void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FolderPicker();
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, MainWindow.WindowHandle);

            if (await picker.PickSingleFolderAsync() is not StorageFolder folder)
            {
                return;
            }

            FolderPathBox.Text = folder.Path;

            if (_mediaType == "episodes")
            {
                AppSettingsStore.ShowFolder = folder.Path;
            }
            else
            {
                AppSettingsStore.MovieFolder = folder.Path;
            }
        }
        catch (Exception error)
        {
            await ShowMessageAsync(LocalizationService.Text("Failed"), error.Message);
        }
    }

    /// <summary>Picks loose episode files. Listing them is supported; registering them is not yet.</summary>
    private async void AddFilesButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker();
            foreach (var extension in MediaExtensions)
            {
                picker.FileTypeFilter.Add(extension);
            }

            WinRT.Interop.InitializeWithWindow.Initialize(picker, MainWindow.WindowHandle);
            var files = await picker.PickMultipleFilesAsync();

            if (files is null || files.Count == 0)
            {
                return;
            }

            var picked = files.Select(file => file.Path).ToArray();
            _detected = await Task.Run(() => DiscoverPicked(picked));
            _state = _detected.Count == 0 ? AddMediaScanState.Idle : AddMediaScanState.Results;
            Refresh();
        }
        catch (Exception error)
        {
            await ShowMessageAsync(LocalizationService.Text("Failed"), error.Message);
        }
    }

    private static readonly string[] MediaExtensions =
        [".mkv", ".mp4", ".avi", ".mov", ".m4v", ".wmv", ".ts", ".webm"];

    /// <summary>Scan, cancel a running scan, or clear the last result - one button, three states.</summary>
    private async void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isScanning)
        {
            _cancellation?.Cancel();
            return;
        }

        if (_state == AddMediaScanState.Results)
        {
            _detected = [];
            _state = AddMediaScanState.Idle;
            Refresh();
            return;
        }

        await ScanAsync();
    }

    /// <summary>
    /// Walks the chosen root on a worker thread. Discovery is synchronous and can take seconds on a
    /// large tree, so it must not run on the UI thread; progress and the result are marshalled back
    /// through the dispatcher.
    /// </summary>
    private async Task ScanAsync()
    {
        var root = FolderPathBox.Text?.Trim();

        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            await ShowMessageAsync(
                LocalizationService.Text("AddMedia"),
                LocalizationService.Text("FolderPathPlaceholder"));
            return;
        }

        _cancellation?.Dispose();
        _cancellation = new CancellationTokenSource();
        var token = _cancellation.Token;

        _isScanning = true;
        _detected = [];
        _state = AddMediaScanState.Scanning;
        ScanProgressBar.Maximum = 1;
        ScanProgressBar.Value = 0;
        Refresh();

        try
        {
            var session = await Task.Run(() => AppServices.Import.PrepareImportReview(
                root,
                recursive: true,
                progress: update => DispatcherQueue.TryEnqueue(() => ReportScanProgress(update)),
                isCancelled: () => token.IsCancellationRequested));

            _detected = session.Items;
            _state = _detected.Count == 0 ? AddMediaScanState.Idle : AddMediaScanState.Results;

            if (_detected.Count == 0)
            {
                EmptyState.Title = LocalizationService.Text("NothingDetected");
                EmptyState.Message = LocalizationService.Text("NothingDetectedHelp");
            }
        }
        catch (OperationCanceledException)
        {
            _state = AddMediaScanState.Idle;
        }
        catch (Exception error)
        {
            _state = AddMediaScanState.Idle;
            await ShowMessageAsync(LocalizationService.Text("Failed"), error.Message);
        }
        finally
        {
            _isScanning = false;
            Refresh();
        }
    }

    private void ReportScanProgress(ImportReviewProgress update)
    {
        ScanProgressBar.Maximum = Math.Max(1, update.FilesDiscovered);
        ScanProgressBar.Value = Math.Min(update.FilesProcessed, ScanProgressBar.Maximum);
        ScanProgressText.Text = LocalizationService.Format(
            "CheckedOfFormat",
            update.FilesProcessed,
            Math.Max(update.FilesDiscovered, update.FilesProcessed));
    }

    /// <summary>
    /// Registers every detected candidate - the list has no per-row selection, and the button says
    /// "Add All (n)" for that reason. Registration is per file path and idempotent, so a folder that was
    /// already added simply reports the same movies again instead of duplicating them.
    /// </summary>
    private async void AddSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        var candidates = _detected
            .Where(item => item.Classification == DiscoveryClassification.MovieCandidate)
            .ToArray();

        if (candidates.Length == 0)
        {
            return;
        }

        AddSelectedButton.IsEnabled = false;
        var added = 0;
        var failures = new List<string>();

        try
        {
            var observedAt = DateTimeOffset.UtcNow;

            await Task.Run(() =>
            {
                foreach (var candidate in candidates)
                {
                    try
                    {
                        AppServices.Import.RegisterMovieImport(new ConfirmMovieImportCommand(
                            candidate.Path,
                            candidate.FileSize ?? 0,
                            candidate.ParsedMedia!,
                            observedAt));
                        added++;
                    }
                    catch (Exception error)
                    {
                        failures.Add(Path.GetFileName(candidate.Path) + ": " + error.Message);
                    }
                }
            });
        }
        finally
        {
            AddSelectedButton.IsEnabled = true;
        }

        MediaRegistered?.Invoke(this, EventArgs.Empty);

        _detected = [];
        _state = AddMediaScanState.Idle;
        Refresh();

        var summary = LocalizationService.Format("ItemsAddedFormat", added);

        if (failures.Count > 0)
        {
            summary += Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, failures);
        }

        await ShowMessageAsync(LocalizationService.Text("AddMedia"), summary);
    }

    /// <summary>
    /// Registers every resolved episode into the show / season / episode hierarchy. Registration is per
    /// file path and idempotent, so re-adding a folder reports the same episodes instead of duplicating
    /// them. A file the parser could not resolve is not registered at all, and the summary says why.
    /// </summary>
    private async void AddEpisodesButton_Click(object sender, RoutedEventArgs e)
    {
        var candidates = EpisodeItems()
            .Where(item => item.Classification == DiscoveryClassification.TvEpisodeCandidate)
            .ToArray();

        var unresolved = EpisodeItems()
            .Where(item => item.Classification == DiscoveryClassification.TvEpisodeSkipped)
            .Select(item => Path.GetFileName(item.Path) + ": " + LocalizationService.Text("EpisodeNoMarkerHelp"))
            .ToList();

        if (candidates.Length == 0 && unresolved.Count == 0)
        {
            return;
        }

        AddEpisodesButton.IsEnabled = false;
        var added = 0;

        try
        {
            var observedAt = DateTimeOffset.UtcNow;

            await Task.Run(() =>
            {
                foreach (var candidate in candidates)
                {
                    try
                    {
                        AppServices.Import.RegisterEpisodeImport(new ConfirmEpisodeImportCommand(
                            candidate.Path,
                            candidate.FileSize ?? 0,
                            candidate.ParsedMedia!,
                            observedAt));
                        added++;
                    }
                    catch (EpisodeRegistrationException refusal)
                    {
                        unresolved.Add(Path.GetFileName(candidate.Path) + ": " + RefusalText(refusal.Refusal));
                    }
                    catch (Exception error)
                    {
                        unresolved.Add(Path.GetFileName(candidate.Path) + ": " + error.Message);
                    }
                }
            });
        }
        finally
        {
            AddEpisodesButton.IsEnabled = true;
        }

        MediaRegistered?.Invoke(this, EventArgs.Empty);

        _detected = [];
        _state = AddMediaScanState.Idle;
        Refresh();

        var summary = LocalizationService.Format("EpisodesAddedFormat", added);

        if (unresolved.Count > 0)
        {
            summary += Environment.NewLine + Environment.NewLine
                + LocalizationService.Format("EpisodesSkippedFormat", unresolved.Count)
                + Environment.NewLine
                + string.Join(Environment.NewLine, unresolved);
        }

        await ShowMessageAsync(LocalizationService.Text("AddMedia"), summary);
    }

    /// <summary>The plain reason a file was refused, in the user's language.</summary>
    private static string RefusalText(EpisodeRegistrationRefusal refusal) => LocalizationService.Text(refusal switch
    {
        EpisodeRegistrationRefusal.NoEpisodeMarker => "EpisodeNoMarkerHelp",
        EpisodeRegistrationRefusal.NoShowTitle => "EpisodeNoTitleHelp",
        EpisodeRegistrationRefusal.NumbersOutOfRange => "EpisodeRangeHelp",
        EpisodeRegistrationRefusal.AlreadyAMovieFile => "EpisodeIsMovieFileHelp",
        _ => "EpisodeOtherOwnerHelp",
    });

    /// <summary>
    /// Parses picked files through the same discovery service the folder scan uses: each file's own
    /// directory is scanned without recursion and the results are filtered back to what was picked, so
    /// one parser decides what a name means.
    /// </summary>
    private static IReadOnlyList<DiscoveredMedia> DiscoverPicked(IReadOnlyList<string> paths)
    {
        var wanted = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        var results = new List<DiscoveredMedia>();

        foreach (var directory in paths
            .Select(Path.GetDirectoryName)
            .Where(directory => !string.IsNullOrEmpty(directory))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var found = AppServices.Import.PrepareImportReview(directory!, recursive: false);
            results.AddRange(found.Items.Where(item => wanted.Contains(item.Path)));
        }

        return results;
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

    /// <summary>
    /// Runs the real manual lookup. Without a configured TMDB token the provider has no candidates to
    /// return, and the flow reports that instead of pretending to have searched.
    /// </summary>
    private async void TmdbSearchButton_Click(object sender, RoutedEventArgs e)
    {
        var title = TmdbSearchBox.Text?.Trim();

        if (string.IsNullOrWhiteSpace(title))
        {
            return;
        }

        try
        {
            if (!AppServices.Settings.IsTmdbConfigured())
            {
                await ShowMessageAsync(
                    LocalizationService.Text("SearchTmdbHeading"),
                    LocalizationService.Text("TmdbNotConfiguredHelp"));
                return;
            }

            var result = await Task.Run(() => AppServices.Import.ManualMovieSearch(title));
            await ShowMessageAsync(
                LocalizationService.Text("SearchTmdbHeading"),
                result.Candidates.Count == 0
                    ? LocalizationService.Text("NoResults")
                    : string.Join(Environment.NewLine, result.Candidates.Take(10).Select(candidate =>
                        candidate.Title + (candidate.Year is null ? string.Empty : $" ({candidate.Year})"))));
        }
        catch (Exception error)
        {
            await ShowMessageAsync(LocalizationService.Text("Failed"), error.Message);
        }
    }

    /// <summary>One themed message surface for this page; a dialog lives outside the content root.</summary>
    private async Task ShowMessageAsync(string title, string message)
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
}
