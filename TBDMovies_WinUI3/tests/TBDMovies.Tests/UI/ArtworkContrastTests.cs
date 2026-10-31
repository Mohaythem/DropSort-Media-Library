using System.Xml.Linq;
using Xunit;

namespace TBDMovies.Tests.UI;

public sealed class ArtworkContrastTests
{
    [Theory]
    [InlineData("Default", 0)]
    [InlineData("Default", 255)]
    [InlineData("Light", 0)]
    [InlineData("Light", 255)]
    public void Episode_badge_text_has_readable_contrast_over_dark_and_bright_artwork(string theme, int backdrop)
    {
        var palette = Palette(theme);
        var foreground = Color(palette, "ArtworkBadgeForegroundBrush");
        var background = Color(palette, "ArtworkBadgeBackgroundBrush");
        var surface = Composite(background, (backdrop, backdrop, backdrop, 255));
        Assert.True(Contrast(foreground, surface) >= 4.5, $"Insufficient badge contrast in {theme}.");
    }

    [Fact]
    public void Light_action_text_has_readable_contrast_on_control_surfaces()
    {
        var palette = Palette("Light");
        var surface = Color(palette, "FigmaSurfaceControlBrush");
        Assert.True(Contrast(Color(palette, "FigmaTextPrimaryBrush"), surface) >= 4.5);
        Assert.True(Contrast(Color(palette, "FigmaTextSecondaryBrush"), surface) >= 4.5);
    }

    [Fact]
    public void Dark_movie_surface_uses_neutral_near_black_palette()
    {
        var palette = Palette("Default");
        Assert.Equal("#FF111111", Value(palette, "FigmaSurfaceAppBrush"));
        Assert.Equal("#FF181818", Value(palette, "FigmaSurfaceSecondaryBrush"));
    }

    [Theory]
    [InlineData("Default", "#FF000000", "#FF000000")]
    [InlineData("Light", "#FF202020", "#FF202020")]
    public void Artwork_viewer_has_distinct_frame_and_canvas(string theme, string frame, string canvas)
    {
        var palette = Palette(theme);
        Assert.Equal(frame, Value(palette, "ArtworkViewerFrameBrush"));
        Assert.Equal(canvas, Value(palette, "ArtworkViewerCanvasBrush"));
    }

    private static XElement Palette(string theme)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var path = Path.Combine(directory.FullName, "src", "TBDMovies.UI", "Resources", "ThemeResources.xaml");
            if (File.Exists(path))
            {
                XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
                return XDocument.Load(path).Descendants().Single(element =>
                    element.Name.LocalName == "ResourceDictionary" && (string?)element.Attribute(x + "Key") == theme);
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("UI theme resources were not found.");
    }

    private static (double R, double G, double B, double A) Color(XElement palette, string key)
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var value = Value(palette, key);
        Assert.Equal(9, value.Length);
        var argb = Convert.ToUInt32(value[1..], 16);
        return ((argb >> 16) & 255, (argb >> 8) & 255, argb & 255, argb >> 24);
    }

    private static string Value(XElement palette, string key)
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        return (string)palette.Elements().Single(element => (string?)element.Attribute(x + "Key") == key).Attribute("Color")!;
    }

    private static (double R, double G, double B, double A) Composite(
        (double R, double G, double B, double A) foreground, (double R, double G, double B, double A) background)
    {
        var alpha = foreground.A / 255;
        return (foreground.R * alpha + background.R * (1 - alpha),
            foreground.G * alpha + background.G * (1 - alpha),
            foreground.B * alpha + background.B * (1 - alpha), 255);
    }

    private static double Contrast((double R, double G, double B, double A) first,
        (double R, double G, double B, double A) second)
    {
        static double Linear(double channel) => channel / 255 <= 0.04045
            ? channel / 255 / 12.92 : Math.Pow((channel / 255 + 0.055) / 1.055, 2.4);
        static double Luminance((double R, double G, double B, double A) color) =>
            0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
        var firstLight = Luminance(first);
        var secondLight = Luminance(second);
        return (Math.Max(firstLight, secondLight) + 0.05) / (Math.Min(firstLight, secondLight) + 0.05);
    }
}
