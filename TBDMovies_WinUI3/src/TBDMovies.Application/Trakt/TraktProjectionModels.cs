using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace TBDMovies.Application.Trakt;

/// <summary>
/// Immutable snapshot of local catalog candidates and their disk availability.
/// Built off the UI thread from read-only catalog repositories and file inspections.
/// Indexes candidates by kind, strong provider ID, and parent coordinates for O(1) matching.
/// </summary>
public sealed record TraktLocalSnapshot
{
    public IReadOnlyList<TraktLocalCandidate> Candidates { get; }
    public DateTimeOffset CreatedAt { get; }

    private readonly Lazy<ILookup<TraktMediaKind, TraktLocalCandidate>> _byKind;
    private readonly Lazy<SnapshotIndex> _index;

    public TraktLocalSnapshot(IReadOnlyList<TraktLocalCandidate> candidates, DateTimeOffset createdAt)
    {
        Candidates = candidates ?? Array.Empty<TraktLocalCandidate>();
        CreatedAt = createdAt;
        _byKind = new Lazy<ILookup<TraktMediaKind, TraktLocalCandidate>>(() => Candidates.ToLookup(c => c.Kind));
        _index = new Lazy<SnapshotIndex>(() => SnapshotIndex.Build(Candidates));
    }

    /// <summary>
    /// Gets all local candidates for a specific media kind.
    /// </summary>
    public IReadOnlyList<TraktLocalCandidate> ForKind(TraktMediaKind kind) =>
        _byKind.Value[kind].ToArray();

    /// <summary>
    /// Matches a remote identity against the indexed local candidates in this snapshot.
    /// Pure in-memory match; does not execute database or filesystem operations.
    /// Uses index to avoid linear candidate scans over large libraries.
    /// </summary>
    public TraktMatchResult Match(TraktIdentity remote)
    {
        ArgumentNullException.ThrowIfNull(remote);
        if (!remote.IsValid)
            return TraktMatchResult.NoMatch;

        var potentialMatches = _index.Value.FindPotentialMatches(remote);
        if (potentialMatches.Count == 0)
            return TraktMatchResult.NoMatch;

        return TraktMatcher.Match(remote, potentialMatches);
    }

    private sealed class SnapshotIndex
    {
        private readonly Dictionary<(TraktMediaKind Kind, long Id), List<TraktLocalCandidate>> _byTraktId = new();
        private readonly Dictionary<(TraktMediaKind Kind, int Id), List<TraktLocalCandidate>> _byTmdbId = new();
        private readonly Dictionary<(TraktMediaKind Kind, string Id), List<TraktLocalCandidate>> _byImdbId = new();
        private readonly Dictionary<(TraktMediaKind Kind, int Id), List<TraktLocalCandidate>> _byTvdbId = new();
        private readonly Dictionary<string, List<TraktLocalCandidate>> _byParentCoords = new(StringComparer.OrdinalIgnoreCase);

        public static SnapshotIndex Build(IReadOnlyList<TraktLocalCandidate> candidates)
        {
            var index = new SnapshotIndex();
            foreach (var candidate in candidates)
            {
                if (candidate.LocalId <= 0 || !candidate.Identity.IsValid)
                    continue;

                var id = candidate.Identity;

                if (id.TraktId is > 0)
                    Add(index._byTraktId, (id.Kind, id.TraktId.Value), candidate);
                if (id.TmdbId is > 0)
                    Add(index._byTmdbId, (id.Kind, id.TmdbId.Value), candidate);
                if (!string.IsNullOrWhiteSpace(id.ImdbId))
                    Add(index._byImdbId, (id.Kind, id.ImdbId.Trim().ToLowerInvariant()), candidate);
                if (id.TvdbId is > 0)
                    Add(index._byTvdbId, (id.Kind, id.TvdbId.Value), candidate);

                if (id.Kind is TraktMediaKind.Season or TraktMediaKind.Episode && id.HasCoordinates && id.HasParentId)
                {
                    var coords = id.Kind == TraktMediaKind.Season ? $"s{id.SeasonNumber}" : $"s{id.SeasonNumber}e{id.EpisodeNumber}";
                    if (id.ShowTraktId is > 0)
                        AddCoord(index._byParentCoords, $"trakt:{id.ShowTraktId.Value}:{coords}", candidate);
                    if (id.ShowTmdbId is > 0)
                        AddCoord(index._byParentCoords, $"tmdb:{id.ShowTmdbId.Value}:{coords}", candidate);
                    if (!string.IsNullOrWhiteSpace(id.ShowImdbId))
                        AddCoord(index._byParentCoords, $"imdb:{id.ShowImdbId.Trim().ToLowerInvariant()}:{coords}", candidate);
                    if (id.ShowTvdbId is > 0)
                        AddCoord(index._byParentCoords, $"tvdb:{id.ShowTvdbId.Value}:{coords}", candidate);
                }
            }
            return index;
        }

        public IReadOnlyList<TraktLocalCandidate> FindPotentialMatches(TraktIdentity remote)
        {
            var set = new HashSet<TraktLocalCandidate>();

            if (remote.TraktId is > 0 && _byTraktId.TryGetValue((remote.Kind, remote.TraktId.Value), out var byTrakt))
                set.UnionWith(byTrakt);

            if (remote.TmdbId is > 0 && _byTmdbId.TryGetValue((remote.Kind, remote.TmdbId.Value), out var byTmdb))
                set.UnionWith(byTmdb);

            if (!string.IsNullOrWhiteSpace(remote.ImdbId) && _byImdbId.TryGetValue((remote.Kind, remote.ImdbId.Trim().ToLowerInvariant()), out var byImdb))
                set.UnionWith(byImdb);

            if (remote.TvdbId is > 0 && _byTvdbId.TryGetValue((remote.Kind, remote.TvdbId.Value), out var byTvdb))
                set.UnionWith(byTvdb);

            if (remote.Kind is TraktMediaKind.Season or TraktMediaKind.Episode && remote.HasCoordinates && remote.HasParentId)
            {
                var coords = remote.Kind == TraktMediaKind.Season ? $"s{remote.SeasonNumber}" : $"s{remote.SeasonNumber}e{remote.EpisodeNumber}";
                if (remote.ShowTraktId is > 0 && _byParentCoords.TryGetValue($"trakt:{remote.ShowTraktId.Value}:{coords}", out var pTrakt))
                    set.UnionWith(pTrakt);
                if (remote.ShowTmdbId is > 0 && _byParentCoords.TryGetValue($"tmdb:{remote.ShowTmdbId.Value}:{coords}", out var pTmdb))
                    set.UnionWith(pTmdb);
                if (!string.IsNullOrWhiteSpace(remote.ShowImdbId) && _byParentCoords.TryGetValue($"imdb:{remote.ShowImdbId.Trim().ToLowerInvariant()}:{coords}", out var pImdb))
                    set.UnionWith(pImdb);
                if (remote.ShowTvdbId is > 0 && _byParentCoords.TryGetValue($"tvdb:{remote.ShowTvdbId.Value}:{coords}", out var pTvdb))
                    set.UnionWith(pTvdb);
            }

            return set.ToList();
        }

