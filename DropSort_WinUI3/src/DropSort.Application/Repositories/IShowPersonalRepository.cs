using System;
using System.Collections.Generic;
using DropSort.Application.Dto;
using DropSort.Domain.Library.Personal;

namespace DropSort.Application.Repositories;

public interface IShowPersonalRepository
{
    ShowPersonalSnapshot GetState(int showId);
    ShowPersonalSnapshot SetFavorite(int showId, bool value, DateTimeOffset now);
    ShowPersonalSnapshot SetWatchLater(int showId, bool value, DateTimeOffset now);
    IReadOnlySet<int> ListShowIds(PersonalLibrarySection section);
}
