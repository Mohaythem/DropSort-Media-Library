using Xunit;

namespace DropSort.Tests.UI;

public sealed class Phase3UiSourceContractTests
{
    private static readonly string Root = FindRoot();

    [Fact]
    public void Home_uses_persisted_history_and_continue_services()
    {
        var code = File.ReadAllText(Path.Combine(Root, "Views", "HomePage.xaml.cs"));
        Assert.Contains("ListRecentWatchHistory", code);
        Assert.Contains("ListContinueWatching", code);
        Assert.Contains("HistoryUnavailableHelp", code);
        Assert.Contains("HistoryCard_Click", code);
        // A movie card leads with its own artwork; a TV card is show-grouped and leads with the show's
        // artwork (backdrop, then poster) plus a season-oriented context line.
        Assert.Contains("[item.ArtworkReference, item.BackdropReference, item.PosterReference]", code);
        Assert.Contains("[item.BackdropReference, item.PosterReference, item.ArtworkReference]", code);
        Assert.Contains("HistoryShowContextFormat", code);
        Assert.Contains("item.ShowTitle", code);
    }

    [Fact]
    public void Home_artwork_is_one_way_and_has_a_fallback_surface()
    {
        var xaml = File.ReadAllText(Path.Combine(Root, "Views", "HomePage.xaml"));
        Assert.Contains("ArtworkSource, Mode=OneWay", xaml);
        Assert.Contains("PosterGlyphForegroundBrush", xaml);
        Assert.Contains("HistorySection", xaml);
    }

    [Fact]
    public void Episode_watch_action_is_explicit_and_play_does_not_record()
    {
        var page = File.ReadAllText(Path.Combine(Root, "Views", "TVShowDetailsPage.xaml.cs"));
        var xaml = File.ReadAllText(Path.Combine(Root, "Views", "TVShowDetailsPage.xaml"));
        Assert.Contains("EpisodeMarkWatched_Click", xaml);
        Assert.Contains("RecordEpisodeWatch", page);
        var playStart = page.IndexOf("private async Task PlayEpisodeAsync", StringComparison.Ordinal);
        Assert.True(playStart >= 0);
        Assert.DoesNotContain("RecordEpisodeWatch", page[playStart..]);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DropSort.slnx")))
            directory = directory.Parent;
        return Path.Combine(directory?.FullName ?? throw new InvalidOperationException("Solution root not found"), "src", "DropSort.UI");
    }
}
