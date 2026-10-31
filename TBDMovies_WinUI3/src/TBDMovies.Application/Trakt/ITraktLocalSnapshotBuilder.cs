using System.Threading;
using System.Threading.Tasks;

namespace TBDMovies.Application.Trakt;

/// <summary>
/// Builds an immutable snapshot of local library candidates and their file availability
/// off the UI thread via read-only repository calls.
/// </summary>
public interface ITraktLocalSnapshotBuilder
{
    /// <summary>
    /// Constructs a local candidate snapshot for matching against remote Trakt resources.
    /// Runs on a background thread without acquiring or holding a database write unit-of-work.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to abort snapshot creation.</param>
    /// <returns>An immutable snapshot containing all valid local candidates and their availability.</returns>
    Task<TraktLocalSnapshot> BuildSnapshotAsync(CancellationToken cancellationToken = default);
}
