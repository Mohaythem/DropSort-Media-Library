using System;
using DropSort.Domain.Core.Operations;
using DropSort.Domain.Core.Safety;
using DropSort.Domain.Library.Availability;
using DropSort.Domain.Media.Discovery;
using DropSort.Domain.Metadata.Contracts;

namespace DropSort.Application.External;

public interface IMetadataProvider
{
    string ProviderName { get; }
    IReadOnlyList<MovieCandidate> Search(MovieSearchQuery query);
    MovieMetadata? GetMovie(string externalId);
}

public record PosterRequest(string Provider, string Reference);

public record PosterAsset(string ImageFormat, byte[] Content);

public interface IPosterService
{
    PosterAsset? LoadPoster(PosterRequest request);
}

public interface IPosterCacheMaintenance
{
    int Clear();
}

public interface IFileOperationCoordinator
{
    (FileOperationPlan Plan, SourceIdentity SourceIdentity) Preview(
        OperationType operationType,
        string source,
        string destination,
        IReadOnlyList<string> approvedRoots,
        int? mediaFileId = null,
        string? reversesOperationId = null);

    (FileOperationPlan Plan, SourceIdentity SourceIdentity) Plan(
        OperationType operationType,
        string source,
        string destination,
        IReadOnlyList<string> approvedRoots,
        int? mediaFileId = null,
        string? reversesOperationId = null,
        SourceIdentity? expectedSourceIdentity = null);

    FileOperationRecord Execute(string operationId, IReadOnlyList<string> approvedRoots);
    RecoveryInspection InspectRecovery(string operationId, IReadOnlyList<string> approvedRoots);
    FileOperationRecord Recover(string operationId, IReadOnlyList<string> approvedRoots);
}

public interface IAvailabilityInspector
{
    AvailabilityInspection Inspect(string path);
}

public interface IMediaDiscoveryService
{
    IReadOnlyList<DiscoveredMedia> Discover(
        string rootPath,
        bool recursive,
        Action<int, int, string?>? progress = null,
        Func<bool>? isCancelled = null);
}
