using System;
using TBDMovies.Domain.Core.Operations;
using TBDMovies.Domain.Core.Safety;
using TBDMovies.Domain.Library.Availability;
using TBDMovies.Domain.Media.Discovery;
using TBDMovies.Domain.Metadata.Contracts;

namespace TBDMovies.Application.External;

public record ConnectionTestResult(bool Success, string Message, int? StatusCode = null,
    MetadataFailureKind? FailureKind = null);

public interface IMetadataProvider
{
    string ProviderName { get; }
    bool IsConfigured => false;
    Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new ConnectionTestResult(false, "Connection test not supported."));
    IReadOnlyList<MovieCandidate> Search(MovieSearchQuery query);
    Task<IReadOnlyList<MovieCandidate>> SearchMoviesAsync(MovieSearchQuery query, CancellationToken cancellationToken = default) =>
        Task.FromResult(Search(query));
    MovieMetadata? GetMovie(string externalId);
    Task<MovieMetadata?> GetMovieAsync(string externalId, CancellationToken cancellationToken = default) =>
        Task.FromResult(GetMovie(externalId));
    Task<IReadOnlyList<TvCandidate>> SearchTvAsync(TvSearchQuery query, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TvCandidate>>([]);
    Task<TvShowMetadata?> GetTvShowAsync(string externalId, CancellationToken cancellationToken = default) =>
        Task.FromResult<TvShowMetadata?>(null);
    Task<TvSeasonMetadata?> GetTvSeasonAsync(string showExternalId, int seasonNumber, CancellationToken cancellationToken = default) =>
        Task.FromResult<TvSeasonMetadata?>(null);
}

public record PosterRequest(string Provider, string Reference);

public record PosterAsset(string ImageFormat, byte[] Content);

public interface IPosterService
{
    PosterAsset? LoadPoster(PosterRequest request);
    Task<string?> EnsurePosterCachedAsync(string provider, string reference, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);
    string? GetCachedPosterPath(string provider, string reference) => null;
}

public interface IPosterCacheMaintenance
{
    int Clear();
    long GetCacheSizeBytes() => 0;
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
    IReadOnlyList<StaleTempFileInfo> DetectStaleTempFiles(IEnumerable<string> directoryRoots);
}

public interface IAvailabilityInspector
{
    AvailabilityInspection Inspect(string path);

    /// <summary>
    /// Reports whether the storage root that holds <paramref name="path" /> is currently reachable and
    /// ready (for example the drive is connected and mounted). It does not check the file itself.
    /// Reconciliation calls this once per distinct root so it does not probe the drive for every file.
    /// It fails closed: if availability cannot be proven, it returns false, so TBDMovies never treats a
    /// file on an unprovable root as deleted.
    /// </summary>
    bool IsStorageAvailable(string path);
}

public interface IMediaDiscoveryService
{
    IReadOnlyList<DiscoveredMedia> Discover(
        string rootPath,
        bool recursive,
        Action<int, int, string?>? progress = null,
        Func<bool>? isCancelled = null);
}
