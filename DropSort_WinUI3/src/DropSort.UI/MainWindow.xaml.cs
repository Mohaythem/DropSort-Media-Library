using DropSort.UI.Models;
using DropSort.UI.Services;
using DropSort.UI.Views;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;

namespace DropSort.UI;

public sealed partial class MainWindow : Window
{
    private readonly HomePage _homePage = new();
    private readonly LibraryPage _libraryPage = new();
    private readonly MyListsPage _myListsPage = new();
    private readonly AddMediaPage _addMediaPage = new();
    private readonly CheckLibraryPage _checkLibraryPage = new();
    private readonly SettingsPage _settingsPage = new();
    private readonly OperationsLogPage _operationsLogPage = new();
    private readonly MovieDetailsPage _movieDetailsPage = new();
    private readonly TVShowDetailsPage _showDetailsPage = new();
    private string _detailsReturnDestination = "library";
    private AppWindow? _appWindow;
    private bool _isNavigating;
    private Page? _currentPage;

    public MainWindow()
    {
        // The stored theme and language are applied before the pages are localized or themed, so the
        // shell opens the way the user left it instead of flashing the defaults first.
        var (theme, language) = AppServices.RestorePreferences();
        ThemeService.Restore(theme);
        LocalizationService.Restore(language);

        InitializeComponent();
        ConfigureNativeWindow();
        HostPages();
        WireNavigation();
        LocalizationService.LanguageChanged += OnLanguageChanged;
        ThemeService.ThemeChanged += OnThemeChanged;
        ApplyTheme();
        ApplyLocalization();

        // The pages are field initializers, so they were constructed - and localized - before the
        // stored language was restored above. Re-localizing them here is what makes a shell that
        // starts in Arabic actually open in Arabic instead of English text inside a mirrored shell.
        LocalizeViews();
        Navigate("home");

        // The composition root owns the journal store's long-lived SQLite connection. Closing the
        // shell is this app's shutdown - it is the only window - so that is where the stack is closed
        // and the write-ahead log gets check-pointed.
        Closed += OnWindowClosed;
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        Closed -= OnWindowClosed;
        LocalizationService.LanguageChanged -= OnLanguageChanged;
        ThemeService.ThemeChanged -= OnThemeChanged;
        AppServices.Shutdown();
    }

    /// <summary>
    /// All pages are parented once and stay parented; navigation only flips Visibility, so every
    /// page keeps its scroll offset, selected tab and typed search text across navigation.
    /// </summary>
    private void HostPages()
    {
        Page[] pages =
        [
            _homePage,
            _libraryPage,
            _myListsPage,
            _addMediaPage,
            _checkLibraryPage,
            _settingsPage,
            _operationsLogPage,
            _movieDetailsPage,
            _showDetailsPage,
        ];

        foreach (var page in pages)
        {
            page.Visibility = Visibility.Collapsed;
            ContentHost.Children.Add(page);
        }
    }

