using Xunit;

namespace DropSort.Tests.UI;

/// <summary>
/// Guards the ContentDialog safety boundary involved in the manual-match crash: optional
/// background poster work must never try to show a competing dialog while matching is open.
/// </summary>
public sealed class ManualMatchCrashRegressionTests
{
    [Theory]
    [InlineData("MovieDetailsPage.xaml.cs", "private void UpdatePosterVisuals()", "private bool HasExternalId()")]
    [InlineData("TVShowDetailsPage.xaml.cs", "private void UpdatePosterVisuals()", "private bool HasExternalId()")]
    public void Background_poster_failure_does_not_open_competing_content_dialog(
        string fileName,
        string methodStart,
        string methodEnd)
    {
        var source = File.ReadAllText(Path.Combine(FindUiRoot(), "Views", fileName));
        var start = source.IndexOf(methodStart, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find {methodStart} in {fileName}.");
        var end = source.IndexOf(methodEnd, start, StringComparison.Ordinal);
        Assert.True(end > start, $"Could not find {methodEnd} after {methodStart} in {fileName}.");

        var posterWorker = source[start..end];
        Assert.Contains("catch (Exception)", posterWorker, StringComparison.Ordinal);
        Assert.DoesNotContain("ReportFailure", posterWorker, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowMessageAsync", posterWorker, StringComparison.Ordinal);
    }

    [Fact]
    public void App_owned_content_dialogs_are_serialized_through_the_coordinator()
    {
        var uiRoot = FindUiRoot();
        var viewFiles = Directory.EnumerateFiles(Path.Combine(uiRoot, "Views"), "*.cs", SearchOption.TopDirectoryOnly);

        foreach (var file in viewFiles)
        {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain("await dialog.ShowAsync()", source, StringComparison.Ordinal);
        }

        var coordinator = File.ReadAllText(Path.Combine(uiRoot, "Services", "ContentDialogCoordinator.cs"));
        Assert.Contains("await Gate.WaitAsync();", coordinator, StringComparison.Ordinal);
        Assert.Contains("finally", coordinator, StringComparison.Ordinal);
        Assert.Contains("Gate.Release();", coordinator, StringComparison.Ordinal);
        Assert.Contains("return await dialog.ShowAsync();", coordinator, StringComparison.Ordinal);
    }

    private static string FindUiRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "DropSort.UI");
            if (Directory.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate DropSort.UI source root.");
    }
}
