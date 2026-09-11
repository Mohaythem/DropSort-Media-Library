using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using DropSort.Application.Contracts;
using DropSort.Domain.Library.Personal;
using DropSort.UI.Models;
using DropSort.UI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace DropSort.UI.Views;

/// <summary>
/// Movie details. Everything on this page reads and writes the real catalog: the hero and the file
/// block come from <see cref="ILibraryUiActions.GetMovieDetails" />, and preference, watchlist and
/// watch history go through <see cref="IPersonalLibraryUiActions" />, so the buttons persist and the
/// page always redraws from what was actually stored.
/// </summary>
public sealed partial class MovieDetailsPage : Page, ILocalizableView
{
    /// <summary>Shown before the first real load: the page is parented at startup, before any click.</summary>
    private static readonly MovieRecord Placeholder = new(0, string.Empty, 0, string.Empty, 0, [], string.Empty);

    private readonly ObservableCollection<WatchHistoryDisplayRecord> _history = [];
    private MovieRecord _movie = Placeholder;
    private string? _preference;
    private bool _inWatchlist;
    private string _returnDestination = "library";

    /// <summary>True while a journalled move is running; the file actions stay locked until it ends.</summary>
    private bool _isOrganizing;
    private bool _isMatching;

    public MovieDetailsPage()
    {
        InitializeComponent();
        HistoryRepeater.ItemsSource = _history;
        WatchedDatePicker.MaxDate = DateTimeOffset.Now;
        SetMovie(_movie);
    }

    public event EventHandler? BackRequested;

    public void SetReturnDestination(string destination)
    {
        _returnDestination = destination;
        UpdateBackLabel();
    }

    /// <summary>
    /// Loads one movie from the catalog. Called on every navigation, so re-entering the page always
    /// shows the stored state rather than whatever the previous visit left behind.
    /// </summary>
    public void LoadMovie(int movieId)
    {
        try
        {
            SetMovie(LibraryProjection.ToDetails(
                AppServices.Library.GetMovieDetails(movieId),
                AppServices.Personal.GetPersonalSnapshot(movieId),
                AppServices.Personal.ListWatchEvents(movieId)));
        }
        catch (Exception error)
        {
            SetMovie(Placeholder);
            ReportFailure(error);
        }
    }

    private void SetMovie(MovieRecord movie)
    {
        _movie = movie;
        _preference = movie.Preference;
        _inWatchlist = movie.InWatchlist;

        MovieTitleText.Text = movie.Title;
        MovieMetaText.Text = movie.MetaLine;
        OverviewText.Text = movie.Overview;
        GenresRepeater.ItemsSource = movie.Genres;

        // A movie registered from a file name has no overview yet. A heading with nothing under it
        // reads as a rendering fault, so the whole block goes away until there is text for it.
        var hasOverview = !string.IsNullOrWhiteSpace(movie.Overview);
        OverviewTitleText.Visibility = hasOverview ? Visibility.Visible : Visibility.Collapsed;
        OverviewText.Visibility = hasOverview ? Visibility.Visible : Visibility.Collapsed;

        var hasFile = movie.HasLocalFile;
        FileNameText.Text = movie.FileName ?? string.Empty;
        FilePathText.Text = movie.FilePath ?? string.Empty;
        FileFactsText.Text = movie.FileFacts ?? string.Empty;
        FileFactsText.Visibility = string.IsNullOrWhiteSpace(movie.FileFacts) ? Visibility.Collapsed : Visibility.Visible;
        FilePresentPanel.Visibility = hasFile ? Visibility.Visible : Visibility.Collapsed;
        NoFilePanel.Visibility = hasFile ? Visibility.Collapsed : Visibility.Visible;
        PresentPill.Visibility = hasFile ? Visibility.Visible : Visibility.Collapsed;
        PlayButton.IsEnabled = hasFile && !_isOrganizing;
        OpenFolderButton.IsEnabled = hasFile && !_isOrganizing;
        OrganizeButton.IsEnabled = hasFile && !_isOrganizing;
        MatchButton.IsEnabled = !_isOrganizing && !_isMatching;

        _history.Clear();
        foreach (var entry in movie.WatchHistory ?? [])
        {
            _history.Add(new WatchHistoryDisplayRecord(entry, LocalizationService.Text("RemoveWatchEvent")));
        }

        WatchedDatePicker.Date = null;
        MarkOnDateButton.IsEnabled = false;
        UpdatePosterVisuals();
        ApplyLocalization();
    }

