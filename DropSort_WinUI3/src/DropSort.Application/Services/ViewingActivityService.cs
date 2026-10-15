using System;
using System.Collections.Generic;
using System.Linq;
using DropSort.Application.Dto;
using DropSort.Application.Repositories;
using DropSort.Domain.Library.Personal;

namespace DropSort.Application.Services;

public sealed class ViewingActivityService
{
    private readonly IViewingActivityRepository _repository;
    private readonly Func<DateTimeOffset> _clock;

    public ViewingActivityService(IViewingActivityRepository repository, Func<DateTimeOffset>? clock = null)
    {
        _repository = repository;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public IReadOnlyList<ActiveViewingItem> ListActive(int limit = 12) => _repository.ListActive(limit);

    public ActiveViewingItem RecordSuccessfulPlay(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A media path is required.", nameof(path));
        return _repository.RecordSuccessfulPlay(path, _clock().ToUniversalTime());
    }

    public IReadOnlyList<WatchHistoryItem> ListRecentCompleted(int limit = 8, int offset = 0) =>
        _repository.ListRecentCompleted(limit, offset).Select(Map).ToList();

    private static WatchHistoryItem Map(WatchHistoryEntry entry) => new(
        entry.Id, entry.MediaKind, entry.MovieId, entry.ShowId, entry.EpisodeId,
        entry.TitleSnapshot, entry.YearSnapshot, entry.ShowTitleSnapshot,
        entry.SeasonNumberSnapshot, entry.EpisodeNumberSnapshot, entry.ArtworkReference,
        entry.BackdropReference, entry.PosterReference, entry.WatchedAt, entry.IsAvailable);
}
