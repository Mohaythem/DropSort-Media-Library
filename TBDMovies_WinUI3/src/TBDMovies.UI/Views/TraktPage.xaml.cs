using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TBDMovies.Application.Trakt;
using TBDMovies.UI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace TBDMovies.UI.Views;

/// <summary>
/// Dedicated Trakt view.
/// Shows synchronized remote items, local library matches, file availability, and watch badges.
/// Runs all snapshot construction and projections asynchronously off the UI thread.
/// </summary>
public sealed partial class TraktPage : Page, ILocalizableView, IActivatableView, IDeactivatableView
{
    private CancellationTokenSource? _activeLoadCts;
    private long _currentGeneration;
    private CancellationTokenSource? _customListsCts;
    private long _customListsGeneration;
    private bool _isRebuildingResourceSelector;
    private bool _isActivated;
    private string? _loadedAccountId;
    private readonly List<TraktRowModel> _allRows = new();

    public ObservableCollection<TraktRowModel> DisplayedRows { get; } = new();

    /// <summary>
    /// Raised when the user clicks a matched movie row.
    /// Passes the verified local movie identifier.
    /// </summary>
    public event EventHandler<int>? MovieSelected;

    /// <summary>
    /// Raised when the user clicks a matched show row.
    /// Passes the verified local show identifier.
    /// </summary>
    public event EventHandler<int>? ShowSelected;

    /// <summary>
    /// Raised when the user clicks the open settings action in an unconfigured view.
    /// </summary>
    public event EventHandler? SettingsRequested;

    public TraktPage()
    {
        InitializeComponent();
        ApplyLocalization();
    }

    /// <inheritdoc />
    public void Activate()
    {
        _isActivated = true;
        RefreshStatusHeader();
        RefreshCustomListsAsync();
        ReloadCurrentResource();
    }

    /// <inheritdoc />
    public void Deactivate()
    {
        _isActivated = false;
        _activeLoadCts?.Cancel();
        _activeLoadCts = null;
        Interlocked.Increment(ref _currentGeneration);

        _customListsCts?.Cancel();
        _customListsCts = null;
        Interlocked.Increment(ref _customListsGeneration);
    }

    /// <inheritdoc />
    public void ApplyLocalization()
    {
        FlowDirection = LocalizationService.FlowDirection;
        PageTitleText.Text = LocalizationService.Text("TraktPageTitle");
        SyncNowButtonText.Text = LocalizationService.Text("TraktSyncNow");
        ResourceLabelText.Text = LocalizationService.Text("TraktResourceLabel");
        WatchlistItem.Content = LocalizationService.Text("TraktResourceWatchlist");
        WatchedMoviesItem.Content = LocalizationService.Text("TraktResourceWatchedMovies");
        WatchedShowsItem.Content = LocalizationService.Text("TraktResourceWatchedShows");
        HistoryItem.Content = LocalizationService.Text("TraktResourceHistory");
        SearchBox.PlaceholderText = LocalizationService.Text("TraktSearchPlaceholder");

        ColHeaderRankText.Text = LocalizationService.Text("TraktColRank");
        ColHeaderTitleText.Text = LocalizationService.Text("TraktColTitle");
        ColHeaderTypeText.Text = LocalizationService.Text("TraktColType");
        ColHeaderWatchedText.Text = LocalizationService.Text("TraktColWatched");
        ColHeaderAvailabilityText.Text = LocalizationService.Text("TraktColAvailability");
        ColHeaderActionText.Text = LocalizationService.Text("TraktColAction");

        EmptyStateTitleText.Text = LocalizationService.Text("TraktEmptyTitle");
        EmptyStateSubtitleText.Text = LocalizationService.Text("TraktEmptySubtitle");
        GoToSettingsButton.Content = LocalizationService.Text("TraktGoToSettings");

        RefreshStatusHeader();
        FilterDisplayedRows();
    }

