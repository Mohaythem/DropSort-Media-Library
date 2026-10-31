using System;
using System.IO;
using Xunit;

namespace TBDMovies.Tests.UI;

/// <summary>Source-only guards for startup ordering. These do not launch or verify a WinUI window.</summary>
public sealed class P2StartupSourceContractTests
{
    [Fact]
    public void OwnershipPrecedesBackgroundInitialization_AndPagesWaitForRecovery()
    {
        var code = File.ReadAllText(Path.Combine(UiRoot(), "App.xaml.cs"));
        var claim = code.IndexOf("if (!AppServices.TryClaimSingleInstance())", StringComparison.Ordinal);
        var initialize = code.IndexOf("await Task.Run(AppServices.Initialize)", StringComparison.Ordinal);
        var window = code.IndexOf("_window = new MainWindow()", StringComparison.Ordinal);
        Assert.True(claim >= 0 && initialize > claim && window > initialize,
            "Claim library on UI, await worker initialization, then create the shell.");
        Assert.Contains("protected override async void OnLaunched", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ConfigureAwait(false)", code, StringComparison.Ordinal);
    }

    private static string UiRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "src", "TBDMovies.UI");
            if (Directory.Exists(path)) return path;
        }
        throw new DirectoryNotFoundException("TBDMovies.UI source not found");
    }
}
