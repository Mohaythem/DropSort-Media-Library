using System;
using System.Collections.Generic;
using System.Linq;
using TBDMovies.Application.Contracts;
using TBDMovies.Application.Dto;
using TBDMovies.Application.Repositories;
using TBDMovies.Domain.Library.Personal;

namespace TBDMovies.Application.Services;

public sealed class WatchHistoryService : IWatchHistoryUiActions
{
    private readonly IWatchHistoryRepository _history;
    private readonly Func<DateTimeOffset> _clock;

    public WatchHistoryService(IWatchHistoryRepository history, Func<DateTimeOffset>? clock = null)
    {
        _history = history;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public IReadOnlyList<WatchHistoryItem> ListRecentWatchHistory(int limit = 50, int offset = 0) =>
        _history.ListRecentlyWatchedMedia(limit, offset).Select(Map).ToList();

    public WatchHistoryItem RecordEpisodeWatch(int episodeId, DateTimeOffset? watchedAt = null) =>
        Map(_history.AddEpisodeWatchEvent(episodeId, watchedAt ?? _clock(), _clock()));

    public int RemoveEpisodeWatch(int episodeId) => _history.DeleteEpisodeWatchEvents(episodeId);

    public int ClearWatchHistory() => _history.ClearWatchHistory();

    private static WatchHistoryItem Map(WatchHistoryEntry entry) => new(
        entry.Id,
        entry.MediaKind,
        entry.MovieId,
        entry.ShowId,
        entry.EpisodeId,
        entry.TitleSnapshot,
        entry.YearSnapshot,
        entry.ShowTitleSnapshot,
        entry.SeasonNumberSnapshot,
        entry.EpisodeNumberSnapshot,
        entry.ArtworkReference,
        entry.BackdropReference,
        entry.PosterReference,
        entry.WatchedAt,
        entry.IsAvailable);
}
