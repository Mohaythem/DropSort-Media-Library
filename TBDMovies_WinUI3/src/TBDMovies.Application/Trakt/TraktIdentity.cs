namespace TBDMovies.Application.Trakt;

public enum TraktMediaKind
{
    Movie,
    Show,
    Season,
    Episode
}

public enum TraktAvailability
{
    Unknown,
    Available,
    Missing,
    Partial
}

/// <summary>Strong remote identifiers. Values are provider identifiers, not local catalog keys.</summary>
public sealed record TraktIdentity(
    TraktMediaKind Kind,
    long? TraktId = null,
    int? TmdbId = null,
    string? ImdbId = null,
    int? TvdbId = null,
    long? ShowTraktId = null,
    int? ShowTmdbId = null,
    string? ShowImdbId = null,
    int? ShowTvdbId = null,
    int? SeasonNumber = null,
    int? EpisodeNumber = null)
{
    public bool HasDirectId => TraktId is not null || TmdbId is not null || !string.IsNullOrWhiteSpace(ImdbId) || TvdbId is not null;

    public bool HasParentId => ShowTraktId is not null || ShowTmdbId is not null || !string.IsNullOrWhiteSpace(ShowImdbId) || ShowTvdbId is not null;

    public bool HasCoordinates => SeasonNumber is not null || EpisodeNumber is not null;

    public bool IsValid =>
        Enum.IsDefined(Kind) &&
        ValidImdb(ImdbId) && ValidImdb(ShowImdbId) &&
        (Kind is not (TraktMediaKind.Movie or TraktMediaKind.Show) || !HasCoordinates) &&
        (TraktId is null or > 0) && (TmdbId is null or > 0) && (TvdbId is null or > 0) &&
        (ShowTraktId is null or > 0) && (ShowTmdbId is null or > 0) && (ShowTvdbId is null or > 0) &&
        (SeasonNumber is null or >= 0) && (EpisodeNumber is null or > 0) &&
        (Kind != TraktMediaKind.Episode || (SeasonNumber is not null && EpisodeNumber is not null)) &&
        (Kind != TraktMediaKind.Season || SeasonNumber is not null);

    public bool CoordinatesConflict(TraktIdentity other) =>
        (SeasonNumber.HasValue && other.SeasonNumber.HasValue && SeasonNumber != other.SeasonNumber) ||
        (EpisodeNumber.HasValue && other.EpisodeNumber.HasValue && EpisodeNumber != other.EpisodeNumber);

    public bool DirectIdsConflict(TraktIdentity other) =>
        Different(TraktId, other.TraktId) ||
        Different(TmdbId, other.TmdbId) ||
        Different(ImdbId, other.ImdbId) ||
        Different(TvdbId, other.TvdbId);

    public bool ParentIdsConflict(TraktIdentity other) =>
        Different(ShowTraktId, other.ShowTraktId) ||
        Different(ShowTmdbId, other.ShowTmdbId) ||
        Different(ShowImdbId, other.ShowImdbId) ||
        Different(ShowTvdbId, other.ShowTvdbId);

    public bool SharesDirectId(TraktIdentity other) =>
        Same(TraktId, other.TraktId) ||
        Same(TmdbId, other.TmdbId) ||
        Same(ImdbId, other.ImdbId) ||
        Same(TvdbId, other.TvdbId);

    public bool SharesParentId(TraktIdentity other) =>
        Same(ShowTraktId, other.ShowTraktId) ||
        Same(ShowTmdbId, other.ShowTmdbId) ||
        Same(ShowImdbId, other.ShowImdbId) ||
        Same(ShowTvdbId, other.ShowTvdbId);

    private static bool Same<T>(T? left, T? right) where T : struct => left.HasValue && right.HasValue && left.Value.Equals(right.Value);
    private static bool Different<T>(T? left, T? right) where T : struct => left.HasValue && right.HasValue && !left.Value.Equals(right.Value);
    private static bool ValidImdb(string? value)
    {
        if (value is null) return true;
        var id = value.Trim();
        return id.Length >= 9 && id.StartsWith("tt", StringComparison.OrdinalIgnoreCase) &&
            id.AsSpan(2).IndexOfAnyExceptInRange('0', '9') < 0 && id.AsSpan(2).ContainsAnyInRange('1', '9');
    }

    private static bool Same(string? left, string? right) => !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right) && string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
    private static bool Different(string? left, string? right) => !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right) && !string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
}

public sealed record TraktLocalCandidate(
    int LocalId,
    TraktMediaKind Kind,
    TraktIdentity Identity,
    TraktAvailability Availability = TraktAvailability.Unknown);
