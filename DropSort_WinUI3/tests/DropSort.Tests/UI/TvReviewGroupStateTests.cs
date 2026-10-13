using DropSort.UI.Services;
using Xunit;

namespace DropSort.Tests.UI;

public sealed class TvReviewGroupStateTests
{
    private sealed class Leaf(bool eligible, bool selected)
    {
        public bool Eligible = eligible;
        public bool Selected = selected;
    }

    [Fact]
    public void Selection_includes_collapsed_leaves_and_preserves_ineligible_files()
    {
        var leaves = new[] { new Leaf(true, true), new Leaf(true, false), new Leaf(false, false) };
        var group = new TvReviewGroupState<Leaf>(leaves) { IsExpanded = false };
        Assert.Null(group.Selection(l => l.Eligible, l => l.Selected));
        Assert.Empty(group.VisibleMembers);
        group.Select(true, l => l.Eligible, (l, value) => l.Selected = value);
        Assert.True(group.Selection(l => l.Eligible, l => l.Selected));
        Assert.False(leaves[2].Selected);
        group.IsExpanded = true;
        Assert.Equal(leaves, group.VisibleMembers);
    }

    [Fact]
    public void Season_selection_changes_only_that_season_and_series_reflects_partial_selection()
    {
        var leaves = new[] { new Leaf(true, true), new Leaf(true, true), new Leaf(true, true) };
        var series = new TvReviewGroupState<Leaf>(leaves);
        var season = new TvReviewGroupState<Leaf>(leaves[..2]);
        season.Select(false, l => l.Eligible, (l, value) => l.Selected = value);
        Assert.False(season.Selection(l => l.Eligible, l => l.Selected));
        Assert.True(leaves[2].Selected);
        Assert.Null(series.Selection(l => l.Eligible, l => l.Selected));
    }
}
