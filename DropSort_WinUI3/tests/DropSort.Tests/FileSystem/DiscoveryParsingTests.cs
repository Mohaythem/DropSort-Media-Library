using System;
using System.IO;
using System.Linq;
using DropSort.Domain.Media.Discovery;
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

    private DiscoveredMedia DiscoverOne(string fileName)
    {
        File.WriteAllText(Path.Combine(_root, fileName), "payload");
        return new MediaDiscoveryService().Discover(_root, recursive: false).Single();
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
    public void An_episode_file_is_classified_as_tv_and_skipped()
    {
        var item = DiscoverOne("Breaking Bad S01E01 1080p.mkv");

        Assert.Equal(DiscoveryClassification.TvEpisodeSkipped, item.Classification);
        Assert.Null(item.ParsedMedia!.Title);
    }
}
