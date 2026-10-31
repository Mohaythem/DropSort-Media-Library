namespace TBDMovies.Application.External;

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
    StaleRequest,

    /// <summary>The chosen provider identity is already owned by a different local Show/Movie. The match is
    /// refused before any write so neither record is left half-updated; the user resolves the conflict.</summary>
    IdentityConflict
}

/// <summary>Never carries credentials, request URLs, response bodies, or transport exceptions.</summary>
public sealed class MetadataServiceException(MetadataFailureKind kind)
    : Exception($"Metadata request failed ({kind}).")
{
    public MetadataFailureKind Kind { get; } = kind;
}
