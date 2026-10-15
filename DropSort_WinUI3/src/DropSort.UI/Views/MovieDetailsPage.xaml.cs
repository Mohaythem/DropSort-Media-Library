using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using DropSort.Application.Contracts;
using DropSort.Domain.Library.Personal;
using DropSort.UI.Models;
using DropSort.UI.Controls;
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
public sealed partial class MovieDetailsPage : Page, ILocalizableView, IDeactivatableView
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

    /// <summary>
    /// True when the registered file's storage root is currently unreachable. The row stays PRESENT in
    /// the catalog (the availability contract never persists a false MISSING), so the page probes the
    /// storage off the UI thread and, when it is offline, shows "storage unavailable" instead of
    /// presenting the file as available and openable.
    /// </summary>
    private bool _storageUnavailable;
    private readonly Dictionary<string, string> _fileAvailability = new(StringComparer.Ordinal);
    private readonly PostHandoffRequestLifetime _metadataRequests = new();
    private readonly PostHandoffRequestLifetime _posterRequests = new();
    private readonly PostHandoffRequestLifetime _availabilityRequests = new();
    private readonly PostHandoffRequestLifetime _actionRequests = new();
    private readonly PostHandoffRequestLifetime _organizationRequests = new();

    private readonly Windows.UI.ViewManagement.AccessibilitySettings _accessibilitySettings = new();
    private readonly Windows.UI.ViewManagement.UISettings _uiSettings = new();
    private bool _watchingContrast;

    public MovieDetailsPage()
    {
        InitializeComponent();
        Root.SizeChanged += (_, _) => UpdateDetailsLayout();
        ActualThemeChanged += (_, _) => UpdateDetailsIcons();
        Loaded += (_, _) =>
        {
            if (!_watchingContrast)
            {
                _uiSettings.ColorValuesChanged += DetailsHighContrastChanged;
                _watchingContrast = true;
            }
            UpdateDetailsIcons();
            UpdateDetailsLayout();
            UpdateDetailsIcons();
            if (_scrollToTopOnLoad) { Root.ChangeView(null, 0, null, true); _scrollToTopOnLoad = false; }
        };
        PosterImage.ImageFailed += (_, _) =>
        {
            PosterGlyph.Visibility = Visibility.Visible;
        };
        OverviewText.IsTextTrimmedChanged += (_, _) => UpdateOverviewToggle();
        HistoryRepeater.ItemsSource = _history;
        WatchedDatePicker.MaxDate = DateTimeOffset.Now;
        WatchedDatePicker.Loaded += (_, _) =>
        {
            WatchedDatePicker.PlaceholderText = string.Empty;
            WatchedDatePicker.PlaceholderText = LocalizationService.Text("PickDate");
        };
        Unloaded += (_, _) =>
        {
            if (_watchingContrast) _uiSettings.ColorValuesChanged -= DetailsHighContrastChanged;
            _watchingContrast = false;
            Deactivate();
        };
        SetMovie(_movie);
    }

    public event EventHandler? BackRequested;
    public event EventHandler? MediaPathChanged;

    public void Deactivate()
    {
        _metadataRequests.Cancel();
        _posterRequests.Cancel();
        _availabilityRequests.Cancel();
        _actionRequests.Cancel();
        _organizationRequests.Cancel();
    }

    /// <summary>Raised after a manual "Move to series" so the shell can open the target show.</summary>
    public event EventHandler<int>? ShowRequested;

    public void SetReturnDestination(string destination)
    {
        _returnDestination = destination;
        UpdateDetailsIcons();
        UpdateBackLabel();
    }

    /// <summary>
    /// Loads one movie from the catalog. Called on every navigation, so re-entering the page always
    /// shows the stored state rather than whatever the previous visit left behind.
    /// </summary>
    public bool IsLoadedSuccessfully { get; private set; }

    public void LoadMovie(int movieId)
    {
        IsLoadedSuccessfully = false;
        try
        {
            var item = AppServices.Library.GetMovieItem(movieId);
            SetMovie(LibraryProjection.ToDetails(
                AppServices.Library.GetMovieDetails(movieId),
                AppServices.Personal.GetPersonalSnapshot(movieId),
                AppServices.Personal.ListWatchEvents(movieId)) with
            { DateAdded = item.DateAdded, HasPendingMetadata = item.HasPendingMetadata });
            IsLoadedSuccessfully = true;
        }
        catch (Exception error)
        {
            SetMovie(Placeholder);
            ReportFailure(error);
        }
    }

    private void SetMovie(MovieRecord movie)
    {
        if (_movie.Id != movie.Id)
        {
            _scrollToTopOnLoad = !Root.ChangeView(null, 0, null, true);
        }
        _organizationRequests.Cancel();
        _metadataRequests.Cancel();
        _posterRequests.Cancel();
        _availabilityRequests.Cancel();
        _actionRequests.Cancel();
        SetMatchingState(false);
        _movie = movie;
        _preference = movie.Preference;
        _inWatchlist = movie.InWatchlist;

        MovieTitleText.Text = movie.Title;
        MatchedIdentityText.Text = movie.HasPendingMetadata ? string.Empty : movie.Title + (movie.Year > 0 ? " (" + movie.Year.ToString(CultureInfo.InvariantCulture) + ")" : string.Empty);
        MovieMetaText.Text = LocalizationService.Digits(string.Join(" · ", new[]
        {
            movie.Year > 0 ? movie.Year.ToString(CultureInfo.InvariantCulture) : null,
            string.IsNullOrWhiteSpace(movie.Runtime) ? null : movie.Runtime,
            movie.Genres.Count == 0 ? null : string.Join(" · ", movie.Genres)
        }.Where(part => !string.IsNullOrWhiteSpace(part))));
        RatingText.Text = movie.Rating > 0
            ? LocalizationService.Digits(movie.Rating.ToString("0.0", CultureInfo.InvariantCulture) + " / 10") : string.Empty;
        RatingPanel.Visibility = movie.Rating > 0 ? Visibility.Visible : Visibility.Collapsed;
        _pathExpanded = false;
        FilePathText.MaxLines = 2;
        OverviewText.Text = movie.Overview;
        _overviewExpanded = false;
        UpdateOverview();
        GenresText.Text = string.Join(" · ", movie.Genres);
        GenresText.Visibility = Visibility.Collapsed;

        // A movie registered from a file name has no overview yet. A heading with nothing under it
        // reads as a rendering fault, so the whole block goes away until there is text for it.
        var hasOverview = !string.IsNullOrWhiteSpace(movie.Overview);
        OverviewTitleText.Visibility = Visibility.Collapsed;
        OverviewText.Visibility = hasOverview ? Visibility.Visible : Visibility.Collapsed;
        OverviewSection.Visibility = hasOverview ? Visibility.Visible : Visibility.Collapsed;

        var hasFile = movie.HasLocalFile;
        _storageUnavailable = false;
        _fileAvailability.Clear();
        foreach (var file in movie.Files) _fileAvailability[file.FilePath] = "UnknownMedia";
        FileNameText.Text = movie.FileName ?? string.Empty;
        FilePathText.Text = movie.FilePath ?? string.Empty;
        FileFactsText.Text = movie.FileFacts ?? string.Empty;
        FileFactsText.Visibility = string.IsNullOrWhiteSpace(movie.FileFacts) ? Visibility.Collapsed : Visibility.Visible;
        FilePresentPanel.Visibility = movie.FilePath is not null && movie.FileCount <= 1 ? Visibility.Visible : Visibility.Collapsed;
        NoFilePanel.Visibility = hasFile ? Visibility.Collapsed : Visibility.Visible;
        PresentPill.Visibility = movie.FilePath is not null ? Visibility.Visible : Visibility.Collapsed;
        // List every associated local file when the movie has more than one (multiple versions/copies).
        MediaFilesRepeater.ItemsSource = movie.Files;
        AllFilesPanel.Visibility = movie.MultiFileListVisibility;
        AllFilesHeadingText.Text = LocalizationService.Format("FilesCountFormat", movie.FileCount);
        PlayButton.IsEnabled = hasFile && !_isOrganizing;
        OpenFolderButton.IsEnabled = hasFile && !_isOrganizing;
        OrganizeButton.IsEnabled = MoveMovieButton.IsEnabled = hasFile && !_isOrganizing;
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
        ProbeStorageAvailability(movie);
    }

    /// <summary>
    /// Confirms off the UI thread whether the file's storage is reachable. When it is not, the page shows
    /// "storage unavailable" and disables the actions that imply an openable file, rather than presenting
    /// an offline-drive file as available. The catalog row is not changed. A stale result (the page moved
    /// to another movie) is ignored.
    /// </summary>
    private void ProbeStorageAvailability(MovieRecord movie)
    {
        if (movie.Files.Count == 0) return;
        var path = movie.FilePath;
        var request = _availabilityRequests.Begin();
        MediaFilesRepeater.ItemsSource = movie.Files.Select(file => file with { Availability = LocalizationService.Text("UnknownMedia") }).ToList();
        PresentText.Text = LocalizationService.Text("UnknownMedia");
        _ = Task.Run(() =>
        {
            IReadOnlySet<string>? unavailable = null;
            try
            {
                unavailable = AppServices.UnavailableStoragePaths(movie.Files.Select(file => file.FilePath), request.Token);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception) { /* Leave availability unknown; never mark storage failure as missing. */ }
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!request.IsCurrent || _movie.Id != movie.Id
                    || !string.Equals(_movie.FilePath, path, StringComparison.Ordinal)) return;
                foreach (var file in movie.Files)
                    _fileAvailability[file.FilePath] = unavailable is null ? "UnknownMedia" :
                        unavailable.Contains(file.FilePath) ? "StorageUnavailable" : file.AvailabilityKey;
                LocalizeFileAvailability();
                _storageUnavailable = path is not null && unavailable?.Contains(path) == true;
                PresentText.Text = LocalizationService.Text(unavailable is null ? "UnknownMedia" :
                    _storageUnavailable ? "StorageUnavailable" : movie.HasLocalFile ? "Present" : "Missing");
                if (_storageUnavailable)
                {
                    PlayButton.IsEnabled = false;
                    OpenFolderButton.IsEnabled = false;
                    OrganizeButton.IsEnabled = MoveMovieButton.IsEnabled = false;
                }
            });
        });
    }

    public void ApplyLocalization()
    {
        Root.FlowDirection = LocalizationService.FlowDirection;

        UpdateBackLabel();
        KindText.Text = LocalizationService.Text("DetailsLibraryIdentity");
        Root.Language = LocalizationService.Culture.Name;
        OriginalTitleText.Text = string.IsNullOrWhiteSpace(_movie.OriginalTitle)
            ? string.Empty
            : LocalizationService.Format("OriginalTitleFormat", _movie.OriginalTitle);
        OriginalTitleText.Visibility = string.IsNullOrWhiteSpace(_movie.OriginalTitle) || string.Equals(_movie.Title, _movie.OriginalTitle, StringComparison.OrdinalIgnoreCase)
            ? Visibility.Collapsed
            : Visibility.Visible;

        PlayText.Text = LocalizationService.Text("PlayMovie");
        OpenFolderText.Text = LocalizationService.Text("OpenFolder");
        AddToListText.Text = LocalizationService.Text("AddToList");
        OrganizeText.Text = LocalizationService.Text("OrganizationRename");
        MoveMovieText.Text = LocalizationService.Text("MovieMoveAction");
        ChangeMatchText.Text = LocalizationService.Text("DetailsChangeMatch");
        MoveToSeriesButton.Content = LocalizationService.Text("AddOrMoveToSeries");
        RemoveFromLibraryButton.Content = LocalizationService.Text("RemoveFromLibrary");
        MoreButton.SetValue(AutomationProperties.NameProperty, LocalizationService.Text("DetailsMoreActions"));
        ToolTipService.SetToolTip(MoreButton, LocalizationService.Text("DetailsMoreActions"));
        FileDetailsTitleText.Text = LocalizationService.Text(_movie.FileCount > 1 ? "DetailsLocalFiles" : "DetailsLocalFile");
        MetadataStatusText.Text = LocalizationService.Text(_movie.HasPendingMetadata ? "DetailsUnmatched" : _movie.ReviewMatch ? "ReviewMatch" : "Matched");
        DateAddedText.Text = _movie.DateAdded == default ? string.Empty : LocalizationService.Format("DetailsDateAdded",
            LocalizationService.Digits(_movie.DateAdded.ToLocalTime().ToString("d", LocalizationService.Culture)));
        DateAddedText.Visibility = _movie.DateAdded == default ? Visibility.Collapsed : Visibility.Visible;
        UpdateOverview();
        NoFileText.Text = LocalizationService.Text("NoLocalFile");
        NoFileHelpText.Text = LocalizationService.Text("NoLocalFileHelp");
        LocalizeFileAvailability();
        OverviewTitleText.Text = LocalizationService.Text("Overview");

        ActivityTitleText.Text = LocalizationService.Text("DetailsWatchActivity");
        ActivityHelpText.Text = LocalizationService.Text("ActivityHelp");
        LikeText.Text = LocalizationService.Text(_preference == "liked" ? "InFavorites" : "AddToFavorites");
        ExcludeText.Text = LocalizationService.Text("Exclude");
        ClearPreferenceButton.Content = LocalizationService.Text("ClearPreference");
        WatchingTitleText.Text = LocalizationService.Text("Watching");
        MarkWatchedText.Text = LocalizationService.Text("MarkWatched");
        WatchedDateLabel.Text = LocalizationService.Text("WatchedDate");
        WatchedDatePicker.Language = LocalizationService.Culture.Name;
        WatchedDatePicker.PlaceholderText = string.Empty;
        WatchedDatePicker.PlaceholderText = LocalizationService.Text("PickDate");
        MarkOnDateButton.Content = LocalizationService.Text("MarkOnDate");
        HistoryTitleText.Text = LocalizationService.Format("DetailsViewings", LocalizationService.Number(_history.Count));
        UpdatePathActionLabels();
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
    private async void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_movie.FilePath))
        {
            ReportMissingFile();
            return;
        }

        var request = _actionRequests.Begin();
        try
        {
            var result = await AppServices.PlayMediaFileAsync(_movie.FilePath, request.Token);
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
            if (request.IsCurrent) ReportFailure(error);
        }
    }

    /// <summary>Opens the containing folder with the file selected, which is what the design implies.</summary>
    private async void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_movie.FilePath))
        {
            ReportMissingFile();
            return;
        }

        var request = _actionRequests.Begin();
        try
        {
            var result = await AppServices.MediaFileActions.OpenContainingFolderAsync(_movie.FilePath, request.Token);
            if (!request.IsCurrent) return;
            await PresentActionResultAsync(result.Status);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (request.IsCurrent) ReportFailure(error);
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

    /// <summary>
    /// Collects one physical-file choice and confirms a journalled rename or explicit move.
    /// <para>
    /// The preview is prepared, the move is confirmed with the real source and destination in front of
    /// the user, and only then does the transfer run - on a worker thread, because the engine copies and
    /// hashes the file when the destination is on another volume and that would otherwise freeze the
    /// window for as long as the copy takes. Declining the confirmation discards the preview, so no
    /// operation is left parked in the service. The journal, the approved-root check and the
    /// no-overwrite rule are the coordinator's, untouched: this only decides when they run.
    /// </para>
    /// </summary>
    private async void OrganizeButton_Click(object sender, RoutedEventArgs e) => await RunOrganizationAsync(false);

    private async void MoveMovieButton_Click(object sender, RoutedEventArgs e) => await RunOrganizationAsync(true);

    private async Task RunOrganizationAsync(bool move)
    {
        if (_isOrganizing)
        {
            return;
        }

        if (_movie.MediaFileId is null || _movie.FilePath is null)
        {
            ReportMissingFile();
            return;
        }

        var movieId = _movie.Id;
        var request = _organizationRequests.Begin();
        DropSort.Application.Dto.OrganizationPreview? preview = null;
        IOrganizationUiActions? organization = null;
        SetOrganizing(true, move: move);

        try
        {
            organization = AppServices.Organization;
            var details = await Task.Run(() => AppServices.Library.GetMovieDetails(movieId));
            if (!request.IsCurrent) return;
            if (!details.MediaFiles.Any(f => f.Status == DropSort.Domain.Library.Movies.MediaFileStatus.Present))
            {
                await ShowMessageAsync(LocalizationService.Text("NoLocalFile"), LocalizationService.Text("NoLocalFileHelp"));
                return;
            }
            var editor = new MovieOrganizationDialog(details, move, () => request.IsCurrent) { XamlRoot = XamlRoot };
            while (request.IsCurrent)
            {
                preview = await editor.ShowEditorAsync();
                if (preview is null || !request.IsCurrent) return;
                var decision = await ConfirmOrganizeAsync(preview);
                if (!request.IsCurrent || decision == ContentDialogResult.None) return;
                if (decision == ContentDialogResult.Primary) break;
                organization.DiscardOrganizationPreview(preview.PreviewId);
                preview = null; // Back returns to the same editor values and prepares a fresh identity snapshot.
            }
            if (preview is null || !request.IsCurrent) return;
            SetOrganizing(true, executing: true, move: move);
            // There is no active-transfer cancellation contract. Confirmation starts the durable operation.
            var result = await Task.Run(() => organization.ConfirmOrganization(preview.PreviewId));
            if (result.Record.State != DropSort.Domain.Core.Operations.OperationState.Committed)
                throw new InvalidOperationException("Operation is not committed.");
            MediaPathChanged?.Invoke(this, EventArgs.Empty);
            if (!request.IsCurrent) return;
            LoadMovie(movieId);
            if (!IsLoadedSuccessfully || _movie.Id != movieId) return;
            // In-place reload cancels the old lifetime. Guard focus during the success dialog with a fresh one.
            if (IsLoadedSuccessfully && _movie.Id == movieId) request = _organizationRequests.Begin();
            await ShowMessageAsync(LocalizationService.Text("MovieFileActions"), LocalizationService.Text("OrganizationDone"));
        }
        catch (Exception)
        {
            if (request.IsCurrent)
                await ShowMessageAsync(LocalizationService.Text("NeedsAttention"), LocalizationService.Text("OrganizationExecutionAttention"));
        }
        finally
        {
            if (preview is not null) organization?.DiscardOrganizationPreview(preview.PreviewId);
            SetOrganizing(false);
            if (request.IsCurrent) (move ? MoveMovieButton : OrganizeButton).Focus(FocusState.Programmatic);
        }
    }

    /// <summary>
    /// Locks the file actions for the duration of a move and swaps the button's glyph for a ring. The
    /// three actions all address the same file, so none of them may run while it is being moved.
    /// </summary>
    private void SetOrganizing(bool isOrganizing, bool executing = false, bool move = false)
    {
        _isOrganizing = isOrganizing;

        var renameExecuting = executing && !move;
        var moveExecuting = executing && move;
        OrganizeProgress.IsActive = renameExecuting;
        OrganizeProgress.Visibility = renameExecuting ? Visibility.Visible : Visibility.Collapsed;
        OrganizeGlyph.Visibility = renameExecuting ? Visibility.Collapsed : Visibility.Visible;
        OrganizeText.Text = LocalizationService.Text(renameExecuting ? "OrganizationWorking" : "OrganizationRename");
        MoveMovieProgress.IsActive = moveExecuting;
        MoveMovieProgress.Visibility = moveExecuting ? Visibility.Visible : Visibility.Collapsed;
        MoveMovieGlyph.Visibility = moveExecuting ? Visibility.Collapsed : Visibility.Visible;
        MoveMovieText.Text = LocalizationService.Text(moveExecuting ? "OrganizationWorking" : "MovieMoveAction");

        var canAct = !isOrganizing && _movie.HasLocalFile && !_storageUnavailable;
        OrganizeButton.IsEnabled = MoveMovieButton.IsEnabled = canAct;
        PlayButton.IsEnabled = canAct;
        OpenFolderButton.IsEnabled = canAct;
        MatchButton.IsEnabled = !isOrganizing && !_isMatching;
        ChangeMatchButton.IsEnabled = !isOrganizing && !_isMatching;
        MoveToSeriesButton.IsEnabled = !isOrganizing;
        MoreButton.IsEnabled = !isOrganizing;
    }

    /// <summary>Reviews the exact validated operation. Back retains the editor values.</summary>
    private async Task<ContentDialogResult> ConfirmOrganizeAsync(DropSort.Application.Dto.OrganizationPreview preview)
    {
        var rename = preview.Plan.OperationType == DropSort.Domain.Core.Operations.OperationType.Rename;
        var renameDuringMove = !rename && !StringComparer.Ordinal.Equals(Path.GetFileName(preview.Plan.Source), Path.GetFileName(preview.Plan.Destination));
        var body = new StackPanel { Spacing = 12, MaxWidth = 560 };
        body.Children.Add(new TextBlock { Text = LocalizationService.Text(rename ? "OrganizationRenameReview" : renameDuringMove ? "MovieMoveRenameReview" : "MovieMoveReview"), TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text = LocalizationService.Text("OrganizationSource") });
        var source = MovieOrganizationDialog.PathText();
        source.Text = preview.Plan.Source;
        body.Children.Add(source);
        body.Children.Add(new TextBlock { Text = LocalizationService.Text("OrganizationDestination") });
        var destination = MovieOrganizationDialog.PathText();
        destination.Text = preview.Plan.Destination;
        body.Children.Add(destination);
        body.Children.Add(new TextBlock { Text = LocalizationService.Text("OrganizationNoOverwrite"), TextWrapping = TextWrapping.Wrap });
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.ElementTheme,
            FlowDirection = LocalizationService.FlowDirection,
            Title = LocalizationService.Text(rename ? "OrganizationConfirmRename" : renameDuringMove ? "MovieMoveRenameConfirm" : "MovieMoveConfirm"),
            Content = new ScrollViewer { Content = body, MaxHeight = 520, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(16, 0, 16, 0), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled },
            PrimaryButtonText = LocalizationService.Text(rename ? "OrganizationRename" : renameDuringMove ? "MovieMoveAndRename" : "MovieMoveAction"),
            SecondaryButtonText = LocalizationService.Text("MovieOrganizationBack"),
            CloseButtonText = LocalizationService.Text("Cancel"),
            DefaultButton = ContentDialogButton.Close,
        };
        AutomationProperties.SetAutomationId(dialog, "MovieOrganizationConfirmation");

        return await ContentDialogCoordinator.ShowAsync(dialog);
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

    /// <summary>
    /// Builds the "Add to list" menu each time it opens, so it reflects the current membership. It lists
    /// the user's custom lists only (Watchlist and Favorites have their own controls and are not
    /// duplicated here), each with a check for current membership. The list id is kept on the item and
    /// never shown. Toggling persists through the personal library.
    /// </summary>
    private void AddToListFlyout_Opening(object sender, object e)
    {
        AddToListFlyout.Items.Clear();
        if (_movie.Id <= 0) return;

        IReadOnlyList<Application.Dto.CustomList> lists;
        IReadOnlySet<int> memberIds;
        try
        {
            lists = AppServices.Personal.ListCustomLists();
            memberIds = AppServices.Personal.GetCustomListIdsForMovie(_movie.Id);
        }
        catch (Exception)
        {
            AddToListFlyout.Items.Add(DisabledItem(LocalizationService.Text("NoCustomLists")));
            return;
        }

        if (lists.Count == 0)
        {
            AddToListFlyout.Items.Add(DisabledItem(LocalizationService.Text("NoCustomLists")));
            return;
        }

        foreach (var list in lists)
        {
            var item = BuildListItem(list.Name, memberIds.Contains(list.Id), ToggleCustomListFromMenu);
            item.Tag = list.Id;
            AddToListFlyout.Items.Add(item);
        }
    }

    private static MenuFlyoutItem DisabledItem(string text) => new() { Text = text, IsEnabled = false };

    private static ToggleMenuFlyoutItem BuildListItem(string text, bool isChecked, RoutedEventHandler onClick)
    {
        var item = new ToggleMenuFlyoutItem { Text = text, IsChecked = isChecked };
        item.Click += onClick;
        return item;
    }

    private void ToggleCustomListFromMenu(object sender, RoutedEventArgs e)
    {
        if (_movie.Id <= 0 || sender is not ToggleMenuFlyoutItem { Tag: int listId }) return;
        try
        {
            // Decide from the stored membership, not the toggle's new visual state, so a stale menu
            // cannot double-add or wrongly remove.
            if (AppServices.Personal.GetCustomListIdsForMovie(_movie.Id).Contains(listId))
            {
                AppServices.Personal.RemoveMovieFromCustomList(listId, _movie.Id);
            }
            else
            {
                AppServices.Personal.AddMovieToCustomList(listId, _movie.Id);
            }
        }
        catch (Exception error)
        {
            ReportFailure(error);
        }
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
        _ = ShowMessageAsync(LocalizationService.Text("Failed"), MetadataErrorText.For(error));

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

        await ContentDialogCoordinator.ShowAsync(dialog);
    }

    /// <summary>Active states use the native accent / critical button styles, never literal colors.</summary>
    private void UpdatePreferenceVisuals()
    {
        LikeText.Text = LocalizationService.Text(_preference == "liked" ? "InFavorites" : "AddToFavorites");
        ToolTipService.SetToolTip(LikeButton, LocalizationService.Text(_preference == "liked" ? "RemoveFromFavorites" : "AddToFavorites"));
        LikeButton.Style = (Style)Resources[
            _preference == "liked" ? "DetailsSelectedButtonStyle" : "DetailsButtonStyle"];
        LikeGlyph.Glyph = _preference == "liked" ? "\uEB52" : "\uEB51";
        ExcludeButton.Style = (Style)Resources[
            _preference == "blacklisted" ? "DetailsSelectedButtonStyle" : "DetailsButtonStyle"];
        ClearPreferenceButton.IsEnabled = _preference is not null;
        UpdateDetailsIcons();
    }

    private void UpdateWatchlistVisuals()
    {
        WatchlistButton.Style = (Style)Resources[
            _inWatchlist ? "DetailsSelectedButtonStyle" : "DetailsButtonStyle"];
        WatchlistGlyph.Glyph = _inWatchlist ? "\uE735" : "\uE734";
        WatchlistText.Text = LocalizationService.Text(_inWatchlist ? "InWatchLater" : "AddToWatchLater");
        ToolTipService.SetToolTip(WatchlistButton, LocalizationService.Text(_inWatchlist ? "RemoveFromWatchLater" : "AddToWatchLater"));
        UpdateDetailsIcons();
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
        HistoryTitleText.Text = LocalizationService.Format("DetailsViewings", LocalizationService.Number(_history.Count));
        WatchingTitleText.Text = LocalizationService.Text(_history.Count == 0 ? "DetailsNotWatched" : "DetailsWatched");
        NoHistoryText.Visibility = _history.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HistoryRepeater.Visibility = _history.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void DetailsHighContrastChanged(Windows.UI.ViewManagement.UISettings sender, object args) =>
        DispatcherQueue.TryEnqueue(UpdateDetailsIcons);

    private void UpdateDetailsIcons()
    {
        var native = ActualTheme == ElementTheme.Light || _accessibilitySettings.HighContrast;
        void Visit(DependencyObject parent)
        {
            if (parent is Grid { Tag: string tag } grid && tag.StartsWith("DetailsIcon:", StringComparison.Ordinal)
                && grid.Children.Count == 2 && grid.Children[0] is Image image && grid.Children[1] is FontIcon glyph)
            {
                var dynamicNative = tag == "DetailsIcon:heart" && _preference != "liked"
                    || tag == "DetailsIcon:bookmark" && !_inWatchlist;
                image.Visibility = native || dynamicNative ? Visibility.Collapsed : Visibility.Visible;
                glyph.Visibility = native || dynamicNative ? Visibility.Visible : Visibility.Collapsed;
                if (tag == "DetailsIcon:back")
                {
                    glyph.MirroredWhenRightToLeft = true;
                    image.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
                    image.RenderTransform = new Microsoft.UI.Xaml.Media.ScaleTransform
                    { ScaleX = LocalizationService.FlowDirection == FlowDirection.RightToLeft ? -1 : 1 };
                }
            }
            for (var index = 0; index < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); index++)
                Visit(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, index));
        }
        Visit(Root);
    }

    private bool _pathExpanded;

    private void UpdatePathActionLabels()
    {
        CopyPathButton.SetValue(AutomationProperties.NameProperty, LocalizationService.Text("DetailsCopyPath"));
        ExpandPathButton.SetValue(AutomationProperties.NameProperty,
            LocalizationService.Text(_pathExpanded ? "DetailsCollapsePath" : "DetailsExpandPath"));
        ToolTipService.SetToolTip(CopyPathButton, LocalizationService.Text("DetailsCopyPath"));
        ToolTipService.SetToolTip(ExpandPathButton,
            LocalizationService.Text(_pathExpanded ? "DetailsCollapsePath" : "DetailsExpandPath"));
    }

    private void ExpandPathButton_Click(object sender, RoutedEventArgs e)
    {
        _pathExpanded = !_pathExpanded;
        FilePathText.MaxLines = _pathExpanded ? 0 : 2;
        UpdatePathActionLabels();
    }

    private void CopyPathButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_movie.FilePath)) return;
        try
        {
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(_movie.FilePath);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        }
        catch (Exception error) { ReportFailure(error); }
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
                            if (request.IsCurrent && _movie.PosterReference == posterRef)
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
                    // second ContentDialog: this worker can finish while MatchMediaDialog is open,
                    // and WinUI permits only one ContentDialog per XamlRoot.
                }
            });
        }
    }

    private bool _overviewExpanded;
    private bool _scrollToTopOnLoad;
    private void LocalizeFileAvailability()
    {
        MediaFilesRepeater.ItemsSource = _movie.Files.Select(file => file with
        {
            Availability = LocalizationService.Text(_fileAvailability.GetValueOrDefault(file.FilePath, "UnknownMedia"))
        }).ToList();
        PresentText.Text = LocalizationService.Text(_movie.FilePath is null ? "UnknownMedia" :
            _fileAvailability.GetValueOrDefault(_movie.FilePath, "UnknownMedia"));
        if (!_movie.HasLocalFile && _movie.Files.Count > 0)
        {
            var key = _movie.FilePath is null ? "UnknownMedia" : _fileAvailability.GetValueOrDefault(_movie.FilePath, "UnknownMedia");
            NoFileText.Text = LocalizationService.Text(key);
            NoFileHelpText.Text = LocalizationService.Text(key == "StorageUnavailable" ? "DetailsOfflineHelp" :
                key == "UnknownMedia" ? "DetailsUnknownHelp" : "DetailsMissingHelp");
        }
    }

    private void OverviewToggleButton_Click(object sender, RoutedEventArgs e)
    {
        _overviewExpanded = !_overviewExpanded;
        UpdateOverview();
    }

    private void UpdateOverview()
    {
        OverviewText.MaxLines = _overviewExpanded ? 0 : 5;
        UpdateOverviewToggle();
        OverviewToggleButton.Content = LocalizationService.Text(_overviewExpanded ? "DetailsShowLess" : "DetailsShowMore");
    }

    private void UpdateOverviewToggle() => OverviewToggleButton.Visibility =
        !string.IsNullOrWhiteSpace(_movie.Overview) && (_overviewExpanded || OverviewText.IsTextTrimmed)
            ? Visibility.Visible : Visibility.Collapsed;


    private void UpdateDetailsLayout()
    {
        // Measure the page, not the window. Expanded navigation consumes content width.
        var available = Math.Max(0, Root.ActualWidth - Root.Padding.Left - Root.Padding.Right);
        MovieMetaText.MaxWidth = Math.Max(0, available >= 1500 ? available - 614.7 : available >= 900 ? available - 460 : available - 120);
        OverviewText.MaxWidth = Math.Min(850, Math.Max(0, available >= 1500 ? available - 494.7 : available >= 900 ? available - 340 : available));
        VisualStateManager.GoToState(this,
            available >= 1500 ? "DetailsWideState" : available >= 900 ? "DetailsMediumState" : "DetailsCompactState", false);
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
        ChangeMatchButton.IsEnabled = !isMatching && !_isOrganizing;
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

    private async void ChangeMatchButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isOrganizing || _isMatching) return;
        if (!AppServices.Settings.IsTmdbConfigured())
        {
            await ShowMessageAsync(
                LocalizationService.Text("MatchWithTmdb"),
                LocalizationService.Text("TmdbNotConfiguredPrompt"));
            return;
        }

        await FixMovieMatchAsync();
    }

    private async Task RefreshMovieMetadataAsync()
    {
        var movieId = _movie.Id;
        var request = _metadataRequests.Begin();
        SetMatchingState(true);
        try
        {
            var refreshed = await Task.Run(() => AppServices.Matching.RefreshMovieMetadataAsync(movieId, request.Token));
            if (request.IsCurrent && refreshed != null)
            {
                SetMatchingState(false);
                LoadMovie(movieId);
            }
        }
        catch (Exception error)
        {
            if (request.IsCurrent) ReportFailure(error);
        }
        finally
        {
            if (request.IsCurrent) SetMatchingState(false);
        }
    }

    private async Task FixMovieMatchAsync()
    {
        var movieId = _movie.Id;
        var request = _metadataRequests.Begin();
        var candidate = await MatchMediaDialog.ShowForMovieAsync(this, _movie.Title, _movie.Year > 0 ? _movie.Year : null);
        if (!request.IsCurrent) return;
        if (candidate != null)
        {
            SetMatchingState(true);
            try
            {
                // The service returns the canonical movie: normally this same movie, but if this was an
                // unmatched duplicate whose chosen identity is already owned, it is merged into the owner
                // and that owner is returned — so load whatever id came back, not the original.
                var canonical = await Task.Run(() => AppServices.Matching.MatchMovieAsync(movieId, candidate, request.Token));
                if (request.IsCurrent) { SetMatchingState(false); LoadMovie(canonical.Id); }
            }
            catch (Exception error)
            {
                if (request.IsCurrent) ReportFailure(error);
            }
            finally
            {
                if (request.IsCurrent) SetMatchingState(false);
            }
        }
    }

    /// <summary>
    /// Manual repair: moves this movie's file(s) into an existing series as an episode. Season/episode are
    /// inferred from the file name when possible and confirmed by the user. On success the movie is gone,
    /// so the page navigates to the target show.
    /// </summary>
    private async void MoveToSeriesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isOrganizing) return;
        MoreButton.Flyout?.Hide();
        if (_movie.Id <= 0)
        {
            return;
        }

        try
        {
            var shows = AppServices.Tv.ListShows();
            if (shows.Count == 0)
            {
                await ShowMessageAsync(
                    LocalizationService.Text("AddOrMoveToSeries"),
                    LocalizationService.Text("NoSeriesInLibrary"));
                return;
            }

            int? inferredSeason = null;
            int? inferredEpisode = null;
            // Infer from the ORIGINAL local filename (never the cleaned movie title), reusing the discovery
            // parser so SxxEyy / 1x02 / "Episode N" / "الحلقة N" / Arabic-Indic digits are all understood.
            var fileName = !string.IsNullOrWhiteSpace(_movie.FileName)
                ? _movie.FileName
                : _movie.FilePath is { Length: > 0 } filePath ? Path.GetFileName(filePath) : null;
            if (AppServices.InferSeasonEpisode(fileName) is var inferred && inferred is { } se)
            {
                inferredSeason = se.Season;
                inferredEpisode = se.Episode;
            }

            var choice = await MoveToSeriesDialog.ShowAsync(this, shows, inferredSeason, inferredEpisode);
            if (choice is not { } target)
            {
                return;
            }

            var movieId = _movie.Id;
            var result = await Task.Run(() =>
                AppServices.Repair.MoveMovieToSeries(movieId, target.ShowId, target.Season, target.Episode));

            ShowRequested?.Invoke(this, result.ShowId);
        }
        catch (Exception error)
        {
            ReportFailure(error);
        }
    }

    /// <summary>
    /// Manual repair: removes this movie from the catalog only. The file on disk is never deleted. On
    /// success the page leaves, since the movie no longer exists.
    /// </summary>
    private async void RemoveFromLibraryButton_Click(object sender, RoutedEventArgs e)
    {
        MoreButton.Flyout?.Hide();
        if (_movie.Id <= 0)
        {
            return;
        }

        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.ElementTheme,
            FlowDirection = LocalizationService.FlowDirection,
            Title = LocalizationService.Text("RemoveFromLibrary"),
            Content = LocalizationService.Text("RemoveFromLibraryConfirm"),
            PrimaryButtonText = LocalizationService.Text("RemoveFromLibrary"),
            CloseButtonText = LocalizationService.Text("Cancel"),
            DefaultButton = ContentDialogButton.Close,
        };

        if (await ContentDialogCoordinator.ShowAsync(confirm) != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            var movieId = _movie.Id;
            await Task.Run(() => AppServices.Repair.RemoveMovieFromLibrary(movieId));
            BackRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception error)
        {
            ReportFailure(error);
        }
    }
}
