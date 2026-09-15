using DropSort.Application.Dto;
using DropSort.UI.Models;
using DropSort.UI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DropSort.UI.Views;

/// <summary>The states the library check can be in.</summary>
public enum LibraryCheckState
{
    Idle,
    Checking,
    Complete,
    Cancelled,
    Failed,
}

/// <summary>
/// Check Library.
/// <para>
/// The check is the real reconciliation pass: every registered media file is stat'd on disk, a file
/// that has disappeared is marked missing (it stays registered - nothing is ever deleted), and each
/// movie's metadata is graded. It runs on a worker thread, reports live progress, and Cancel stops it
/// between files. Nothing starts it but this page: there is no scan at startup.
/// </para>
/// </summary>
public sealed partial class CheckLibraryPage : Page, ILocalizableView, IActivatableView
{
    private LibraryCheckState _state = LibraryCheckState.Idle;
    private LibraryHealthProgress? _result;
    private string? _failure;
    private IReadOnlyList<Domain.Core.Operations.StaleTempFileInfo> _staleTemps = [];

    /// <summary>
    /// The registered files the last pass found missing, captured once when the pass ends. Refresh runs
    /// on every language change and every navigation, and re-reading the catalog from there would put a
    /// database query behind each of those.
    /// </summary>
    private IReadOnlyList<Domain.Library.Movies.MediaFile> _missing = [];

    /// <summary>
    /// Which of those missing files are episode files, resolved once when the pass ends so the rows and
    /// the counts do not each query the catalog again.
    /// </summary>
    private HashSet<int> _missingEpisodeFiles = [];

    /// <summary>
    /// Cancellation for the running pass. A CancellationTokenSource is the safe way to hand a stop
    /// signal to a worker thread; a plain bool field is not guaranteed to be observed there.
    /// </summary>
    private CancellationTokenSource? _cancellation;

    public CheckLibraryPage()
    {
        this.InitializeComponent();
        Unloaded += (_, _) => _cancellation?.Cancel();
        ApplyLocalization();
    }

    /// <summary>Navigating here must not start a check; only the button does.</summary>
    public void Activate() => Refresh();

    public void ApplyLocalization()
    {
        FlowDirection = LocalizationService.FlowDirection;
        PageTitleText.Text = LocalizationService.Text("CheckLibrary");
        DescriptionText.Text = LocalizationService.Text("CheckDescription");
        CancelCheckButton.Content = LocalizationService.Text("Cancel");
        PassedLabelText.Text = LocalizationService.Text("Passed");
        AttentionLabelText.Text = LocalizationService.Text("NeedsAttention");
        TotalLabelText.Text = LocalizationService.Text("Items");
        IssuesHeadingText.Text = LocalizationService.Text("IssuesHeading");
        NoIssuesState.Title = LocalizationService.Text("NoIssues");
        NoIssuesState.Message = LocalizationService.Text("NoIssuesHelp");
        Refresh();
    }

