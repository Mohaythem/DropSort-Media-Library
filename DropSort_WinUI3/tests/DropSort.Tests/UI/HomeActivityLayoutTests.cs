using DropSort.UI.Services;
using Xunit;

namespace DropSort.Tests.UI;

public sealed class HomeActivityLayoutTests
{
    [Theory]
    [InlineData(384, 2, 2, false)]
    [InlineData(450, 8, 3, true)]
    [InlineData(180, 1, 1, true)]
    [InlineData(900, 1, 1, false)]
    [InlineData(900, 0, 0, false)]
    public void Poster_shelf_keeps_existing_cell_size_and_reveals_the_next_card(
        double width, int items, int rendered, bool seeAll)
    {
        Assert.Equal(rendered, HomeActivityLayout.CalculateItemsToRender(width, items, 192, 0));
        Assert.Equal(seeAll, HomeActivityLayout.ShouldShowSeeAll(width, items, 192, 0));
    }

    [Theory]
    // 1. Zero items: never show See All
    [InlineData(800, 0, false)]
    // 2. One item that fits fully: no See All
    [InlineData(500, 1, false)]
    // 3. Exact fill (e.g., 2 items at 652 DIP = 2 * 320 + 12 gap): no See All
    [InlineData(652, 2, false)]
    // 4. More items than fully fit, showing a partial card: See All is visible
    [InlineData(750, 3, true)]
    [InlineData(750, 10, true)]
    // Partial card when window is narrower than one card: See All is visible
    [InlineData(200, 1, true)]
    public void ShouldShowSeeAll_FollowsFullyVisibleCapacity(double width, int totalItems, bool expected)
    {
        Assert.Equal(expected, HomeActivityLayout.ShouldShowSeeAll(width, totalItems));
    }

    [Theory]
    // 0 items
    [InlineData(800, 0, 0)]
    // 1 item within 500 DIP
    [InlineData(500, 1, 1)]
    // Exact fill: 2 items within 652 DIP
    [InlineData(652, 2, 2)]
    // Partial card: 3 items within 750 DIP (2 full + 1 partial)
    [InlineData(750, 3, 3)]
    // Excess items beyond the partial card are not rendered on the shelf
    [InlineData(750, 10, 3)]
    public void CalculateItemsToRender_RevealsAtMostOnePartialCard(double width, int totalItems, int expected)
    {
        Assert.Equal(expected, HomeActivityLayout.CalculateItemsToRender(width, totalItems));
    }

    [Fact]
    public void CardDimensions_AreFixed320x196()
    {
        Assert.Equal(320, HomeActivityLayout.CardWidth);
        Assert.Equal(196, HomeActivityLayout.CardHeight);
    }
}
