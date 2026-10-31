using System.Xml.Linq;
using Xunit;

namespace TBDMovies.Tests.UI;

/// <summary>
/// Guards the Home-to-My Lists contract for the two read-only activity sections.
/// These checks stay source-level because WinUI controls require an app host.
/// </summary>
public sealed class ActivityListsSourceContractTests
{
    private static readonly string UiRoot = FindUiRoot();

    [Fact]
    public void HomeSeeAllButtonsRaiseDistinctActivityKeys()
    {
        var home = Read("Views", "HomePage.xaml.cs");
        var xaml = Read("Views", "HomePage.xaml");

        Assert.Contains("ActivityListRequested", home, StringComparison.Ordinal);
        Assert.Contains("ContinueSeeAllButton_Click", xaml, StringComparison.Ordinal);
        Assert.Contains("HistorySeeAllButton_Click", xaml, StringComparison.Ordinal);
        Assert.Contains("ActivityListRequested?.Invoke(this, \"continue-watching\")", home, StringComparison.Ordinal);
        Assert.Contains("ActivityListRequested?.Invoke(this, \"recently-watched\")", home, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindowNavigatesToListsAndSelectsRequestedActivitySection()
    {
        var shell = Read("MainWindow.xaml.cs");

        Assert.Contains("_homePage.ActivityListRequested += (_, key)", shell, StringComparison.Ordinal);
        Assert.Contains("Navigate(\"lists\")", shell, StringComparison.Ordinal);
        Assert.Contains("_myListsPage.SelectSystemList(key)", shell, StringComparison.Ordinal);
    }

    [Fact]
    public void MyListsDeclaresBothActivityTabsAndLoadsFullHistory()
    {
        var code = Read("Views", "MyListsPage.xaml.cs");
        var xaml = Read("Views", "MyListsPage.xaml");

        Assert.Contains("new(\"continue-watching\", \"ContinueWatching\", true)", code, StringComparison.Ordinal);
        Assert.Contains("new(\"recently-watched\", \"RecentlyWatched\", true)", code, StringComparison.Ordinal);
        Assert.Contains("SelectSystemList(string key)", code, StringComparison.Ordinal);
        Assert.Contains("ListActive(int.MaxValue)", code, StringComparison.Ordinal);
        Assert.Contains("ListRecentCompleted(int.MaxValue)", code, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ContinueWatchingItem\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"continue-watching\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RecentlyWatchedItem\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Tag=\"recently-watched\"", xaml, StringComparison.Ordinal);
        foreach (var grid in new[] { "ContinueGrid", "HistoryGrid" })
        {
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var element = XDocument.Parse(xaml).Descendants().Single(node => (string?)node.Attribute(x + "Name") == grid);
            Assert.Equal("Enabled", (string?)element.Attribute("ScrollViewer.VerticalScrollMode"));
            Assert.Equal("{StaticResource MediaHorizontalGridViewItemStyle}", (string?)element.Attribute("ItemContainerStyle"));
            var layout = element.Descendants().Single(node => node.Name.LocalName == "ItemsWrapGrid");
            Assert.Equal("Horizontal", (string?)layout.Attribute("Orientation"));
            Assert.Equal("{StaticResource LibraryHorizontalFallbackCellWidth}", (string?)layout.Attribute("ItemWidth"));
            Assert.Equal("{StaticResource LibraryHorizontalCellHeight}", (string?)layout.Attribute("ItemHeight"));
        }

        Assert.DoesNotContain("MediaCardLandscapeWidth", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MediaCardLandscapeHeight", xaml, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([UiRoot, .. parts]));

    private static string FindUiRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TBDMovies.slnx")))
            directory = directory.Parent;
        return Path.Combine(directory?.FullName ?? throw new InvalidOperationException("Solution root not found"), "src", "TBDMovies.UI");
    }
}
