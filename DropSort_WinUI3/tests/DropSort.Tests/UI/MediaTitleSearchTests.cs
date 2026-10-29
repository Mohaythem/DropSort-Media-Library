using DropSort.UI.Services;
using Xunit;

namespace DropSort.Tests.UI;

public sealed class MediaTitleSearchTests
{
    [Theory]
    [InlineData("Batman 2", "The Dark Knight", 2008, "dark knight", true)]
    [InlineData("Batman 2", "The Dark Knight", 2008, "BATMAN", true)]
    [InlineData("Batman 2", "The Dark Knight", 2008, "2008", true)]
    [InlineData("Batman 2", "The Dark Knight", 2008, "Interstellar", false)]
    [InlineData("Breaking Bad", null, 2008, "breaking", true)]
    [InlineData("الكيميائي", "Breaking Bad", 2008, "Breaking", true)]
    [InlineData("الكيميائي", "Breaking Bad", 2008, "الكيميائي", true)]
    public void MatchesDisplayCanonicalAndYear(string title, string? canonical, int year, string query, bool expected) =>
        Assert.Equal(expected, MediaTitleSearch.Matches(title, canonical, year, query));
}
