using System.IO;
using DropSort.UI.Models;
using DropSort.UI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace DropSort.UI.Views;

/// <summary>
/// TV show details.
/// <para>
/// The hierarchy comes from the catalog: the show is re-read by id on every navigation, its seasons and
/// episodes are the persisted rows, and an episode's state is derived from the media files registered
/// against it. Play and Open Folder act on the registered file; an episode whose file has disappeared
/// stays listed and is marked missing rather than being dropped.
/// </para>
/// <para>
/// Watched badges come from persisted episode watch events. The season line and the progress figure
/// continue to report local availability, while Play remains separate from the explicit watch action.
/// </para>
/// </summary>
public sealed partial class TVShowDetailsPage : Page, ILocalizableView, IDeactivatableView
{
    private TVShowRecord _show = new(0, string.Empty, 0, [], string.Empty, []);
    private string _returnDestination = "library";
    private bool _isMatching;
    private bool _showFavorite;
    private bool _showWatchLater;
    private bool _showPersonalAvailable;
    private readonly PostHandoffRequestLifetime _metadataRequests = new();
    private readonly PostHandoffRequestLifetime _posterRequests = new();
    private readonly PostHandoffRequestLifetime _enrichmentRequests = new();
    private readonly PostHandoffRequestLifetime _availabilityRequests = new();
    private readonly PostHandoffRequestLifetime _actionRequests = new();

    public TVShowDetailsPage()
    {
        InitializeComponent();
        Unloaded += (_, _) => Deactivate();
        SetShow(_show);
    }

    public event EventHandler? BackRequested;

    public void Deactivate()
    {
        _metadataRequests.Cancel();
        _posterRequests.Cancel();
        _enrichmentRequests.Cancel();
        _availabilityRequests.Cancel();
        _actionRequests.Cancel();
    }

    public void SetReturnDestination(string destination)
    {
        _returnDestination = destination;
        UpdateBackLabel();
    }

    /// <summary>Loads one show's hierarchy from the catalog.</summary>
    public bool IsLoadedSuccessfully { get; private set; }

    public void LoadShow(int showId)
    {
        IsLoadedSuccessfully = false;
        try
        {
            SetShow(TvProjection.ToDetails(AppServices.Tv.GetShowDetails(showId)));
            BeginSeasonEnrichment(showId);
            IsLoadedSuccessfully = true;
        }
        catch (Exception error)
        {
            SetShow(new TVShowRecord(0, string.Empty, 0, [], string.Empty, []));
            _ = ShowMessageAsync(LocalizationService.Text("Failed"), MetadataErrorText.For(error));
        }
    }

    public void SetShow(TVShowRecord show)
    {
        _metadataRequests.Cancel();
        _posterRequests.Cancel();
        _enrichmentRequests.Cancel();
        _availabilityRequests.Cancel();
        _actionRequests.Cancel();
        SetMatchingState(false);
        _show = show;
        ReloadShowPersonalState();
        ShowTitleText.Text = show.Title;
        OverviewText.Text = show.Overview;

        // A show registered from file names has no overview yet; a heading with nothing under it reads
        // as a rendering fault, so the block goes away until there is text for it.
        var hasOverview = !string.IsNullOrWhiteSpace(show.Overview);
        OverviewText.Visibility = hasOverview ? Visibility.Visible : Visibility.Collapsed;

        UpdateShowSummary();
        MatchButton.IsEnabled = !_isMatching;
        UpdatePosterVisuals();
        ApplyLocalization();
    }