    private void RefreshStatusHeader()
    {
        var trakt = AppServices.Trakt;
        if (trakt == null)
        {
            ConnectedDot.Visibility = Visibility.Collapsed;
            SyncingDot.Visibility = Visibility.Collapsed;
            DisconnectedDot.Visibility = Visibility.Visible;
            StatusPillText.Text = LocalizationService.Text("TraktStatusNotConfigured");
            AccountInfoText.Text = LocalizationService.Text("TraktNotConfiguredPrompt");
            SyncNowButton.IsEnabled = false;
            return;
        }

        var state = trakt.GetSyncState();

        if (state.IsSyncing)
        {
            ConnectedDot.Visibility = Visibility.Collapsed;
            SyncingDot.Visibility = Visibility.Visible;
            DisconnectedDot.Visibility = Visibility.Collapsed;
            StatusPillText.Text = LocalizationService.Text("TraktStatusSyncing");
            SyncProgressBar.Visibility = Visibility.Visible;
            SyncNowButton.IsEnabled = false;
        }
        else
        {
            SyncProgressBar.Visibility = Visibility.Collapsed;
            SyncNowButton.IsEnabled = state.AuthState.IsConnected;

            if (state.AuthState.IsConnected)
            {
                ConnectedDot.Visibility = Visibility.Visible;
                SyncingDot.Visibility = Visibility.Collapsed;
                DisconnectedDot.Visibility = Visibility.Collapsed;
                StatusPillText.Text = LocalizationService.Text("TraktStatusConnected");
                AccountInfoText.Text = string.Format(LocalizationService.Text("TraktConnectedAccountFormat"), state.CurrentAccountId ?? "Trakt User");
            }
            else if (!string.IsNullOrWhiteSpace(state.LastAccountId))
            {
                ConnectedDot.Visibility = Visibility.Collapsed;
                SyncingDot.Visibility = Visibility.Collapsed;
                DisconnectedDot.Visibility = Visibility.Visible;
                StatusPillText.Text = LocalizationService.Text("TraktStatusOfflineCache");
                AccountInfoText.Text = string.Format(LocalizationService.Text("TraktOfflineAccountFormat"), state.LastAccountId);
            }
            else
            {
                ConnectedDot.Visibility = Visibility.Collapsed;
                SyncingDot.Visibility = Visibility.Collapsed;
                DisconnectedDot.Visibility = Visibility.Visible;
                StatusPillText.Text = LocalizationService.Text("TraktStatusDisconnected");
                AccountInfoText.Text = string.Empty;
            }
        }

        if (!string.IsNullOrWhiteSpace(state.LastSyncError))
        {
            TraktErrorBanner.Message = string.Format(LocalizationService.Text("TraktLastSyncErrorFormat"), state.LastSyncError);
            TraktErrorBanner.IsOpen = true;
        }
        else
        {
            TraktErrorBanner.IsOpen = false;
        }
    }

