using System;
using System.Collections.Generic;
using System.Text.Json;
using DropSort.Application.Trakt;
using Xunit;

namespace DropSort.Tests.Application;

public sealed class TraktProjectionServiceTests
{
    private readonly ITraktProjectionService _service = new TraktProjectionService();

    [Fact]
    public void Learned_show_ids_apply_to_all_existing_watched_episodes()
    {
        using var shows = JsonDocument.Parse("""[{"plays":2,"show":{"ids":{"trakt":200}},"seasons":[{"number":1,"episodes":[{"number":1,"plays":1},{"number":2,"plays":1}]}]}]""");
        using var history = JsonDocument.Parse("""[{"id":1,"type":"episode","action":"watch","episode":{"season":1,"number":1,"ids":{"trakt":300}},"show":{"ids":{"trakt":200,"tmdb":2000}}}]""");
        var context = TraktWatchedContext.Create(shows: TraktDataProjections.ParseWatchedShows(shows.RootElement),
            history: TraktDataProjections.ParseHistory(history.RootElement));

        Assert.True(context.GetEpisodeWatchStatus(new TraktIdentity(TraktMediaKind.Episode,
            ShowTmdbId: 2000, SeasonNumber: 1, EpisodeNumber: 2)).IsWatched);
        Assert.False(context.GetEpisodeWatchStatus(new TraktIdentity(TraktMediaKind.Episode,
            ShowTraktId: 200, ShowTmdbId: 9999, SeasonNumber: 1, EpisodeNumber: 2)).IsWatched);
    }

    [Fact]
    public void Zero_play_records_do_not_create_watched_badges()
    {
        using var movies = JsonDocument.Parse("""[{"plays":0,"movie":{"ids":{"trakt":10}}}]""");
        using var shows = JsonDocument.Parse("""[{"plays":1,"show":{"ids":{"trakt":20}},"seasons":[{"number":1,"episodes":[{"number":1,"plays":0},{"number":2,"plays":1}]}]}]""");
        var context = TraktWatchedContext.Create(TraktDataProjections.ParseWatchedMovies(movies.RootElement),
            TraktDataProjections.ParseWatchedShows(shows.RootElement));
        Assert.False(context.GetMovieWatchStatus(new TraktIdentity(TraktMediaKind.Movie, TraktId: 10)).IsWatched);
        Assert.False(context.GetEpisodeWatchStatus(new TraktIdentity(TraktMediaKind.Episode,
            ShowTraktId: 20, SeasonNumber: 1, EpisodeNumber: 1)).IsWatched);
        Assert.Equal(1, context.GetShowWatchStatus(new TraktIdentity(TraktMediaKind.Show, TraktId: 20)).WatchedEpisodeCount);
    }

    [Fact]
    public void Retains_all_raw_json_rank_notes_and_unknown_or_person_or_missing_title_rows()
    {
        var rawJson = """
        [
            {
                "id": 1001,
                "rank": 1,
                "listed_at": "2026-01-01T12:00:00Z",
                "type": "person",
                "notes": "Favorite actor",
                "person": {
                    "name": "Bryan Cranston",
                    "ids": { "trakt": 297, "imdb": "nm0186505", "tmdb": 17419 }
                }
            },
            {
                "id": 1002,
                "rank": 2,
                "listed_at": "2026-01-02T12:00:00Z",
                "type": "unknown_future_type",
                "notes": "Custom item",
                "unknown_future_type": {
                    "custom_field": "val"
                }
            },
            {
                "id": 1003,
                "rank": 3,
                "listed_at": "2026-01-03T12:00:00Z",
                "type": "movie",
                "movie": {
                    "ids": { "trakt": 9999 }
                }
            }
        ]
        """;

        using var doc = JsonDocument.Parse(rawJson);
        var items = TraktDataProjections.ParseListItems(doc.RootElement);

        var snapshot = new TraktLocalSnapshot(Array.Empty<TraktLocalCandidate>(), DateTimeOffset.UtcNow);
        var projected = _service.ProjectListItems(items, snapshot);

        Assert.Equal(3, projected.Count);

        // 1. Person item
        var personRow = projected[0];
        Assert.Equal(1001, personRow.Id);
        Assert.Equal(1, personRow.Rank);
        Assert.Equal("person", personRow.Type);
        Assert.Equal("Bryan Cranston", personRow.Title);
        Assert.Equal("Bryan Cranston", personRow.DisplayTitle);
        Assert.Equal("Favorite actor", personRow.Notes);
        Assert.Equal(JsonValueKind.Object, personRow.RawElement.ValueKind);
        Assert.Null(personRow.RemoteIdentity);
        Assert.Equal(TraktMatchStatus.NoMatch, personRow.MatchStatus);
        Assert.Null(personRow.MatchedLocalKind);
        Assert.Null(personRow.MatchedLocalId);
        Assert.Equal(TraktAvailability.Missing, personRow.Availability);

        // 2. Unknown future type
        var unknownRow = projected[1];
        Assert.Equal(1002, unknownRow.Id);
        Assert.Equal(2, unknownRow.Rank);
        Assert.Equal("unknown_future_type", unknownRow.Type);
        Assert.Equal("Custom item", unknownRow.Notes);
        Assert.Null(unknownRow.RemoteIdentity);
        Assert.Equal(TraktMatchStatus.NoMatch, unknownRow.MatchStatus);
        Assert.Equal("[unknown_future_type #1002]", unknownRow.DisplayTitle);

        // 3. Missing-title movie row
        var missingTitleRow = projected[2];
        Assert.Equal(1003, missingTitleRow.Id);
        Assert.Equal(3, missingTitleRow.Rank);
        Assert.Equal("movie", missingTitleRow.Type);
        Assert.Null(missingTitleRow.Title);
        Assert.NotNull(missingTitleRow.RemoteIdentity);
        Assert.Equal(9999L, missingTitleRow.RemoteIdentity!.TraktId);
        Assert.Equal("[movie #9999]", missingTitleRow.DisplayTitle);
    }

