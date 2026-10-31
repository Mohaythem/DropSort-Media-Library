using System.Runtime.InteropServices;
using DropSort.UI.Services;
using Microsoft.UI.Xaml;

namespace DropSort.UI;

public partial class App : Microsoft.UI.Xaml.Application
{
    private Window? _window;

    public App()
    {
        this.InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Claim single-instance ownership before any window exists. A second DropSort process must not
        // open a normal (empty) window it can never fill: the library is already owned, so migration,
        // recovery and every database read are refused. Tell the user once and exit instead. This never
        // weakens LibraryInstanceGuard - it acts on the same refusal, only earlier.
        if (!AppServices.TryClaimSingleInstance())
        {
            _ = MessageBox(
                IntPtr.Zero,
                LocalizationService.Text("AlreadyRunningBody"),
                LocalizationService.Text("AlreadyRunningTitle"),
                MessageBoxOkIcon);
            Environment.Exit(0);
            return;
        }

        // Keep ownership on this UI thread. Complete migrations and journal recovery before any
        // pages can issue actions, but let the dispatcher run while large files are verified.
        await Task.Run(AppServices.Initialize);

        _window = new MainWindow();
        _window.Activate();
    }

    private const uint MessageBoxOkIcon = 0x00000040; // MB_OK | MB_ICONINFORMATION

    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    /// <summary>
    /// XAML load failures surface as a stowed exception with no message in the event log, so the
    /// real exception is written to %TEMP% before the process dies.
    /// </summary>
    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        try
        {
            var path = Path.Combine(Path.GetTempPath(), "tbd_movies_ui_crash.log");
            File.WriteAllText(path, DateTimeOffset.Now.ToString("O") + Environment.NewLine + e.Exception);
        }
        catch (IOException)
        {
            // Diagnostics are best effort: never mask the original failure.
        }
    }
}
