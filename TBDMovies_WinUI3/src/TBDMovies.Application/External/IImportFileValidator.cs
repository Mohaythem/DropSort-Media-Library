using System.Threading;
using TBDMovies.Domain.Media.Discovery;

namespace TBDMovies.Application.External;

public interface IImportFileValidator
{
    void Validate(DiscoveredMedia item, CancellationToken cancellationToken = default);
}