    [Fact]
    public void Matches_local_movie_and_provides_matched_kind_and_id_for_details_navigation()
    {
        var rawJson = """
        [
            {
                "id": 501,
                "rank": 1,
                "type": "movie",
                "movie": {
                    "title": "Fight Club",
                    "year": 1999,
                    "ids": { "tmdb": 550, "imdb": "tt0137523", "trakt": 55 }
                }
            }
        ]
        """;

        using var doc = JsonDocument.Parse(rawJson);
        var items = TraktDataProjections.ParseListItems(doc.RootElement);

        var candidate = new TraktLocalCandidate(
            LocalId: 42,
            Kind: TraktMediaKind.Movie,
            Identity: new TraktIdentity(TraktMediaKind.Movie, TmdbId: 550),
            Availability: TraktAvailability.Available);

        var snapshot = new TraktLocalSnapshot(new[] { candidate }, DateTimeOffset.UtcNow);
        var projected = _service.ProjectListItems(items, snapshot);

        var row = Assert.Single(projected);
        Assert.Equal(TraktMatchStatus.Matched, row.MatchStatus);
        Assert.Equal(TraktMediaKind.Movie, row.MatchedLocalKind);
        Assert.Equal(42, row.MatchedLocalId);
        Assert.Equal(TraktAvailability.Available, row.Availability);
    }

    [Fact]
    public void Unmatched_item_provides_null_matched_kind_and_id_with_no_fake_paths()
    {
        var rawJson = """
        [
            {
                "id": 601,
                "rank": 1,
                "type": "movie",
                "movie": {
                    "title": "Unmatched Movie",
                    "year": 2024,
                    "ids": { "tmdb": 999999 }
                }
            }
        ]
        """;

        using var doc = JsonDocument.Parse(rawJson);
        var items = TraktDataProjections.ParseListItems(doc.RootElement);

        var snapshot = new TraktLocalSnapshot(Array.Empty<TraktLocalCandidate>(), DateTimeOffset.UtcNow);
        var projected = _service.ProjectListItems(items, snapshot);

        var row = Assert.Single(projected);
        Assert.Equal(TraktMatchStatus.NoMatch, row.MatchStatus);
        Assert.Null(row.MatchedLocalKind);
        Assert.Null(row.MatchedLocalId);
        Assert.Equal(TraktAvailability.Missing, row.Availability);
    }

    [Fact]
    public void Watched_badge_from_exact_remote_movie_history_and_watched_set()
    {
        var rawListJson = """
        [
            {
                "id": 701,
                "rank": 1,
                "type": "movie",
                "movie": {
                    "title": "Movie In Watched Set",
                    "ids": { "tmdb": 100 }
                }
            },
            {
                "id": 702,
                "rank": 2,
                "type": "movie",
                "movie": {
                    "title": "Unwatched Movie",
                    "ids": { "tmdb": 200 }
                }
            }
        ]
        """;

        var rawWatchedJson = """
        [
            {
                "plays": 3,
                "last_watched_at": "2026-02-15T20:00:00Z",
                "movie": {
                    "ids": { "tmdb": 100 }
                }
            }
        ]
        """;

        using var listDoc = JsonDocument.Parse(rawListJson);
        using var watchedDoc = JsonDocument.Parse(rawWatchedJson);

        var items = TraktDataProjections.ParseListItems(listDoc.RootElement);
        var watchedMovies = TraktDataProjections.ParseWatchedMovies(watchedDoc.RootElement);

        var watchedContext = TraktWatchedContext.Create(movies: watchedMovies);
        var snapshot = new TraktLocalSnapshot(Array.Empty<TraktLocalCandidate>(), DateTimeOffset.UtcNow);

        var projected = _service.ProjectListItems(items, snapshot, watchedContext);

        // Movie 1: in watched set
        Assert.True(projected[0].IsWatched);
        Assert.Equal(3, projected[0].PlayCount);
        Assert.NotNull(projected[0].LastWatchedAt);

        // Movie 2: not in watched set
        Assert.False(projected[1].IsWatched);
        Assert.Equal(0, projected[1].PlayCount);
        Assert.Null(projected[1].LastWatchedAt);
    }

    [Fact]
    public void Full_show_not_watched_just_because_one_episode_watched_reports_watched_episode_count()
    {
        var rawListJson = """
        [
            {
                "id": 801,
                "rank": 1,
                "type": "show",
                "show": {
                    "title": "Breaking Bad",
                    "ids": { "tmdb": 1396 }
                }
            }
        ]
        """;

        var rawWatchedShowsJson = """
        [
            {
                "plays": 2,
                "show": {
                    "ids": { "tmdb": 1396 }
                },
                "seasons": [
                    {
                        "number": 1,
                        "episodes": [
                            { "number": 1, "plays": 1 },
                            { "number": 2, "plays": 1 }
                        ]
                    }
                ]
            }
        ]
        """;

        using var listDoc = JsonDocument.Parse(rawListJson);
        using var watchedDoc = JsonDocument.Parse(rawWatchedShowsJson);

        var items = TraktDataProjections.ParseListItems(listDoc.RootElement);
        var watchedShows = TraktDataProjections.ParseWatchedShows(watchedDoc.RootElement);

        var watchedContext = TraktWatchedContext.Create(shows: watchedShows);
        var snapshot = new TraktLocalSnapshot(Array.Empty<TraktLocalCandidate>(), DateTimeOffset.UtcNow);

        var projected = _service.ProjectListItems(items, snapshot, watchedContext);

        var showRow = Assert.Single(projected);
        // Watched episode count reported accurately
        Assert.Equal(2, showRow.WatchedEpisodeCount);
        // Full show is NOT watched just because episodes were watched!
        Assert.False(showRow.IsWatched);
    }

    [Fact]
    public void History_projection_retains_repeated_event_ids_and_chronological_order()
    {
        var rawHistoryJson = """
        [
            {
                "id": 9001,
                "watched_at": "2026-03-01T10:00:00Z",
                "action": "watch",
                "type": "movie",
                "movie": {
                    "title": "The Matrix",
                    "ids": { "tmdb": 603 }
                }
            },
            {
                "id": 9002,
                "watched_at": "2026-03-02T10:00:00Z",
                "action": "watch",
                "type": "movie",
                "movie": {
                    "title": "The Matrix",
                    "ids": { "tmdb": 603 }
                }
            },
            {
                "id": 9002,
                "watched_at": "2026-03-02T10:00:00Z",
                "action": "watch",
                "type": "movie",
                "movie": {
                    "title": "The Matrix",
                    "ids": { "tmdb": 603 }
                }
            }
        ]
        """;

        using var doc = JsonDocument.Parse(rawHistoryJson);
        var historyItems = TraktDataProjections.ParseHistory(doc.RootElement);

        var candidate = new TraktLocalCandidate(
            LocalId: 77,
            Kind: TraktMediaKind.Movie,
            Identity: new TraktIdentity(TraktMediaKind.Movie, TmdbId: 603),
            Availability: TraktAvailability.Available);

        var snapshot = new TraktLocalSnapshot(new[] { candidate }, DateTimeOffset.UtcNow);
        var projected = _service.ProjectHistory(historyItems, snapshot);

        Assert.Equal(3, projected.Count);
        Assert.Equal(9001, projected[0].HistoryId);
        Assert.Equal(9002, projected[1].HistoryId);
        Assert.Equal(9002, projected[2].HistoryId); // Preserves repeated 64-bit history event IDs!

        Assert.All(projected, row =>
        {
            Assert.True(row.IsWatched);
            Assert.Equal(TraktMatchStatus.Matched, row.MatchStatus);
            Assert.Equal(TraktMediaKind.Movie, row.MatchedLocalKind);
            Assert.Equal(77, row.MatchedLocalId);
            Assert.Equal(TraktAvailability.Available, row.Availability);
        });
    }

