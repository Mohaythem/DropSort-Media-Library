using System;
using System.Collections.Generic;
using System.Linq;
using DropSort.Application.Contracts;
using DropSort.Application.Dto;
using DropSort.Application.Repositories;
using DropSort.Domain.Library.Personal;

namespace DropSort.Application.Services;

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
