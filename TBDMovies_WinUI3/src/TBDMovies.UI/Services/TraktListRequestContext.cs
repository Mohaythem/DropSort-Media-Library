namespace TBDMovies.UI.Services;

/// <summary>A remote dialog/action remains valid only for the page/session it started in.</summary>
internal sealed record TraktListRequestContext(string Key, string Account, object Coordinator, long Generation)
{
    public bool IsCurrent(bool active, string key, string? account, object? coordinator, long generation, bool cancelled) =>
        active && !cancelled && Generation == generation &&
        string.Equals(Key, key, System.StringComparison.Ordinal) &&
        string.Equals(Account, account, System.StringComparison.Ordinal) &&
        object.ReferenceEquals(Coordinator, coordinator);
}