    public void ApplyLocalization()
    {
        Root.FlowDirection = LocalizationService.FlowDirection;

        UpdateBackLabel();
        KindText.Text = LocalizationService.Text("Movie");
        OriginalTitleText.Text = string.IsNullOrWhiteSpace(_movie.OriginalTitle)
            ? string.Empty
            : LocalizationService.Format("OriginalTitleFormat", _movie.OriginalTitle);
        OriginalTitleText.Visibility = string.IsNullOrWhiteSpace(_movie.OriginalTitle)
            ? Visibility.Collapsed
            : Visibility.Visible;

        PlayText.Text = LocalizationService.Text("PlayMovie");
        OpenFolderText.Text = LocalizationService.Text("OpenFolder");
        OrganizeText.Text = LocalizationService.Text("OrganizeFile");
        MoreButton.SetValue(AutomationProperties.NameProperty, LocalizationService.Text("FileDetails"));
        ToolTipService.SetToolTip(MoreButton, LocalizationService.Text("FileDetails"));
        FileDetailsTitleText.Text = LocalizationService.Text("MediaFile");
        PresentText.Text = LocalizationService.Text("Present");
        NoFileText.Text = LocalizationService.Text("NoLocalFile");
        NoFileHelpText.Text = LocalizationService.Text("NoLocalFileHelp");
        OverviewTitleText.Text = LocalizationService.Text("Overview");

        ActivityTitleText.Text = LocalizationService.Text("YourActivity");
        ActivityHelpText.Text = LocalizationService.Text("ActivityHelp");
        PreferenceTitleText.Text = LocalizationService.Text("Preference");
        LikeText.Text = LocalizationService.Text("Like");
        ExcludeText.Text = LocalizationService.Text("Exclude");
        ClearPreferenceButton.Content = LocalizationService.Text("ClearPreference");
        WatchlistTitleText.Text = LocalizationService.Text("Watchlist");
        WatchingTitleText.Text = LocalizationService.Text("Watching");
        MarkWatchedText.Text = LocalizationService.Text("MarkWatched");
        WatchedDateLabel.Text = LocalizationService.Text("WatchedDate");
        WatchedDatePicker.PlaceholderText = LocalizationService.Text("PickDate");
        MarkOnDateButton.Content = LocalizationService.Text("MarkOnDate");
        HistoryTitleText.Text = LocalizationService.Text("WatchHistory");
        NoHistoryText.Text = LocalizationService.Text("NoWatchesHelp");

        UpdatePreferenceVisuals();
        UpdateWatchlistVisuals();
        RelabelHistory();
        UpdateMatchButtonVisuals();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Hands the file to whatever the user has registered for it. ShellExecute is the only way to
    /// honour the user's default player, and UseShellExecute is required for that.
    /// </summary>
    private void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(_movie.FilePath))
        {
            ReportMissingFile();
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(_movie.FilePath!) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception error)
        {
            ReportFailure(error);
        }
    }

    /// <summary>Opens the containing folder with the file selected, which is what the design implies.</summary>
    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(_movie.FilePath))
        {
            ReportMissingFile();
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe")
            {
                // The argument is quoted so a path with spaces stays one argument.
                Arguments = "/select,\"" + _movie.FilePath + "\"",
                UseShellExecute = true,
            })?.Dispose();
        }
        catch (Exception error)
        {
            ReportFailure(error);
        }
    }

    /// <summary>
    /// Organizing moves the file into the approved movies root through the journalled coordinator.
    /// <para>
    /// The preview is prepared, the move is confirmed with the real source and destination in front of
    /// the user, and only then does the transfer run - on a worker thread, because the engine copies and
    /// hashes the file when the destination is on another volume and that would otherwise freeze the
    /// window for as long as the copy takes. Declining the confirmation discards the preview, so no
    /// operation is left parked in the service. The journal, the approved-root check and the
    /// no-overwrite rule are the coordinator's, untouched: this only decides when they run.
    /// </para>
    /// </summary>
    private async void OrganizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isOrganizing)
        {
            return;
        }

        if (_movie.MediaFileId is not int mediaFileId || _movie.FilePath is null)
        {
            ReportMissingFile();
            return;
        }

        var root = AppSettingsStore.MovieFolder;

        if (string.IsNullOrWhiteSpace(root))
        {
            await ShowMessageAsync(LocalizationService.Text("OrganizeFile"), LocalizationService.Text("NoMovieFolderHelp"));
            return;
        }

        var fileName = Path.GetFileName(_movie.FilePath);
        var movieId = _movie.Id;
        SetOrganizing(true);

        try
        {
            var preview = await Task.Run(() => AppServices.Organization.PrepareOrganization(mediaFileId, root, fileName));

            if (!await ConfirmOrganizeAsync(preview.Plan.Source, preview.Plan.Destination))
            {
                AppServices.Organization.DiscardOrganizationPreview(preview.PreviewId);
                return;
            }

            await Task.Run(() => AppServices.Organization.ConfirmOrganization(preview.PreviewId));

            LoadMovie(movieId);
            await ShowMessageAsync(LocalizationService.Text("OrganizeFile"), LocalizationService.Text("OrganizeDone"));
        }
        catch (Exception error)
        {
            ReportFailure(error);
        }
        finally
        {
            SetOrganizing(false);
        }
    }

    /// <summary>
    /// Locks the file actions for the duration of a move and swaps the button's glyph for a ring. The
    /// three actions all address the same file, so none of them may run while it is being moved.
    /// </summary>
    private void SetOrganizing(bool isOrganizing)
    {
        _isOrganizing = isOrganizing;

        OrganizeProgress.IsActive = isOrganizing;
        OrganizeProgress.Visibility = isOrganizing ? Visibility.Visible : Visibility.Collapsed;
        OrganizeGlyph.Visibility = isOrganizing ? Visibility.Collapsed : Visibility.Visible;
        OrganizeText.Text = LocalizationService.Text(isOrganizing ? "Organizing" : "OrganizeFile");

        var canAct = !isOrganizing && _movie.HasLocalFile;
        OrganizeButton.IsEnabled = canAct;
        PlayButton.IsEnabled = canAct;
        OpenFolderButton.IsEnabled = canAct;
        MatchButton.IsEnabled = !isOrganizing && !_isMatching;
    }

    /// <summary>Shows the exact move that is about to happen; the destination is the approved root.</summary>
    private async Task<bool> ConfirmOrganizeAsync(string source, string destination)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.ElementTheme,
            FlowDirection = LocalizationService.FlowDirection,
            Title = LocalizationService.Text("OrganizeFile"),
            Content = LocalizationService.Format("OrganizeConfirmFormat", source, destination),
            PrimaryButtonText = LocalizationService.Text("Confirm"),
            CloseButtonText = LocalizationService.Text("Cancel"),
            DefaultButton = ContentDialogButton.Close,
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private void ReportMissingFile() => _ = ShowMessageAsync(
        LocalizationService.Text("NoLocalFile"),
        LocalizationService.Text("NoLocalFileHelp"));

    private void UpdateBackLabel()
    {
        BackText.Text = LocalizationService.Text(_returnDestination switch
        {
            "home" => "BackToHome",
            "lists" => "BackToMyLists",
            _ => "BackToLibrary",
        });
    }

    private void LikeButton_Click(object sender, RoutedEventArgs e) =>
        WritePreference(_preference == "liked" ? null : "liked");

    private void ExcludeButton_Click(object sender, RoutedEventArgs e) =>
        WritePreference(_preference == "blacklisted" ? null : "blacklisted");

    private void ClearPreferenceButton_Click(object sender, RoutedEventArgs e) => WritePreference(null);

    /// <summary>
    /// Writes the preference through the personal library and then redraws from the snapshot the
    /// store returned, so the button state is the stored state and never a local guess.
    /// </summary>
    private void WritePreference(string? preference)
    {
        if (_movie.Id <= 0)
        {
            return;
        }

        try
        {
            var snapshot = preference switch
            {
                "liked" => AppServices.Personal.SetPersonalPreference(_movie.Id, PersonalPreference.Liked),
                "blacklisted" => AppServices.Personal.SetPersonalPreference(_movie.Id, PersonalPreference.Blacklisted),
                _ => AppServices.Personal.ClearPersonalPreference(_movie.Id),
            };

            _preference = snapshot.Preference switch
            {
                PersonalPreference.Liked => "liked",
                PersonalPreference.Blacklisted => "blacklisted",
                _ => null,
            };
        }
        catch (Exception error)
        {
            ReportFailure(error);
        }

        UpdatePreferenceVisuals();
    }

    private void WatchlistButton_Click(object sender, RoutedEventArgs e)
    {
        if (_movie.Id <= 0)
        {
            return;
        }

        try
        {
            var snapshot = _inWatchlist
                ? AppServices.Personal.RemoveFromWatchlist(_movie.Id)
                : AppServices.Personal.AddToWatchlist(_movie.Id);
            _inWatchlist = snapshot.Watchlisted;
        }
        catch (Exception error)
        {
            ReportFailure(error);
        }

        UpdateWatchlistVisuals();
    }

    private void MarkWatchedButton_Click(object sender, RoutedEventArgs e) => AddWatch(DateTimeOffset.Now);

    private void MarkOnDateButton_Click(object sender, RoutedEventArgs e)
    {
        if (WatchedDatePicker.Date is DateTimeOffset date)
        {
            AddWatch(date);
            WatchedDatePicker.Date = null;
            MarkOnDateButton.IsEnabled = false;
        }
    }

    private void WatchedDatePicker_DateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args) =>
        MarkOnDateButton.IsEnabled = sender.Date is not null;

    private void RemoveHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: WatchHistoryDisplayRecord entry })
        {
            return;
        }

        if (entry.Entry.EventId is not int eventId)
        {
            return;
        }

        try
        {
            AppServices.Personal.RemoveWatchEvent(eventId);
        }
        catch (Exception error)
        {
            ReportFailure(error);
        }

        ReloadPersonalState();
    }

    /// <summary>Records a watch, then reloads so the row order and the labels come from the store.</summary>
    private void AddWatch(DateTimeOffset date)
    {
        if (_movie.Id <= 0)
        {
            return;
        }

        try
        {
            AppServices.Personal.RecordWatch(_movie.Id, date);
        }
        catch (Exception error)
        {
            ReportFailure(error);
        }

        ReloadPersonalState();
    }

    /// <summary>Re-reads preference, watchlist and history for the current movie.</summary>
    private void ReloadPersonalState()
    {
        if (_movie.Id <= 0)
        {
            return;
        }

        try
        {
            var snapshot = AppServices.Personal.GetPersonalSnapshot(_movie.Id);
            var events = AppServices.Personal.ListWatchEvents(_movie.Id);

            _preference = snapshot.Preference switch
            {
                PersonalPreference.Liked => "liked",
                PersonalPreference.Blacklisted => "blacklisted",
                _ => null,
            };
            _inWatchlist = snapshot.Watchlisted;

            _movie = _movie with
            {
                Preference = _preference,
                InWatchlist = _inWatchlist,
                WatchHistory = LibraryProjection.ToWatchHistory(events),
            };

            _history.Clear();
            foreach (var entry in _movie.WatchHistory ?? [])
            {
                _history.Add(new WatchHistoryDisplayRecord(entry, LocalizationService.Text("RemoveWatchEvent")));
            }
        }
        catch (Exception error)
        {
            ReportFailure(error);
        }

        UpdatePreferenceVisuals();
        UpdateWatchlistVisuals();
        UpdateHistoryVisibility();
    }

    private void ReportFailure(Exception error) =>
        _ = ShowMessageAsync(LocalizationService.Text("Failed"), error.Message);

    /// <summary>
    /// One themed message surface for this page. A ContentDialog is hosted outside the window's
    /// content root, so it is stamped with the active theme and flow direction explicitly.
    /// </summary>
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

        await dialog.ShowAsync();
    }

    /// <summary>Active states use the native accent / critical button styles, never literal colors.</summary>
    private void UpdatePreferenceVisuals()
    {
        LikeButton.Style = (Style)App.Current.Resources[
            _preference == "liked" ? "ActiveControlButtonStyle" : "SubtleControlButtonStyle"];
        LikeGlyph.Glyph = _preference == "liked" ? "\uEB52" : "\uEB51";
        ExcludeButton.Style = (Style)App.Current.Resources[
            _preference == "blacklisted" ? "DangerControlButtonStyle" : "SubtleControlButtonStyle"];
        ClearPreferenceButton.IsEnabled = _preference is not null;
    }

    private void UpdateWatchlistVisuals()
    {
        WatchlistButton.Style = (Style)App.Current.Resources[
            _inWatchlist ? "ActiveControlButtonStyle" : "SubtleControlButtonStyle"];
        WatchlistGlyph.Glyph = _inWatchlist ? "\uE735" : "\uE734";
        WatchlistText.Text = LocalizationService.Text(_inWatchlist ? "InWatchlist" : "AddToWatchlist");
    }

    /// <summary>Re-stamps the localized remove-button label on rows created under another language.</summary>
    private void RelabelHistory()
    {
        var removeLabel = LocalizationService.Text("RemoveWatchEvent");
        for (var index = 0; index < _history.Count; index++)
        {
            if (_history[index].RemoveLabel != removeLabel)
            {
                _history[index] = _history[index] with { RemoveLabel = removeLabel };
            }
        }

        UpdateHistoryVisibility();
    }

    private void UpdateHistoryVisibility()
    {
        NoHistoryText.Visibility = _history.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HistoryRepeater.Visibility = _history.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdatePosterVisuals()
    {
        if (string.IsNullOrWhiteSpace(_movie.PosterReference) || !AppServices.IsAvailable)
        {
            PosterImage.Source = null;
            PosterGlyph.Visibility = Visibility.Visible;
            return;
        }

        var posterRef = _movie.PosterReference;
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

            _ = Task.Run(async () =>
            {
                var downloadedPath = await AppServices.Poster.EnsurePosterCachedAsync("TMDB", posterRef);
                if (downloadedPath != null && File.Exists(downloadedPath))
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (_movie.PosterReference == posterRef)
                        {
                            PosterImage.Source = new BitmapImage(new Uri(downloadedPath));
                            PosterGlyph.Visibility = Visibility.Collapsed;
                        }
                    });
                }
            });
        }
    }

    private bool HasExternalId()
    {
        try
        {
            if (_movie.Id <= 0 || !AppServices.IsAvailable)
            {
                return false;
            }

            var item = AppServices.Library.GetMovieItem(_movie.Id);
            return !item.HasPendingMetadata;
        }
        catch
        {
            return !string.IsNullOrWhiteSpace(_movie.PosterReference);
        }
    }

    private void UpdateMatchButtonVisuals()
    {
        var hasId = HasExternalId();
        MatchText.Text = LocalizationService.Text(hasId ? "RefreshMetadata" : "MatchMetadata");
    }

    private void SetMatchingState(bool isMatching)
    {
        _isMatching = isMatching;
        MatchButton.IsEnabled = !isMatching && !_isOrganizing;
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
            refreshItem.Click += async (_, _) => await RefreshMovieMetadataAsync();

            var fixMatchItem = new MenuFlyoutItem
            {
                Text = LocalizationService.Text("FixMatch"),
                Icon = new FontIcon { Glyph = "\uE721" }
            };
            fixMatchItem.Click += async (_, _) => await FixMovieMatchAsync();

            flyout.Items.Add(refreshItem);
            flyout.Items.Add(fixMatchItem);
            flyout.ShowAt(MatchButton);
            return;
        }

        await FixMovieMatchAsync();
    }

    private async Task RefreshMovieMetadataAsync()
    {
        SetMatchingState(true);
        try
        {
            var refreshed = await Task.Run(() => AppServices.Matching.RefreshMovieMetadataAsync(_movie.Id));
            if (refreshed != null)
            {
                LoadMovie(_movie.Id);
            }
        }
        catch (Exception error)
        {
            ReportFailure(error);
        }
        finally
        {
            SetMatchingState(false);
        }
    }

    private async Task FixMovieMatchAsync()
    {
        var candidate = await MatchMediaDialog.ShowForMovieAsync(this, _movie.Title, _movie.Year > 0 ? _movie.Year : null);
        if (candidate != null)
        {
            SetMatchingState(true);
            try
            {
                await Task.Run(() => AppServices.Matching.MatchMovieAsync(_movie.Id, candidate));
                LoadMovie(_movie.Id);
            }
            catch (Exception error)
            {
                ReportFailure(error);
            }
            finally
            {
                SetMatchingState(false);
            }
        }
    }
}
