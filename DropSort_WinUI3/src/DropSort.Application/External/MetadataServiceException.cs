namespace DropSort.Application.External;

/// <summary>Safe error categories shared by metadata adapters and presentation.</summary>
public enum MetadataFailureKind
{
    NotConfigured,
    Authentication,
    RateLimited,
    Network,
    Timeout,
    Api,
    InvalidResponse,
    NotFound,
    IncompleteMetadata,
    StaleRequest
}

/// <summary>Never carries credentials, request URLs, response bodies, or transport exceptions.</summary>
public sealed class MetadataServiceException(MetadataFailureKind kind)
    : Exception($"Metadata request failed ({kind}).")
{
    public MetadataFailureKind Kind { get; } = kind;
}
