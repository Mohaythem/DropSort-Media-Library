using DropSort.UI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace DropSort.UI.Views;

public sealed partial class SettingsPage : Page, ILocalizableView, IActivatableView
{
    private static readonly string DataFolderPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DropSort");

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
    private string _sessionToken = string.Empty;

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
        MovieFoldersLabelText.Text = LocalizationService.Text("MovieFolders");
        MovieFoldersHelpText.Text = LocalizationService.Text("MovieFoldersHelp");
        ManageMovieFoldersButton.Content = LocalizationService.Text("Manage");
        TvFoldersLabelText.Text = LocalizationService.Text("TvFolders");
        TvFoldersHelpText.Text = LocalizationService.Text("TvFoldersHelp");
        ManageTvFoldersButton.Content = LocalizationService.Text("Manage");
        DataFolderLabelText.Text = LocalizationService.Text("LibraryDataFolder");
        DataFolderHelpText.Text = LocalizationService.Text("LibraryDataFolderHelp");
        DataFolderPathText.Text = DataFolderPath;
        OpenDataFolderButton.Content = LocalizationService.Text("OpenFolder");

        TmdbHeadingText.Text = LocalizationService.Text("Tmdb");
        TmdbStatusHelpText.Text = LocalizationService.Text("TmdbStatusHelp");
        TokenLabelText.Text = LocalizationService.Text("ReadAccessToken");
        TokenHelpText.Text = LocalizationService.Text("ReadAccessTokenHelp");
        TokenBox.PlaceholderText = LocalizationService.Text("TmdbTokenPlaceholder");
        UseTokenButton.Content = LocalizationService.Text("UseForSession");
        ClearTokenButton.Content = LocalizationService.Text("Clear");
        TestConnectionButton.Content = LocalizationService.Text("TestConnection");
        SetupGuideButton.Content = LocalizationService.Text("SetupGuide");

        LibraryDataHeadingText.Text = LocalizationService.Text("LibraryData");
        ExportLabelText.Text = LocalizationService.Text("ExportData");
        ExportHelpText.Text = LocalizationService.Text("ExportHelp");
        ExportButton.Content = LocalizationService.Text("Export");
        ImportLabelText.Text = LocalizationService.Text("ImportData");
        ImportHelpText.Text = LocalizationService.Text("ImportHelp");
        ImportButton.Content = LocalizationService.Text("Import");

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

    private void RefreshTmdbStatus()
    {
        var isConnected = _sessionToken.Length > 0;
        TmdbStatusText.Text = LocalizationService.Text(isConnected ? "Connected" : "NotConfigured");
        TmdbConnectedDot.Visibility = isConnected ? Visibility.Visible : Visibility.Collapsed;
        TmdbMissingDot.Visibility = isConnected ? Visibility.Collapsed : Visibility.Visible;
        ClearTokenButton.IsEnabled = isConnected;
        TestConnectionButton.IsEnabled = isConnected;
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
    /// Picks the movies or TV root and stores it. The movies root is also the approved destination that
    /// Organize File moves into, which is why choosing it here enables that action on Movie Details.
    /// </summary>
    private async void ManageFolders_Click(object sender, RoutedEventArgs e)
    {
        var isMovies = ReferenceEquals(sender, ManageMovieFoldersButton);

        try
        {
            var picker = new Windows.Storage.Pickers.FolderPicker();
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, MainWindow.WindowHandle);

            if (await picker.PickSingleFolderAsync() is not Windows.Storage.StorageFolder folder)
            {
                return;
            }

            if (isMovies)
            {
                AppSettingsStore.MovieFolder = folder.Path;
            }
            else
            {
                AppSettingsStore.ShowFolder = folder.Path;
            }

            RefreshFolders();
        }
        catch (Exception error)
        {
            await ShowMessageAsync(LocalizationService.Text("Failed"), error.Message);
        }
    }

    /// <summary>Shows the configured roots, or the help text when none is set yet.</summary>
    private void RefreshFolders()
    {
        MovieFoldersHelpText.Text = AppSettingsStore.MovieFolder is { Length: > 0 } movies
            ? movies
            : LocalizationService.Text("MovieFoldersHelp");
        TvFoldersHelpText.Text = AppSettingsStore.ShowFolder is { Length: > 0 } shows
            ? shows
            : LocalizationService.Text("TvFoldersHelp");
    }

    /// <summary>Opening the data folder is fully implemented through the shell launcher.</summary>
    private void OpenDataFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (Directory.Exists(DataFolderPath))
        {
            _ = Launcher.LaunchFolderPathAsync(DataFolderPath);
        }
    }

    /// <summary>
    /// The token is held for this session only. Persisting it and calling TMDB both belong to the V2
    /// metadata backend, so the status pill reflects the in-memory value.
    /// </summary>
    private void UseTokenButton_Click(object sender, RoutedEventArgs e)
    {
        var token = TokenBox.Password.Trim();

        try
        {
            if (AppServices.Settings.ApplyTmdbSessionToken(token))
            {
                _sessionToken = token;
            }
        }
        catch (Exception)
        {
            _sessionToken = string.Empty;
        }

        RefreshTmdbStatus();
    }

    private void ClearTokenButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            AppServices.Settings.ClearTmdbSessionToken();
        }
        catch (Exception)
        {
        }

        _sessionToken = string.Empty;
        TokenBox.Password = string.Empty;
        RefreshTmdbStatus();
    }

    /// <summary>
    /// There is no TMDB client in this build - the metadata provider answers "unconfigured" - so a
    /// connection cannot be verified. The button states that plainly instead of reporting a pass.
    /// </summary>
    private async void TestConnectionButton_Click(object sender, RoutedEventArgs e) => await ShowMessageAsync(
        LocalizationService.Text("TestConnection"),
        LocalizationService.Text("TmdbNoClientHelp"));

    private void SetupGuideButton_Click(object sender, RoutedEventArgs e) =>
        _ = Launcher.LaunchUriAsync(new Uri("https://developer.themoviedb.org/docs/authentication-application"));

    /// <summary>
    /// Exporting and importing a library snapshot has no application contract in this build, so both
    /// say so rather than writing a file that would not be a real snapshot.
    /// </summary>
    private async void ExportButton_Click(object sender, RoutedEventArgs e) => await ShowMessageAsync(
        LocalizationService.Text("ExportData"),
        LocalizationService.Text("SnapshotUnsupportedHelp"));

    private async void ImportButton_Click(object sender, RoutedEventArgs e) => await ShowMessageAsync(
        LocalizationService.Text("ImportData"),
        LocalizationService.Text("SnapshotUnsupportedHelp"));

    private void ViewLogButton_Click(object sender, RoutedEventArgs e) =>
        OperationsLogRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Clears every watch event, movie by movie, through the personal library. Preferences and the
    /// watchlist are untouched, which is what the row promises.
    /// </summary>
    private async void ClearHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (!await ConfirmAsync("ClearHistory", "ClearHistoryHelp"))
        {
            return;
        }

        var removed = 0;

        try
        {
            await Task.Run(() =>
            {
                foreach (var movie in AppServices.Library.ListMovies())
                {
                    foreach (var watch in AppServices.Personal.ListWatchEvents(movie.Id))
                    {
                        AppServices.Personal.RemoveWatchEvent(watch.Id);
                        removed++;
                    }
                }
            });
        }
        catch (Exception error)
        {
            await ShowMessageAsync(LocalizationService.Text("Failed"), error.Message);
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
            await ShowMessageAsync(LocalizationService.Text("Failed"), error.Message);
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

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
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

        await dialog.ShowAsync();
    }
}