        private static void Add<TKey>(Dictionary<TKey, List<TraktLocalCandidate>> dict, TKey key, TraktLocalCandidate candidate) where TKey : notnull
        {
            if (!dict.TryGetValue(key, out var list))
            {
                list = new List<TraktLocalCandidate>();
                dict[key] = list;
            }
            list.Add(candidate);
        }

        private static void AddCoord(Dictionary<string, List<TraktLocalCandidate>> dict, string key, TraktLocalCandidate candidate)
        {
            if (!dict.TryGetValue(key, out var list))
            {
                list = new List<TraktLocalCandidate>();
                dict[key] = list;
            }
            list.Add(candidate);
        }
    }
}

/// <summary>
/// Lookup context of remote watch activity, watched items, and playback history.
/// Used to compute watch badges and play counts without altering local catalog state.
/// Preserves full identities, strictly rejects conflicting provider keys, resolves ambiguity,
/// and retains actual episode plays and timestamps.
/// </summary>
public sealed class TraktWatchedContext
{
    private sealed class WatchedMovieRecord
    {
        public TraktIdentity Identity { get; set; }
        public int Plays { get; set; }
        public DateTimeOffset? LastWatchedAt { get; set; }
        public bool FromAggregate { get; }

        public WatchedMovieRecord(TraktIdentity identity, int plays, DateTimeOffset? lastWatchedAt, bool fromAggregate)
        {
            Identity = identity;
            Plays = plays;
            LastWatchedAt = lastWatchedAt;
            FromAggregate = fromAggregate;
        }
    }

    private sealed class WatchedEpisodeRecord
    {
        public TraktIdentity Identity { get; set; }
        public int Plays { get; set; }
        public DateTimeOffset? LastWatchedAt { get; set; }
        public bool FromAggregate { get; }

        public WatchedEpisodeRecord(TraktIdentity identity, int plays, DateTimeOffset? lastWatchedAt, bool fromAggregate)
        {
            Identity = identity;
            Plays = plays;
            LastWatchedAt = lastWatchedAt;
            FromAggregate = fromAggregate;
        }
    }

    private sealed class WatchedShowRecord
    {
        public TraktIdentity Identity { get; set; }
        public int TotalPlays { get; set; }
        public DateTimeOffset? LastWatchedAt { get; set; }
        public Dictionary<(int Season, int Episode), WatchedEpisodeRecord> Episodes { get; } = new();
        public bool FromAggregate { get; }

        public WatchedShowRecord(TraktIdentity identity, int totalPlays, DateTimeOffset? lastWatchedAt, bool fromAggregate)
        {
            Identity = identity;
            TotalPlays = totalPlays;
            LastWatchedAt = lastWatchedAt;
            FromAggregate = fromAggregate;
        }
    }

    private readonly List<WatchedMovieRecord> _movieRecords = new();
    private readonly Dictionary<long, List<WatchedMovieRecord>> _moviesByTraktId = new();
    private readonly Dictionary<int, List<WatchedMovieRecord>> _moviesByTmdbId = new();
    private readonly Dictionary<string, List<WatchedMovieRecord>> _moviesByImdbId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, List<WatchedMovieRecord>> _moviesByTvdbId = new();

    private readonly List<WatchedShowRecord> _showRecords = new();
    private readonly Dictionary<long, List<WatchedShowRecord>> _showsByTraktId = new();
    private readonly Dictionary<int, List<WatchedShowRecord>> _showsByTmdbId = new();
    private readonly Dictionary<string, List<WatchedShowRecord>> _showsByImdbId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, List<WatchedShowRecord>> _showsByTvdbId = new();

    private readonly List<WatchedEpisodeRecord> _episodeRecords = new();
    private readonly Dictionary<long, List<WatchedEpisodeRecord>> _episodesByTraktId = new();
    private readonly Dictionary<int, List<WatchedEpisodeRecord>> _episodesByTmdbId = new();
    private readonly Dictionary<string, List<WatchedEpisodeRecord>> _episodesByImdbId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, List<WatchedEpisodeRecord>> _episodesByTvdbId = new();
    private readonly Dictionary<string, List<WatchedEpisodeRecord>> _episodesByParentCoords = new(StringComparer.OrdinalIgnoreCase);

    public static TraktWatchedContext Empty { get; } = new();

