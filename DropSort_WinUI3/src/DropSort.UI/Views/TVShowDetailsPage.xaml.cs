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
/// There is no episode watch state in this build, so nothing here reports a watched count - the season
/// line and the progress figure report how much of the show is on disk instead.
/// </para>
/// </summary>
public sealed partial class TVShowDetailsPage : Page, ILocalizableView
{
    private TVShowRecord _show = new(0, string.Empty, 0, [], string.Empty, []);
    private string _returnDestination = "library";
    private bool _isMatching;
    private readonly PostHandoffRequestLifetime _metadataRequests = new();
    private readonly PostHandoffRequestLifetime _posterRequests = new();

    public TVShowDetailsPage()
    {
        InitializeComponent();
        Unloaded += (_, _) => { _metadataRequests.Cancel(); _posterRequests.Cancel(); };
        SetShow(_show);
    }

    public event EventHandler? BackRequested;

    public void SetReturnDestination(string destination)
    {
        _returnDestination = destination;
        UpdateBackLabel();
    }

    /// <summary>Loads one show's hierarchy from the catalog.</summary>
    public void LoadShow(int showId)
    {
        try
        {
            SetShow(TvProjection.ToDetails(AppServices.Tv.GetShowDetails(showId)));
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
        SetMatchingState(false);
        _show = show;
        ShowTitleText.Text = show.Title;
        ShowMetaText.Text = show.MetaLine;
        OverviewText.Text = show.Overview;

        // A show registered from file names has no overview yet; a heading with nothing under it reads
        // as a rendering fault, so the block goes away until there is text for it.
        var hasOverview = !string.IsNullOrWhiteSpace(show.Overview);
        OverviewText.Visibility = hasOverview ? Visibility.Visible : Visibility.Collapsed;

        ProgressValueText.Text = show.ProgressLine;
        PlayNextButton.IsEnabled = show.NextPlayableEpisode is not null;
        MatchButton.IsEnabled = !_isMatching;
        UpdatePosterVisuals();
        ApplyLocalization();
    }

    public void ApplyLocalization()
    {
        Root.FlowDirection = LocalizationService.FlowDirection;

        UpdateBackLabel();
        KindText.Text = LocalizationService.Text("TvShow");
        ProgressLabelText.Text = LocalizationService.Text("EpisodesOnDisk");
        PlayNextText.Text = LocalizationService.Text("PlayNextEpisode");
        AddToListText.Text = LocalizationService.Text("AddToList");
        MoreButton.SetValue(AutomationProperties.NameProperty, LocalizationService.Text("MoreOptions"));
        ToolTipService.SetToolTip(MoreButton, LocalizationService.Text("MoreOptions"));
        MarkShowWatchedItem.Text = LocalizationService.Text("MarkWatched");
        OpenShowFolderItem.Text = LocalizationService.Text("OpenFolder");
        SeasonsTitleText.Text = LocalizationService.Text("Seasons");
        SeasonsHelpText.Text = LocalizationService.Text("SeasonsHelp");

        SeasonsRepeater.ItemsSource = BuildSeasons(_show);
        UpdateMatchButtonVisuals();
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

    /// <summary>Plays the first episode of the show that has a file on disk.</summary>
    private void PlayNextButton_Click(object sender, RoutedEventArgs e)
    {
        if (_show.NextPlayableEpisode is { } selection)
        {
            PlayEpisode(selection.Episode);
        }
    }

    private void EpisodePlay_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: EpisodeDisplayRecord row })
        {
            PlayEpisode(FindEpisode(row));
        }
    }

    private void EpisodeOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: EpisodeDisplayRecord row })
        {
            return;
        }

        var episode = FindEpisode(row);

        if (episode?.FilePath is not { Length: > 0 } path || !File.Exists(path))
        {
            ReportMissingFile();
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe")
            {
                // Quoted so a path with spaces stays one argument.
                Arguments = "/select,\"" + path + "\"",
                UseShellExecute = true,
            })?.Dispose();
        }
        catch (Exception error)
        {
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

    private void PlayEpisode(EpisodeRecord? episode)
    {
        if (episode?.FilePath is not { Length: > 0 } path || !File.Exists(path))
        {
            ReportMissingFile();
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception error)
        {
            _ = ShowMessageAsync(LocalizationService.Text("Failed"), MetadataErrorText.For(error));
        }
    }

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

    /// <summary>The first season starts expanded, exactly like the design source.</summary>
    private static IReadOnlyList<SeasonDisplayRecord> BuildSeasons(TVShowRecord show)
    {
        var labels = new EpisodeActionLabels(
            LocalizationService.Text("Watched"),
            LocalizationService.Text("Missing"),
            LocalizationService.Text("PlayEpisode"),
            LocalizationService.Text("OpenFolder"),
            LocalizationService.Text("MoreOptions"));

        return
        [
            .. show.Seasons.Select((season, index) => new SeasonDisplayRecord(
                show.Id,
                season.Number,
                ShowFormatting.SeasonArtLabel(season.Number),
                LocalizationService.Format("SeasonFormat", season.Number),
                SeasonMeta(season),
                season.Episodes.Count == 0 ? 0 : 100.0 * season.LocalCount / season.Episodes.Count,
                index == 0,
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
                        labels)),
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
