using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using TBDMovies.Domain.Library.Movies;

namespace TBDMovies.Application.Services;

/// <summary>Suggests editable leaf names from confirmed catalog metadata. Never performs file I/O.</summary>
public static class MovieOrganizationNaming
{
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static bool HasTrustworthyMetadata(Movie movie) => movie.MetadataStatus == MetadataStatus.Ready
        && !movie.ReviewMatch && !string.IsNullOrWhiteSpace(movie.Provider) && !string.IsNullOrWhiteSpace(movie.ExternalId)
        && !string.IsNullOrWhiteSpace(movie.Title) && movie.Year is >= 1888 and <= 2100;

    public static string SuggestBaseName(string? title, int? year, bool hasTrustworthyMetadata, string currentFilename)
    {
        var fallback = Path.GetFileNameWithoutExtension(currentFilename);
        if (!hasTrustworthyMetadata || string.IsNullOrWhiteSpace(title) || year is not (>= 1888 and <= 2100)) return fallback;
        var text = new StringBuilder();
        foreach (var character in title)
        {
            if (character < 32) continue;
            if (character is ':' or '/' or '\\') text.Append(" - ");
            else if (!"<>\"|?*".Contains(character)) text.Append(character);
        }
        var clean = Regex.Replace(text.ToString(), @"\s+", " ").Trim(' ', '.');
        if (!clean.Any(char.IsLetterOrDigit) || Reserved.Contains(clean.Split('.', 2)[0].TrimEnd(' ', '.'))) return fallback;
        var candidate = clean + " (" + year.Value.ToString(CultureInfo.InvariantCulture) + ")";
        return candidate.Length + Path.GetExtension(currentFilename).Length <= 255 ? candidate : fallback;
    }
}
