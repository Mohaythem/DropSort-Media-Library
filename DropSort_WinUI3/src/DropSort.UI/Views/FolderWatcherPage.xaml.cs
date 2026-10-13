using System.Collections.ObjectModel;
using DropSort.Application.Dto;
using DropSort.Application.Services;
using DropSort.UI.Models;
using DropSort.UI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace DropSort.UI.Views;

public sealed partial class FolderWatcherPage : Page, ILocalizableView, IActivatableView, IDeactivatableView
{
    private readonly ObservableCollection<FolderWatcherDisplayModel> _folders = [];
    private readonly ObservableCollection<FolderWatcherChangeDisplayModel> _changes = [];
    private readonly Dictionary<string, FolderWatcherDisplayModel> _folderByPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FolderWatcherChangeDisplayModel> _changeByKey = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _operationCancellation;
    private bool _isActive;
    private bool _updateQueued;
    private bool _isPickingFolder;

    public FolderWatcherPage()
    {
        InitializeComponent();
        WatchedFoldersItemsControl.ItemsSource = _folders;
        Root.ItemsSource = _changes;
        ApplyLocalization();
    }

    public event EventHandler<FolderWatcherPathsEventArgs>? ReviewNewRequested;
    public event EventHandler? CheckLibraryRequested;

    public string ReviewLabel => LocalizationService.Text("FolderWatcherReview");
    public string IgnoreLabel => LocalizationService.Text("FolderWatcherIgnore");
    public string RemoveFolderLabel => LocalizationService.Text("FolderWatcherRemoveFolder");

    public void Activate()
    {
        if (_isActive)
        {
            RefreshSnapshot();
            return;
        }

        _isActive = true;
        try
        {
            if (!AppServices.IsAvailable)
            {
                StatusText.Text = AppServices.InitializationError ?? LocalizationService.Text("Failed");
                return;
            }

            AppServices.FolderWatcher.Changed += FolderWatcher_Changed;
            RefreshSnapshot();
        }
        catch (Exception error)
        {
            StatusText.Text = error.Message;
        }
    }

    public void Deactivate()
    {
        if (!_isActive)
        {
            return;
        }

        _isActive = false;
        try { AppServices.FolderWatcher.Changed -= FolderWatcher_Changed; } catch { }
        CancelUiOperation();
    }

    public void ApplyLocalization()
    {
        // The shell owns direction. A second local change can leave cached Grid visuals mirrored.
        ClearValue(FlowDirectionProperty);
        PageTitleText.Text = LocalizationService.Text("FolderWatcherTitle");
        SafetyHelpText.Text = LocalizationService.Text("FolderWatcherHelp");
        FolderLabelText.Text = LocalizationService.Text("FolderWatcherFolderLabel");
        FolderPathBox.PlaceholderText = LocalizationService.Text("FolderWatcherFolderPlaceholder");
        BrowseButton.Content = LocalizationService.Text("Browse");
        AddFolderButton.Content = LocalizationService.Text("FolderWatcherAddFolder");
        ScanNowButton.Content = LocalizationService.Text("FolderWatcherScanNow");
        CancelScanButton.Content = LocalizationService.Text("FolderWatcherCancelScan");
        ChangesHeaderTitle.Text = LocalizationService.Text("FolderWatcherChanges");
        BulkReviewNewButton.Content = LocalizationService.Text("FolderWatcherReview");
        IgnoreAllButton.Content = LocalizationService.Text("FolderWatcherIgnoreAll");
        CancelScanButton.IsEnabled = _operationCancellation is not null;
        foreach (var folder in _folders) folder.RefreshLocalization();
        foreach (var change in _changes) change.RefreshLocalization();
        if (_isActive) RefreshSnapshot(); else RefreshStatus();
    }

    private void FolderWatcher_Changed(object? sender, FolderWatcherChangedEventArgs args)
    {
        if (!_isActive)
        {
            return;
        }

        if (!_updateQueued)
        {
            _updateQueued = true;
            DispatcherQueue.TryEnqueue(() =>
            {
                _updateQueued = false;
                if (_isActive) RefreshSnapshot();
            });
        }
    }

