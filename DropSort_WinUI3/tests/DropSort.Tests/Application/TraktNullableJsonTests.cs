using System;
using System.Text.Json;
using DropSort.Application.Trakt;
using Xunit;

namespace DropSort.Tests.Application;

public sealed class TraktNullableJsonTests
{
    [Fact]
    public void Explicit_null_ids_and_row_numbers_remain_visible_without_a_fake_match()
    {
        using var raw = JsonDocument.Parse("""[{"id":null,"rank":null,"type":"movie","movie":{"title":"Unknown IDs","year":null,"ids":{"trakt":null,"tmdb":null,"tvdb":null,"imdb":null}}}]""");
        var items = TraktDataProjections.ParseWatchlist(raw.RootElement);
        var rows = new TraktProjectionService().ProjectWatchlist(items, new TraktLocalSnapshot([], DateTimeOffset.UtcNow));
        var row = Assert.Single(rows);
        Assert.Equal("Unknown IDs", row.DisplayTitle);
        Assert.Equal(TraktMatchStatus.NoMatch, row.MatchStatus);
        Assert.Null(row.MatchedLocalId);
        Assert.Equal(JsonValueKind.Null, row.RawElement.GetProperty("id").ValueKind);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"unknown\"")]
    [InlineData("true")]
    public void Optional_numeric_fields_preserve_rows_and_valid_provider_evidence(string unknown)
    {
        using var payload = JsonDocument.Parse("""
        [{"id":1,"rank":1,"type":"movie","plays":2,
          "movie":{"title":"Remote movie","year":__UNKNOWN__,"ids":{"trakt":10,"tmdb":__UNKNOWN__,"tvdb":__UNKNOWN__,"imdb":null}}},
         {"id":2,"rank":2,"type":"movie","plays":1,
          "movie":{"title":"Known movie","year":2020,"ids":{"trakt":20,"tmdb":200,"tvdb":null,"imdb":null}}}]
        """.Replace("__UNKNOWN__", unknown));
        var movies = TraktDataProjections.ParseWatchedMovies(payload.RootElement);
        Assert.Equal(2, movies.Count);
        Assert.Null(movies[0].TmdbId);
        Assert.Null(movies[0].Year);
        Assert.Equal(200, movies[1].TmdbId);
        var context = TraktWatchedContext.Create(movies: movies,
            history: TraktDataProjections.ParseHistory(payload.RootElement));
        Assert.True(context.GetMovieWatchStatus(new TraktIdentity(TraktMediaKind.Movie, TraktId: 10)).IsWatched);
        var projection = new TraktProjectionService();
        var snapshot = new TraktLocalSnapshot([], DateTimeOffset.UtcNow);
        var watchlist = projection.ProjectWatchlist(TraktDataProjections.ParseWatchlist(payload.RootElement), snapshot, context);
        var list = projection.ProjectListItems(TraktDataProjections.ParseListItems(payload.RootElement), snapshot, context);
        Assert.Equal(2, watchlist.Count);
        Assert.Equal(2, list.Count);
        Assert.Equal("Remote movie", watchlist[0].DisplayTitle);
        Assert.Equal(unknown, watchlist[0].RawElement.GetProperty("movie").GetProperty("ids").GetProperty("tmdb").GetRawText());
    }

    [Fact]
    public void Null_show_episode_and_history_ids_do_not_abort_the_resource()
    {
        using var shows = JsonDocument.Parse("""[{"plays":1,"show":{"year":null,"ids":{"trakt":30,"tmdb":null,"tvdb":null}},"seasons":[{"number":1,"episodes":[{"number":1,"plays":1}]}]}]""");
        using var history = JsonDocument.Parse("""[{"id":1,"type":"episode","episode":{"season":1,"number":1,"ids":{"trakt":null,"tmdb":null,"tvdb":null}},"show":{"ids":{"trakt":30,"tmdb":null,"tvdb":null}}}]""");
        var parsed = TraktDataProjections.ParseWatchedShows(shows.RootElement);
        var events = TraktDataProjections.ParseHistory(history.RootElement);
        var context = TraktWatchedContext.Create(shows: parsed, history: events);
        Assert.True(context.GetEpisodeWatchStatus(new TraktIdentity(TraktMediaKind.Episode,
            ShowTraktId: 30, SeasonNumber: 1, EpisodeNumber: 1)).IsWatched);
        Assert.Single(new TraktProjectionService().ProjectHistory(events, new TraktLocalSnapshot([], DateTimeOffset.UtcNow)));
    }
}
