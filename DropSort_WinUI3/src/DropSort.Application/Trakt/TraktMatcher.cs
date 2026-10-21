namespace DropSort.Application.Trakt;

public enum TraktMatchStatus
{
    NoMatch,
    Matched,
    Ambiguous
}

public sealed record TraktMatchResult(TraktMatchStatus Status, TraktLocalCandidate? Candidate)
{
    public static TraktMatchResult NoMatch { get; } = new(TraktMatchStatus.NoMatch, null);
    public static TraktMatchResult Ambiguous { get; } = new(TraktMatchStatus.Ambiguous, null);
}

/// <summary>Pure identity matching. It does not read files, databases, or remote services.</summary>
public static class TraktMatcher
{
    public static TraktMatchResult Match(TraktIdentity remote, IEnumerable<TraktLocalCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(remote);
        ArgumentNullException.ThrowIfNull(candidates);

        if (!remote.IsValid)
            return TraktMatchResult.NoMatch;

        var sameKind = candidates.Where(candidate => candidate.LocalId > 0 &&
            candidate.Kind == remote.Kind && candidate.Identity.Kind == remote.Kind && candidate.Identity.IsValid).Distinct().ToArray();
        var direct = sameKind.Where(candidate => IsDirectMatch(remote, candidate.Identity)).ToArray();
        if (direct.Length == 1)
            return new(TraktMatchStatus.Matched, direct[0]);
        if (direct.Length > 1)
            return TraktMatchResult.Ambiguous;

        var parent = sameKind.Where(candidate => IsCoordinateMatch(remote, candidate.Identity)).ToArray();
        return parent.Length switch
        {
            1 => new(TraktMatchStatus.Matched, parent[0]),
            > 1 => TraktMatchResult.Ambiguous,
            _ => TraktMatchResult.NoMatch
        };
    }

    private static bool IsDirectMatch(TraktIdentity remote, TraktIdentity local)
    {
        if (!remote.HasDirectId || local.Kind != remote.Kind || local.DirectIdsConflict(remote) ||
            local.ParentIdsConflict(remote) || local.CoordinatesConflict(remote))
            return false;

        return remote.SharesDirectId(local);
    }

    private static bool IsCoordinateMatch(TraktIdentity remote, TraktIdentity local)
    {
        if (remote.Kind is not (TraktMediaKind.Season or TraktMediaKind.Episode) ||
            !remote.HasParentId ||
            !remote.SharesParentId(local) ||
            remote.ParentIdsConflict(local) ||
            remote.DirectIdsConflict(local) ||
            remote.CoordinatesConflict(local))
            return false;

        if (remote.Kind == TraktMediaKind.Season)
            return remote.SeasonNumber is not null && remote.SeasonNumber == local.SeasonNumber;

        return remote.SeasonNumber is not null && remote.EpisodeNumber is not null &&
               remote.SeasonNumber == local.SeasonNumber && remote.EpisodeNumber == local.EpisodeNumber;
    }
}
