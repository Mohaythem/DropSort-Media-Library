using System.Threading;
using System.Threading.Tasks;

namespace TBDMovies.Application.Trakt;

/// <summary>
/// Low-level HTTP client contract for Trakt OAuth endpoints (https://auth.trakt.tv).
/// </summary>
public interface ITraktAuthClient
{
    Task<TraktDeviceCode> GenerateDeviceCodeAsync(string clientId, CancellationToken cancellationToken = default);

    Task<TraktPollResponse> PollDeviceTokenAsync(string deviceCode, string clientId, CancellationToken cancellationToken = default);

    Task<TraktTokenPair> RefreshTokenAsync(string clientId, string redirectUri, string refreshToken, CancellationToken cancellationToken = default);
}