    private void RefreshCustomListsAsync()
    {
        var trakt = AppServices.Trakt;
        if (trakt == null) return;

        _customListsCts?.Cancel();
        var cts = new CancellationTokenSource();
        _customListsCts = cts;
        var generation = Interlocked.Increment(ref _customListsGeneration);
        var ct = cts.Token;

        Task.Run(() =>
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                var lists = trakt.GetLists();
                ct.ThrowIfCancellationRequested();

                DispatcherQueue.TryEnqueue(() =>
                {
                    if (generation != Volatile.Read(ref _customListsGeneration) || !_isActivated || ct.IsCancellationRequested)
                    {
                        return;
                    }

                    var previousSelectedItem = ResourceSelector.SelectedItem as ComboBoxItem;
                    var previousSelectedTag = previousSelectedItem?.Tag;

                    _isRebuildingResourceSelector = true;
                    try
                    {
                        for (int i = ResourceSelector.Items.Count - 1; i >= 4; i--)
                        {
                            ResourceSelector.Items.RemoveAt(i);
                        }

                        ComboBoxItem? reselectItem = null;
                        foreach (var list in lists)
                        {
                            var item = new ComboBoxItem
                            {
                                Content = string.Format(LocalizationService.Text("TraktResourceCustomListFormat"), list.Name),
                                Tag = list.TraktId
                            };
                            ResourceSelector.Items.Add(item);

                            if (previousSelectedTag is long selectedId && selectedId == list.TraktId)
                            {
                                reselectItem = item;
                            }
                        }

                        if (reselectItem != null)
                        {
                            ResourceSelector.SelectedItem = reselectItem;
                        }
                        else if (previousSelectedTag is long)
                        {
                            // Preserved custom list no longer exists; fall back to watchlist
                            ResourceSelector.SelectedIndex = 0;
                        }
                    }
                    finally
                    {
                        _isRebuildingResourceSelector = false;
                    }

                    var newSelectedTag = (ResourceSelector.SelectedItem as ComboBoxItem)?.Tag;
                    if (!Equals(previousSelectedTag, newSelectedTag))
                    {
                        ReloadCurrentResource();
                    }
                });
            }
            catch (OperationCanceledException)
            {
                // Cancellation tolerated
            }
            catch (Exception)
            {
                // Cache read failure tolerated
            }
            finally
            {
                ReleaseLoadSource(cts, customLists: true);
            }
        });
    }

    public void SelectResource(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;

        if (key == "trakt:watchlist" || key == "watchlist")
        {
            if (ResourceSelector.Items.Count > 0)
            {
                ResourceSelector.SelectedIndex = 0;
                ReloadCurrentResource();
            }
            return;
        }

        if (key.StartsWith("trakt:list:", StringComparison.Ordinal)
            && long.TryParse(key.AsSpan("trakt:list:".Length), out var listId))
        {
            foreach (var item in ResourceSelector.Items)
            {
                if (item is ComboBoxItem cb && cb.Tag is long id && id == listId)
                {
                    ResourceSelector.SelectedItem = cb;
                    ReloadCurrentResource();
                    return;
                }
            }
        }
    }

    private void ResourceSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isActivated || _isRebuildingResourceSelector) return;
        ReloadCurrentResource();
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        FilterDisplayedRows();
    }

    private void ReloadCurrentResource()
    {
        _activeLoadCts?.Cancel();
        var cts = new CancellationTokenSource();
        _activeLoadCts = cts;
        var generation = Interlocked.Increment(ref _currentGeneration);

        var trakt = AppServices.Trakt;
        var snapshotBuilder = AppServices.TraktLocalSnapshot;
        var projection = AppServices.TraktProjection;

        var state = trakt?.GetSyncState();
        var currentAccountId = state?.CurrentAccountId ?? state?.LastAccountId;
        if (!string.Equals(_loadedAccountId, currentAccountId, StringComparison.Ordinal))
        {
            _allRows.Clear();
            DisplayedRows.Clear();
            _loadedAccountId = currentAccountId;
        }

        SetLoading(true);

        if (trakt == null || snapshotBuilder == null || projection == null)
        {
            SetLoading(false);
            _allRows.Clear();
            DisplayedRows.Clear();
            UpdateEmptyState(isNotConfigured: true);
            _activeLoadCts = null;
            cts.Dispose();
            return;
        }

        var selectedItem = ResourceSelector.SelectedItem as ComboBoxItem;
        var selectedTag = selectedItem?.Tag?.ToString() ?? "watchlist";
        var customListId = selectedItem?.Tag as long?;

        var ct = cts.Token;
        Task.Run(async () =>
        {
            try
            {
                ct.ThrowIfCancellationRequested();

                // 1. Build local candidate snapshot off UI thread.
                // Snapshot can fail without hiding remote items: degrade to empty candidate snapshot.
                TraktLocalSnapshot snapshot;
                bool localSnapshotFailed = false;
                try
                {
                    snapshot = await snapshotBuilder.BuildSnapshotAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception)
                {
                    localSnapshotFailed = true;
                    snapshot = new TraktLocalSnapshot([], DateTimeOffset.UtcNow);
                }

                if (generation != Volatile.Read(ref _currentGeneration) || ct.IsCancellationRequested)
                {
                    return;
                }

                // 2. Fetch and project resource
                IReadOnlyList<TraktRowModel> rows;

                switch (selectedTag)
                {
                    case "watched_movies":
                    {
                        var movies = trakt.GetWatchedMovies();
                        ct.ThrowIfCancellationRequested();
                        var projected = projection.ProjectWatchedMovies(movies, snapshot, ct);
                        rows = projected.Select((p, idx) => MapWatchedMovie(p, idx + 1, localSnapshotFailed)).ToList();
                        break;
                    }
                    case "watched_shows":
                    {
                        var shows = trakt.GetWatchedShows();
                        ct.ThrowIfCancellationRequested();
                        var projected = projection.ProjectWatchedShows(shows, snapshot, ct);
                        rows = projected.Select((p, idx) => MapWatchedShow(p, idx + 1, localSnapshotFailed)).ToList();
                        break;
                    }
                    case "history":
                    {
                        var history = trakt.GetHistory();
                        ct.ThrowIfCancellationRequested();
                        var projected = projection.ProjectHistory(history, snapshot, ct);
                        rows = projected.Select((p, idx) => MapHistoryItem(p, idx + 1, snapshot, localSnapshotFailed)).ToList();
                        break;
                    }
                    case "watchlist":
                    {
                        var watchlist = trakt.GetWatchlist();
                        ct.ThrowIfCancellationRequested();
                        var movies = trakt.GetWatchedMovies();
                        var shows = trakt.GetWatchedShows();
                        var history = trakt.GetHistory();
                        var context = TraktWatchedContext.Create(movies, shows, history);
                        ct.ThrowIfCancellationRequested();
                        var projected = projection.ProjectWatchlist(watchlist, snapshot, context, ct);
                        rows = projected.Select((p, idx) => MapWatchlistItem(p, idx + 1, snapshot, localSnapshotFailed)).ToList();
                        break;
                    }
                    default:
                    {
                        if (customListId.HasValue)
                        {
                            var listItems = trakt.GetListItems(customListId.Value);
                            ct.ThrowIfCancellationRequested();
                            var movies = trakt.GetWatchedMovies();
                            var shows = trakt.GetWatchedShows();
                            var history = trakt.GetHistory();
                            var context = TraktWatchedContext.Create(movies, shows, history);
                            ct.ThrowIfCancellationRequested();
                            var projected = projection.ProjectListItems(listItems, snapshot, context, ct);
                            rows = projected.Select((p, idx) => MapListItem(p, idx + 1, snapshot, localSnapshotFailed)).ToList();
                        }
                        else
                        {
                            rows = [];
                        }
                        break;
                    }
                }

                var lastSyncError = trakt.GetSyncState().LastSyncError;
                // 3. Post back to UI dispatcher queue if generation still matches
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (generation != Volatile.Read(ref _currentGeneration) || ct.IsCancellationRequested)
                    {
                        return;
                    }

                    _allRows.Clear();
                    _allRows.AddRange(rows);
                    FilterDisplayedRows();
                    SetLoading(false);

                    if (localSnapshotFailed)
                    {
                        TraktErrorBanner.Severity = InfoBarSeverity.Warning;
                        var warn = LocalizationService.Text("TraktLocalSnapshotFailedWarning");
                        TraktErrorBanner.Message = warn == "TraktLocalSnapshotFailedWarning"
                            ? "Unable to read local library snapshot. Local match status is unknown."
                            : warn;
                        TraktErrorBanner.IsOpen = true;
                    }
                    else
                    {
                        if (string.IsNullOrWhiteSpace(lastSyncError))
                        {
                            TraktErrorBanner.IsOpen = false;
                        }
                    }
                });
            }
            catch (OperationCanceledException)
            {
                // Clean cancellation
            }
            catch (Exception ex)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (generation != Volatile.Read(ref _currentGeneration) || ct.IsCancellationRequested)
                    {
                        return;
                    }

                    TraktErrorBanner.Severity = InfoBarSeverity.Error;
                    TraktErrorBanner.Message = ex.Message;
                    TraktErrorBanner.IsOpen = true;
                    SetLoading(false);
                    UpdateEmptyState(isNotConfigured: false);
                });
            }
            finally
            {
                ReleaseLoadSource(cts, customLists: false);
            }
        });
    }

    private void ReleaseLoadSource(CancellationTokenSource source, bool customLists)
    {
        // Pointer ownership and cancellation are serialized on the dispatcher.
        // Always run the worker so cancellation before scheduling still reaches cleanup.
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            if (customLists && ReferenceEquals(_customListsCts, source)) _customListsCts = null;
            if (!customLists && ReferenceEquals(_activeLoadCts, source)) _activeLoadCts = null;
            source.Dispose();
        }))
        {
            source.Dispose();
        }
    }

    private void FilterDisplayedRows()
    {
        var filter = SearchBox?.Text?.Trim();
        DisplayedRows.Clear();

        IEnumerable<TraktRowModel> query = _allRows;
        if (!string.IsNullOrWhiteSpace(filter))
        {
            query = query.Where(r =>
                r.Title.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                r.SubtitleText.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                r.KindText.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var row in query)
        {
            row.AvailabilityBrush = GetThemeBrush(row.AvailabilityBrushKey);
            DisplayedRows.Add(row);
        }

        ItemCountText.Text = string.Format(LocalizationService.Text("TraktItemsCountFormat"), DisplayedRows.Count);
        UpdateEmptyState(isNotConfigured: false);
    }

    private void SetLoading(bool isLoading)
    {
        LoadingRing.IsActive = isLoading;
        LoadingRing.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
        UpdateEmptyState(isNotConfigured: false);
    }

    private void UpdateEmptyState(bool isNotConfigured)
    {
        // The list is later in the Grid and otherwise intercepts clicks on the empty-state button.
        ItemsListView.Visibility = !LoadingRing.IsActive && !isNotConfigured && DisplayedRows.Count > 0
            ? Visibility.Visible : Visibility.Collapsed;
        if (LoadingRing.IsActive)
        {
            EmptyStatePanel.Visibility = Visibility.Collapsed;
            return;
        }

        if (isNotConfigured)
        {
            EmptyStateTitleText.Text = LocalizationService.Text("TraktStatusNotConfigured");
            EmptyStateSubtitleText.Text = LocalizationService.Text("TraktNotConfiguredPrompt");
            GoToSettingsButton.Visibility = Visibility.Visible;
            EmptyStatePanel.Visibility = Visibility.Visible;
        }
        else if (DisplayedRows.Count == 0)
        {
            EmptyStateTitleText.Text = LocalizationService.Text("TraktEmptyTitle");
            EmptyStateSubtitleText.Text = LocalizationService.Text("TraktEmptySubtitle");
            GoToSettingsButton.Visibility = Visibility.Collapsed;
            EmptyStatePanel.Visibility = Visibility.Visible;
        }
        else
        {
            EmptyStatePanel.Visibility = Visibility.Collapsed;
        }
    }

    private async void SyncNowButton_Click(object sender, RoutedEventArgs e)
    {
        var trakt = AppServices.Trakt;
        if (trakt == null) return;

        SyncNowButton.IsEnabled = false;
        SyncProgressBar.Visibility = Visibility.Visible;
        TraktErrorBanner.IsOpen = false;

        try
        {
            var result = await trakt.SyncAsync().ConfigureAwait(true);
            if (!_isActivated)
            {
                return;
            }

            RefreshStatusHeader();
            if (result.Success)
            {
                RefreshCustomListsAsync();
                ReloadCurrentResource();
            }
            else if (!string.IsNullOrWhiteSpace(result.AbortReason))
            {
                TraktErrorBanner.Severity = InfoBarSeverity.Warning;
                TraktErrorBanner.Message = result.AbortReason;
                TraktErrorBanner.IsOpen = true;
            }
        }
        catch (Exception ex)
        {
            if (!_isActivated)
            {
                return;
            }

            TraktErrorBanner.Severity = InfoBarSeverity.Error;
            TraktErrorBanner.Message = ex.Message;
            TraktErrorBanner.IsOpen = true;
        }
        finally
        {
            if (_isActivated)
            {
                RefreshStatusHeader();
                SyncProgressBar.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void GoToSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        SettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    private void DetailsButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: TraktRowModel row })
        {
            if (row.MatchedLocalKind == TraktMediaKind.Movie && row.MatchedLocalId.HasValue && row.MatchedLocalId.Value > 0)
            {
                MovieSelected?.Invoke(this, row.MatchedLocalId.Value);
            }
            else if (row.MatchedLocalKind == TraktMediaKind.Show && row.MatchedLocalId.HasValue && row.MatchedLocalId.Value > 0)
            {
                ShowSelected?.Invoke(this, row.MatchedLocalId.Value);
            }
            else if ((row.MatchedLocalKind == TraktMediaKind.Season || row.MatchedLocalKind == TraktMediaKind.Episode) &&
                     row.ParentShowId.HasValue && row.ParentShowId.Value > 0)
            {
                ShowSelected?.Invoke(this, row.ParentShowId.Value);
            }
        }
    }

    #region Projection Mappings

    private static TraktRowModel MapWatchlistItem(TraktProjectedWatchlistItem item, int index, TraktLocalSnapshot snapshot, bool localSnapshotFailed)
    {
        var (availText, availBrush) = ResolveAvailabilityDisplay(item.MatchStatus, item.Availability, localSnapshotFailed);
        var (watchedText, hasWatched) = ResolveWatchedDisplay(item.IsWatched, item.PlayCount, item.WatchedEpisodeCount, item.TotalEpisodeCount);
        var parentShowId = ResolveProvenParentShowId(item.RemoteIdentity, snapshot);

        return new TraktRowModel
        {
            KeyId = item.Id,
            IndexText = item.Rank > 0 ? item.Rank.ToString() : index.ToString(),
            Title = item.DisplayTitle,
            YearText = item.Year.HasValue ? $"({item.Year.Value})" : string.Empty,
            YearVisibility = item.Year.HasValue ? Visibility.Visible : Visibility.Collapsed,
            KindText = FormatKindText(item.Type),
            SubtitleText = item.Notes ?? string.Empty,
            SubtitleVisibility = !string.IsNullOrWhiteSpace(item.Notes) ? Visibility.Visible : Visibility.Collapsed,
            WatchedBadgeText = watchedText,
            WatchedBadgeVisibility = hasWatched ? Visibility.Visible : Visibility.Collapsed,
            AvailabilityBadgeText = availText,
            AvailabilityBrushKey = availBrush,
            ActionButtonText = LocalizationService.Text("TraktActionViewDetails"),
            MatchedLocalKind = item.MatchedLocalKind,
            MatchedLocalId = item.MatchedLocalId,
            ParentShowId = parentShowId,
            NavigateButtonVisibility = CanNavigate(item.MatchedLocalKind, item.MatchedLocalId, parentShowId)
                ? Visibility.Visible : Visibility.Collapsed
        };
    }

    private static TraktRowModel MapListItem(TraktProjectedListItem item, int index, TraktLocalSnapshot snapshot, bool localSnapshotFailed)
    {
        var (availText, availBrush) = ResolveAvailabilityDisplay(item.MatchStatus, item.Availability, localSnapshotFailed);
        var (watchedText, hasWatched) = ResolveWatchedDisplay(item.IsWatched, item.PlayCount, item.WatchedEpisodeCount, item.TotalEpisodeCount);
        var parentShowId = ResolveProvenParentShowId(item.RemoteIdentity, snapshot);

        return new TraktRowModel
        {
            KeyId = item.Id,
            IndexText = item.Rank > 0 ? item.Rank.ToString() : index.ToString(),
            Title = item.DisplayTitle,
            YearText = item.Year.HasValue ? $"({item.Year.Value})" : string.Empty,
            YearVisibility = item.Year.HasValue ? Visibility.Visible : Visibility.Collapsed,
            KindText = FormatKindText(item.Type),
            SubtitleText = item.Notes ?? string.Empty,
            SubtitleVisibility = !string.IsNullOrWhiteSpace(item.Notes) ? Visibility.Visible : Visibility.Collapsed,
            WatchedBadgeText = watchedText,
            WatchedBadgeVisibility = hasWatched ? Visibility.Visible : Visibility.Collapsed,
            AvailabilityBadgeText = availText,
            AvailabilityBrushKey = availBrush,
            ActionButtonText = LocalizationService.Text("TraktActionViewDetails"),
            MatchedLocalKind = item.MatchedLocalKind,
            MatchedLocalId = item.MatchedLocalId,
            ParentShowId = parentShowId,
            NavigateButtonVisibility = CanNavigate(item.MatchedLocalKind, item.MatchedLocalId, parentShowId)
                ? Visibility.Visible : Visibility.Collapsed
        };
    }

    private static TraktRowModel MapWatchedMovie(TraktProjectedWatchedMovie item, int index, bool localSnapshotFailed)
    {
        var (availText, availBrush) = ResolveAvailabilityDisplay(item.MatchStatus, item.Availability, localSnapshotFailed);
        var (watchedText, hasWatched) = ResolveWatchedDisplay(item.IsWatched, item.Plays, 0, null);

        var subtitle = item.LastWatchedAt.HasValue
            ? string.Format(LocalizationService.Text("TraktWatchedAtFormat"), item.LastWatchedAt.Value.ToLocalTime().ToString("g"))
            : string.Empty;

        return new TraktRowModel
        {
            KeyId = item.TraktId ?? index,
            IndexText = index.ToString(),
            Title = item.DisplayTitle,
            YearText = item.Year.HasValue ? $"({item.Year.Value})" : string.Empty,
            YearVisibility = item.Year.HasValue ? Visibility.Visible : Visibility.Collapsed,
            KindText = LocalizationService.Text("Movie"),
            SubtitleText = subtitle,
            SubtitleVisibility = !string.IsNullOrWhiteSpace(subtitle) ? Visibility.Visible : Visibility.Collapsed,
            WatchedBadgeText = watchedText,
            WatchedBadgeVisibility = hasWatched ? Visibility.Visible : Visibility.Collapsed,
            AvailabilityBadgeText = availText,
            AvailabilityBrushKey = availBrush,
            ActionButtonText = LocalizationService.Text("TraktActionViewDetails"),
            MatchedLocalKind = item.MatchedLocalKind,
            MatchedLocalId = item.MatchedLocalId,
            ParentShowId = null,
            NavigateButtonVisibility = CanNavigate(item.MatchedLocalKind, item.MatchedLocalId, null)
                ? Visibility.Visible : Visibility.Collapsed
        };
    }

    private static TraktRowModel MapWatchedShow(TraktProjectedWatchedShow item, int index, bool localSnapshotFailed)
    {
        var (availText, availBrush) = ResolveAvailabilityDisplay(item.MatchStatus, item.Availability, localSnapshotFailed);
        var (watchedText, hasWatched) = ResolveWatchedDisplay(item.IsWatched, item.Plays, item.WatchedEpisodeCount, item.TotalEpisodeCount);

        var subtitle = item.LastWatchedAt.HasValue
            ? string.Format(LocalizationService.Text("TraktWatchedAtFormat"), item.LastWatchedAt.Value.ToLocalTime().ToString("g"))
            : string.Empty;

        return new TraktRowModel
        {
            KeyId = item.TraktId ?? index,
            IndexText = index.ToString(),
            Title = item.DisplayTitle,
            YearText = item.Year.HasValue ? $"({item.Year.Value})" : string.Empty,
            YearVisibility = item.Year.HasValue ? Visibility.Visible : Visibility.Collapsed,
            KindText = LocalizationService.Text("TvShow"),
            SubtitleText = subtitle,
            SubtitleVisibility = !string.IsNullOrWhiteSpace(subtitle) ? Visibility.Visible : Visibility.Collapsed,
            WatchedBadgeText = watchedText,
            WatchedBadgeVisibility = hasWatched ? Visibility.Visible : Visibility.Collapsed,
            AvailabilityBadgeText = availText,
            AvailabilityBrushKey = availBrush,
            ActionButtonText = LocalizationService.Text("TraktActionViewDetails"),
            MatchedLocalKind = item.MatchedLocalKind,
            MatchedLocalId = item.MatchedLocalId,
            ParentShowId = null,
            NavigateButtonVisibility = CanNavigate(item.MatchedLocalKind, item.MatchedLocalId, null)
                ? Visibility.Visible : Visibility.Collapsed
        };
    }

    private static TraktRowModel MapHistoryItem(TraktProjectedHistoryItem item, int index, TraktLocalSnapshot snapshot, bool localSnapshotFailed)
    {
        var (availText, availBrush) = ResolveAvailabilityDisplay(item.MatchStatus, item.Availability, localSnapshotFailed);
        var (watchedText, hasWatched) = ResolveWatchedDisplay(item.IsWatched, 1, 0, null);

        var details = new List<string>();
        if (item.SeasonNumber.HasValue && item.EpisodeNumber.HasValue)
        {
            details.Add($"S{item.SeasonNumber.Value:D2}E{item.EpisodeNumber.Value:D2}");
        }
        if (item.WatchedAt.HasValue)
        {
            details.Add(item.WatchedAt.Value.ToLocalTime().ToString("g"));
        }
        var subtitle = string.Join(" · ", details);

        var parentShowId = ResolveProvenParentShowId(item.RemoteIdentity, snapshot);

        return new TraktRowModel
        {
            KeyId = item.HistoryId,
            IndexText = index.ToString(),
            Title = item.DisplayTitle,
            YearText = string.Empty,
            YearVisibility = Visibility.Collapsed,
            KindText = FormatKindText(item.Type),
            SubtitleText = subtitle,
            SubtitleVisibility = !string.IsNullOrWhiteSpace(subtitle) ? Visibility.Visible : Visibility.Collapsed,
            WatchedBadgeText = watchedText,
            WatchedBadgeVisibility = hasWatched ? Visibility.Visible : Visibility.Collapsed,
            AvailabilityBadgeText = availText,
            AvailabilityBrushKey = availBrush,
            ActionButtonText = LocalizationService.Text("TraktActionViewDetails"),
            MatchedLocalKind = item.MatchedLocalKind,
            MatchedLocalId = item.MatchedLocalId,
            ParentShowId = parentShowId,
            NavigateButtonVisibility = CanNavigate(item.MatchedLocalKind, item.MatchedLocalId, parentShowId)
                ? Visibility.Visible : Visibility.Collapsed
        };
    }

    private static (string Text, string BrushKey) ResolveAvailabilityDisplay(
        TraktMatchStatus matchStatus,
        TraktAvailability availability,
        bool localSnapshotFailed = false)
    {
        if (localSnapshotFailed)
        {
            return (LocalizationService.Text("TraktAvailabilityUnknown"), "TextFillColorSecondaryBrush");
        }

        if (matchStatus != TraktMatchStatus.Matched)
        {
            return (LocalizationService.Text("TraktUnmatched"), "TextFillColorTertiaryBrush");
        }

        return availability switch
        {
            TraktAvailability.Available => (LocalizationService.Text("TraktOnDisk"), "SystemFillColorSuccessBrush"),
            TraktAvailability.Missing => (LocalizationService.Text("TraktNotDownloaded"), "SystemFillColorCautionBrush"),
            TraktAvailability.Partial => (LocalizationService.Text("TraktPartial"), "SystemFillColorCautionBrush"),
            TraktAvailability.Unknown => (LocalizationService.Text("TraktAvailabilityUnknown"), "TextFillColorSecondaryBrush"),
            _ => (LocalizationService.Text("TraktAvailabilityUnknown"), "TextFillColorSecondaryBrush"),
        };
    }

    private static (string Text, bool HasWatched) ResolveWatchedDisplay(
        bool isWatched,
        int plays,
        int watchedEpisodes,
        int? totalEpisodes)
    {
        if (!isWatched && watchedEpisodes <= 0)
        {
            return (string.Empty, false);
        }

        if (watchedEpisodes > 0)
        {
            if (totalEpisodes.HasValue && totalEpisodes.Value > 0)
            {
                return (string.Format(LocalizationService.Text("TraktWatchedEpisodesFormat"), watchedEpisodes, totalEpisodes.Value), true);
            }

            return (string.Format(LocalizationService.Text("TraktWatchedEpisodesConservativeFormat"), watchedEpisodes), true);
        }

        if (plays > 1)
        {
            return (string.Format(LocalizationService.Text("TraktWatchedCountFormat"), plays), true);
        }

        return (LocalizationService.Text("TraktWatchedBadge"), true);
    }

    private static int? ResolveProvenParentShowId(TraktIdentity? identity, TraktLocalSnapshot snapshot)
    {
        if (identity == null || !identity.HasParentId)
        {
            return null;
        }

        var parentIdentity = new TraktIdentity(
            TraktMediaKind.Show,
            TraktId: identity.ShowTraktId,
            TmdbId: identity.ShowTmdbId,
            ImdbId: identity.ShowImdbId,
            TvdbId: identity.ShowTvdbId);

        if (!parentIdentity.IsValid)
        {
            return null;
        }

        var match = snapshot.Match(parentIdentity);
        if (match.Status == TraktMatchStatus.Matched && match.Candidate is { LocalId: > 0 } candidate)
        {
            return candidate.LocalId;
        }

        return null;
    }

    private static bool CanNavigate(TraktMediaKind? kind, int? localId, int? parentShowId)
    {
        if (kind == TraktMediaKind.Movie && localId.HasValue && localId.Value > 0) return true;
        if (kind == TraktMediaKind.Show && localId.HasValue && localId.Value > 0) return true;
        if ((kind == TraktMediaKind.Season || kind == TraktMediaKind.Episode) && parentShowId.HasValue && parentShowId.Value > 0) return true;
        return false;
    }

    private static string FormatKindText(string? rawType) => rawType?.ToLowerInvariant() switch
    {
        "movie" => LocalizationService.Text("Movie"),
        "show" => LocalizationService.Text("TvShow"),
        "season" => LocalizationService.Text("Season"),
        "episode" => LocalizationService.Text("Episode"),
        "person" => LocalizationService.Text("Person"),
        _ => rawType ?? LocalizationService.Text("Item")
    };

    private static Brush GetThemeBrush(string key)
    {
        if (Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue(key, out var resource) && resource is Brush brush)
        {
            return brush;
        }
        return new SolidColorBrush(Microsoft.UI.Colors.Gray);
    }

    #endregion
}

/// <summary>
/// Presentation model bound to the virtualized Trakt ListView item template.
/// </summary>
public sealed class TraktRowModel
{
    public long KeyId { get; set; }
    public string IndexText { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string YearText { get; set; } = string.Empty;
    public Visibility YearVisibility { get; set; } = Visibility.Collapsed;
    public string KindText { get; set; } = string.Empty;
    public string SubtitleText { get; set; } = string.Empty;
    public Visibility SubtitleVisibility { get; set; } = Visibility.Collapsed;

    public string WatchedBadgeText { get; set; } = string.Empty;
    public Visibility WatchedBadgeVisibility { get; set; } = Visibility.Collapsed;

    public string AvailabilityBadgeText { get; set; } = string.Empty;
    public string AvailabilityBrushKey { get; set; } = "TextFillColorSecondaryBrush";
    public Brush? AvailabilityBrush { get; set; }

    public string ActionButtonText { get; set; } = string.Empty;
    public Visibility NavigateButtonVisibility { get; set; } = Visibility.Collapsed;

    public TraktMediaKind? MatchedLocalKind { get; set; }
    public int? MatchedLocalId { get; set; }
    public int? ParentShowId { get; set; }
}