    [Fact]
    public void Watched_show_availability_conservatively_partial_unless_full_remote_set_known_complete()
    {
        var rawWatchedShowsJson = """
        [
            {
                "plays": 1,
                "show": {
                    "title": "Show 1",
                    "ids": { "tmdb": 1000 }
                },
                "seasons": [
                    {
                        "number": 1,
                        "episodes": [
                            { "number": 1, "plays": 1 },
                            { "number": 2, "plays": 1 }
                        ]
                    }
                ]
            }
        ]
        """;

        using var doc = JsonDocument.Parse(rawWatchedShowsJson);
        var watchedShows = TraktDataProjections.ParseWatchedShows(doc.RootElement);

        // Case 1: Only episode 1 is available -> Show is Partial
        var ep1Candidate = new TraktLocalCandidate(
            LocalId: 101,
            Kind: TraktMediaKind.Episode,
            Identity: new TraktIdentity(TraktMediaKind.Episode, ShowTmdbId: 1000, SeasonNumber: 1, EpisodeNumber: 1),
            Availability: TraktAvailability.Available);

        var snapshotPartial = new TraktLocalSnapshot(new[] { ep1Candidate }, DateTimeOffset.UtcNow);
        var projectedPartial = _service.ProjectWatchedShows(watchedShows, snapshotPartial);

        var showPartial = Assert.Single(projectedPartial);
        Assert.Equal(TraktAvailability.Partial, showPartial.Availability);

        // Case 2: Both remote episodes in watched subset are available -> Show is still conservatively Partial (full series remote episodes unknown!)
        var ep2Candidate = new TraktLocalCandidate(
            LocalId: 102,
            Kind: TraktMediaKind.Episode,
            Identity: new TraktIdentity(TraktMediaKind.Episode, ShowTmdbId: 1000, SeasonNumber: 1, EpisodeNumber: 2),
            Availability: TraktAvailability.Available);

        var snapshotComplete = new TraktLocalSnapshot(new[] { ep1Candidate, ep2Candidate }, DateTimeOffset.UtcNow);
        var projectedComplete = _service.ProjectWatchedShows(watchedShows, snapshotComplete);

        var showComplete = Assert.Single(projectedComplete);
        Assert.Equal(TraktAvailability.Partial, showComplete.Availability);

        // Case 3: Neither episode is available -> Unknown (unavailable is only Unknown, no file Missing for watched show subsets)
        var snapshotEmpty = new TraktLocalSnapshot(Array.Empty<TraktLocalCandidate>(), DateTimeOffset.UtcNow);
        var projectedEmpty = _service.ProjectWatchedShows(watchedShows, snapshotEmpty);

        var showEmpty = Assert.Single(projectedEmpty);
        Assert.Equal(TraktAvailability.Unknown, showEmpty.Availability);
    }

    [Fact]
    public void Snapshot_matching_uses_indexed_lookup_for_direct_ids_and_parent_coordinates()
    {
        var candidates = new List<TraktLocalCandidate>();

        // Generate 1000 candidates of mixed kinds
        for (int i = 1; i <= 200; i++)
        {
            candidates.Add(new TraktLocalCandidate(
                LocalId: i,
                Kind: TraktMediaKind.Movie,
                Identity: new TraktIdentity(TraktMediaKind.Movie, TmdbId: 10000 + i, ImdbId: $"tt{1000000 + i}"),
                Availability: TraktAvailability.Available));
        }

        for (int i = 1; i <= 50; i++)
        {
            candidates.Add(new TraktLocalCandidate(
                LocalId: 200 + i,
                Kind: TraktMediaKind.Show,
                Identity: new TraktIdentity(TraktMediaKind.Show, TmdbId: 20000 + i, TvdbId: 30000 + i),
                Availability: TraktAvailability.Available));

            // 10 episodes per show
            for (int ep = 1; ep <= 10; ep++)
            {
                candidates.Add(new TraktLocalCandidate(
                    LocalId: 1000 + (i * 10) + ep,
                    Kind: TraktMediaKind.Episode,
                    Identity: new TraktIdentity(
                        TraktMediaKind.Episode,
                        ShowTmdbId: 20000 + i,
                        SeasonNumber: 1,
                        EpisodeNumber: ep),
                    Availability: TraktAvailability.Available));
            }
        }

        var snapshot = new TraktLocalSnapshot(candidates, DateTimeOffset.UtcNow);

        // 1. Direct Movie match via TMDB
        var movieTmdbQuery = new TraktIdentity(TraktMediaKind.Movie, TmdbId: 10042);
        var movieMatch = snapshot.Match(movieTmdbQuery);
        Assert.Equal(TraktMatchStatus.Matched, movieMatch.Status);
        Assert.Equal(42, movieMatch.Candidate?.LocalId);

        // 2. Direct Movie match via IMDb
        var movieImdbQuery = new TraktIdentity(TraktMediaKind.Movie, ImdbId: "tt1000088");
        var movieImdbMatch = snapshot.Match(movieImdbQuery);
        Assert.Equal(TraktMatchStatus.Matched, movieImdbMatch.Status);
        Assert.Equal(88, movieImdbMatch.Candidate?.LocalId);

        // 3. Parent coordinate match for Episode
        var epQuery = new TraktIdentity(
            TraktMediaKind.Episode,
            ShowTmdbId: 20015,
            SeasonNumber: 1,
            EpisodeNumber: 7);
        var epMatch = snapshot.Match(epQuery);
        Assert.Equal(TraktMatchStatus.Matched, epMatch.Status);
        Assert.Equal(1000 + (15 * 10) + 7, epMatch.Candidate?.LocalId);

        // 4. Unknown query returns NoMatch without scanning entire candidate pool
        var unknownQuery = new TraktIdentity(TraktMediaKind.Movie, TmdbId: 999999);
        var unknownMatch = snapshot.Match(unknownQuery);
        Assert.Equal(TraktMatchStatus.NoMatch, unknownMatch.Status);
    }

