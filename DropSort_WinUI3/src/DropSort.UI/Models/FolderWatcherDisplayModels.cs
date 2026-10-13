using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using DropSort.Application.Dto;
using DropSort.UI.Services;
using Microsoft.UI.Xaml;

namespace DropSort.UI.Models;

/// <summary>
/// Presentation state for one configured folder-watcher root. The source snapshot remains immutable;
/// this model only exposes values needed by the review page.
/// </summary>
public sealed class FolderWatcherDisplayModel : INotifyPropertyChanged
{
    private FolderWatcherSnapshot _snapshot;

    public FolderWatcherDisplayModel(FolderWatcherSnapshot snapshot)
    {
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        Changes = new ObservableCollection<FolderWatcherChangeDisplayModel>(
            snapshot.Changes.Select(change => new FolderWatcherChangeDisplayModel(change)));
    }

    public string Path => _snapshot.Path;
    public bool IsEnabled => _snapshot.Enabled;
    public bool IsScanning => _snapshot.IsScanning;
    public bool MissedEvents => _snapshot.MissedEvents;
    public FolderWatcherRootStatus Status => _snapshot.Status;
    public DateTimeOffset? LastScanUtc => _snapshot.LastScanUtc;
    public int FileCount => _snapshot.Files.Count;
    public int ChangeCount => _snapshot.Changes.Count;
    public int WarningCount => _snapshot.Warnings.Count;
    public bool HasChanges => ChangeCount > 0;
    public bool HasWarnings => WarningCount > 0 || MissedEvents;
    public IReadOnlyList<string> Warnings => _snapshot.Warnings;
    public ObservableCollection<FolderWatcherChangeDisplayModel> Changes { get; }

    public string StatusLabel => LocalizationService.Text("FolderWatcherStatus_" + Status);
    public string SummaryLabel => LocalizationService.Format(
        "FolderWatcherSummaryFormat", FileCount, ChangeCount);
    public string LastScanLabel => LastScanUtc is DateTimeOffset scan
        ? LocalizationService.Format("FolderWatcherLastScanFormat", scan.ToLocalTime().ToString("g", LocalizationService.Culture))
        : LocalizationService.Text("FolderWatcherNeverScanned");
    public string WarningLabel => HasWarnings
        ? LocalizationService.Format("FolderWatcherWarningCountFormat", WarningCount + (MissedEvents ? 1 : 0))
        : string.Empty;
    public Visibility WarningVisibility => HasWarnings ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ChangesVisibility => HasChanges ? Visibility.Visible : Visibility.Collapsed;
    public string ToggleLabel => LocalizationService.Text(IsEnabled ? "FolderWatcherDisable" : "FolderWatcherEnable");
    public string RemoveLabel => LocalizationService.Text("FolderWatcherRemoveFolder");
    public string AutomationName => string.Join(" · ", Path, StatusLabel, SummaryLabel);

    public void RefreshLocalization()
    {
        foreach (var change in Changes) change.RefreshLocalization();
        OnPropertyChanged(string.Empty);
    }

    public void Update(FolderWatcherSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _snapshot = snapshot;
        Changes.Clear();
        foreach (var change in snapshot.Changes)
        {
            Changes.Add(new FolderWatcherChangeDisplayModel(change));
        }

        RaiseAll();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void RaiseAll()
    {
        OnPropertyChanged(string.Empty);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>Presentation state for one pending filesystem change.</summary>
public sealed class FolderWatcherChangeDisplayModel : INotifyPropertyChanged
{
    public FolderWatcherChangeDisplayModel(FolderWatcherChange change)
    {
        Change = change ?? throw new ArgumentNullException(nameof(change));
    }

    public FolderWatcherChange Change { get; }
    public string Path => Change.Path;
    public string FileName => System.IO.Path.GetFileName(Change.Path);
    public FolderWatcherChangeKind Kind => Change.Kind;
    public string KindLabel => LocalizationService.Text("FolderWatcherChange_" + Kind);
    public string Message => Kind == FolderWatcherChangeKind.PotentialMove
        ? LocalizationService.Text("FolderWatcherChangeHelp_PotentialMove")
        : string.IsNullOrWhiteSpace(Change.Message)
        ? Kind == FolderWatcherChangeKind.New && Change.Current?.IsReady != true
            ? LocalizationService.Text("FolderWatcherNotReady")
            : LocalizationService.Text("FolderWatcherChangeHelp_" + Kind)
        : Change.Message!;
    public bool CanReview => Kind != FolderWatcherChangeKind.New || Change.Current?.IsReady == true;
    public string Details => FormatFact(Change.Current ?? Change.Baseline);
    public string RelatedPath => Change.RelatedPath ?? string.Empty;
    public Visibility RelatedPathVisibility => string.IsNullOrWhiteSpace(Change.RelatedPath)
        ? Visibility.Collapsed : Visibility.Visible;
    public string AutomationName => string.Join(" · ", KindLabel, FileName, Path, Message);
    public string ReviewLabel => LocalizationService.Text("FolderWatcherReview");
    public string IgnoreLabel => LocalizationService.Text("FolderWatcherIgnore");

    public event PropertyChangedEventHandler? PropertyChanged;

    public void RefreshLocalization() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));

    private static string FormatFact(FolderWatcherFileFact? fact)
    {
        if (fact is null)
        {
            return string.Empty;
        }

        return LocalizationService.Format(
            "FolderWatcherFileFactFormat",
            fact.Size,
            fact.LastWriteUtc.ToLocalTime().ToString("g", LocalizationService.Culture));
    }
}
