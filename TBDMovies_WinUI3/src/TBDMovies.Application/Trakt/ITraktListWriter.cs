using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TBDMovies.Application.Trakt;

public interface ITraktListWriter
{
    Task<JsonElement> AddMovieToListAsync(long? listId, long tmdbId, string clientId, string accessToken, CancellationToken cancellationToken = default);
}