    [Fact]
    public void Watched_context_strictly_differentiates_media_types_and_supports_season_zero_specials()
    {
        var rawWatchedShowsJson = """
        [
            {
                "plays": 1,
                "show": {
                    "ids": { "tmdb": 5000 }
                },
                "seasons": [
                    {
                        "number": 0,
                        "episodes": [
                            { "number": 1, "plays": 2, "last_watched_at": "2026-04-01T10:00:00Z" }
                        ]
                    }
                ]
            }
        ]
        """;

        using var showDoc = JsonDocument.Parse(rawWatchedShowsJson);
        var watchedShows = TraktDataProjections.ParseWatchedShows(showDoc.RootElement);

        var context = TraktWatchedContext.Create(shows: watchedShows);

        // 1. Season 0 special is recognized as watched
        var specialIdentity = new TraktIdentity(
            TraktMediaKind.Episode,
            ShowTmdbId: 5000,
            SeasonNumber: 0,
            EpisodeNumber: 1);

        var specialWatch = context.GetEpisodeWatchStatus(specialIdentity);
        Assert.True(specialWatch.IsWatched);

        // 2. Movie with the same TMDB ID (5000) is NOT marked watched (strict type separation)
        var movieIdentity = new TraktIdentity(TraktMediaKind.Movie, TmdbId: 5000);
        var movieWatch = context.GetMovieWatchStatus(movieIdentity);
        Assert.False(movieWatch.IsWatched);
    }

    [Fact]
    public void Title_null_entries_remain_representable_with_visible_fallback_display_title()
    {
        // 1. ListItem with null title
        var listItem = new TraktListItem(
            Id: 101,
            Rank: 1,
            ListedAt: DateTimeOffset.UtcNow,
            Type: "movie",
            Notes: null,
            MediaTraktId: 8888L,
            Title: null,
            Year: null,
            RawElement: default);

        var snapshot = new TraktLocalSnapshot(Array.Empty<TraktLocalCandidate>(), DateTimeOffset.UtcNow);
        var projectedList = _service.ProjectListItems(new[] { listItem }, snapshot);
        var listRow = Assert.Single(projectedList);
        Assert.Null(listRow.Title);
        Assert.Equal("[movie #8888]", listRow.DisplayTitle);

        // 2. WatchlistItem with null title and null MediaTraktId
        var watchItem = new TraktWatchlistItem(
            Id: 202,
            Rank: 2,
            ListedAt: DateTimeOffset.UtcNow,
            Type: "show",
            Notes: null,
            MediaTraktId: null,
            Title: null,
            Year: null,
            RawElement: default);

        var projectedWatch = _service.ProjectWatchlist(new[] { watchItem }, snapshot);
        var watchRow = Assert.Single(projectedWatch);
        Assert.Null(watchRow.Title);
        Assert.Equal("[show #202]", watchRow.DisplayTitle);

        // 3. HistoryItem episode with null title
        var historyItem = new TraktHistoryItem(
            Id: 303,
            WatchedAt: DateTimeOffset.UtcNow,
            Action: "watch",
            Type: "episode",
            MediaTraktId: null,
            Title: null,
            SeasonNumber: 2,
            EpisodeNumber: 5,
            RawElement: default);

        var projectedHistory = _service.ProjectHistory(new[] { historyItem }, snapshot);
        var historyRow = Assert.Single(projectedHistory);
        Assert.Null(historyRow.Title);
        Assert.Equal("[Episode S02E05]", historyRow.DisplayTitle);

        // 4. WatchedMovie with null title
        var watchedMovie = new TraktWatchedMovie(
            Plays: 1,
            LastWatchedAt: DateTimeOffset.UtcNow,
            LastUpdatedAt: null,
            TraktId: 404L,
            ImdbId: null,
            TmdbId: 505L,
            Title: null,
            Year: 2022,
            RawElement: default);

        var projectedMovies = _service.ProjectWatchedMovies(new[] { watchedMovie }, snapshot);
        var movieRow = Assert.Single(projectedMovies);
        Assert.Null(movieRow.Title);
        Assert.Equal("[Movie #404]", movieRow.DisplayTitle);

        // 5. WatchedShow with null title
        var watchedShow = new TraktWatchedShow(
            Plays: 1,
            LastWatchedAt: DateTimeOffset.UtcNow,
            LastUpdatedAt: null,
            ResetAt: null,
            TraktId: 606L,
            ImdbId: null,
            TmdbId: 707L,
            TvdbId: null,
            Title: null,
            Year: 2023,
            Seasons: Array.Empty<TraktWatchedSeason>(),
            RawElement: default);

        var projectedShows = _service.ProjectWatchedShows(new[] { watchedShow }, snapshot);
        var showRow = Assert.Single(projectedShows);
        Assert.Null(showRow.Title);
        Assert.Equal("[Show #606]", showRow.DisplayTitle);
    }

    [Fact]
    public void Contradictory_direct_provider_ids_reject_movie_and_show_watch_badges()
    {
        // Watched movie has TraktId: 10, TmdbId: 100
        using var movieDoc = JsonDocument.Parse("""[{"plays":1,"movie":{"ids":{"trakt":10,"tmdb":100}}}]""");
        // Watched show has TraktId: 20, TmdbId: 200
        using var showDoc = JsonDocument.Parse("""[{"plays":1,"show":{"ids":{"trakt":20,"tmdb":200}},"seasons":[{"number":1,"episodes":[{"number":1,"plays":1}]}]}]""");

        var context = TraktWatchedContext.Create(
            TraktDataProjections.ParseWatchedMovies(movieDoc.RootElement),
            TraktDataProjections.ParseWatchedShows(showDoc.RootElement));

        // Query movie has matching TMDB (100) but contradictory TraktId (99 vs 10)
        var conflictingMovie = new TraktIdentity(TraktMediaKind.Movie, TraktId: 99, TmdbId: 100);
        var movieStatus = context.GetMovieWatchStatus(conflictingMovie);
        Assert.False(movieStatus.IsWatched);
        Assert.Equal(0, movieStatus.Plays);

        // Query show has matching TMDB (200) but contradictory TraktId (99 vs 20)
        var conflictingShow = new TraktIdentity(TraktMediaKind.Show, TraktId: 99, TmdbId: 200);
        var showStatus = context.GetShowWatchStatus(conflictingShow);
        Assert.False(showStatus.IsFullyWatched);
        Assert.Equal(0, showStatus.WatchedEpisodeCount);
    }

