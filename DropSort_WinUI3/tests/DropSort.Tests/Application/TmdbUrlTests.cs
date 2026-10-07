using DropSort.Application.Metadata;
using Xunit;

namespace DropSort.Tests.Application;

/// <summary>
/// Parsing pasted TMDB links into a media type and bare numeric id. This is the whole basis of the manual
/// "Paste TMDB URL" match, so both the accepted shapes and the clean rejections are a contract.
/// </summary>
public sealed class TmdbUrlTests
{
    [Theory]
    [InlineData("https://www.themoviedb.org/movie/438631-dune", "438631")]
    [InlineData("https://www.themoviedb.org/movie/438631", "438631")]
    [InlineData("http://themoviedb.org/movie/27205-inception", "27205")]
    [InlineData("themoviedb.org/movie/155-the-dark-knight", "155")]
    [InlineData("https://www.themoviedb.org/movie/438631-dune/watch?locale=US", "438631")]
    public void Movie_links_parse_to_the_numeric_id(string url, string expectedId)
    {
        Assert.True(TmdbUrl.TryParse(url, out var kind, out var id));
        Assert.Equal(TmdbUrlKind.Movie, kind);
        Assert.Equal(expectedId, id);
    }

    [Theory]
    [InlineData("https://www.themoviedb.org/tv/1399-game-of-thrones", "1399")]
    [InlineData("https://www.themoviedb.org/tv/1399", "1399")]
    [InlineData("themoviedb.org/tv/1396/season/1", "1396")]
    public void Tv_links_parse_to_the_numeric_id(string url, string expectedId)
    {
        Assert.True(TmdbUrl.TryParse(url, out var kind, out var id));
        Assert.Equal(TmdbUrlKind.Tv, kind);
        Assert.Equal(expectedId, id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("https://example.com/movie/123")]              // wrong host
    [InlineData("https://www.themoviedb.org/person/123")]      // not movie/tv
    [InlineData("https://www.themoviedb.org/movie/")]          // no id
    [InlineData("https://www.themoviedb.org/movie/dune")]      // non-numeric id
    [InlineData("ftp://www.themoviedb.org/movie/123")]         // wrong scheme
    [InlineData("https://www.themoviedb.org/")]                // no path
    public void Invalid_or_unsupported_links_are_rejected(string url)
    {
        Assert.False(TmdbUrl.TryParse(url, out _, out _));
    }
}