    private void ConfigureNativeWindow()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarDragRegion);

        var windowHandle = WindowNative.GetWindowHandle(this);
        WindowHandle = windowHandle;
        var windowId = Win32Interop.GetWindowIdFromWindow(windowHandle);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        _appWindow.Resize(new Windows.Graphics.SizeInt32(1360, 860));
    }

    /// <summary>
    /// The shell window's handle. A WinRT file or folder picker has no owner window in an unpackaged
    /// app, so it has to be initialized with one before it can be shown.
    /// </summary>
    internal static nint WindowHandle { get; private set; }

    private void WireNavigation()
    {
        _homePage.MovieSelected += OpenMovie;
        _homePage.ShowSelected += OpenShow;
        _homePage.LibraryRequested += (_, _) => Navigate("library");
        _libraryPage.MovieSelected += OpenMovie;
        _libraryPage.ShowSelected += OpenShow;
        _myListsPage.MovieSelected += OpenMovie;
        _movieDetailsPage.BackRequested += (_, _) => Navigate(_detailsReturnDestination);
        _showDetailsPage.BackRequested += (_, _) => Navigate(_detailsReturnDestination);
        _settingsPage.OperationsLogRequested += (_, _) => Navigate("operations");
        _settingsPage.LibraryDataChanged += (_, _) =>
        {
            _libraryPage.Activate();
            _homePage.Activate();
            _myListsPage.Activate();
        };
        _operationsLogPage.BackRequested += (_, _) => Navigate("settings");

        // Registering media changes the catalog every other page reads, so the cached pages are
        // refreshed straight away instead of waiting for the next navigation.
        _addMediaPage.MediaRegistered += (_, _) =>
        {
            _libraryPage.Activate();
            _homePage.Activate();
        };
    }

    /// <summary>
    /// Details are always re-read from the catalog by id: the card that was clicked carries only what
    /// a card shows, and the stored preference or watch history may have changed since it was built.
    /// </summary>
    private void OpenMovie(object? sender, MovieRecord movie)
    {
        _detailsReturnDestination = ReturnDestinationFor(sender);
        _movieDetailsPage.SetReturnDestination(_detailsReturnDestination);
        _movieDetailsPage.LoadMovie(movie.Id);
        Navigate("details");
    }

    private void OpenShow(object? sender, TVShowRecord show)
    {
        _detailsReturnDestination = ReturnDestinationFor(sender);
        _showDetailsPage.SetReturnDestination(_detailsReturnDestination);
        _showDetailsPage.SetReturnDestination(_detailsReturnDestination);
        _showDetailsPage.LoadShow(show.Id);
        Navigate("show");
    }

    private string ReturnDestinationFor(object? sender) => sender switch
    {
        _ when ReferenceEquals(sender, _homePage) => "home",
        _ when ReferenceEquals(sender, _myListsPage) => "lists",
        _ => "library",
    };

    private void ShellNavigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (!_isNavigating && args.SelectedItemContainer?.Tag is string destination)
        {
            Navigate(destination);
        }
    }

    private void Navigate(string destination)
    {
        Page page = destination switch
        {
            "library" => _libraryPage,
            "lists" => _myListsPage,
            "add" => _addMediaPage,
            "check" => _checkLibraryPage,
            "settings" => _settingsPage,
            "operations" => _operationsLogPage,
            "details" => _movieDetailsPage,
            "show" => _showDetailsPage,
            _ => _homePage,
        };

        if (!ReferenceEquals(page, _currentPage))
        {
            if (_currentPage is not null)
            {
                _currentPage.Visibility = Visibility.Collapsed;
            }

            page.Visibility = Visibility.Visible;
            _currentPage = page;
        }

        var selected = destination switch
        {
            "library" or "details" or "show" => LibraryNavItem,
            "lists" => MyListsNavItem,
            "add" => AddMediaNavItem,
            "check" => CheckLibraryNavItem,
            "settings" or "operations" => SettingsNavItem,
            _ => HomeNavItem,
        };

        if (destination is "details" or "show")
        {
            selected = _detailsReturnDestination switch
            {
                "home" => HomeNavItem,
                "lists" => MyListsNavItem,
                _ => LibraryNavItem,
            };
        }

        _isNavigating = true;
        ShellNavigation.SelectedItem = selected;
        _isNavigating = false;

        if (page is IActivatableView activatable)
        {
            activatable.Activate();
        }
    }

    private void ApplyLocalization()
    {
        RootGrid.FlowDirection = LocalizationService.FlowDirection;

        // Font properties inherit down the tree, but only from an element that owns them: a Grid
        // does not, the shell's NavigationView does, and everything the user reads - the pane, the
        // title bar text and every page inside ContentHost - sits underneath these two.
        var family = new FontFamily(LocalizationService.FontFamily);
        ShellNavigation.FontFamily = family;
        WindowTitleText.FontFamily = family;

        Title = LocalizationService.Text("WindowTitle");
        SetNavigationItemText(HomeNavItem, "Home");
        SetNavigationItemText(LibraryNavItem, "Library");
        SetNavigationItemText(MyListsNavItem, "MyLists");
        SetNavigationItemText(AddMediaNavItem, "AddMedia");
        SetNavigationItemText(CheckLibraryNavItem, "CheckLibrary");
        SetNavigationItemText(SettingsNavItem, "Settings");
    }

    private static void SetNavigationItemText(NavigationViewItem item, string key)
    {
        var text = LocalizationService.Text(key);
        item.Content = text;
        item.SetValue(AutomationProperties.NameProperty, text);
        ToolTipService.SetToolTip(item, text);
    }

    /// <summary>
    /// One assignment drives the whole shell: RootGrid is the window's content root, so every page,
    /// card, input and flyout under it re-resolves its {ThemeResource} brushes the moment the
    /// requested theme changes - no page needs to be rebuilt and no cached page keeps the old theme.
    /// Nothing here resolves a brush out of Application.Resources: that lookup answers for the
    /// process theme, not the element theme, which is what used to keep the shell dark in Light.
    /// </summary>
    private void ApplyTheme()
    {
        RootGrid.RequestedTheme = ThemeService.ElementTheme;
        SlateSurface.Visibility = ThemeService.UsesSlateSurface ? Visibility.Visible : Visibility.Collapsed;
        UpdateTitleBarColors();
    }

    private void UpdateTitleBarColors()
    {
        if (_appWindow?.TitleBar is not AppWindowTitleBar titleBar)
        {
            return;
        }

        var isDark = ThemeService.ElementTheme != ElementTheme.Light;
        var foreground = isDark ? Colors.White : Colors.Black;
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonInactiveForegroundColor = ColorHelper.FromArgb(0x8B, foreground.R, foreground.G, foreground.B);
        titleBar.ButtonHoverBackgroundColor = ColorHelper.FromArgb(0x1A, foreground.R, foreground.G, foreground.B);
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonPressedBackgroundColor = ColorHelper.FromArgb(0x33, foreground.R, foreground.G, foreground.B);
        titleBar.ButtonPressedForegroundColor = foreground;
    }

    private void OnThemeChanged(object? sender, EventArgs e) => ApplyTheme();

    /// <summary>
    /// Every page is cached for the lifetime of the shell, so all of them have to be re-localized:
    /// only re-localizing the visible one would leave stale text behind the first navigation.
    /// </summary>
    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        ApplyLocalization();
        LocalizeViews();
    }

    private void LocalizeViews()
    {
        ILocalizableView[] views =
        [
            _homePage,
            _libraryPage,
            _myListsPage,
            _addMediaPage,
            _checkLibraryPage,
            _settingsPage,
            _operationsLogPage,
            _movieDetailsPage,
            _showDetailsPage,
        ];

        foreach (var view in views)
        {
            view.ApplyLocalization();
        }
    }
}
