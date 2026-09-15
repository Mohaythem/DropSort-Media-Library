using System.Collections.ObjectModel;
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
/// Unified Add Media review. Discovery is shared for movies and TV episodes; only rows the user
/// selects are handed to the application import use case. Local registration commits before any
/// optional confidence-aware metadata enrichment, so an offline or ambiguous lookup never loses a
/// locally indexed file.
/// </summary>
public sealed partial class AddMediaPage : Page, ILocalizableView, IActivatableView
{
    private AddMediaScanState _state = AddMediaScanState.Idle;
    private IReadOnlyList<DiscoveredMedia> _detected = [];
    private readonly ObservableCollection<DetectedMediaDisplayRecord> _rows = [];
    private bool _isScanning;
    private bool _isAdding;
    private long _scanVersion;

    /// <summary>
    /// Cancellation for the running scan. A CancellationTokenSource is the safe way to hand a stop
    /// signal to a worker thread; a plain bool field is not guaranteed to be observed there.
    /// </summary>
    private CancellationTokenSource? _cancellation;

    public AddMediaPage()
    {
        this.InitializeComponent();
        DetectedMediaRepeater.ItemsSource = _rows;
        FolderPathBox.Text = AppSettingsStore.MovieFolder ?? AppSettingsStore.ShowFolder ?? string.Empty;
        ApplyLocalization();
    }

    /// <summary>Raised after media is registered so the shell can refresh the library.</summary>
    public event EventHandler? MediaRegistered;

    public void Activate() => Refresh();

    public void ApplyLocalization()
    {
        FlowDirection = LocalizationService.FlowDirection;
        PageTitleText.Text = LocalizationService.Text("AddMedia");
        SourceHeadingText.Text = LocalizationService.Text("AddMediaScanHeading");
        SourceHelpText.Text = LocalizationService.Text("AddMediaScanHelp");
        FolderPathBox.PlaceholderText = LocalizationService.Text("FolderPathPlaceholder");
        FolderLabelText.Text = LocalizationService.Text("MediaFolderLabel");
        BrowseButton.Content = LocalizationService.Text("Browse");
        AddFilesButton.Content = LocalizationService.Text("AddFiles");
        CancelButton.Content = LocalizationService.Text("CancelReview");
        ResultsHeadingText.Text = LocalizationService.Text("ReviewMedia");
        SelectAllButton.Content = LocalizationService.Text("SelectAll");
        DeselectAllButton.Content = LocalizationService.Text("DeselectAll");
        ColumnSelectText.Text = LocalizationService.Text("Select");
        ColumnTypeText.Text = LocalizationService.Text("MediaType");
        ColumnTitleText.Text = LocalizationService.Text("Title");
        ColumnDetailsText.Text = LocalizationService.Text("Details");
        ColumnStatusText.Text = LocalizationService.Text("Status");
        SearchTmdbHeadingText.Text = LocalizationService.Text("SearchTmdbHeading");
        SearchTmdbHelpText.Text = LocalizationService.Text("SearchTmdbHelp");
        TmdbSearchBox.PlaceholderText = LocalizationService.Text("SearchTmdbPlaceholder");
        TmdbSearchButton.Content = LocalizationService.Text("Search");
        EmptyState.Title = LocalizationService.Text("NothingDetected");
        EmptyState.Message = LocalizationService.Text("NothingDetectedHelp");
        foreach (var row in _rows)
        {
            row.RefreshLocalization();
        }
        Refresh();
    }

