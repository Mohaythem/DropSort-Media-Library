using System.Collections.ObjectModel;
using DropSort.Application.Dto;
using DropSort.Domain.Media.Discovery;
using DropSort.Domain.Media.Matcher;
using DropSort.Domain.Metadata.Contracts;
using DropSort.UI.Models;
using DropSort.UI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
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
public sealed partial class AddMediaPage : Page, ILocalizableView, IActivatableView, IDeactivatableView
{
    private AddMediaScanState _state = AddMediaScanState.Idle;
    private IReadOnlyList<DiscoveredMedia> _detected = [];
    private readonly ObservableCollection<DetectedMediaDisplayRecord> _rows = [];
    private bool _isScanning;
    private bool _isAdding;
    private bool _isReviewingMetadata;
    private long _scanVersion;
    private long _metadataVersion;
    private CancellationTokenSource? _metadataCancellation;
    private readonly Dictionary<DetectedMediaDisplayRecord, CancellationTokenSource> _retryCancellations = [];

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

    public void Deactivate()
    {
        _cancellation?.Cancel();
        CancelMetadataReview();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        Deactivate();
        base.OnNavigatedFrom(e);
    }

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
        ColumnPreviewText.Text = LocalizationService.Text("MatchedTitle");
        ColumnDetailsText.Text = LocalizationService.Text("Details");
        ColumnStatusText.Text = LocalizationService.Text("Status");
        ColumnActionText.Text = LocalizationService.Text("Action");
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
        AddFilesButton.IsEnabled = !_isScanning && !_isAdding && !_isReviewingMetadata;
        BrowseButton.IsEnabled = !_isScanning && !_isAdding && !_isReviewingMetadata;
        CancelButton.Visibility = _isScanning || (_state == AddMediaScanState.Results && !_isAdding)
            ? Visibility.Visible
            : Visibility.Collapsed;
        ScanButton.IsEnabled = !_isAdding && !_isReviewingMetadata;

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