    private void RefreshSnapshot()
    {
        if (!_isActive) return;
        try
        {
            var watcher = AppServices.FolderWatcher;
            if (watcher.InitializationError is not null)
            {
                _folders.Clear();
                _changes.Clear();
                _folderByPath.Clear();
                _changeByKey.Clear();
                StatusText.Text = LocalizationService.Text("Failed") + ": " + watcher.InitializationError.Message;
                RefreshControls();
                return;
            }

            var snapshots = watcher.GetSnapshot();
            var livePaths = snapshots.Select(snapshot => snapshot.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var removed in _folderByPath.Keys.Where(path => !livePaths.Contains(path)).ToArray())
            {
                _folderByPath.Remove(removed);
            }
            _folders.Clear();
            foreach (var snapshot in snapshots.OrderBy(item => item.Path, StringComparer.CurrentCultureIgnoreCase))
            {
                if (!_folderByPath.TryGetValue(snapshot.Path, out var model))
                {
                    model = new FolderWatcherDisplayModel(snapshot);
                    _folderByPath[snapshot.Path] = model;
                }
                else
                {
                    model.Update(snapshot);
                }
                _folders.Add(model);
            }

            _changeByKey.Clear();
            _changes.Clear();
            foreach (var change in snapshots.SelectMany(snapshot => snapshot.Changes)
                .DistinctBy(change => change.Key, StringComparer.OrdinalIgnoreCase)
                .OrderBy(change => change.Path, StringComparer.CurrentCultureIgnoreCase))
            {
                var model = new FolderWatcherChangeDisplayModel(change);
                _changeByKey[change.Key] = model;
                _changes.Add(model);
            }
            RefreshStatus();
        }
        catch (Exception error)
        {
            StatusText.Text = error.Message;
        }
    }

    private void RefreshStatus()
    {
        var count = _changes.Count;
        BulkReviewNewButton.IsEnabled = _changes.Any(change => change.Kind == FolderWatcherChangeKind.New && change.CanReview);
        StatusText.Text = _folders.Count == 0
            ? LocalizationService.Text("FolderWatcherNoFolders")
            : LocalizationService.Format("FolderWatcherSummaryFormat", _folders.Sum(folder => folder.FileCount), count);
    }

