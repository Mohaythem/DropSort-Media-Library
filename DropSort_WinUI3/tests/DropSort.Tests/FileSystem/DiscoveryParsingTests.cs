using System;
using System.IO;
using System.Linq;
using DropSort.Domain.Media.Discovery;
using DropSort.Domain.Media.Parser;
using DropSort.FileSystem.Discovery;
using Xunit;

namespace DropSort.Tests.FileSystem;

/// <summary>
/// Discovery is what Add Media shows in its review list, so the facts it parses out of a file name are
/// a product contract: the title the user reads, the year, and the Quality column.
/// </summary>
public sealed class DiscoveryParsingTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "dropsort-discovery-" + Guid.NewGuid().ToString("N"));

    public DiscoveryParsingTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>
    /// Writes one file into its own directory and discovers it, so a test that needs several names does
    /// not have to reason about which result belongs to which file.
    /// </summary>
    private DiscoveredMedia DiscoverOne(string fileName)
    {
        var directory = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, fileName), "payload");
        return new MediaDiscoveryService().Discover(directory, recursive: false).Single();
    }

    [Fact]
    public void Bracketed_year_is_not_left_on_the_title()
    {
        var item = DiscoverOne("Dune Part Two (2024) 1080p BluRay x265.mkv");

        Assert.Equal(DiscoveryClassification.MovieCandidate, item.Classification);
        Assert.Equal("Dune Part Two", item.ParsedMedia!.Title);
        Assert.Equal(2024, item.ParsedMedia.Year);
    }

    [Fact]
    public void Release_tags_become_the_files_verified_facts()
    {
        var item = DiscoverOne("Oppenheimer (2023) 2160p WEB-DL x265.mkv");

        Assert.Equal("2160p", item.ParsedMedia!.Resolution);
        Assert.Equal("WEB-DL", item.ParsedMedia.Source);
        Assert.Equal("x265", item.ParsedMedia.Codec);
    }

    [Fact]
    public void A_name_without_tags_reports_no_facts_instead_of_guessing()
    {
        var item = DiscoverOne("Past Lives (2023).mp4");

        Assert.Equal("Past Lives", item.ParsedMedia!.Title);
        Assert.Null(item.ParsedMedia.Resolution);
        Assert.Null(item.ParsedMedia.Source);
        Assert.Null(item.ParsedMedia.Codec);
    }

    [Fact]
    public void An_episode_file_is_resolved_into_a_show_season_and_episode()
    {
        var item = DiscoverOne("Breaking Bad S01E01 1080p.mkv");

        Assert.Equal(DiscoveryClassification.TvEpisodeCandidate, item.Classification);
        Assert.Equal(MediaType.TvEpisode, item.ParsedMedia!.MediaType);
        Assert.Equal("Breaking Bad", item.ParsedMedia!.Title);
        Assert.Equal(1, item.ParsedMedia!.SeasonNumber);
        Assert.Equal(1, item.ParsedMedia!.EpisodeNumber);
        Assert.Equal("1080p", item.ParsedMedia!.Resolution);
    }

    [Fact]
    public void The_dotted_and_the_alternate_episode_forms_are_both_read()
    {
        var dotted = DiscoverOne("Better.Call.Saul.S02E07.1080p.WEB-DL.x265.mkv");
        var alternate = DiscoverOne("Severance 1x04 720p.mkv");

        Assert.Equal(DiscoveryClassification.TvEpisodeCandidate, dotted.Classification);
        Assert.Equal("Better Call Saul", dotted.ParsedMedia!.Title);
        Assert.Equal(2, dotted.ParsedMedia!.SeasonNumber);
        Assert.Equal(7, dotted.ParsedMedia!.EpisodeNumber);

        Assert.Equal(DiscoveryClassification.TvEpisodeCandidate, alternate.Classification);
        Assert.Equal("Severance", alternate.ParsedMedia!.Title);
        Assert.Equal(1, alternate.ParsedMedia!.SeasonNumber);
        Assert.Equal(4, alternate.ParsedMedia!.EpisodeNumber);
    }

    [Fact]
    public void An_episode_with_no_show_name_before_the_marker_stays_unresolved()
    {
        var item = DiscoverOne("S03E09.mkv");

        Assert.Equal(DiscoveryClassification.TvEpisodeSkipped, item.Classification);
        Assert.Equal(MediaType.TvEpisode, item.ParsedMedia!.MediaType);
        Assert.Null(item.ParsedMedia!.Title);
    }

    /// <summary>
    /// A resolution is not an episode marker. "1920x1080" must stay a movie, or every remux in a library
    /// would be filed as season 1920.
    /// </summary>
    [Fact]
    public void A_resolution_is_never_read_as_an_episode_marker()
    {
        var item = DiscoverOne("Some Movie 2019 1920x1080 x264.mkv");

        Assert.Equal(DiscoveryClassification.MovieCandidate, item.Classification);
        Assert.Equal(MediaType.Movie, item.ParsedMedia!.MediaType);
        Assert.Null(item.ParsedMedia!.SeasonNumber);
    }
}
