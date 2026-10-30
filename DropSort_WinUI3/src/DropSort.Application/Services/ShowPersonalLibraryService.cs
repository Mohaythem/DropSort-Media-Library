using System;
using System.Collections.Generic;
using System.Linq;
using DropSort.Application.Contracts;
using DropSort.Application.Dto;
using DropSort.Application.Repositories;
using DropSort.Domain.Library.Personal;

namespace DropSort.Application.Services;

public sealed class ShowPersonalLibraryService
{
    private readonly IShowPersonalRepository _repository;
    private readonly ITvLibraryUiActions _tv;
    private readonly Func<DateTimeOffset> _clock;

    public ShowPersonalLibraryService(
        IShowPersonalRepository repository,
        ITvLibraryUiActions tv,
        Func<DateTimeOffset>? clock = null)
    {
        _repository = repository;
        _tv = tv;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public ShowPersonalSnapshot GetState(int showId) => _repository.GetState(showId);

    public ShowPersonalSnapshot SetFavorite(int showId, bool value) =>
        _repository.SetFavorite(showId, value, _clock());

    public ShowPersonalSnapshot SetWatchLater(int showId, bool value) =>
        _repository.SetWatchLater(showId, value, _clock());

    public IReadOnlyList<TvShowListItem> ListShows(PersonalLibrarySection section)
    {
        ValidateSection(section);
        var ids = _repository.ListShowIds(section);
        return _tv.ListShows().Where(show => ids.Contains(show.Id)).ToList();
    }

    private static void ValidateSection(PersonalLibrarySection section)
    {
        if (section is not (PersonalLibrarySection.Watchlist or PersonalLibrarySection.Liked))
            throw new ArgumentOutOfRangeException(nameof(section));
    }
}
