using System;
using System.IO;
using System.Linq;
using DropSort.Domain.Media.Discovery;
using DropSort.Domain.Media.Parser;
using DropSort.FileSystem.Discovery;
using Xunit;

namespace DropSort.Tests.FileSystem;

/// <summary>
/// Honest TV classification. A file whose name clearly names a TV episode must not be silently
/// registered as a movie. Ordinary single-episode forms (including underscore separators) resolve as
/// episode candidates; multi-episode and range forms the single-episode path cannot represent are
/// surfaced for review (TvEpisodeUnsupported), never dropped into MovieCandidate. Genuine movies stay
/// movies.
/// </summary>
public sealed class TvClassificationTrustTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dropsort-tvclass-" + Guid.NewGuid().ToString("N"));

    public TvClassificationTrustTests() => Directory.CreateDirectory(_root);

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

    [Theory]
    [InlineData("Severance.S01E02.1080p.mkv")]      // dot separators
    [InlineData("Severance_S01E02_1080p.mkv")]      // underscore separators (previously misread as a movie)
    [InlineData("Severance S01E02.mkv")]            // spaces
    [InlineData("Show.1x02.720p.mkv")]              // 1x02 form
    [InlineData("Show.1x01-1080p.mkv")]             // single 1x01 followed by a resolution tag, not a range
    [InlineData("Show.S00E01.mkv")]                 // a special (season 0) is still a single episode
    public void Single_episode_forms_are_episode_candidates(string fileName)
    {
        var item = DiscoverOne(fileName);
        Assert.Equal(DiscoveryClassification.TvEpisodeCandidate, item.Classification);
        Assert.Equal(MediaType.TvEpisode, item.ParsedMedia!.MediaType);
        Assert.NotNull(item.ParsedMedia.SeasonNumber);
        Assert.NotNull(item.ParsedMedia.EpisodeNumber);
        Assert.False(string.IsNullOrWhiteSpace(item.ParsedMedia.Title));
    }

    [Fact]
    public void Underscore_single_episode_reads_show_title_and_numbers()
    {
        var item = DiscoverOne("Severance_S01E02_1080p.mkv");
        Assert.Equal("Severance", item.ParsedMedia!.Title);
        Assert.Equal(1, item.ParsedMedia.SeasonNumber);
        Assert.Equal(2, item.ParsedMedia.EpisodeNumber);
    }

    [Theory]
    [InlineData("Show.S01E01E02.1080p.mkv")]        // concatenated multi-episode
    [InlineData("Show.S01E01-E03.mkv")]             // dash range with E
    [InlineData("Show.S01E01-S01E02.mkv")]          // full SxxExx range
    [InlineData("Show.S01E01_S01E02.mkv")]          // two full markers, underscore separator
    [InlineData("Show.S01E01.S01E02.mkv")]          // two full markers, dot separator
    [InlineData("Show.S01E01-03.mkv")]              // numeric range
    [InlineData("Show.1x01x02.mkv")]                // x-form multi-episode
    public void Multi_episode_and_range_forms_are_surfaced_for_review_not_movies(string fileName)
    {
        var item = DiscoverOne(fileName);
        Assert.Equal(DiscoveryClassification.TvEpisodeUnsupported, item.Classification);
        Assert.NotEqual(DiscoveryClassification.MovieCandidate, item.Classification);
    }

    [Theory]
    [InlineData("Dune Part Two (2024) 1080p BluRay x265.mkv")]
    [InlineData("Blade Runner 2049 (2017) 2160p.mkv")]
    public void Genuine_movies_stay_movies(string fileName)
    {
        var item = DiscoverOne(fileName);
        Assert.Equal(DiscoveryClassification.MovieCandidate, item.Classification);
    }
}