    public static TraktWatchedContext Create(
        IEnumerable<TraktWatchedMovie>? movies = null,
        IEnumerable<TraktWatchedShow>? shows = null,
        IEnumerable<TraktHistoryItem>? history = null)
    {
        var context = new TraktWatchedContext();

        if (movies != null)
        {
            foreach (var movie in movies)
            {
                if (movie.Plays <= 0) continue;

                int? tmdb = movie.TmdbId is > 0 and <= int.MaxValue ? (int)movie.TmdbId.Value : null;
                string? imdb = ValidImdb(movie.ImdbId) ? movie.ImdbId!.Trim() : null;

                var identity = new TraktIdentity(
                    TraktMediaKind.Movie,
                    TraktId: movie.TraktId,
                    TmdbId: tmdb,
                    ImdbId: imdb);

                if (!identity.IsValid || !identity.HasDirectId) continue;

                var existingMatches = context.FindPotentialMovieMatches(identity)
                    .Where(m => !m.Identity.DirectIdsConflict(identity) && m.Identity.SharesDirectId(identity))
                    .ToList();

                if (existingMatches.Count == 1)
                {
                    var existing = existingMatches[0];
                    existing.Identity = EnrichIdentity(existing.Identity, identity);
                    context.IndexMovie(existing);
                    existing.Plays = Math.Max(existing.Plays, movie.Plays);
                    if (movie.LastWatchedAt > existing.LastWatchedAt || existing.LastWatchedAt == null)
                        existing.LastWatchedAt = movie.LastWatchedAt;
                }
                else if (existingMatches.Count == 0)
                {
                    var record = new WatchedMovieRecord(identity, movie.Plays, movie.LastWatchedAt, fromAggregate: true);
                    context.AddMovieRecord(record);
                }
            }
        }

        if (shows != null)
        {
            foreach (var show in shows)
            {
                int? tmdb = show.TmdbId is > 0 and <= int.MaxValue ? (int)show.TmdbId.Value : null;
                int? tvdb = show.TvdbId is > 0 and <= int.MaxValue ? (int)show.TvdbId.Value : null;
                string? imdb = ValidImdb(show.ImdbId) ? show.ImdbId!.Trim() : null;

                var showIdentity = new TraktIdentity(
                    TraktMediaKind.Show,
                    TraktId: show.TraktId,
                    TmdbId: tmdb,
                    ImdbId: imdb,
                    TvdbId: tvdb);

                if (!showIdentity.IsValid || !showIdentity.HasDirectId) continue;

                var potentialShows = context.FindPotentialShowMatches(showIdentity);
                var existingMatches = potentialShows
                    .Where(s => !s.Identity.DirectIdsConflict(showIdentity) && s.Identity.SharesDirectId(showIdentity))
                    .ToList();

                WatchedShowRecord showRecord;
                if (existingMatches.Count == 1)
                {
                    showRecord = existingMatches[0];
                    context.EnrichShowIdentity(showRecord, showIdentity);
                    showRecord.TotalPlays = Math.Max(showRecord.TotalPlays, show.Plays);
                    if (show.LastWatchedAt > showRecord.LastWatchedAt || showRecord.LastWatchedAt == null)
                        showRecord.LastWatchedAt = show.LastWatchedAt;
                }
                else if (existingMatches.Count == 0)
                {
                    showRecord = new WatchedShowRecord(showIdentity, show.Plays, show.LastWatchedAt, fromAggregate: true);
                    context.AddShowRecord(showRecord);
                }
                else
                {
                    continue;
                }

                foreach (var season in show.Seasons)
                {
                    foreach (var ep in season.Episodes)
                    {
                        if (season.Number >= 0 && ep.Number > 0 && ep.Plays > 0)
                        {
                            var epIdentity = new TraktIdentity(
                                TraktMediaKind.Episode,
                                ShowTraktId: showRecord.Identity.TraktId,
                                ShowTmdbId: showRecord.Identity.TmdbId,
                                ShowImdbId: showRecord.Identity.ImdbId,
                                ShowTvdbId: showRecord.Identity.TvdbId,
                                SeasonNumber: season.Number,
                                EpisodeNumber: ep.Number);

                            if (epIdentity.IsValid)
                            {
                                var key = (season.Number, ep.Number);
                                if (showRecord.Episodes.TryGetValue(key, out var existingEp))
                                {
                                    existingEp.Identity = EnrichIdentity(existingEp.Identity, epIdentity);
                                    context.IndexEpisode(existingEp);
                                    existingEp.Plays = Math.Max(existingEp.Plays, ep.Plays);
                                    if (ep.LastWatchedAt > existingEp.LastWatchedAt || existingEp.LastWatchedAt == null)
                                        existingEp.LastWatchedAt = ep.LastWatchedAt;
                                }
                                else
                                {
                                    var epRecord = new WatchedEpisodeRecord(epIdentity, ep.Plays, ep.LastWatchedAt, fromAggregate: true);
                                    showRecord.Episodes[key] = epRecord;
                                    context.AddEpisodeRecord(epRecord);
                                }
                            }
                        }
                    }
                }
            }
        }

        if (history != null)
        {
            foreach (var item in history)
            {
                if (item.Type == "movie")
                {
                    var identity = ExtractHistoryMovieIdentity(item);
                    if (identity == null || !identity.IsValid) continue;

                    var existingMatches = context.FindPotentialMovieMatches(identity)
                        .Where(m => !m.Identity.DirectIdsConflict(identity) && m.Identity.SharesDirectId(identity))
                        .ToList();

                    if (existingMatches.Count == 1)
                    {
                        var existing = existingMatches[0];
                        existing.Identity = EnrichIdentity(existing.Identity, identity);
                        context.IndexMovie(existing);

                        if (!existing.FromAggregate)
                        {
                            existing.Plays++;
                            if (item.WatchedAt > existing.LastWatchedAt || existing.LastWatchedAt == null)
                                existing.LastWatchedAt = item.WatchedAt;
                        }
                        else
                        {
                            if (item.WatchedAt > existing.LastWatchedAt || existing.LastWatchedAt == null)
                                existing.LastWatchedAt = item.WatchedAt;
                        }
                    }
                    else if (existingMatches.Count == 0)
                    {
                        var record = new WatchedMovieRecord(identity, plays: 1, item.WatchedAt, fromAggregate: false);
                        context.AddMovieRecord(record);
                    }
                }
                else if (item.Type == "episode")
                {
                    var identity = ExtractHistoryEpisodeIdentity(item);
                    if (identity == null || !identity.IsValid) continue;

                    var existingMatches = context.FindPotentialEpisodeMatches(identity);
                    var direct = identity.HasDirectId
                        ? existingMatches.Where(e => e.Identity.HasDirectId &&
                                                    !e.Identity.DirectIdsConflict(identity) &&
                                                    !e.Identity.ParentIdsConflict(identity) &&
                                                    !e.Identity.CoordinatesConflict(identity) &&
                                                    e.Identity.SharesDirectId(identity)).ToList()
                        : new List<WatchedEpisodeRecord>();

                    WatchedEpisodeRecord? matched = null;
                    bool isAmbiguous = false;

                    if (direct.Count == 1)
                    {
                        matched = direct[0];
                    }
                    else if (direct.Count > 1)
                    {
                        isAmbiguous = true;
                    }
                    else if (identity.HasCoordinates && identity.HasParentId)
                    {
                        var coords = existingMatches.Where(e => e.Identity.HasCoordinates && e.Identity.HasParentId &&
                                                               !e.Identity.DirectIdsConflict(identity) &&
                                                               !e.Identity.ParentIdsConflict(identity) &&
                                                               !e.Identity.CoordinatesConflict(identity) &&
                                                               e.Identity.SharesParentId(identity) &&
                                                               e.Identity.SeasonNumber == identity.SeasonNumber &&
                                                               e.Identity.EpisodeNumber == identity.EpisodeNumber).ToList();
                        if (coords.Count == 1)
                        {
                            matched = coords[0];
                        }
                        else if (coords.Count > 1)
                        {
                            isAmbiguous = true;
                        }
                    }

                    if (isAmbiguous)
                    {
                        continue;
                    }

                    if (matched != null)
                    {
                        if (!matched.FromAggregate)
                        {
                            matched.Plays++;
                            if (item.WatchedAt > matched.LastWatchedAt || matched.LastWatchedAt == null)
                                matched.LastWatchedAt = item.WatchedAt;
                        }
                        else
                        {
                            if (item.WatchedAt > matched.LastWatchedAt || matched.LastWatchedAt == null)
                                matched.LastWatchedAt = item.WatchedAt;
                        }

                        matched.Identity = EnrichIdentity(matched.Identity, identity);
                        context.IndexEpisode(matched);
                        context.AttachHistoryEpisodeToShow(matched, isRepeatWatch: true);
                    }
                    else if (direct.Count == 0)
                    {
                        var record = new WatchedEpisodeRecord(identity, plays: 1, item.WatchedAt, fromAggregate: false);
                        context.AddEpisodeRecord(record);
                        context.AttachHistoryEpisodeToShow(record, isRepeatWatch: false);
                    }
                }
            }
        }

        return context;
    }

