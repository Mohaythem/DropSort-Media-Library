using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace TBDMovies.Application.Trakt;

/// <summary>
/// Service that projects raw remote Trakt models into presentation-ready rows with local library matching,
/// independent file availability, and exact watched badges.
/// </summary>
public sealed class TraktProjectionService : ITraktProjectionService
{
    /// <inheritdoc />
    public IReadOnlyList<TraktProjectedListItem> ProjectListItems(
        IEnumerable<TraktListItem> items,
        TraktLocalSnapshot snapshot,
        TraktWatchedContext? watchedContext = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(snapshot);

        var context = watchedContext ?? TraktWatchedContext.Empty;
        var result = new List<TraktProjectedListItem>();

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var remoteIdentity = ExtractIdentity(item.Type, item.RawElement);

            TraktMatchStatus matchStatus = TraktMatchStatus.NoMatch;
            TraktMediaKind? matchedKind = null;
            int? matchedId = null;
            TraktAvailability availability = TraktAvailability.Missing;

            if (remoteIdentity != null && remoteIdentity.IsValid)
            {
                var match = snapshot.Match(remoteIdentity);
                matchStatus = match.Status;
                if (match.Status == TraktMatchStatus.Matched && match.Candidate != null)
                {
                    matchedKind = match.Candidate.Kind;
                    matchedId = match.Candidate.LocalId;
                    availability = match.Candidate.Availability;
                }
            }

            bool isWatched = false;
            int watchedEpisodeCount = 0;
            int? totalEpisodeCount = null;
            int playCount = 0;
            DateTimeOffset? lastWatchedAt = null;

            if (remoteIdentity != null && remoteIdentity.IsValid)
            {
                if (item.Type == "movie")
                {
                    var watch = context.GetMovieWatchStatus(remoteIdentity);
                    isWatched = watch.IsWatched;
                    playCount = watch.Plays;
                    lastWatchedAt = watch.LastWatchedAt;
                }
                else if (item.Type == "show")
                {
                    var watch = context.GetShowWatchStatus(remoteIdentity);
                    isWatched = watch.IsFullyWatched; // Full show not Watched just because one episode watched
                    watchedEpisodeCount = watch.WatchedEpisodeCount;
                    totalEpisodeCount = watch.TotalEpisodeCount;
                    playCount = watch.TotalPlays;
                    lastWatchedAt = watch.LastWatchedAt;
                }
                else if (item.Type == "season")
                {
                    var watch = context.GetSeasonWatchStatus(remoteIdentity);
                    isWatched = watch.IsFullyWatched;
                    watchedEpisodeCount = watch.WatchedEpisodeCount;
                    totalEpisodeCount = watch.TotalEpisodeCount;
                    playCount = watch.TotalPlays;
                    lastWatchedAt = watch.LastWatchedAt;
                }
                else if (item.Type == "episode")
                {
                    var watch = context.GetEpisodeWatchStatus(remoteIdentity);
                    isWatched = watch.IsWatched;
                    playCount = watch.Plays;
                    lastWatchedAt = watch.LastWatchedAt;
                }
            }

            result.Add(new TraktProjectedListItem(
                Id: item.Id,
                Rank: item.Rank,
                ListedAt: item.ListedAt,
                Type: item.Type,
                Notes: item.Notes,
                MediaTraktId: item.MediaTraktId,
                Title: item.Title,
                Year: item.Year,
                RawElement: item.RawElement,
                RemoteIdentity: remoteIdentity,
                MatchStatus: matchStatus,
                MatchedLocalKind: matchedKind,
                MatchedLocalId: matchedId,
                Availability: availability,
                IsWatched: isWatched,
                WatchedEpisodeCount: watchedEpisodeCount,
                TotalEpisodeCount: totalEpisodeCount,
                PlayCount: playCount,
                LastWatchedAt: lastWatchedAt));
        }