    private void Refresh()
    {
        ScanButton.Content = LocalizationService.Text(
            _isScanning ? "Cancel" : "ScanFolder");
        ScanProgressText.Text = LocalizationService.Text("ScanningFolder");
        AddFilesButton.IsEnabled = !_isScanning && !_isAdding;
        BrowseButton.IsEnabled = !_isScanning && !_isAdding;
        CancelButton.Visibility = _isScanning || (_state == AddMediaScanState.Results && !_isAdding)
            ? Visibility.Visible
            : Visibility.Collapsed;
        ScanButton.IsEnabled = !_isAdding;

        ScanProgressPanel.Visibility = _state == AddMediaScanState.Scanning
            ? Visibility.Visible
            : Visibility.Collapsed;
        EmptyState.Visibility = _state == AddMediaScanState.Idle ? Visibility.Visible : Visibility.Collapsed;

        var showResults = _state == AddMediaScanState.Results;
        ResultsSection.Visibility = showResults ? Visibility.Visible : Visibility.Collapsed;

        if (!showResults)
        {
            return;
        }

        var selectable = _rows.Count(row => row.CanSelect);
        var selected = _rows.Count(row => row.CanSelect && row.IsSelected);
        ResultsCountText.Text = LocalizationService.Format("ItemsFoundFormat", _rows.Count)
            + " · " + LocalizationService.Format("SelectedItemsFormat", selected);
        AddSelectedButton.IsEnabled = selected > 0 && !_isAdding;
        AddSelectedButton.Content = LocalizationService.Format("AddSelectedFormat", selected);
        SelectAllButton.IsEnabled = selectable > 0 && !_isAdding;
        DeselectAllButton.IsEnabled = selected > 0 && !_isAdding;
    }

    private void SetDetected(IReadOnlyList<DiscoveredMedia> items)
    {
        foreach (var row in _rows)
        {
            row.PropertyChanged -= SelectionChanged;
        }

        _detected = items;
        _rows.Clear();
        foreach (var item in items)
        {
            var row = new DetectedMediaDisplayRecord(item);
            row.PropertyChanged += SelectionChanged;
            _rows.Add(row);
        }
    }

