using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DropSort.Application.Trakt;
using DropSort.Domain.Library.Personal;
using DropSort.UI.Models;
using DropSort.UI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DropSort.UI.Views;

public sealed partial class MyListsPage
{
    private const string TraktWatchlistKey = "trakt:watchlist";
    private readonly List<(RadioButton Tab, TextBlock Label, TextBlock Count, string Key)> _traktPlaylistsTabs = [];
    private string _selectedTraktKey = TraktWatchlistKey;
    private static readonly ConcurrentDictionary<long, string?> _traktPosterCache = new();
    private CancellationTokenSource? _traktCts;
    private CancellationTokenSource? _traktWriteCts;
    private CancellationTokenSource? _traktDisplayCts;
    private long _traktDisplayGeneration;
    private long _traktGeneration;
    private string? _traktAccount;
    private bool _traktActive;
    private bool _traktBusy;

    private bool IsTraktKey(string key) => key == "trakt" || key.StartsWith("trakt:", StringComparison.Ordinal);

    public void SelectTraktList(string key)
    {
        if (!IsTraktKey(key)) return;
        _selectedTraktKey = key == "trakt" ? TraktWatchlistKey : key;
        _selectedKey = _selectedTraktKey;
        _query = string.Empty;
        SearchBox.Text = string.Empty;
        Refresh();
    }

