using System;
using System.Collections.Immutable;
using DropSort.Domain.Library.Movies;
using Xunit;

namespace DropSort.Tests.Domain.Library.Movies;

public class CatalogModelsTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    private MovieCatalogData CreateMovieData(
        string? provider = "tmdb",
        string? externalId = "155",
        string title = "The Dark Knight",
        string? originalTitle = "The Dark Knight",
        int? year = 2008,
        string? overview = "Batman faces the Joker.",
        string[]? genres = null,
        int? runtimeMinutes = 152,
        double? rating = 8.5,
        string? posterReference = "/poster.jpg",
        MetadataStatus status = MetadataStatus.Ready)
    {
        return new MovieCatalogData(
            provider,
            externalId,
            title,
            originalTitle,
            year,
            overview,
            genres == null ? ImmutableArray.Create("Drama", "Action") : ImmutableArray.Create(genres),
            runtimeMinutes,
            rating,
            posterReference,
            status);
    }

    [Fact]
    public void MovieCatalogData_PreservesOptionalMetadataAndGenres()
    {
        var data = CreateMovieData(
            originalTitle: null,
            year: null,
            overview: null,
            genres: Array.Empty<string>(),
            runtimeMinutes: null,
            rating: null,
            posterReference: null);

        Assert.Null(data.Year);
        Assert.Null(data.Overview);
        Assert.Empty(data.Genres);
        Assert.Null(data.RuntimeMinutes);
        Assert.Null(data.Rating);
    }

    [Theory]
    [InlineData("provider", "")]
    [InlineData("externalId", " ")]
    [InlineData("title", "")]
    [InlineData("year", 0)]
    [InlineData("runtimeMinutes", 0)]
    [InlineData("rating", 11.0)]
    [InlineData("genres", "")]
    public void MovieCatalogData_RejectsInvalidValues(string field, object value)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
        {
            CreateMovieData(
                provider: field == "provider" ? (string)value : "tmdb",
                externalId: field == "externalId" ? (string)value : "155",
                title: field == "title" ? (string)value : "The Dark Knight",
                year: field == "year" ? (int)value : 2008,
                runtimeMinutes: field == "runtimeMinutes" ? (int)value : 152,
                rating: field == "rating" ? (double)value : 8.5,
                genres: field == "genres" ? new[] { "Drama", (string)value } : null
            );
        });
    }

    [Fact]
    public void Movie_ExposesFlatCatalogProperties()
    {
        var movie = new Movie(1, CreateMovieData(), Now, Now, Now);

        Assert.Equal("The Dark Knight", movie.OriginalTitle);
        Assert.Equal(2008, movie.Year);
        Assert.Equal("Batman faces the Joker.", movie.Overview);
        Assert.Equal(new[] { "Drama", "Action" }, movie.Genres);
        Assert.Equal(152, movie.RuntimeMinutes);
        Assert.Equal(8.5, movie.Rating);
        Assert.Equal("/poster.jpg", movie.PosterReference);
    }

    [Fact]
    public void Movie_RejectsInvalidDataAndId()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Movie(0, CreateMovieData(), Now, Now, Now));
        Assert.Throws<ArgumentNullException>(() => new Movie(1, null!, Now, Now, Now));
    }

    [Fact]
    public void VerifiedFileFacts_RequireAbsolutePath_And_PreserveTechnicalData()
    {
        var path = System.IO.Path.GetFullPath("Movie.mkv");
        var facts = new VerifiedMediaFileFacts(
            currentPath: path,
            fileSize: 123,
            extension: ".mkv",
            resolution: "1080p",
            codec: "x264",
            source: "BluRay",
            observedAt: Now);

        Assert.Equal(path, facts.CurrentPath);
        Assert.Equal(123, facts.FileSize);
        Assert.Equal(".mkv", facts.Extension);
    }

    [Theory]
    [InlineData("currentPath", "relative.mkv")]
    [InlineData("fileSize", -1L)]
    [InlineData("extension", "mkv")]
    public void VerifiedFileFacts_RejectInvalidValues(string field, object value)
    {
        var path = System.IO.Path.GetFullPath("Movie.mkv");
        Assert.ThrowsAny<ArgumentException>(() =>
        {
            new VerifiedMediaFileFacts(
                currentPath: field == "currentPath" ? (string)value : path,
                fileSize: field == "fileSize" ? (long)value : 123,
                extension: field == "extension" ? (string)value : ".mkv",
                resolution: null,
                codec: null,
                source: null,
                observedAt: Now);
        });
    }

    [Fact]
    public void MediaFile_KeepsLogicalAssociationSeparateFromPhysicalFacts()
    {
        var path = System.IO.Path.GetFullPath("Movie.mkv");
        var facts = new VerifiedMediaFileFacts(path, 123, ".mkv", "1080p", "x264", "BluRay", Now);
        
        var mediaFile = new MediaFile(
            id: 2,
            movieId: 1,
            currentPath: facts.CurrentPath,
            fileSize: facts.FileSize,
            extension: facts.Extension,
            resolution: facts.Resolution,
            codec: facts.Codec,
            source: facts.Source,
            status: MediaFileStatus.Present,
            discoveredAt: Now,
            lastSeenAt: Now);

        Assert.Equal(1, mediaFile.MovieId);
        Assert.Equal(MediaFileStatus.Present, mediaFile.Status);
    }

    [Fact]
    public void MediaFile_AcceptsLegacyOptionalExtension()
    {
        var path = System.IO.Path.GetFullPath("legacy");
        var mediaFile = new MediaFile(
            id: 1,
            movieId: null,
            currentPath: path,
            fileSize: 0,
            extension: null,
            resolution: null,
            codec: null,
            source: null,
            status: MediaFileStatus.Missing,
            discoveredAt: Now,
            lastSeenAt: Now);

        Assert.Null(mediaFile.Extension);
        Assert.Null(mediaFile.MovieId);
    }

    [Theory]
    [InlineData("id", 0)]
    [InlineData("movieId", 0)]
    [InlineData("currentPath", "relative.mkv")]
    [InlineData("fileSize", -1L)]
    [InlineData("extension", "mkv")]
    [InlineData("status", 999)]
    public void MediaFile_RejectsInvalidCatalogValues(string field, object value)
    {
        var path = System.IO.Path.GetFullPath("Movie.mkv");
        Assert.ThrowsAny<ArgumentException>(() =>
        {
            new MediaFile(
                id: field == "id" ? (int)value : 1,
                movieId: field == "movieId" ? (int)value : 1,
                currentPath: field == "currentPath" ? (string)value : path,
                fileSize: field == "fileSize" ? (long)value : 1,
                extension: field == "extension" ? (string)value : ".mkv",
                resolution: null,
                codec: null,
                source: null,
                status: field == "status" ? (MediaFileStatus)(int)value : MediaFileStatus.Present,
                discoveredAt: Now,
                lastSeenAt: Now);
        });
    }
}
