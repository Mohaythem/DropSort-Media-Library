using System.Threading;
using System.Threading.Tasks;

namespace DropSort.Application.Trakt;

public interface ITraktListActions
{
    Task<TraktListRefreshResult> RefreshListAsync(long? listId, string expectedAccountId, CancellationToken cancellationToken = default);

    Task<TraktListWriteResult> AddMovieToListAsync(long? listId, long tmdbId, string expectedAccountId, CancellationToken cancellationToken = default);
}

public enum TraktListWriteStatus
{
    Added,
    AlreadyExists,
    NotFound,
    Failed,
    Cancelled
}

public sealed record TraktListRefreshResult(bool Success, bool CacheRefreshSucceeded, string? Error = null);

public sealed record TraktListWriteResult(
    TraktListWriteStatus Status,
    bool CacheRefreshSucceeded,
    string? Error = null)
{
    public bool IsConfirmed => Status is TraktListWriteStatus.Added or TraktListWriteStatus.AlreadyExists;
}
