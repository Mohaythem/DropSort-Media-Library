using DropSort.UI.Models;
using DropSort.Application.External;
using DropSort.UI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace DropSort.UI.Views;

public sealed partial class SettingsPage : Page, ILocalizableView, IActivatableView, IDeactivatableView
{
    private bool _editingCredential;
    private bool _credentialBusy;
    private CancellationTokenSource? _credentialRequest;

    public void Deactivate()
    {
        _credentialRequest?.Cancel();
        _editingCredential = false;
        TokenBox.Password = string.Empty;
        TmdbFeedback.IsOpen = false;
    }

    /// <summary>
    /// True until the page has pushed the stored theme and language into the two selectors.
    /// <para>
    /// A ComboBox raises SelectionChanged for its initial selection while the XAML is still being
    /// parsed - before the constructor body runs - and the first item is Slate / English. Without this
    /// starting true, that initial event would look like a user choice and overwrite the preference
    /// the shell had just restored.
    /// </para>
    /// </summary>
    private bool _isSyncing = true;

    /// <summary>
    /// False until the user has actually opened this page. A ComboBox whose items have not been
    /// realized yet can raise SelectionChanged late - after the sync flag has been cleared - and report
    /// its first item rather than the index that was assigned. Treating that as a user choice is what
    /// silently reset the stored language to English on startup.
    /// </summary>
    private bool _hasBeenShown;

    public SettingsPage()
    {
        this.InitializeComponent();
        ApplyLocalization();
    }

    public event EventHandler? OperationsLogRequested;

    /// <summary>Raised after the catalog is cleared so the cached pages can re-read it.</summary>
    public event EventHandler? LibraryDataChanged;

    public void Activate()
    {
        _hasBeenShown = true;
        SyncSelectors();
        RefreshFolders();
        RefreshTmdbStatus();
    }

    public void ApplyLocalization()
    {
        FlowDirection = LocalizationService.FlowDirection;
        PageTitleText.Text = LocalizationService.Text("Settings");

        AppearanceHeadingText.Text = LocalizationService.Text("Appearance");
        ThemeLabelText.Text = LocalizationService.Text("Theme");
        RefreshThemeHelp();
        ThemeSlateItem.Content = LocalizationService.Text("Slate");
        ThemeDarkItem.Content = LocalizationService.Text("Dark");
        ThemeLightItem.Content = LocalizationService.Text("Light");
        LanguageLabelText.Text = LocalizationService.Text("Language");
        LanguageHelpText.Text = LocalizationService.Text("LanguageHelp");
        LanguageEnglishItem.Content = LocalizationService.Text("EnglishLanguage");
        LanguageArabicItem.Content = LocalizationService.Text("ArabicLanguage");

        LibraryHeadingText.Text = LocalizationService.Text("LibrarySection");
        MediaFoldersLabelText.Text = LocalizationService.Text("MediaFolders");
        MediaFoldersHelpText.Text = LocalizationService.Text("MediaFoldersHelp");
        AddMediaFolderButton.Content = LocalizationService.Text("AddFolder");
        StorageAdvancedHeadingText.Text = LocalizationService.Text("StorageAdvanced");
        DataFolderLabelText.Text = LocalizationService.Text("LibraryDataFolder");
        DataFolderHelpText.Text = LocalizationService.Text("LibraryDataFolderHelp");
        DataFolderPathText.Text = AppServices.DataFolder;
        OpenDataFolderButton.Content = LocalizationService.Text("OpenFolder");

        TmdbHeadingText.Text = LocalizationService.Text("Tmdb");
        TokenLabelText.Text = LocalizationService.Text("ReadAccessToken");
        TokenHelpText.Text = LocalizationService.Text("ReadAccessTokenHelp");
        TokenBox.PlaceholderText = LocalizationService.Text("TmdbTokenPlaceholder");
        UseTokenButton.Content = LocalizationService.Text("TmdbSaveConnect");
        CancelTokenButton.Content = LocalizationService.Text("Cancel");
        ClearTokenButton.Content = LocalizationService.Text("Remove");
        TestConnectionButton.Content = LocalizationService.Text("TestConnection");
        SetupGuideButton.Content = LocalizationService.Text("SetupGuide");

        HistoryHeadingText.Text = LocalizationService.Text("HistoryRecovery");
        ViewLogLabelText.Text = LocalizationService.Text("OperationsLog");
        HistoryHelpText.Text = LocalizationService.Text("OperationsLogHelp");
        ViewLogButton.Content = LocalizationService.Text("ViewOperationsLog");

        DangerHeadingText.Text = LocalizationService.Text("DangerZone");
        ClearHistoryLabelText.Text = LocalizationService.Text("ClearHistory");
        ClearHistoryHelpText.Text = LocalizationService.Text("ClearHistoryHelp");
        ClearHistoryButton.Content = LocalizationService.Text("Clear");
        ClearLibraryLabelText.Text = LocalizationService.Text("ClearLibraryData");
        ClearLibraryHelpText.Text = LocalizationService.Text("ClearLibraryDataHelp");
        ClearLibraryButton.Content = LocalizationService.Text("Clear");

        SyncSelectors();
        RefreshFolders();
        RefreshTmdbStatus();
    }

