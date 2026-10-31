using System;
using System.Collections.Generic;
using System.Text.Json;

namespace TBDMovies.Application.Trakt;

/// <summary>
/// Authenticated user account profile with immutable identifier.
/// </summary>
public sealed record TraktAccountProfile(
    string Uuid,
    string Username,
    string? Name,
    bool IsPrivate,
    bool IsVip,
    JsonElement RawElement);

/// <summary>
/// Summary record for a personal custom list.
/// </summary>
public sealed record TraktCustomList(
    long TraktId,
    string Slug,
    string Name,
    string? Description,
    string? Privacy,
    int ItemCount,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt,
    JsonElement RawElement);

/// <summary>
/// Entry within a custom user list. Retains all items including person and unknown types.
/// </summary>
public sealed record TraktListItem(
    long Id,
    int Rank,
    DateTimeOffset? ListedAt,
    string Type,
    string? Notes,
    long? MediaTraktId,
    string? Title,
    int? Year,
    JsonElement RawElement);

/// <summary>
/// Entry within a user watchlist.
/// </summary>
public sealed record TraktWatchlistItem(
    long Id,
    int Rank,
    DateTimeOffset? ListedAt,
    string Type,
    string? Notes,
    long? MediaTraktId,
    string? Title,
    int? Year,
    JsonElement RawElement);

/// <summary>
/// Record for a watched movie with play count and timestamps.
/// </summary>
public sealed record TraktWatchedMovie(
    int Plays,
    DateTimeOffset? LastWatchedAt,
    DateTimeOffset? LastUpdatedAt,
    long? TraktId,
    string? ImdbId,
    long? TmdbId,
    string? Title,
    int? Year,
    JsonElement RawElement);

/// <summary>
/// Record for a watched show with nested seasons and episode play counts.
/// </summary>
public sealed record TraktWatchedShow(
    int Plays,
    DateTimeOffset? LastWatchedAt,
    DateTimeOffset? LastUpdatedAt,
    DateTimeOffset? ResetAt,
    long? TraktId,
    string? ImdbId,
    long? TmdbId,
    long? TvdbId,
    string? Title,
    int? Year,
    IReadOnlyList<TraktWatchedSeason> Seasons,
    JsonElement RawElement);

/// <summary>
/// Season watch container for a watched show.
/// </summary>
public sealed record TraktWatchedSeason(
    int Number,
    IReadOnlyList<TraktWatchedEpisode> Episodes);

/// <summary>
/// Episode watch record inside a watched season.
/// </summary>
public sealed record TraktWatchedEpisode(
    int Number,
    int Plays,
    DateTimeOffset? LastWatchedAt);

/// <summary>
/// Playback history event with unique 64-bit history identifier.
/// </summary>
public sealed record TraktHistoryItem(
    long Id,
    DateTimeOffset? WatchedAt,
    string Action,
    string Type,
    long? MediaTraktId,
    string? Title,
    int? SeasonNumber,
    int? EpisodeNumber,
    JsonElement RawElement);

/// <summary>
/// Projections from raw JsonElement snapshots into strongly typed records.
/// Never discards records and preserves raw element references for round-trips.
/// </summary>
public static class TraktDataProjections
{
    public static TraktAccountProfile ParseAccountProfile(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("Expected JSON object for account profile.");
        }

        if (!root.TryGetProperty("user", out var userEl))
        {
            throw new FormatException("Account profile missing 'user' object.");
        }

        var username = userEl.TryGetProperty("username", out var uProp) ? uProp.GetString() ?? "" : "";
        var name = userEl.TryGetProperty("name", out var nProp) ? nProp.GetString() : null;
        var isPrivate = userEl.TryGetProperty("private", out var pProp) && pProp.GetBoolean();
        var isVip = userEl.TryGetProperty("vip", out var vProp) && vProp.GetBoolean();

        string? uuid = null;
        if (userEl.TryGetProperty("ids", out var idsEl) && idsEl.TryGetProperty("uuid", out var uuidEl))
        {
            uuid = uuidEl.GetString();
        }

        if (string.IsNullOrWhiteSpace(uuid))
        {
            throw new FormatException("Account profile missing immutable 'user.ids.uuid' identifier.");
        }

