using System;
using System.Globalization;

namespace TBDMovies.UI.Services;

/// <summary>Search display text without losing the catalog's canonical title.</summary>
internal static class MediaTitleSearch
{
    public static bool Matches(string title, string? canonicalTitle, int year, string query) =>
        title.Contains(query, StringComparison.CurrentCultureIgnoreCase)
        || (canonicalTitle?.Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false)
        || year.ToString(CultureInfo.InvariantCulture).Contains(query, StringComparison.Ordinal);
}