    private static TraktIdentity EnrichIdentity(TraktIdentity current, TraktIdentity incoming)
    {
        return current with
        {
            TraktId = current.TraktId ?? incoming.TraktId,
            TmdbId = current.TmdbId ?? incoming.TmdbId,
            ImdbId = string.IsNullOrWhiteSpace(current.ImdbId) ? incoming.ImdbId : current.ImdbId,
            TvdbId = current.TvdbId ?? incoming.TvdbId,
            ShowTraktId = current.ShowTraktId ?? incoming.ShowTraktId,
            ShowTmdbId = current.ShowTmdbId ?? incoming.ShowTmdbId,
            ShowImdbId = string.IsNullOrWhiteSpace(current.ShowImdbId) ? incoming.ShowImdbId : current.ShowImdbId,
            ShowTvdbId = current.ShowTvdbId ?? incoming.ShowTvdbId,
            SeasonNumber = current.SeasonNumber ?? incoming.SeasonNumber,
            EpisodeNumber = current.EpisodeNumber ?? incoming.EpisodeNumber
        };
    }

    private void AddMovieRecord(WatchedMovieRecord record)
    {
        _movieRecords.Add(record);
        IndexMovie(record);
    }

    private void IndexMovie(WatchedMovieRecord record)
    {
        var id = record.Identity;
        if (id.TraktId is > 0)
            AddToList(_moviesByTraktId, id.TraktId.Value, record);
        if (id.TmdbId is > 0)
            AddToList(_moviesByTmdbId, id.TmdbId.Value, record);
        if (!string.IsNullOrWhiteSpace(id.ImdbId))
            AddToList(_moviesByImdbId, id.ImdbId.Trim().ToLowerInvariant(), record);
        if (id.TvdbId is > 0)
            AddToList(_moviesByTvdbId, id.TvdbId.Value, record);
    }

    private void AddShowRecord(WatchedShowRecord record)
    {
        _showRecords.Add(record);
        IndexShow(record);
    }

    private void IndexShow(WatchedShowRecord record)
    {
        var id = record.Identity;
        if (id.TraktId is > 0)
            AddToList(_showsByTraktId, id.TraktId.Value, record);
        if (id.TmdbId is > 0)
            AddToList(_showsByTmdbId, id.TmdbId.Value, record);
        if (!string.IsNullOrWhiteSpace(id.ImdbId))
            AddToList(_showsByImdbId, id.ImdbId.Trim().ToLowerInvariant(), record);
        if (id.TvdbId is > 0)
            AddToList(_showsByTvdbId, id.TvdbId.Value, record);
    }

    private void EnrichShowIdentity(WatchedShowRecord show, TraktIdentity incoming)
    {
        var enriched = EnrichIdentity(show.Identity, incoming);
        if (enriched == show.Identity) return;
        show.Identity = enriched;
        IndexShow(show);

        // New parent evidence also constrains siblings absent from the history page.
        var parent = new TraktIdentity(TraktMediaKind.Episode,
            ShowTraktId: enriched.TraktId, ShowTmdbId: enriched.TmdbId,
            ShowImdbId: enriched.ImdbId, ShowTvdbId: enriched.TvdbId);
        foreach (var episode in show.Episodes.Values)
        {
            if (episode.Identity.ParentIdsConflict(parent)) continue;
            episode.Identity = EnrichIdentity(episode.Identity, parent);
            IndexEpisode(episode);
        }
    }

    private void AddEpisodeRecord(WatchedEpisodeRecord record)
    {
        _episodeRecords.Add(record);
        IndexEpisode(record);
    }