    public void ApplyLocalization()
    {
        Root.FlowDirection = LocalizationService.FlowDirection;
        HeroGrid.FlowDirection = FlowDirection.LeftToRight;
        HeroCopyPanel.FlowDirection = LocalizationService.FlowDirection;
        ReadingPane.FlowDirection = LocalizationService.FlowDirection;

        UpdateBackLabel();
        KindText.Text = LocalizationService.Text("TvShow");
        ProgressLabelText.Text = LocalizationService.Text("EpisodesOnDisk");
        PlayNextText.Text = LocalizationService.Text("PlayNextEpisode");
        AddToListText.Text = LocalizationService.Text("AddToList");
        UpdateShowPersonalVisuals();
        MoreButton.SetValue(AutomationProperties.NameProperty, LocalizationService.Text("MoreOptions"));
        ToolTipService.SetToolTip(MoreButton, LocalizationService.Text("MoreOptions"));
        MarkShowWatchedItem.Text = LocalizationService.Text("MarkWatched");
        OpenShowFolderItem.Text = LocalizationService.Text("OpenFolder");
        RemoveShowFromLibraryItem.Text = LocalizationService.Text("RemoveFromLibrary");
        RemoveShowFromLibraryItem.IsEnabled = _show.Id > 0;
        SeasonsTitleText.Text = LocalizationService.Text("Seasons");
        SeasonsHelpText.Text = LocalizationService.Text("SeasonsHelp");

        UpdateShowSummary();
        SeasonsRepeater.ItemsSource = BuildSeasons(_show);
        UpdateMatchButtonVisuals();
        ProbeEpisodeStorageAvailability(_show);
    }

    private void UpdateShowSummary()
    {
        var seasonLabel = LocalizationService.Format(
            _show.SeasonCount == 1 ? "SeasonCountFormat" : "SeasonsCountFormat",
            _show.SeasonCount);
        var episodeLabel = $"{LocalizationService.Number(_show.EpisodeCount)} {LocalizationService.Text("Episodes")}";
        ShowMetaText.Text = string.Join(
            " · ",
            new[] { _show.MetaLine, seasonLabel, episodeLabel }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
        ProgressValueText.Text = _show.ProgressLine;
        var next = _show.NextPlayableEpisode;
        PlayNextButton.IsEnabled = next is not null;
        PlayNextEpisodeText.Text = next is null
            ? string.Empty
            : LocalizationService.Digits(LocalizationService.Format(
                "HomeContinueEpisodeFormat",
                next.SeasonNumber,
                next.EpisodeNumber,
                next.Episode.Title));
        PlayNextEpisodeText.Visibility = next is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ReloadShowPersonalState()
    {
        _showFavorite = false;
        _showWatchLater = false;
        _showPersonalAvailable = false;
        if (_show.Id <= 0) return;
        try
        {
            var state = AppServices.ShowPersonal.GetState(_show.Id);
            _showFavorite = state.IsFavorite;
            _showWatchLater = state.WatchLater;
            _showPersonalAvailable = true;
        }
        catch (Exception error)
        {
            _ = ShowMessageAsync(LocalizationService.Text("Failed"), MetadataErrorText.For(error));
        }
    }

    private void UpdateShowPersonalVisuals()
    {
        ShowFavoriteButton.IsEnabled = _showPersonalAvailable;
        ShowWatchLaterButton.IsEnabled = _showPersonalAvailable;
        ShowFavoriteText.Text = LocalizationService.Text(_showFavorite ? "InFavorites" : "AddToFavorites");
        ShowWatchLaterText.Text = LocalizationService.Text(_showWatchLater ? "InWatchLater" : "AddToWatchLater");
        ShowFavoriteGlyph.Glyph = _showFavorite ? "\uEB52" : "\uEB51";
        ShowWatchLaterGlyph.Glyph = _showWatchLater ? "\uE735" : "\uE734";
        ShowFavoriteButton.Style = (Style)Resources[_showFavorite ? "TvDetailsSelectedButtonStyle" : "TvDetailsButtonStyle"];
        ShowWatchLaterButton.Style = (Style)Resources[_showWatchLater ? "TvDetailsSelectedButtonStyle" : "TvDetailsButtonStyle"];
        ToolTipService.SetToolTip(ShowFavoriteButton, LocalizationService.Text(_showFavorite ? "RemoveFromFavorites" : "AddToFavorites"));
        ToolTipService.SetToolTip(ShowWatchLaterButton, LocalizationService.Text(_showWatchLater ? "RemoveFromWatchLater" : "AddToWatchLater"));
    }

    private void ShowFavoriteButton_Click(object sender, RoutedEventArgs e) => ToggleShowPersonal(favorite: true);
    private void ShowWatchLaterButton_Click(object sender, RoutedEventArgs e) => ToggleShowPersonal(favorite: false);

    private void ToggleShowPersonal(bool favorite)
    {
        if (_show.Id <= 0) return;
        try
        {
            var current = AppServices.ShowPersonal.GetState(_show.Id);
            var saved = favorite
                ? AppServices.ShowPersonal.SetFavorite(_show.Id, !current.IsFavorite)
                : AppServices.ShowPersonal.SetWatchLater(_show.Id, !current.WatchLater);
            _showFavorite = saved.IsFavorite;
            _showWatchLater = saved.WatchLater;
            _showPersonalAvailable = true;
            UpdateShowPersonalVisuals();
        }
        catch (Exception error)
        {
            _ = ShowMessageAsync(LocalizationService.Text("Failed"), MetadataErrorText.For(error));
        }
    }

    /// <summary>
    /// Confirms off the UI thread which episode files sit on unreachable storage, probing each distinct
    /// root once. Present files on an offline drive stay PRESENT in the catalog, so the rows are rebuilt
    /// to show "storage unavailable" and to disable file-open actions (Mark Watched stays available). A
    /// stale result for a show the page has navigated away from is ignored.
    /// </summary>
    private void ProbeEpisodeStorageAvailability(TVShowRecord show)
    {
        var paths = show.Seasons
            .SelectMany(season => season.Episodes)
            .Where(episode => episode.HasLocalFile && !string.IsNullOrWhiteSpace(episode.FilePath))
            .Select(episode => episode.FilePath!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (paths.Length == 0) return;

        var showId = show.Id;
        var request = _availabilityRequests.Begin();
        _ = Task.Run(() =>
        {
            IReadOnlySet<string> unavailable;
            try
            {
                unavailable = AppServices.UnavailableStoragePaths(paths, request.Token);
            }
            catch (OperationCanceledException) { return; }
            if (unavailable.Count == 0) return;
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!request.IsCurrent || _show.Id != showId) return;
                SeasonsRepeater.ItemsSource = BuildSeasons(_show, unavailable);
            });
        }, request.Token);
    }

