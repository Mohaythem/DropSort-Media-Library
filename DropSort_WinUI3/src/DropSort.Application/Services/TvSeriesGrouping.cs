using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DropSort.Domain.Media.Discovery;
using DropSort.Domain.Media.Parser;

namespace DropSort.Application.Services;

/// <summary>One discovered file with the episode number derived for it inside a probable-series group.</summary>
public sealed record TvSeriesEpisodeGuess(DiscoveredMedia Item, int Episode);

/// <summary>
/// A group of movie-classified files that share a base title and differ only by a trailing episode number,
/// so they are probably one TV series. It is a SUGGESTION, never an automatic reclassification.
/// </summary>
public sealed record TvSeriesSuggestion(string BaseTitle, IReadOnlyList<TvSeriesEpisodeGuess> Episodes)
{
    public int FileCount => Episodes.Count;
}

/// <summary>
/// Detects probable TV series among discovered files and reclassifies files between movie and TV episode.
/// <para>
/// The parser already classifies "الحلقة N" / "Episode N" names as episodes; this catches the remaining
/// case the goal describes: four or more files that share one base title and differ only by a trailing
/// number ("الرجل العناب 1..30"). Because it can be wrong for a numbered movie franchise, it only produces
/// a suggestion the user confirms — it never forces a classification.
/// </para>
/// </summary>
public static class TvSeriesGrouping
{
    /// <summary>The smallest group that is offered as a probable series.</summary>
    public const int MinGroupSize = 4;

    // The base title, then an optional episode word, then a trailing 1-3 digit number that is NOT part of a
    // longer number (so a year like 2049 is never split). Arabic-Indic digits are folded before matching.
    private static readonly Regex TrailingEpisode = new(
        @"^(?<base>.*?)[\s._\-:]*(?:الحلقة|حلقة|episode|ep)?[\s._\-:]*(?<!\d)(?<num>\d{1,3})$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static IReadOnlyList<TvSeriesSuggestion> Detect(IReadOnlyList<DiscoveredMedia> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var groups = new Dictionary<string, List<TvSeriesEpisodeGuess>>(StringComparer.Ordinal);
        var display = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (item.Classification != DiscoveryClassification.MovieCandidate)
            {
                continue;
            }

            var title = item.ParsedMedia?.Title;
            if (string.IsNullOrWhiteSpace(title) || !TryDeriveEpisode(title!, out var baseTitle, out var episode))
            {
                continue;
            }

            var key = NormalizeKey(baseTitle);
            if (key.Length == 0)
            {
                continue;
            }

            if (!groups.TryGetValue(key, out var list))
            {
                list = [];
                groups[key] = list;
                display[key] = baseTitle;
            }

            list.Add(new TvSeriesEpisodeGuess(item, episode));
        }

        var result = new List<TvSeriesSuggestion>();
        foreach (var (key, list) in groups)
        {
            // Enough members AND enough distinct episode numbers to read as a run of episodes, not repeats.
            if (list.Count < MinGroupSize || list.Select(guess => guess.Episode).Distinct().Count() < MinGroupSize)
            {
                continue;
            }

            result.Add(new TvSeriesSuggestion(
                display[key],
                [.. list.OrderBy(guess => guess.Episode)]));
        }

        return result;
    }

    /// <summary>
    /// Derives the base title and trailing episode number from a title such as "الرجل العناب 3" or
    /// "Show 12". A title with no clean trailing 1-3 digit number (or nothing left as a base) does not
    /// qualify. Arabic-Indic digits are folded to ASCII first.
    /// </summary>
    public static bool TryDeriveEpisode(string title, out string baseTitle, out int episode)
    {
        baseTitle = string.Empty;
        episode = 0;
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        var match = TrailingEpisode.Match(FoldDigits(title.Trim()));
        if (!match.Success)
        {
            return false;
        }

        var derivedBase = match.Groups["base"].Value.Trim(' ', '.', '_', '-', ':');
        if (derivedBase.Length == 0)
        {
            return false;
        }

        if (!int.TryParse(match.Groups["num"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out episode)
            || episode is < 0 or > 999)
        {
            return false;
        }

        baseTitle = derivedBase;
        return true;
    }

    /// <summary>Reclassifies a discovered file as a TV episode of the given show, season and episode.</summary>
    public static DiscoveredMedia AsTvEpisode(DiscoveredMedia item, string showTitle, int season, int episode)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (string.IsNullOrWhiteSpace(showTitle))
        {
            throw new ArgumentException("A TV episode needs a show title.", nameof(showTitle));
        }

        var parsed = item.ParsedMedia
            ?? throw new ArgumentException("The item has no parsed media to reclassify.", nameof(item));
        var episodeParsed = new ParsedMedia(
            parsed.OriginalName,
            MediaType.TvEpisode,
            showTitle.Trim(),
            null,
            parsed.Resolution,
            parsed.Source,
            parsed.Codec,
            parsed.Extension,
            season,
            episode);
        return new DiscoveredMedia(item.Path, item.FileSize, episodeParsed, DiscoveryClassification.TvEpisodeCandidate, null);
    }

    /// <summary>Reclassifies a discovered file back to a movie, dropping any season/episode.</summary>
    public static DiscoveredMedia AsMovie(DiscoveredMedia item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var parsed = item.ParsedMedia
            ?? throw new ArgumentException("The item has no parsed media to reclassify.", nameof(item));
        var title = string.IsNullOrWhiteSpace(parsed.Title)
            ? System.IO.Path.GetFileNameWithoutExtension(item.Path)
            : parsed.Title!;
        var movieParsed = new ParsedMedia(
            parsed.OriginalName,
            MediaType.Movie,
            title,
            parsed.Year,
            parsed.Resolution,
            parsed.Source,
            parsed.Codec,
            parsed.Extension);
        return new DiscoveredMedia(item.Path, item.FileSize, movieParsed, DiscoveryClassification.MovieCandidate, null);
    }

    private static string NormalizeKey(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }

    private static string FoldDigits(string value)
    {
        var chars = value.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            if (c is >= '٠' and <= '٩')
            {
                chars[i] = (char)('0' + (c - '٠'));
            }
            else if (c is >= '۰' and <= '۹')
            {
                chars[i] = (char)('0' + (c - '۰'));
            }
        }

        return new string(chars);
    }
}