    private async void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isPickingFolder || _operationCancellation is not null) return;
        _isPickingFolder = true;
        RefreshControls();
        try
        {
            var picker = new FolderPicker();
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, MainWindow.WindowHandle);
            if (await picker.PickSingleFolderAsync() is StorageFolder folder)
            {
                FolderPathBox.Text = folder.Path;
            }
        }
        catch (Exception error) { await ShowMessageAsync(LocalizationService.Text("Failed"), error.Message); }
        finally { _isPickingFolder = false; RefreshControls(); }
    }

    private async void AddFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var path = FolderPathBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            await ShowMessageAsync(LocalizationService.Text("FolderWatcherAddFolder"), LocalizationService.Text("FolderWatcherFolderPlaceholder"));
            return;
        }

        await RunWatcherOperationAsync(
            token => AppServices.FolderWatcher.AddFolderAsync(path, token),
            clearPathOnSuccess: true);
    }

    private async void ScanNowButton_Click(object sender, RoutedEventArgs e)
    {
        if (_operationCancellation is not null)
        {
            AppServices.FolderWatcher.CancelScan();
            return;
        }
        await RunWatcherOperationAsync(token => AppServices.FolderWatcher.ScanNowAsync(token));
    }

    private void CancelScanButton_Click(object sender, RoutedEventArgs e)
    {
        AppServices.FolderWatcher.CancelScan();
        CancelUiOperation();
    }

    private async void FolderToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox toggle || toggle.Tag is not FolderWatcherDisplayModel folder)
        {
            return;
        }

        var enabled = toggle.IsChecked == true;
        if (!_isActive || folder.IsEnabled == enabled) return;
        await RunWatcherOperationAsync(token => AppServices.FolderWatcher.SetEnabledAsync(folder.Path, enabled, token));
    }

    private async void RemoveFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: FolderWatcherDisplayModel folder }) return;
        await RunWatcherOperationAsync(token => AppServices.FolderWatcher.RemoveFolderAsync(folder.Path, token));
    }

    private async void ReviewChange_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: FolderWatcherChangeDisplayModel change }) return;
        if (!change.CanReview)
        {
            return;
        }
        if (change.Kind == FolderWatcherChangeKind.New)
        {
            ReviewNewRequested?.Invoke(this, new FolderWatcherPathsEventArgs([change.Path]));
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.ElementTheme,
            FlowDirection = LocalizationService.FlowDirection,
            Title = LocalizationService.Text("FolderWatcherReviewTitle"),
            Content = new StackPanel { Spacing = 8, Children =
            {
                new TextBlock { Text = change.Path, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = change.Message, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = change.Details, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = change.RelatedPath, TextWrapping = TextWrapping.Wrap, Visibility = change.RelatedPathVisibility },
                new TextBlock { Text = LocalizationService.Text("FolderWatcherReviewHelp"), TextWrapping = TextWrapping.Wrap }
            }},
            PrimaryButtonText = LocalizationService.Text("FolderWatcherCheckLibrary"),
            CloseButtonText = LocalizationService.Text("Close")
        };
        if (await ContentDialogCoordinator.ShowAsync(dialog) == ContentDialogResult.Primary)
        {
            CheckLibraryRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private async void IgnoreChange_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: FolderWatcherChangeDisplayModel change })
        {
            await RunWatcherOperationAsync(token => AppServices.FolderWatcher.IgnoreChangesAsync([change.Change.Key], token));
        }
    }

    private void BulkReviewNewButton_Click(object sender, RoutedEventArgs e)
    {
        var paths = _changes.Where(change => change.Kind == FolderWatcherChangeKind.New && change.CanReview)
            .Select(change => change.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (paths.Length > 0) ReviewNewRequested?.Invoke(this, new FolderWatcherPathsEventArgs(paths));
    }

    private async void IgnoreAllButton_Click(object sender, RoutedEventArgs e)
    {
        var keys = _changes.Select(change => change.Change.Key).ToArray();
        if (keys.Length > 0) await RunWatcherOperationAsync(token => AppServices.FolderWatcher.IgnoreChangesAsync(keys, token));
    }

    private async Task RunWatcherOperationAsync<T>(Func<CancellationToken, Task<T>> operation, bool clearPathOnSuccess = false)
    {
        if (_operationCancellation is not null) return;
        using var cancellation = new CancellationTokenSource();
        _operationCancellation = cancellation;
        RefreshControls();
        try
        {
            await Task.Run(() => operation(cancellation.Token), cancellation.Token);
            if (clearPathOnSuccess) FolderPathBox.Text = string.Empty;
            RefreshSnapshot();
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { await ShowMessageAsync(LocalizationService.Text("Failed"), error.Message); }
        finally
        {
            if (ReferenceEquals(_operationCancellation, cancellation))
            {
                _operationCancellation = null;
                RefreshControls();
            }
        }
    }

    private async Task RunWatcherOperationAsync(Func<CancellationToken, Task> operation)
    {
        await RunWatcherOperationAsync(async token =>
        {
            await operation(token);
            return true;
        });
    }

    private void RefreshControls()
    {
        var watcherUnavailable = false;
        try
        {
            watcherUnavailable = AppServices.FolderWatcher.InitializationError is not null;
        }
        catch
        {
            watcherUnavailable = true;
        }

        FolderPathBox.IsEnabled = _operationCancellation is null && !_isPickingFolder;
        BrowseButton.IsEnabled = FolderPathBox.IsEnabled;
        AddFolderButton.IsEnabled = _operationCancellation is null && !_isPickingFolder && !watcherUnavailable;
        ScanNowButton.IsEnabled = _operationCancellation is null && !watcherUnavailable;
        CancelScanButton.IsEnabled = _operationCancellation is not null;
    }

    private void CancelUiOperation() => _operationCancellation?.Cancel();

    private async Task ShowMessageAsync(string title, string message)
    {
        if (XamlRoot is null) return;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.ElementTheme,
            FlowDirection = LocalizationService.FlowDirection,
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = LocalizationService.Text("Close")
        };
        await ContentDialogCoordinator.ShowAsync(dialog);
    }
}

public sealed class FolderWatcherPathsEventArgs(IReadOnlyList<string> paths) : EventArgs
{
    public IReadOnlyList<string> Paths { get; } = paths ?? throw new ArgumentNullException(nameof(paths));
}