    [Fact]
    public void Contradictory_parent_show_ids_reject_episode_watch_badges()
    {
        // Watched show has ShowTraktId: 10, ShowTmdbId: 500, S1E1 watched
        using var showDoc = JsonDocument.Parse("""
        [
            {
                "plays": 1,
                "show": { "ids": { "trakt": 10, "tmdb": 500 } },
                "seasons": [{ "number": 1, "episodes": [{ "number": 1, "plays": 2 }] }]
            }
        ]
        """);

        var context = TraktWatchedContext.Create(shows: TraktDataProjections.ParseWatchedShows(showDoc.RootElement));

        // Query episode shares TMDB (500) and coordinates (S1E1) but has contradictory ShowTraktId (99 vs 10)
        var conflictingEp = new TraktIdentity(
            TraktMediaKind.Episode,
            ShowTraktId: 99,
            ShowTmdbId: 500,
            SeasonNumber: 1,
            EpisodeNumber: 1);

        var epStatus = context.GetEpisodeWatchStatus(conflictingEp);
        Assert.False(epStatus.IsWatched);
        Assert.Equal(0, epStatus.Plays);
        Assert.Null(epStatus.LastWatchedAt);
    }

    [Fact]
    public void Ambiguous_duplicate_keys_reject_watch_badges()
    {
        // Two distinct movies share TmdbId: 550, but have different TraktIds (duplicate key in input)
        using var movieDoc = JsonDocument.Parse("""
        [
            { "plays": 1, "movie": { "ids": { "trakt": 1001, "tmdb": 550 } } },
            { "plays": 2, "movie": { "ids": { "trakt": 1002, "tmdb": 550 } } }
        ]
        """);

        var context = TraktWatchedContext.Create(movies: TraktDataProjections.ParseWatchedMovies(movieDoc.RootElement));

        // Query by TmdbId alone: matches both distinct records -> ambiguous!
        var ambiguousQuery = new TraktIdentity(TraktMediaKind.Movie, TmdbId: 550);
        var status = context.GetMovieWatchStatus(ambiguousQuery);
        Assert.False(status.IsWatched);
        Assert.Equal(0, status.Plays);
    }

    [Fact]
    public void Watched_episode_status_retains_actual_plays_and_timestamp()
    {
        var watchedAt = DateTimeOffset.Parse("2026-05-15T18:30:00Z");
        using var showDoc = JsonDocument.Parse($$"""
        [
            {
                "plays": 5,
                "show": { "ids": { "trakt": 300, "tmdb": 400 } },
                "seasons": [
                    {
                        "number": 2,
                        "episodes": [
                            {
                                "number": 4,
                                "plays": 7,
                                "last_watched_at": "2026-05-15T18:30:00Z"
                            }
                        ]
                    }
                ]
            }
        ]
        """);

        var context = TraktWatchedContext.Create(shows: TraktDataProjections.ParseWatchedShows(showDoc.RootElement));

        var epIdentity = new TraktIdentity(
            TraktMediaKind.Episode,
            ShowTmdbId: 400,
            SeasonNumber: 2,
            EpisodeNumber: 4);

        var status = context.GetEpisodeWatchStatus(epIdentity);
        Assert.True(status.IsWatched);
        Assert.Equal(7, status.Plays); // Actual plays retained!
        Assert.Equal(watchedAt, status.LastWatchedAt); // Actual timestamp retained!
    }

    [Fact]
    public void One_available_watched_episode_plus_unknown_other_series_episodes_is_partial_not_on_disk_full_show()
    {
        // Watched show payload contains only 1 episode (the only episode the user has watched)
        using var showDoc = JsonDocument.Parse("""
        [
            {
                "plays": 1,
                "show": { "title": "Large Series", "ids": { "tmdb": 8000 } },
                "seasons": [
                    {
                        "number": 1,
                        "episodes": [
                            { "number": 1, "plays": 1 }
                        ]
                    }
                ]
            }
        ]
        """);

        var watchedShows = TraktDataProjections.ParseWatchedShows(showDoc.RootElement);

        // Episode 1 is available on local disk
        var ep1Candidate = new TraktLocalCandidate(
            LocalId: 501,
            Kind: TraktMediaKind.Episode,
            Identity: new TraktIdentity(TraktMediaKind.Episode, ShowTmdbId: 8000, SeasonNumber: 1, EpisodeNumber: 1),
            Availability: TraktAvailability.Available);

        var snapshot = new TraktLocalSnapshot(new[] { ep1Candidate }, DateTimeOffset.UtcNow);
        var projected = _service.ProjectWatchedShows(watchedShows, snapshot);

        var showRow = Assert.Single(projected);
        // Having 1 available episode from watched subset must be Partial, NEVER Available (OnDisk full show)!
        Assert.Equal(TraktAvailability.Partial, showRow.Availability);
        // TotalEpisodeCount must be null (no fake total series denominator)
        Assert.Null(showRow.TotalEpisodeCount);
        Assert.Equal(1, showRow.WatchedEpisodeCount);
        Assert.False(showRow.IsWatched);
    }

    [Fact]
    public void Avoid_overcount_duplicate_watch_events_between_aggregate_and_history()
    {
        // Aggregate movies has Movie 50 with 3 plays
        using var moviesDoc = JsonDocument.Parse("""
        [
            {
                "plays": 3,
                "last_watched_at": "2026-01-10T12:00:00Z",
                "movie": { "ids": { "tmdb": 50 } }
            }
        ]
        """);

        // History has 2 watch events for Movie 50
        using var historyDoc = JsonDocument.Parse("""
        [
            {
                "id": 1,
                "watched_at": "2026-02-01T10:00:00Z",
                "action": "watch",
                "type": "movie",
                "movie": { "ids": { "tmdb": 50 } }
            },
            {
                "id": 2,
                "watched_at": "2026-02-02T10:00:00Z",
                "action": "watch",
                "type": "movie",
                "movie": { "ids": { "tmdb": 50 } }
            }
        ]
        """);

        var movies = TraktDataProjections.ParseWatchedMovies(moviesDoc.RootElement);
        var history = TraktDataProjections.ParseHistory(historyDoc.RootElement);

        var context = TraktWatchedContext.Create(movies: movies, history: history);

        var movieQuery = new TraktIdentity(TraktMediaKind.Movie, TmdbId: 50);
        var status = context.GetMovieWatchStatus(movieQuery);

        Assert.True(status.IsWatched);
        // Does NOT add 3 + 2 = 5 plays! Aggregate plays (3) is retained without overcount!
        Assert.Equal(3, status.Plays);
        // Latest watched timestamp from history is recognized
        Assert.Equal(DateTimeOffset.Parse("2026-02-02T10:00:00Z"), status.LastWatchedAt);
    }

