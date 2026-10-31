using System.Threading;
using System.Threading.Tasks;

namespace TBDMovies.Application.Trakt;

/// <summary>
/// High-level orchestration contract for Trakt device authentication, token refreshing,
/// credential persistence, and connection state.
/// </summary>
public interface ITraktAuthService
{
    Task<TraktDeviceCode> BeginDeviceAuthAsync(CancellationToken cancellationToken = default);

    Task<TraktAuthState> CompleteDeviceAuthAsync(TraktDeviceCode deviceCode, CancellationToken cancellationToken = default);

    Task<string> GetAccessTokenAsync(bool forceRefresh = false, string? rejectedAccessToken = null, CancellationToken cancellationToken = default);

    Task DisconnectAsync(CancellationToken cancellationToken = default);

    Task RejectAccessTokenAsync(string rejectedAccessToken, CancellationToken cancellationToken = default);

    TraktAuthState GetAuthState();

    Task<bool> RecordVerifiedAccountAsync(string accountId, string expectedAccessToken, CancellationToken cancellationToken = default);
}