    private void SyncSelectors()
    {
        _isSyncing = true;
        ThemeSelector.SelectedIndex = ThemeService.CurrentTheme switch
        {
            AppTheme.Dark => 1,
            AppTheme.Light => 2,
            _ => 0,
        };
        LanguageSelector.SelectedIndex = LocalizationService.CurrentLanguage == UiLanguage.Arabic ? 1 : 0;
        _isSyncing = false;
    }

    /// <summary>
    /// Show credential state without copying a saved secret into any UI control.
    /// </summary>
    private void RefreshTmdbStatus()
    {
        var state = ReadTmdbState();
        var configured = state is TmdbCredentialState.Configured or TmdbCredentialState.AuthenticationFailed;
        TmdbStatusText.Text = LocalizationService.Text(state switch
        {
            TmdbCredentialState.Configured => "TmdbConnected",
            TmdbCredentialState.AuthenticationFailed => "TmdbAuthFailed",
            TmdbCredentialState.StorageUnavailable => "TmdbStorageUnavailable",
            _ => "NotConfigured",
        });
        TmdbStatusHelpText.Text = LocalizationService.Text(state switch
        {
            TmdbCredentialState.Configured => "TmdbSavedHelp",
            TmdbCredentialState.AuthenticationFailed => "TmdbAuthFailedHelp",
            TmdbCredentialState.StorageUnavailable => "TmdbStorageFailureHelp",
            _ => "TmdbNotConfiguredHelp",
        });
        TmdbConnectedDot.Visibility = state == TmdbCredentialState.Configured ? Visibility.Visible : Visibility.Collapsed;
        TmdbMissingDot.Visibility = state == TmdbCredentialState.Configured ? Visibility.Collapsed : Visibility.Visible;
        ChangeTokenButton.Content = LocalizationService.Text(state == TmdbCredentialState.AuthenticationFailed
            ? "TmdbUpdateCredential" : configured ? "TmdbChangeCredential" : "TmdbAddCredential");
        ChangeTokenButton.Visibility = _editingCredential ? Visibility.Collapsed : Visibility.Visible;
        TokenInputPanel.Visibility = _editingCredential ? Visibility.Visible : Visibility.Collapsed;
        UseTokenButton.Visibility = _editingCredential ? Visibility.Visible : Visibility.Collapsed;
        CancelTokenButton.Visibility = _editingCredential ? Visibility.Visible : Visibility.Collapsed;
        ClearTokenButton.Visibility = configured || state == TmdbCredentialState.StorageUnavailable ? Visibility.Visible : Visibility.Collapsed;
        TestConnectionButton.Visibility = configured ? Visibility.Visible : Visibility.Collapsed;
        ChangeTokenButton.IsEnabled = !_credentialBusy;
        UseTokenButton.IsEnabled = !_credentialBusy;
        CancelTokenButton.IsEnabled = !_credentialBusy;
        ClearTokenButton.IsEnabled = !_credentialBusy;
        TestConnectionButton.IsEnabled = configured && !_credentialBusy;
        TokenBox.IsEnabled = !_credentialBusy;
    }

