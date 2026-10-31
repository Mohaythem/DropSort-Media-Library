using TBDMovies.Application.Trakt;
using Xunit;

namespace TBDMovies.Tests.Application;

public sealed class TraktMatcherTests
{
    [Fact]
    public void Matches_exact_provider_ids()
    {
        var remote = new TraktIdentity(TraktMediaKind.Movie, TmdbId: 42, ImdbId: "tt0000042");
        var candidate = Candidate(7, TraktMediaKind.Movie, new TraktIdentity(TraktMediaKind.Movie, TmdbId: 42), TraktAvailability.Available);

        var result = TraktMatcher.Match(remote, [candidate]);

        Assert.Equal(TraktMatchStatus.Matched, result.Status);
        Assert.Equal(7, result.Candidate!.LocalId);
        Assert.Equal(TraktAvailability.Available, result.Candidate.Availability);
    }

    [Fact]
    public void Rejects_wrong_kind_absent_id_and_conflicting_id()
    {
        var remote = new TraktIdentity(TraktMediaKind.Movie, TmdbId: 42);
        var candidates = new[]
        {
            Candidate(1, TraktMediaKind.Show, new TraktIdentity(TraktMediaKind.Show, TmdbId: 42)),
            Candidate(2, TraktMediaKind.Movie, new TraktIdentity(TraktMediaKind.Movie)),
            Candidate(3, TraktMediaKind.Movie, new TraktIdentity(TraktMediaKind.Movie, TmdbId: 43, ImdbId: "tt0000042"))
        };

        Assert.Equal(TraktMatchStatus.NoMatch, TraktMatcher.Match(remote, candidates).Status);
    }

    [Fact]
    public void Rejects_ambiguous_exact_matches()
    {
        var remote = new TraktIdentity(TraktMediaKind.Show, TraktId: 9);
        var candidates = new[]
        {
            Candidate(1, TraktMediaKind.Show, new TraktIdentity(TraktMediaKind.Show, TraktId: 9)),
            Candidate(2, TraktMediaKind.Show, new TraktIdentity(TraktMediaKind.Show, TraktId: 9))
        };

        Assert.Equal(TraktMatchStatus.Ambiguous, TraktMatcher.Match(remote, candidates).Status);
    }

    [Fact]
    public void Matches_season_and_episode_by_parent_and_coordinates()
    {
        var season = new TraktIdentity(TraktMediaKind.Season, ShowTmdbId: 100, SeasonNumber: 2);
        var episode = new TraktIdentity(TraktMediaKind.Episode, ShowTmdbId: 100, SeasonNumber: 2, EpisodeNumber: 3);
        var candidates = new[]
        {
            Candidate(11, TraktMediaKind.Season, new TraktIdentity(TraktMediaKind.Season, ShowTmdbId: 100, SeasonNumber: 2), TraktAvailability.Partial),
            Candidate(12, TraktMediaKind.Episode, new TraktIdentity(TraktMediaKind.Episode, ShowTmdbId: 100, SeasonNumber: 2, EpisodeNumber: 3), TraktAvailability.Missing)
        };

        Assert.Equal(11, TraktMatcher.Match(season, candidates).Candidate!.LocalId);
        var episodeResult = TraktMatcher.Match(episode, candidates);
        Assert.Equal(12, episodeResult.Candidate!.LocalId);
        Assert.Equal(TraktAvailability.Missing, episodeResult.Candidate.Availability);
    }

    [Fact]
    public void Rejects_invalid_ids_and_incomplete_or_conflicting_coordinates()
    {
        var invalid = new[]
        {
            Candidate(1, TraktMediaKind.Movie, new TraktIdentity(TraktMediaKind.Movie, TmdbId: 0)),
            Candidate(2, TraktMediaKind.Episode, new TraktIdentity(TraktMediaKind.Episode, ShowTmdbId: 10, SeasonNumber: 1)),
            Candidate(3, TraktMediaKind.Episode, new TraktIdentity(TraktMediaKind.Episode, ShowTmdbId: 10, SeasonNumber: 1, EpisodeNumber: 0))
        };

        Assert.Equal(TraktMatchStatus.NoMatch,
            TraktMatcher.Match(new TraktIdentity(TraktMediaKind.Movie, TmdbId: 0), invalid).Status);
        Assert.Equal(TraktMatchStatus.NoMatch,
            TraktMatcher.Match(new TraktIdentity(TraktMediaKind.Episode, ShowTmdbId: 10, SeasonNumber: 1, EpisodeNumber: 2), invalid).Status);
    }