        var selectable = _rows.Count(row => row.CanAdd);
        var selected = _rows.Count(row => row.CanAdd && row.IsSelected);
        ResultsCountText.Text = LocalizationService.Format("ItemsFoundFormat", _rows.Count)
            + " · " + LocalizationService.Format("SelectedItemsFormat", selected);
        var metadataPending = _rows.Any(row => row.CanAdd && row.Status == ImportItemStatus.Matching);
        AddSelectedButton.IsEnabled = selected > 0 && !_isAdding && !_isReviewingMetadata && !metadataPending;
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
        foreach (var row in _rows.Where(row => row.CanAdd))
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
        CancelMetadataReview();
        SetDetected([]);
        _state = AddMediaScanState.Idle;
        if (!wasScanning)
        {
            _isScanning = false;
        }
        Refresh();
    }

    /// <summary>
    /// Picks a transient scan path. Approved media roots are managed in Settings so a one-off scan
    /// cannot silently replace an existing movie or TV root.
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
                if (_detected.Count > 0)
                {
                    BeginMetadataReview();
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
            if (_detected.Count > 0)
            {
                BeginMetadataReview();
            }

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
        CancelMetadataReview();
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

    /// <summary>
    /// Runs the pure metadata review after discovery. No catalog transaction is opened here; the
    /// staged previews are copied into ImportSelection only when Add Selected is confirmed.
    /// </summary>
    private void BeginMetadataReview()
    {
        CancelMetadataReview();
        var cancellation = new CancellationTokenSource();
        _metadataCancellation = cancellation;
        var version = ++_metadataVersion;
        _isReviewingMetadata = true;

        Refresh();
        _ = ReviewMetadataAsync(version, cancellation);
    }

    private async Task ReviewMetadataAsync(long version, CancellationTokenSource cancellation)
    {
        try
        {
            // Keep provider calls sequential during review. This avoids turning a large scan into a
            // burst that trips TMDB rate limits while still keeping the UI responsive via async I/O.
            foreach (var row in _rows.Where(row => row.CanAdd).ToArray())
            {
                cancellation.Token.ThrowIfCancellationRequested();

                bool isRegistered;
                try
                {
                    // Catalog reads are synchronous at the application boundary. Run each one off
                    // the UI thread so a large scan or a busy SQLite writer cannot freeze the page.
                    isRegistered = await Task.Run(
                        () => AppServices.Import.IsMediaFileRegistered(row.Item.Path),
                        cancellation.Token);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception error)
                {
                    // A failed identity check is not proof that the file is new. Keep the row
                    // visible for an explicit retry, but never send it to TMDB on that assumption.
                    row.SetMetadataPreview(new ImportMetadataPreview(
                        ImportItemStatus.MetadataError,
                        Failure: MetadataErrorText.For(error)));
                    Refresh();
                    continue;
                }

                if (isRegistered)
                {
                    row.SetAlreadyInLibrary();
                    Refresh();
                    continue;
                }

                row.SetMetadataReviewing();
                Refresh();
                await ReviewMetadataRowAsync(row, cancellation.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Cancellation is an expected lifecycle event (Cancel, rescan, or page deactivation).
        }
        finally
        {
            if (version == _metadataVersion && ReferenceEquals(_metadataCancellation, cancellation))
            {
                _isReviewingMetadata = false;
                _metadataCancellation = null;
                Refresh();
            }

            // The owner may have cancelled and started a newer review. Dispose only this completed
            // operation's source; CancelMetadataReview never disposes an in-flight source.
            cancellation.Dispose();
        }
    }

    private async Task ReviewMetadataRowAsync(DetectedMediaDisplayRecord row, CancellationToken token)
    {
        try
        {
            // PreviewMetadataAsync performs its canonical path check before its first await. Keep
            // that synchronous application read (and the provider call that follows) off the UI
            // thread; the continuation returns to the page context for row updates.
            var preview = await Task.Run(
                () => AppServices.Import.PreviewMetadataAsync(row.Item, token),
                token);
            if (!token.IsCancellationRequested)
            {
                if (preview.Status == ImportItemStatus.AlreadyInLibrary)
                {
                    row.SetAlreadyInLibrary();
                }
                else
                {
                    row.SetMetadataPreview(preview);
                }
                Refresh();
            }
        }
        catch (OperationCanceledException)
        {
            // The parent review is being cancelled; do not resurrect rows after the user closed it.
        }
        catch (Exception error)
        {
            if (!token.IsCancellationRequested)
            {
                row.SetMetadataPreview(new ImportMetadataPreview(
                    ImportItemStatus.MetadataError,
                    Failure: MetadataErrorText.For(error)));
                Refresh();
            }
        }
    }

    private void CancelMetadataReview()
    {
        _metadataVersion++;
        _metadataCancellation?.Cancel();
        _metadataCancellation = null;
        _isReviewingMetadata = false;
        foreach (var row in _rows)
        {
            row.SetMetadataCancelled();
        }
        foreach (var retry in _retryCancellations.Values)
        {
            retry.Cancel();
        }
    }

    private static DetectedMediaDisplayRecord? RowFromSender(object sender) =>
        (sender as FrameworkElement)?.Tag as DetectedMediaDisplayRecord;

    private async void RetryMetadata_Click(object sender, RoutedEventArgs e)
    {
        var row = RowFromSender(sender);
        if (row is null || _isAdding || !row.CanAdd || _retryCancellations.ContainsKey(row))
        {
            return;
        }

        var cancellation = new CancellationTokenSource();
        _retryCancellations[row] = cancellation;
        row.SetMetadataReviewing();
        Refresh();
        try
        {
            // RetryMetadataAsync delegates to PreviewMetadataAsync, whose canonical path check is
            // synchronous before its first await. Run the complete retry off the UI thread as well.
            var preview = await Task.Run(
                () => AppServices.Import.RetryMetadataAsync(row.Item, cancellation.Token),
                cancellation.Token);
            if (!cancellation.IsCancellationRequested && _rows.Contains(row))
            {
                if (preview.Status == ImportItemStatus.AlreadyInLibrary)
                {
                    row.SetAlreadyInLibrary();
                }
                else
                {
                    row.SetMetadataPreview(preview);
                }
                Refresh();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            if (!cancellation.IsCancellationRequested && _rows.Contains(row))
            {
                row.SetMetadataPreview(new ImportMetadataPreview(
                    ImportItemStatus.MetadataError,
                    Failure: MetadataErrorText.For(error)));
                Refresh();
            }
        }
        finally
        {
            if (_retryCancellations.Remove(row, out var active))
            {
                active.Dispose();
            }
            Refresh();
        }
    }

    private async void ManualMatch_Click(object sender, RoutedEventArgs e)
    {
        var row = RowFromSender(sender);
        if (row is null || _isAdding || !row.CanAdd)
        {
            return;
        }

        try
        {
            var year = row.Item.ParsedMedia?.Year;
            if (row.Item.Classification == DiscoveryClassification.MovieCandidate)
            {
                var movie = await MatchMediaDialog.ShowForMovieAsync(this, row.Title, year);
                if (movie is not null)
                {
                    row.SetMetadataPreview(new ImportMetadataPreview(
                        ImportItemStatus.Matched,
                        MatchStatus.Matched,
                        MovieCandidate: movie,
                        IsManual: true));
                }
            }
            else
            {
                var show = await MatchMediaDialog.ShowForTvShowAsync(this, row.Title, year);
                if (show is not null)
                {
                    row.SetMetadataPreview(new ImportMetadataPreview(
                        ImportItemStatus.Matched,
                        MatchStatus.Matched,
                        TvCandidate: show,
                        IsManual: true));
                }
            }
            Refresh();
        }
        catch (Exception error)
        {
            await ShowMessageAsync(LocalizationService.Text("Failed"), MetadataErrorText.For(error));
        }
    }

    private async void AddSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isAdding)
        {
            return;
        }

        var selections = _rows
            .Where(row => row.CanAdd && row.IsSelected)
            .Select(row => new ImportSelection(row.Item, row.MetadataPreview))
            .ToArray();

        if (selections.Length == 0)
        {
            return;
        }

        _isAdding = true;
        // Freeze the entire review while the captured selection is being processed. Otherwise an
        // unsubmitted checkbox change would make the visible count disagree with the operation in
        // flight, even though the application correctly uses the immutable captured selection.
        foreach (var row in _rows)
        {
            row.SetImporting(true);
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
            LocalizationService.Format("ImportNeedsAttentionFormat", result.NeedsAttentionCount),
        };

        // The three lines above are the result users need every time. Only append actionable
        // failure detail when a failure actually occurred; zero-value implementation categories
        // made a successful import look like an accounting report.
        if (result.RegistrationFailuresCount > 0)
        {
            summaryLines.Add(LocalizationService.Format(
                "ImportRegistrationErrorsFormat", result.RegistrationFailuresCount));
        }

        if (result.ExistingCount > 0)
        {
            summaryLines.Add(LocalizationService.Format(
                "ImportExistingFormat", result.ExistingCount));
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

            // Use the same selectable, ranked result surface as per-row Match Metadata. The global
            // card has no library row to mutate, so Apply confirms the chosen result and reports it
            // without inventing a registration side effect.
            var selected = await MatchMediaDialog.ShowForMovieAsync(this, title, null);
            if (selected is not null)
            {
                await ShowMessageAsync(
                    LocalizationService.Text("SearchTmdbHeading"),
                    LocalizationService.Format(
                        "MatchSelectedFormat",
                        selected.Title + (selected.Year is null ? string.Empty : $" ({selected.Year})")));
            }
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