    private void IndexEpisode(WatchedEpisodeRecord record)
    {
        var id = record.Identity;
        if (id.TraktId is > 0)
            AddToList(_episodesByTraktId, id.TraktId.Value, record);
        if (id.TmdbId is > 0)
            AddToList(_episodesByTmdbId, id.TmdbId.Value, record);
        if (!string.IsNullOrWhiteSpace(id.ImdbId))
            AddToList(_episodesByImdbId, id.ImdbId.Trim().ToLowerInvariant(), record);
        if (id.TvdbId is > 0)
            AddToList(_episodesByTvdbId, id.TvdbId.Value, record);

        if (id.HasCoordinates && id.HasParentId)
        {
            var coords = $"s{id.SeasonNumber}e{id.EpisodeNumber}";
            if (id.ShowTraktId is > 0)
                AddToList(_episodesByParentCoords, $"trakt:{id.ShowTraktId.Value}:{coords}", record);
            if (id.ShowTmdbId is > 0)
                AddToList(_episodesByParentCoords, $"tmdb:{id.ShowTmdbId.Value}:{coords}", record);
            if (!string.IsNullOrWhiteSpace(id.ShowImdbId))
                AddToList(_episodesByParentCoords, $"imdb:{id.ShowImdbId.Trim().ToLowerInvariant()}:{coords}", record);
            if (id.ShowTvdbId is > 0)
                AddToList(_episodesByParentCoords, $"tvdb:{id.ShowTvdbId.Value}:{coords}", record);
        }
    }

    private void AttachHistoryEpisodeToShow(WatchedEpisodeRecord epRecord, bool isRepeatWatch = false)
    {
        var epId = epRecord.Identity;
        if (!epId.HasParentId || !epId.HasCoordinates) return;

        var parentShow = new TraktIdentity(
            TraktMediaKind.Show,
            TraktId: epId.ShowTraktId,
            TmdbId: epId.ShowTmdbId,
            ImdbId: epId.ShowImdbId,
            TvdbId: epId.ShowTvdbId);

        if (!parentShow.IsValid || !parentShow.HasDirectId) return;

        var potentialShows = FindPotentialShowMatches(parentShow);
        var directShows = potentialShows
            .Where(s => !s.Identity.DirectIdsConflict(parentShow) && s.Identity.SharesDirectId(parentShow))
            .Distinct()
            .ToList();

        WatchedShowRecord show;
        if (directShows.Count == 1)
        {
            show = directShows[0];
            EnrichShowIdentity(show, parentShow);

            var enrichedEpId = epRecord.Identity with
            {
                ShowTraktId = epRecord.Identity.ShowTraktId ?? show.Identity.TraktId,
                ShowTmdbId = epRecord.Identity.ShowTmdbId ?? show.Identity.TmdbId,
                ShowImdbId = string.IsNullOrWhiteSpace(epRecord.Identity.ShowImdbId) ? show.Identity.ImdbId : epRecord.Identity.ShowImdbId,
                ShowTvdbId = epRecord.Identity.ShowTvdbId ?? show.Identity.TvdbId
            };
            if (enrichedEpId != epRecord.Identity)
            {
                epRecord.Identity = enrichedEpId;
                IndexEpisode(epRecord);
            }

            if (!show.FromAggregate)
            {
                if (!isRepeatWatch || !epRecord.FromAggregate)
                {
                    show.TotalPlays++;
                    if (epRecord.LastWatchedAt > show.LastWatchedAt || show.LastWatchedAt == null)
                        show.LastWatchedAt = epRecord.LastWatchedAt;
                }
            }
            else
            {
                if (epRecord.LastWatchedAt > show.LastWatchedAt || show.LastWatchedAt == null)
                    show.LastWatchedAt = epRecord.LastWatchedAt;
            }
        }
        else if (directShows.Count == 0)
        {
            show = new WatchedShowRecord(parentShow, totalPlays: 1, lastWatchedAt: epRecord.LastWatchedAt, fromAggregate: false);
            AddShowRecord(show);
        }
        else
        {
            return;
        }

        var key = (epId.SeasonNumber!.Value, epId.EpisodeNumber!.Value);
        if (!show.Episodes.ContainsKey(key))
        {
            show.Episodes[key] = epRecord;
        }
    }

    private HashSet<WatchedMovieRecord> FindPotentialMovieMatches(TraktIdentity remote)
    {
        var set = new HashSet<WatchedMovieRecord>();
        if (remote.TraktId is > 0 && _moviesByTraktId.TryGetValue(remote.TraktId.Value, out var byTr))
            set.UnionWith(byTr);
        if (remote.TmdbId is > 0 && _moviesByTmdbId.TryGetValue(remote.TmdbId.Value, out var byTm))
            set.UnionWith(byTm);
        if (!string.IsNullOrWhiteSpace(remote.ImdbId) && _moviesByImdbId.TryGetValue(remote.ImdbId.Trim().ToLowerInvariant(), out var byIm))
            set.UnionWith(byIm);
        if (remote.TvdbId is > 0 && _moviesByTvdbId.TryGetValue(remote.TvdbId.Value, out var byTv))
            set.UnionWith(byTv);
        return set;
    }

    private HashSet<WatchedShowRecord> FindPotentialShowMatches(TraktIdentity remote)
    {
        var set = new HashSet<WatchedShowRecord>();
        if (remote.TraktId is > 0 && _showsByTraktId.TryGetValue(remote.TraktId.Value, out var byTr))
            set.UnionWith(byTr);
        if (remote.TmdbId is > 0 && _showsByTmdbId.TryGetValue(remote.TmdbId.Value, out var byTm))
            set.UnionWith(byTm);
        if (!string.IsNullOrWhiteSpace(remote.ImdbId) && _showsByImdbId.TryGetValue(remote.ImdbId.Trim().ToLowerInvariant(), out var byIm))
            set.UnionWith(byIm);
        if (remote.TvdbId is > 0 && _showsByTvdbId.TryGetValue(remote.TvdbId.Value, out var byTv))
            set.UnionWith(byTv);
        return set;
    }

