using System.Collections.Generic;
using System.IO;
using System.Linq;
using TBDMovies.Application.Services;
using TBDMovies.Domain.Media.Discovery;
using TBDMovies.Domain.Media.Parser;
using Xunit;

namespace TBDMovies.Tests.Application;

/// <summary>
/// Grouping movie-classified files into a probable TV series and reclassifying files between movie and TV.
/// The detection is a suggestion (never forced), so the contract is: 4+ files sharing a base title with
/// distinct trailing episode numbers are grouped, a 4-digit year is never split into an episode, and the
/// reclassification helpers produce valid episode/movie items.
/// </summary>
public sealed class TvSeriesGroupingTests
{
    private static DiscoveredMedia Movie(string title)
    {
        var path = Path.Combine(Path.GetTempPath(), title.Replace(' ', '.') + ".mkv");
        var parsed = new ParsedMedia(Path.GetFileName(path), MediaType.Movie, title, null, "1080p", null, null, ".mkv");
        return new DiscoveredMedia(path, 100, parsed, DiscoveryClassification.MovieCandidate, null);
    }

    [Fact]
    public void Four_files_sharing_a_base_title_with_trailing_numbers_are_a_probable_series()
    {
        var items = new List<DiscoveredMedia>
        {
            Movie("الرجل العناب 1"),
            Movie("الرجل العناب 2"),
            Movie("الرجل العناب 3"),
            Movie("الرجل العناب 4"),
        };

        var suggestions = TvSeriesGrouping.Detect(items);

        var suggestion = Assert.Single(suggestions);
        Assert.Equal("الرجل العناب", suggestion.BaseTitle);
        Assert.Equal(4, suggestion.FileCount);
        Assert.Equal(new[] { 1, 2, 3, 4 }, suggestion.Episodes.Select(e => e.Episode));
    }

    [Fact]
    public void Fewer_than_four_files_are_not_grouped()
    {
        var items = new List<DiscoveredMedia>
        {
            Movie("Some Show 1"),
            Movie("Some Show 2"),
            Movie("Some Show 3"),
        };

        Assert.Empty(TvSeriesGrouping.Detect(items));
    }

    [Fact]
    public void A_four_digit_year_is_never_read_as_an_episode_number()
    {
        // Four unrelated year-suffixed movies must not be split ("2049" is not a 1-3 digit episode).
        var items = new List<DiscoveredMedia>
        {
            Movie("City 2049"),
            Movie("City 2050"),
            Movie("City 2051"),
            Movie("City 2052"),
        };

        Assert.Empty(TvSeriesGrouping.Detect(items));
    }

    [Fact]
    public void Arabic_indic_trailing_number_is_derived()
    {
        Assert.True(TvSeriesGrouping.TryDeriveEpisode("الرجل العناب ٣", out var baseTitle, out var episode));
        Assert.Equal("الرجل العناب", baseTitle);
        Assert.Equal(3, episode);
    }

    [Fact]
    public void Reclassifying_a_movie_as_a_tv_episode_yields_a_valid_episode_candidate()
    {
        var movie = Movie("الرجل العناب 5");

        var episode = TvSeriesGrouping.AsTvEpisode(movie, "الرجل العناب", season: 1, episode: 5);

        Assert.Equal(DiscoveryClassification.TvEpisodeCandidate, episode.Classification);
        Assert.Equal(MediaType.TvEpisode, episode.ParsedMedia!.MediaType);
        Assert.Equal("الرجل العناب", episode.ParsedMedia!.Title);
        Assert.Equal(1, episode.ParsedMedia!.SeasonNumber);
        Assert.Equal(5, episode.ParsedMedia!.EpisodeNumber);
        Assert.Equal(movie.Path, episode.Path);
        Assert.Equal("1080p", episode.ParsedMedia!.Resolution); // verified facts are preserved

        var backToMovie = TvSeriesGrouping.AsMovie(episode);
        Assert.Equal(DiscoveryClassification.MovieCandidate, backToMovie.Classification);
        Assert.Equal(MediaType.Movie, backToMovie.ParsedMedia!.MediaType);
        Assert.Null(backToMovie.ParsedMedia!.SeasonNumber);
    }
}