    /// <summary>
    /// Enriches the already-rendered local hierarchy with provider-confirmed season/episode data, off the UI
    /// thread. The page stays fully usable while this runs, and it is never required for the page to open. A
    /// result is applied only while it is still the current request for the same show, so navigating away or
    /// reloading can never let a stale provider response update the wrong page. Provider slowness, offline
    /// state, timeout, or failure returns nothing and the local representation is kept.
    /// </summary>
    private void BeginSeasonEnrichment(int showId)
    {
        if (showId <= 0) return;
        var request = _enrichmentRequests.Begin();
        _ = Task.Run(async () =>
        {
            try
            {
                var enriched = await AppServices.Tv.GetShowDetailsEnrichedAsync(showId, request.Token);
                if (enriched is null || !request.IsCurrent) return;
                var record = TvProjection.ToDetails(enriched);
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (request.IsCurrent && _show.Id == showId)
                    {
                        ApplyEnrichedShow(record);
                    }
                });
            }
            catch
            {
                // Keep the local representation: provider work must never disturb an open page.
            }
        });
    }

    /// <summary>
    /// Applies a provider-enriched hierarchy to the open page: the seasons/episodes, the on-disk progress
    /// figure, and the Play-next state are refreshed from it. Title, overview, poster, and matching state are
    /// unchanged because enrichment reads the same show row. It never cancels in-flight requests.
    /// </summary>
    private void ApplyEnrichedShow(TVShowRecord enriched)
    {
        _show = enriched;
        UpdateShowSummary();
        SeasonsRepeater.ItemsSource = BuildSeasons(_show);
        ProbeEpisodeStorageAvailability(_show);
    }

    /// <summary>
    /// Builds the "Add to list" menu each time it opens, listing the user's custom lists only, each with a
    /// check for this show's current membership. The list id is kept on the item and never shown. Toggling
    /// adds or removes the show through the personal library by stable list id.
    /// </summary>
    private void AddToListFlyout_Opening(object sender, object e)
    {
        AddToListFlyout.Items.Clear();
        if (_show.Id <= 0) return;

        IReadOnlyList<DropSort.Application.Dto.CustomList> lists;
        IReadOnlySet<int> memberIds;
        try
        {
            lists = AppServices.Personal.ListCustomLists();
            memberIds = AppServices.Personal.GetCustomListIdsForShow(_show.Id);
        }
        catch (Exception)
        {
            AddToListFlyout.Items.Add(new MenuFlyoutItem { Text = LocalizationService.Text("NoCustomLists"), IsEnabled = false });
            return;
        }

        if (lists.Count == 0)
        {
            AddToListFlyout.Items.Add(new MenuFlyoutItem { Text = LocalizationService.Text("NoCustomLists"), IsEnabled = false });
            return;
        }

        foreach (var list in lists)
        {
            var item = new ToggleMenuFlyoutItem { Text = list.Name, IsChecked = memberIds.Contains(list.Id), Tag = list.Id };
            item.Click += ToggleCustomListFromMenu;
            AddToListFlyout.Items.Add(item);
        }
    }

    private void ToggleCustomListFromMenu(object sender, RoutedEventArgs e)
    {
        if (_show.Id <= 0 || sender is not ToggleMenuFlyoutItem { Tag: int listId }) return;
        try
        {
            // Decide from stored membership, not the toggle's new visual state, so a stale menu is safe.
            if (AppServices.Personal.GetCustomListIdsForShow(_show.Id).Contains(listId))
            {
                AppServices.Personal.RemoveShowFromCustomList(listId, _show.Id);
            }
            else
            {
                AppServices.Personal.AddShowToCustomList(listId, _show.Id);
            }
        }
        catch (Exception error)
        {
            _ = ShowMessageAsync(LocalizationService.Text("Failed"), MetadataErrorText.For(error));
        }
    }

    private void UpdateBackLabel()
    {
        BackText.Text = LocalizationService.Text(_returnDestination switch
        {
            "home" => "BackToHome",
            "lists" => "BackToMyLists",
            _ => "BackToLibrary",
        });
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

    private async void RemoveShowFromLibrary_Click(object sender, RoutedEventArgs e)
    {
        MoreButton.Flyout?.Hide();
        var showId = _show.Id;
        if (showId <= 0) return;
        var request = _actionRequests.Begin();
        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.ElementTheme,
            FlowDirection = LocalizationService.FlowDirection,
            Title = LocalizationService.Text("RemoveFromLibrary"),
            Content = LocalizationService.Text("RemoveShowFromLibraryConfirm"),
            PrimaryButtonText = LocalizationService.Text("RemoveFromLibrary"),
            CloseButtonText = LocalizationService.Text("Cancel"),
            DefaultButton = ContentDialogButton.Close,
        };

        try
        {
            if (await ContentDialogCoordinator.ShowAsync(confirm) != ContentDialogResult.Primary
                || !request.IsCurrent) return;
            RemoveShowFromLibraryItem.IsEnabled = false;
            await Task.Run(() => AppServices.Repair.RemoveShowFromLibrary(showId));
            if (request.IsCurrent) BackRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception error)
        {
            if (request.IsCurrent)
                await ShowMessageAsync(LocalizationService.Text("Failed"), MetadataErrorText.For(error));
        }
        finally
        {
            if (request.IsCurrent) RemoveShowFromLibraryItem.IsEnabled = true;
        }
    }

    /// <summary>Plays the first episode of the show that has a file on disk.</summary>
    private async void PlayNextButton_Click(object sender, RoutedEventArgs e)
    {
        if (_show.NextPlayableEpisode is { } selection)
        {
            await PlayEpisodeAsync(selection.Episode);
        }
    }

    private async void EpisodePlay_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: EpisodeDisplayRecord row })
        {
            await PlayEpisodeAsync(FindEpisode(row));
        }
    }

    private async void EpisodeMarkWatched_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: EpisodeDisplayRecord row }) return;
        var episode = FindEpisode(row);
        if (episode is null || !episode.HasLocalFile || episode.Id <= 0) return;
        try
        {
            AppServices.WatchHistory.RecordEpisodeWatch(episode.Id);
            LoadShow(_show.Id);
        }
        catch (Exception error)
        {
            await ShowMessageAsync(LocalizationService.Text("Failed"), MetadataErrorText.For(error));
        }
    }

    private async void EpisodeOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: EpisodeDisplayRecord row })
        {
            return;
        }

        var episode = FindEpisode(row);

        if (episode?.FilePath is not { Length: > 0 } path)
        {
            ReportMissingFile();
            return;
        }

        var request = _actionRequests.Begin();
        try
        {
            var result = await AppServices.MediaFileActions.OpenContainingFolderAsync(path, request.Token);
            if (!request.IsCurrent) return;
            await PresentActionResultAsync(result.Status);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (request.IsCurrent)
                _ = ShowMessageAsync(LocalizationService.Text("Failed"), MetadataErrorText.For(error));
        }
    }

    /// <summary>
    /// Shows what the catalog knows about the episode's file. The flyout the design puts on a movie's
    /// file block has no episode equivalent, so the same facts are stated in a themed dialog.
    /// </summary>
    private void EpisodeDetails_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: EpisodeDisplayRecord row })
        {
            return;
        }

        var episode = FindEpisode(row);

        if (episode is null)
        {
            return;
        }

        var files = SafeEpisodeFiles(episode.Id);

        var body = files.Count == 0
            ? LocalizationService.Text("NoLocalFileHelp")
            : string.Join(
                Environment.NewLine,
                files.Select(file => file.CurrentPath
                    + Environment.NewLine
                    + LocalizationService.Text(
                        file.Status == DropSort.Domain.Library.Movies.MediaFileStatus.Present
                            ? "Present"
                            : "Missing")));

        _ = ShowMessageAsync(LocalizationService.Text("MediaFile"), body);
    }

    private async Task PlayEpisodeAsync(EpisodeRecord? episode)
    {
        if (episode?.FilePath is not { Length: > 0 } path)
        {
            ReportMissingFile();
            return;
        }

        var request = _actionRequests.Begin();
        try
        {
            var result = await AppServices.PlayMediaFileAsync(path, request.Token);
            if (!request.IsCurrent) return;
            if (result.ErrorCode == "PlaybackActivityNotSaved")
            {
                await ShowMessageAsync(LocalizationService.Text("Failed"), LocalizationService.Text("PlaybackActivityNotSaved"));
                return;
            }
            await PresentActionResultAsync(result.Status);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (request.IsCurrent)
                _ = ShowMessageAsync(LocalizationService.Text("Failed"), MetadataErrorText.For(error));
        }
    }

    private Task PresentActionResultAsync(DropSort.Domain.Library.Availability.AvailabilityInspectionStatus status) =>
        status switch
        {
            DropSort.Domain.Library.Availability.AvailabilityInspectionStatus.Missing =>
                ShowMessageAsync(LocalizationService.Text("NoLocalFile"), LocalizationService.Text("NoLocalFileHelp")),
            DropSort.Domain.Library.Availability.AvailabilityInspectionStatus.StorageUnavailable =>
                ShowMessageAsync(LocalizationService.Text("Failed"), LocalizationService.Text("StorageUnavailable")),
            DropSort.Domain.Library.Availability.AvailabilityInspectionStatus.Error =>
                ShowMessageAsync(LocalizationService.Text("Failed"), LocalizationService.Text("Failed")),
            _ => Task.CompletedTask,
        };

    /// <summary>Resolves a row back to the loaded episode by its catalog id.</summary>
    private EpisodeRecord? FindEpisode(EpisodeDisplayRecord row) => _show.Seasons
        .SelectMany(season => season.Episodes)
        .FirstOrDefault(episode => episode.Id == row.EpisodeId);

    private static IReadOnlyList<DropSort.Domain.Library.Movies.MediaFile> SafeEpisodeFiles(int episodeId)
    {
        try
        {
            return AppServices.Tv.ListEpisodeFiles(episodeId);
        }
        catch (Exception)
        {
            return [];
        }
    }

    private void ReportMissingFile() => _ = ShowMessageAsync(
        LocalizationService.Text("NoLocalFile"),
        LocalizationService.Text("NoLocalFileHelp"));

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

    /// <summary>Each season starts collapsed until the user opens it.</summary>
    private static IReadOnlyList<SeasonDisplayRecord> BuildSeasons(TVShowRecord show, IReadOnlySet<string>? unavailablePaths = null)
    {
        var labels = new EpisodeActionLabels(
            LocalizationService.Text("Watched"),
            LocalizationService.Text("MarkWatched"),
            LocalizationService.Text("Missing"),
            LocalizationService.Text("PlayEpisode"),
            LocalizationService.Text("OpenFolder"),
            LocalizationService.Text("MoreOptions"),
            LocalizationService.Text("StorageUnavailable"));

        return
        [
            // A real TMDB Specials season (0) with no local files is noise in a local library, so it is
            // hidden here; a Specials that actually has files on disk (present or missing) still shows.
            .. show.Seasons
                .Where(season => season.Number != 0 || season.LocalCount > 0 || season.MissingCount > 0)
                .Select(season => new SeasonDisplayRecord(
                show.Id,
                season.Number,
                ShowFormatting.SeasonArtLabel(season.Number),
                ShowFormatting.SeasonLabel(season.Number),
                SeasonMeta(season),
                season.Episodes.Count == 0 ? 0 : 100.0 * season.LocalCount / season.Episodes.Count,
                false,
                [
                    .. season.Episodes.Select(episode => new EpisodeDisplayRecord(
                        show.Id,
                        season.Number,
                        episode.Number,
                        episode.Id,
                        ShowFormatting.EpisodeThumbnailLabel(episode.Number),
                        episode.Title,
                        EpisodeMeta(season.Number, episode),
                        episode.Watched,
                        episode.HasLocalFile,
                        episode.IsFileMissing,
                        labels,
                        StorageUnavailable: episode.HasLocalFile
                            && episode.FilePath is { Length: > 0 } path
                            && unavailablePaths?.Contains(path) == true)),
                ])),
        ];
    }

    /// <summary>The season line names the missing files only when there are some.</summary>
    private static string SeasonMeta(SeasonRecord season) => season.MissingCount == 0
        ? LocalizationService.Format("SeasonMetaFormat", season.Episodes.Count, season.LocalCount)
        : LocalizationService.Format(
            "SeasonMetaMissingFormat",
            season.Episodes.Count,
            season.LocalCount,
            season.MissingCount);

    /// <summary>The episode code, plus the runtime when the catalog knows one.</summary>
    private static string EpisodeMeta(int seasonNumber, EpisodeRecord episode)
    {
        var code = ShowFormatting.EpisodeCode(seasonNumber, episode.Number);

        return LocalizationService.Digits(
            episode.Runtime.Length == 0 ? code : code + " · " + episode.Runtime);
    }

    private void UpdatePosterVisuals()
    {
        if (string.IsNullOrWhiteSpace(_show.PosterReference) || !AppServices.IsAvailable)
        {
            PosterImage.Source = null;
            PosterGlyph.Visibility = Visibility.Visible;
            return;
        }

        var posterRef = _show.PosterReference;
        var cachedPath = AppServices.Poster.GetCachedPosterPath("TMDB", posterRef);
        if (cachedPath != null && File.Exists(cachedPath))
        {
            PosterImage.Source = new BitmapImage(new Uri(cachedPath));
            PosterGlyph.Visibility = Visibility.Collapsed;
        }
        else
        {
            PosterImage.Source = null;
            PosterGlyph.Visibility = Visibility.Visible;

            var request = _posterRequests.Begin();
            _ = Task.Run(async () =>
            {
                try
                {
                    var downloadedPath = await AppServices.Poster.EnsurePosterCachedAsync("TMDB", posterRef, request.Token);
                    if (request.IsCurrent && downloadedPath != null && File.Exists(downloadedPath))
                    {
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            if (request.IsCurrent && _show.PosterReference == posterRef)
                            {
                                PosterImage.Source = new BitmapImage(new Uri(downloadedPath));
                                PosterGlyph.Visibility = Visibility.Collapsed;
                            }
                        });
                    }
                }
                catch (Exception)
                {
                    // Poster artwork is optional. Keep the fallback glyph visible and do not open a
                    // second ContentDialog while MatchMediaDialog may own this XamlRoot.
                }
            });
        }
    }

    private bool HasExternalId()
    {
        return !string.IsNullOrWhiteSpace(_show?.ExternalId);
    }

    private void UpdateMatchButtonVisuals()
    {
        var hasId = HasExternalId();
        MatchText.Text = LocalizationService.Text(hasId ? "RefreshMetadata" : "MatchMetadata");
    }

    private void SetMatchingState(bool isMatching)
    {
        _isMatching = isMatching;
        MatchButton.IsEnabled = !isMatching;
        MatchProgress.IsActive = isMatching;
        MatchProgress.Visibility = isMatching ? Visibility.Visible : Visibility.Collapsed;
        MatchGlyph.Visibility = isMatching ? Visibility.Collapsed : Visibility.Visible;
        if (isMatching)
        {
            MatchText.Text = LocalizationService.Text("MatchingProgress");
        }
        else
        {
            UpdateMatchButtonVisuals();
        }
    }

    private async void MatchButton_Click(object sender, RoutedEventArgs e)
    {
        if (!AppServices.Settings.IsTmdbConfigured())
        {
            await ShowMessageAsync(
                LocalizationService.Text("MatchWithTmdb"),
                LocalizationService.Text("TmdbNotConfiguredPrompt"));
            return;
        }

        if (HasExternalId())
        {
            var flyout = new MenuFlyout();

            var refreshItem = new MenuFlyoutItem
            {
                Text = LocalizationService.Text("RefreshMetadata"),
                Icon = new FontIcon { Glyph = "\uE72C" }
            };
            refreshItem.Click += async (_, _) => await RefreshTvShowMetadataAsync();

            var fixMatchItem = new MenuFlyoutItem
            {
                Text = LocalizationService.Text("FixMatch"),
                Icon = new FontIcon { Glyph = "\uE721" }
            };
            fixMatchItem.Click += async (_, _) => await FixTvShowMatchAsync();

            flyout.Items.Add(refreshItem);
            flyout.Items.Add(fixMatchItem);
            flyout.ShowAt(MatchButton);
            return;
        }

        await FixTvShowMatchAsync();
    }

    private async Task RefreshTvShowMetadataAsync()
    {
        var showId = _show.Id;
        var request = _metadataRequests.Begin();
        SetMatchingState(true);
        try
        {
            var refreshed = await Task.Run(() => AppServices.Matching.RefreshTvShowMetadataAsync(showId, true, request.Token));
            if (request.IsCurrent && refreshed != null)
            {
                SetMatchingState(false);
                LoadShow(showId);
            }
        }
        catch (Exception error)
        {
            if (request.IsCurrent) _ = ShowMessageAsync(LocalizationService.Text("Failed"), MetadataErrorText.For(error));
        }
        finally
        {
            if (request.IsCurrent) SetMatchingState(false);
        }
    }

    private async Task FixTvShowMatchAsync()
    {
        var showId = _show.Id;
        var request = _metadataRequests.Begin();
        var candidate = await MatchMediaDialog.ShowForTvShowAsync(this, _show.Title, _show.Year > 0 ? _show.Year : null);
        if (!request.IsCurrent) return;
        if (candidate != null)
        {
            SetMatchingState(true);
            try
            {
                await Task.Run(() => AppServices.Matching.MatchTvShowAsync(showId, candidate, true, request.Token));
                if (request.IsCurrent) { SetMatchingState(false); LoadShow(showId); }
            }
            catch (Exception error)
            {
                if (request.IsCurrent) _ = ShowMessageAsync(LocalizationService.Text("Failed"), MetadataErrorText.For(error));
            }
            finally
            {
                if (request.IsCurrent) SetMatchingState(false);
            }
        }
    }
}