    private HashSet<WatchedEpisodeRecord> FindPotentialEpisodeMatches(TraktIdentity remote)
    {
        var set = new HashSet<WatchedEpisodeRecord>();
        if (remote.TraktId is > 0 && _episodesByTraktId.TryGetValue(remote.TraktId.Value, out var byTr))
            set.UnionWith(byTr);
        if (remote.TmdbId is > 0 && _episodesByTmdbId.TryGetValue(remote.TmdbId.Value, out var byTm))
            set.UnionWith(byTm);
        if (!string.IsNullOrWhiteSpace(remote.ImdbId) && _episodesByImdbId.TryGetValue(remote.ImdbId.Trim().ToLowerInvariant(), out var byIm))
            set.UnionWith(byIm);
        if (remote.TvdbId is > 0 && _episodesByTvdbId.TryGetValue(remote.TvdbId.Value, out var byTv))
            set.UnionWith(byTv);

        if (remote.HasCoordinates && remote.HasParentId)
        {
            var coords = $"s{remote.SeasonNumber}e{remote.EpisodeNumber}";
            if (remote.ShowTraktId is > 0 && _episodesByParentCoords.TryGetValue($"trakt:{remote.ShowTraktId.Value}:{coords}", out var pTr))
                set.UnionWith(pTr);
            if (remote.ShowTmdbId is > 0 && _episodesByParentCoords.TryGetValue($"tmdb:{remote.ShowTmdbId.Value}:{coords}", out var pTm))
                set.UnionWith(pTm);
            if (!string.IsNullOrWhiteSpace(remote.ShowImdbId) && _episodesByParentCoords.TryGetValue($"imdb:{remote.ShowImdbId.Trim().ToLowerInvariant()}:{coords}", out var pIm))
                set.UnionWith(pIm);
            if (remote.ShowTvdbId is > 0 && _episodesByParentCoords.TryGetValue($"tvdb:{remote.ShowTvdbId.Value}:{coords}", out var pTv))
                set.UnionWith(pTv);
        }
        return set;
    }

    private static void AddToList<TKey, TItem>(Dictionary<TKey, List<TItem>> dict, TKey key, TItem item) where TKey : notnull
    {
        if (!dict.TryGetValue(key, out var list))
        {
            list = new List<TItem>();
            dict[key] = list;
        }
        if (!list.Contains(item))
        {
            list.Add(item);
        }
    }

