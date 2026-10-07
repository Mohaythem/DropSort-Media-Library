using System;
using System.IO;
using System.Linq;
using DropSort.Domain.Media.Discovery;
using DropSort.Domain.Media.Parser;
using DropSort.FileSystem.Discovery;
using Xunit;

namespace DropSort.Tests.FileSystem;

/// <summary>
/// Word-form and Arabic episode markers. A file named with "Episode N", "Ep N" or the Arabic "الحلقة N"
/// (ASCII or Arabic-Indic digits) is a TV episode, not a movie, and is read as a flat single-season show
/// (season 1) so it never lands in a fake Specials/Season 0. Roman-numeral "Episode IV" stays a movie.
/// </summary>
public sealed class WordEpisodeClassificationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dropsort-wordep-" + Guid.NewGuid().ToString("N"));

    public WordEpisodeClassificationTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { }
    }

    private DiscoveredMedia DiscoverOne(string fileName)
    {
        var directory = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, fileName), "payload");
        return new MediaDiscoveryService().Discover(directory, recursive: false).Single();
    }

    [Fact]
    public void Arabic_episode_marker_is_a_tv_episode_not_a_movie()
    {
        var item = DiscoverOne("الرجل العناب الحلقة 1.mkv");

        Assert.Equal(DiscoveryClassification.TvEpisodeCandidate, item.Classification);
        Assert.Equal(MediaType.TvEpisode, item.ParsedMedia!.MediaType);
        Assert.Equal("الرجل العناب", item.ParsedMedia!.Title);
        Assert.Equal(1, item.ParsedMedia!.SeasonNumber); // flat show -> season 1, never a fake Specials (0)
        Assert.Equal(1, item.ParsedMedia!.EpisodeNumber);
    }

    [Fact]
    public void Arabic_indic_digits_are_read_as_the_episode_number()
    {
        var item = DiscoverOne("الرجل العناب الحلقة ٢.mkv");

        Assert.Equal(DiscoveryClassification.TvEpisodeCandidate, item.Classification);
        Assert.Equal(2, item.ParsedMedia!.EpisodeNumber);
        Assert.Equal(1, item.ParsedMedia!.SeasonNumber);
    }

    [Theory]
    [InlineData("Show Name Episode 3.mkv", "Show Name", 3)]
    [InlineData("Show.Name.Ep.4.1080p.mkv", "Show Name", 4)]
    [InlineData("Some Show Episode 12.mkv", "Some Show", 12)]
    public void English_word_episode_markers_are_tv_episodes(string fileName, string title, int episode)
    {
        var item = DiscoverOne(fileName);

        Assert.Equal(DiscoveryClassification.TvEpisodeCandidate, item.Classification);
        Assert.Equal(title, item.ParsedMedia!.Title);
        Assert.Equal(1, item.ParsedMedia!.SeasonNumber);
        Assert.Equal(episode, item.ParsedMedia!.EpisodeNumber);
    }

    [Fact]
    public void Roman_numeral_episode_in_a_movie_title_stays_a_movie()
    {
        var item = DiscoverOne("Star Wars Episode IV A New Hope (1977) 1080p.mkv");

        Assert.Equal(DiscoveryClassification.MovieCandidate, item.Classification);
        Assert.Equal(MediaType.Movie, item.ParsedMedia!.MediaType);
    }

    [Fact]
    public void Word_episode_with_no_show_name_stays_unresolved_not_a_movie()
    {
        var item = DiscoverOne("Episode 1.mkv");

        Assert.Equal(DiscoveryClassification.TvEpisodeSkipped, item.Classification);
        Assert.Equal(MediaType.TvEpisode, item.ParsedMedia!.MediaType);
    }

    [Theory]
    [InlineData("Dune Part Two (2024) 1080p BluRay x265.mkv")]
    [InlineData("Blade Runner 2049 (2017) 2160p.mkv")]
    [InlineData("Escape Plan (2013) 1080p.mkv")]
    public void Genuine_movies_are_not_misread_as_word_episodes(string fileName)
    {
        var item = DiscoverOne(fileName);

        Assert.Equal(DiscoveryClassification.MovieCandidate, item.Classification);
        Assert.Equal(MediaType.Movie, item.ParsedMedia!.MediaType);
    }
}