    [Fact]
    public void Season_watch_status_reports_known_watched_count_while_complete_status_unknown()
    {
        using var showDoc = JsonDocument.Parse("""
        [
            {
                "plays": 3,
                "show": { "ids": { "tmdb": 900 } },
                "seasons": [
                    {
                        "number": 1,
                        "episodes": [
                            { "number": 1, "plays": 1 },
                            { "number": 2, "plays": 2 }
                        ]
                    }
                ]
            }
        ]
        """);

        var context = TraktWatchedContext.Create(shows: TraktDataProjections.ParseWatchedShows(showDoc.RootElement));

        using var listDoc = JsonDocument.Parse("""
        [
            {
                "id": 7001,
                "rank": 1,
                "type": "season",
                "season": {
                    "number": 1,
                    "ids": { "tmdb": 9901 }
                },
                "show": {
                    "ids": { "tmdb": 900 }
                }
            }
        ]
        """);

        var listItems = TraktDataProjections.ParseListItems(listDoc.RootElement);
        var snapshot = new TraktLocalSnapshot(Array.Empty<TraktLocalCandidate>(), DateTimeOffset.UtcNow);
        var projected = _service.ProjectListItems(listItems, snapshot, context);

        var seasonRow = Assert.Single(projected);
        Assert.Equal("season", seasonRow.Type);
        Assert.Equal(2, seasonRow.WatchedEpisodeCount); // 2 episodes watched in Season 1
        Assert.Null(seasonRow.TotalEpisodeCount); // Complete series total is unknown
        Assert.False(seasonRow.IsWatched); // Complete status unknown
        Assert.Equal(3, seasonRow.PlayCount); // Total plays across season episodes
    }

    [Fact]
    public void Zero_play_episodes_in_watched_shows_do_not_increment_watched_episode_count()
    {
        using var showDoc = JsonDocument.Parse("""
        [
            {
                "plays": 1,
                "show": { "title": "Test Show", "ids": { "tmdb": 3333 } },
                "seasons": [
                    {
                        "number": 1,
                        "episodes": [
                            { "number": 1, "plays": 0 },
                            { "number": 2, "plays": 1 }
                        ]
                    }
                ]
            }
        ]
        """);

        var watchedShows = TraktDataProjections.ParseWatchedShows(showDoc.RootElement);
        var snapshot = new TraktLocalSnapshot(Array.Empty<TraktLocalCandidate>(), DateTimeOffset.UtcNow);

        var projected = _service.ProjectWatchedShows(watchedShows, snapshot);
        var showRow = Assert.Single(projected);

        // Episode 1 has 0 plays, so WatchedEpisodeCount must be 1, NOT 2!
        Assert.Equal(1, showRow.WatchedEpisodeCount);
        Assert.Equal(TraktAvailability.Unknown, showRow.Availability); // Unavailable is Unknown, not Missing
    }

    [Fact]
    public void Discarding_richer_history_identity_allows_false_watched_match_on_conflicting_item()
    {
        using var moviesDoc = JsonDocument.Parse("""
        [
            {
                "plays": 1,
                "movie": { "ids": { "trakt": 100 } }
            }
        ]
        """);

        using var historyDoc = JsonDocument.Parse("""
        [
            {
                "id": 99991,
                "action": "watch",
                "type": "movie",
                "movie": { "ids": { "trakt": 100, "tmdb": 500 } }
            }
        ]
        """);

        var movies = TraktDataProjections.ParseWatchedMovies(moviesDoc.RootElement);
        var history = TraktDataProjections.ParseHistory(historyDoc.RootElement);

        var context = TraktWatchedContext.Create(movies: movies, history: history);

        var conflictingQuery = new TraktIdentity(TraktMediaKind.Movie, TraktId: 100, TmdbId: 999);
        var status = context.GetMovieWatchStatus(conflictingQuery);

        Assert.False(status.IsWatched);
    }

    [Fact]
    public void Discarding_richer_history_identity_prevents_matching_by_secondary_provider_id()
    {
        using var moviesDoc = JsonDocument.Parse("""
        [
            {
                "plays": 1,
                "movie": { "ids": { "trakt": 100 } }
            }
        ]
        """);

        using var historyDoc = JsonDocument.Parse("""
        [
            {
                "id": 99992,
                "action": "watch",
                "type": "movie",
                "movie": { "ids": { "trakt": 100, "tmdb": 500 } }
            }
        ]
        """);

        var movies = TraktDataProjections.ParseWatchedMovies(moviesDoc.RootElement);
        var history = TraktDataProjections.ParseHistory(historyDoc.RootElement);

        var context = TraktWatchedContext.Create(movies: movies, history: history);

        var localQuery = new TraktIdentity(TraktMediaKind.Movie, TmdbId: 500);
        var status = context.GetMovieWatchStatus(localQuery);

        Assert.True(status.IsWatched);
    }

    [Fact]
    public void Secondary_id_lookups_succeed_for_shows_and_episodes_after_history_enrichment()
    {
        // 1. Aggregate watched shows has Show with TraktId: 200 (TmdbId is null)
        // and episode S1E1 with ShowTraktId: 200, TraktId: 300 (TmdbId is null)
        using var showsDoc = JsonDocument.Parse("""
        [
            {
                "plays": 2,
                "show": { "ids": { "trakt": 200 } },
                "seasons": [
                    {
                        "number": 1,
                        "episodes": [
                            { "number": 1, "plays": 2 }
                        ]
                    }
                ]
            }
        ]
        """);

        // 2. History contains event for S1E1 with richer show & episode IDs
        using var historyDoc = JsonDocument.Parse("""
        [
            {
                "id": 99993,
                "action": "watch",
                "type": "episode",
                "episode": {
                    "season": 1,
                    "number": 1,
                    "ids": { "trakt": 300, "tmdb": 3500, "tvdb": 6500 }
                },
                "show": {
                    "ids": { "trakt": 200, "tmdb": 2000, "tvdb": 4000 }
                }
            }
        ]
        """);

        var shows = TraktDataProjections.ParseWatchedShows(showsDoc.RootElement);
        var history = TraktDataProjections.ParseHistory(historyDoc.RootElement);

        var context = TraktWatchedContext.Create(shows: shows, history: history);

        // Verify show lookup by secondary TMDB ID
        var showTmdbQuery = new TraktIdentity(TraktMediaKind.Show, TmdbId: 2000);
        var showStatus = context.GetShowWatchStatus(showTmdbQuery);
        Assert.Equal(1, showStatus.WatchedEpisodeCount);

        // Verify show lookup by secondary TVDB ID
        var showTvdbQuery = new TraktIdentity(TraktMediaKind.Show, TvdbId: 4000);
        Assert.Equal(1, context.GetShowWatchStatus(showTvdbQuery).WatchedEpisodeCount);

        // Verify episode lookup by direct TMDB ID
        var epTmdbQuery = new TraktIdentity(TraktMediaKind.Episode, TmdbId: 3500, SeasonNumber: 1, EpisodeNumber: 1);
        var epStatus = context.GetEpisodeWatchStatus(epTmdbQuery);
        Assert.True(epStatus.IsWatched);
        Assert.Equal(2, epStatus.Plays);

        // Verify episode lookup by direct TVDB ID
        var epTvdbQuery = new TraktIdentity(TraktMediaKind.Episode, TvdbId: 6500, SeasonNumber: 1, EpisodeNumber: 1);
        Assert.True(context.GetEpisodeWatchStatus(epTvdbQuery).IsWatched);

        // Verify episode lookup by parent coords using enriched ShowTmdbId
        var epCoordQuery = new TraktIdentity(TraktMediaKind.Episode, ShowTmdbId: 2000, SeasonNumber: 1, EpisodeNumber: 1);
        Assert.True(context.GetEpisodeWatchStatus(epCoordQuery).IsWatched);
    }