    private static TraktIdentity? ExtractHistoryMovieIdentity(TraktHistoryItem item)
    {
        long? traktId = item.MediaTraktId is > 0 ? item.MediaTraktId : null;
        int? tmdbId = null;
        string? imdbId = null;
        int? tvdbId = null;

        if (item.RawElement.ValueKind == JsonValueKind.Object &&
            item.RawElement.TryGetProperty("movie", out var movEl) &&
            movEl.ValueKind == JsonValueKind.Object &&
            movEl.TryGetProperty("ids", out var idsEl) &&
            idsEl.ValueKind == JsonValueKind.Object)
        {
            if (idsEl.TryGetProperty("trakt", out var tr) && tr.ValueKind == JsonValueKind.Number && tr.TryGetInt64(out var trVal) && trVal > 0)
                traktId ??= trVal;
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

        if (traktId is null && tmdbId is null && imdbId is null && tvdbId is null)
            return null;

        var id = new TraktIdentity(TraktMediaKind.Movie, TraktId: traktId, TmdbId: tmdbId, ImdbId: imdbId, TvdbId: tvdbId);
        return id.IsValid ? id : null;
    }

    private static TraktIdentity? ExtractHistoryEpisodeIdentity(TraktHistoryItem item)
    {
        long? traktId = item.MediaTraktId is > 0 ? item.MediaTraktId : null;
        int? tmdbId = null;
        string? imdbId = null;
        int? tvdbId = null;

        long? showTraktId = null;
        int? showTmdbId = null;
        string? showImdbId = null;
        int? showTvdbId = null;

        int? seasonNumber = item.SeasonNumber;
        int? episodeNumber = item.EpisodeNumber;

        if (item.RawElement.ValueKind == JsonValueKind.Object)
        {
            if (item.RawElement.TryGetProperty("episode", out var epEl) && epEl.ValueKind == JsonValueKind.Object)
            {
                if (seasonNumber is null && epEl.TryGetProperty("season", out var sn) && sn.ValueKind == JsonValueKind.Number && sn.TryGetInt32(out var sVal))
                    seasonNumber = sVal;
                if (episodeNumber is null && epEl.TryGetProperty("number", out var en) && en.ValueKind == JsonValueKind.Number && en.TryGetInt32(out var eVal))
                    episodeNumber = eVal;

                if (epEl.TryGetProperty("ids", out var epIds) && epIds.ValueKind == JsonValueKind.Object)
                {
                    if (epIds.TryGetProperty("trakt", out var tr) && tr.ValueKind == JsonValueKind.Number && tr.TryGetInt64(out var trVal) && trVal > 0)
                        traktId ??= trVal;
                    if (epIds.TryGetProperty("tmdb", out var tm) && tm.ValueKind == JsonValueKind.Number && tm.TryGetInt32(out var tmVal) && tmVal > 0)
                        tmdbId = tmVal;
                    if (epIds.TryGetProperty("imdb", out var im) && im.ValueKind == JsonValueKind.String)
                    {
                        var s = im.GetString()?.Trim();
                        if (ValidImdb(s))
                            imdbId = s;
                    }
                    if (epIds.TryGetProperty("tvdb", out var tv) && tv.ValueKind == JsonValueKind.Number && tv.TryGetInt32(out var tvVal) && tvVal > 0)
                        tvdbId = tvVal;
                }
            }

            if (item.RawElement.TryGetProperty("show", out var showEl) && showEl.ValueKind == JsonValueKind.Object &&
                showEl.TryGetProperty("ids", out var sIds) && sIds.ValueKind == JsonValueKind.Object)
            {
                if (sIds.TryGetProperty("trakt", out var str) && str.ValueKind == JsonValueKind.Number && str.TryGetInt64(out var strVal) && strVal > 0)
                    showTraktId = strVal;
                if (sIds.TryGetProperty("tmdb", out var stm) && stm.ValueKind == JsonValueKind.Number && stm.TryGetInt32(out var stmVal) && stmVal > 0)
                    showTmdbId = stmVal;
                if (sIds.TryGetProperty("imdb", out var sim) && sim.ValueKind == JsonValueKind.String)
                {
                    var s = sim.GetString()?.Trim();
                    if (ValidImdb(s))
                        showImdbId = s;
                }
                if (sIds.TryGetProperty("tvdb", out var stv) && stv.ValueKind == JsonValueKind.Number && stv.TryGetInt32(out var stvVal) && stvVal > 0)
                    showTvdbId = stvVal;
            }
        }

        var id = new TraktIdentity(
            TraktMediaKind.Episode,
            TraktId: traktId,
            TmdbId: tmdbId,
            ImdbId: imdbId,
            TvdbId: tvdbId,
            ShowTraktId: showTraktId,
            ShowTmdbId: showTmdbId,
            ShowImdbId: showImdbId,
            ShowTvdbId: showTvdbId,
            SeasonNumber: seasonNumber,
            EpisodeNumber: episodeNumber);

        return id.IsValid ? id : null;
    }

    private static bool ValidImdb(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var id = value.Trim();
        return id.Length >= 9 && id.StartsWith("tt", StringComparison.OrdinalIgnoreCase) &&
            id.AsSpan(2).IndexOfAnyExceptInRange('0', '9') < 0 && id.AsSpan(2).ContainsAnyInRange('1', '9');
    }

    public (bool IsWatched, int Plays, DateTimeOffset? LastWatchedAt) GetMovieWatchStatus(TraktIdentity identity)
    {
        if (identity.Kind != TraktMediaKind.Movie || !identity.IsValid || !identity.HasDirectId)
            return (false, 0, null);

        var potentialMatches = FindPotentialMovieMatches(identity);
        if (potentialMatches.Count == 0)
            return (false, 0, null);

        var direct = potentialMatches
            .Where(m => !m.Identity.DirectIdsConflict(identity) && m.Identity.SharesDirectId(identity))
            .Distinct()
            .ToList();

        if (direct.Count == 1)
            return (true, direct[0].Plays, direct[0].LastWatchedAt);

        return (false, 0, null);
    }

    public (int WatchedEpisodeCount, int? TotalEpisodeCount, bool IsFullyWatched, int TotalPlays, DateTimeOffset? LastWatchedAt) GetShowWatchStatus(TraktIdentity identity)
    {
        if (identity.Kind != TraktMediaKind.Show || !identity.IsValid || !identity.HasDirectId)
            return (0, null, false, 0, null);

        var potentialMatches = FindPotentialShowMatches(identity);
        if (potentialMatches.Count == 0)
            return (0, null, false, 0, null);

        var direct = potentialMatches
            .Where(s => !s.Identity.DirectIdsConflict(identity) && s.Identity.SharesDirectId(identity))
            .Distinct()
            .ToList();

        if (direct.Count == 1)
        {
            var match = direct[0];
            return (
                WatchedEpisodeCount: match.Episodes.Count,
                TotalEpisodeCount: null, // Full remote episode set unknown unless explicit series totals are declared
                IsFullyWatched: false,   // Conservative: never claim full show watched unless entire known set complete
                TotalPlays: match.TotalPlays,
                LastWatchedAt: match.LastWatchedAt);
        }

        return (0, null, false, 0, null);
    }

    public (int WatchedEpisodeCount, int? TotalEpisodeCount, bool IsFullyWatched, int TotalPlays, DateTimeOffset? LastWatchedAt) GetSeasonWatchStatus(TraktIdentity identity)
    {
        if (identity.Kind != TraktMediaKind.Season || !identity.IsValid || identity.SeasonNumber is null || !identity.HasParentId)
            return (0, null, false, 0, null);

        var parentShow = new TraktIdentity(
            TraktMediaKind.Show,
            TraktId: identity.ShowTraktId,
            TmdbId: identity.ShowTmdbId,
            ImdbId: identity.ShowImdbId,
            TvdbId: identity.ShowTvdbId);

        if (!parentShow.IsValid || !parentShow.HasDirectId)
            return (0, null, false, 0, null);

        var potentialShows = FindPotentialShowMatches(parentShow);
        if (potentialShows.Count == 0)
            return (0, null, false, 0, null);

        var directShows = potentialShows
            .Where(s => !s.Identity.DirectIdsConflict(parentShow) && s.Identity.SharesDirectId(parentShow))
            .Distinct()
            .ToList();

        if (directShows.Count == 1)
        {
            var show = directShows[0];
            var sNum = identity.SeasonNumber.Value;
            var seasonEps = show.Episodes.Values
                .Where(e => e.Identity.SeasonNumber == sNum && e.Plays > 0)
                .ToList();

            var count = seasonEps.Count;
            var plays = seasonEps.Sum(e => e.Plays);
            DateTimeOffset? last = seasonEps.Count > 0 ? seasonEps.Max(e => e.LastWatchedAt) : null;

            return (
                WatchedEpisodeCount: count,
                TotalEpisodeCount: null,
                IsFullyWatched: false,
                TotalPlays: plays,
                LastWatchedAt: last);
        }

        return (0, null, false, 0, null);
    }

    public (bool IsWatched, int Plays, DateTimeOffset? LastWatchedAt) GetEpisodeWatchStatus(TraktIdentity identity)
    {
        if (identity.Kind != TraktMediaKind.Episode || !identity.IsValid)
            return (false, 0, null);

        var potentialMatches = FindPotentialEpisodeMatches(identity);
        if (potentialMatches.Count == 0)
            return (false, 0, null);

        if (identity.HasDirectId)
        {
            var direct = potentialMatches
                .Where(e => e.Identity.HasDirectId &&
                            !e.Identity.DirectIdsConflict(identity) &&
                            !e.Identity.ParentIdsConflict(identity) &&
                            !e.Identity.CoordinatesConflict(identity) &&
                            e.Identity.SharesDirectId(identity))
                .Distinct()
                .ToList();

            if (direct.Count == 1)
                return (true, direct[0].Plays, direct[0].LastWatchedAt);
            if (direct.Count > 1)
                return (false, 0, null);
        }

        if (identity.HasCoordinates && identity.HasParentId)
        {
            var coords = potentialMatches
                .Where(e => e.Identity.HasCoordinates && e.Identity.HasParentId &&
                            !e.Identity.DirectIdsConflict(identity) &&
                            !e.Identity.ParentIdsConflict(identity) &&
                            !e.Identity.CoordinatesConflict(identity) &&
                            e.Identity.SharesParentId(identity) &&
                            e.Identity.SeasonNumber == identity.SeasonNumber &&
                            e.Identity.EpisodeNumber == identity.EpisodeNumber)
                .Distinct()
                .ToList();

            if (coords.Count == 1)
                return (true, coords[0].Plays, coords[0].LastWatchedAt);
            if (coords.Count > 1)
                return (false, 0, null);
        }

        return (false, 0, null);
    }
}

/// <summary>
/// Projected row for an item in a Trakt custom list.
/// Retains all raw payload fields, list rank, notes, and provides fallback visible display title.
/// </summary>
public sealed record TraktProjectedListItem(
    long Id,
    int Rank,
    DateTimeOffset? ListedAt,
    string Type,
    string? Notes,
    long? MediaTraktId,
    string? Title,
    int? Year,
    JsonElement RawElement,
    TraktIdentity? RemoteIdentity,
    TraktMatchStatus MatchStatus,
    TraktMediaKind? MatchedLocalKind,
    int? MatchedLocalId,
    TraktAvailability Availability,
    bool IsWatched,
    int WatchedEpisodeCount,
    int? TotalEpisodeCount,
    int PlayCount,
    DateTimeOffset? LastWatchedAt)
{
    public string DisplayTitle =>
        !string.IsNullOrWhiteSpace(Title)
            ? Title
            : MediaTraktId is > 0
                ? $"[{Type} #{MediaTraktId}]"
                : Id > 0
                    ? $"[{Type} #{Id}]"
                    : $"[Untitled {Type}]";
}

/// <summary>
/// Projected row for an item in a Trakt watchlist.
/// Retains all raw payload fields, rank, notes, and provides fallback visible display title.
/// </summary>
public sealed record TraktProjectedWatchlistItem(
    long Id,
    int Rank,
    DateTimeOffset? ListedAt,
    string Type,
    string? Notes,
    long? MediaTraktId,
    string? Title,
    int? Year,
    JsonElement RawElement,
    TraktIdentity? RemoteIdentity,
    TraktMatchStatus MatchStatus,
    TraktMediaKind? MatchedLocalKind,
    int? MatchedLocalId,
    TraktAvailability Availability,
    bool IsWatched,
    int WatchedEpisodeCount,
    int? TotalEpisodeCount,
    int PlayCount,
    DateTimeOffset? LastWatchedAt)
{
    public string DisplayTitle =>
        !string.IsNullOrWhiteSpace(Title)
            ? Title
            : MediaTraktId is > 0
                ? $"[{Type} #{MediaTraktId}]"
                : Id > 0
                    ? $"[{Type} #{Id}]"
                    : $"[Untitled {Type}]";
}

/// <summary>
/// Projected row for a playback history event.
/// Preserves the unique 64-bit history identifier, chronologic order, and repeated events.
/// </summary>
public sealed record TraktProjectedHistoryItem(
    long HistoryId,
    DateTimeOffset? WatchedAt,
    string Action,
    string Type,
    long? MediaTraktId,
    string? Title,
    int? SeasonNumber,
    int? EpisodeNumber,
    JsonElement RawElement,
    TraktIdentity? RemoteIdentity,
    TraktMatchStatus MatchStatus,
    TraktMediaKind? MatchedLocalKind,
    int? MatchedLocalId,
    TraktAvailability Availability,
    bool IsWatched = true)
{
    public string DisplayTitle =>
        !string.IsNullOrWhiteSpace(Title)
            ? Title
            : Type == "episode" && SeasonNumber is not null && EpisodeNumber is not null
                ? $"[Episode S{SeasonNumber:D2}E{EpisodeNumber:D2}]"
                : MediaTraktId is > 0
                    ? $"[{Type} #{MediaTraktId}]"
                    : $"[{Type} #{HistoryId}]";
}

/// <summary>
/// Projected row for a watched movie record.
/// Retains raw payload, plays, timestamps, and matches local catalog item for navigation.
/// </summary>
public sealed record TraktProjectedWatchedMovie(
    int Plays,
    DateTimeOffset? LastWatchedAt,
    DateTimeOffset? LastUpdatedAt,
    long? TraktId,
    string? ImdbId,
    long? TmdbId,
    string? Title,
    int? Year,
    JsonElement RawElement,
    TraktIdentity RemoteIdentity,
    TraktMatchStatus MatchStatus,
    TraktMediaKind? MatchedLocalKind,
    int? MatchedLocalId,
    TraktAvailability Availability,
    bool IsWatched = true)
{
    public string DisplayTitle =>
        !string.IsNullOrWhiteSpace(Title)
            ? Title
            : TraktId is > 0
                ? $"[Movie #{TraktId}]"
                : TmdbId is > 0
                    ? $"[Movie TMDB#{TmdbId}]"
                    : "[Untitled Movie]";
}

/// <summary>
/// Projected row for a watched TV show record with season and episode hierarchy.
/// </summary>
public sealed record TraktProjectedWatchedShow(
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
    int WatchedEpisodeCount,
    int? TotalEpisodeCount,
    bool IsWatched,
    IReadOnlyList<TraktProjectedWatchedSeason> Seasons,
    JsonElement RawElement,
    TraktIdentity RemoteIdentity,
    TraktMatchStatus MatchStatus,
    TraktMediaKind? MatchedLocalKind,
    int? MatchedLocalId,
    TraktAvailability Availability)
{
    public string DisplayTitle =>
        !string.IsNullOrWhiteSpace(Title)
            ? Title
            : TraktId is > 0
                ? $"[Show #{TraktId}]"
                : TmdbId is > 0
                    ? $"[Show TMDB#{TmdbId}]"
                    : "[Untitled Show]";
}

/// <summary>
/// Projected season within a watched show record.
/// </summary>
public sealed record TraktProjectedWatchedSeason(
    int Number,
    IReadOnlyList<TraktProjectedWatchedEpisode> Episodes,
    TraktMatchStatus MatchStatus,
    int? MatchedLocalId,
    TraktAvailability Availability);

/// <summary>
/// Projected episode within a watched show record.
/// </summary>
public sealed record TraktProjectedWatchedEpisode(
    int Number,
    int Plays,
    DateTimeOffset? LastWatchedAt,
    TraktMatchStatus MatchStatus,
    int? MatchedLocalId,
    TraktAvailability Availability);
