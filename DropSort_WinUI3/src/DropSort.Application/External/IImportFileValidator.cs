using System.Threading;
using DropSort.Domain.Media.Discovery;

namespace DropSort.Application.External;

public interface IImportFileValidator
{
    void Validate(DiscoveredMedia item, CancellationToken cancellationToken = default);
}
