using System.Text;
using DropSort.Application.Dto;
using DropSort.Domain.Core.Operations;
using DropSort.UI.Models;
using DropSort.UI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace DropSort.UI.Views;

public sealed partial class OperationsLogPage : Page, ILocalizableView, IActivatableView
{
    private string _statusFilter = "all";

    /// <summary>
    /// The journal rows the filter currently shows, as application items. Copy, Save and the row count
    /// all read this one list, so what is written can never differ from what is on screen.
    /// </summary>
    private IReadOnlyList<OperationHistoryItem> _visible = [];

    /// <summary>
    /// The tab strip raises Checked while the XAML is still being parsed, before the log surface
    /// exists; the constructor applies the initial filter instead.
    /// </summary>
    private readonly bool _isReady;

    public OperationsLogPage()
    {
        this.InitializeComponent();
        _isReady = true;
        ApplyLocalization();
    }

    public event EventHandler? BackRequested;

    public void Activate()
    {
        CopiedInfoBar.IsOpen = false;
        Refresh();
    }

    public void ApplyLocalization()
    {
        FlowDirection = LocalizationService.FlowDirection;
        PageTitleText.Text = LocalizationService.Text("OperationsLog");
        BackButtonText.Text = LocalizationService.Text("Settings");
        CopyLogButtonText.Text = LocalizationService.Text("CopyLog");
        SaveLogButtonText.Text = LocalizationService.Text("SaveLog");
        FilterAllItem.Content = LocalizationService.Text("All");
        FilterSuccessItem.Content = LocalizationService.Text("Success");
        FilterAttentionItem.Content = LocalizationService.Text("NeedsAttention");
        CopiedInfoBar.Severity = InfoBarSeverity.Informational;
        CopiedInfoBar.Message = LocalizationService.Text("LogCopied");
        EmptyState.Title = LocalizationService.Text("NoEntries");
        EmptyState.Message = LocalizationService.Text("NoEntriesHelp");
        Refresh();
    }

    private void Refresh()
    {
        var entries = BuildEntries();
        OperationsRepeater.ItemsSource = entries;
        EntriesCountText.Text = LocalizationService.Format(
            entries.Count == 1 ? "EntryFormat" : "EntriesFormat",
            entries.Count);

        var isEmpty = entries.Count == 0;
        LogSurface.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;
        EmptyState.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
        CopyLogButton.IsEnabled = !isEmpty;
        SaveLogButton.IsEnabled = !isEmpty;
    }

    /// <summary>The entries on screen as tab-separated text - the shape both Copy and Save write.</summary>
    private string BuildLogText()
    {
        var builder = new StringBuilder();
        foreach (var entry in _visible.Select(ToRecord))
        {
            builder
                .Append(entry.Timestamp).Append('\t')
                .Append(entry.Operation).Append('\t')
                .Append(entry.Target).Append('\t')
                .Append(entry.Status).Append('\t')
                .AppendLine(entry.Detail);
        }

        return builder.ToString();
    }

    /// <summary>
    /// The journal, newest first, reduced to the active filter. Every row is a real recorded file
    /// operation: DropSort only writes one when it moves or renames a file, so a library that has only
    /// ever registered files in place has an empty log - and the page says so rather than inventing
    /// entries. The filtered application items are kept in <see cref="_visible" /> so Copy and Save
    /// write exactly these rows.
    /// </summary>
    private IReadOnlyList<OperationLogRecord> BuildEntries()
    {
        IReadOnlyList<OperationHistoryItem> items;

        try
        {
            items = AppServices.History.ListOperationHistory(new OperationHistoryQuery(Limit: 200));
        }
        catch (Exception)
        {
            items = [];
        }

        _visible = [.. items.Where(item => _statusFilter switch
        {
            "success" => IsSuccess(item),
            "attention" => !IsSuccess(item),
            _ => true,
        })];

        return [.. _visible.Select(ToRecord)];
    }

    /// <summary>A committed or filesystem-verified operation is the success case; everything else is not.</summary>
    private static bool IsSuccess(OperationHistoryItem item) =>
        item.State is OperationState.Committed or OperationState.FsVerified;

    private static OperationLogRecord ToRecord(OperationHistoryItem item)
    {
        var local = item.Timestamp.ToLocalTime();
        var status = item.State switch
        {
            OperationState.Committed or OperationState.FsVerified => "success",
            OperationState.Failed or OperationState.RecoveryRequired => "failed",
            _ => "warning",
        };

        var detail = item.DestinationPath is { Length: > 0 } destination
            ? (item.SourcePath ?? string.Empty) + " -> " + destination
            : item.SourcePath ?? string.Empty;

        return new OperationLogRecord(
            LocalizationService.Digits(local.ToString("yyyy-MM-dd HH:mm:ss", LocalizationService.Culture)),
            LocalizationService.Digits(local.ToString("HH:mm", LocalizationService.Culture)),
            LocalizationService.Text(item.Type == OperationType.Move ? "OperationMove" : "OperationRename"),
            string.IsNullOrWhiteSpace(item.MovieTitle) ? Path.GetFileName(item.SourcePath ?? string.Empty) : item.MovieTitle,
            status,
            detail);
    }

    private void StatusTab_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isReady)
        {
            return;
        }

        if (sender is RadioButton { Tag: string filter } && filter != _statusFilter)
        {
            _statusFilter = filter;
            Refresh();
        }
    }

    /// <summary>Copying is fully implemented: the entries on screen are written to the clipboard as TSV.</summary>
    private void CopyLogButton_Click(object sender, RoutedEventArgs e)
    {
        var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        package.SetText(BuildLogText());
        Clipboard.SetContent(package);
        ShowInfo(LocalizationService.Text("LogCopied"), InfoBarSeverity.Informational);
    }

    /// <summary>
    /// Saving writes exactly the rows on screen, next to the user's documents, which needs no picker and
    /// no V2 backend. A failure is reported in the same InfoBar as the success: a save that silently did
    /// nothing is indistinguishable from one that worked.
    /// </summary>
    private void SaveLogButton_Click(object sender, RoutedEventArgs e)
    {
        var folder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var path = Path.Combine(folder, "dropsort-operations-log.txt");

        try
        {
            AppServices.History.SaveOperationHistory(_visible, path);
            ShowInfo(LocalizationService.Text("LogSaved"), InfoBarSeverity.Success);
        }
        catch (Exception error)
        {
            ShowInfo(LocalizationService.Format("LogSaveFailed", error.Message), InfoBarSeverity.Error);
        }
    }

    private void ShowInfo(string message, InfoBarSeverity severity)
    {
        CopiedInfoBar.Severity = severity;
        CopiedInfoBar.Message = message;
        CopiedInfoBar.IsOpen = true;
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);
}