    [Fact]
    public void Conflicting_richer_ids_rejected_for_shows_and_episodes_after_history_enrichment()
    {
        using var showsDoc = JsonDocument.Parse("""
        [
            {
                "plays": 1,
                "show": { "ids": { "trakt": 200 } },
                "seasons": [
                    {
                        "number": 1,
                        "episodes": [
                            { "number": 1, "plays": 1 }
                        ]
                    }
                ]
            }
        ]
        """);

        using var historyDoc = JsonDocument.Parse("""
        [
            {
                "id": 99994,
                "action": "watch",
                "type": "episode",
                "episode": {
                    "season": 1,
                    "number": 1,
                    "ids": { "trakt": 300, "tmdb": 3500 }
                },
                "show": {
                    "ids": { "trakt": 200, "tmdb": 2000 }
                }
            }
        ]
        """);

        var shows = TraktDataProjections.ParseWatchedShows(showsDoc.RootElement);
        var history = TraktDataProjections.ParseHistory(historyDoc.RootElement);

        var context = TraktWatchedContext.Create(shows: shows, history: history);

        // 1. Conflicting show query: TraktId matches (200), but TmdbId contradicts (9999 vs 2000)
        var conflictingShow = new TraktIdentity(TraktMediaKind.Show, TraktId: 200, TmdbId: 9999);
        var showStatus = context.GetShowWatchStatus(conflictingShow);
        Assert.Equal(0, showStatus.WatchedEpisodeCount);

        // 2. Conflicting episode direct query: TraktId matches (300), but TmdbId contradicts (9999 vs 3500)
        var conflictingEpDirect = new TraktIdentity(TraktMediaKind.Episode, TraktId: 300, TmdbId: 9999, SeasonNumber: 1, EpisodeNumber: 1);
        Assert.False(context.GetEpisodeWatchStatus(conflictingEpDirect).IsWatched);

        // 3. Conflicting episode parent query: ShowTraktId matches (200), but ShowTmdbId contradicts (9999 vs 2000)
        var conflictingEpParent = new TraktIdentity(TraktMediaKind.Episode, ShowTraktId: 200, ShowTmdbId: 9999, SeasonNumber: 1, EpisodeNumber: 1);
        Assert.False(context.GetEpisodeWatchStatus(conflictingEpParent).IsWatched);
    }

    [Fact]
    public void Repeated_history_events_do_not_create_duplicate_candidates_or_inflate_aggregate_plays()
    {
        // Aggregate has Movie 100 (plays: 4) and Show 200 with S1E1 (plays: 5)
        using var moviesDoc = JsonDocument.Parse("""
        [
            {
                "plays": 4,
                "last_watched_at": "2026-01-01T10:00:00Z",
                "movie": { "ids": { "trakt": 100 } }
            }
        ]
        """);

        using var showsDoc = JsonDocument.Parse("""
        [
            {
                "plays": 5,
                "last_watched_at": "2026-01-01T10:00:00Z",
                "show": { "ids": { "trakt": 200 } },
                "seasons": [
                    {
                        "number": 1,
                        "episodes": [
                            { "number": 1, "plays": 5, "last_watched_at": "2026-01-01T10:00:00Z" }
                        ]
                    }
                ]
            }
        ]
        """);

        // History contains 3 repeated events for Movie 100 (richer tmdb: 500)
        // and 3 repeated events for S1E1 (richer show tmdb: 2000, ep tmdb: 3500)
        using var historyDoc = JsonDocument.Parse("""
        [
            {
                "id": 1001,
                "watched_at": "2026-02-01T10:00:00Z",
                "action": "watch",
                "type": "movie",
                "movie": { "ids": { "trakt": 100, "tmdb": 500 } }
            },
            {
                "id": 1002,
                "watched_at": "2026-02-02T10:00:00Z",
                "action": "watch",
                "type": "movie",
                "movie": { "ids": { "trakt": 100, "tmdb": 500 } }
            },
            {
                "id": 1003,
                "watched_at": "2026-02-03T10:00:00Z",
                "action": "watch",
                "type": "movie",
                "movie": { "ids": { "trakt": 100, "tmdb": 500 } }
            },
            {
                "id": 2001,
                "watched_at": "2026-02-01T10:00:00Z",
                "action": "watch",
                "type": "episode",
                "episode": { "season": 1, "number": 1, "ids": { "trakt": 300, "tmdb": 3500 } },
                "show": { "ids": { "trakt": 200, "tmdb": 2000 } }
            },
            {
                "id": 2002,
                "watched_at": "2026-02-02T10:00:00Z",
                "action": "watch",
                "type": "episode",
                "episode": { "season": 1, "number": 1, "ids": { "trakt": 300, "tmdb": 3500 } },
                "show": { "ids": { "trakt": 200, "tmdb": 2000 } }
            },
            {
                "id": 2003,
                "watched_at": "2026-02-03T10:00:00Z",
                "action": "watch",
                "type": "episode",
                "episode": { "season": 1, "number": 1, "ids": { "trakt": 300, "tmdb": 3500 } },
                "show": { "ids": { "trakt": 200, "tmdb": 2000 } }
            }
        ]
        """);

        var movies = TraktDataProjections.ParseWatchedMovies(moviesDoc.RootElement);
        var shows = TraktDataProjections.ParseWatchedShows(showsDoc.RootElement);
        var history = TraktDataProjections.ParseHistory(historyDoc.RootElement);

        var context = TraktWatchedContext.Create(movies: movies, shows: shows, history: history);

        // 1. Movie aggregate plays must NOT be inflated (stays 4, not 4 + 3 = 7)
        var movieStatus = context.GetMovieWatchStatus(new TraktIdentity(TraktMediaKind.Movie, TmdbId: 500));
        Assert.True(movieStatus.IsWatched);
        Assert.Equal(4, movieStatus.Plays);
        Assert.Equal(DateTimeOffset.Parse("2026-02-03T10:00:00Z"), movieStatus.LastWatchedAt);

        // 2. Episode aggregate plays must NOT be inflated (stays 5, not 5 + 3 = 8)
        var epStatus = context.GetEpisodeWatchStatus(new TraktIdentity(TraktMediaKind.Episode, TmdbId: 3500, SeasonNumber: 1, EpisodeNumber: 1));
        Assert.True(epStatus.IsWatched);
        Assert.Equal(5, epStatus.Plays);
        Assert.Equal(DateTimeOffset.Parse("2026-02-03T10:00:00Z"), epStatus.LastWatchedAt);

        // 3. Show watched episode count remains 1 (not 3 distinct candidates)
        var showStatus = context.GetShowWatchStatus(new TraktIdentity(TraktMediaKind.Show, TmdbId: 2000));
        Assert.Equal(1, showStatus.WatchedEpisodeCount);
        Assert.Equal(5, showStatus.TotalPlays);

        // 4. Projection with local candidate snapshot does not produce duplicate candidates or ambiguous matching
        var candidateMovie = new TraktLocalCandidate(1, TraktMediaKind.Movie, new TraktIdentity(TraktMediaKind.Movie, TmdbId: 500), TraktAvailability.Available);
        var snapshot = new TraktLocalSnapshot(new[] { candidateMovie }, DateTimeOffset.UtcNow);

        var projectedMovies = _service.ProjectWatchedMovies(movies, snapshot);
        var movieRow = Assert.Single(projectedMovies);
        Assert.True(movieRow.IsWatched);
        Assert.Equal(4, movieRow.Plays);

        var projectedShows = _service.ProjectWatchedShows(shows, snapshot);
        var showRow = Assert.Single(projectedShows);
        Assert.Equal(1, showRow.WatchedEpisodeCount);
    }