    private void Refresh()
    {
        var isChecking = _state == LibraryCheckState.Checking;

        CheckHeadingText.Text = LocalizationService.Text(isChecking ? "CheckingLibrary" : "LibraryCheck");
        CheckHelpText.Text = _state switch
        {
            LibraryCheckState.Checking => LocalizationService.Text("CheckingHelp"),
            LibraryCheckState.Cancelled => LocalizationService.Text("CheckCancelledHelp"),
            LibraryCheckState.Failed => _failure ?? LocalizationService.Text("Failed"),
            _ => LocalizationService.Text("LibraryCheckHelp"),
        };

        RunCheckButton.Content = LocalizationService.Text(
            _state is LibraryCheckState.Complete or LibraryCheckState.Cancelled or LibraryCheckState.Failed
                ? "RunAgain"
                : "StartCheck");
        RunCheckButton.IsEnabled = !isChecking;
        RunCheckButton.Visibility = isChecking ? Visibility.Collapsed : Visibility.Visible;
        CancelCheckButton.Visibility = isChecking ? Visibility.Visible : Visibility.Collapsed;
        CancelCheckButton.IsEnabled = isChecking && _cancellation is { IsCancellationRequested: false };
        CheckProgressPanel.Visibility = isChecking ? Visibility.Visible : Visibility.Collapsed;

        var hasResult = _state == LibraryCheckState.Complete && _result is not null;
        CheckSummaryPanel.Visibility = hasResult ? Visibility.Visible : Visibility.Collapsed;
        CrashTempInfo.IsOpen = hasResult && _staleTemps.Count > 0;
        CrashTempInfo.Title = LocalizationService.Text("CrashTempTitle");
        CrashTempInfo.Message = LocalizationService.Format("CrashTempHelp", _staleTemps.Count);
        CrashTempPaths.Text = string.Join(Environment.NewLine, _staleTemps.Select(file => file.FilePath));

        if (!hasResult)
        {
            IssuesSection.Visibility = Visibility.Collapsed;
            NoIssuesState.Visibility = Visibility.Collapsed;
            return;
        }

        var result = _result!;
        var issues = BuildIssues(result);

        // The unit is the movie, not the movie plus its file: counting both made a four-movie library
        // report eight items. A movie needs attention when its file is gone or its metadata is thin,
        // and the file figures are reported separately in the help line under the heading.
        var attention = AttentionItemCount(result);
        var total = Math.Max(result.TotalMovies + EpisodeTotal(), attention);
        var passed = Math.Max(0, total - attention);

        SummaryHeadingText.Text = LocalizationService.Text("CheckComplete");
        SummaryHelpText.Text = attention == 0
            ? LocalizationService.Text("CheckHealthyHelp")
            : LocalizationService.Format("CheckIssuesFormat", attention, total)
                + " "
                + LocalizationService.Format(
                    "FilesCheckedFormat",
                    result.FileProgress.CheckedFiles,
                    result.FileProgress.MissingFiles);
        PassedValueText.Text = LocalizationService.Number(passed);
        AttentionValueText.Text = LocalizationService.Number(attention);
        TotalValueText.Text = LocalizationService.Number(total);

        IssuesRepeater.ItemsSource = issues;
        IssuesSection.Visibility = issues.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        NoIssuesState.Visibility = issues.Count == 0 && _staleTemps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// How many movies need attention. A movie with both a missing file and thin metadata is one movie
    /// with a problem, so the two sources are unioned by movie id rather than added up.
    /// </summary>
    private int AttentionItemCount(LibraryHealthProgress result)
    {
        var movies = new HashSet<int>();
        var episodes = new HashSet<int>();

        foreach (var missing in _missing)
        {
            if (missing.MovieId is int movieId)
            {
                movies.Add(movieId);
            }
            else if (EpisodeForFile(missing.Id) is int episodeId)
            {
                episodes.Add(episodeId);
            }
        }

        foreach (var item in result.CurrentIssues)
        {
            movies.Add(item.MovieId);
        }

        return movies.Count + episodes.Count;
    }

    /// <summary>The episode a registered file belongs to, or null when it is not an episode file.</summary>
    private static int? EpisodeForFile(int mediaFileId)
    {
        try
        {
            return AppServices.Tv.FindEpisodeForMediaFile(mediaFileId);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>How many episodes the catalog holds, so the total counts TV as well as movies.</summary>
    private static int EpisodeTotal()
    {
        try
        {
            return AppServices.Tv.ListShows().Sum(show => show.EpisodeCount);
        }
        catch (Exception)
        {
            return 0;
        }
    }

    /// <summary>
    /// The issue rows: first the files that are registered but no longer on disk, then the movies whose
    /// metadata is incomplete. A missing file keeps its registration - that is the product contract -
    /// so it is reported, never removed. The file rows carry their media-file id, which is what the row
    /// action resolves.
    /// </summary>
    private IReadOnlyList<LibraryIssueDisplayRecord> BuildIssues(LibraryHealthProgress result)
    {
        var rows = new List<LibraryIssueDisplayRecord>();

        foreach (var missing in _missing)
        {
            rows.Add(new LibraryIssueDisplayRecord(
                Path.GetFileName(missing.CurrentPath),
                LocalizationService.Text(
                    _missingEpisodeFiles.Contains(missing.Id) ? "IssueEpisodeFileMissing" : "IssueMovieMissing"),
                LocalizationService.Text("OpenFolder"),
                missing.Id,
                LocalizationService.Text("Relink")));
        }

        foreach (var item in result.CurrentIssues)
        {
            rows.Add(new LibraryIssueDisplayRecord(
                item.Title,
                LocalizationService.Text("IssueMetadataReview"),
                LocalizationService.Text("Review")));
        }

        return rows;
    }

    /// <summary>
    /// Reads the missing files straight from the catalog, which is where the pass just recorded them.
    /// Called once per pass; every later redraw uses the captured list.
    /// </summary>
    private static IReadOnlyList<Domain.Library.Movies.MediaFile> MissingFiles()
    {
        try
        {
            return AppServices.MissingMediaFiles();
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>Runs the pass on a worker thread; the UI thread only draws progress.</summary>
    private async void RunCheckButton_Click(object sender, RoutedEventArgs e)
    {
        _cancellation?.Dispose();
        _cancellation = new CancellationTokenSource();
        var token = _cancellation.Token;

        _state = LibraryCheckState.Checking;
        _failure = null;
        _result = null;
        _missing = [];
        _missingEpisodeFiles = [];
        _staleTemps = [];
        CheckProgressBar.IsIndeterminate = true;
        CheckProgressText.Text = LocalizationService.Format("CheckedCountFormat", 0);
        Refresh();

        try
        {
            var result = await Task.Run(() => AppServices.Reconciliation.CheckLibrary(
                progress: update => DispatcherQueue.TryEnqueue(() => ReportProgress(update)),
                isCancelled: () => token.IsCancellationRequested));

            token.ThrowIfCancellationRequested();
            _staleTemps = await Task.Run(() => AppServices.DetectStaleTempFiles(), token);
            token.ThrowIfCancellationRequested();
            _result = result;
            _missing = MissingFiles();
            _missingEpisodeFiles = [.. _missing
                .Select(file => (File: file, Episode: EpisodeForFile(file.Id)))
                .Where(pair => pair.Episode is not null)
                .Select(pair => pair.File.Id)];
            _state = LibraryCheckState.Complete;
        }
        catch (OperationCanceledException)
        {
            _state = LibraryCheckState.Cancelled;
        }
        catch (Exception error)
        {
            _failure = MetadataErrorText.For(error);
            _state = LibraryCheckState.Failed;
        }
        finally
        {
            CheckProgressBar.IsIndeterminate = false;
            Refresh();
        }
    }

    private void ReportProgress(LibraryHealthProgress update) =>
        CheckProgressText.Text = LocalizationService.Format(
            "CheckedCountFormat",
            update.FileProgress.CheckedFiles);

    private void CancelCheckButton_Click(object sender, RoutedEventArgs e)
    {
        _cancellation?.Cancel();
        CancelCheckButton.IsEnabled = false;
    }

    private async void RelinkAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int mediaFileId })
        {
            return;
        }

        var missing = _missing.FirstOrDefault(file => file.Id == mediaFileId);
        if (missing is null)
        {
            return;
        }

        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, MainWindow.WindowHandle);

            var picked = await picker.PickSingleFileAsync();
            if (picked is null)
            {
                return;
            }

            var candidatePath = picked.Path;

            // 1. Validate candidate before mutation
            var preview = AppServices.Reconciliation.PrepareMediaRelink(mediaFileId, candidatePath);

            // 2. Show confirmation dialog
            var message = LocalizationService.Format(
                "RelinkConfirmMessage",
                missing.CurrentPath,
                candidatePath,
                missing.FileSize);

            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                RequestedTheme = ThemeService.ElementTheme,
                FlowDirection = LocalizationService.FlowDirection,
                Title = LocalizationService.Text("RelinkTitle"),
                Content = message,
                PrimaryButtonText = LocalizationService.Text("Confirm"),
                CloseButtonText = LocalizationService.Text("Cancel"),
                DefaultButton = ContentDialogButton.Primary,
            };

            var dialogResult = await dialog.ShowAsync();
            if (dialogResult != ContentDialogResult.Primary)
            {
                AppServices.Reconciliation.DiscardMediaRelinkPreview(preview.PreviewId);
                return;
            }

            // 3. Confirm relink (updates SQLite record, does not move/delete candidate file)
            AppServices.Reconciliation.ConfirmMediaRelink(preview.PreviewId);

            // 4. Immediate UI update
            _missing = _missing.Where(file => file.Id != mediaFileId).ToList();
            _missingEpisodeFiles.Remove(mediaFileId);

            Refresh();

            await ShowMessageAsync(
                LocalizationService.Text("Success"),
                LocalizationService.Text("RelinkSuccess"));
        }
        catch (Exception)
        {
            await ShowMessageAsync(
                LocalizationService.Text("Failed"),
                LocalizationService.Text("RelinkFailedHelp"));
        }
    }

    /// <summary>
    /// The row action opens the folder a missing file used to live in, which is the one thing that can
    /// be done about it without a relink candidate. The button carries the media-file id, so the record
    /// is resolved exactly even when two missing files share a name. Metadata rows have no file and no
    /// id, so they only state the issue.
    /// </summary>
    private async void IssueAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int mediaFileId })
        {
            return;
        }

        var missing = _missing.FirstOrDefault(file => file.Id == mediaFileId);

        if (missing is null)
        {
            return;
        }

        var folder = Path.GetDirectoryName(missing.CurrentPath);

        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            await ShowMessageAsync(LocalizationService.Text("IssuesHeading"), missing.CurrentPath);
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder)
            {
                UseShellExecute = true,
            })?.Dispose();
        }
        catch (Exception error)
        {
            await ShowMessageAsync(LocalizationService.Text("Failed"), MetadataErrorText.For(error));
        }
    }

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
}