    private void ThemeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSyncing || !_hasBeenShown)
        {
            return;
        }

        ThemeService.SetTheme(ThemeSelector.SelectedIndex switch
        {
            1 => AppTheme.Dark,
            2 => AppTheme.Light,
            _ => AppTheme.Slate,
        });
        RefreshThemeHelp();
    }

    private void RefreshThemeHelp()
    {
        ThemeHelpText.Text = LocalizationService.Text(ThemeService.CurrentTheme switch
        {
            AppTheme.Dark => "ThemeHelpDark",
            AppTheme.Light => "ThemeHelpLight",
            _ => "ThemeHelpSlate",
        });
    }

    private void LanguageSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSyncing || !_hasBeenShown)
        {
            return;
        }

        LocalizationService.SetLanguage(
            LanguageSelector.SelectedIndex == 1 ? UiLanguage.Arabic : UiLanguage.English);
    }

    /// <summary>
    /// Picks a media root without asking the user to classify it as movie or TV content.
    /// </summary>
    /// <summary>Add another media root. Adding never replaces or removes an existing root.</summary>
    private async void AddMediaFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (await PickFolderAsync() is not { } path)
            {
                return;
            }

            if (!AppSettingsStore.AddMediaFolder(path))
            {
                await ShowMessageAsync(LocalizationService.Text("MediaFolders"), LocalizationService.Text("FolderAlreadyAdded"));
            }

            RefreshFolders();
        }
        catch (Exception error)
        {
            await ShowMessageAsync(LocalizationService.Text("Failed"), MetadataErrorText.For(error));
        }
    }

    /// <summary>Replace the selected root with a newly picked one, in place. Never treated as an add.</summary>
    private async void ReplaceMediaFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string oldPath })
        {
            return;
        }

        try
        {
            if (await PickFolderAsync() is not { } newPath)
            {
                return;
            }

            if (!AppSettingsStore.ReplaceMediaFolder(oldPath, newPath))
            {
                await ShowMessageAsync(LocalizationService.Text("MediaFolders"), LocalizationService.Text("FolderAlreadyAdded"));
            }

            RefreshFolders();
        }
        catch (Exception error)
        {
            await ShowMessageAsync(LocalizationService.Text("Failed"), MetadataErrorText.For(error));
        }
    }

    /// <summary>
    /// Remove the selected root from the configuration. This only edits settings; the media files on that
    /// folder are never moved or deleted, and library/watch/list state is untouched.
    /// </summary>
    private async void RemoveMediaFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string path })
        {
            return;
        }

        var confirmed = await ConfirmAsync(
            LocalizationService.Text("RemoveFolder"),
            LocalizationService.Format("RemoveFolderConfirm", path));
        if (!confirmed)
        {
            return;
        }

        AppSettingsStore.RemoveMediaFolder(path);
        RefreshFolders();
    }

    private static async Task<string?> PickFolderAsync()
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, MainWindow.WindowHandle);
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    /// <summary>Shows the configured roots (each with Replace / Remove), or the empty help when none is set.</summary>
    private void RefreshFolders()
    {
        var folders = AppSettingsStore.ConfiguredMediaFolders;
        MediaFoldersRepeater.ItemsSource = folders.Select(path => new MediaFolderRow(path)).ToList();
        MediaFoldersEmptyText.Text = LocalizationService.Text("MediaFoldersNone");
        MediaFoldersEmptyText.Visibility = folders.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Opening the data folder is fully implemented through the shell launcher.</summary>
    private void OpenDataFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (Directory.Exists(AppServices.DataFolder))
        {
            _ = Launcher.LaunchFolderPathAsync(AppServices.DataFolder);
        }
    }

    /// <summary>
    /// Validate a new value before securely replacing the saved credential.
    /// </summary>
    private async void UseTokenButton_Click(object sender, RoutedEventArgs e)
    {
        if (_credentialBusy) return;
        _credentialBusy = true;
        _credentialRequest = new CancellationTokenSource();
        RefreshTmdbStatus();
        try
        {
            var credential = TokenBox.Password;
            TokenBox.Password = string.Empty;
            var result = await AppServices.Settings.SaveTmdbCredentialAsync(
                credential, AppServices.TmdbClient, _credentialRequest.Token);
            if (result.Success)
            {
                _editingCredential = false;
                TmdbFeedback.IsOpen = false;
            }
            else ShowCredentialFeedback(CredentialFailureKey(result.Failure));
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            ShowCredentialFeedback("TmdbCredentialStorageError");
        }
        finally
        {
            TokenBox.Password = string.Empty;
            _credentialRequest.Dispose();
            _credentialRequest = null;
            _credentialBusy = false;
            RefreshTmdbStatus();
        }
    }

    private void ChangeTokenButton_Click(object sender, RoutedEventArgs e)
    {
        TokenBox.Password = string.Empty;
        _editingCredential = true;
        TmdbFeedback.IsOpen = false;
        RefreshTmdbStatus();
        TokenBox.Focus(FocusState.Programmatic);
    }

    private void CancelTokenButton_Click(object sender, RoutedEventArgs e)
    {
        TokenBox.Password = string.Empty;
        _editingCredential = false;
        TmdbFeedback.IsOpen = false;
        RefreshTmdbStatus();
    }

    private static TmdbCredentialState ReadTmdbState()
    {
        try
        {
            return AppServices.IsAvailable ? AppServices.Settings.GetTmdbCredentialState() : TmdbCredentialState.StorageUnavailable;
        }
        catch (Exception)
        {
            return TmdbCredentialState.StorageUnavailable;
        }
    }

    private async void ClearTokenButton_Click(object sender, RoutedEventArgs e)
    {
        if (_credentialBusy || !await ConfirmAsync("TmdbRemoveCredential", "TmdbRemoveConfirm")) return;
        if (_credentialBusy) return;
        _credentialBusy = true;
        RefreshTmdbStatus();
        try
        {
            if (await AppServices.Settings.RemoveTmdbCredentialAsync())
            {
                TokenBox.Password = string.Empty;
                _editingCredential = false;
                TmdbFeedback.IsOpen = false;
            }
            else ShowCredentialFeedback("TmdbCredentialStorageError");
        }
        catch (Exception)
        {
            ShowCredentialFeedback("TmdbCredentialStorageError");
        }
        finally { _credentialBusy = false; RefreshTmdbStatus(); }
    }

    private async void TestConnectionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_credentialBusy) return;
        _credentialBusy = true;
        _credentialRequest = new CancellationTokenSource();
        RefreshTmdbStatus();
        try
        {
            var result = await AppServices.Settings.TestTmdbConnectionAsync(AppServices.Metadata, _credentialRequest.Token);
            var message = MetadataErrorText.For(result);
            await ShowMessageAsync(LocalizationService.Text("TestConnection"), message);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            await ShowMessageAsync(LocalizationService.Text("TestConnection"), MetadataErrorText.For(ex));
        }
        finally
        {
            _credentialRequest.Dispose();
            _credentialRequest = null;
            _credentialBusy = false;
            RefreshTmdbStatus();
        }
    }

    private void ShowCredentialFeedback(string key)
    {
        TmdbFeedback.Message = LocalizationService.Text(key);
        TmdbFeedback.IsOpen = _hasBeenShown && Visibility == Visibility.Visible;
    }

    private static string CredentialFailureKey(TmdbCredentialFailure failure) => failure switch
    {
        TmdbCredentialFailure.Empty => "TmdbCredentialEmpty",
        TmdbCredentialFailure.Authentication => "TmdbNewCredentialRejected",
        TmdbCredentialFailure.Network => "TmdbCredentialNetworkError",
        TmdbCredentialFailure.Timeout => "TmdbCredentialNetworkError",
        TmdbCredentialFailure.Storage => "TmdbCredentialStorageError",
        _ => "TmdbCredentialServiceError",
    };

    private void SetupGuideButton_Click(object sender, RoutedEventArgs e) =>
        _ = Launcher.LaunchUriAsync(new Uri("https://developer.themoviedb.org/docs/authentication-application"));

    private void ViewLogButton_Click(object sender, RoutedEventArgs e) =>
        OperationsLogRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Clears the entire watch history - movie, episode and events orphaned by deleted media - in one
    /// pass. Preferences and the watchlist are untouched, which is what the row promises.
    /// </summary>
    private async void ClearHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (!await ConfirmAsync("ClearHistory", "ClearHistoryHelp"))
        {
            return;
        }

        int removed;

        try
        {
            removed = await Task.Run(() => AppServices.WatchHistory.ClearWatchHistory());
        }
        catch (Exception error)
        {
            await ShowMessageAsync(LocalizationService.Text("Failed"), MetadataErrorText.For(error));
            return;
        }

        LibraryDataChanged?.Invoke(this, EventArgs.Empty);
        await ShowMessageAsync(
            LocalizationService.Text("ClearHistory"),
            LocalizationService.Format("WatchEventsRemovedFormat", removed));
    }

    /// <summary>
    /// Clears the local index only. The catalog rows go away; not one media file on disk is touched -
    /// that is the product contract and the service enforces it.
    /// </summary>
    private async void ClearLibraryButton_Click(object sender, RoutedEventArgs e)
    {
        if (!await ConfirmAsync("ClearLibraryData", "ClearLibraryDataHelp"))
        {
            return;
        }

        try
        {
            var result = AppServices.Settings.ClearLibraryData();
            LibraryDataChanged?.Invoke(this, EventArgs.Empty);
            await ShowMessageAsync(
                LocalizationService.Text("ClearLibraryData"),
                LocalizationService.Format("LibraryClearedFormat", result.MoviesRemoved, result.MediaFilesRemoved));
        }
        catch (Exception error)
        {
            await ShowMessageAsync(LocalizationService.Text("Failed"), MetadataErrorText.For(error));
        }
    }

    /// <summary>The destructive confirmations are real, native and correctly themed.</summary>
    private async Task<bool> ConfirmAsync(string titleKey, string messageKey)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            FlowDirection = LocalizationService.FlowDirection,
            RequestedTheme = ThemeService.ElementTheme,
            Title = LocalizationService.Text(titleKey),
            Content = LocalizationService.Text(messageKey),
            PrimaryButtonText = LocalizationService.Text("Confirm"),
            CloseButtonText = LocalizationService.Text("Cancel"),
            DefaultButton = ContentDialogButton.Close,
        };

        return await ContentDialogCoordinator.ShowAsync(dialog) == ContentDialogResult.Primary;
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            FlowDirection = LocalizationService.FlowDirection,
            RequestedTheme = ThemeService.ElementTheme,
            Title = title,
            Content = message,
            CloseButtonText = LocalizationService.Text("Close"),
        };

        await ContentDialogCoordinator.ShowAsync(dialog);
    }
}
