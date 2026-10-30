using System.Collections.ObjectModel;
using DropSort.Application.Dto;
using DropSort.Application.Services;
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
    private readonly ObservableCollection<object> _visibleRows = [];
    private readonly ObservableCollection<DetectedMediaDisplayRecord> _registeredRows = [];
    private readonly HashSet<DetectedMediaDisplayRecord> _visibleMembers = [];
    private readonly HashSet<DetectedMediaDisplayRecord> _registeredMembers = [];
    private readonly AddMediaReviewCounts<DetectedMediaDisplayRecord> _reviewCounts = new();
    private AddMediaReviewFilter _reviewFilter;
    private bool _batchPresentation;
    private IReadOnlyList<TvSeriesSuggestion> _seriesSuggestions = [];
    private bool _isScanning;
    private bool _isAdding;
    private bool _isReviewingMetadata;
    private bool _isPickingFolder;
    private long _scanVersion;
    private long _metadataVersion;
    private CancellationTokenSource? _metadataCancellation;
    private readonly Dictionary<DetectedMediaDisplayRecord, CancellationTokenSource> _retryCancellations = [];

    // Real processed/total counts for the two long-running phases, so the status line shows honest
    // progress instead of a fake bar. Matching counts the review pass; adding counts the bulk registration.
    private int _matchProcessed;
    private int _matchTotal;
    private int _addProcessed;
    private int _addTotal;

    // The last completed add, kept only to render the transient "Done" phase line until the next action.
    private bool _addCompleted;
    private int _lastAddedCount;
    private int _lastAttentionCount;

    // Visual-only column sort. Clicking a header sorts the rows; it never changes selection, match state,
    // or the catalog. None means the natural discovery order.
    private enum SortColumn { None, Title, Type, Status }
    private SortColumn _sortColumn = SortColumn.None;
    private bool _sortDescending;

    /// <summary>
    /// Cancellation for the running scan. A CancellationTokenSource is the safe way to hand a stop
    /// signal to a worker thread; a plain bool field is not guaranteed to be observed there.
    /// </summary>
    private CancellationTokenSource? _cancellation;

    public AddMediaPage()
    {
        this.InitializeComponent();
        Root.ItemsSource = _visibleRows;
        _rows.CollectionChanged += Rows_CollectionChanged;
        ReviewFilterBox.SelectedIndex = 0;
        FolderPathBox.Text = AppSettingsStore.MovieFolder ?? AppSettingsStore.ShowFolder ?? string.Empty;
        ApplyLocalization();
    }

    /// <summary>Raised after media is registered so the shell can refresh the library.</summary>
    public event EventHandler? MediaRegistered;

    public void Activate() => Refresh();

    public void Deactivate()
    {
        _presentationVersion++;
        _confirmationCancelled = true;
        _importCancellation?.Cancel();
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
        SourceHelpText.Text = LocalizationService.Text("AddMediaScanHelp");
        FolderPathBox.PlaceholderText = LocalizationService.Text("FolderPathPlaceholder");
        FolderLabelText.Text = LocalizationService.Text("MediaFolderLabel");
        BrowseButton.Content = LocalizationService.Text("Browse");
        AddFilesButton.Content = LocalizationService.Text("AddFiles");
        CancelButton.Content = LocalizationService.Text("CancelReview");
        LocalOnlyBox.Content = LocalizationService.Text("TvBatchLocalOnly");
        ResultsHeadingText.Text = LocalizationService.Text("ReviewMedia");
        SelectAllButton.Content = LocalizationService.Text("SelectAll");
        SelectReadyButton.Content = LocalizationService.Text("SelectReady");
        DeselectAllButton.Content = LocalizationService.Text("DeselectAll");
        ColumnSelectText.Text = LocalizationService.Text("Select");
        ColumnTypeText.Text = LocalizationService.Text("MediaType");
        ColumnTitleText.Text = LocalizationService.Text("ImportItemColumn");
        AllNewFilterItem.Content = LocalizationService.Text("ImportAllNew");
        ReadyFilterItem.Content = LocalizationService.Text("ImportReady");
        AttentionFilterItem.Content = LocalizationService.Text("ImportAttention");
        // Refresh the selected ComboBox presenter after an in-place language change.
        var selectedFilter = ReviewFilterBox.SelectedIndex;
        ReviewFilterBox.SelectedIndex = -1;
        ReviewFilterBox.SelectedIndex = selectedFilter;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(ReviewFilterBox, LocalizationService.Text("ReviewMedia"));
        ColumnStatusText.Text = LocalizationService.Text("ImportStateColumn");
        ColumnActionText.Text = LocalizationService.Text("Action");
        EmptyState.Title = LocalizationService.Text("NothingDetected");
        EmptyState.Message = LocalizationService.Text("NothingDetectedHelp");
        foreach (var row in _rows)
        {
            row.RefreshLocalization();
        }
        UpdateSeriesSuggestion();
        Refresh();
    }

    private void Refresh()
    {
        ScanButton.Content = LocalizationService.Text(
            _isScanning ? "Cancel" : "ScanFolder");
        AddFilesButton.IsEnabled = !_isScanning && !_isAdding && !_isReviewingMetadata;
        BrowseButton.IsEnabled = !_isScanning && !_isAdding && !_isReviewingMetadata && !_isPickingFolder;
        CancelButton.Content = LocalizationService.Text(_isAdding ? "Cancel" : "CancelReview");
        LocalOnlyBox.IsEnabled = !_isScanning && !_isAdding && !_isReviewingMetadata;
        FolderPathBox.IsEnabled = !_isScanning && !_isAdding && !_isReviewingMetadata && !_isPickingFolder;
        if (!_isAdding && !_isReviewingMetadata) RefreshGroupHeaders();
        CancelButton.Visibility = _isScanning || _isAdding || (_state == AddMediaScanState.Results)
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

        var selectable = _reviewCounts.Eligible;
        var selected = _reviewCounts.Selected;
        SummaryText.Visibility = _isReviewingMetadata ? Visibility.Collapsed : Visibility.Visible;
        SummaryText.Text = LocalizationService.Format("ImportSummaryFormat",
            _reviewCounts.NewFiles, _reviewCounts.Eligible, _reviewCounts.Attention);
        SelectionFeedbackText.Text = LocalizationService.Format("ImportSelectionGlobalFormat", selected);
        RegisteredSection.Header = LocalizationService.Format("ImportRegisteredFilesFormat", _reviewCounts.Registered);
        RegisteredSection.Visibility = _reviewCounts.Registered > 0 ? Visibility.Visible : Visibility.Collapsed;
        QueueEmptyText.Visibility = _visibleRows.Count == 0 && !_isReviewingMetadata ? Visibility.Visible : Visibility.Collapsed;
        QueueEmptyText.Text = LocalizationService.Text(_reviewCounts.NewFiles == 0 ? "ImportNoNew" : "ImportNoFilterResults");
        ReviewFilterBox.IsEnabled = !_isAdding && !_isReviewingMetadata;

        // The status line names the current phase (Matching / Ready / Adding / Done) with real counts, so
        // the user always knows whether item counts are still changing in the background.
        ResultsCountText.Text = CurrentPhaseStatus(selected);

        // While matching is still running the counts are not final, so every control whose meaning
        // depends on the final matched set is disabled until the review pass completes (Ready).
        var matching = _isReviewingMetadata;
        var busy = _isAdding || matching;
        AddSelectedButton.IsEnabled = selected > 0 && !busy;
        AddSelectedButton.Content = LocalizationService.Format("AddSelectedFormat", selected);
        SelectAllButton.IsEnabled = selectable > 0 && !busy;
        var ready = _reviewCounts.Ready;
        SelectReadyButton.IsEnabled = ready > 0 && !busy;
        DeselectAllButton.IsEnabled = selected > 0 && !busy;

        // Header sorting is disabled while adding (rows are being removed) but allowed while matching:
        // sorting is visual only and never touches match state.
        var canSort = !_isAdding;
        SortTitleButton.IsEnabled = canSort;
        SortTypeButton.IsEnabled = canSort;
        SortStatusButton.IsEnabled = canSort;
    }

    /// <summary>
    /// The phase-aware status line: Matching / Ready / Adding / Done with real processed-of-total counts.
    /// Scanning has its own progress panel, so it is not represented here.
    /// </summary>
    private string CurrentPhaseStatus(int selected)
    {
        if (_isReviewingMetadata)
        {
            return LocalizationService.Format("ImportReviewPhaseFormat", _matchProcessed, _matchTotal);
        }

        if (_isAdding)
        {
            return LocalizationService.Format("AddingProgressFormat", _addProcessed, _addTotal);
        }

        if (_addCompleted)
        {
            return LocalizationService.Format("ImportDoneFormat", _lastAddedCount, _lastAttentionCount);
        }

        // Ready: matching has settled and the counts are final.
        return LocalizationService.Format(
            "ReadyPhaseFormat",
            LocalizationService.Format("ItemsFoundFormat", _reviewCounts.NewFiles),
            LocalizationService.Format("SelectedItemsFormat", selected));
    }

    private void SetDetected(IReadOnlyList<DiscoveredMedia> items, string? analysisRoot = null)
    {
        foreach (var row in _rows)
        {
            row.PropertyChanged -= SelectionChanged;
        }

        _batchPresentation = true;
        RegisteredSection.IsExpanded = false;
        _detected = items;
        _rows.Clear();
        _tvHeaders.Clear();
        var analyses = AnalyzeDetected(items, analysisRoot);
        foreach (var item in items)
        {
            analyses.TryGetValue(item.Path, out var analysis);
            var row = new DetectedMediaDisplayRecord(analysis.Candidate?.CurrentItem ?? item, analysis.Candidate, analysis.Provenance);
            row.PropertyChanged += SelectionChanged;
            _rows.Add(row);
        }

        _batchPresentation = false;
        RebuildVisibleQueue();

        // A fresh detection set starts in the natural discovery order and clears the transient "Done"
        // line from a previous add.
        _sortColumn = SortColumn.None;
        _sortDescending = false;
        _addCompleted = false;
        UpdateSortIndicators();
        UpdateSeriesSuggestion();
    }

    /// <summary>
    /// Recomputes the probable-TV-series suggestion from the current detection set and drives the banner.
    /// The grouping is a suggestion only: the user chooses "Treat all as TV" or dismisses it.
    /// </summary>
    private void UpdateSeriesSuggestion()
    {
        _seriesSuggestions = _detected.Count == 0
            ? []
            : TvSeriesGrouping.Detect(_detected);

        var fileCount = _seriesSuggestions.Sum(suggestion => suggestion.FileCount);
        var hasSuggestion = fileCount > 0;
        SeriesSuggestionBar.Title = LocalizationService.Text("PossibleTvSeriesTitle");
        SeriesSuggestionBar.Message = LocalizationService.Format("PossibleTvSeriesFormat", fileCount);
        TreatAllAsTvButton.Content = LocalizationService.Text("TreatAllAsTv");
        SeriesSuggestionBar.IsOpen = hasSuggestion;
        SeriesSuggestionBar.Visibility = hasSuggestion ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Applies every probable-series group as TV episodes (season 1) and re-runs the review.</summary>
    private void TreatAllAsTv_Click(object sender, RoutedEventArgs e)
    {
        if (_isAdding || _seriesSuggestions.Count == 0)
        {
            return;
        }

        var replacements = new Dictionary<string, DiscoveredMedia>(StringComparer.OrdinalIgnoreCase);
        foreach (var suggestion in _seriesSuggestions)
        {
            foreach (var guess in suggestion.Episodes)
            {
                replacements[guess.Item.Path] =
                    TvSeriesGrouping.AsTvEpisode(guess.Item, suggestion.BaseTitle, season: 1, guess.Episode);
            }
        }

        ReclassifyRows(replacements);
    }

    /// <summary>Flips one row between Movie and TV episode, then re-runs the review for the new type.</summary>
    private void OverrideType_Click(object sender, RoutedEventArgs e)
    {
        var row = RowFromSender(sender);
        if (row is null || _isAdding || !row.CanOverrideType)
        {
            return;
        }

        var item = row.Item;
        DiscoveredMedia replacement;
        if (item.Classification == DiscoveryClassification.MovieCandidate)
        {
            _ = CorrectTvRowsAsync([row], singleFile: true);
            return;
        }
        else
        {
            replacement = TvSeriesGrouping.AsMovie(item);
        }

        ReclassifyRows(new Dictionary<string, DiscoveredMedia>(StringComparer.OrdinalIgnoreCase)
        {
            [item.Path] = replacement,
        });
    }

    /// <summary>
    /// Replaces the given rows (by path) with reclassified discovered items, preserving the order and the
    /// other rows, then recomputes the suggestion and re-runs the metadata review so the new types are
    /// matched against the right catalog (movie vs TV). Selection subscriptions are moved to the new rows.
    /// </summary>
    private void ReclassifyRows(IReadOnlyDictionary<string, DiscoveredMedia> replacements)
    {
        if (replacements.Count == 0)
        {
            return;
        }

        _batchPresentation = true;
        for (var index = 0; index < _rows.Count; index++)
        {
            if (!replacements.TryGetValue(_rows[index].Item.Path, out var newItem))
            {
                continue;
            }

            _rows[index].PropertyChanged -= SelectionChanged;
            var candidate = newItem.Classification == DiscoveryClassification.TvEpisodeCandidate
                ? new TvBatchAnalysisService().Analyze(Path.GetDirectoryName(newItem.Path)!, [newItem]).AllFiles.FirstOrDefault() : null;
            var replacement = new DetectedMediaDisplayRecord(newItem, candidate, Path.GetDirectoryName(newItem.Path));
            replacement.PropertyChanged += SelectionChanged;
            _rows[index] = replacement;
        }

        _batchPresentation = false;
        RebuildVisibleQueue();
        _detected = _rows.Select(row => row.Item).ToList();
        UpdateSeriesSuggestion();
        BeginMetadataReview();
        Refresh();
    }

    private void SelectionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not DetectedMediaDisplayRecord row ||
            e.PropertyName is not (nameof(DetectedMediaDisplayRecord.IsSelected) or nameof(DetectedMediaDisplayRecord.Status))) return;
        _reviewCounts.Update(row, row.ReviewFacts);
        // Selection does not change filter membership. Keep the native control and keyboard focus.
        if (!_batchPresentation && !_isReviewingMetadata && e.PropertyName == nameof(DetectedMediaDisplayRecord.Status)) SynchronizeRow(row);
        if (e.PropertyName == nameof(DetectedMediaDisplayRecord.IsSelected)) _addCompleted = false;
        if (!_batchPresentation) Refresh();
    }

    private void Rows_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
        {
            _reviewCounts.Clear();
            _visibleRows.Clear();
            _registeredRows.Clear();
            _visibleMembers.Clear();
            _registeredMembers.Clear();
        }
        else if (e.Action != System.Collections.Specialized.NotifyCollectionChangedAction.Move)
        {
            if (e.OldItems is not null)
                foreach (DetectedMediaDisplayRecord row in e.OldItems)
                {
                    _reviewCounts.Remove(row);
                    if (_visibleMembers.Remove(row)) _visibleRows.Remove(row);
                    if (_registeredMembers.Remove(row)) _registeredRows.Remove(row);
                }
            if (e.NewItems is not null)
                foreach (DetectedMediaDisplayRecord row in e.NewItems)
                {
                    _reviewCounts.Update(row, row.ReviewFacts);
                    if (!_batchPresentation && !_isReviewingMetadata) SynchronizeRow(row);
                }
        }
        if (!_batchPresentation && e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Move)
            RebuildVisibleQueue();
    }

    private void SynchronizeRow(DetectedMediaDisplayRecord row)
    {
        if (_rows.Any(r => r.IsTv)) { RebuildVisibleQueue(); return; }
        var show = row.ReviewFacts.Matches(_reviewFilter);
        if (show && _visibleMembers.Add(row))
        {
            // Only a membership transition needs an index walk; normal progress updates do not.
            var index = 0;
            foreach (var candidate in _rows)
            {
                if (ReferenceEquals(candidate, row)) break;
                if (_visibleMembers.Contains(candidate)) index++;
            }
            _visibleRows.Insert(index, row);
        }
        else if (!show && _visibleMembers.Remove(row)) _visibleRows.Remove(row);
        if (row.ReviewFacts.Registered && _registeredMembers.Add(row)) _registeredRows.Add(row);
        else if (!row.ReviewFacts.Registered && _registeredMembers.Remove(row)) _registeredRows.Remove(row);
    }

    private void RebuildVisibleQueue()
    {
        _visibleRows.Clear();
        _visibleMembers.Clear();
        _registeredRows.Clear();
        _registeredMembers.Clear();
        foreach (var row in _rows)
        {
            if (row.ReviewFacts.Matches(_reviewFilter)) _visibleMembers.Add(row);
            if (row.ReviewFacts.Registered) { _registeredRows.Add(row); _registeredMembers.Add(row); }
        }
        BuildGroupedQueue();
    }

    private void ReviewFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Root is null || ReviewFilterBox.SelectedIndex < 0) return;
        _reviewFilter = (AddMediaReviewFilter)ReviewFilterBox.SelectedIndex;
        RebuildVisibleQueue();
        Refresh();
    }

    private void RegisteredSection_Expanding(Expander sender, ExpanderExpandingEventArgs args) =>
        RegisteredMediaRepeater.ItemsSource = _registeredRows;

    private void RegisteredSection_Collapsed(Expander sender, ExpanderCollapsedEventArgs args) =>
        RegisteredMediaRepeater.ItemsSource = null;

    private void PageRoot_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var padding = e.NewSize.Width < 600 ? 16 : 32;
        var narrow = e.NewSize.Width - 2 * padding < 760;
        // The ScrollViewer can measure its child beyond the viewport. Constrain the whole table,
        // rather than relying on MaxWidth + Stretch, which can arrange columns outside the window.
        ContentHost.Width = Math.Min(
            (double)Microsoft.UI.Xaml.Application.Current.Resources["AddMediaContentMaxWidth"],
            Math.Max(0, e.NewSize.Width - 2 * padding));
        // Minimal navigation places its native toggle over the page at small shell widths.
        Root.Padding = new Thickness(padding, e.NewSize.Width < 640 ? 56 : 32, padding, padding);
        SourceActions.Orientation = ContentHost.Width < 360 ? Orientation.Vertical : Orientation.Horizontal;
        PrimaryActions.Orientation = ContentHost.Width < 440 ? Orientation.Vertical : Orientation.Horizontal;
        SelectionActions.Orientation = e.NewSize.Width < 480 ? Orientation.Vertical : Orientation.Horizontal;
        SortActions.Orientation = e.NewSize.Width < 480 ? Orientation.Vertical : Orientation.Horizontal;
        Grid.SetColumn(SelectionActions, narrow ? 0 : 1);
        Grid.SetColumnSpan(SelectionActions, narrow ? 2 : 1);
        Grid.SetRow(SelectionActions, narrow ? 1 : 0);
        Grid.SetRow(ReviewFilterBox, narrow ? 2 : 1);
        Grid.SetColumn(SelectionFeedbackText, narrow ? 0 : 1);
        Grid.SetColumnSpan(SelectionFeedbackText, narrow ? 2 : 1);
        Grid.SetRow(SelectionFeedbackText, narrow ? 3 : 1);
        Grid.SetColumnSpan(PrimaryActions, narrow ? 2 : 1);
        TableHeader.ColumnDefinitions[0].Width = new GridLength(narrow ? 0 : 40);
        TableHeader.ColumnDefinitions[2].Width = narrow ? GridLength.Auto : new GridLength(210);
        TableHeader.ColumnDefinitions[3].Width = new GridLength(narrow ? 0 : 150);
        ColumnSelectText.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
        ColumnActionText.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ReviewContainer_ContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue) return;
        BindRowAutomationName(args);
        // Native containers must use the same bounded width as the header. Binding also updates
        // already-realized rows during shell reflow, without replacing their controls or state.
        args.ItemContainer.SetBinding(FrameworkElement.WidthProperty, new Microsoft.UI.Xaml.Data.Binding
        {
            Source = ContentHost,
            Path = new PropertyPath(nameof(FrameworkElement.Width)),
            Mode = Microsoft.UI.Xaml.Data.BindingMode.OneWay,
        });
    }

    private void RegisteredContainer_ContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (!args.InRecycleQueue) BindRowAutomationName(args);
    }

    private static void BindRowAutomationName(ContainerContentChangingEventArgs args) =>
        args.ItemContainer.SetBinding(Microsoft.UI.Xaml.Automation.AutomationProperties.NameProperty,
            new Microsoft.UI.Xaml.Data.Binding
            {
                Source = args.Item,
                Path = new PropertyPath(nameof(DetectedMediaDisplayRecord.AutomationName)),
                Mode = Microsoft.UI.Xaml.Data.BindingMode.OneWay,
            });

    private void ReviewRow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not Grid grid) return;
        var narrow = e.NewSize.Width < 760;
        grid.ColumnDefinitions[2].Width = narrow ? new GridLength(0) : new GridLength(210);
        grid.ColumnDefinitions[3].Width = narrow ? new GridLength(0) : new GridLength(150);
        if (grid.FindName("RowState") is not FrameworkElement state ||
            grid.FindName("RowActions") is not FrameworkElement actions) return;
        Grid.SetColumn(state, narrow ? 1 : 2);
        Grid.SetRow(state, narrow ? 1 : 0);
        Grid.SetColumn(actions, narrow ? 1 : 3);
        Grid.SetRow(actions, narrow ? 2 : 0);
    }

    private async void RowDetails_Click(object sender, RoutedEventArgs e)
    {
        var row = RowFromSender(sender);
        if (row is null) return;
        var text = new TextBlock { IsTextSelectionEnabled = true, TextWrapping = TextWrapping.Wrap };
        var preview = row.MetadataPreview;
        var identity = preview?.MovieCandidate is { } movie ? $"{movie.Provider} · {movie.ExternalId}"
            : preview?.TvCandidate is { } show ? $"{show.Provider} · {show.ExternalId}" : string.Empty;
        text.Text = string.Join(Environment.NewLine, new[]
        {
            LocalizationService.Text("ImportSourceIdentity"), row.Title, row.IdentityLabel, row.Item.Path,
            row.LocalEligibilityLabel, row.StatusLabel, row.Item.Issue?.Message, row.RegistrationFailure,
            LocalizationService.Text("ImportMetadataEvidence"), row.PreviewTitle, row.PreviewYear,
            identity, row.PreviewDetails, preview?.Failure,
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(text);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, RequestedTheme = ThemeService.ElementTheme,
            FlowDirection = LocalizationService.FlowDirection, Title = LocalizationService.Text("Details"),
            CloseButtonText = LocalizationService.Text("Close"),
            Content = new ScrollViewer { Content = content, MaxHeight = 480, HorizontalScrollMode = ScrollMode.Disabled },
        };
        if (row.CanOverrideType && row.CanAdd && !_isAdding)
        {
            var changeType = new Button { Content = row.OverrideTypeLabel, Tag = row };
            changeType.Click += (button, args) => { dialog.Hide(); OverrideType_Click(button, args); };
            content.Children.Add(changeType);
        }
        await ContentDialogCoordinator.ShowAsync(dialog);
    }

    /// <summary>Removes the given paths from the active queue, unsubscribing so no handler leaks.</summary>
    private void RemoveRows(ISet<string> paths)
    {
        if (paths.Count == 0)
        {
            return;
        }

        for (var index = _rows.Count - 1; index >= 0; index--)
        {
            if (paths.Contains(_rows[index].Item.Path))
            {
                _rows[index].PropertyChanged -= SelectionChanged;
                _rows.RemoveAt(index);
            }
        }

        _detected = _rows.Select(row => row.Item).ToList();
    }

    private void SelectAllButton_Click(object sender, RoutedEventArgs e)
    {
        _batchPresentation = true;
        foreach (var row in _rows.Where(row => row.CanAdd))
        {
            row.IsSelected = true;
        }
        _batchPresentation = false;
        Refresh();
    }

    /// <summary>
    /// Replaces the current selection with only the rows that are safe to add immediately: a confident
    /// match or the clean initial state. The selection is cleared first, so pressing this after "Select
    /// all" leaves needs-review / no-match / metadata-failure / already-in-library rows unselected without
    /// the user having to press "Deselect all" first.
    /// </summary>
    private void SelectReadyButton_Click(object sender, RoutedEventArgs e)
    {
        _batchPresentation = true;
        foreach (var row in _rows)
        {
            row.IsSelected = row.IsReadyToAdd;
        }
        _batchPresentation = false;
        Refresh();
    }

    private void DeselectAllButton_Click(object sender, RoutedEventArgs e)
    {
        _batchPresentation = true;
        foreach (var row in _rows)
        {
            row.IsSelected = false;
        }
        _batchPresentation = false;
        Refresh();
    }

    private void SortType_Click(object sender, RoutedEventArgs e) => ToggleSort(SortColumn.Type);

    private void SortTitle_Click(object sender, RoutedEventArgs e) => ToggleSort(SortColumn.Title);

    private void SortStatus_Click(object sender, RoutedEventArgs e) => ToggleSort(SortColumn.Status);

    /// <summary>
    /// Sets the sort column, or flips direction when the same header is clicked again, then re-orders the
    /// visible rows. Sorting is visual only.
    /// </summary>
    private void ToggleSort(SortColumn column)
    {
        if (_sortColumn == column)
        {
            _sortDescending = !_sortDescending;
        }
        else
        {
            _sortColumn = column;
            _sortDescending = false;
        }

        ApplySort();
        UpdateSortIndicators();
    }

    /// <summary>
    /// Reorders the visible rows only. The same row instances are moved in place, so selection, match
    /// status, staged previews and their change subscriptions are all preserved; nothing is re-fetched,
    /// matching is not restarted, and the catalog is never touched.
    /// </summary>
    private void ApplySort()
    {
        if (_sortColumn == SortColumn.None || _rows.Count < 2)
        {
            return;
        }

        IEnumerable<DetectedMediaDisplayRecord> ordered = _sortColumn switch
        {
            SortColumn.Title => _rows.OrderBy(r => r.Title, StringComparer.CurrentCultureIgnoreCase),
            SortColumn.Type => _rows
                .OrderBy(r => (int)r.Item.Classification)
                .ThenBy(r => r.Title, StringComparer.CurrentCultureIgnoreCase),
            SortColumn.Status => _rows
                .OrderBy(r => (int)r.Status)
                .ThenBy(r => r.Title, StringComparer.CurrentCultureIgnoreCase),
            _ => _rows,
        };

        var sorted = (_sortDescending ? ordered.Reverse() : ordered).ToList();
        _batchPresentation = true;
        for (var targetIndex = 0; targetIndex < sorted.Count; targetIndex++)
        {
            var current = _rows.IndexOf(sorted[targetIndex]);
            if (current != targetIndex)
            {
                _rows.Move(current, targetIndex);
            }
        }
        _batchPresentation = false;
        RebuildVisibleQueue();
    }

    private void UpdateSortIndicators()
    {
        SetSortIndicator(SortTitleIndicator, SortColumn.Title);
        SetSortIndicator(SortTypeIndicator, SortColumn.Type);
        SetSortIndicator(SortStatusIndicator, SortColumn.Status);
    }

    private void SetSortIndicator(FontIcon indicator, SortColumn column)
    {
        if (_sortColumn == column)
        {
            // ChevronUp for ascending, ChevronDown for descending (Segoe Fluent Icons).
            indicator.Glyph = _sortDescending ? "" : "";
            indicator.Visibility = Visibility.Visible;
        }
        else
        {
            indicator.Visibility = Visibility.Collapsed;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isAdding) { _importCancellation?.Cancel(); return; }
        if (_isConfirming) { _confirmationCancelled = true; return; }
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
        if (_isPickingFolder || _isScanning || _isAdding || _isReviewingMetadata) return;
        _isPickingFolder = true;
        Refresh();
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
        finally { _isPickingFolder = false; Refresh(); }
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

            SetDetected(session.Items, root);
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
        ScanProgressText.Text = LocalizationService.Text("ScanningFolder");
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
        RefreshGroupHeaders();

        Refresh();
        _ = ReviewMetadataAsync(version, cancellation);
    }

    private async Task ReviewMetadataAsync(long version, CancellationTokenSource cancellation)
    {
        var candidates = _rows.Where(row => row.Item.FileSize is not null).ToArray();
        var tvPreviews = _rows.Where(r => r.IsTv && r.MetadataPreview is { Status: not ImportItemStatus.ReadyToAdd } && r.Status != ImportItemStatus.AlreadyInLibrary)
            .GroupBy(r => r.TvGroupKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().MetadataPreview!, StringComparer.OrdinalIgnoreCase);
        _matchTotal = candidates.Length;
        _matchProcessed = 0;
        Refresh();
        try
        {
            // Keep provider calls sequential during review. This avoids turning a large scan into a
            // burst that trips TMDB rate limits while still keeping the UI responsive via async I/O.
            foreach (var row in candidates)
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
                    _matchProcessed++;
                    Refresh();
                    continue;
                }

                cancellation.Token.ThrowIfCancellationRequested();
                if (isRegistered)
                {
                    row.SetAlreadyInLibrary();
                    _matchProcessed++;
                    Refresh();
                    continue;
                }

                if (!row.CanAdd || row.ExistingShowId is not null) { _matchProcessed++; continue; }
                if (LocalOnlyBox.IsChecked == true)
                {
                    row.SetMetadataPreview(new ImportMetadataPreview(ImportItemStatus.ReadyToAdd));
                    _matchProcessed++;
                    continue;
                }
                if (!row.IsTv && row.MetadataPreview is { Status: not ImportItemStatus.ReadyToAdd }) { _matchProcessed++; continue; }
                if (row.IsTv && tvPreviews.TryGetValue(row.TvGroupKey, out var groupedPreview))
                {
                    row.SetMetadataPreview(groupedPreview);
                    _matchProcessed++;
                    continue;
                }
                row.SetMetadataReviewing();
                Refresh();
                await ReviewMetadataRowAsync(row, cancellation.Token);
                if (row.IsTv && row.MetadataPreview is { } preview) tvPreviews[row.TvGroupKey] = preview;
                _matchProcessed++;
                Refresh();
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
                // Matching has settled (Ready). Re-apply any active sort once so a Status/Type sort the
                // user chose reflects the final states, without rows jumping on every row update.
                ApplySort();
                RebuildVisibleQueue();
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
        if (row is null || _isAdding || _isReviewingMetadata || !row.CanAdd || _retryCancellations.ContainsKey(row))
        {
            return;
        }

        var members = row.IsTv ? _rows.Where(r => r.IsTv && r.TvGroupKey == row.TvGroupKey && r.CanAdd).ToArray() : [row];
        var cancellation = new CancellationTokenSource();
        foreach (var member in members)
        {
            _retryCancellations[member] = cancellation;
            member.SetMetadataReviewing();
        }
        Refresh();
        try
        {
            // The provider is retried once for the series. The path check remains specific to row.
            var preview = await Task.Run(
                () => AppServices.Import.RetryMetadataAsync(row.Item, cancellation.Token), cancellation.Token);
            if (!cancellation.IsCancellationRequested)
            {
                foreach (var member in members.Where(_rows.Contains))
                {
                    if (preview.Status == ImportItemStatus.AlreadyInLibrary)
                    {
                        if (ReferenceEquals(member, row)) member.SetAlreadyInLibrary();
                        else member.SetMetadataCancelled();
                    }
                    else member.SetMetadataPreview(preview);
                }
                Refresh();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (!cancellation.IsCancellationRequested)
            {
                var preview = new ImportMetadataPreview(ImportItemStatus.MetadataError, Failure: MetadataErrorText.For(error));
                foreach (var member in members.Where(_rows.Contains)) member.SetMetadataPreview(preview);
                Refresh();
            }
        }
        finally
        {
            foreach (var member in members) _retryCancellations.Remove(member);
            cancellation.Dispose();
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
                    var preview = new ImportMetadataPreview(ImportItemStatus.Matched, MatchStatus.Matched, TvCandidate: show, IsManual: true);
                    foreach (var member in _rows.Where(r => r.IsTv && r.TvGroupKey == row.TvGroupKey && r.CanAdd))
                        member.SetMetadataPreview(preview);
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
        if (_isAdding || _isConfirming || _isReviewingMetadata)
        {
            return;
        }

        var selections = _rows
            .Where(row => row.Status != ImportItemStatus.AlreadyInLibrary)
            .Select(row => new ImportSelection(row.Item, row.MetadataPreview, row.ExistingShowId,
                row.OriginalLocalTitle, !row.CanAdd || !row.IsSelected, LocalOnlyBox.IsChecked == true))
            .ToArray();

        var selectedCount = selections.Count(s => !s.IsExcluded);
        if (selectedCount == 0)
        {
            return;
        }

        var importPresentationVersion = _presentationVersion;

        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot, RequestedTheme = ThemeService.ElementTheme, FlowDirection = LocalizationService.FlowDirection,
            Title = LocalizationService.Text("TvBatchConfirm"),
            Content = LocalizationService.Format("TvBatchConfirmCount", selectedCount, selections.Count(s => s.IsExcluded)),
            PrimaryButtonText = LocalizationService.Text("TvBatchImport"), CloseButtonText = LocalizationService.Text("Cancel"),
            DefaultButton = ContentDialogButton.Close
        };
        _confirmationCancelled = false;
        confirm.PrimaryButtonClick += (_, _) =>
        {
            if (_confirmationCancelled) return;
            // Native dialog dismissal is asynchronous. Cancellation must already be available
            // during that transition, before the background registration loop starts.
            _importCancellation = new CancellationTokenSource();
            _isAdding = true;
            _addProcessed = 0;
            _addTotal = selections.Length;
            foreach (var row in _rows) row.SetImporting(true);
            RefreshGroupHeaders();
            Refresh();
        };
        _isConfirming = true;
        ContentDialogResult confirmation;
        try { confirmation = await ContentDialogCoordinator.ShowAsync(confirm); }
        finally { _isConfirming = false; }
        if (confirmation != ContentDialogResult.Primary || _confirmationCancelled)
        {
            _importCancellation?.Dispose();
            _importCancellation = null;
            _isAdding = false;
            foreach (var row in _rows) row.SetImporting(false);
            RefreshGroupHeaders();
            Refresh();
            return;
        }
        CancelMetadataReview();
        _isAdding = true;
        RefreshGroupHeaders();
        _addCompleted = false;
        _addProcessed = 0;
        _addTotal = selections.Length;
        // Freeze the entire review while the captured selection is being processed. Otherwise an
        // unsubmitted checkbox change would make the visible count disagree with the operation in
        // flight, even though the application correctly uses the immutable captured selection.
        foreach (var row in _rows)
        {
            row.SetImporting(true);
        }

        Refresh();
        ImportBatchResult result;
        var importCancellation = _importCancellation ??= new CancellationTokenSource();

        try
        {
            result = await Task.Run(() => AppServices.Import.ImportSelectedAsync(
                selections,
                cancellationToken: importCancellation.Token,
                progress: update => DispatcherQueue.TryEnqueue(() =>
                {
                    // Real processed/total from the registration loop; keep the status line honest.
                    _addProcessed = update.Processed;
                    _addTotal = update.Total;
                    if (_isAdding)
                    {
                        Refresh();
                    }
                })));
        }
        catch (Exception error)
        {
            _isAdding = false;
            foreach (var row in _rows)
            {
                row.SetImporting(false);
            }
            Refresh();
            if (importPresentationVersion == _presentationVersion && XamlRoot is not null)
                await ShowMessageAsync(LocalizationService.Text("Failed"), MetadataErrorText.For(error));
            return;
        }
        finally
        {
            _isAdding = false;
            _importCancellation?.Dispose();
            _importCancellation = null;
            foreach (var row in _rows)
            {
                row.SetImporting(false);
            }
        }

        var addedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rowsByPath = _rows.ToDictionary(row => row.Item.Path, StringComparer.OrdinalIgnoreCase);
        _batchPresentation = true;
        foreach (var item in result.Items)
        {
            if (!rowsByPath.TryGetValue(item.Path, out var row))
            {
                continue;
            }

            row.SetRegistrationResult(item.Status, item.Failure);
            if (item.Registered)
            {
                addedPaths.Add(item.Path);
            }
        }

        // Successfully registered rows leave the active queue so it answers "what still needs my
        // attention?": failed/unresolved rows, and rows the user did not select, stay visible.
        RemoveRows(addedPaths);
        _batchPresentation = false;
        RebuildVisibleQueue();

        // Enter the transient "Done" phase: the status line reports what was added and what still needs
        // attention until the user acts again.
        _addCompleted = true;
        _lastAddedCount = result.AddedCount;
        _lastAttentionCount = result.NeedsAttentionCount;

        MediaRegistered?.Invoke(this, EventArgs.Empty);
        Refresh();

        var summaryLines = new List<string>
        {
            LocalizationService.Format("ImportAddedFormat", result.AddedCount),
            LocalizationService.Format("ImportMatchedFormat", result.MatchedCount),
            LocalizationService.Format("ImportNeedsAttentionFormat", result.NeedsAttentionCount),
            LocalizationService.Format("TvBatchSkippedCount", result.SkippedCount),
            LocalizationService.Format("TvBatchCancelledCount", result.NotProcessedCount),
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

        // Cached queue state and shell catalog notifications stay truthful after navigation.
        // A completed operation must not present a dialog from a page that was left meanwhile.
        if (importPresentationVersion == _presentationVersion && XamlRoot is not null)
            await ShowMessageAsync(
                LocalizationService.Text("AddMedia"),
                string.Join(Environment.NewLine, summaryLines));

        // Only the successfully added rows have been removed. If the whole queue is now empty the page
        // returns to its idle state; otherwise the remaining items (failures / not-yet-resolved) stay on
        // screen so the user can act on them without reopening.
        if (_rows.Count == 0)
        {
            _state = AddMediaScanState.Idle;
        }

        Refresh();
    }

    /// <summary>
    /// Parses picked files through the same discovery service the folder scan uses: each file's own
    /// directory is scanned without recursion and the results are filtered back to what was picked, so
    /// one parser decides what a name means.
    /// </summary>
    internal static IReadOnlyList<DiscoveredMedia> DiscoverPicked(
        IReadOnlyList<string> paths,
        Func<bool>? isCancelled = null)
    {
        var wanted = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        var results = new List<DiscoveredMedia>();

        foreach (var directory in paths
            .Select(Path.GetDirectoryName)
            .Where(directory => !string.IsNullOrEmpty(directory))
              .Distinct(StringComparer.OrdinalIgnoreCase)
              .OrderBy(directory => DropSort.FileSystem.Inspection.NetworkReadGuard.IsNetworkPath(directory!)))
        {
            if (isCancelled?.Invoke() == true)
            {
                throw new OperationCanceledException();
            }

            var found = AppServices.Import.PrepareImportReview(
                directory!,
                recursive: false,
                isCancelled: isCancelled);
            results.AddRange(found.Items.Where(item => wanted.Contains(item.Path)
                || item.Classification == DiscoveryClassification.Error));
        }

        return results;
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
