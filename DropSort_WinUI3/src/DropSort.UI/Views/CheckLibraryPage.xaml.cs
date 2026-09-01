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
    private bool _cancelRequested;

    public CheckLibraryPage()
    {
        this.InitializeComponent();
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
        CancelCheckButton.IsEnabled = isChecking && !_cancelRequested;
        CheckProgressPanel.Visibility = isChecking ? Visibility.Visible : Visibility.Collapsed;

        var hasResult = _state == LibraryCheckState.Complete && _result is not null;
        CheckSummaryPanel.Visibility = hasResult ? Visibility.Visible : Visibility.Collapsed;

        if (!hasResult)
        {
            IssuesSection.Visibility = Visibility.Collapsed;
            NoIssuesState.Visibility = Visibility.Collapsed;
            return;
        }

        var result = _result!;
        var issues = BuildIssues(result);
        var totalItems = result.FileProgress.CheckedFiles + result.TotalMovies;
        var passed = Math.Max(0, totalItems - issues.Count);

        SummaryHeadingText.Text = LocalizationService.Text("CheckComplete");
        SummaryHelpText.Text = LocalizationService.Text(
            issues.Count == 0 ? "CheckHealthyHelp" : "CheckCompleteHelp");
        PassedValueText.Text = LocalizationService.Number(passed);
        AttentionValueText.Text = LocalizationService.Number(issues.Count);
        TotalValueText.Text = LocalizationService.Number(totalItems);

        IssuesRepeater.ItemsSource = issues;
        IssuesSection.Visibility = issues.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        NoIssuesState.Visibility = issues.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// The issue rows: first the files that are registered but no longer on disk, then the movies whose
    /// metadata is incomplete. A missing file keeps its registration - that is the product contract -
    /// so it is reported, never removed.
    /// </summary>
    private static IReadOnlyList<LibraryIssueDisplayRecord> BuildIssues(LibraryHealthProgress result)
    {
        var rows = new List<LibraryIssueDisplayRecord>();

        foreach (var missing in MissingFiles())
        {
            rows.Add(new LibraryIssueDisplayRecord(
                Path.GetFileName(missing.CurrentPath),
                LocalizationService.Text("IssueMovieMissing"),
                LocalizationService.Text("OpenFolder")));
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
    /// Reads the missing files straight from the catalog after the pass, which is where the pass just
    /// recorded them.
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
        _state = LibraryCheckState.Checking;
        _cancelRequested = false;
        _failure = null;
        _result = null;
        CheckProgressBar.IsIndeterminate = true;
        CheckProgressText.Text = LocalizationService.Format("CheckedCountFormat", 0);
        Refresh();

        try
        {
            var result = await Task.Run(() => AppServices.Reconciliation.CheckLibrary(
                progress: update => DispatcherQueue.TryEnqueue(() => ReportProgress(update)),
                isCancelled: () => _cancelRequested));

            _result = result;
            _state = LibraryCheckState.Complete;
        }
        catch (OperationCanceledException)
        {
            _state = LibraryCheckState.Cancelled;
        }
        catch (Exception error)
        {
            _failure = error.Message;
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
        _cancelRequested = true;
        CancelCheckButton.IsEnabled = false;
    }

    /// <summary>
    /// The row action opens the folder a missing file used to live in, which is the one thing that can
    /// be done about it without a relink candidate. Metadata rows have nothing to open, so they only
    /// state the issue.
    /// </summary>
    private async void IssueAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string item })
        {
            return;
        }

        var missing = MissingFiles()
            .FirstOrDefault(file => string.Equals(
                Path.GetFileName(file.CurrentPath),
                item,
                StringComparison.OrdinalIgnoreCase));

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
            await ShowMessageAsync(LocalizationService.Text("Failed"), error.Message);
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
