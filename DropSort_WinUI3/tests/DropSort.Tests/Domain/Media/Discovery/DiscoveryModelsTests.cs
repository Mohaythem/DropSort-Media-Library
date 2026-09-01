using System;
using DropSort.Domain.Media.Discovery;
using DropSort.Domain.Media.Parser;
using Xunit;

namespace DropSort.Tests.Domain.Media.Discovery;

public class DiscoveryModelsTests
{
    private ParsedMedia CreateParsed(MediaType mediaType = MediaType.Movie)
    {
        return new ParsedMedia(
            originalName: "Movie.2024.mkv",
            mediaType: mediaType,
            title: mediaType == MediaType.Movie ? "Movie" : null,
            year: mediaType == MediaType.Movie ? 2024 : null,
            resolution: null,
            source: null,
            codec: null,
            extension: ".mkv");
    }

    [Fact]
    public void MovieDiscoveryItem_IsCoherent()
    {
        var path = System.IO.Path.GetFullPath("Movie.mkv");
        var item = new DiscoveredMedia(
            path: path,
            fileSize: 100,
            parsedMedia: CreateParsed(),
            classification: DiscoveryClassification.MovieCandidate,
            issue: null);

        Assert.Equal(100, item.FileSize);
        Assert.Equal(MediaType.Movie, item.ParsedMedia!.MediaType);
    }

    [Fact]
    public void ErrorItem_RequiresIssueAndHasNoFileFacts()
    {
        var path = System.IO.Path.GetFullPath("blocked");
        var issue = new DiscoveryIssue(DiscoveryErrorCode.PermissionDenied, "not readable");
        var item = DiscoveredMedia.Error(path, issue);

        Assert.Equal(DiscoveryClassification.Error, item.Classification);
        Assert.Null(item.FileSize);
        Assert.Null(item.ParsedMedia);
    }

    [Fact]
    public void IncoherentDiscoveryModels_AreRejected()
    {
        var path = System.IO.Path.GetFullPath("Movie.mkv");
        
        // Relative path
        Assert.Throws<ArgumentException>(() => new DiscoveredMedia("Movie.mkv", 1, CreateParsed(), DiscoveryClassification.MovieCandidate, null));
        
        // Invalid classification
        Assert.Throws<ArgumentException>(() => new DiscoveredMedia(path, 1, CreateParsed(), (DiscoveryClassification)999, null));
        
        // Error without issue
        Assert.Throws<ArgumentException>(() => new DiscoveredMedia(path, null, null, DiscoveryClassification.Error, null));
        
        // Error with file facts
        Assert.Throws<ArgumentException>(() => new DiscoveredMedia(path, 1, CreateParsed(), DiscoveryClassification.Error, new DiscoveryIssue(DiscoveryErrorCode.StatFailed, "failed")));
        
        // Non-error with issue
        Assert.Throws<ArgumentException>(() => new DiscoveredMedia(path, 1, CreateParsed(), DiscoveryClassification.MovieCandidate, new DiscoveryIssue(DiscoveryErrorCode.StatFailed, "failed")));
        
        // Non-error missing filesize
        Assert.Throws<ArgumentException>(() => new DiscoveredMedia(path, null, CreateParsed(), DiscoveryClassification.MovieCandidate, null));
        
        // Non-error missing parsed_media
        Assert.Throws<ArgumentException>(() => new DiscoveredMedia(path, 1, null, DiscoveryClassification.MovieCandidate, null));
        
        // MovieCandidate with TV_EPISODE media type
        Assert.Throws<ArgumentException>(() => new DiscoveredMedia(path, 10, CreateParsed(MediaType.TvEpisode), DiscoveryClassification.MovieCandidate, null));
    }

    [Fact]
    public void DiscoveryIssue_RequiresControlledCodeAndMessage()
    {
        Assert.Throws<ArgumentException>(() => new DiscoveryIssue((DiscoveryErrorCode)999, "message"));
        Assert.Throws<ArgumentException>(() => new DiscoveryIssue(DiscoveryErrorCode.StatFailed, ""));
    }
}
