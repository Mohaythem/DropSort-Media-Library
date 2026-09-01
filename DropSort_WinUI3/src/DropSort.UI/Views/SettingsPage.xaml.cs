using DropSort.UI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace DropSort.UI.Views;

public sealed partial class SettingsPage : Page, ILocalizableView, IActivatableView
{
    private static readonly string DataFolderPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DropSort");

    private bool _isSyncing;
    private string _sessionToken = string.Empty;

    public SettingsPage()
    {
        this.InitializeComponent();
        ApplyLocalization();
    }

    public event EventHandler? OperationsLogRequested;

    public void Activate() => SyncSelectors();

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
        if (_isSyncing)
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
        if (_isSyncing)
        {
            return;
        }

        LocalizationService.SetLanguage(
            LanguageSelector.SelectedIndex == 1 ? UiLanguage.Arabic : UiLanguage.English);
    }

    /// <summary>
    /// Folder roots are stored by the V2 configuration layer, which does not exist yet. The rows and
    /// their buttons stay visible and native; managing a root is inert for now.
    /// </summary>
    private void ManageFolders_Click(object sender, RoutedEventArgs e)
    {
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
        _sessionToken = TokenBox.Password.Trim();
        RefreshTmdbStatus();
    }

    private void ClearTokenButton_Click(object sender, RoutedEventArgs e)
    {
        _sessionToken = string.Empty;
        TokenBox.Password = string.Empty;
        RefreshTmdbStatus();
    }

    /// <summary>Verifying the token needs the metadata client, so this action is inert for now.</summary>
    private void TestConnectionButton_Click(object sender, RoutedEventArgs e)
    {
    }

    private void SetupGuideButton_Click(object sender, RoutedEventArgs e) =>
        _ = Launcher.LaunchUriAsync(new Uri("https://developer.themoviedb.org/docs/authentication-application"));

    /// <summary>Export and import need the snapshot writer, so both remain inert.</summary>
    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
    }

    private void ImportButton_Click(object sender, RoutedEventArgs e)
    {
    }

    private void ViewLogButton_Click(object sender, RoutedEventArgs e) =>
        OperationsLogRequested?.Invoke(this, EventArgs.Empty);

    private void ClearHistoryButton_Click(object sender, RoutedEventArgs e) =>
        _ = ConfirmAsync("ClearHistory", "ClearHistoryHelp");

    private void ClearLibraryButton_Click(object sender, RoutedEventArgs e) =>
        _ = ConfirmAsync("ClearLibraryData", "ClearLibraryDataHelp");

    /// <summary>
    /// The destructive confirmations are real, native and correctly themed. Erasing data needs the
    /// V2 store, so confirming currently changes nothing.
    /// </summary>
    private async Task ConfirmAsync(string titleKey, string messageKey)
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

        await dialog.ShowAsync();
    }
}