    private void RebuildTraktTabs()
    {
        TraktPlaylistsSelector.Children.Clear();
        _traktPlaylistsTabs.Clear();
        var trakt = AppServices.Trakt;
        if (trakt is null) return;
        TraktSyncState state;
        try { state = trakt.GetSyncState(); } catch { return; }
        var account = state.AuthState.IsConnected ? state.CurrentAccountId : state.LastAccountId;
        _traktAccount = account;
        IReadOnlyList<TraktCustomList> lists;
        int watchCount;
        try { watchCount = trakt.GetWatchlist(account).Count; lists = trakt.GetLists(account); }
        catch { watchCount = 0; lists = []; }
        var defs = new List<(string Key, string Name, int Count, string Glyph)>
        {
            (TraktWatchlistKey, LocalizationService.Text("TraktWatchlist"), watchCount, "\uE8FD")
        };
        foreach (var list in lists) defs.Add(($"trakt:list:{list.TraktId}", list.Name, list.ItemCount, "\uE8BD"));
        if (IsTraktKey(_selectedKey) && !defs.Any(x => x.Key == _selectedKey))
        {
            _selectedKey = TraktWatchlistKey;
            _selectedTraktKey = TraktWatchlistKey;
        }
        foreach (var def in defs)
        {
            var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Text = def.Name };
            var count = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Text = LocalizationService.Number(def.Count), Style = (Style)App.Current.Resources["SecondaryCaptionTextStyle"] };
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            content.Children.Add(new FontIcon { FontSize = 14, Glyph = def.Glyph, FontFamily = (FontFamily)App.Current.Resources["SymbolThemeFontFamily"] });
            content.Children.Add(label); content.Children.Add(count);
            var tab = new RadioButton { GroupName = "TraktPlaylistsGroup", Style = (Style)App.Current.Resources["TabStripItemStyle"], Tag = def.Key, Content = content, IsChecked = def.Key == _selectedKey };
            tab.Checked += TraktPlaylistTab_Checked;
            TraktPlaylistsSelector.Children.Add(tab);
            _traktPlaylistsTabs.Add((tab, label, count, def.Key));
        }
    }

    private void TraktPlaylistTab_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isReady || _rebuilding || _syncing) return;
        if (sender is not RadioButton { Tag: string key } || key == _selectedKey) return;
        _selectedTraktKey = key;
        _selectedKey = key;
        _query = string.Empty;
        SearchBox.Text = string.Empty;
        Refresh();
    }

    private void SyncTraktSelectedTab()
    {
        _syncing = true;
        if (TraktTabItem.IsChecked != true) TraktTabItem.IsChecked = true;
        foreach (var (tab, _, _, key) in _traktPlaylistsTabs)
            if (key == _selectedKey && tab.IsChecked != true) tab.IsChecked = true;
        _syncing = false;
    }

    private async void RefreshTrakt()
    {
        try { await RefreshTraktAsync(); }
        catch (OperationCanceledException) { }
        catch (Exception) { if (_traktActive && IsTraktKey(_selectedKey)) { ListGrid.ItemsSource = null; ListCountText.Text = LocalizationService.Text("Failed"); } }
    }

    private async Task RefreshTraktAsync()
    {
        if (!_traktActive) return;
        var displayGeneration = ++_traktDisplayGeneration;
        _traktDisplayCts?.Cancel();
        _traktDisplayCts?.Dispose();
        _traktDisplayCts = new CancellationTokenSource();
        var displayToken = _traktDisplayCts.Token;
        var operationGeneration = _traktGeneration;
        var displayCoordinator = AppServices.Trakt;

        if (_selectedKey == "trakt") _selectedKey = _selectedTraktKey;
        var displayKey = _selectedKey;

        SyncTraktSelectedTab();
        TraktPlaylistsScroll.Visibility = Visibility.Visible;
        ListGrid.Visibility = Visibility.Visible;
        ContinueGrid.Visibility = Visibility.Collapsed;
        HistoryGrid.Visibility = Visibility.Collapsed;
        RenameListButton.Visibility = Visibility.Collapsed;
        DeleteListButton.Visibility = Visibility.Collapsed;

        var trakt = displayCoordinator;
        TraktSyncState? state = null;
        try { state = trakt?.GetSyncState(); } catch { }
        var account = state is null ? null : state.AuthState.IsConnected ? state.CurrentAccountId : state.LastAccountId;
        _traktAccount = account;
        var name = _selectedKey == TraktWatchlistKey
            ? LocalizationService.Text("TraktWatchlist")
            : SafeTraktListName(trakt, account, _selectedKey);
        ListNameText.Text = "Trakt · " + name;

        var canWrite = state?.AuthState.IsConnected == true && !state.AuthState.RequiresReconnect && !string.IsNullOrWhiteSpace(state.CurrentAccountId);
        AddMoviesButton.Visibility = canWrite ? Visibility.Visible : Visibility.Collapsed;
        UpdateTraktSyncVisibility();
        AddMoviesButton.IsEnabled = canWrite && !_traktBusy;
        SyncTraktButton.IsEnabled = canWrite && !_traktBusy;
        AddMoviesButtonText.Text = LocalizationService.Text("AddToTrakt");

        var loadedItems = new List<ListMediaItem>();
        if (trakt is not null && ReferenceEquals(displayCoordinator, AppServices.Trakt))
        {
            var builder = AppServices.TraktLocalSnapshot;
            TraktLocalSnapshot snapshot;
            try
            {
                snapshot = builder is null
                    ? new TraktLocalSnapshot([], DateTimeOffset.UtcNow)
                    : await Task.Run(() => builder.BuildSnapshotAsync(displayToken), displayToken);
            }
            catch (OperationCanceledException) { return; }
            catch { snapshot = new TraktLocalSnapshot([], DateTimeOffset.UtcNow); }

            var currentState = trakt.GetSyncState();
            var currentAccount = currentState.AuthState.IsConnected ? currentState.CurrentAccountId : currentState.LastAccountId;
            if (displayToken.IsCancellationRequested || displayGeneration != _traktDisplayGeneration || operationGeneration != _traktGeneration || !_traktActive || !ReferenceEquals(displayCoordinator, AppServices.Trakt) || _selectedKey != displayKey || !IsTraktKey(_selectedKey) || !string.Equals(currentAccount, account, StringComparison.Ordinal)) return;

            if (AppServices.TraktProjection is { } projection)
            {
                if (_selectedKey == TraktWatchlistKey)
                {
                    var rawList = trakt.GetWatchlist(account);
                    var projected = projection.ProjectWatchlist(rawList, snapshot);
                    var mapped = await Task.Run(() => projected.Select(x => CreateMediaItemFromTrakt(x.DisplayTitle, x.Year, x.Type, x.MatchedLocalId, x.MatchedLocalKind, x.Availability, x.RemoteIdentity?.TmdbId, snapshot)).ToList(), displayToken);
                    loadedItems.AddRange(mapped);
                }
                else if (ParseTraktListId(_selectedKey) is long id)
                {
                    var rawList = trakt.GetListItems(id, account);
                    var projected = projection.ProjectListItems(rawList, snapshot);
                    var mapped = await Task.Run(() => projected.Select(x => CreateMediaItemFromTrakt(x.DisplayTitle, x.Year, x.Type, x.MatchedLocalId, x.MatchedLocalKind, x.Availability, x.RemoteIdentity?.TmdbId, snapshot)).ToList(), displayToken);
                    loadedItems.AddRange(mapped);
                }
            }
        }

        var filtered = _query.Length == 0
            ? loadedItems
            : loadedItems.Where(x => MediaTitleSearch.Matches(x.Title, x.CanonicalTitle, x.Year, _query)).ToList();

        ListGrid.ItemsSource = filtered;
        ListCountText.Text = FormatCount(filtered.Count);
        ListGrid.Visibility = filtered.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        EmptyState.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Title = LocalizationService.Text(_query.Length == 0 ? "TraktListEmpty" : "NoResults");
        EmptyState.Message = LocalizationService.Text(_query.Length == 0 ? "TraktListOfflineHelp" : "NoResultsHelp");
        EmptyState.IsActionVisible = false;

        TraktTabCount.Text = filtered.Count > 0 ? LocalizationService.Number(filtered.Count) : string.Empty;
        foreach (var (_, _, count, key) in _traktPlaylistsTabs)
            count.Text = key == _selectedKey ? LocalizationService.Number(filtered.Count) : count.Text;
    }

    private static ListMediaItem CreateMediaItemFromTrakt(
        string displayTitle,
        int? year,
        string type,
        int? matchedLocalId,
        TraktMediaKind? matchedKind,
        TraktAvailability availability,
        long? tmdbId,
        TraktLocalSnapshot? snapshot)
    {
        var isShow = string.Equals(type, "show", StringComparison.OrdinalIgnoreCase) || matchedKind == TraktMediaKind.Show;

        if (matchedLocalId is int localId && localId > 0)
        {
            if (isShow)
            {
                var show = AppServices.Tv.ListShows().FirstOrDefault(x => x.Id == localId);
                if (show is not null)
                {
                    var card = TvProjection.ToCard(show);
                    var isLocal = card.LocalEpisodeCount > 0;
                    var badge = LocalizationService.Text(isLocal ? "Local" : "Unavailable");
                    return new ListMediaItem(card, showLocalBadge: true, isLocal: isLocal, localBadgeText: badge);
                }
            }
            else
            {
                var movie = AppServices.Library.ListMovies().FirstOrDefault(x => x.Id == localId);
                if (movie is not null)
                {
                    var card = LibraryProjection.ToCard(movie);
                    var isLocal = card.HasLocalFile;
                    var badge = LocalizationService.Text(isLocal ? "Local" : "Unavailable");
                    return new ListMediaItem(card, showLocalBadge: true, isLocal: isLocal, localBadgeText: badge);
                }
            }
        }

        string? posterRef = null;
        int movieDbId = 0;
        if (tmdbId is > 0)
        {
            if (snapshot is not null)
            {
                var candidate = snapshot.ForKind(isShow ? TraktMediaKind.Show : TraktMediaKind.Movie)
                    .FirstOrDefault(c => c.Identity.TmdbId == tmdbId);
                if (candidate is not null && candidate.LocalId > 0)
                {
                    if (isShow)
                    {
                        var show = AppServices.Tv.ListShows().FirstOrDefault(x => x.Id == candidate.LocalId);
                        if (show is not null)
                        {
                            var card = TvProjection.ToCard(show);
                            var isLocal = card.LocalEpisodeCount > 0;
                            var badge = LocalizationService.Text(isLocal ? "Local" : "Unavailable");
                            return new ListMediaItem(card, showLocalBadge: true, isLocal: isLocal, localBadgeText: badge);
                        }
                    }
                    else
                    {
                        var movie = AppServices.Library.ListMovies().FirstOrDefault(x => x.Id == candidate.LocalId);
                        if (movie is not null)
                        {
                            var card = LibraryProjection.ToCard(movie);
                            var isLocal = card.HasLocalFile;
                            var badge = LocalizationService.Text(isLocal ? "Local" : "Unavailable");
                            return new ListMediaItem(card, showLocalBadge: true, isLocal: isLocal, localBadgeText: badge);
                        }
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(posterRef))
            {
                if (_traktPosterCache.TryGetValue(tmdbId.Value, out var cachedRef))
                {
                    posterRef = cachedRef;
                }
                else if (AppServices.Metadata is { IsConfigured: true })
                {
                    try
                    {
                        var meta = AppServices.Metadata.GetMovie(tmdbId.Value.ToString());
                        posterRef = meta?.PosterReference;
                        _traktPosterCache[tmdbId.Value] = posterRef;
                    }
                    catch
                    {
                        _traktPosterCache[tmdbId.Value] = null;
                    }
                }
            }
        }

        if (isShow)
        {
            var placeholderShow = new TVShowRecord(0, displayTitle, year ?? 0, [], string.Empty, [], null, posterRef);
            return new ListMediaItem(placeholderShow, showLocalBadge: true, isLocal: false, localBadgeText: LocalizationService.Text("Unavailable"));
        }

        var placeholderMovie = new MovieRecord(
            movieDbId,
            displayTitle,
            year ?? 0,
            string.Empty,
            0,
            [],
            string.Empty,
            HasLocalFile: false,
            PosterReference: posterRef);

        return new ListMediaItem(placeholderMovie, showLocalBadge: true, isLocal: false, localBadgeText: LocalizationService.Text("Unavailable"));
    }

    private static long? ParseTraktListId(string key) => key.StartsWith("trakt:list:", StringComparison.Ordinal) && long.TryParse(key.AsSpan(11), out var id) && id > 0 ? id : null;

    private static string SafeTraktListName(ITraktUiActions? trakt, string? account, string key)
    {
        try { return trakt?.GetLists(account).FirstOrDefault(x => $"trakt:list:{x.TraktId}" == key)?.Name ?? LocalizationService.Text("TraktList"); }
        catch { return LocalizationService.Text("TraktList"); }
    }

    private async void SyncTraktButton_Click(object sender, RoutedEventArgs e)
    {
        try { await DiscoverTraktAsync(selectedOnly: false); } catch (Exception error) { if (_traktActive) await ReportAsync(error); }
    }

    private async Task RefreshSelectedTraktAsync()
    {
        if (!IsTraktKey(_selectedKey)) return;
        await DiscoverTraktAsync(true);
    }

    private async Task DiscoverTraktAsync(bool selectedOnly = false)
    {
        if (_traktBusy || !_traktActive) return;
        var trakt = AppServices.Trakt;
        if (trakt is null || !trakt.GetSyncState().AuthState.IsConnected) return;
        _traktBusy = true;
        SyncTraktButton.IsEnabled = AddMoviesButton.IsEnabled = false;
        var generation = ++_traktGeneration;
        _traktCts?.Cancel(); _traktCts?.Dispose(); _traktCts = new CancellationTokenSource();
        try
        {
            TraktSyncResult? fullResult = null;
            TraktListRefreshResult? listResult = null;
            var state = trakt.GetSyncState();
            var account = state.AuthState.IsConnected ? state.CurrentAccountId : null;
            if (selectedOnly && IsTraktKey(_selectedKey) && trakt is ITraktListActions actions && !string.IsNullOrWhiteSpace(account))
                listResult = await actions.RefreshListAsync(ParseTraktListId(_selectedKey), account, _traktCts.Token);
            else
                fullResult = await trakt.SyncAsync(_traktCts.Token);
            if (!_traktActive || generation != _traktGeneration || !ReferenceEquals(trakt, AppServices.Trakt)) return;
            if (fullResult is { Success: false }) { await ShowMessageAsync(LocalizationService.Text("TraktLists"), fullResult.AbortReason ?? LocalizationService.Text("Failed")); return; }
            if (listResult is { Success: false }) { await ShowMessageAsync(LocalizationService.Text("TraktLists"), listResult.Error ?? LocalizationService.Text("Failed")); return; }
            RebuildTabs(); Refresh();
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (_traktActive && ReferenceEquals(trakt, AppServices.Trakt)) await ReportAsync(error); }
        finally
        {
            _traktBusy = false;
            if (_traktActive) { SyncTraktButton.IsEnabled = true; AddMoviesButton.IsEnabled = true; }
        }
    }

    private async Task ShowAddTraktAsync()
    {
        if (_traktBusy) return;
        _traktBusy = true;
        SyncTraktButton.IsEnabled = AddMoviesButton.IsEnabled = false;
        _traktWriteCts?.Cancel(); _traktWriteCts?.Dispose(); _traktWriteCts = new CancellationTokenSource();
        try { await ShowAddTraktCoreAsync(_traktWriteCts.Token); }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (_traktActive) await ReportAsync(error); }
        finally { _traktBusy = false; _traktWriteCts?.Dispose(); _traktWriteCts = null; if (_traktActive) { SyncTraktButton.IsEnabled = AddMoviesButton.IsEnabled = true; } }
    }

    private async Task ShowAddTraktCoreAsync(CancellationToken operationToken)
    {
        var trakt = AppServices.Trakt;
        var snapshotBuilder = AppServices.TraktLocalSnapshot;
        var state = trakt?.GetSyncState();
        var account = state?.CurrentAccountId;
        if (trakt is null || trakt is not ITraktListActions writer || snapshotBuilder is null || state?.AuthState.IsConnected != true || string.IsNullOrWhiteSpace(account)) return;
        var destinationKey = _selectedKey;
        var destinationListId = destinationKey == TraktWatchlistKey ? null : ParseTraktListId(destinationKey);
        if (destinationKey != TraktWatchlistKey && destinationListId is null) return;
        var operationGeneration = _traktGeneration;
        var snapshot = await snapshotBuilder.BuildSnapshotAsync(operationToken);
        if (operationGeneration != _traktGeneration || !string.Equals(account, AppServices.Trakt?.GetSyncState().CurrentAccountId, StringComparison.Ordinal) || _selectedKey != destinationKey) return;
        var ids = snapshot.Candidates.Where(x => x.Kind == TraktMediaKind.Movie && x.LocalId > 0 && x.Identity.TmdbId is > 0).ToDictionary(x => x.LocalId, x => x.Identity.TmdbId!.Value);
        var context = new TraktListRequestContext(destinationKey, account, trakt, operationGeneration);
        bool IsCurrent() => context.IsCurrent(_traktActive, _selectedKey, AppServices.Trakt?.GetSyncState().CurrentAccountId, AppServices.Trakt, _traktGeneration, operationToken.IsCancellationRequested) && trakt.GetSyncState().AuthState.IsConnected;
        var existingTmdbIds = new HashSet<int>();
        if (AppServices.TraktProjection is { } projection)
        {
            if (destinationKey == TraktWatchlistKey)
            {
                var rawList = trakt.GetWatchlist(account);
                var projected = projection.ProjectWatchlist(rawList, snapshot);
                foreach (var p in projected) { if (p.RemoteIdentity?.TmdbId is int tmdb && tmdb > 0) existingTmdbIds.Add(tmdb); }
            }
            else if (destinationListId is long listId)
            {
                var rawList = trakt.GetListItems(listId, account);
                var projected = projection.ProjectListItems(rawList, snapshot);
                foreach (var p in projected) { if (p.RemoteIdentity?.TmdbId is int tmdb && tmdb > 0) existingTmdbIds.Add(tmdb); }
            }
        }
        var entries = await Task.Run(() => AppServices.Library.ListMovies().Where(x => ids.ContainsKey(x.Id) && !x.HasPendingMetadata && !x.ReviewMatch && (!ids.TryGetValue(x.Id, out var tmdb) || !existingTmdbIds.Contains(tmdb))).Select(x => new MediaPickerEntry(CustomListMediaKind.Movie, x.Id, x.Title, x.Year, false)).ToArray(), operationToken);
        if (!IsCurrent()) return;
        var picks = await AddMediaToListDialog.ShowAsync(XamlRoot, entries);
        if (!IsCurrent() || picks.Count == 0) return;
        var listName = destinationKey == TraktWatchlistKey ? LocalizationService.Text("TraktWatchlist") : SafeTraktListName(trakt, account, destinationKey);
        var dialog = new ContentDialog { XamlRoot = XamlRoot, RequestedTheme = ThemeService.ElementTheme, FlowDirection = LocalizationService.FlowDirection, Title = LocalizationService.Text("ConfirmTraktAdd"), Content = string.Format(LocalizationService.Text("ConfirmTraktAddMessage"), listName, picks.Count), PrimaryButtonText = LocalizationService.Text("Add"), CloseButtonText = LocalizationService.Text("Cancel") };
        if (await ContentDialogCoordinator.ShowAsync(dialog) != ContentDialogResult.Primary) return;
        if (!IsCurrent()) return;
        foreach (var pick in picks)
        {
            if (!IsCurrent()) return;
            if (!ids.TryGetValue(pick.Id, out var tmdb)) continue;
            var result = await writer.AddMovieToListAsync(destinationListId, tmdb, account, operationToken);
            if (!IsCurrent()) return;
            if (!result.IsConfirmed) { await ShowMessageAsync(LocalizationService.Text("TraktLists"), result.Error ?? LocalizationService.Text("TraktAddFailed")); break; }
            if (!result.CacheRefreshSucceeded) { await ShowMessageAsync(LocalizationService.Text("TraktLists"), LocalizationService.Text("TraktAddCacheStale")); break; }
        }
        RefreshTrakt();
    }

    public void Deactivate()
    {
        _traktActive = false;
        ++_traktGeneration;
        _traktCts?.Cancel();
        _traktWriteCts?.Cancel();
        _traktDisplayCts?.Cancel();
        ++_traktDisplayGeneration;
    }

    private void CancelTraktDisplay()
    {
        ++_traktGeneration;
        _traktCts?.Cancel();
        _traktWriteCts?.Cancel();
        _traktDisplayCts?.Cancel();
        ++_traktDisplayGeneration;
    }

    private void UpdateTraktSyncVisibility()
    {
        var trakt = AppServices.Trakt;
        var isConnected = trakt?.GetSyncState().AuthState.IsConnected == true;
        SyncTraktButton.Visibility = isConnected ? Visibility.Visible : Visibility.Collapsed;
        SyncTraktButton.IsEnabled = isConnected && !_traktBusy;
    }
}
