using TBDMovies.UI.Services;
using Xunit;

namespace TBDMovies.Tests.UI;

public sealed class ArtworkViewerLayoutTests
{
    [Theory]
    [InlineData(1920, 1080, 1920, 1080)]
    [InlineData(1920, 1080, 400, 600)]
    [InlineData(960, 640, 1920, 1080)]
    [InlineData(960, 640, 400, 600)]
    [InlineData(1360, 860, 1000, 1000)]
    public void Fit_preserves_image_ratio_and_leaves_space_for_the_frame_and_outside_clicks(
        double viewportWidth, double viewportHeight, double imageWidth, double imageHeight)
    {
        var size = ArtworkViewerLayout.Fit(viewportWidth, viewportHeight, imageWidth, imageHeight);
        Assert.Equal(imageWidth / imageHeight, size.Width / size.Height, precision: 6);
        Assert.InRange(size.Width, 1, Math.Min(1040, viewportWidth - 160));
        Assert.InRange(size.Height, 1, Math.Min(700, viewportHeight - 160));
        // Five units of frame distance (4 padding + 1 border) on each side still leave outside space.
        Assert.True(size.Width + 10 < viewportWidth);
        Assert.True(size.Height + 10 < viewportHeight);
    }

    [Fact]
    public void An_image_that_has_not_decoded_yet_still_has_bounded_layout()
    {
        var size = ArtworkViewerLayout.Fit(960, 640, 0, 0);
        Assert.InRange(size.Width, 1, 800);
        Assert.InRange(size.Height, 1, 480);
    }
}
