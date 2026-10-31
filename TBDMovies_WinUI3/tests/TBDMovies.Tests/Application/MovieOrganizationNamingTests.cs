using TBDMovies.Application.Services;
using TBDMovies.Domain.Library.Movies;
using Xunit;

namespace TBDMovies.Tests.Application;

public sealed class MovieOrganizationNamingTests
{
    [Theory]
    [InlineData("Fight Club", 1999, true, "Fight Club (1999)")]
    [InlineData("المصيدة", 2021, true, "المصيدة (2021)")]
    [InlineData("Star Wars: A New Hope", 1977, true, "Star Wars - A New Hope (1977)")]
    [InlineData("Fight Club", 1999, false, "original.release")]
    [InlineData("Fight Club", null, true, "original.release")]
    [InlineData("Fight Club", 1800, true, "original.release")]
    [InlineData("???:::***", 2000, true, "original.release")]
    [InlineData("CON", 2000, true, "original.release")]
    public void Suggestion_is_conservative_and_keeps_original_extension(string title, int? year, bool trusted, string expected)
    {
        const string file = "original.release.MKV";
        var name = MovieOrganizationNaming.SuggestBaseName(title, year, trusted, file);
        Assert.Equal(expected, name);
        Assert.Equal(".MKV", Path.GetExtension(name + Path.GetExtension(file)));
    }

    [Fact]
    public void A_title_cannot_be_interpreted_as_a_path_and_oversize_suggestions_fall_back()
    {
        var name = MovieOrganizationNaming.SuggestBaseName(@"../../film\secret", 2020, true, "original.mkv");
        Assert.DoesNotContain('/', name);
        Assert.DoesNotContain('\\', name);
        Assert.False(Path.IsPathRooted(name));
        Assert.Equal("original", MovieOrganizationNaming.SuggestBaseName(new string('A', 260), 2020, true, "original.mkv"));
    }

    [Theory]
    [InlineData(MetadataStatus.Ready, false, true)]
    [InlineData(MetadataStatus.Ready, true, false)]
    [InlineData(MetadataStatus.NeedsMatch, false, false)]
    [InlineData(MetadataStatus.Pending, false, false)]
    [InlineData(MetadataStatus.Failed, false, false)]
    public void Metadata_gate_rejects_uncertain_or_review_matches(MetadataStatus status, bool review, bool expected)
    {
        var now = DateTimeOffset.UtcNow;
        var data = new MovieCatalogData("tmdb", "550", "Fight Club", null, 1999, null, [], null, null, null, status);
        var movie = new Movie(1, data, now, now, now, review);
        Assert.Equal(expected, MovieOrganizationNaming.HasTrustworthyMetadata(movie));
    }

    [Fact]
    public void Local_filename_metadata_never_becomes_a_trusted_suggestion()
    {
        var now = DateTimeOffset.UtcNow;
        var data = new MovieCatalogData(null, null, "Fight Club", null, 1999, null, [], null, null, null, MetadataStatus.Pending);
        Assert.False(MovieOrganizationNaming.HasTrustworthyMetadata(new Movie(1, data, now, now, now)));
    }
}