        return result;
    }

    /// <inheritdoc />
    public IReadOnlyList<TraktProjectedWatchlistItem> ProjectWatchlist(
        IEnumerable<TraktWatchlistItem> items,
        TraktLocalSnapshot snapshot,
        TraktWatchedContext? watchedContext = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(snapshot);

        var context = watchedContext ?? TraktWatchedContext.Empty;
        var result = new List<TraktProjectedWatchlistItem>();

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var remoteIdentity = ExtractIdentity(item.Type, item.RawElement);

            TraktMatchStatus matchStatus = TraktMatchStatus.NoMatch;
            TraktMediaKind? matchedKind = null;
            int? matchedId = null;
            TraktAvailability availability = TraktAvailability.Missing;

            if (remoteIdentity != null && remoteIdentity.IsValid)
            {
                var match = snapshot.Match(remoteIdentity);
                matchStatus = match.Status;
                if (match.Status == TraktMatchStatus.Matched && match.Candidate != null)
                {
                    matchedKind = match.Candidate.Kind;
                    matchedId = match.Candidate.LocalId;
                    availability = match.Candidate.Availability;
                }
            }

            bool isWatched = false;
            int watchedEpisodeCount = 0;
            int? totalEpisodeCount = null;
            int playCount = 0;
            DateTimeOffset? lastWatchedAt = null;

            if (remoteIdentity != null && remoteIdentity.IsValid)
            {
                if (item.Type == "movie")
                {
                    var watch = context.GetMovieWatchStatus(remoteIdentity);
                    isWatched = watch.IsWatched;
                    playCount = watch.Plays;
                    lastWatchedAt = watch.LastWatchedAt;
                }
                else if (item.Type == "show")
                {
                    var watch = context.GetShowWatchStatus(remoteIdentity);
                    isWatched = watch.IsFullyWatched; // Full show not Watched just because one episode watched
                    watchedEpisodeCount = watch.WatchedEpisodeCount;
                    totalEpisodeCount = watch.TotalEpisodeCount;
                    playCount = watch.TotalPlays;
                    lastWatchedAt = watch.LastWatchedAt;
                }
                else if (item.Type == "season")
                {
                    var watch = context.GetSeasonWatchStatus(remoteIdentity);
                    isWatched = watch.IsFullyWatched;
                    watchedEpisodeCount = watch.WatchedEpisodeCount;
                    totalEpisodeCount = watch.TotalEpisodeCount;
                    playCount = watch.TotalPlays;
                    lastWatchedAt = watch.LastWatchedAt;
                }
                else if (item.Type == "episode")
                {
                    var watch = context.GetEpisodeWatchStatus(remoteIdentity);
                    isWatched = watch.IsWatched;
                    playCount = watch.Plays;
                    lastWatchedAt = watch.LastWatchedAt;
                }
            }

            result.Add(new TraktProjectedWatchlistItem(
                Id: item.Id,
                Rank: item.Rank,
                ListedAt: item.ListedAt,
                Type: item.Type,
                Notes: item.Notes,
                MediaTraktId: item.MediaTraktId,
                Title: item.Title,
                Year: item.Year,
                RawElement: item.RawElement,
                RemoteIdentity: remoteIdentity,
                MatchStatus: matchStatus,
                MatchedLocalKind: matchedKind,
                MatchedLocalId: matchedId,
                Availability: availability,
                IsWatched: isWatched,
                WatchedEpisodeCount: watchedEpisodeCount,
                TotalEpisodeCount: totalEpisodeCount,
                PlayCount: playCount,
                LastWatchedAt: lastWatchedAt));
        }

        return result;
    }

    /// <inheritdoc />
    public IReadOnlyList<TraktProjectedHistoryItem> ProjectHistory(
        IEnumerable<TraktHistoryItem> items,
        TraktLocalSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(snapshot);

        var result = new List<TraktProjectedHistoryItem>();

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var remoteIdentity = ExtractIdentity(item.Type, item.RawElement);

            TraktMatchStatus matchStatus = TraktMatchStatus.NoMatch;
            TraktMediaKind? matchedKind = null;
            int? matchedId = null;
            TraktAvailability availability = TraktAvailability.Missing;

            if (remoteIdentity != null && remoteIdentity.IsValid)
            {
                var match = snapshot.Match(remoteIdentity);
                matchStatus = match.Status;
                if (match.Status == TraktMatchStatus.Matched && match.Candidate != null)
                {
                    matchedKind = match.Candidate.Kind;
                    matchedId = match.Candidate.LocalId;
                    availability = match.Candidate.Availability;
                }
            }

            // Retains original 64-bit history event ID and repeated chronological events
            result.Add(new TraktProjectedHistoryItem(
                HistoryId: item.Id,
                WatchedAt: item.WatchedAt,
                Action: item.Action,
                Type: item.Type,
                MediaTraktId: item.MediaTraktId,
                Title: item.Title,
                SeasonNumber: item.SeasonNumber,
                EpisodeNumber: item.EpisodeNumber,
                RawElement: item.RawElement,
                RemoteIdentity: remoteIdentity,
                MatchStatus: matchStatus,
                MatchedLocalKind: matchedKind,
                MatchedLocalId: matchedId,
                Availability: availability,
                IsWatched: true));
        }

        return result;
    }

    /// <inheritdoc />
    public IReadOnlyList<TraktProjectedWatchedMovie> ProjectWatchedMovies(
        IEnumerable<TraktWatchedMovie> movies,
        TraktLocalSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(movies);
        ArgumentNullException.ThrowIfNull(snapshot);

        var result = new List<TraktProjectedWatchedMovie>();

        foreach (var movie in movies)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int? tmdb = movie.TmdbId is > 0 and <= int.MaxValue ? (int)movie.TmdbId.Value : null;
            string? imdb = ValidImdb(movie.ImdbId) ? movie.ImdbId!.Trim() : null;

            var identity = new TraktIdentity(
                TraktMediaKind.Movie,
                TraktId: movie.TraktId,
                TmdbId: tmdb,
                ImdbId: imdb);

            TraktMatchStatus matchStatus = TraktMatchStatus.NoMatch;
            TraktMediaKind? matchedKind = null;
            int? matchedId = null;
            TraktAvailability availability = TraktAvailability.Missing;

            if (identity.IsValid)
            {
                var match = snapshot.Match(identity);
                matchStatus = match.Status;
                if (match.Status == TraktMatchStatus.Matched && match.Candidate != null)
                {
                    matchedKind = match.Candidate.Kind;
                    matchedId = match.Candidate.LocalId;
                    availability = match.Candidate.Availability;
                }
            }

            result.Add(new TraktProjectedWatchedMovie(
                Plays: movie.Plays,
                LastWatchedAt: movie.LastWatchedAt,
                LastUpdatedAt: movie.LastUpdatedAt,
                TraktId: movie.TraktId,
                ImdbId: movie.ImdbId,
                TmdbId: movie.TmdbId,
                Title: movie.Title,
                Year: movie.Year,
                RawElement: movie.RawElement,
                RemoteIdentity: identity,
                MatchStatus: matchStatus,
                MatchedLocalKind: matchedKind,
                MatchedLocalId: matchedId,
                Availability: availability,
                IsWatched: true));
        }

        return result;
    }

    /// <inheritdoc />
    public IReadOnlyList<TraktProjectedWatchedShow> ProjectWatchedShows(
        IEnumerable<TraktWatchedShow> shows,
        TraktLocalSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shows);
        ArgumentNullException.ThrowIfNull(snapshot);

        var result = new List<TraktProjectedWatchedShow>();

        foreach (var show in shows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int? tmdb = show.TmdbId is > 0 and <= int.MaxValue ? (int)show.TmdbId.Value : null;
            int? tvdb = show.TvdbId is > 0 and <= int.MaxValue ? (int)show.TvdbId.Value : null;
            string? imdb = ValidImdb(show.ImdbId) ? show.ImdbId!.Trim() : null;

            var showIdentity = new TraktIdentity(
                TraktMediaKind.Show,
                TraktId: show.TraktId,
                TmdbId: tmdb,
                ImdbId: imdb,
                TvdbId: tvdb);

            TraktMatchStatus showMatchStatus = TraktMatchStatus.NoMatch;
            TraktMediaKind? showMatchedKind = null;
            int? showMatchedId = null;

            if (showIdentity.IsValid)
            {
                var showMatch = snapshot.Match(showIdentity);
                showMatchStatus = showMatch.Status;
                if (showMatch.Status == TraktMatchStatus.Matched && showMatch.Candidate != null)
                {
                    showMatchedKind = showMatch.Candidate.Kind;
                    showMatchedId = showMatch.Candidate.LocalId;
                }
            }

            var projectedSeasons = new List<TraktProjectedWatchedSeason>();
            int watchedEpisodeCount = 0;
            bool anyEpisodeAvailable = false;

            foreach (var season in show.Seasons)
            {
                var seasonIdentity = new TraktIdentity(
                    TraktMediaKind.Season,
                    ShowTraktId: show.TraktId,
                    ShowTmdbId: tmdb,
                    ShowImdbId: imdb,
                    ShowTvdbId: tvdb,
                    SeasonNumber: season.Number);

                var seasonMatch = seasonIdentity.IsValid ? snapshot.Match(seasonIdentity) : TraktMatchResult.NoMatch;
                int? seasonMatchedId = seasonMatch.Status == TraktMatchStatus.Matched ? seasonMatch.Candidate?.LocalId : null;

                var projectedEpisodes = new List<TraktProjectedWatchedEpisode>();
                bool seasonHasAvailable = false;

                foreach (var ep in season.Episodes)
                {
                    if (season.Number >= 0 && ep.Number > 0 && ep.Plays > 0)
                    {
                        watchedEpisodeCount++;
                    }

                    var epIdentity = new TraktIdentity(
                        TraktMediaKind.Episode,
                        ShowTraktId: show.TraktId,
                        ShowTmdbId: tmdb,
                        ShowImdbId: imdb,
                        ShowTvdbId: tvdb,
                        SeasonNumber: season.Number,
                        EpisodeNumber: ep.Number);

                    var epMatch = epIdentity.IsValid ? snapshot.Match(epIdentity) : TraktMatchResult.NoMatch;
                    int? epMatchedId = epMatch.Status == TraktMatchStatus.Matched ? epMatch.Candidate?.LocalId : null;
                    var epAvail = epMatch.Status == TraktMatchStatus.Matched && epMatch.Candidate != null
                        ? epMatch.Candidate.Availability
                        : TraktAvailability.Unknown;

                    if (epAvail == TraktAvailability.Available)
                    {
                        seasonHasAvailable = true;
                        anyEpisodeAvailable = true;
                    }

                    projectedEpisodes.Add(new TraktProjectedWatchedEpisode(
                        Number: ep.Number,
                        Plays: ep.Plays,
                        LastWatchedAt: ep.LastWatchedAt,
                        MatchStatus: epMatch.Status,
                        MatchedLocalId: epMatchedId,
                        Availability: epAvail));
                }

                // Season availability:
                // Conservatively Partial if any episode is available; unavailable is only Unknown, never Missing
                TraktAvailability seasonAvail = seasonHasAvailable
                    ? TraktAvailability.Partial
                    : TraktAvailability.Unknown;

                projectedSeasons.Add(new TraktProjectedWatchedSeason(
                    Number: season.Number,
                    Episodes: projectedEpisodes,
                    MatchStatus: seasonMatch.Status,
                    MatchedLocalId: seasonMatchedId,
                    Availability: seasonAvail));
            }

            // Show availability:
            // Conservatively Partial if any episode is available; unavailable is only Unknown, never Missing.
            // Never claim full show Available from a watched subset.
            TraktAvailability showAvailability = anyEpisodeAvailable
                ? TraktAvailability.Partial
                : TraktAvailability.Unknown;

            // Full show not Watched just because one episode watched; never claim entire show watched without full series completion metadata
            bool isFullyWatched = false;

            result.Add(new TraktProjectedWatchedShow(
                Plays: show.Plays,
                LastWatchedAt: show.LastWatchedAt,
                LastUpdatedAt: show.LastUpdatedAt,
                ResetAt: show.ResetAt,
                TraktId: show.TraktId,
                ImdbId: show.ImdbId,
                TmdbId: show.TmdbId,
                TvdbId: show.TvdbId,
                Title: show.Title,
                Year: show.Year,
                WatchedEpisodeCount: watchedEpisodeCount,
                TotalEpisodeCount: null,
                IsWatched: isFullyWatched,
                Seasons: projectedSeasons,
                RawElement: show.RawElement,
                RemoteIdentity: showIdentity,
                MatchStatus: showMatchStatus,
                MatchedLocalKind: showMatchedKind,
                MatchedLocalId: showMatchedId,
                Availability: showAvailability));
        }

        return result;
    }

    private static TraktIdentity? ExtractIdentity(string type, JsonElement rawElement)
    {
        if (rawElement.ValueKind != JsonValueKind.Object)
            return null;

        if (type == "movie")
        {
            if (rawElement.TryGetProperty("movie", out var movieEl) && movieEl.ValueKind == JsonValueKind.Object)
            {
                return ExtractMovieIdentity(movieEl);
            }
        }
        else if (type == "show")
        {
            if (rawElement.TryGetProperty("show", out var showEl) && showEl.ValueKind == JsonValueKind.Object)
            {
                return ExtractShowIdentity(showEl);
            }
        }
        else if (type == "season")
        {
            if (rawElement.TryGetProperty("season", out var seasonEl) && seasonEl.ValueKind == JsonValueKind.Object)
            {
                int? sNum = seasonEl.TryGetProperty("number", out var sn) && sn.ValueKind == JsonValueKind.Number && sn.TryGetInt32(out var sVal) ? sVal : null;
                var (traktId, tmdbId, imdbId, tvdbId) = ExtractIds(seasonEl);

                long? sTr = null; int? sTm = null; string? sIm = null; int? sTv = null;
                if (rawElement.TryGetProperty("show", out var showEl) && showEl.ValueKind == JsonValueKind.Object)
                {
                    (sTr, sTm, sIm, sTv) = ExtractIds(showEl);
                }

                return new TraktIdentity(
                    TraktMediaKind.Season,
                    TraktId: traktId,
                    TmdbId: tmdbId,
                    TvdbId: tvdbId,
                    ShowTraktId: sTr,
                    ShowTmdbId: sTm,
                    ShowImdbId: sIm,
                    ShowTvdbId: sTv,
                    SeasonNumber: sNum);
            }
        }
        else if (type == "episode")
        {
            if (rawElement.TryGetProperty("episode", out var epEl) && epEl.ValueKind == JsonValueKind.Object)
            {
                int? sNum = epEl.TryGetProperty("season", out var sn) && sn.ValueKind == JsonValueKind.Number && sn.TryGetInt32(out var sVal) ? sVal : null;
                int? eNum = epEl.TryGetProperty("number", out var en) && en.ValueKind == JsonValueKind.Number && en.TryGetInt32(out var eVal) ? eVal : null;
                var (traktId, tmdbId, imdbId, tvdbId) = ExtractIds(epEl);

                long? sTr = null; int? sTm = null; string? sIm = null; int? sTv = null;
                if (rawElement.TryGetProperty("show", out var showEl) && showEl.ValueKind == JsonValueKind.Object)
                {
                    (sTr, sTm, sIm, sTv) = ExtractIds(showEl);
                }

                return new TraktIdentity(
                    TraktMediaKind.Episode,
                    TraktId: traktId,
                    TmdbId: tmdbId,
                    ImdbId: imdbId,
                    TvdbId: tvdbId,
                    ShowTraktId: sTr,
                    ShowTmdbId: sTm,
                    ShowImdbId: sIm,
                    ShowTvdbId: sTv,
                    SeasonNumber: sNum,
                    EpisodeNumber: eNum);
            }
        }

        // Unknown type, person, or unrecognized structure
        return null;
    }

    private static TraktIdentity ExtractMovieIdentity(JsonElement movieEl)
    {
        var (traktId, tmdbId, imdbId, tvdbId) = ExtractIds(movieEl);
        return new TraktIdentity(
            TraktMediaKind.Movie,
            TraktId: traktId,
            TmdbId: tmdbId,
            ImdbId: imdbId,
            TvdbId: tvdbId);
    }

    private static TraktIdentity ExtractShowIdentity(JsonElement showEl)
    {
        var (traktId, tmdbId, imdbId, tvdbId) = ExtractIds(showEl);
        return new TraktIdentity(
            TraktMediaKind.Show,
            TraktId: traktId,
            TmdbId: tmdbId,
            ImdbId: imdbId,
            TvdbId: tvdbId);
    }

    private static (long? TraktId, int? TmdbId, string? ImdbId, int? TvdbId) ExtractIds(JsonElement parentEl)
    {
        long? traktId = null;
        int? tmdbId = null;
        string? imdbId = null;
        int? tvdbId = null;

        if (parentEl.TryGetProperty("ids", out var idsEl) && idsEl.ValueKind == JsonValueKind.Object)
        {
            if (idsEl.TryGetProperty("trakt", out var tr) && tr.ValueKind == JsonValueKind.Number && tr.TryGetInt64(out var trVal) && trVal > 0)
                traktId = trVal;

            if (idsEl.TryGetProperty("tmdb", out var tm) && tm.ValueKind == JsonValueKind.Number && tm.TryGetInt32(out var tmVal) && tmVal > 0)
                tmdbId = tmVal;

            if (idsEl.TryGetProperty("imdb", out var im) && im.ValueKind == JsonValueKind.String)
            {
                var s = im.GetString()?.Trim();
                if (ValidImdb(s))
                    imdbId = s;
            }

            if (idsEl.TryGetProperty("tvdb", out var tv) && tv.ValueKind == JsonValueKind.Number && tv.TryGetInt32(out var tvVal) && tvVal > 0)
                tvdbId = tvVal;
        }

        return (traktId, tmdbId, imdbId, tvdbId);
    }

    private static bool ValidImdb(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var id = value.Trim();
        return id.Length >= 9 && id.StartsWith("tt", StringComparison.OrdinalIgnoreCase) &&
            id.AsSpan(2).IndexOfAnyExceptInRange('0', '9') < 0 && id.AsSpan(2).ContainsAnyInRange('1', '9');
    }
}
