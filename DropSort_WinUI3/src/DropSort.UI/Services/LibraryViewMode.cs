namespace DropSort.UI.Services;

public enum LibraryViewMode
{
    Grid,
    Compact,
}

public static class LibraryViewModePreference
{
    public static LibraryViewMode Parse(string? value) =>
        string.Equals(value, "compact", StringComparison.OrdinalIgnoreCase)
            ? LibraryViewMode.Compact
            : LibraryViewMode.Grid;

    public static string Serialize(LibraryViewMode mode) =>
        mode == LibraryViewMode.Compact ? "compact" : "grid";
}

/// <summary>Pure sizing rules for the responsive horizontal library cards.</summary>
public static class LibraryHorizontalLayout
{
    public const double MinCardWidth = 320;
    public const double MaxCardWidth = 420;
    public const double InterCardGap = 12;
    public const double CardHeight = 184;
    public const double CellHeight = 196;
    public const double PosterWidth = 96;
    public const double PosterHeight = 144;
    public const double FallbackCellWidth = 332;
    public const double LibraryHorizontalFallbackCellWidth = FallbackCellWidth;

    public readonly record struct Metrics(double CardWidth, double CellWidth, int Columns);

    public static Metrics Calculate(double availableWidth)
    {
        var available = availableWidth;
        if (double.IsNaN(available) || double.IsInfinity(available) || available <= 0)
        {
            available = FallbackCellWidth;
        }

        var columns = Math.Max(1, (int)Math.Floor(available / (MinCardWidth + InterCardGap)));
        var cellWidth = Math.Min(MaxCardWidth + InterCardGap, available / columns);
        var cardWidth = Math.Max(0, cellWidth - InterCardGap);
        return new Metrics(cardWidth, cellWidth, columns);
    }

    public static double CalculateCardWidth(double availableWidth) => Calculate(availableWidth).CardWidth;
}

/// <summary>Library-only poster sizing. Container padding, focus borders and row spacing stay unchanged.</summary>
public static class LibraryPosterLayout
{
    public const double MinPosterWidth = 180;
    public const double MaxPosterWidth = 220;
    public const double AspectRatio = 1.5;
    public const double ContainerChrome = 12;
    public const double MinCellWidth = MinPosterWidth + ContainerChrome;
    public const double MaxCellWidth = MaxPosterWidth + ContainerChrome;
    public const double MovieExtraHeight = 76;
    public const double ShowExtraHeight = 94;

    public readonly record struct Metrics(double PosterWidth, double PosterHeight, double CellWidth,
        double MovieCellHeight, double ShowCellHeight, int Columns);

    public static Metrics Calculate(double availableWidth)
    {
        var available = double.IsFinite(availableWidth) && availableWidth > 0 ? availableWidth : MinCellWidth;
        var columns = Math.Max(1, (int)Math.Floor(available / MinCellWidth));
        var cellWidth = Math.Min(MaxCellWidth, available / columns);
        var posterWidth = Math.Max(0, cellWidth - ContainerChrome);
        var posterHeight = posterWidth * AspectRatio;
        return new Metrics(posterWidth, posterHeight, cellWidth,
            posterHeight + MovieExtraHeight, posterHeight + ShowExtraHeight, columns);
    }
}
