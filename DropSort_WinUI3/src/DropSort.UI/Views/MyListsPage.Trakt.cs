using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DropSort.Application.Trakt;
using DropSort.Domain.Library.Personal;
using DropSort.UI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DropSort.UI.Views;

public sealed partial class MyListsPage
{
    private const string TraktWatchlistKey = "trakt:watchlist";
    private readonly List<(RadioButton Tab, TextBlock Label, TextBlock Count, string Key)> _traktTabs = [];
    private readonly List<TraktListCard> _traktCards = [];
    private CancellationTokenSource? _traktCts;
    private CancellationTokenSource? _traktWriteCts;
    private CancellationTokenSource? _traktDisplayCts;
    private long _traktDisplayGeneration;
    private long _traktGeneration;
    private string? _traktAccount;
    private bool _traktActive;
    private bool _traktBusy;

    private bool IsTraktKey(string key) => key.StartsWith("trakt:", StringComparison.Ordinal);

    private void RebuildTraktTabs()
    {
        foreach (var (tab, _, _, _) in _traktTabs) ListSelector.Children.Remove(tab);
        _traktTabs.Clear();
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
        var defs = new List<(string Key, string Name, int Count)> { (TraktWatchlistKey, LocalizationService.Text("TraktWatchlist"), watchCount) };
        foreach (var list in lists) defs.Add(($"trakt:list:{list.TraktId}", list.Name, list.ItemCount));
        if (IsTraktKey(_selectedKey) && !defs.Any(x => x.Key == _selectedKey)) _selectedKey = TraktWatchlistKey;
        foreach (var def in defs)
        {
            var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Text = "Trakt · " + def.Name };
            var count = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Text = string.Empty, Style = (Style)App.Current.Resources["SecondaryCaptionTextStyle"] };
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            content.Children.Add(new FontIcon { FontSize = 14, Glyph = "", FontFamily = (FontFamily)App.Current.Resources["SymbolThemeFontFamily"] });
            content.Children.Add(label); content.Children.Add(count);
            var tab = new RadioButton { GroupName = "MyListsSelector", Style = (Style)App.Current.Resources["TabStripItemStyle"], Tag = def.Key, Content = content };
            tab.Checked += ListTab_Checked;
            ListSelector.Children.Add(tab);
            _traktTabs.Add((tab, label, count, def.Key));
        }
    }

    private void SyncTraktSelectedTab()
    {
        _syncing = true;
        foreach (var (tab, _, _, key) in _traktTabs)
            if (key == _selectedKey && tab.IsChecked != true) tab.IsChecked = true;
        _syncing = false;
    }

    private async void RefreshTrakt()
    {
        try { await RefreshTraktAsync(); }
        catch (OperationCanceledException) { }
        catch (Exception) { if (_traktActive && IsTraktKey(_selectedKey)) { TraktGrid.ItemsSource = null; ListCountText.Text = LocalizationService.Text("Failed"); } }
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
        var displayKey = _selectedKey;
        SyncTraktSelectedTab();
        ListGrid.Visibility = Visibility.Collapsed;
        ContinueGrid.Visibility = Visibility.Collapsed;
        HistoryGrid.Visibility = Visibility.Collapsed;
        TraktGrid.Visibility = Visibility.Visible;
        TraktGrid.ItemsSource = null;
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
        SyncTraktButton.Visibility = canWrite ? Visibility.Visible : Visibility.Collapsed;
        AddMoviesButton.IsEnabled = canWrite && !_traktBusy;
        SyncTraktButton.IsEnabled = canWrite && !_traktBusy;
        TraktListsButton.IsEnabled = state?.AuthState.IsConnected == true && !_traktBusy;
        AddMoviesButtonText.Text = LocalizationService.Text("AddToTrakt");
        TraktListsButton.Visibility = Visibility.Visible;
        var loadedCards = new List<TraktListCard>();
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
                    loadedCards.AddRange(projection.ProjectWatchlist(trakt.GetWatchlist(account), snapshot).Select(TraktListCard.From));
                else if (ParseTraktListId(_selectedKey) is long id)
                    loadedCards.AddRange(projection.ProjectListItems(trakt.GetListItems(id, account), snapshot).Select(TraktListCard.From));
            }
        }
        _traktCards.Clear();
        _traktCards.AddRange(loadedCards);
        var cards = _query.Length == 0 ? loadedCards : loadedCards.Where(x => x.Title.Contains(_query, StringComparison.CurrentCultureIgnoreCase)).ToList();
        TraktGrid.ItemsSource = cards;
        ListCountText.Text = FormatCount(cards.Count);
        EmptyState.Visibility = cards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Title = LocalizationService.Text(_query.Length == 0 ? "TraktListEmpty" : "NoResults");
        EmptyState.Message = LocalizationService.Text(_query.Length == 0 ? "TraktListOfflineHelp" : "NoResultsHelp");
        EmptyState.IsActionVisible = false;
        foreach (var (_, _, count, key) in _traktTabs) count.Text = key == _selectedKey ? LocalizationService.Number(cards.Count) : string.Empty;
    }

    private static long? ParseTraktListId(string key) => key.StartsWith("trakt:list:", StringComparison.Ordinal) && long.TryParse(key.AsSpan(11), out var id) && id > 0 ? id : null;

    private static string SafeTraktListName(ITraktUiActions? trakt, string? account, string key)
    {
        try { return trakt?.GetLists(account).FirstOrDefault(x => $"trakt:list:{x.TraktId}" == key)?.Name ?? LocalizationService.Text("TraktList"); }
        catch { return LocalizationService.Text("TraktList"); }
    }

    private static string AvailabilityText(TraktAvailability availability) => availability switch
    {
        TraktAvailability.Available => LocalizationService.Text("Local"),
        TraktAvailability.Partial => LocalizationService.Text("TraktPartialAvailability"),
        TraktAvailability.Missing => LocalizationService.Text("TraktMissingAvailability"),
        _ => LocalizationService.Text("TraktUnknownAvailability")
    };

    private async void TraktListsButton_Click(object sender, RoutedEventArgs e)
    {
        try { await DiscoverTraktAsync(); } catch (Exception error) { if (_traktActive) await ReportAsync(error); }
    }
    private async void SyncTraktButton_Click(object sender, RoutedEventArgs e)
    {
        try { await RefreshSelectedTraktAsync(); } catch (Exception error) { if (_traktActive) await ReportAsync(error); }
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
        TraktListsButton.IsEnabled = SyncTraktButton.IsEnabled = AddMoviesButton.IsEnabled = false;
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
            if (_traktActive) { TraktListsButton.IsEnabled = true; SyncTraktButton.IsEnabled = true; AddMoviesButton.IsEnabled = true; }
        }
    }

    private async void TraktGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        try
        {
        var key = _selectedKey;
        var coordinator = AppServices.Trakt;
        var generation = _traktDisplayGeneration;
        bool Current() => _traktActive && _selectedKey == key && generation == _traktDisplayGeneration && ReferenceEquals(coordinator, AppServices.Trakt);
        if (e.ClickedItem is not TraktListCard card || card.LocalId is not int localId) return;
        if (card.Type.Equals("movie", StringComparison.OrdinalIgnoreCase))
        {
            var movie = await Task.Run(() => AppServices.Library.ListMovies().FirstOrDefault(x => x.Id == localId));
            if (Current() && movie is not null) MovieSelected?.Invoke(this, LibraryProjection.ToCard(movie));
        }
        else if (card.Type.Equals("show", StringComparison.OrdinalIgnoreCase))
        {
            var show = await Task.Run(() => AppServices.Tv.ListShows().FirstOrDefault(x => x.Id == localId));
            if (Current() && show is not null) ShowSelected?.Invoke(this, TvProjection.ToCard(show));
        }
        }
        catch (Exception error) { if (_traktActive) await ReportAsync(error); }
    }

    private async Task ShowAddTraktAsync()
    {
        if (_traktBusy) return;
        _traktBusy = true;
        TraktListsButton.IsEnabled = SyncTraktButton.IsEnabled = AddMoviesButton.IsEnabled = false;
        _traktWriteCts?.Cancel(); _traktWriteCts?.Dispose(); _traktWriteCts = new CancellationTokenSource();
        try { await ShowAddTraktCoreAsync(_traktWriteCts.Token); }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (_traktActive) await ReportAsync(error); }
        finally { _traktBusy = false; _traktWriteCts?.Dispose(); _traktWriteCts = null; if (_traktActive) { TraktListsButton.IsEnabled = SyncTraktButton.IsEnabled = AddMoviesButton.IsEnabled = true; } }
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
        var entries = await Task.Run(() => AppServices.Library.ListMovies().Where(x => ids.ContainsKey(x.Id) && !x.HasPendingMetadata && !x.ReviewMatch).Select(x => new MediaPickerEntry(CustomListMediaKind.Movie, x.Id, x.Title, x.Year, false)).ToArray(), operationToken);
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

    private string RefreshTraktName(ITraktUiActions trakt, string account) => trakt.GetLists(account).FirstOrDefault(x => $"trakt:list:{x.TraktId}" == _selectedKey)?.Name ?? LocalizationService.Text("TraktList");

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

    private sealed record TraktListCard(string Title, string Year, string Type, string Availability, int? LocalId, long? TmdbId)
    {
        public static TraktListCard From(TraktProjectedListItem x) => new(x.DisplayTitle, x.Year?.ToString() ?? string.Empty, x.Type, AvailabilityText(x.Availability), x.MatchedLocalId, x.RemoteIdentity?.TmdbId);
        public static TraktListCard From(TraktProjectedWatchlistItem x) => new(x.DisplayTitle, x.Year?.ToString() ?? string.Empty, x.Type, AvailabilityText(x.Availability), x.MatchedLocalId, x.RemoteIdentity?.TmdbId);
    }
}
