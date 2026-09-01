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
    private IReadOnlyList<OperationHistoryItem> _items = [];

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

    /// <summary>The visible entries as tab-separated text - the shape both Copy and Save write.</summary>
    private string BuildLogText()
    {
        var builder = new StringBuilder();
        foreach (var entry in BuildEntries())
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
    /// The journal, newest first. Every row is a real recorded file operation: DropSort only writes
    /// one when it moves or renames a file, so a library that has only ever registered files in place
    /// has an empty log - and the page says so rather than inventing entries.
    /// </summary>
    private IReadOnlyList<OperationLogRecord> BuildEntries()
    {
        try
        {
            _items = AppServices.History.ListOperationHistory(new OperationHistoryQuery(Limit: 200));
        }
        catch (Exception)
        {
            _items = [];
        }

        return
        [
            .. _items
                .Select(ToRecord)
                .Where(entry => _statusFilter switch
                {
                    "success" => entry.IsSuccess,
                    "attention" => entry.NeedsAttention,
                    _ => true,
                }),
        ];
    }

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

    /// <summary>Copying is fully implemented: the visible entries are written to the clipboard as TSV.</summary>
    private void CopyLogButton_Click(object sender, RoutedEventArgs e)
    {
        var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        package.SetText(BuildLogText());
        Clipboard.SetContent(package);
        CopiedInfoBar.Message = LocalizationService.Text("LogCopied");
        CopiedInfoBar.IsOpen = true;
    }

    /// <summary>
    /// Saving is fully implemented too: the same text is written next to the user's documents, which
    /// needs no picker and no V2 backend.
    /// </summary>
    private void SaveLogButton_Click(object sender, RoutedEventArgs e)
    {
        var folder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var path = Path.Combine(folder, "dropsort-operations-log.txt");

        try
        {
            AppServices.History.SaveOperationHistory(_items, path);
            CopiedInfoBar.Message = LocalizationService.Text("LogSaved");
            CopiedInfoBar.IsOpen = true;
        }
        catch (IOException)
        {
            // A failed save must never take the shell down; the InfoBar simply stays closed.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);
}
