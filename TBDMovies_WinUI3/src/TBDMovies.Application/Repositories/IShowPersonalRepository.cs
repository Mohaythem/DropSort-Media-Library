using System;
using System.Collections.Generic;
using TBDMovies.Application.Dto;
using TBDMovies.Domain.Library.Personal;

namespace TBDMovies.Application.Repositories;

public interface IShowPersonalRepository
{
    ShowPersonalSnapshot GetState(int showId);
    ShowPersonalSnapshot SetFavorite(int showId, bool value, DateTimeOffset now);
    ShowPersonalSnapshot SetWatchLater(int showId, bool value, DateTimeOffset now);
    IReadOnlySet<int> ListShowIds(PersonalLibrarySection section);
}
