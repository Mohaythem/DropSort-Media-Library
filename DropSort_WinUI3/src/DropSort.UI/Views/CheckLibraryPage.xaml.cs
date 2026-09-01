using System.Globalization;
using DropSort.UI.Models;
using DropSort.UI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DropSort.UI.Views;

/// <summary>The states the library check can be in. A future verifier backend drives these.</summary>
public enum LibraryCheckState
{
    Idle,
    Checking,
    Complete,
}

public sealed partial class CheckLibraryPage : Page, ILocalizableView, IActivatableView
{
    private LibraryCheckState _state = LibraryCheckState.Idle;

    public CheckLibraryPage()
    {
        this.InitializeComponent();
        ApplyLocalization();
    }

    public void Activate() => Refresh();

    /// <summary>
    /// Entry point for a future verifier to report progress. The designed progress track is a real
    /// code path instead of a timed animation.
    /// </summary>
    public void SetCheckProgress(int completed, int total)
    {
        _state = LibraryCheckState.Checking;
        CheckProgressBar.Maximum = total <= 0 ? 1 : total;
        CheckProgressBar.Value = completed;
        CheckProgressText.Text = LocalizationService.Format("CheckedOfFormat", completed, total);
        Refresh();
    }

    public void ApplyLocalization()
    {
        FlowDirection = LocalizationService.FlowDirection;
        PageTitleText.Text = LocalizationService.Text("CheckLibrary");
        DescriptionText.Text = LocalizationService.Text("CheckDescription");
        CancelCheckButton.Content = LocalizationService.Text("Cancel");
        SummaryHeadingText.Text = LocalizationService.Text("CheckComplete");
        SummaryHelpText.Text = LocalizationService.Text("CheckCompleteHelp");
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
        CheckHelpText.Text = LocalizationService.Text(isChecking ? "CheckingHelp" : "LibraryCheckHelp");
        RunCheckButton.IsEnabled = !isChecking;
        RunCheckButton.Content = LocalizationService.Text(
            _state == LibraryCheckState.Complete ? "RunAgain" : "RunCheck");
        CheckProgressPanel.Visibility = isChecking ? Visibility.Visible : Visibility.Collapsed;
        CancelCheckButton.Visibility = isChecking ? Visibility.Visible : Visibility.Collapsed;

        var isComplete = _state == LibraryCheckState.Complete;
        CheckSummaryPanel.Visibility = isComplete ? Visibility.Visible : Visibility.Collapsed;

        if (!isComplete)
        {
            IssuesSection.Visibility = Visibility.Collapsed;
            NoIssuesState.Visibility = Visibility.Collapsed;
            return;
        }

        var issues = BuildIssues();
        PassedValueText.Text = LocalizationService.Number(DemoData.CheckPassed);
        AttentionValueText.Text = LocalizationService.Number(issues.Count);
        TotalValueText.Text = LocalizationService.Number(DemoData.CheckTotal);

        IssuesRepeater.ItemsSource = issues;
        IssuesSection.Visibility = issues.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        NoIssuesState.Visibility = issues.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static IReadOnlyList<LibraryIssueDisplayRecord> BuildIssues() =>
        DemoData.Issues
            .Select(issue => new LibraryIssueDisplayRecord(
                issue.Item,
                LocalizationService.Text(IssueKey(issue.Issue)),
                LocalizationService.Text("Review")))
            .ToArray();

    /// <summary>Maps the stored issue reason onto its localization key.</summary>
    private static string IssueKey(string issue) => issue switch
    {
        "Episode file is missing" => "IssueEpisodeMissing",
        "Movie file is missing" => "IssueMovieMissing",
        "Metadata needs review" => "IssueMetadataReview",
        _ => issue,
    };

    /// <summary>
    /// Re-running the verifier needs the V2 file-system layer. The button is real, native and
    /// enabled; it currently reprojects the last known result instead of touching disk.
    /// </summary>
    private void RunCheckButton_Click(object sender, RoutedEventArgs e)
    {
        _state = LibraryCheckState.Complete;
        Refresh();
    }

    /// <summary>
    /// Cancelling returns the card to its idle state. A real verifier would also stop its work here;
    /// the designed control exists and behaves either way.
    /// </summary>
    private void CancelCheckButton_Click(object sender, RoutedEventArgs e)
    {
        _state = LibraryCheckState.Idle;
        Refresh();
    }

    /// <summary>Reviewing an issue needs the recovery flow, so this action stays inert for now.</summary>
    private void IssueAction_Click(object sender, RoutedEventArgs e)
    {
    }
}
