using System;
using System.Linq;

namespace DropSort.Application.Metadata;

/// <summary>The media type a TMDB URL points at.</summary>
public enum TmdbUrlKind
{
    Movie,
    Tv,
}

/// <summary>
/// Parses a pasted TMDB web URL into a media type and the bare numeric TMDB id used as the external id.
/// This is a deliberate, closed parser for themoviedb.org movie/tv links only — never an arbitrary web
/// fetch. A user-supplied URL is an explicit manual identity, so no scoring or confidence is involved;
/// the caller applies it directly through the normal match path.
/// </summary>
public static class TmdbUrl
{
    /// <summary>
    /// Extracts the media type and TMDB id from a link such as
    /// <c>https://www.themoviedb.org/movie/438631-dune</c> or <c>https://www.themoviedb.org/tv/1399</c>.
    /// Accepts links with or without a scheme, with or without <c>www.</c>, with a trailing slug or extra
    /// path/query, and returns false for any other host, any non movie/tv path, or a missing id.
    /// </summary>
    public static bool TryParse(string? input, out TmdbUrlKind kind, out string externalId)
    {
        kind = default;
        externalId = string.Empty;

        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var text = input.Trim();
        if (!text.Contains("://", StringComparison.Ordinal))
        {
            // A bare "themoviedb.org/movie/1" is still a valid link the user can paste.
            text = "https://" + text;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme is not ("http" or "https"))
        {
            return false;
        }

        var host = uri.Host.ToLowerInvariant();
        if (host is not ("themoviedb.org" or "www.themoviedb.org"))
        {
            return false;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
        {
            return false;
        }

        switch (segments[0].ToLowerInvariant())
        {
            case "movie":
                kind = TmdbUrlKind.Movie;
                break;
            case "tv":
                kind = TmdbUrlKind.Tv;
                break;
            default:
                return false;
        }

        // The id is the leading digits of the second segment; a "-slug" suffix (or none) is dropped.
        var digits = new string(segments[1].TakeWhile(char.IsDigit).ToArray());
        if (digits.Length == 0)
        {
            return false;
        }

        externalId = digits;
        return true;
    }
}