        return new TraktAccountProfile(uuid, username, name, isPrivate, isVip, root);
    }

    public static IReadOnlyList<TraktCustomList> ParseLists(JsonElement root)
    {
        var list = new List<TraktCustomList>();
        if (root.ValueKind != JsonValueKind.Array) return list;

        foreach (var item in root.EnumerateArray())
        {
            long traktId = 0;
            string slug = "";
            if (item.TryGetProperty("ids", out var idsEl))
            {
                if (idsEl.TryGetProperty("trakt", out var tProp) && tProp.ValueKind == JsonValueKind.Number && tProp.TryGetInt64(out var tid)) traktId = tid;
                if (idsEl.TryGetProperty("slug", out var sProp)) slug = sProp.GetString() ?? "";
            }

            var name = item.TryGetProperty("name", out var nProp) ? nProp.GetString() ?? "" : "";
            var desc = item.TryGetProperty("description", out var dProp) ? dProp.GetString() : null;
            var priv = item.TryGetProperty("privacy", out var pProp) ? pProp.GetString() : null;
            var count = item.TryGetProperty("item_count", out var cProp) && cProp.ValueKind == JsonValueKind.Number && cProp.TryGetInt32(out var cVal) ? cVal : 0;
            var created = TryGetDateTimeOffset(item, "created_at");
            var updated = TryGetDateTimeOffset(item, "updated_at");

            list.Add(new TraktCustomList(traktId, slug, name, desc, priv, count, created, updated, item));
        }

        return list;
    }

    public static IReadOnlyList<TraktListItem> ParseListItems(JsonElement root)
    {
        var list = new List<TraktListItem>();
        if (root.ValueKind != JsonValueKind.Array) return list;

        foreach (var item in root.EnumerateArray())
        {
            var rank = item.TryGetProperty("rank", out var rProp) && rProp.ValueKind == JsonValueKind.Number && rProp.TryGetInt32(out var rVal) ? rVal : 0;
            var id = item.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.Number && idProp.TryGetInt64(out var idVal) ? idVal : 0L;
            var listedAt = TryGetDateTimeOffset(item, "listed_at");
            var notes = item.TryGetProperty("notes", out var notProp) ? notProp.GetString() : null;
            var type = item.TryGetProperty("type", out var tProp) ? tProp.GetString() ?? "unknown" : "unknown";

            long? mediaTraktId = null;
            string? title = null;
            int? year = null;

            if (item.TryGetProperty(type, out var mediaObj) && mediaObj.ValueKind == JsonValueKind.Object)
            {
                if (mediaObj.TryGetProperty("title", out var titProp)) title = titProp.GetString();
                if (mediaObj.TryGetProperty("name", out var nameProp)) title ??= nameProp.GetString();
                if (mediaObj.TryGetProperty("year", out var yrProp) && yrProp.ValueKind == JsonValueKind.Number && yrProp.TryGetInt32(out var yrVal)) year = yrVal;
                if (mediaObj.TryGetProperty("ids", out var idsObj) && idsObj.TryGetProperty("trakt", out var trProp) && trProp.ValueKind == JsonValueKind.Number && trProp.TryGetInt64(out var trVal))
                {
                    mediaTraktId = trVal;
                }
            }

            list.Add(new TraktListItem(id, rank, listedAt, type, notes, mediaTraktId, title, year, item));
        }

        return list;
    }

    public static IReadOnlyList<TraktWatchlistItem> ParseWatchlist(JsonElement root)
    {
        var list = new List<TraktWatchlistItem>();
        if (root.ValueKind != JsonValueKind.Array) return list;

        foreach (var item in root.EnumerateArray())
        {
            var rank = item.TryGetProperty("rank", out var rProp) && rProp.ValueKind == JsonValueKind.Number && rProp.TryGetInt32(out var rVal) ? rVal : 0;
            var id = item.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.Number && idProp.TryGetInt64(out var idVal) ? idVal : 0L;
            var listedAt = TryGetDateTimeOffset(item, "listed_at");
            var notes = item.TryGetProperty("notes", out var notProp) ? notProp.GetString() : null;
            var type = item.TryGetProperty("type", out var tProp) ? tProp.GetString() ?? "unknown" : "unknown";

            long? mediaTraktId = null;
            string? title = null;
            int? year = null;

            if (item.TryGetProperty(type, out var mediaObj) && mediaObj.ValueKind == JsonValueKind.Object)
            {
                if (mediaObj.TryGetProperty("title", out var titProp)) title = titProp.GetString();
                if (mediaObj.TryGetProperty("year", out var yrProp) && yrProp.ValueKind == JsonValueKind.Number && yrProp.TryGetInt32(out var yrVal)) year = yrVal;
                if (mediaObj.TryGetProperty("ids", out var idsObj) && idsObj.TryGetProperty("trakt", out var trProp) && trProp.ValueKind == JsonValueKind.Number && trProp.TryGetInt64(out var trVal))
                {
                    mediaTraktId = trVal;
                }
            }

            list.Add(new TraktWatchlistItem(id, rank, listedAt, type, notes, mediaTraktId, title, year, item));
        }

        return list;
    }

    public static IReadOnlyList<TraktWatchedMovie> ParseWatchedMovies(JsonElement root)
    {
        var list = new List<TraktWatchedMovie>();
        if (root.ValueKind != JsonValueKind.Array) return list;

        foreach (var item in root.EnumerateArray())
        {
            var plays = item.TryGetProperty("plays", out var plProp) && plProp.ValueKind == JsonValueKind.Number && plProp.TryGetInt32(out var plVal) ? plVal : 1;
            var lastWatched = TryGetDateTimeOffset(item, "last_watched_at");
            var lastUpdated = TryGetDateTimeOffset(item, "last_updated_at");

            long? traktId = null;
            string? imdbId = null;
            long? tmdbId = null;
            string? title = null;
            int? year = null;

            if (item.TryGetProperty("movie", out var movObj))
            {
                if (movObj.TryGetProperty("title", out var titProp)) title = titProp.GetString();
                if (movObj.TryGetProperty("year", out var yrProp) && yrProp.ValueKind == JsonValueKind.Number && yrProp.TryGetInt32(out var yrVal)) year = yrVal;
                if (movObj.TryGetProperty("ids", out var idsObj))
                {
                    if (idsObj.TryGetProperty("trakt", out var trProp) && trProp.ValueKind == JsonValueKind.Number && trProp.TryGetInt64(out var trVal)) traktId = trVal;
                    if (idsObj.TryGetProperty("imdb", out var imProp)) imdbId = imProp.GetString();
                    if (idsObj.TryGetProperty("tmdb", out var tmProp) && tmProp.ValueKind == JsonValueKind.Number && tmProp.TryGetInt64(out var tmVal)) tmdbId = tmVal;
                }
            }

            list.Add(new TraktWatchedMovie(plays, lastWatched, lastUpdated, traktId, imdbId, tmdbId, title, year, item));
        }

        return list;
    }

    public static IReadOnlyList<TraktWatchedShow> ParseWatchedShows(JsonElement root)
    {
        var list = new List<TraktWatchedShow>();
        if (root.ValueKind != JsonValueKind.Array) return list;

        foreach (var item in root.EnumerateArray())
        {
            var plays = item.TryGetProperty("plays", out var plProp) && plProp.ValueKind == JsonValueKind.Number && plProp.TryGetInt32(out var plVal) ? plVal : 1;
            var lastWatched = TryGetDateTimeOffset(item, "last_watched_at");
            var lastUpdated = TryGetDateTimeOffset(item, "last_updated_at");
            var resetAt = TryGetDateTimeOffset(item, "reset_at");

            long? traktId = null;
            string? imdbId = null;
            long? tmdbId = null;
            long? tvdbId = null;
            string? title = null;
            int? year = null;

            if (item.TryGetProperty("show", out var shwObj))
            {
                if (shwObj.TryGetProperty("title", out var titProp)) title = titProp.GetString();
                if (shwObj.TryGetProperty("year", out var yrProp) && yrProp.ValueKind == JsonValueKind.Number && yrProp.TryGetInt32(out var yrVal)) year = yrVal;
                if (shwObj.TryGetProperty("ids", out var idsObj))
                {
                    if (idsObj.TryGetProperty("trakt", out var trProp) && trProp.ValueKind == JsonValueKind.Number && trProp.TryGetInt64(out var trVal)) traktId = trVal;
                    if (idsObj.TryGetProperty("imdb", out var imProp)) imdbId = imProp.GetString();
                    if (idsObj.TryGetProperty("tmdb", out var tmProp) && tmProp.ValueKind == JsonValueKind.Number && tmProp.TryGetInt64(out var tmVal)) tmdbId = tmVal;
                    if (idsObj.TryGetProperty("tvdb", out var tvProp) && tvProp.ValueKind == JsonValueKind.Number && tvProp.TryGetInt64(out var tvVal)) tvdbId = tvVal;
                }
            }

            var seasons = new List<TraktWatchedSeason>();
            if (item.TryGetProperty("seasons", out var seaArr) && seaArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var seaItem in seaArr.EnumerateArray())
                {
                    var sNum = seaItem.TryGetProperty("number", out var snProp) && snProp.ValueKind == JsonValueKind.Number && snProp.TryGetInt32(out var snVal) ? snVal : 0;
                    var episodes = new List<TraktWatchedEpisode>();
                    if (seaItem.TryGetProperty("episodes", out var epArr) && epArr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var epItem in epArr.EnumerateArray())
                        {
                            var epNum = epItem.TryGetProperty("number", out var enProp) && enProp.ValueKind == JsonValueKind.Number && enProp.TryGetInt32(out var enVal) ? enVal : 0;
                            var epPlays = epItem.TryGetProperty("plays", out var eplProp) && eplProp.ValueKind == JsonValueKind.Number && eplProp.TryGetInt32(out var eplVal) ? eplVal : 1;
                            var epWatched = TryGetDateTimeOffset(epItem, "last_watched_at");
                            episodes.Add(new TraktWatchedEpisode(epNum, epPlays, epWatched));
                        }
                    }
                    seasons.Add(new TraktWatchedSeason(sNum, episodes));
                }
            }

            list.Add(new TraktWatchedShow(plays, lastWatched, lastUpdated, resetAt, traktId, imdbId, tmdbId, tvdbId, title, year, seasons, item));
        }

        return list;
    }

    public static IReadOnlyList<TraktHistoryItem> ParseHistory(JsonElement root)
    {
        var list = new List<TraktHistoryItem>();
        if (root.ValueKind != JsonValueKind.Array) return list;

        foreach (var item in root.EnumerateArray())
        {
            var id = item.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.Number && idProp.TryGetInt64(out var idVal) ? idVal : 0L;
            var watchedAt = TryGetDateTimeOffset(item, "watched_at");
            var action = item.TryGetProperty("action", out var acProp) ? acProp.GetString() ?? "watch" : "watch";
            var type = item.TryGetProperty("type", out var tpProp) ? tpProp.GetString() ?? "unknown" : "unknown";

            long? mediaTraktId = null;
            string? title = null;
            int? seasonNum = null;
            int? epNum = null;

            if (item.TryGetProperty(type, out var mediaObj))
            {
                if (mediaObj.TryGetProperty("title", out var titProp)) title = titProp.GetString();
                if (mediaObj.TryGetProperty("season", out var sProp) && sProp.ValueKind == JsonValueKind.Number && sProp.TryGetInt32(out var sVal)) seasonNum = sVal;
                if (mediaObj.TryGetProperty("number", out var nProp) && nProp.ValueKind == JsonValueKind.Number && nProp.TryGetInt32(out var nVal)) epNum = nVal;
                if (mediaObj.TryGetProperty("ids", out var idsObj) && idsObj.TryGetProperty("trakt", out var trProp) && trProp.ValueKind == JsonValueKind.Number && trProp.TryGetInt64(out var trVal))
                {
                    mediaTraktId = trVal;
                }
            }

            if (type == "episode" && item.TryGetProperty("show", out var showObj))
            {
                if (showObj.TryGetProperty("title", out var stitProp))
                {
                    title = string.IsNullOrEmpty(title) ? stitProp.GetString() : $"{stitProp.GetString()} - {title}";
                }
            }

            list.Add(new TraktHistoryItem(id, watchedAt, action, type, mediaTraktId, title, seasonNum, epNum, item));
        }

        return list;
    }

    private static DateTimeOffset? TryGetDateTimeOffset(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) &&
            prop.ValueKind == JsonValueKind.String &&
            prop.TryGetDateTimeOffset(out var dto))
        {
            return dto;
        }
        return null;
    }
}
