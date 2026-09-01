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

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }

    /// <summary>
    /// XAML load failures surface as a stowed exception with no message in the event log, so the
    /// real exception is written to %TEMP% before the process dies.
    /// </summary>
    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        try
        {
            var path = Path.Combine(Path.GetTempPath(), "dropsort_ui_crash.log");
            File.WriteAllText(path, DateTimeOffset.Now.ToString("O") + Environment.NewLine + e.Exception);
        }
        catch (IOException)
        {
            // Diagnostics are best effort: never mask the original failure.
        }
    }
}