    private void SelectionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DetectedMediaDisplayRecord.IsSelected))
        {
            Refresh();
        }
    }

    private void SelectAllButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in _rows.Where(row => row.CanSelect))
        {
            row.IsSelected = true;
        }
        Refresh();
    }

    private void DeselectAllButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in _rows)
        {
            row.IsSelected = false;
        }
        Refresh();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        var wasScanning = _isScanning;
        _cancellation?.Cancel();
        SetDetected([]);
        _state = AddMediaScanState.Idle;
        if (!wasScanning)
        {
            _isScanning = false;
        }
        Refresh();
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

            AppSettingsStore.MovieFolder = folder.Path;
            if (string.IsNullOrWhiteSpace(AppSettingsStore.ShowFolder))
            {
                AppSettingsStore.ShowFolder = folder.Path;
            }
        }
        catch (Exception error)
        {
            await ShowMessageAsync(LocalizationService.Text("Failed"), MetadataErrorText.For(error));
        }
    }

    /// <summary>Picks loose media files and sends them through the same mixed review as folder scans.</summary>
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
            var (cancellation, scanVersion) = BeginScan();
            try
            {
                var discovered = await Task.Run(() => DiscoverPicked(
                    picked,
                    () => cancellation.Token.IsCancellationRequested));

                if (!IsCurrentScan(scanVersion, cancellation))
                {
                    return;
                }

                if (cancellation.IsCancellationRequested)
                {
                    _state = AddMediaScanState.Idle;
                    return;
                }

                SetDetected(discovered);
                _state = _detected.Count == 0 ? AddMediaScanState.Idle : AddMediaScanState.Results;
            }
            catch (OperationCanceledException)
            {
                if (IsCurrentScan(scanVersion, cancellation))
                {
                    _state = AddMediaScanState.Idle;
                }
            }
            catch (Exception error)
            {
                if (IsCurrentScan(scanVersion, cancellation))
                {
                    _state = AddMediaScanState.Idle;
                    await ShowMessageAsync(LocalizationService.Text("Failed"), MetadataErrorText.For(error));
                }
            }
            finally
            {
                EndScan(scanVersion, cancellation);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            await ShowMessageAsync(LocalizationService.Text("Failed"), MetadataErrorText.For(error));
        }
    }

    private static readonly string[] MediaExtensions =
        [".mkv", ".mp4", ".avi", ".mov", ".m4v", ".wmv"];

    /// <summary>Starts a folder scan, or requests cancellation of the running scan.</summary>
    private async void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isScanning)
        {
            _cancellation?.Cancel();
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

        if (string.IsNullOrWhiteSpace(root))
        {
            await ShowMessageAsync(
                LocalizationService.Text("AddMedia"),
                LocalizationService.Text("FolderPathPlaceholder"));
            return;
        }

        var (cancellation, scanVersion) = BeginScan();
        var token = cancellation.Token;

        try
        {
            var session = await Task.Run(() => AppServices.Import.PrepareImportReview(
                root,
                recursive: true,
                progress: update => DispatcherQueue.TryEnqueue(() =>
                {
                    if (IsCurrentScan(scanVersion, cancellation))
                    {
                        ReportScanProgress(update);
                    }
                }),
                isCancelled: () => token.IsCancellationRequested));

            if (!IsCurrentScan(scanVersion, cancellation))
            {
                return;
            }

            if (token.IsCancellationRequested)
            {
                _state = AddMediaScanState.Idle;
                return;
            }

            SetDetected(session.Items);
            _state = _detected.Count == 0 ? AddMediaScanState.Idle : AddMediaScanState.Results;

            if (_detected.Count == 0)
            {
                EmptyState.Title = LocalizationService.Text("NothingDetected");
                EmptyState.Message = LocalizationService.Text("NothingDetectedHelp");
            }
        }
        catch (OperationCanceledException)
        {
            if (IsCurrentScan(scanVersion, cancellation))
            {
                _state = AddMediaScanState.Idle;
            }
        }
        catch (Exception error)
        {
            if (IsCurrentScan(scanVersion, cancellation))
            {
                _state = AddMediaScanState.Idle;
                await ShowMessageAsync(LocalizationService.Text("Failed"), MetadataErrorText.For(error));
            }
        }
        finally
        {
            EndScan(scanVersion, cancellation);
        }
    }

    private (CancellationTokenSource Cancellation, long ScanVersion) BeginScan()
    {
        _cancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        var scanVersion = ++_scanVersion;
        _isScanning = true;
        SetDetected([]);
        _state = AddMediaScanState.Scanning;
        ScanProgressBar.Maximum = 1;
        ScanProgressBar.Value = 0;
        Refresh();
        return (cancellation, scanVersion);
    }

    private void EndScan(long scanVersion, CancellationTokenSource cancellation)
    {
        if (!IsCurrentScan(scanVersion, cancellation))
        {
            return;
        }

        _isScanning = false;
        _cancellation = null;
        cancellation.Dispose();
        Refresh();
    }

    private bool IsCurrentScan(long scanVersion, CancellationTokenSource cancellation) =>
        scanVersion == _scanVersion && ReferenceEquals(_cancellation, cancellation);

    private void ReportScanProgress(ImportReviewProgress update)
    {
        ScanProgressBar.Maximum = Math.Max(1, update.FilesDiscovered);
        ScanProgressBar.Value = Math.Min(update.FilesProcessed, ScanProgressBar.Maximum);
        ScanProgressText.Text = LocalizationService.Format(
            "CheckedOfFormat",
            update.FilesProcessed,
            Math.Max(update.FilesDiscovered, update.FilesProcessed));
    }

    private async void AddSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isAdding)
        {
            return;
        }

        var selections = _rows
            .Where(row => row.CanSelect && row.IsSelected)
            .Select(row => new ImportSelection(row.Item))
            .ToArray();

        if (selections.Length == 0)
        {
            return;
        }

        _isAdding = true;
        var metadataConfigured = false;
        try
        {
            metadataConfigured = AppServices.Metadata.IsConfigured;
        }
        catch
        {
            // The import service will still report the authoritative outcome. This only controls
            // the transient label while the asynchronous operation is running.
        }

        // Freeze the entire review while the captured selection is being processed. Otherwise an
        // unsubmitted checkbox change would make the visible count disagree with the operation in
        // flight, even though the application correctly uses the immutable captured selection.
        foreach (var row in _rows)
        {
            row.SetImporting(true);
        }

        foreach (var row in _rows.Where(row => selections.Any(selection =>
            string.Equals(selection.Item.Path, row.Item.Path, StringComparison.OrdinalIgnoreCase))))
        {
            row.SetImportStatus(metadataConfigured
                ? ImportItemStatus.Matching
                : ImportItemStatus.MetadataUnavailable);
        }
        Refresh();
        ImportBatchResult result;

        try
        {
            result = await Task.Run(() => AppServices.Import.ImportSelectedAsync(selections));
        }
        catch (Exception error)
        {
            _isAdding = false;
            foreach (var row in _rows)
            {
                row.SetImporting(false);
            }
            Refresh();
            await ShowMessageAsync(LocalizationService.Text("Failed"), MetadataErrorText.For(error));
            return;
        }
        finally
        {
            _isAdding = false;
            foreach (var row in _rows)
            {
                row.SetImporting(false);
            }
        }

        foreach (var item in result.Items)
        {
            var row = _rows.FirstOrDefault(candidate =>
                string.Equals(candidate.Item.Path, item.Path, StringComparison.OrdinalIgnoreCase));
            row?.SetImportStatus(item.Status);
        }

        MediaRegistered?.Invoke(this, EventArgs.Empty);
        Refresh();

        var summaryLines = new List<string>
        {
            LocalizationService.Format("ImportAddedFormat", result.AddedCount),
            LocalizationService.Format("ImportMatchedFormat", result.MatchedCount),
            LocalizationService.Format("ImportNeedsMatchFormat", result.NeedsMatchCount),
            LocalizationService.Format("ImportMetadataUnavailableFormat", result.MetadataUnavailableCount),
            LocalizationService.Format("ImportMetadataErrorsFormat", result.MetadataErrorsCount),
            LocalizationService.Format("ImportExistingFormat", result.ExistingCount),
            LocalizationService.Format("ImportRegistrationErrorsFormat", result.RegistrationFailuresCount),
        };

        if (result.MetadataCancelledCount > 0)
        {
            summaryLines.Add(LocalizationService.Format(
                "ImportMetadataCancelledFormat", result.MetadataCancelledCount));
        }

        foreach (var kind in result.Items
            .Where(item => item.MetadataFailureKind is not null)
            .Select(item => item.MetadataFailureKind!.Value)
            .Distinct())
        {
            summaryLines.Add(LocalizationService.Text(MetadataErrorKey.For(kind)));
        }

        await ShowMessageAsync(
            LocalizationService.Text("AddMedia"),
            string.Join(Environment.NewLine, summaryLines));

        SetDetected([]);
        _state = AddMediaScanState.Idle;
        Refresh();
    }

    /// <summary>
    /// Parses picked files through the same discovery service the folder scan uses: each file's own
    /// directory is scanned without recursion and the results are filtered back to what was picked, so
    /// one parser decides what a name means.
    /// </summary>
    private static IReadOnlyList<DiscoveredMedia> DiscoverPicked(
        IReadOnlyList<string> paths,
        Func<bool>? isCancelled = null)
    {
        var wanted = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        var results = new List<DiscoveredMedia>();

        foreach (var directory in paths
            .Select(Path.GetDirectoryName)
            .Where(directory => !string.IsNullOrEmpty(directory))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (isCancelled?.Invoke() == true)
            {
                throw new OperationCanceledException();
            }

            var found = AppServices.Import.PrepareImportReview(
                directory!,
                recursive: false,
                isCancelled: isCancelled);
            results.AddRange(found.Items.Where(item => wanted.Contains(item.Path)));
        }

        return results;
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
            await ShowMessageAsync(LocalizationService.Text("Failed"), MetadataErrorText.For(error));
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
