namespace DropSort.UI.Services;

internal static class ArtworkViewerLayout
{
    internal static (double Width, double Height) Fit(double viewportWidth, double viewportHeight,
        double imageWidth, double imageHeight)
    {
        var width = Math.Clamp(double.IsFinite(viewportWidth) ? viewportWidth - 160 : 1040, 1, 1040);
        var height = Math.Clamp(double.IsFinite(viewportHeight) ? viewportHeight - 160 : 700, 1, 700);
        if (!double.IsFinite(imageWidth) || !double.IsFinite(imageHeight) || imageWidth <= 0 || imageHeight <= 0)
            return (width, height);

        var scale = Math.Min(width / imageWidth, height / imageHeight);
        return (imageWidth * scale, imageHeight * scale);
    }
}
