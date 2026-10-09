using DropSort.UI.Models;
using DropSort.UI.Services;
using DropSort.UI.Views;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
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
    private Page? _currentPage;
    private NavigationPaneState _navigationPane = new(true);
    private long _paneOpenCallbackToken;
    private bool _isApplyingResponsiveChange;

    public MainWindow()
    {
        // The stored theme and language are applied before the pages are localized or themed, so the
        // shell opens the way the user left it instead of flashing the defaults first.
        var (theme, language) = AppServices.RestorePreferences();
        ThemeService.Restore(theme);
        LocalizationService.Restore(language);

        InitializeComponent();
        _navigationPane = new NavigationPaneState(AppSettingsStore.ReadWideNavigationPaneOpen());
        AppServices.UiDispatcherQueue = this.DispatcherQueue;
        ConfigureNativeWindow();
        HostPages();
        WireNavigation();
        ShellNavigation.SizeChanged += ShellNavigation_SizeChanged;
        _paneOpenCallbackToken = ShellNavigation.RegisterPropertyChangedCallback(
            NavigationView.IsPaneOpenProperty, ShellNavigation_IsPaneOpenChanged);
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
        ShellNavigation.SizeChanged -= ShellNavigation_SizeChanged;
        ShellNavigation.UnregisterPropertyChangedCallback(NavigationView.IsPaneOpenProperty, _paneOpenCallbackToken);
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
        _myListsPage.ShowSelected += OpenShow;
        _movieDetailsPage.BackRequested += (_, _) => Navigate(_detailsReturnDestination);
        _movieDetailsPage.ShowRequested += OpenShowById;
        _showDetailsPage.BackRequested += (_, _) => Navigate(_detailsReturnDestination);
        _checkLibraryPage.MovieReviewRequested += OpenMovieForReview;
        _settingsPage.OperationsLogRequested += (_, _) => Navigate("operations");
        _settingsPage.LibraryDataChanged += (_, _) =>
        {
            _libraryPage.InvalidateCatalog();
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
        if (ReferenceEquals(sender, _libraryPage) && _movieDetailsPage.IsLoadedSuccessfully)
            RecordLibraryActivity(() => _libraryPage.RecordMovieOpened(movie.Id));
    }

    private void OpenShow(object? sender, TVShowRecord show)
    {
        _detailsReturnDestination = ReturnDestinationFor(sender);
        _showDetailsPage.SetReturnDestination(_detailsReturnDestination);
        _showDetailsPage.SetReturnDestination(_detailsReturnDestination);
        _showDetailsPage.LoadShow(show.Id);
        Navigate("show");
        if (ReferenceEquals(sender, _libraryPage) && _showDetailsPage.IsLoadedSuccessfully)
            RecordLibraryActivity(() => _libraryPage.RecordShowOpened(show.Id));
    }

    private void RecordLibraryActivity(Action record)
    {
        try { record(); }
        catch (Exception)
        {
            _libraryPage.InvalidateCatalog();
            _ = ShowActivityWarningAsync();
        }
    }

    private async System.Threading.Tasks.Task ShowActivityWarningAsync()
    {
        try
        {
            await ContentDialogCoordinator.ShowAsync(new ContentDialog
            {
                XamlRoot = _currentPage?.XamlRoot,
                RequestedTheme = ThemeService.ElementTheme,
                FlowDirection = LocalizationService.FlowDirection,
                Title = LocalizationService.Text("SortRecent"),
                Content = LocalizationService.Text("RecentSaveFailed"),
                CloseButtonText = LocalizationService.Text("Close"),
            });
        }
        catch (Exception) { /* The window can close before the queued warning is shown. */ }
    }

    /// <summary>
    /// Opens a show by id after a manual "Move to series" repair. Back returns to wherever the movie was
    /// opened from, since that is the return destination already in effect.
    /// </summary>
    private void OpenShowById(object? sender, int showId)
    {
        _showDetailsPage.SetReturnDestination(_detailsReturnDestination);
        _showDetailsPage.LoadShow(showId);
        Navigate("show");
    }

    /// <summary>
    /// Opens a movie's details from a Check Library metadata-issue row so the user can review and rematch
    /// its metadata. Back returns to Check Library, since that is where the review was started.
    /// </summary>
    private void OpenMovieForReview(object? sender, int movieId)
    {
        _detailsReturnDestination = "check";
        _movieDetailsPage.SetReturnDestination("check");
        _movieDetailsPage.LoadMovie(movieId);
        Navigate("details");
    }

    private string ReturnDestinationFor(object? sender) => sender switch
    {
        _ when ReferenceEquals(sender, _homePage) => "home",
        _ when ReferenceEquals(sender, _myListsPage) => "lists",
        _ => "library",
    };

    /// <summary>
    /// Sidebar navigation is driven by ItemInvoked, not SelectionChanged: SelectionChanged does not fire
    /// when the user clicks the item that is already selected, which is exactly the case when a detail
    /// page keeps its origin section (Home / Library / My Lists) highlighted. ItemInvoked fires on every
    /// click, so pressing Home from a movie or show always navigates to Home.
    /// </summary>
    private void ShellNavigation_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer?.Tag is string destination)
        {
            Navigate(destination);
        }
    }

    /// <summary>
    /// Esc is the keyboard Back. It runs the same navigation as the visible Back action for the current
    /// inner page, and returns false on a root page so the key is left unhandled (the window never
    /// closes on Esc). Transient controls (dialogs, flyouts, menus, pickers) handle Esc themselves and
    /// this accelerator is not invoked while one of them owns the key.
    /// </summary>
    private void EscAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        // Dismiss temporary navigation before applying the existing page Back rule.
        if (_navigationPane.ShouldDismissOverlay(ShellNavigation.IsPaneOpen))
        {
            ShellNavigation.IsPaneOpen = false;
            args.Handled = true;
            return;
        }

        args.Handled = GoBack();
    }

    private void ShellNavigation_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        // XAML sizes are client DIPs. Only zone transitions change the native pane state.
        if (!_navigationPane.TryResize(args.NewSize.Width, out var isPaneOpen))
        {
            return;
        }

        _isApplyingResponsiveChange = true;
        try
        {
            ShellNavigation.PaneDisplayMode = _navigationPane.Zone switch
            {
                NavigationZone.Wide => NavigationViewPaneDisplayMode.Left,
                NavigationZone.Medium => NavigationViewPaneDisplayMode.LeftCompact,
                _ => NavigationViewPaneDisplayMode.LeftMinimal,
            };
            ShellNavigation.IsPaneOpen = isPaneOpen;
        }
        finally
        {
            _isApplyingResponsiveChange = false;
        }
    }

    private void ShellNavigation_IsPaneOpenChanged(DependencyObject sender, DependencyProperty property)
    {
        if (ShellNavigation.PaneDisplayMode == NavigationViewPaneDisplayMode.Left
            && _navigationPane.RememberWidePreference(ShellNavigation.ActualWidth,
                ShellNavigation.IsPaneOpen, _isApplyingResponsiveChange))
        {
            AppSettingsStore.WriteWideNavigationPaneOpen(_navigationPane.WidePaneOpen);
        }
    }

    /// <summary>
    /// The single Back rule shared by the visible Back button and Esc: a detail page returns to the
    /// section it was opened from, the operations log returns to Settings, and a root page has no back
    /// destination (returns false).
    /// </summary>
    private bool GoBack()
    {
        switch (_currentPage)
        {
            case MovieDetailsPage:
            case TVShowDetailsPage:
                Navigate(_detailsReturnDestination);
                return true;
            case OperationsLogPage:
                Navigate("settings");
                return true;
            default:
                return false;
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
                if (_currentPage is IDeactivatableView deactivatable)
                {
                    deactivatable.Deactivate();
                }

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
                "check" => CheckLibraryNavItem,
                _ => LibraryNavItem,
            };
        }

        // Keep the sidebar highlight in sync with the shown page. Setting SelectedItem
        // programmatically does not raise ItemInvoked, so this never re-enters navigation.
        ShellNavigation.SelectedItem = selected;

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
        UpdateNavigationPaneBackgrounds();
        UpdateTitleBarColors();
    }

    private void UpdateNavigationPaneBackgrounds()
    {
        // Slate and Dark share ElementTheme.Dark. Bind the pane brushes explicitly on each app
        // theme change, using element-resolved colors so native theme/contrast updates stay live.
        var resources = (ResourceDictionary)ShellNavigation.Resources.ThemeDictionaries["Default"];
        var overlay = (SolidColorBrush)resources["NavigationViewDefaultPaneBackground"];
        var inline = (SolidColorBrush)resources["NavigationViewExpandedPaneBackground"];
        var surface = ThemeService.UsesSlateSurface ? (FrameworkElement)SlateSurface : RootGrid;
        BindingOperations.SetBinding(overlay, SolidColorBrush.ColorProperty, new Binding
        {
            Source = surface,
            Path = new PropertyPath("Background.Color"),
            Mode = BindingMode.OneWay,
        });

        if (ThemeService.UsesSlateSurface)
        {
            BindingOperations.SetBinding(inline, SolidColorBrush.ColorProperty, new Binding
            {
                Source = SlateSurface,
                Path = new PropertyPath("Background.Color"),
                Mode = BindingMode.OneWay,
            });
        }
        else
        {
            inline.ClearValue(SolidColorBrush.ColorProperty);
            inline.Color = Colors.Transparent;
        }
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