    [Fact]
    public void Rejects_direct_and_fallback_id_conflicts()
    {
        var directRemote = new TraktIdentity(TraktMediaKind.Episode, TmdbId: 20, ImdbId: "tt0000001", ShowTmdbId: 10, SeasonNumber: 1, EpisodeNumber: 2);
        var directConflict = Candidate(1, TraktMediaKind.Episode, new TraktIdentity(TraktMediaKind.Episode, TmdbId: 20, ImdbId: "tt0000002", ShowTmdbId: 10, SeasonNumber: 1, EpisodeNumber: 2));
        Assert.Equal(TraktMatchStatus.NoMatch, TraktMatcher.Match(directRemote, [directConflict]).Status);

        var fallbackRemote = new TraktIdentity(TraktMediaKind.Episode, TmdbId: 30, ShowTmdbId: 10, SeasonNumber: 1, EpisodeNumber: 2);
        var fallbackConflict = Candidate(2, TraktMediaKind.Episode, new TraktIdentity(TraktMediaKind.Episode, TmdbId: 31, ShowTmdbId: 10, SeasonNumber: 1, EpisodeNumber: 2));
        Assert.Equal(TraktMatchStatus.NoMatch, TraktMatcher.Match(fallbackRemote, [fallbackConflict]).Status);
    }

    [Theory]
    [InlineData("trakt")]
    [InlineData("tmdb")]
    [InlineData("imdb")]
    [InlineData("tvdb")]
    public void Matches_each_supported_provider_without_inventing_availability(string provider)
    {
        var identity = provider switch
        {
            "trakt" => new TraktIdentity(TraktMediaKind.Movie, TraktId: 15),
            "tmdb" => new TraktIdentity(TraktMediaKind.Movie, TmdbId: 15),
            "imdb" => new TraktIdentity(TraktMediaKind.Movie, ImdbId: "tt0000015"),
            _ => new TraktIdentity(TraktMediaKind.Movie, TvdbId: 15)
        };

        var result = TraktMatcher.Match(identity, [Candidate(1, identity.Kind, identity)]);

        Assert.Equal(TraktMatchStatus.Matched, result.Status);
        Assert.Equal(TraktAvailability.Unknown, result.Candidate!.Availability);
    }

    [Fact]
    public void Direct_episode_id_does_not_override_conflicting_parent_or_coordinates()
    {
        var remote = new TraktIdentity(TraktMediaKind.Episode, TmdbId: 15,
            ShowTmdbId: 100, SeasonNumber: 1, EpisodeNumber: 2);
        Assert.Equal(TraktMatchStatus.NoMatch, TraktMatcher.Match(remote,
            [Candidate(1, remote.Kind, remote with { ShowTmdbId = 101 })]).Status);
        Assert.Equal(TraktMatchStatus.NoMatch, TraktMatcher.Match(remote,
            [Candidate(1, remote.Kind, remote with { EpisodeNumber = 3 })]).Status);
    }

    [Fact]
    public void Rejects_invalid_enum_local_key_and_inconsistent_candidate_kind()
    {
        var remote = new TraktIdentity(TraktMediaKind.Season, ShowTmdbId: 100, SeasonNumber: 0);
        Assert.Equal(TraktMatchStatus.NoMatch, TraktMatcher.Match(remote,
            [Candidate(0, remote.Kind, remote)]).Status);
        Assert.Equal(TraktMatchStatus.NoMatch, TraktMatcher.Match(remote,
            [Candidate(1, remote.Kind, remote with { Kind = TraktMediaKind.Episode, EpisodeNumber = 1 })]).Status);
        var invalid = new TraktIdentity((TraktMediaKind)999, TraktId: 1);
        Assert.Equal(TraktMatchStatus.NoMatch, TraktMatcher.Match(invalid,
            [Candidate(1, invalid.Kind, invalid)]).Status);
        Assert.Equal(TraktMatchStatus.Matched, TraktMatcher.Match(remote,
            [Candidate(1, remote.Kind, remote)]).Status);
    }

    [Theory]
    [InlineData("N/A")]
    [InlineData("null")]
    [InlineData("0")]
    [InlineData("tt0000000")]
    [InlineData("tt123x567")]
    public void Rejects_malformed_imdb_identity_and_parent_identity(string id)
    {
        var movie = new TraktIdentity(TraktMediaKind.Movie, ImdbId: id);
        var episode = new TraktIdentity(TraktMediaKind.Episode, ShowImdbId: id, SeasonNumber: 1, EpisodeNumber: 1);
        Assert.Equal(TraktMatchStatus.NoMatch, TraktMatcher.Match(movie, [Candidate(1, movie.Kind, movie)]).Status);
        Assert.Equal(TraktMatchStatus.NoMatch, TraktMatcher.Match(episode, [Candidate(1, episode.Kind, episode)]).Status);
    }

    [Fact]
    public void Trims_imdb_ids_and_collapses_identical_candidate_duplicates()
    {
        var remote = new TraktIdentity(TraktMediaKind.Movie, ImdbId: "tt0000042");
        var candidate = Candidate(1, remote.Kind, remote with { ImdbId = " TT0000042 " });
        Assert.Equal(TraktMatchStatus.Matched, TraktMatcher.Match(remote, [candidate, candidate]).Status);
        Assert.False((remote with { SeasonNumber = 1 }).IsValid);
    }

    private static TraktLocalCandidate Candidate(int id, TraktMediaKind kind, TraktIdentity identity, TraktAvailability availability = TraktAvailability.Unknown) =>
        new(id, kind, identity, availability);
}
