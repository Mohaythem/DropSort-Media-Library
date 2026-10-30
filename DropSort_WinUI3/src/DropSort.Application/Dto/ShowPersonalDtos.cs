namespace DropSort.Application.Dto;

/// <summary>Persisted personal flags for one TV show.</summary>
public record ShowPersonalSnapshot(
    int ShowId,
    bool IsFavorite,
    bool WatchLater);