    [Fact]
    public void Episode_having_only_coordinates_enriched_with_direct_and_parent_ids()
    {
        // Aggregate show episode has only coordinates (S1E2) under ShowTraktId: 200, no episode direct IDs
        using var showsDoc = JsonDocument.Parse("""
        [
            {
                "plays": 1,
                "show": { "ids": { "trakt": 200 } },
                "seasons": [
                    {
                        "number": 1,
                        "episodes": [
                            { "number": 2, "plays": 1 }
                        ]
                    }
                ]
            }
        ]
        """);

        // History item provides direct IDs (TraktId: 401, TmdbId: 4001) and parent TmdbId: 2000
        using var historyDoc = JsonDocument.Parse("""
        [
            {
                "id": 99995,
                "action": "watch",
                "type": "episode",
                "episode": {
                    "season": 1,
                    "number": 2,
                    "ids": { "trakt": 401, "tmdb": 4001 }
                },
                "show": {
                    "ids": { "trakt": 200, "tmdb": 2000 }
                }
            }
        ]
        """);

        var shows = TraktDataProjections.ParseWatchedShows(showsDoc.RootElement);
        var history = TraktDataProjections.ParseHistory(historyDoc.RootElement);

        var context = TraktWatchedContext.Create(shows: shows, history: history);

        // 1. Secondary lookup by direct TMDB ID succeeds
        var queryTmdb = new TraktIdentity(TraktMediaKind.Episode, TmdbId: 4001, SeasonNumber: 1, EpisodeNumber: 2);
        Assert.True(context.GetEpisodeWatchStatus(queryTmdb).IsWatched);

        // 2. Lookup by direct Trakt ID succeeds
        var queryTrakt = new TraktIdentity(TraktMediaKind.Episode, TraktId: 401, SeasonNumber: 1, EpisodeNumber: 2);
        Assert.True(context.GetEpisodeWatchStatus(queryTrakt).IsWatched);

        // 3. Conflicting richer ID rejected
        var queryConflict = new TraktIdentity(TraktMediaKind.Episode, TraktId: 401, TmdbId: 9999, SeasonNumber: 1, EpisodeNumber: 2);
        Assert.False(context.GetEpisodeWatchStatus(queryConflict).IsWatched);
    }

    [Fact]
    public void Conflicting_or_ambiguous_history_records_are_not_merged_into_existing_records()
    {
        using var moviesDoc = JsonDocument.Parse("""
        [
            {
                "plays": 1,
                "movie": { "ids": { "trakt": 100 } }
            }
        ]
        """);

        // History item 1 enriches Movie 100 with TMDB 500.
        // History item 2 has Movie 200 with TMDB 999.
        using var historyDoc = JsonDocument.Parse("""
        [
            {
                "id": 1,
                "watched_at": "2026-02-01T10:00:00Z",
                "action": "watch",
                "type": "movie",
                "movie": { "ids": { "trakt": 100, "tmdb": 500 } }
            },
            {
                "id": 2,
                "watched_at": "2026-02-02T10:00:00Z",
                "action": "watch",
                "type": "movie",
                "movie": { "ids": { "trakt": 200, "tmdb": 999 } }
            }
        ]
        """);

        var movies = TraktDataProjections.ParseWatchedMovies(moviesDoc.RootElement);
        var history = TraktDataProjections.ParseHistory(historyDoc.RootElement);

        var context = TraktWatchedContext.Create(movies: movies, history: history);

        // Movie X retains TMDB 500 with original aggregate play count 1 (item 2 did not merge into it)
        var status500 = context.GetMovieWatchStatus(new TraktIdentity(TraktMediaKind.Movie, TmdbId: 500));
        Assert.True(status500.IsWatched);
        Assert.Equal(1, status500.Plays);

        // Conflicting query mixing Movie X's TraktId (100) and Movie Y's TmdbId (999) is rejected
        var conflictStatus = context.GetMovieWatchStatus(new TraktIdentity(TraktMediaKind.Movie, TraktId: 100, TmdbId: 999));
        Assert.False(conflictStatus.IsWatched);
    }
}
